// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// On-hand minus committed. Available is allowed to go negative when a job is
/// promised more than the shed holds. Nothing in here places an order.
/// </summary>
public static class InventoryMath
{
    public sealed class Row
    {
        public string Sku { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Name { get; init; } = "";
        public double OnHand { get; init; }
        public double Committed { get; init; }
        public double Available { get; init; }
        public double PlannedNeed { get; init; }
        public bool Refill { get; init; }
        public string Unit { get; init; } = "";
        public string Note { get; init; } = "";
    }

    public static double Available(double onHand, double committed) => onHand - committed;

    /// <summary>Grain that can still be offered. Delivered contract quantity is already out of the bin.</summary>
    public static double MarketableKg(double binDryKg, double remainingCommitmentKg) =>
        binDryKg - remainingCommitmentKg;

    public static Row Project(StockItem item)
    {
        double available = Available(item.OnHand, item.Committed);
        bool refill = available < item.PlannedNeed;
        string note = item.Note;
        if (refill)
            note = string.IsNullOrEmpty(note)
                ? "Available is under the planned need. This is a suggestion, not an order."
                : note + " Available is under the planned need. This is a suggestion, not an order.";
        return new Row
        {
            Sku = item.Sku,
            Kind = item.Kind,
            Name = item.Name,
            OnHand = item.OnHand,
            Committed = item.Committed,
            Available = available,
            PlannedNeed = item.PlannedNeed,
            Refill = refill,
            Unit = item.Unit,
            Note = note,
        };
    }
}
