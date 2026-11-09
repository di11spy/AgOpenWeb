// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

namespace AgOpenWeb.Models.Farm;

/// <summary>
/// A measured value with an explicit unit. Rates and lab results in the farm office
/// are stored this way. Mass is kilograms, length metres, temperature kelvin, moisture
/// a mass fraction (kg water / kg grain), and a rate carries the compound unit in
/// <see cref="Unit"/> (for example <c>kg/m2</c>). Currency is tagged <c>USD</c> and is
/// a ledger amount, not a purchase.
/// </summary>
public sealed class SiValue
{
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string What { get; set; } = "";

    public SiValue() { }

    public SiValue(double value, string unit, string what)
    {
        Value = value;
        Unit = unit;
        What = what;
    }
}

/// <summary>
/// The farm-office book. It is not <c>field.geojson</c> and not <c>job.json</c>.
/// Guidance keeps its own files. This book is the season layer that lines those
/// fields up with yield, soil, operations, and the rest of the office.
/// </summary>
public sealed class FarmBook
{
    public int SchemaVersion { get; set; } = 1;
    public FarmIdentity Identity { get; set; } = new();
    public List<FieldPlot> Fields { get; set; } = new();
    public List<MachineNode> Machines { get; set; } = new();
    public List<TruckLeg> Trucks { get; set; } = new();
    public GrainInputs Grain { get; set; } = new();
    public List<StockItem> Inventory { get; set; } = new();
    public List<MaintenanceWatch> Maintenance { get; set; } = new();
    public List<DowntimeSpan> Downtime { get; set; } = new();
    public WeatherBook Weather { get; set; } = new();
    public List<Destination> Destinations { get; set; } = new();
    public MarketBook Market { get; set; } = new();
    public CommsBook Comms { get; set; } = new();
    public EvenFillPlan EvenFill { get; set; } = new();
    public List<Observation> Telemetry { get; set; } = new();
    public List<RefusalNote> Refusals { get; set; } = new();
    public List<PlantingRow> PrecisionPlantingRows { get; set; } = new();
    public string PrecisionPlantingNote { get; set; } = PrecisionPlantingIntake.Note;
    public bool LiveLlmEnabled { get; set; }
    public DateTime ReplayUtc { get; set; } = DateTime.UnixEpoch;
}

public static class PrecisionPlantingIntake
{
    public const string Note =
        "Precision Planting is named here and the monitor and row modules are still unnamed. " +
        "This intake reads a documented JSON row record (row, spacingM, downforceN, singulation). " +
        "It is not a decoder for an unpublished proprietary stream, and it does not claim one.";

    public static readonly string[] Fields = { "row", "spacingM", "downforceN", "singulation" };
}

public sealed class FarmIdentity
{
    public string Name { get; set; } = "North Ridge (sample)";
    public string Season { get; set; } = "2026 harvest";
    public string DataLabel { get; set; } = "sample-fixture";
    public string LabelNote { get; set; } =
        "SAMPLE / FIXTURE. Positions, yields, bids, weather, photos, and cameras are labeled sample data. " +
        "They are not a live OEM export and not a quote.";
}

public sealed class FieldPlot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CropYear { get; set; } = "2026";
    public string Crop { get; set; } = "";
    public string Provenance { get; set; } = "sample-fixture";
    /// <summary>Set when a non-destructive import wrote a guidance field folder. Empty for fixtures.</summary>
    public string GuidanceFolder { get; set; } = "";
    public string ProvenanceFile { get; set; } = "";
    /// <summary>Field origin as longitude, latitude. Layers share this boundary. Do not move it.</summary>
    public double[] OriginLonLat { get; set; } = new double[2];
    /// <summary>Outer ring, longitude then latitude, not closed. Headings are not stored here.</summary>
    public List<double[]> Boundary { get; set; } = new();
    public double Hectares { get; set; }
    public List<YieldCell> Yield { get; set; } = new();
    public List<SoilPoint> Soil { get; set; } = new();
    public List<ZonePoly> Zones { get; set; } = new();
    public List<FieldOp> Operations { get; set; } = new();
    public List<PlaceMark> Places { get; set; } = new();
    public List<RockMark> Rocks { get; set; } = new();
    public GprNote Gpr { get; set; } = new();
    public List<ScoutMark> Scouting { get; set; } = new();
    public List<NitrogenCell> Nitrogen { get; set; } = new();
    public StripPlan Strips { get; set; } = new();
    public RainBook Rain { get; set; } = new();
    public CostBook Costs { get; set; } = new();
    /// <summary>Harvest moisture, kg/kg.</summary>
    public double HarvestMoisture { get; set; }
    public double UnharvestedFraction { get; set; }
}

