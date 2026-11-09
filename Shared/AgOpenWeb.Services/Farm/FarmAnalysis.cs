// AgOpenWeb
// Copyright (C) 2024-2026 AgOpenWeb Contributors
// SPDX-License-Identifier: Apache-2.0

using AgOpenWeb.Models;
using AgOpenWeb.Models.Farm;

namespace AgOpenWeb.Services.Farm;

/// <summary>
/// Office arithmetic that does not touch guidance: rings, haul, work windows,
/// harvest order, rock pickup order, costs, and the local assistant cards.
/// </summary>
public static class FarmAnalysis
{
    /// <summary>International acre, for a display conversion from square metres.</summary>
    public const double SquareMetresPerAcre = 4046.8564224;

    public const double DryerKgPerHour = 12000;
    public const double RoadSpeedMps = 15;

    public static double AreaM2(IReadOnlyList<double[]> lonLat)
    {
        if (lonLat.Count < 3) return 0;
        double lat = 0, lon = 0;
        foreach (var p in lonLat) { lon += p[0]; lat += p[1]; }
        lat /= lonLat.Count;
        lon /= lonLat.Count;
        var plane = new LocalPlane(new Wgs84(lat, lon), new SharedFieldProperties());
        var ring = new List<(double E, double N)>(lonLat.Count);
        foreach (var p in lonLat)
        {
            var c = plane.ConvertWgs84ToGeoCoord(new Wgs84(p[1], p[0]));
            ring.Add((c.Easting, c.Northing));
        }
        double sum = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            sum += (ring[j].E + ring[i].E) * (ring[j].N - ring[i].N);
        return Math.Abs(sum / 2.0);
    }

    public static double Metres(double lon1, double lat1, double lon2, double lat2)
    {
        var a = new Wgs84(lat1, lon1);
        return a.DistanceInMeters(new Wgs84(lat2, lon2));
    }

    public static (double Lon, double Lat) Centroid(IReadOnlyList<double[]> lonLat)
    {
        if (lonLat.Count == 0) return (0, 0);
        double lon = 0, lat = 0;
        foreach (var p in lonLat) { lon += p[0]; lat += p[1]; }
        return (lon / lonLat.Count, lat / lonLat.Count);
    }

    public sealed class Haul
    {
        public string FieldId { get; init; } = "";
        public string DestinationId { get; init; } = "";
        public double Metres { get; init; }
        public double Seconds { get; init; }
        public double DryingUsdPerKg { get; init; }
        public double HaulUsdPerKg { get; init; }
        public double NetUsdPerKg { get; init; }
        public string Note { get; init; } = "Straight-line estimate, not a road route.";
    }

    public static List<Haul> Hauls(FarmBook book, double loadKg, double moisture)
    {
        var list = new List<Haul>();
        if (loadKg <= 0) loadKg = 25000;
        foreach (var field in book.Fields)
        {
            var (lon, lat) = Centroid(field.Boundary);
            foreach (var dest in book.Destinations)
            {
                double metres = Metres(lon, lat, dest.Lon, dest.Lat);
                double points = Math.Max(0, (moisture - 0.155) / 0.01);
                double drying = points * dest.DryingUsdPerMoisturePoint;
                double haul = dest.HaulUsdPerMetre * metres * 2.0 / loadKg;
                list.Add(new Haul
                {
                    FieldId = field.Id,
                    DestinationId = dest.Id,
                    Metres = metres,
                    Seconds = metres / RoadSpeedMps,
                    DryingUsdPerKg = drying,
                    HaulUsdPerKg = haul,
                    NetUsdPerKg = dest.BidUsdPerKg - drying - haul,
                });
            }
        }
        return list;
    }

    public sealed class HourWindow
    {
        public string When { get; init; } = "";
        public bool Spray { get; init; }
        public bool Plant { get; init; }
        public bool Tillage { get; init; }
        public string SprayWhy { get; init; } = "";
        public string PlantWhy { get; init; } = "";
        public string TillageWhy { get; init; } = "";
    }

