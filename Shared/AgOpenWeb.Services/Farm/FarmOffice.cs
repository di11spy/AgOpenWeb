// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using AgOpenWeb.Models;
using AgOpenWeb.Models.Farm;
using AgOpenWeb.Services.GeoJson;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// The farm office: one book, a view the pages render, and actions that never
/// steer, never set a rate, and never write a command PGN.
/// </summary>
public static class FarmOffice
{
    private static readonly object Gate = new();
    private static FarmStore? _store;
    private static Func<LiveFix?>? _live;

    public sealed class LiveFix
    {
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public double HeadingDegrees { get; init; }
        public double HostSpeed { get; init; }
        public double? Pgn250SensorPercent { get; init; }
        public double? Pgn253SteerAngleDeg { get; init; }
        public double? Pgn211RollDegrees { get; init; }
    }

    public static void UseStore(FarmStore store) { lock (Gate) _store = store; }

    public static void UseLiveFix(Func<LiveFix?>? reader) { lock (Gate) _live = reader; }

    public static FarmStore Store
    {
        get
        {
            lock (Gate)
            {
                _store ??= FarmStore.Open();
                return _store;
            }
        }
    }

    public static object View()
    {
        var store = Store;
        LiveFix? fix = null;
        var reader = _live;
        if (reader != null)
        {
            try { fix = reader(); }
            catch { fix = null; }
        }
        return store.Read(book =>
        {
            var utc = DateTime.UtcNow;
            Advance(book, utc);
            if (fix != null && Math.Abs(fix.Latitude) > 0.01 && Math.Abs(fix.Longitude) > 0.01)
                NoteHost(book, fix, utc);
            return Build(book, utc);
        });
    }