public sealed class YieldCell
{
    public double Lon { get; set; }
    public double Lat { get; set; }
    public double WetKg { get; set; }
    public double AreaM2 { get; set; }
    /// <summary>kg/kg.</summary>
    public double Moisture { get; set; }
    public string Source { get; set; } = "sample-fixture";
}

public sealed class SoilPoint
{
    public string Id { get; set; } = "";
    public double Lon { get; set; }
    public double Lat { get; set; }
    public string Lab { get; set; } = "sample lab";
    public string Sampled { get; set; } = "2026-03-18";
    public SiValue Ph { get; set; } = new(0, "1", "pH");
    public SiValue Phosphorus { get; set; } = new(0, "mg/kg", "phosphorus");
    public SiValue Potassium { get; set; } = new(0, "mg/kg", "potassium");
    public SiValue OrganicMatter { get; set; } = new(0, "kg/kg", "organic matter");
}

public sealed class ZonePoly
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Management { get; set; } = "";
    public List<double[]> Ring { get; set; } = new();
}

public sealed class FieldOp
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = ""; // as-planted | as-applied
    public string When { get; set; } = "";
    public string Product { get; set; } = "";
    public SiValue Rate { get; set; } = new();
    public double Hectares { get; set; }
    public string MachineId { get; set; } = "";
    public string Source { get; set; } = "sample-fixture";
    public string Note { get; set; } = "";
}

public sealed class PlaceMark
{
    public string Kind { get; set; } = ""; // entrance | tile-inlet | wet | hazard
    public string Name { get; set; } = "";
    public double Lon { get; set; }
    public double Lat { get; set; }
    public string Note { get; set; } = "";
}

public sealed class RockMark
{
    public string Id { get; set; } = "";
    public double Lon { get; set; }
    public double Lat { get; set; }
    public string Note { get; set; } = "";
    public string Source { get; set; } = "cultivator-gps-fixture";
}

public sealed class GprNote
{
    public string Sensor { get; set; } = "gpr-on-cultivator";
    public string Status { get; set; } = "placeholder";
    public string Note { get; set; } =
        "No GPR hardware is connected. This is the record a cultivator-mounted GPR survey could fill later. " +
        "Nothing here recommends buying one.";
    public List<GprSample> Samples { get; set; } = new();
}

public sealed class GprSample
{
    public double Lon { get; set; }
    public double Lat { get; set; }
    public double DepthM { get; set; }
    public string ReturnClass { get; set; } = "unclassified-placeholder";
}

public sealed class ScoutMark
{
    public string Id { get; set; } = "";
    public string Flag { get; set; } = ""; // stand-gap | weeds | water | scout-needed
    public double Lon { get; set; }
    public double Lat { get; set; }
    public string Caption { get; set; } = "";
    public string When { get; set; } = "";
    public string Source { get; set; } = "drone-fixture — no image file";
}

public sealed class NitrogenCell
{
    public double Lon { get; set; }
    public double Lat { get; set; }
    /// <summary>0 to 1 sufficiency estimate. Not a nitrogen measurement.</summary>
    public double SufficiencyEstimate { get; set; }
    public string Source { get; set; } = "fixture imagery index";
}

public sealed class StripPlan
{
    public string Banner { get; set; } =
        "Planning/simulation only — not connected to machine control. " +
        "These lines are kept so next year's planting can repeat them. " +
        "No GPS offset is sent to a Case IH, Trimble, John Deere, or any other display.";
    public string RtkNote { get; set; } = "";
    public List<StripLine> Lines { get; set; } = new();
}