    public static List<HourWindow> Windows(WeatherBook weather)
    {
        var list = new List<HourWindow>();
        foreach (var hour in weather.Hours)
        {
            var spray = new List<string>();
            if (hour.WindMps > 4.5) spray.Add($"wind {hour.WindMps:0.0} m/s is over 4.5");
            if (hour.RainProbability > 0.2) spray.Add($"rain probability {hour.RainProbability:0.00} is over 0.20");
            if (hour.AirK < 278 || hour.AirK > 303) spray.Add($"air {hour.AirK:0.0} K is outside 278–303");
            if (hour.SoilWater >= 0.42) spray.Add($"soil water {hour.SoilWater:0.00} is at the wet limit");

            var plant = new List<string>();
            if (hour.SoilK < 283.15) plant.Add($"soil {hour.SoilK:0.0} K is under 283.15 (10 °C)");
            if (hour.RainProbability > 0.4) plant.Add($"rain probability {hour.RainProbability:0.00} is over 0.40");
            if (hour.SoilWater >= 0.45) plant.Add($"soil water {hour.SoilWater:0.00} is too wet to plant");

            var till = new List<string>();
            if (hour.SoilWater >= 0.38) till.Add($"soil water {hour.SoilWater:0.00} is over 0.38");
            if (hour.RainProbability > 0.3) till.Add($"rain probability {hour.RainProbability:0.00} is over 0.30");

            list.Add(new HourWindow
            {
                When = hour.When,
                Spray = spray.Count == 0,
                Plant = plant.Count == 0,
                Tillage = till.Count == 0,
                SprayWhy = spray.Count == 0 ? "Within the spray limits used here." : string.Join("; ", spray),
                PlantWhy = plant.Count == 0 ? "Within the planting limits used here." : string.Join("; ", plant),
                TillageWhy = till.Count == 0 ? "Within the tillage limits used here." : string.Join("; ", till),
            });
        }
        return list;
    }

    public sealed class HarvestRow
    {
        public string FieldId { get; init; } = "";
        public string FieldName { get; init; } = "";
        public double Moisture { get; init; }
        public double UnharvestedDryKg { get; init; }
        public double DryerHours { get; init; }
        public string Decision { get; set; } = "";
        public int Order { get; set; }
    }

    public static List<HarvestRow> Harvest(FarmBook book, GrainLedger.Report grain)
    {
        var rows = new List<HarvestRow>();
        foreach (var field in book.Fields)
        {
            double wet = field.Yield.Sum(c => c.WetKg) * field.UnharvestedFraction;
            double dry = GrainLedger.DryMassKg(wet, field.HarvestMoisture, book.Grain.TargetMoisture);
            string decision;
            if (field.HarvestMoisture > 0.22)
                decision = "Hold. Moisture is above the 0.22 kg/kg band this dryer schedule treats as inefficient.";
            else if (field.UnharvestedFraction <= 0.02)
                decision = "Done for this fixture. Almost nothing is left standing.";
            else
                decision = "Cut while bin space remains. Drier fields go first so the dryer is not the queue.";
            rows.Add(new HarvestRow
            {
                FieldId = field.Id,
                FieldName = field.Name,
                Moisture = field.HarvestMoisture,
                UnharvestedDryKg = dry,
                DryerHours = dry / DryerKgPerHour,
                Decision = decision,
            });
        }

        var ordered = rows
            .OrderBy(r => r.Decision.StartsWith("Hold", StringComparison.Ordinal) ? 2 : r.Decision.StartsWith("Done", StringComparison.Ordinal) ? 3 : 1)
            .ThenBy(r => r.Moisture)
            .ToList();
        double free = Math.Max(0, grain.BinFreeKg);
        int n = 1;
        foreach (var row in ordered)
        {
            row.Order = n++;
            if (row.Decision.StartsWith("Cut", StringComparison.Ordinal))
            {
                if (row.UnharvestedDryKg > free)
                    row.Decision += $" Bin free space ({free:0} kg) does not cover this field ({row.UnharvestedDryKg:0} kg dry). This is the critical stop.";
                free -= row.UnharvestedDryKg;
                if (free < 0) free = 0;
            }
        }
        return ordered;
    }

    public sealed class RockRoute
    {
        public string FieldId { get; init; } = "";
        public List<string> Order { get; init; } = new();
        public double Metres { get; init; }
        public string Note { get; init; } =
            "Nearest-neighbour order from the field entrance, for a side-by-side pickup. A heuristic, not an optimized route, and not a command to a vehicle.";
    }

    public static List<RockRoute> RockRoutes(FarmBook book)
    {
        var list = new List<RockRoute>();
        foreach (var field in book.Fields)
        {
            if (field.Rocks.Count == 0) continue;
            var entrance = field.Places.FirstOrDefault(p => p.Kind == "entrance");
            double lon = entrance?.Lon ?? field.OriginLonLat[0];
            double lat = entrance?.Lat ?? field.OriginLonLat[1];
            var left = field.Rocks.ToList();
            var order = new List<string>();
            double travelled = 0;
            while (left.Count > 0)
            {
                RockMark best = left[0];
                double bestM = double.MaxValue;
                foreach (var rock in left)
                {
                    double m = Metres(lon, lat, rock.Lon, rock.Lat);
                    if (m < bestM) { bestM = m; best = rock; }
                }
                travelled += bestM;
                order.Add(best.Id);
                lon = best.Lon;
                lat = best.Lat;
                left.Remove(best);
            }
            list.Add(new RockRoute { FieldId = field.Id, Order = order, Metres = travelled });
        }
        return list;
    }

