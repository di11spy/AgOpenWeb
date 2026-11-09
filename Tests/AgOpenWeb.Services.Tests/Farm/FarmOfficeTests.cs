// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgOpenWeb.Models;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Farm;
using AgOpenWeb.Models.IsoXml;
using AgOpenWeb.Services.Farm;
using AgOpenWeb.Services.GeoJson;
using AgOpenWeb.Services.IsoXml;

namespace AgOpenWeb.Services.Tests.Farm;

[TestFixture]
public class FarmOfficeTests
{
    [Test]
    public void FixtureSnapshot_BuildsTheOfficeView()
    {
        using var dir = new TempDir();
        FarmOffice.UseStore(FarmStore.Open(dir.Path));
        FarmOffice.UseLiveFix(null);
        var json = JsonSerializer.Serialize(FarmOffice.View(), FarmStore.Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.That(root.GetProperty("book").GetProperty("fields").GetArrayLength(), Is.EqualTo(3));
        Assert.That(root.GetProperty("grain").GetProperty("yieldEqualsBins").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("roles").GetArrayLength(), Is.EqualTo(3));
        Assert.That(root.GetProperty("assistant").GetArrayLength(), Is.GreaterThan(3));
        Assert.That(root.GetProperty("safetyBanner").GetString(), Does.Contain("not connected to machine control"));
        foreach (var role in root.GetProperty("roles").EnumerateArray())
            Assert.That(role.GetProperty("mayCommandSteer").GetBoolean(), Is.False);
    }

    [Test]
    public void DryMass_ShrinksWetGrain_AndDoesNotInventWater()
    {
        // 1000 kg at 0.200 down to 0.155: 1000 * 0.800 / 0.845.
        double dry = GrainLedger.DryMassKg(1000, 0.200, 0.155);
        Assert.That(dry, Is.EqualTo(1000 * 0.800 / 0.845).Within(1e-9));
        Assert.That(GrainLedger.DryMassKg(1000, 0.140, 0.155), Is.EqualTo(1000));
    }

    [Test]
    public void Calibration_ScalesTheMeasuredMass()
    {
        Assert.That(GrainLedger.ApplyCalibration(1000, 0.97), Is.EqualTo(970).Within(1e-9));
    }

    [Test]
    public void Reconciliation_DoesNotTreatYieldAsBinInventory()
    {
        var inputs = new GrainInputs
        {
            TargetMoisture = 0.155,
            YieldCalibration = 0.97,
            CartCalibration = 1,
            CombineTankWetKg = 8000,
            CombineTankMoisture = 0.20,
            TransferredToBinDryKg = 30000,
            Carts = new List<ScaleTicket>
            {
                new() { WetKg = 40000, Moisture = 0.185 },
                new() { WetKg = 35000, Moisture = 0.19 },
            },
            Bins = new List<BinLot> { new() { DryKg = 70000, CapacityKg = 100000 } },
            Contracts = new List<GrainContract> { new() { CommittedDryKg = 25000, DeliveredDryKg = 5000 } },
        };
        var report = GrainLedger.Reconcile(inputs, 100000, 0.20);
        double yieldDry = GrainLedger.ApplyCalibration(GrainLedger.DryMassKg(100000, 0.20, 0.155), 0.97);
        Assert.That(report.YieldMapAdjustedDryKg, Is.EqualTo(yieldDry).Within(1e-6));
        Assert.That(report.BinDryKg, Is.EqualTo(70000));
        Assert.That(report.YieldEqualsBins, Is.False);
        Assert.That(report.YieldMapAdjustedDryKg, Is.Not.EqualTo(report.BinDryKg));
        Assert.That(report.RemainingCommitmentDryKg, Is.EqualTo(20000));
        Assert.That(report.MarketableDryKg, Is.EqualTo(InventoryMath.MarketableKg(70000, 20000)));
        Assert.That(report.YieldMinusAccountedKg, Is.Not.EqualTo(0).Within(1));
    }

    [Test]
    public void Inventory_AvailableIsOnHandMinusCommitted()
    {
        Assert.That(InventoryMath.Available(180, 40), Is.EqualTo(140));
        Assert.That(InventoryMath.Available(100, 140), Is.EqualTo(-40));
        var row = InventoryMath.Project(new StockItem
        {
            Sku = "herb", Kind = "chemical", Name = "Burndown",
            OnHand = 180, Committed = 40, PlannedNeed = 200, Unit = "kg",
        });
        Assert.That(row.Available, Is.EqualTo(140));
        Assert.That(row.Refill, Is.True);
        Assert.That(InventoryMath.MarketableKg(70000, 20000), Is.EqualTo(50000));
    }

    [Test]
    public void Roles_CannotCommand_AndCommandPgnsAreRefused()
    {
        foreach (var role in MachineRolePolicy.Matrix())
        {
            Assert.That(role.MayCommandSteer, Is.False, role.Role);
            Assert.That(role.MayCommandRate, Is.False, role.Role);
            Assert.That(role.MayCommandSections, Is.False, role.Role);
            Assert.That(role.MaySendPgn254, Is.False);
            Assert.That(role.MaySendPgn239, Is.False);
            Assert.That(role.MaySendPgn229, Is.False);
            Assert.That(role.MaySendPgn241, Is.False);
        }
        Assert.That(MachineRolePolicy.For(MachineRoles.ReadOnlyLogger).MayRecordGuidanceLines, Is.False);
        Assert.That(MachineRolePolicy.For(MachineRoles.ReadOnlyLogger).MayForwardLogs, Is.True);
        Assert.That(MachineRolePolicy.RefuseInbound(254, null), Does.Contain("254"));
        Assert.That(MachineRolePolicy.RefuseInbound(241, null), Does.Contain("241"));
        Assert.That(MachineRolePolicy.RefuseInbound(null, "gps-offset"), Does.Contain("Refused"));
        Assert.That(MachineRolePolicy.RefuseInbound(253, "observation"), Is.Null);
    }

    [Test]
    public void PrecisionPlanting_ReadsTheDocumentedRowShape_AndRejectsAPartialOne()
    {
        using var good = JsonDocument.Parse("""{"row":7,"spacingM":0.19,"downforceN":210,"singulation":0.98}""");
        Assert.That(PrecisionPlantingRows.TryRead(good.RootElement, out var row, out _), Is.True);
        Assert.That(row.Row, Is.EqualTo(7));
        Assert.That(row.SpacingM, Is.EqualTo(0.19));
        using var bad = JsonDocument.Parse("""{"row":7,"downforceN":210}""");
        Assert.That(PrecisionPlantingRows.TryRead(bad.RootElement, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("spacingM"));
    }

    [Test]
    public void IsoXmlImport_LeavesSourceBytes_AndWritesProvenance()
    {
        using var dir = new TempDir();
        string sourceDir = Path.Combine(dir.Path, "dataset");
        Directory.CreateDirectory(sourceDir);
        string xml = Path.Combine(sourceDir, "TASKDATA.XML");
        File.WriteAllText(xml, FarmFixture.SampleTaskData(), new UTF8Encoding(false));
        AssertNoAgOpenGpsFiles(sourceDir);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(xml)));
        var result = FarmGeometryImporter.ImportIsoXml(xml, Path.Combine(dir.Path, "Fields"), "Sample North", "unit test");
        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(xml))), Is.EqualTo(hash));
        Assert.That(new FileInfo(xml).Length, Is.EqualTo(result.ByteLength));
        Assert.That(File.Exists(result.ProvenancePath), Is.True);
        Assert.That(File.Exists(Path.Combine(result.FieldDirectory, "field.geojson")), Is.True);
        Assert.That(File.Exists(Path.Combine(result.FieldDirectory, "farm-record.json")), Is.True);
        string provenance = File.ReadAllText(result.ProvenancePath);
        Assert.That(provenance, Does.Contain(result.Sha256));
        Assert.That(provenance, Does.Contain("unit test"));
        Assert.That(Directory.GetFiles(sourceDir).Select(Path.GetFileName), Is.EqualTo(new[] { "TASKDATA.XML" }));
    }

    [Test]
    public void IsoXmlRoundTrip_MatchesBoundaryWithinLocalPlaneTolerance()
    {
        using var dir = new TempDir();
        var origin = new Wgs84(41.6, -93.7);
        var plane = new LocalPlane(origin, new SharedFieldProperties());
        var points = new List<Vec3>
        {
            new(-40, -30, 0), new(40, -30, 0), new(40, 30, 0), new(-40, 30, 0),
        };
        var boundary = new IsoXmlBoundary { FenceLine = points };
        string exportDir = Path.Combine(dir.Path, "export");
        Directory.CreateDirectory(exportDir);
        IsoXmlExporter.Export(
            exportDir, "Round Trip", 4800,
            new List<IsoXmlBoundary> { boundary },
            new List<List<Vec3>>(),
            new List<IsoXmlTrack>(),
            plane, IsoXmlExporter.IsoXmlVersion.V4, FarmGeometryImporter.ImporterVersion);
        string xml = Directory.EnumerateFiles(exportDir).Single(f =>
            Path.GetFileName(f).Equals("TASKDATA.XML", StringComparison.OrdinalIgnoreCase));
        AssertNoAgOpenGpsFiles(exportDir);
        byte[] before = File.ReadAllBytes(xml);
        var result = FarmGeometryImporter.ImportIsoXml(xml, Path.Combine(dir.Path, "Fields"), "Round Trip", "round trip");
        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(File.ReadAllBytes(xml), Is.EqualTo(before));

        var loaded = GeoJsonFieldService.Load(result.FieldDirectory);
        Assert.That(loaded.field.Boundary?.OuterBoundary, Is.Not.Null);
        var back = new LocalPlane(
            new Wgs84(loaded.field.Origin.Latitude, loaded.field.Origin.Longitude),
            new SharedFieldProperties());
        var actual = loaded.field.Boundary!.OuterBoundary!.Points;
        Assert.That(actual, Has.Count.EqualTo(points.Count));
        for (int i = 0; i < points.Count; i++)
        {
            var wgs = back.ConvertGeoCoordToWgs84(new GeoCoord(actual[i].Northing, actual[i].Easting));
            var local = plane.ConvertWgs84ToGeoCoord(wgs);
            double dE = local.Easting - points[i].Easting;
            double dN = local.Northing - points[i].Northing;
            double metres = Math.Sqrt(dE * dE + dN * dN);
            Assert.That(metres, Is.LessThan(FarmGeometryImporter.LocalPlaneRoundTripToleranceMeters),
                $"vertex {i} is {metres:0.000} m off; tolerance is {FarmGeometryImporter.LocalPlaneRoundTripToleranceMeters} m on the original LocalPlane");
        }
    }

    [Test]
    public void KmlImport_LeavesSourceBytes()
    {
        using var dir = new TempDir();
        string kml = Path.Combine(dir.Path, "edge.kml");
        File.WriteAllText(kml, FarmFixture.SampleKml(), new UTF8Encoding(false));
        AssertNoAgOpenGpsFiles(dir.Path);
        byte[] before = File.ReadAllBytes(kml);
        var result = FarmGeometryImporter.ImportKml(kml, Path.Combine(dir.Path, "Fields"), "KML edge", "kml test");
        Assert.That(result.Ok, Is.True, result.Error);
        Assert.That(File.ReadAllBytes(kml), Is.EqualTo(before));
        var loaded = GeoJsonFieldService.Load(result.FieldDirectory);
        Assert.That(loaded.field.Boundary?.OuterBoundary?.Points, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(File.Exists(kml), Is.True);
    }

    [Test]
    public void AgOpenGpsFolder_IsRefused_AndLeftInPlace()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Field.txt"), "not imported");
        File.WriteAllText(Path.Combine(dir.Path, "TASKDATA.XML"), FarmFixture.SampleTaskData());
        byte[] before = File.ReadAllBytes(Path.Combine(dir.Path, "TASKDATA.XML"));
        var result = FarmGeometryImporter.ImportIsoXml(
            Path.Combine(dir.Path, "TASKDATA.XML"), Path.Combine(dir.Path, "Fields"), "Nope", "should refuse");
        Assert.That(result.Ok, Is.False);
        Assert.That(File.ReadAllText(Path.Combine(dir.Path, "Field.txt")), Is.EqualTo("not imported"));
        Assert.That(File.ReadAllBytes(Path.Combine(dir.Path, "TASKDATA.XML")), Is.EqualTo(before));
        Assert.That(Directory.Exists(Path.Combine(dir.Path, "Fields")), Is.False);
    }

    private static void AssertNoAgOpenGpsFiles(string directory)
    {
        foreach (var name in new[] { "Field.txt", "Boundary.txt", "TrackLines.txt" })
            Assert.That(File.Exists(Path.Combine(directory, name)), Is.False, name + " would make this an AgOpenGPS folder");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aow-farm-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
