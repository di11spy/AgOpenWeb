// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.IO.Compression;

namespace AgOpenWeb.Services.Kml;

/// <summary>
/// The KML/KMZ coordinate reader the field importer uses. Moved here from
/// <c>MainViewModel.ParseKmlFile</c> so a farm-office import can call it without a
/// view-model, an autosteer service, or a section-control service. The rules are the
/// ones the guidance import already had: every <c>&lt;coordinates&gt;</c> block, lon/lat
/// order, a repeated closing vertex dropped.
/// </summary>
public static class KmlGeometryReader
{
    public sealed class Result
    {
        public List<List<(double Latitude, double Longitude)>> Polygons { get; } = new();
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public int PointCount { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>Text of a KML file, or of the KML inside a KMZ (a zip — usually doc.kml).</summary>
    public static TextReader OpenText(string filePath)
    {
        if (!filePath.EndsWith(".kmz", StringComparison.OrdinalIgnoreCase))
            return new StreamReader(filePath);

        var zip = ZipFile.OpenRead(filePath);
        var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.Name, "doc.kml", StringComparison.OrdinalIgnoreCase))
                    ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));
        if (entry == null) { zip.Dispose(); return new StringReader(string.Empty); }
        using (zip)
        using (var r = new StreamReader(entry.Open()))
            return new StringReader(r.ReadToEnd());
    }

    public static Result Read(string filePath)
    {
        var result = new Result();
        try
        {
            using var reader = OpenText(filePath);
            ReadText(reader.ReadToEnd(), result);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    public static Result ReadText(string text)
    {
        var result = new Result();
        ReadText(text, result);
        return result;
    }

    private static void ReadText(string text, Result result)
    {
        double sumLat = 0, sumLon = 0;
        int total = 0;
        const string Open = "<coordinates>", Close = "</coordinates>";
        int at = 0;
        while ((at = text.IndexOf(Open, at, StringComparison.Ordinal)) != -1)
        {
            int start = at + Open.Length;
            int end = text.IndexOf(Close, start, StringComparison.Ordinal);
            if (end == -1) break;
            at = end + Close.Length;
            string coordinates = text.Substring(start, end - start);
            char[] delimiterChars = { ' ', '\t', '\r', '\n' };
            string[] numberSets = coordinates.Split(delimiterChars, StringSplitOptions.RemoveEmptyEntries);
            if (numberSets.Length < 3) continue;

            var polygon = new List<(double Latitude, double Longitude)>();
            foreach (string item in numberSets)
            {
                if (item.Length < 3) continue;
                string[] parts = item.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    polygon.Add((lat, lon));
                    sumLat += lat;
                    sumLon += lon;
                    total++;
                }
            }

            if (polygon.Count >= 4 && polygon[0] == polygon[^1])
            {
                sumLat -= polygon[^1].Latitude;
                sumLon -= polygon[^1].Longitude;
                total--;
                polygon.RemoveAt(polygon.Count - 1);
            }

            if (polygon.Count >= 3)
                result.Polygons.Add(polygon);
        }

        result.PointCount = total;
        if (total > 0)
        {
            result.CenterLatitude = sumLat / total;
            result.CenterLongitude = sumLon / total;
        }
    }

    /// <summary>SHA-256 of the file bytes, lowercase hex. The file is only read.</summary>
    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
