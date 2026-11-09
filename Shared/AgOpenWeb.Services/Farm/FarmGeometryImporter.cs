// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Xml;
using AgOpenWeb.Models;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.IsoXml;
using AgOpenWeb.Services.GeoJson;
using TrackLine = AgOpenWeb.Models.Track.Track;
using AgOpenWeb.Services.IsoXml;
using AgOpenWeb.Services.Kml;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// Writes a new guidance field folder from ISOXML TASKDATA.XML or KML/KMZ.
/// The source file is only read. <see cref="FieldService"/> is not used, so the
/// AgOpenGPS delete-on-open path never runs. Autosteer and section control are
/// not referenced.
/// </summary>
public static class FarmGeometryImporter
{
    public const string ImporterVersion = "26.7.2-farm-office";

    /// <summary>
    /// Metres on the original <see cref="LocalPlane"/> after a round trip through
    /// this app's own ISOXML export and this importer. The plane is the ellipsoid
    /// series in <c>LocalPlane</c> (the same one <c>Docs/FIELD_FILE_FORMATS.md</c>
    /// writes down). ISOXML stores WGS84 as decimal degrees. 0.05 m is the budget
    /// for that formatting. A swapped axis or a dropped corner fails it.
    /// </summary>
    public const double LocalPlaneRoundTripToleranceMeters = 0.05;

    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";
        public string FieldDirectory { get; init; } = "";
        public string ProvenancePath { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public long ByteLength { get; init; }
        public List<string> Imported { get; init; } = new();
        public string FieldName { get; init; } = "";
    }

    public static Result ImportIsoXml(string taskDataPath, string fieldsDirectory, string fieldName, string sourceLabel, int fieldIndex = 0)
        => ImportFile(taskDataPath, fieldsDirectory, fieldName, sourceLabel, fieldIndex, kml: false);

    public static Result ImportKml(string kmlPath, string fieldsDirectory, string fieldName, string sourceLabel)
        => ImportFile(kmlPath, fieldsDirectory, fieldName, sourceLabel, 0, kml: true);

    private static Result ImportFile(string sourcePath, string fieldsDirectory, string fieldName, string sourceLabel, int fieldIndex, bool kml)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return Fail("Source file was not found.");
        if (LooksLikeAgOpenGpsFolder(Path.GetDirectoryName(sourcePath) ?? ""))
            return Fail("The source sits in an AgOpenGPS field folder. Copy the TASKDATA.XML or KML out before importing. This importer will not open that folder.");

        if (!FieldNames.TryCheck(fieldName, out var checkedName, out var nameProblem))
            return Fail(nameProblem);

        Directory.CreateDirectory(fieldsDirectory);
        string unique = UniqueName(fieldsDirectory, checkedName);
        string before = KmlGeometryReader.Sha256Hex(sourcePath);
        long length = new FileInfo(sourcePath).Length;

        Result result;
        try
        {
            result = kml
                ? WriteKml(sourcePath, fieldsDirectory, unique, sourceLabel, before, length)
                : WriteIsoXml(sourcePath, fieldsDirectory, unique, sourceLabel, fieldIndex, before, length);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }

        string after = KmlGeometryReader.Sha256Hex(sourcePath);
        if (!string.Equals(before, after, StringComparison.Ordinal) || new FileInfo(sourcePath).Length != length)
            return Fail("The source bytes changed during import. The new field folder should be discarded.");
        return result;
    }

    private static Result WriteIsoXml(string path, string fieldsDirectory, string fieldName, string sourceLabel, int fieldIndex, string sha, long length)
    {
        var doc = new XmlDocument();
        doc.Load(path);
        var pfds = doc.GetElementsByTagName("PFD");
        if (pfds.Count == 0) return Fail("ISOXML file contains no field (PFD).");
        var pfd = pfds[Math.Clamp(fieldIndex, 0, pfds.Count - 1)]!;
        if (!TryCentroid(pfd, out double lat, out double lon))
            return Fail("ISOXML file has no coordinates to import.");

        var plane = new LocalPlane(new Wgs84(lat, lon), new SharedFieldProperties());
        var boundaries = IsoXmlParserHelpers.ParseBoundaries(pfd.ChildNodes, plane);
        var headland = IsoXmlParserHelpers.ParseHeadland(pfd.ChildNodes, plane);
        var tracks = IsoXmlParserHelpers.ParseAllGuidanceLines(pfd.ChildNodes, plane);
        bool hasBoundary = boundaries.Count > 0 && boundaries[0].FenceLine.Count >= 3;
        if (!hasBoundary && tracks.Count == 0)
            return Fail("ISOXML field has no boundary and no guidance line.");

        var imported = new List<string>();
        var field = NewField(Path.Combine(fieldsDirectory, fieldName), fieldName, lat, lon);
        if (hasBoundary)
        {
            field.Boundary = ToBoundary(boundaries, headland);
            imported.Add("outer-boundary");
            if (boundaries.Count > 1) imported.Add("inner-boundary");
            if (headland.Count >= 3) imported.Add("headland");
        }
        var trackModels = ToTracks(tracks);
        if (trackModels.Count > 0) imported.Add("guidance-line");
        GeoJsonFieldService.Save(field, trackModels.Count > 0 ? trackModels : null);
        return Finish(field, path, sourceLabel, sha, length, imported, "isoxml-taskdata", fieldIndex);
    }

    private static Result WriteKml(string path, string fieldsDirectory, string fieldName, string sourceLabel, string sha, long length)
    {
        var parsed = KmlGeometryReader.Read(path);
        if (parsed.Error != null) return Fail("KML: " + parsed.Error);
        if (parsed.Polygons.Count == 0 || parsed.PointCount < 3)
            return Fail("KML file has no polygon to import.");

        var plane = new LocalPlane(new Wgs84(parsed.CenterLatitude, parsed.CenterLongitude), new SharedFieldProperties());
        var field = NewField(Path.Combine(fieldsDirectory, fieldName), fieldName, parsed.CenterLatitude, parsed.CenterLongitude);
        var boundary = new Boundary();
        for (int i = 0; i < parsed.Polygons.Count; i++)
        {
            var poly = new BoundaryPolygon();
            foreach (var (plat, plon) in parsed.Polygons[i])
            {
                var local = plane.ConvertWgs84ToGeoCoord(new Wgs84(plat, plon));
                poly.Points.Add(new BoundaryPoint(local.Easting, local.Northing, 0));
            }
            StampHeadings(poly.Points);
            poly.UpdateBounds();
            if (i == 0) boundary.OuterBoundary = poly;
            else boundary.InnerBoundaries.Add(poly);
        }
        field.Boundary = boundary;
        GeoJsonFieldService.Save(field, null);
        var imported = new List<string> { "outer-boundary" };
        if (parsed.Polygons.Count > 1) imported.Add("inner-boundary");
        string format = path.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase) ? "kmz" : "kml";
        return Finish(field, path, sourceLabel, sha, length, imported, format, 0);
    }

    private static Result Finish(Field field, string sourcePath, string sourceLabel, string sha, long length, List<string> imported, string format, int fieldIndex)
    {
        var provenance = new
        {
            schemaVersion = 1,
            sourceFileName = Path.GetFileName(sourcePath),
            sourceByteLength = length,
            sha256 = sha,
            sha256AfterImport = sha,
            sourceUnchanged = true,
            importedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
            importerVersion = ImporterVersion,
            sourceLabel,
            imported,
            fieldIndex,
            format,
            notes = "Source bytes were not modified. FieldService was not called. This is not an OEM display decoder and not brand support.",
        };
        string provenancePath = Path.Combine(field.DirectoryPath, "import-provenance.json");
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(provenance, JsonOptions()));
        var record = new
        {
            schemaVersion = 1,
            fieldName = field.Name,
            cropYear = "",
            crop = "",
            boundaryFile = "field.geojson",
            boundaryRole = "outer-boundary",
            provenanceFile = "import-provenance.json",
            notes = "Shared season record for this folder. Layers line up on this boundary. The origin in field.geojson is the one written at import and is not moved.",
        };
        File.WriteAllText(Path.Combine(field.DirectoryPath, "farm-record.json"), JsonSerializer.Serialize(record, JsonOptions()));
        return new Result
        {
            Ok = true,
            FieldDirectory = field.DirectoryPath,
            ProvenancePath = provenancePath,
            Sha256 = sha,
            ByteLength = length,
            Imported = imported,
            FieldName = field.Name,
        };
    }

    private static Field NewField(string directory, string name, double lat, double lon) => new()
    {
        Name = name,
        DirectoryPath = directory,
        Origin = new Position { Latitude = lat, Longitude = lon },
        CreatedDate = DateTime.UtcNow,
        LastModifiedDate = DateTime.UtcNow,
    };

    private static Boundary ToBoundary(List<IsoXmlBoundary> boundaries, List<Vec3> headland)
    {
        var boundary = new Boundary();
        for (int i = 0; i < boundaries.Count; i++)
        {
            var poly = new BoundaryPolygon { IsDriveThrough = boundaries[i].IsDriveThru };
            foreach (var v in boundaries[i].FenceLine)
                poly.Points.Add(new BoundaryPoint(v.Easting, v.Northing, v.Heading));
            if (poly.Points.Count >= 2 && poly.Points[0].Heading == 0)
                StampHeadings(poly.Points);
            poly.UpdateBounds();
            if (i == 0) boundary.OuterBoundary = poly;
            else boundary.InnerBoundaries.Add(poly);
        }
        if (headland.Count >= 3)
        {
            var hp = new BoundaryPolygon();
            foreach (var v in headland)
                hp.Points.Add(new BoundaryPoint(v.Easting, v.Northing, v.Heading));
            hp.UpdateBounds();
            boundary.HeadlandPolygon = hp;
        }
        return boundary;
    }

    private static List<TrackLine> ToTracks(List<IsoXmlTrack> parsed)
    {
        var tracks = new List<TrackLine>();
        foreach (var t in parsed)
        {
            var track = new TrackLine { Name = t.Name, NudgeDistance = t.NudgeDistance, IsVisible = t.IsVisible };
            if (t.Mode == IsoXmlTrackMode.Curve && t.CurvePoints.Count >= 2)
            {
                track.Type = AgOpenWeb.Models.Track.TrackType.Curve;
                track.Points = new List<Vec3>(t.CurvePoints);
            }
            else
            {
                track.Type = AgOpenWeb.Models.Track.TrackType.ABLine;
                track.Points = new List<Vec3>
                {
                    new(t.PtA.Easting, t.PtA.Northing, t.Heading),
                    new(t.PtB.Easting, t.PtB.Northing, t.Heading),
                };
            }
            if (track.Points.Count >= 2) tracks.Add(track);
        }
        return tracks;
    }

    private static void StampHeadings(List<BoundaryPoint> pts)
    {
        if (pts.Count < 2) return;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            double heading = Math.Atan2(pts[i + 1].Easting - pts[i].Easting, pts[i + 1].Northing - pts[i].Northing);
            if (heading < 0) heading += Math.PI * 2;
            pts[i].Heading = heading;
        }
        pts[^1].Heading = pts[^2].Heading;
    }

    private static bool TryCentroid(XmlNode pfd, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        XmlNodeList? points = null;
        var plns = pfd.SelectNodes("PLN");
        if (plns != null)
        {
            foreach (XmlNode pln in plns)
            {
                var type = pln.Attributes?["A"]?.Value;
                if (type != "1" && type != "9") continue;
                points = pln.SelectSingleNode("LSG[@A='1']")?.SelectNodes("PNT");
                if (points is { Count: > 0 }) break;
            }
        }
        if (points == null || points.Count == 0)
            points = pfd.SelectNodes(".//PNT");
        if (points == null || points.Count == 0) return false;
        double latSum = 0, lonSum = 0;
        int n = 0;
        foreach (XmlNode pnt in points)
        {
            if (double.TryParse(pnt.Attributes?["C"]?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double la) &&
                double.TryParse(pnt.Attributes?["D"]?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lo))
            {
                latSum += la;
                lonSum += lo;
                n++;
            }
        }
        if (n == 0) return false;
        lat = latSum / n;
        lon = lonSum / n;
        return true;
    }

    public static bool LooksLikeAgOpenGpsFolder(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            if (name.Equals("Field.txt", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Boundary.txt", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string UniqueName(string fieldsDirectory, string name)
    {
        if (FieldNames.ExistingTwin(fieldsDirectory, name) == null) return name;
        for (int i = 2; i < 1000; i++)
        {
            string candidate = name + " " + i.ToString(CultureInfo.InvariantCulture);
            if (FieldNames.ExistingTwin(fieldsDirectory, candidate) == null) return candidate;
        }
        return name + " " + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    private static Result Fail(string error) => new() { Ok = false, Error = error };
}
