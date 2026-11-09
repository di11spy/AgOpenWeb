// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// Moisture, calibration, and the three grain ledgers. Dry mass at a target moisture
/// (kg/kg) is <c>wet * (1 - moisture) / (1 - target)</c> when the grain is wetter than
/// the target. At or below the target, no water is invented and the wet mass is kept.
/// A calibration factor scales a measured mass (1 = the instrument matches the reference).
/// Yield-map dry kilograms are not copied into bin inventory.
/// </summary>
public static class GrainLedger
{
    public sealed class Report
    {
        public double TargetMoisture { get; init; }
        public double YieldMapWetKg { get; init; }
        public double YieldMapMoisture { get; init; }
        public double YieldCalibration { get; init; }
        public double YieldMapAdjustedDryKg { get; init; }
        public double CartScaleWetKg { get; init; }
        public double CartScaleDryKg { get; init; }
        public double CombineTankWetKg { get; init; }
        public double CombineTankDryKg { get; init; }
        public double AccountedOffCombineDryKg { get; init; }
        public double YieldMinusAccountedKg { get; init; }
        public double BinDryKg { get; init; }
        public double BinCapacityKg { get; init; }
        public double BinFreeKg { get; init; }
        public double TransferredToBinDryKg { get; init; }
        public double ContractedDryKg { get; init; }
        public double DeliveredOnContractDryKg { get; init; }
        public double RemainingCommitmentDryKg { get; init; }
        public double MarketableDryKg { get; init; }
        public bool YieldEqualsBins { get; init; }
        public string ResidualNote { get; init; } = "";
        public List<string> Steps { get; init; } = new();
    }

    public static double DryMassKg(double wetKg, double moisture, double targetMoisture)
    {
        if (wetKg < 0) throw new ArgumentOutOfRangeException(nameof(wetKg));
        CheckFraction(moisture, nameof(moisture));
        CheckFraction(targetMoisture, nameof(targetMoisture));
        if (moisture <= targetMoisture) return wetKg;
        return wetKg * (1.0 - moisture) / (1.0 - targetMoisture);
    }

    public static double ApplyCalibration(double measuredKg, double factor)
    {
        if (measuredKg < 0) throw new ArgumentOutOfRangeException(nameof(measuredKg));
        if (factor <= 0 || factor > 2)
            throw new ArgumentOutOfRangeException(nameof(factor), "Calibration must be in (0, 2].");
        return measuredKg * factor;
    }

    public static void CheckFraction(double value, string name)
    {
        if (value < 0 || value >= 1)
            throw new ArgumentOutOfRangeException(name, "Moisture must be a kg/kg fraction in [0, 1).");
    }

    public static Report Reconcile(GrainInputs inputs, double yieldMapWetKg, double yieldMapMoisture)
    {
        CheckFraction(inputs.TargetMoisture, nameof(inputs.TargetMoisture));
        CheckFraction(yieldMapMoisture, nameof(yieldMapMoisture));
        double yieldDry = ApplyCalibration(
            DryMassKg(yieldMapWetKg, yieldMapMoisture, inputs.TargetMoisture),
            inputs.YieldCalibration);

        double cartWet = 0, cartDry = 0;
        foreach (var ticket in inputs.Carts)
        {
            cartWet += ticket.WetKg;
            cartDry += ApplyCalibration(
                DryMassKg(ticket.WetKg, ticket.Moisture, inputs.TargetMoisture),
                inputs.CartCalibration);
        }

        double tankDry = DryMassKg(inputs.CombineTankWetKg, inputs.CombineTankMoisture, inputs.TargetMoisture);
        double accounted = cartDry + tankDry;
        double gap = yieldDry - accounted;

        double bin = 0, capacity = 0;
        foreach (var lot in inputs.Bins)
        {
            bin += lot.DryKg;
            capacity += lot.CapacityKg;
        }

        double committed = 0, delivered = 0;
        foreach (var contract in inputs.Contracts)
        {
            committed += contract.CommittedDryKg;
            delivered += contract.DeliveredDryKg;
        }
        double remaining = committed - delivered;
        double marketable = bin - remaining;

        var steps = new List<string>
        {
            $"Yield map {yieldMapWetKg:0} kg wet at moisture {yieldMapMoisture:0.000} kg/kg.",
            $"Shrink to {inputs.TargetMoisture:0.000} kg/kg, then calibration × {inputs.YieldCalibration:0.000} → {yieldDry:0} kg dry.",
            $"Cart tickets {cartWet:0} kg wet → {cartDry:0} kg dry after the same shrink and cart calibration × {inputs.CartCalibration:0.000}.",
            $"Combine tank still aboard {inputs.CombineTankWetKg:0} kg wet → {tankDry:0} kg dry.",
            $"Accounted off the combine (carts + tank) {accounted:0} kg dry. Yield map minus that is {gap:0} kg.",
            $"Bins hold {bin:0} kg dry of {capacity:0} kg capacity. That figure is inventory, not the yield map.",
            $"Transfers into bins today {inputs.TransferredToBinDryKg:0} kg dry are already inside the bin figure.",
            $"Contracts commit {committed:0} kg, of which {delivered:0} kg is delivered. Remaining commitment {remaining:0} kg.",
            $"Marketable grain is bin inventory minus the remaining commitment: {marketable:0} kg. Committed grain is not offered.",
        };

        return new Report
        {
            TargetMoisture = inputs.TargetMoisture,
            YieldMapWetKg = yieldMapWetKg,
            YieldMapMoisture = yieldMapMoisture,
            YieldCalibration = inputs.YieldCalibration,
            YieldMapAdjustedDryKg = yieldDry,
            CartScaleWetKg = cartWet,
            CartScaleDryKg = cartDry,
            CombineTankWetKg = inputs.CombineTankWetKg,
            CombineTankDryKg = tankDry,
            AccountedOffCombineDryKg = accounted,
            YieldMinusAccountedKg = gap,
            BinDryKg = bin,
            BinCapacityKg = capacity,
            BinFreeKg = capacity - bin,
            TransferredToBinDryKg = inputs.TransferredToBinDryKg,
            ContractedDryKg = committed,
            DeliveredOnContractDryKg = delivered,
            RemainingCommitmentDryKg = remaining,
            MarketableDryKg = marketable,
            YieldEqualsBins = false,
            ResidualNote =
                "The residual is expected. Unharvested area, a monitor that does not match the scale, " +
                "grain still in the tank, and grain from other days in the bins are different quantities.",
            Steps = steps,
        };
    }

    public static (double WetKg, double Moisture) YieldTotals(IEnumerable<FieldPlot> fields)
    {
        double wet = 0, water = 0;
        foreach (var field in fields)
        {
            foreach (var cell in field.Yield)
            {
                wet += cell.WetKg;
                water += cell.WetKg * cell.Moisture;
            }
        }
        return (wet, wet > 0 ? water / wet : 0);
    }
}