public sealed class StripLine
{
    public string Name { get; set; } = "";
    public double LonA { get; set; }
    public double LatA { get; set; }
    public double LonB { get; set; }
    public double LatB { get; set; }
    /// <summary>Radians, 0 = north, clockwise. Same sense as field.geojson headings.</summary>
    public double HeadingRadians { get; set; }
    public string SurveyedUtc { get; set; } = "";
    public string BaseId { get; set; } = "";
}

public sealed class RainBook
{
    public List<RainEvent> Storms { get; set; } = new();
    public List<RainDay> Days { get; set; } = new();
    public double SeasonGaugeM { get; set; }
    public double SeasonRadarM { get; set; }
}

public sealed class RainEvent
{
    public string When { get; set; } = "";
    public double GaugeM { get; set; }
    public double RadarM { get; set; }
    public string GaugeSource { get; set; } = "field rain gauge (fixture)";
    public string RadarSource { get; set; } = "radar estimate (fixture)";
}

public sealed class RainDay
{
    public string Day { get; set; } = "";
    public double GaugeM { get; set; }
    public double RadarM { get; set; }
}

public sealed class CostBook
{
    public double SeedUsd { get; set; }
    public double FertilizerUsd { get; set; }
    public double ChemicalUsd { get; set; }
    public double FuelUsd { get; set; }
    public double LaborUsd { get; set; }
    public string Note { get; set; } = "Ledger only. Nothing is purchased from this screen.";
}

public sealed class MachineNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Display { get; set; } = "";
    public string Software { get; set; } = "unnamed";
    public string Role { get; set; } = MachineRoles.ReadOnlyLogger;
    public string Operator { get; set; } = "";
    public double Lon { get; set; }
    public double Lat { get; set; }
    /// <summary>Radians, 0 = north, clockwise.</summary>
    public double HeadingRadians { get; set; }
    public double SpeedMps { get; set; }
    public string Link { get; set; } = "online"; // online | offline
    public bool HoldClock { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public int PendingRecords { get; set; }
    public string Camera { get; set; } = "placeholder";
    public string CameraNote { get; set; } =
        "No camera is connected. The frame on the fleet page is a labeled fixture, not a feed.";
    public double EngineHours { get; set; }
    public double FuelFraction { get; set; }
    public double FuelBurnKgPerHour { get; set; }
    public double PathPhase { get; set; }
    public List<double[]> Path { get; set; } = new();
}

public static class MachineRoles
{
    public const string ReadOnlyLogger = "read-only-logger";
    public const string ImplementGuidance = "implement-guidance";
    public const string FullGuidance = "full-guidance";
}

public sealed class TruckLeg
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = ""; // coming | going | enroute
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public double Lon { get; set; }
    public double Lat { get; set; }
    public double Progress { get; set; }
    public double LoadDryKg { get; set; }
    public string Crop { get; set; } = "";
    public double SpeedMps { get; set; } = 15;
    public string Note { get; set; } = "";
}

public sealed class GrainInputs
{
    public double TargetMoisture { get; set; } = 0.155;
    public double YieldCalibration { get; set; } = 1;
    public double CartCalibration { get; set; } = 1;
    public double CombineTankWetKg { get; set; }
    public double CombineTankMoisture { get; set; }
    public List<ScaleTicket> Carts { get; set; } = new();
    public List<BinLot> Bins { get; set; } = new();
    public List<GrainContract> Contracts { get; set; } = new();
    public double TransferredToBinDryKg { get; set; }
    public string Note { get; set; } =
        "The yield map, the cart scale, and the bin inventory are three ledgers. " +
        "They are not assumed to match.";
}

public sealed class ScaleTicket
{
    public string Id { get; set; } = "";
    public double WetKg { get; set; }
    public double Moisture { get; set; }
    public string When { get; set; } = "";
}

public sealed class BinLot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double DryKg { get; set; }
    public double CapacityKg { get; set; }
    public double Moisture { get; set; }
    public string Crop { get; set; } = "";
}

