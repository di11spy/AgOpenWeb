// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// One labeled sample farm so every office screen has something to run.
/// Nothing here is a Case IH, Trimble, John Deere, or Precision Planting export.
/// </summary>
public static class FarmFixture
{
    public static FarmBook Create(DateTime utc)
    {
        var north = Field("north-80", "North 80", "corn", 41.6000, -93.7000, 41.6050, -93.6930, 0.175, 0.35);
        var south = Field("south-40", "South 40", "soybeans", 41.5940, -93.7000, 41.5980, -93.6940, 0.135, 0.10);
        var home = Field("home-quarter", "Home quarter", "corn", 41.6000, -93.6880, 41.6030, -93.6820, 0.230, 0.80);
        StampLayers(north, wetPerM2: 1.15, moisture: 0.185);
        StampLayers(south, wetPerM2: 0.42, moisture: 0.130);
        StampLayers(home, wetPerM2: 1.05, moisture: 0.210);

        var book = new FarmBook
        {
            ReplayUtc = utc,
            Fields = new List<FieldPlot> { north, south, home },
            Machines = Machines(utc, north),
            Trucks = Trucks(),
            Grain = Grain(),
            Inventory = Inventory(),
            Maintenance = Maintenance(),
            Downtime = Downtime(),
            Weather = Weather(utc),
            Destinations = Destinations(),
            Market = Market(),
            Comms = Comms(utc),
            EvenFill = new EvenFillPlan { CombineTankFraction = 0.62, CartLeft = 0.71, CartRight = 0.48 },
            PrecisionPlantingRows = Rows(),
        };
        book.EvenFill.Suggestion = FarmAnalysis.EvenFillSuggestion(book.EvenFill.CartLeft, book.EvenFill.CartRight);
        foreach (var field in book.Fields)
            field.Hectares = FarmAnalysis.AreaM2(field.Boundary) / 10000.0;
        return book;
    }

    public static void WriteSamples(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TASKDATA.XML"), SampleTaskData(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "sample-field.kml"), SampleKml(), new UTF8Encoding(false));
    }

    public static string SampleTaskData() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <ISO11783_TaskData VersionMajor="4" VersionMinor="2" ManagementSoftwareManufacturer="AgOpenWeb" ManagementSoftwareVersion="26.7.2-farm-office" DataTransferOrigin="1">
          <PFD A="PFD1" C="Sample North">
            <PLN A="1" B="Field"><LSG A="1">
              <PNT A="2" C="41.601000" D="-93.699000"/>
              <PNT A="2" C="41.601000" D="-93.697000"/>
              <PNT A="2" C="41.603000" D="-93.697000"/>
              <PNT A="2" C="41.603000" D="-93.699000"/>
            </LSG></PLN>
            <GGP A="GGP1" B="AB"><GPN A="GPN1" B="AB 1" C="1"><LSG A="5">
              <PNT A="6" C="41.601200" D="-93.698800"/>
              <PNT A="7" C="41.602800" D="-93.698800"/>
            </LSG></GPN></GGP>
          </PFD>
        </ISO11783_TaskData>
        """;

    public static string SampleKml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <kml xmlns="http://www.opengis.net/kml/2.2"><Document><Placemark><Polygon><outerBoundaryIs><LinearRing><coordinates>
        -93.6860,41.6010,0 -93.6840,41.6010,0 -93.6840,41.6025,0 -93.6860,41.6025,0 -93.6860,41.6010,0
        </coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></Document></kml>
        """;

