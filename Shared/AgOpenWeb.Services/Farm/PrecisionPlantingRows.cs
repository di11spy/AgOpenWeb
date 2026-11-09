// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// Extension point for a Precision Planting style row record. The monitor and the
/// modules on this farm are still unnamed. Callers hand this a JSON object with
/// row, spacingM (metres), downforceN (newtons) and singulation (fraction).
/// There is no decoder for a proprietary stream.
/// </summary>
public static class PrecisionPlantingRows
{
    public static bool TryRead(JsonElement element, out PlantingRow row, out string error)
    {
        row = new PlantingRow();
        error = "";
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "A row record is a JSON object.";
            return false;
        }
        if (!TryDouble(element, "row", out double rowNo) || rowNo < 1)
        {
            error = "row is required and starts at 1.";
            return false;
        }
        if (!TryDouble(element, "spacingM", out double spacing) || spacing <= 0)
        {
            error = "spacingM (metres) is required.";
            return false;
        }
        if (!TryDouble(element, "downforceN", out double force))
        {
            error = "downforceN (newtons) is required.";
            return false;
        }
        if (!TryDouble(element, "singulation", out double sing) || sing < 0 || sing > 1)
        {
            error = "singulation is a fraction from 0 to 1.";
            return false;
        }
        row.Row = (int)rowNo;
        row.SpacingM = spacing;
        row.DownforceN = force;
        row.Singulation = sing;
        row.Source = "json-row-record";
        return true;
    }

    private static bool TryDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Number) return prop.TryGetDouble(out value);
        if (prop.ValueKind == JsonValueKind.String)
            return double.TryParse(prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return false;
    }
}