    public sealed class CostRow
    {
        public string FieldId { get; init; } = "";
        public string FieldName { get; init; } = "";
        public double Usd { get; init; }
        public double AreaM2 { get; init; }
        public double UsdPerM2 { get; init; }
        public double UsdPerHectare { get; init; }
        public double UsdPerAcre { get; init; }
        public double DowntimeHours { get; init; }
        public double DowntimeUsd { get; init; }
    }

    public static List<CostRow> Costs(FarmBook book)
    {
        double downHours = book.Downtime.Sum(d => d.Hours);
        double downUsd = book.Downtime.Sum(d => d.CostUsd);
        var list = new List<CostRow>();
        foreach (var field in book.Fields)
        {
            var c = field.Costs;
            double usd = c.SeedUsd + c.FertilizerUsd + c.ChemicalUsd + c.FuelUsd + c.LaborUsd;
            double area = field.Hectares * 10000.0;
            if (area < 1) area = AreaM2(field.Boundary);
            list.Add(new CostRow
            {
                FieldId = field.Id,
                FieldName = field.Name,
                Usd = usd,
                AreaM2 = area,
                UsdPerM2 = area > 0 ? usd / area : 0,
                UsdPerHectare = area > 0 ? usd / (area / 10000.0) : 0,
                UsdPerAcre = area > 0 ? usd / (area / SquareMetresPerAcre) : 0,
                DowntimeHours = downHours,
                DowntimeUsd = downUsd,
            });
        }
        return list;
    }

    public sealed class MarketScenario
    {
        public string Name { get; init; } = "";
        public double FractionOfMarketable { get; init; }
        public double Kg { get; init; }
        public double Usd { get; init; }
        public double PriceUsdPerKg { get; init; }
        public string Note { get; init; } = "Analysis only. This does not sell grain or place an order.";
    }

    public static List<MarketScenario> Scenarios(FarmBook book, GrainLedger.Report grain)
    {
        double price = book.Market.Points.Count == 0 ? 0 : book.Market.Points[^1].UsdPerKg;
        double marketable = Math.Max(0, grain.MarketableDryKg);
        var list = new List<MarketScenario>();
        foreach (var (name, fraction) in new[] { ("Sell a quarter of what is free", 0.25), ("Sell half of what is free", 0.5), ("Sell all that is free", 1.0) })
        {
            double kg = marketable * fraction;
            list.Add(new MarketScenario
            {
                Name = name,
                FractionOfMarketable = fraction,
                Kg = kg,
                Usd = kg * price,
                PriceUsdPerKg = price,
                Note = $"Uses {marketable:0} kg marketable. Remaining contract commitment ({grain.RemainingCommitmentDryKg:0} kg) is excluded. Last sample price {price:0.0000} USD/kg. Not an order.",
            });
        }
        return list;
    }

    public sealed class AssistantCard
    {
        public string Title { get; init; } = "";
        public string Recommendation { get; init; } = "";
        public List<string> Evidence { get; init; } = new();
        public string Uncertainty { get; init; } = "";
        public string WillNot { get; init; } = "Does not control a machine, send a command, or purchase anything.";
    }