    private static FieldPlot Field(string id, string name, string crop, double south, double west, double north, double east, double moisture, double left)
    {
        var ring = new List<double[]>
        {
            new[] { west, south }, new[] { east, south }, new[] { east, north }, new[] { west, north },
        };
        var (lon, lat) = FarmAnalysis.Centroid(ring);
        return new FieldPlot
        {
            Id = id,
            Name = name,
            Crop = crop,
            CropYear = "2026",
            Provenance = "sample-fixture — not a monitor export",
            OriginLonLat = new[] { lon, lat },
            Boundary = ring,
            HarvestMoisture = moisture,
            UnharvestedFraction = left,
            Costs = new CostBook
            {
                SeedUsd = crop == "corn" ? 14800 : 7200,
                FertilizerUsd = crop == "corn" ? 18600 : 4100,
                ChemicalUsd = 6300,
                FuelUsd = 4100,
                LaborUsd = 5200,
            },
            Rain = new RainBook
            {
                SeasonGaugeM = 0.412,
                SeasonRadarM = 0.438,
                Storms = new List<RainEvent>
                {
                    new() { When = "2026-06-14", GaugeM = 0.041, RadarM = 0.036 },
                    new() { When = "2026-07-02", GaugeM = 0.018, RadarM = 0.027 },
                    new() { When = "2026-08-21", GaugeM = 0.033, RadarM = 0.031 },
                },
                Days = new List<RainDay>
                {
                    new() { Day = "2026-11-07", GaugeM = 0.000, RadarM = 0.001 },
                    new() { Day = "2026-11-08", GaugeM = 0.006, RadarM = 0.004 },
                    new() { Day = "2026-11-09", GaugeM = 0.000, RadarM = 0.000 },
                },
            },
        };
    }

    private static void StampLayers(FieldPlot field, double wetPerM2, double moisture)
    {
        double south = field.Boundary.Min(p => p[1]);
        double north = field.Boundary.Max(p => p[1]);
        double west = field.Boundary.Min(p => p[0]);
        double east = field.Boundary.Max(p => p[0]);
        double area = FarmAnalysis.AreaM2(field.Boundary);
        int cols = 4, rows = 3;
        double cellArea = area / (cols * rows);
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            double lon = west + (c + 0.5) * (east - west) / cols;
            double lat = south + (r + 0.5) * (north - south) / rows;
            double vary = 0.82 + 0.06 * c + 0.04 * r;
            field.Yield.Add(new YieldCell
            {
                Lon = lon, Lat = lat, AreaM2 = cellArea, WetKg = cellArea * wetPerM2 * vary, Moisture = moisture,
            });
            field.Nitrogen.Add(new NitrogenCell
            {
                Lon = lon, Lat = lat,
                SufficiencyEstimate = Math.Clamp(0.45 + 0.1 * c - 0.05 * r, 0.15, 0.95),
            });
        }

        field.Soil.Add(new SoilPoint
        {
            Id = field.Id + "-s1", Lon = west + (east - west) * 0.3, Lat = south + (north - south) * 0.4,
            Ph = new SiValue(6.4, "1", "pH"),
            Phosphorus = new SiValue(22, "mg/kg", "phosphorus"),
            Potassium = new SiValue(160, "mg/kg", "potassium"),
            OrganicMatter = new SiValue(0.031, "kg/kg", "organic matter"),
        });
        field.Soil.Add(new SoilPoint
        {
            Id = field.Id + "-s2", Lon = west + (east - west) * 0.7, Lat = south + (north - south) * 0.7,
            Ph = new SiValue(7.1, "1", "pH"),
            Phosphorus = new SiValue(11, "mg/kg", "phosphorus"),
            Potassium = new SiValue(120, "mg/kg", "potassium"),
            OrganicMatter = new SiValue(0.024, "kg/kg", "organic matter"),
        });

        field.Zones.Add(new ZonePoly
        {
            Id = field.Id + "-z-low", Name = "Low P", Management = "Hold a higher phosphorus rate on this side.",
            Ring = new List<double[]>
            {
                new[] { west, south }, new[] { (west + east) / 2, south },
                new[] { (west + east) / 2, north }, new[] { west, north },
            },
        });
        field.Zones.Add(new ZonePoly
        {
            Id = field.Id + "-z-high", Name = "High ground", Management = "Standard rate.",
            Ring = new List<double[]>
            {
                new[] { (west + east) / 2, south }, new[] { east, south },
                new[] { east, north }, new[] { (west + east) / 2, north },
            },
        });