    public static (int Status, object Body) Act(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException) { return (400, new { ok = false, error = "The body is not JSON." }); }
        using (doc)
        {
            var root = doc.RootElement;
            string op = Str(root, "op");
            string? refusal = op is "steer" or "rate" or "section" or "command" or "gps-offset" or "cart"
                ? MachineRolePolicy.RefuseInbound(null, op + "-command")
                : MachineRolePolicy.RefuseInbound(Int(root, "pgn"), Str(root, "kind"));
            if (refusal != null)
            {
                Store.Update(book => PushRefusal(book, refusal));
                return (403, new { ok = false, error = refusal });
            }
            try
            {
                string? error = op switch
                {
                    "send-message" => Send(root, ptt: false),
                    "ptt" => Send(root, ptt: true),
                    "set-link" => SetLink(root),
                    "sync" => Sync(root),
                    "queue-record" => Queue(root),
                    "ingest-observation" => Ingest(root),
                    "import-upload" => ImportUpload(root),
                    "import-sample" => ImportSample(root),
                    "set-crop" => SetCrop(root),
                    "reset-fixture" => Reset(),
                    _ => "Unknown action.",
                };
                if (error != null) return (400, new { ok = false, error });
                return (200, new { ok = true, view = View() });
            }
            catch (Exception ex)
            {
                return (400, new { ok = false, error = ex.Message });
            }
        }
    }

    private static string? Send(JsonElement root, bool ptt)
    {
        string channel = Str(root, "channelId");
        string from = Str(root, "from");
        string text = Str(root, "text");
        if (channel.Length == 0 || from.Length == 0) return "A channel and a sender are required.";
        string? audio = root.TryGetProperty("audioBase64", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (audio != null && audio.Length > 100_000) return "The push-to-talk clip is too long for this office (about 100 KB of text).";
        Store.Update(book =>
        {
            if (book.Comms.Channels.All(c => c.Id != channel)) return;
            book.Comms.Posts.Add(new CommPost
            {
                Id = Guid.NewGuid().ToString("N"),
                ChannelId = channel,
                From = from,
                Kind = ptt ? "ptt" : "text",
                Text = text.Length == 0 && ptt ? "Push-to-talk" : text,
                AudioBase64 = string.IsNullOrEmpty(audio) ? null : audio,
                AudioType = Str(root, "audioType"),
                Utc = DateTime.UtcNow,
            });
            if (book.Comms.Posts.Count > 200)
                book.Comms.Posts.RemoveRange(0, book.Comms.Posts.Count - 200);
        });
        return null;
    }

    private static string? SetLink(JsonElement root)
    {
        var machine = Find(Str(root, "machineId"));
        if (machine == null) return "Unknown machine.";
        string link = Str(root, "link");
        if (link is not ("online" or "offline")) return "Link is online or offline.";
        Store.Update(book =>
        {
            var node = book.Machines.First(m => m.Id == machine);
            node.Link = link;
            if (link == "offline") node.SpeedMps = 0;
            else node.LastSeenUtc = DateTime.UtcNow;
        });
        return null;
    }

    private static string? Sync(JsonElement root)
    {
        var id = Find(Str(root, "machineId"));
        if (id == null) return "Unknown machine.";
        string? error = null;
        Store.Update(book =>
        {
            var node = book.Machines.First(m => m.Id == id);
            var state = LinkState(node, DateTime.UtcNow);
            if (state == "offline")
            {
                error = "Offline. The queued records stay on the machine until it is online.";
                return;
            }
            node.PendingRecords = 0;
            node.LastSyncUtc = DateTime.UtcNow;
            node.LastSeenUtc = DateTime.UtcNow;
        });
        return error;
    }

    private static string? Queue(JsonElement root)
    {
        var id = Find(Str(root, "machineId"));
        if (id == null) return "Unknown machine.";
        Store.Update(book =>
        {
            var node = book.Machines.First(m => m.Id == id);
            node.PendingRecords++;
        });
        return null;
    }

    private static string? Ingest(JsonElement root)
    {
        Store.Update(book =>
        {
            book.Telemetry.Add(new Observation
            {
                Utc = DateTime.UtcNow,
                MachineId = Str(root, "machineId").Length == 0 ? "agopen-host" : Str(root, "machineId"),
                Source = Str(root, "source").Length == 0 ? "posted-observation" : Str(root, "source"),
                Latitude = Dbl(root, "latitude"),
                Longitude = Dbl(root, "longitude"),
                HeadingDegrees = Dbl(root, "headingDegrees"),
                HostSpeed = Dbl(root, "hostSpeed"),
                Pgn250SensorPercent = Dbl(root, "pgn250SensorPercent"),
                Pgn253SteerAngleDeg = Dbl(root, "pgn253SteerAngleDeg"),
                Pgn211RollDegrees = Dbl(root, "pgn211RollDegrees"),
                Note = "Read-only. Not forwarded to a module.",
            });
            Trim(book);
        });
        return null;
    }

    private static string? ImportUpload(JsonElement root)
    {
        string name = Str(root, "fileName");
        string label = Str(root, "label");
        if (label.Length == 0) label = "office upload";
        if (!root.TryGetProperty("base64", out var raw) || raw.ValueKind != JsonValueKind.String)
            return "The upload needs base64 file bytes.";
        byte[] bytes;
        try { bytes = Convert.FromBase64String(raw.GetString() ?? ""); }
        catch (FormatException) { return "The upload is not base64."; }
        if (bytes.Length == 0) return "The upload is empty.";
        string incoming = Path.Combine(Store.Root, "incoming");
        Directory.CreateDirectory(incoming);
        string safe = Path.GetFileName(name);
        if (string.IsNullOrEmpty(safe)) safe = "upload.bin";
        string path = Path.Combine(incoming, DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + safe);
        File.WriteAllBytes(path, bytes);
        return ImportPath(path, label, fieldIndex: 0);
    }

    private static string? ImportSample(JsonElement root)
    {
        string kind = Str(root, "kind");
        string label = Str(root, "label");
        if (label.Length == 0) label = "bundled sample";
        string samples = Path.Combine(Store.Root, "samples");
        FarmFixture.WriteSamples(samples);
        string path = kind == "kml"
            ? Path.Combine(samples, "sample-field.kml")
            : Path.Combine(samples, "TASKDATA.XML");
        return ImportPath(path, label, 0);
    }

    private static string? ImportPath(string path, string label, int fieldIndex)
    {
        bool kml = path.EndsWith(".kml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase);
        string stem = Path.GetFileNameWithoutExtension(path);
        if (stem.Equals("TASKDATA", StringComparison.OrdinalIgnoreCase)) stem = "ISOXML import";
        var result = kml
            ? FarmGeometryImporter.ImportKml(path, Store.FieldsDirectory, stem, label)
            : FarmGeometryImporter.ImportIsoXml(path, Store.FieldsDirectory, stem, label, fieldIndex);
        if (!result.Ok) return result.Error;
        Store.Update(book =>
        {
            var plot = PlotFromFolder(result);
            book.Fields.Add(plot);
        });
        return null;
    }

    private static FieldPlot PlotFromFolder(FarmGeometryImporter.Result result)
    {
        var (field, _) = GeoJsonFieldService.Load(result.FieldDirectory);
        var plane = new LocalPlane(new Wgs84(field.Origin.Latitude, field.Origin.Longitude), new SharedFieldProperties());
        var ring = new List<double[]>();
        if (field.Boundary?.OuterBoundary != null)
        {
            foreach (var pt in field.Boundary.OuterBoundary.Points)
            {
                var w = plane.ConvertGeoCoordToWgs84(new GeoCoord(pt.Northing, pt.Easting));
                ring.Add(new[] { w.Longitude, w.Latitude });
            }
        }
        var (lon, lat) = FarmAnalysis.Centroid(ring);
        return new FieldPlot
        {
            Id = "import-" + Guid.NewGuid().ToString("N")[..8],
            Name = result.FieldName,
            CropYear = "",
            Crop = "",
            Provenance = "imported",
            GuidanceFolder = result.FieldDirectory,
            ProvenanceFile = result.ProvenancePath,
            OriginLonLat = new[] { lon, lat },
            Boundary = ring,
            Hectares = FarmAnalysis.AreaM2(ring) / 10000.0,
        };
    }

    private static string? SetCrop(JsonElement root)
    {
        string id = Str(root, "fieldId");
        string year = Str(root, "cropYear");
        string crop = Str(root, "crop");
        bool found = false;
        Store.Update(book =>
        {
            var field = book.Fields.FirstOrDefault(f => f.Id == id);
            if (field == null) return;
            found = true;
            if (year.Length > 0) field.CropYear = year;
            if (crop.Length > 0) field.Crop = crop;
            if (!string.IsNullOrEmpty(field.GuidanceFolder))
            {
                var record = new
                {
                    schemaVersion = 1,
                    fieldName = field.Name,
                    cropYear = field.CropYear,
                    crop = field.Crop,
                    boundaryFile = "field.geojson",
                    boundaryRole = "outer-boundary",
                    provenanceFile = "import-provenance.json",
                    notes = "Shared season record. The field origin is not moved.",
                };
                File.WriteAllText(Path.Combine(field.GuidanceFolder, "farm-record.json"),
                    JsonSerializer.Serialize(record, FarmStore.Options));
            }
        });
        return found ? null : "Unknown field.";
    }

    private static string? Reset()
    {
        Store.Update(book =>
        {
            var fresh = FarmFixture.Create(DateTime.UtcNow);
            book.Identity = fresh.Identity;
            book.Fields = fresh.Fields;
            book.Machines = fresh.Machines;
            book.Trucks = fresh.Trucks;
            book.Grain = fresh.Grain;
            book.Inventory = fresh.Inventory;
            book.Maintenance = fresh.Maintenance;
            book.Downtime = fresh.Downtime;
            book.Weather = fresh.Weather;
            book.Destinations = fresh.Destinations;
            book.Market = fresh.Market;
            book.Comms = fresh.Comms;
            book.EvenFill = fresh.EvenFill;
            book.PrecisionPlantingRows = fresh.PrecisionPlantingRows;
            book.Telemetry.Clear();
            book.Refusals.Clear();
        });
        return null;
    }

    private static void NoteHost(FarmBook book, LiveFix fix, DateTime utc)
    {
        var host = book.Machines.FirstOrDefault(m => m.Id == "agopen-host");
        if (host == null)
        {
            host = new MachineNode
            {
                Id = "agopen-host",
                Name = "This AgOpen host",
                Display = "AgOpenWeb guidance host",
                Software = "This process. The farm office only reads it.",
                Role = MachineRoles.FullGuidance,
                Operator = "This cab",
                CameraNote = "No camera is attached by the farm office.",
            };
            book.Machines.Add(host);
        }
        host.Lat = fix.Latitude;
        host.Lon = fix.Longitude;
        host.HeadingRadians = fix.HeadingDegrees * Math.PI / 180.0;
        host.SpeedMps = fix.HostSpeed;
        host.Link = "online";
        host.LastSeenUtc = utc;
        book.Telemetry.Add(new Observation
        {
            Utc = utc,
            MachineId = "agopen-host",
            Source = "host-published-state",
            Latitude = fix.Latitude,
            Longitude = fix.Longitude,
            HeadingDegrees = fix.HeadingDegrees,
            HostSpeed = fix.HostSpeed,
            Pgn250SensorPercent = fix.Pgn250SensorPercent,
            Pgn253SteerAngleDeg = fix.Pgn253SteerAngleDeg,
            Pgn211RollDegrees = fix.Pgn211RollDegrees,
            Note = "Read once a second from the published vehicle state and the already-parsed inbound PGNs. Not taken inside the 100 Hz loop. Nothing is sent back.",
        });
        Trim(book);
    }

    private static void Trim(FarmBook book)
    {
        if (book.Telemetry.Count > 240)
            book.Telemetry.RemoveRange(0, book.Telemetry.Count - 240);
    }

    private static void PushRefusal(FarmBook book, string what)
    {
        book.Refusals.Add(new RefusalNote { Utc = DateTime.UtcNow, What = what });
        if (book.Refusals.Count > 40)
            book.Refusals.RemoveRange(0, book.Refusals.Count - 40);
    }

    private static void Advance(FarmBook book, DateTime utc)
    {
        double dt = book.ReplayUtc > DateTime.UnixEpoch ? (utc - book.ReplayUtc).TotalSeconds : 0;
        if (dt < 0 || dt > 3) dt = 1;
        book.ReplayUtc = utc;
        foreach (var machine in book.Machines)
        {
            if (machine.Id == "agopen-host") continue;
            if (machine.Link == "online" && !machine.HoldClock)
                machine.LastSeenUtc = utc;
            if (machine.Link != "online" || machine.Path.Count < 2 || machine.SpeedMps <= 0) continue;
            double length = 0;
            for (int i = 1; i < machine.Path.Count; i++)
                length += FarmAnalysis.Metres(machine.Path[i - 1][0], machine.Path[i - 1][1], machine.Path[i][0], machine.Path[i][1]);
            if (length < 1) continue;
            machine.PathPhase = (machine.PathPhase + machine.SpeedMps * dt / length) % 1.0;
            if (machine.PathPhase < 0) machine.PathPhase += 1;
            Place(machine, length);
        }
        foreach (var truck in book.Trucks)
        {
            truck.Progress += truck.SpeedMps * dt / 20000.0;
            if (truck.Progress > 1) truck.Progress -= 1;
        }
        double wave = 0.5 + 0.5 * Math.Sin(utc.TimeOfDay.TotalSeconds / 9.0);
        book.EvenFill.CartLeft = 0.40 + 0.35 * wave;
        book.EvenFill.CartRight = 0.75 - 0.35 * wave;
        book.EvenFill.CombineTankFraction = 0.30 + 0.55 * (1 - wave);
        book.EvenFill.Suggestion = FarmAnalysis.EvenFillSuggestion(book.EvenFill.CartLeft, book.EvenFill.CartRight);
    }

    private static void Place(MachineNode machine, double length)
    {
        double target = machine.PathPhase * length;
        double walked = 0;
        for (int i = 1; i < machine.Path.Count; i++)
        {
            double seg = FarmAnalysis.Metres(machine.Path[i - 1][0], machine.Path[i - 1][1], machine.Path[i][0], machine.Path[i][1]);
            if (walked + seg >= target || i == machine.Path.Count - 1)
            {
                double t = seg < 0.001 ? 0 : Math.Clamp((target - walked) / seg, 0, 1);
                double lon1 = machine.Path[i - 1][0], lat1 = machine.Path[i - 1][1];
                double lon2 = machine.Path[i][0], lat2 = machine.Path[i][1];
                machine.Lon = lon1 + (lon2 - lon1) * t;
                machine.Lat = lat1 + (lat2 - lat1) * t;
                machine.HeadingRadians = Math.Atan2(lon2 - lon1, lat2 - lat1);
                if (machine.HeadingRadians < 0) machine.HeadingRadians += Math.PI * 2;
                return;
            }
            walked += seg;
        }
    }

    public static string LinkState(MachineNode machine, DateTime utc)
    {
        if (machine.Link == "offline") return "offline";
        var age = utc - machine.LastSeenUtc;
        if (age > TimeSpan.FromMinutes(5)) return "offline";
        if (age > TimeSpan.FromSeconds(45)) return "stale";
        return "online";
    }

    private static object Build(FarmBook book, DateTime utc)
    {
        var (wet, moisture) = GrainLedger.YieldTotals(book.Fields);
        if (wet <= 0) { wet = 1; moisture = book.Grain.TargetMoisture; }
        var grain = GrainLedger.Reconcile(book.Grain, wet, moisture);
        var windows = FarmAnalysis.Windows(book.Weather);
        var harvest = FarmAnalysis.Harvest(book, grain);
        var hauls = FarmAnalysis.Hauls(book, 25000, book.Fields.FirstOrDefault()?.HarvestMoisture ?? 0.18);
        var costs = FarmAnalysis.Costs(book);
        var rocks = FarmAnalysis.RockRoutes(book);
        var scenarios = FarmAnalysis.Scenarios(book, grain);
        var assistant = FarmAnalysis.Assistant(book, grain, windows, harvest, hauls);
        var sync = book.Machines.Select(m => new
        {
            m.Id,
            m.Name,
            m.Link,
            state = LinkState(m, utc),
            ageSeconds = (utc - m.LastSeenUtc).TotalSeconds,
            m.PendingRecords,
            lastSyncUtc = m.LastSyncUtc,
        }).ToList();
        int online = sync.Count(s => s.state == "online");
        int stale = sync.Count(s => s.state == "stale");
        int offline = sync.Count(s => s.state == "offline");
        return new
        {
            safetyBanner = MachineRolePolicy.SafetyBanner,
            fixtureBanner = book.Identity.LabelNote,
            generatedAtUtc = utc,
            importerVersion = FarmGeometryImporter.ImporterVersion,
            localPlaneToleranceM = FarmGeometryImporter.LocalPlaneRoundTripToleranceMeters,
            liveLlmEnabled = false,
            liveLlmNote = book.LiveLlmEnabled
                ? "The live-model flag is on and this build still does not call out or read a key."
                : "Live model calls are off. No API key is required. The cards are local rules on the books.",
            book,
            roles = MachineRolePolicy.Matrix(),
            sync,
            grain,
            inventory = book.Inventory.Select(InventoryMath.Project).ToList(),
            weather = windows,
            hauls,
            marketScenarios = scenarios,
            harvest,
            costs,
            rockRoutes = rocks,
            assistant,
            summary = new
            {
                fields = book.Fields.Count,
                hectares = book.Fields.Sum(f => f.Hectares),
                machines = book.Machines.Count,
                online,
                stale,
                offline,
                pending = book.Machines.Sum(m => m.PendingRecords),
                marketableDryKg = grain.MarketableDryKg,
                yieldDryKg = grain.YieldMapAdjustedDryKg,
                binDryKg = grain.BinDryKg,
                downtimeHours = book.Downtime.Sum(d => d.Hours),
                downtimeUsd = book.Downtime.Sum(d => d.CostUsd),
                trucksComing = book.Trucks.Count(t => t.State == "coming"),
                trucksGoing = book.Trucks.Count(t => t.State == "going"),
                trucksEnroute = book.Trucks.Count(t => t.State == "enroute"),
            },
            precisionPlanting = new
            {
                note = PrecisionPlantingIntake.Note,
                fields = PrecisionPlantingIntake.Fields,
                rows = book.PrecisionPlantingRows,
            },
        };
    }

    private static string? Find(string id) =>
        Store.Read(book => book.Machines.Any(m => m.Id == id) ? id : null);

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out int n) ? n : null;

    private static double? Dbl(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n)) return n;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
            return s;
        return null;
    }
}