    public static List<AssistantCard> Assistant(FarmBook book, GrainLedger.Report grain, List<HourWindow> windows, List<HarvestRow> harvest, List<Haul> hauls)
    {
        var cards = new List<AssistantCard>();
        var lowFuel = book.Machines.Where(m => m.FuelFraction < 0.25 && m.Link != "offline").ToList();
        if (lowFuel.Count > 0)
        {
            var m = lowFuel[0];
            double hoursLeft = m.FuelBurnKgPerHour <= 0 ? 0 : (m.FuelFraction * 400.0) / m.FuelBurnKgPerHour;
            cards.Add(new AssistantCard
            {
                Title = "Fuel",
                Recommendation = $"{m.Name} is under a quarter of a tank on the fixture gauge. Refuel before the next long pass if that gauge is the one you trust.",
                Evidence = new List<string>
                {
                    $"Fuel fraction {m.FuelFraction:0.00} (fixture gauge).",
                    $"Burn {m.FuelBurnKgPerHour:0.0} kg/h and a 400 kg tank assumption → about {hoursLeft:0.0} h.",
                },
                Uncertainty = "The tank capacity and the burn rate are fixture assumptions, not a sensor calibration. Treat the hour figure as ±30%.",
            });
        }

        var truck = book.Trucks.FirstOrDefault(t => t.State == "enroute");
        if (truck != null)
        {
            double remain = (1.0 - truck.Progress) * 12000.0;
            double seconds = truck.SpeedMps <= 0 ? 0 : remain / truck.SpeedMps;
            cards.Add(new AssistantCard
            {
                Title = "Truck turnaround",
                Recommendation = $"{truck.Name} is en route. At the fixture road speed the rest of this leg is about {seconds / 60.0:0} minutes, then add the unload.",
                Evidence = new List<string>
                {
                    $"Progress {truck.Progress:0.00} on a 12 km stand-in leg.",
                    $"Speed {truck.SpeedMps:0.0} m/s. Unload allowance used elsewhere is 20 minutes and is not measured.",
                },
                Uncertainty = "Straight-line stand-in, not a road network. A train, a scale line, or a detour is not in this number.",
            });
        }

        var stop = harvest.FirstOrDefault(h => h.Decision.Contains("critical stop", StringComparison.OrdinalIgnoreCase));
        cards.Add(new AssistantCard
        {
            Title = "Bottleneck",
            Recommendation = stop == null
                ? "Bin space covers the unharvested dry estimate in this fixture. The dryer, not the bin, is the slower step if everything is cut today."
                : $"Bin space runs out on {stop.FieldName}. That is the critical stop in this schedule, ahead of combine capacity.",
            Evidence = new List<string>
            {
                $"Bin free {grain.BinFreeKg:0} kg dry. Capacity {grain.BinCapacityKg:0} kg.",
                $"Dryer rate used for the schedule: {DryerKgPerHour:0} kg/h dry. That rate is a fixture.",
                $"Yield-map dry {grain.YieldMapAdjustedDryKg:0} kg is not the same number as bin inventory {grain.BinDryKg:0} kg.",
            },
            Uncertainty = "Moisture, the unharvested fraction, and the dryer rate are all fixture inputs. A point of moisture moves the dry mass.",
        });

        var spray = windows.FirstOrDefault(w => w.Spray);
        cards.Add(new AssistantCard
        {
            Title = "Next spray window",
            Recommendation = spray == null
                ? "No hour in the fixture series passes the spray limits."
                : $"The first hour that passes the spray limits is {spray.When}.",
            Evidence = spray == null
                ? windows.Take(3).Select(w => $"{w.When}: {w.SprayWhy}").ToList()
                : new List<string> { spray.SprayWhy, "Limits: wind ≤ 4.5 m/s, rain probability ≤ 0.20, air 278–303 K, soil water < 0.42." },
            Uncertainty = "The series is a labeled fixture, not a forecast subscription. A real wind shift invalidates it.",
        });

        if (hauls.Count > 0)
        {
            var best = hauls.OrderByDescending(h => h.NetUsdPerKg).First();
            var dest = book.Destinations.First(d => d.Id == best.DestinationId);
            var field = book.Fields.First(f => f.Id == best.FieldId);
            cards.Add(new AssistantCard
            {
                Title = "Haul comparison",
                Recommendation = $"On these sample bids, {dest.Name} nets the most per kilogram from {field.Name} after haul and drying. This is not a sale.",
                Evidence = new List<string>
                {
                    $"Bid {dest.BidUsdPerKg:0.0000} USD/kg ({dest.BidSource}).",
                    $"Straight-line {best.Metres:0} m, about {best.Seconds / 60.0:0} min one way at {RoadSpeedMps:0} m/s.",
                    $"Drying {best.DryingUsdPerKg:0.0000} USD/kg, haul {best.HaulUsdPerKg:0.0000} USD/kg, net {best.NetUsdPerKg:0.0000} USD/kg.",
                },
                Uncertainty = "Bids are fixtures. The route ignores roads. Drying uses 0.155 kg/kg as the target and the field's harvest moisture.",
            });
        }

        cards.Add(new AssistantCard
        {
            Title = "What this panel will not do",
            Recommendation = "Local rules only, unless a later build is given a key on purpose. This build does not call a model and does not read a secret.",
            Evidence = new List<string>
            {
                book.LiveLlmEnabled
                    ? "The live-model flag is set and is still ignored. No request leaves the machine."
                    : "Live model calls are off. No API key is required to read these cards.",
            },
            Uncertainty = "A rule can be wrong when the fixture does not match the farm. The evidence is on the card so you can throw the card out.",
        });
        return cards;
    }

    public static string EvenFillSuggestion(double left, double right)
    {
        double delta = left - right;
        if (Math.Abs(delta) < 0.08)
            return "The two sides are within 0.08 of each other. Either side is fine on this plan.";
        return delta > 0
            ? "The left side of the cart is heavier. The plan says approach so the next unload lands on the right. The cart is not moved by this screen."
            : "The right side of the cart is heavier. The plan says approach so the next unload lands on the left. The cart is not moved by this screen.";
    }
}