        field.Operations.Add(new FieldOp
        {
            Id = field.Id + "-plant", Kind = "as-planted", When = "2026-04-28",
            Product = field.Crop, Rate = new SiValue(field.Crop == "corn" ? 0.00085 : 0.0072, "kg/m2", "seed"),
            Hectares = 0, MachineId = "planter", Source = "sample-fixture row summary",
            Note = "Not a Precision Planting binary. Row detail is the JSON fixture on the planter.",
        });
        field.Operations.Add(new FieldOp
        {
            Id = field.Id + "-n", Kind = "as-applied", When = "2026-06-12",
            Product = "UAN", Rate = new SiValue(0.018, "kg/m2", "product"),
            MachineId = "pro700", Source = "sample-fixture as-applied",
            Note = "Fixture application record. No rate controller was commanded.",
        });

        field.Places.Add(new PlaceMark { Kind = "entrance", Name = "Main entrance", Lon = west, Lat = south, Note = "Gravel approach. Shared with every operator." });
        field.Places.Add(new PlaceMark { Kind = "tile-inlet", Name = "Tile inlet", Lon = west + (east - west) * 0.2, Lat = north - (north - south) * 0.15, Note = "Keep the boom off the riser." });
        field.Places.Add(new PlaceMark { Kind = "wet", Name = "Wet hole", Lon = east - (east - west) * 0.25, Lat = south + (north - south) * 0.3, Note = "Holds water after 20 mm." });
        field.Places.Add(new PlaceMark { Kind = "hazard", Name = "Well", Lon = west + (east - west) * 0.15, Lat = south + (north - south) * 0.8, Note = "Marked. Do not fold the implement over it." });

        field.Rocks.Add(new RockMark { Id = field.Id + "-r1", Lon = west + (east - west) * 0.4, Lat = south + (north - south) * 0.45, Note = "GPS tag from the cultivator pass (fixture)." });
        field.Rocks.Add(new RockMark { Id = field.Id + "-r2", Lon = west + (east - west) * 0.62, Lat = south + (north - south) * 0.7, Note = "Second tag, same pass." });
        field.Rocks.Add(new RockMark { Id = field.Id + "-r3", Lon = west + (east - west) * 0.8, Lat = south + (north - south) * 0.25, Note = "Edge of the wet hole." });

        field.Scouting.Add(new ScoutMark { Id = field.Id + "-d1", Flag = "stand-gap", Lon = west + (east - west) * 0.35, Lat = south + (north - south) * 0.55, When = "2026-06-02", Caption = "Fixture frame: thin stand along the old waterway. No photo file is attached." });
        field.Scouting.Add(new ScoutMark { Id = field.Id + "-d2", Flag = "weeds", Lon = west + (east - west) * 0.58, Lat = south + (north - south) * 0.22, When = "2026-06-02", Caption = "Fixture frame: weed streak. Not a spray command." });
        field.Scouting.Add(new ScoutMark { Id = field.Id + "-d3", Flag = "water", Lon = east - (east - west) * 0.22, Lat = south + (north - south) * 0.33, When = "2026-07-03", Caption = "Fixture frame: water sitting where the wet hole is marked." });
        field.Scouting.Add(new ScoutMark { Id = field.Id + "-d4", Flag = "scout-needed", Lon = west + (east - west) * 0.5, Lat = north - (north - south) * 0.2, When = "2026-07-03", Caption = "Fixture frame: odd color. A person should walk it. The nitrogen layer is only an estimate." });