public sealed class GrainContract
{
    public string Id { get; set; } = "";
    public string Buyer { get; set; } = "";
    public double CommittedDryKg { get; set; }
    public double DeliveredDryKg { get; set; }
    public double PriceUsdPerKg { get; set; }
    public string Crop { get; set; } = "corn";
}

public sealed class StockItem
{
    public string Sku { get; set; } = "";
    public string Kind { get; set; } = ""; // seed | fertilizer | chemical | fuel
    public string Name { get; set; } = "";
    public double OnHand { get; set; }
    public double Committed { get; set; }
    public double PlannedNeed { get; set; }
    public string Unit { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class MaintenanceWatch
{
    public string MachineId { get; set; } = "";
    public string Item { get; set; } = "";
    public double Hours { get; set; }
    public double IntervalHours { get; set; }
    public string Trend { get; set; } = "";
    public string Severity { get; set; } = "watch"; // watch | soon | now
}

public sealed class DowntimeSpan
{
    public string MachineId { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Hours { get; set; }
    public double CostUsd { get; set; }
    public string When { get; set; } = "";
}

public sealed class WeatherBook
{
    public string Source { get; set; } = "sample-fixture hourly series — not a forecast feed";
    public List<WeatherHour> Hours { get; set; } = new();
}

public sealed class WeatherHour
{
    public string When { get; set; } = "";
    public double WindMps { get; set; }
    public double RainProbability { get; set; }
    public double AirK { get; set; }
    public double SoilK { get; set; }
    public double SoilWater { get; set; }
}

public sealed class Destination
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "elevator";
    public double Lon { get; set; }
    public double Lat { get; set; }
    public string Hours { get; set; } = "";
    public List<string> Crops { get; set; } = new();
    public double BidUsdPerKg { get; set; }
    public double DryingUsdPerMoisturePoint { get; set; }
    public double HaulUsdPerMetre { get; set; }
    public string BidSource { get; set; } = "sample bid, not a live quote";
}

public sealed class MarketBook
{
    public string Source { get; set; } =
        "SAMPLE series shaped like a public cash-price history. Not a live quote and not an order.";
    public string Crop { get; set; } = "corn";
    public List<MarketPoint> Points { get; set; } = new();
}

public sealed class MarketPoint
{
    public string Day { get; set; } = "";
    public double UsdPerKg { get; set; }
}

public sealed class CommsBook
{
    public string Note { get; set; } =
        "In-app messages and push-to-talk supplement the radios. They do not replace them. LAN first; nothing is sent off the farm.";
    public List<CommChannel> Channels { get; set; } = new();
    public List<CommPost> Posts { get; set; } = new();
}

public sealed class CommChannel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "group"; // group | direct
    public List<string> Members { get; set; } = new();
}

public sealed class CommPost
{
    public string Id { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string From { get; set; } = "";
    public string Kind { get; set; } = "text"; // text | ptt
    public string Text { get; set; } = "";
    public string? AudioBase64 { get; set; }
    public string? AudioType { get; set; }
    public DateTime Utc { get; set; }
}

public sealed class EvenFillPlan
{
    public string Banner { get; set; } =
        "Planning/simulation only — not connected to machine control. " +
        "The cart is not commanded from combine feedback.";
    public double CombineTankFraction { get; set; }
    public double CartLeft { get; set; }
    public double CartRight { get; set; }
    public string Suggestion { get; set; } = "";
}

public sealed class Observation
{
    public DateTime Utc { get; set; }
    public string MachineId { get; set; } = "agopen-host";
    public string Source { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? HeadingDegrees { get; set; }
    public double? HostSpeed { get; set; }
    public double? Pgn250SensorPercent { get; set; }
    public double? Pgn253SteerAngleDeg { get; set; }
    public double? Pgn211RollDegrees { get; set; }
    public string Note { get; set; } = "read-only log";
}

public sealed class RefusalNote
{
    public DateTime Utc { get; set; }
    public string What { get; set; } = "";
}

public sealed class PlantingRow
{
    public int Row { get; set; }
    public double SpacingM { get; set; }
    public double DownforceN { get; set; }
    public double Singulation { get; set; }
    public string Source { get; set; } = "fixture-json";
}