        double midLon = (west + east) / 2;
        field.Strips = new StripPlan
        {
            RtkNote = "Fixture base FIXTURE-BASE-1, surveyed 2026-04-12T15:04:00Z. Repeat these strips next spring. The base id is a label, not a connection.",
            Lines = new List<StripLine>
            {
                new() { Name = "Strip 1", LonA = midLon, LatA = south, LonB = midLon, LatB = north, HeadingRadians = 0, SurveyedUtc = "2026-04-12T15:04:00Z", BaseId = "FIXTURE-BASE-1" },
                new() { Name = "Strip 2", LonA = midLon + (east - west) * 0.08, LatA = south, LonB = midLon + (east - west) * 0.08, LatB = north, HeadingRadians = 0, SurveyedUtc = "2026-04-12T15:10:00Z", BaseId = "FIXTURE-BASE-1" },
            },
        };
    }

    private static List<MachineNode> Machines(DateTime utc, FieldPlot north)
    {
        double s = north.Boundary.Min(p => p[1]), n = north.Boundary.Max(p => p[1]);
        double w = north.Boundary.Min(p => p[0]), e = north.Boundary.Max(p => p[0]);
        List<double[]> pass = new()
        {
            new[] { w + (e - w) * 0.2, s + (n - s) * 0.15 },
            new[] { w + (e - w) * 0.2, s + (n - s) * 0.85 },
            new[] { w + (e - w) * 0.35, s + (n - s) * 0.85 },
            new[] { w + (e - w) * 0.35, s + (n - s) * 0.15 },
        };
        var east = Node("fm750-east", "Tractor east", "Trimble FM-750", "Display software unnamed. No FM-750 decoder.",
            MachineRoles.ReadOnlyLogger, "Tractor operator", e - 0.001, n - 0.001, "online", utc.AddMinutes(-2), 3, 980, 0.40, 18, pass);
        east.HoldClock = true;
        var list = new List<MachineNode>
        {
            Node("combine-pro600", "Combine", "Case IH Pro 600", "Display software unnamed. No Pro 600 decoder.",
                MachineRoles.ReadOnlyLogger, "Combine operator", pass[0][0], pass[0][1], "online", utc, 0, 640, 0.22, 42, pass),
            Node("fm750-west", "Tractor west", "Trimble FM-750", "Display software unnamed. No FM-750 decoder.",
                MachineRoles.ImplementGuidance, "Tractor operator", w + 0.001, s + 0.001, "online", utc, 0, 1210, 0.55, 18, pass),
            east,
            Node("fmx", "Tractor FmX", "Trimble FmX", "Display software unnamed. No FmX decoder.",
                MachineRoles.FullGuidance, "Tractor operator", -93.705, 41.599, "offline", utc.AddHours(-5), 4, 1540, 0.15, 16, pass),
            Node("pro700", "Tractor Pro 700", "Case IH Pro 700", "Display software unnamed. No Pro 700 decoder.",
                MachineRoles.ReadOnlyLogger, "Tractor operator", -93.690, 41.602, "online", utc, 0, 880, 0.61, 20, pass),
            Node("jd-unnamed", "Green tractor", "John Deere — display unnamed", "Software unnamed. No decoder is claimed.",
                MachineRoles.ReadOnlyLogger, "Tractor operator", -93.686, 41.6015, "online", utc.AddMinutes(-1), 1, 760, 0.48, 17, pass),
            Node("planter", "Planter", "Precision Planting — monitor unnamed", "Modules unnamed. JSON row fixture only.",
                MachineRoles.ReadOnlyLogger, "Planter operator", -93.691, 41.597, "offline", utc.AddDays(-1), 0, 210, 0.80, 0, pass),
        };
        for (int i = 0; i < list.Count; i++) list[i].PathPhase = i * 0.17;
        return list;
    }

    private static MachineNode Node(string id, string name, string display, string software, string role, string op,
        double lon, double lat, string link, DateTime seen, int pending, double hours, double fuel, double burn, List<double[]> path)
        => new()
        {
            Id = id, Name = name, Display = display, Software = software, Role = role, Operator = op,
            Lon = lon, Lat = lat, Link = link, LastSeenUtc = seen, PendingRecords = pending,
            LastSyncUtc = link == "offline" ? seen : seen.AddMinutes(-2),
            EngineHours = hours, FuelFraction = fuel, FuelBurnKgPerHour = burn, Path = path, SpeedMps = link == "online" ? 2.4 : 0,
        };

    private static List<TruckLeg> Trucks() => new()
    {
        new() { Id = "truck-coming", Name = "Truck A", State = "coming", From = "County elevator", To = "North 80", Lon = -93.720, Lat = 41.590, Progress = 0.35, LoadDryKg = 0, Crop = "corn", Note = "Empty, on the way to the field." },
        new() { Id = "truck-going", Name = "Truck B", State = "going", From = "North 80", To = "County elevator", Lon = -93.710, Lat = 41.608, Progress = 0.55, LoadDryKg = 24000, Crop = "corn", Note = "Loaded, leaving the field." },
        new() { Id = "truck-enroute", Name = "Truck C", State = "enroute", From = "Farm bin", To = "River terminal", Lon = -93.740, Lat = 41.620, Progress = 0.42, LoadDryKg = 26000, Crop = "corn", Note = "Between the farm and the terminal." },
    };

    private static GrainInputs Grain() => new()
    {
        TargetMoisture = 0.155,
        YieldCalibration = 0.97,
        CartCalibration = 1.0,
        CombineTankWetKg = 8000,
        CombineTankMoisture = 0.185,
        TransferredToBinDryKg = 30000,
        Carts = new List<ScaleTicket>
        {
            new() { Id = "cart-1", WetKg = 40000, Moisture = 0.185, When = "2026-11-09T14:10:00Z" },
            new() { Id = "cart-2", WetKg = 35000, Moisture = 0.190, When = "2026-11-09T16:40:00Z" },
        },
        Bins = new List<BinLot>
        {
            new() { Id = "bin-1", Name = "North bin", DryKg = 180000, CapacityKg = 250000, Moisture = 0.155, Crop = "corn" },
            new() { Id = "bin-2", Name = "South bin", DryKg = 70000, CapacityKg = 120000, Moisture = 0.150, Crop = "corn" },
            new() { Id = "bin-soy", Name = "Soy bin", DryKg = 40000, CapacityKg = 80000, Moisture = 0.130, Crop = "soybeans" },
        },
        Contracts = new List<GrainContract>
        {
            new() { Id = "c-1", Buyer = "County elevator (sample contract)", CommittedDryKg = 50000, DeliveredDryKg = 20000, PriceUsdPerKg = 0.168, Crop = "corn" },
        },
    };

    private static List<StockItem> Inventory() => new()
    {
        new() { Sku = "seed-corn", Kind = "seed", Name = "Corn seed", OnHand = 820, Committed = 240, PlannedNeed = 700, Unit = "kg", Note = "Committed to the home-quarter replant plan." },
        new() { Sku = "uan", Kind = "fertilizer", Name = "UAN", OnHand = 12000, Committed = 4000, PlannedNeed = 9000, Unit = "kg" },
        new() { Sku = "herb", Kind = "chemical", Name = "Burndown", OnHand = 180, Committed = 40, PlannedNeed = 200, Unit = "kg", Note = "Available will sit under the planned need." },
        new() { Sku = "diesel", Kind = "fuel", Name = "Diesel", OnHand = 6400, Committed = 1200, PlannedNeed = 2500, Unit = "kg" },
    };

    private static List<MaintenanceWatch> Maintenance() => new()
    {
        new() { MachineId = "combine-pro600", Item = "Engine oil", Hours = 248, IntervalHours = 250, Trend = "Hours are 2 under the interval.", Severity = "soon" },
        new() { MachineId = "combine-pro600", Item = "Coolant temperature", Hours = 0, IntervalHours = 0, Trend = "Fixture trend: +0.4 K per hour over the last six samples.", Severity = "watch" },
        new() { MachineId = "fm750-west", Item = "Filters", Hours = 190, IntervalHours = 200, Trend = "Ten hours left on the fixture meter.", Severity = "soon" },
        new() { MachineId = "fmx", Item = "Feeder chain", Hours = 40, IntervalHours = 100, Trend = "No trend. The machine is offline in this fixture.", Severity = "watch" },
    };

    private static List<DowntimeSpan> Downtime() => new()
    {
        new() { MachineId = "combine-pro600", Reason = "Plugged rotor (fixture log)", Hours = 1.4, CostUsd = 420, When = "2026-11-08" },
        new() { MachineId = "truck-going", Reason = "Scale line", Hours = 0.6, CostUsd = 90, When = "2026-11-09" },
    };

    private static WeatherBook Weather(DateTime utc)
    {
        var hours = new List<WeatherHour>();
        for (int i = 0; i < 18; i++)
        {
            var t = utc.AddHours(i);
            hours.Add(new WeatherHour
            {
                When = t.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture),
                WindMps = i < 4 ? 6.2 : i < 10 ? 3.1 : 2.4,
                RainProbability = i < 3 ? 0.45 : i == 8 ? 0.25 : 0.05,
                AirK = 281 + (i % 6),
                SoilK = i < 6 ? 281.5 : 284.2,
                SoilWater = i < 2 ? 0.44 : 0.34,
            });
        }
        return new WeatherBook { Hours = hours };
    }

    private static List<Destination> Destinations() => new()
    {
        new() { Id = "county", Name = "County elevator", Kind = "elevator", Lon = -93.760, Lat = 41.630, Hours = "07:00–17:00 local (fixture)", Crops = new() { "corn", "soybeans" }, BidUsdPerKg = 0.172, DryingUsdPerMoisturePoint = 0.004, HaulUsdPerMetre = 0.0018 },
        new() { Id = "river", Name = "River terminal", Kind = "terminal", Lon = -93.820, Lat = 41.680, Hours = "06:00–22:00 local (fixture)", Crops = new() { "corn" }, BidUsdPerKg = 0.181, DryingUsdPerMoisturePoint = 0.0035, HaulUsdPerMetre = 0.0018 },
        new() { Id = "coop", Name = "South co-op", Kind = "elevator", Lon = -93.650, Lat = 41.540, Hours = "08:00–16:00 local (fixture)", Crops = new() { "corn", "soybeans" }, BidUsdPerKg = 0.169, DryingUsdPerMoisturePoint = 0.0045, HaulUsdPerMetre = 0.0020 },
    };

    private static MarketBook Market()
    {
        var points = new List<MarketPoint>();
        double price = 0.162;
        for (int i = 0; i < 16; i++)
        {
            price += (i % 3 - 1) * 0.0015;
            points.Add(new MarketPoint
            {
                Day = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc).AddDays(i * 7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                UsdPerKg = Math.Round(price, 4),
            });
        }
        return new MarketBook { Points = points };
    }

    private static CommsBook Comms(DateTime utc) => new()
    {
        Channels = new List<CommChannel>
        {
            new() { Id = "harvest", Name = "Harvest", Kind = "group", Members = new() { "Office", "Combine operator", "Cart operator", "Truck A" } },
            new() { Id = "shop", Name = "Shop", Kind = "group", Members = new() { "Office", "Shop" } },
            new() { Id = "office-combine", Name = "Office → Combine", Kind = "direct", Members = new() { "Office", "Combine operator" } },
        },
        Posts = new List<CommPost>
        {
            new() { Id = "m1", ChannelId = "harvest", From = "Office", Kind = "text", Text = "Bin 2 is the one with room. Sample figure, check the board.", Utc = utc.AddMinutes(-40) },
            new() { Id = "m2", ChannelId = "harvest", From = "Combine operator", Kind = "text", Text = "North 80 is the pass we're on. Radios stay primary.", Utc = utc.AddMinutes(-28) },
            new() { Id = "m3", ChannelId = "office-combine", From = "Office", Kind = "text", Text = "Truck A is the empty one coming to you.", Utc = utc.AddMinutes(-12) },
        },
    };

    private static List<PlantingRow> Rows()
    {
        var rows = new List<PlantingRow>();
        for (int i = 1; i <= 16; i++)
        {
            rows.Add(new PlantingRow
            {
                Row = i,
                SpacingM = i == 7 ? 0.24 : 0.19,
                DownforceN = 180 + i * 3,
                Singulation = i == 7 ? 0.91 : 0.985,
            });
        }
        return rows;
    }
}
