using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>Spawner groups, as EA's worlds sort them into World Layers (Riverview: FishSpawners, SeedSpawners...).</summary>
    public enum SpawnGroup { FishFresh, FishOcean, Seeds, Gems, Metals, Meteorites, Butterflies, Bugs, PetAnimals, Other }

    internal sealed class SpawnPlacement
    {
        public int Item;            // index into the item list
        public float MapX, MapY;    // map-pixel coordinates (full map)
        public float Yaw;
        public bool OnWater;        // sits on the water surface (fishing spots)
    }

    /// <summary>
    /// Where spawners go, from EA's worlds (Riverview's layer files against its heightmap, 2026-10-20): land spawners
    /// (seeds, gems, metals, meteorites, butterflies, bugs) sit on the ground on gentle slopes (median 1-6°), well away
    /// from water; fishing spots sit on the water surface of rivers, ponds and lakes, a few metres out from the bank.
    /// Counts per hectare of land come from the Like world (Sunset Valley, Riverview, Appaloosa Plains) or defaults.
    /// </summary>
    internal static class SpawnerEngine
    {
        public static readonly string[] GroupNames = { "Fish (rivers, lakes)", "Fish (sea)", "Seeds", "Gems", "Metals", "Meteorites",
            "Butterflies", "Bugs and beetles", "Pet animals (Pets)", "Other spawners" };
        // EA's own layer names for each group (Riverview)
        public static readonly string[] LayerNames = { "FishSpawners", "FishSpawners", "SeedSpawners", "GemSpawners", "MetalSpawners",
            "RockSpawners", "ButterflySpawners", "BugSpawners", "PetSpawners", "Spawners" };
        // per hectare of land when no Like world is chosen (about Sunset Valley's)
        public static readonly float[] DefaultPerHa = { 0.06f, 0.015f, 0.2f, 0.1f, 0.08f, 0.045f, 0.08f, 0.02f, 0f, 0f };

        public static SpawnGroup GroupOf(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (n.Contains("fish")) return n.Contains("ocean") || n.Contains("sea") ? SpawnGroup.FishOcean : SpawnGroup.FishFresh;
            if (n.Contains("seed")) return SpawnGroup.Seeds;
            if (n.Contains("gem")) return SpawnGroup.Gems;
            if (n.Contains("metal")) return SpawnGroup.Metals;
            if (n.Contains("meteor")) return SpawnGroup.Meteorites;
            if (n.Contains("butterfl")) return SpawnGroup.Butterflies;
            if (n.Contains("beetle") || n.Contains("bug") || n.Contains("insect") || n.Contains("firefl")) return SpawnGroup.Bugs;
            if (n.Contains("minorpet") || n.Contains("bird") || n.Contains("rodent") || n.Contains("lizard") || n.Contains("snake") || n.Contains("turtle"))
                return SpawnGroup.PetAnimals;
            return SpawnGroup.Other;
        }

        /// <summary>Per-hectare count of each group in a reference world (its spawner items over its land area).</summary>
        public static float[] PerHa(ObjectLibrary lib)
        {
            float[] d = new float[GroupNames.Length];
            if (lib == null || lib.LandHectares <= 0) return (float[])DefaultPerHa.Clone();
            float n = 0;
            foreach (RefObject r in lib.Items)
                if (r.Kind == PlaceKind.Spawner) { d[(int)GroupOf(r.Name)] += r.Count; n += r.Count; }
            // per hectare of land: the measured figure (tools/placement.py) where there is one; the library's area_ha
            // is the extent of its objects, sea included, which halved the density on coastal and island worlds
            KindShape sh = lib.ShapeOf(PlaceKind.Spawner);
            float perHa = sh != null && !float.IsNaN(sh.PerHa) ? sh.PerHa : n / lib.LandHectares;
            for (int g = 0; g < d.Length; g++) d[g] = n > 0 ? d[g] / n * perHa : 0;
            return d;
        }

        /// <summary>
        /// Places 'counts[g]' spawners of each group, chosen from the selected items of that group (weighted).
        /// m: maps on the working grid (cell metres); scale = map pixels per working cell; occupied = (mapX, mapY, r).
        /// With a World type, land spawners also keep to its distances from roads (EA's are a median
        /// 33-108 m from a road, tools/placement.py) using m.RoadDist (PlacementEngine.RoadDistance).
        /// </summary>
        public static List<SpawnPlacement> Place(PlacementEngine.Maps m, float cell, float scale, List<PlaceItem> items, int[] counts,
                                                 int seed, List<float[]> occupied, ObjectLibrary lib)
        {
            KindShape shape = lib != null ? lib.ShapeOf(PlaceKind.Spawner) : null;
            bool roadPref = shape != null && shape.Road != null && m.RoadDist != null;
            List<SpawnPlacement> result = new List<SpawnPlacement>();
            Random rng = new Random(seed * 17 + 3);
            int W = m.Water.W, H = m.Water.H;
            float mpm = scale / cell; // map pixels per metre
            List<float[]> taken = new List<float[]>(); // x, y, group
            for (int g = 0; g < counts.Length; g++)
            {
                if (counts[g] <= 0) continue;
                List<int> pool = new List<int>();
                for (int i = 0; i < items.Count; i++) if (items[i].Selected && GroupOf(items[i].Name) == (SpawnGroup)g) pool.Add(i);
                if (pool.Count == 0) continue;
                // candidate weights over the grid
                double[] cum = new double[W * H]; double total = 0;
                int rim = (int)Math.Ceiling(60f / cell); // the map's rim is under the distant terrain
                for (int i = 0; i < cum.Length; i++)
                {
                    int x = i % W, y = i / W;
                    bool edge = x < rim || y < rim || x >= W - rim || y >= H - rim;
                    float v = edge ? 0 : Habitat((SpawnGroup)g, m, i);
                    if (roadPref && g > 1 && v > 0) v *= lib.RoadPreference(shape, PlacementEngine.Band(m.RoadDist.D[i], PlacementEngine.RoadBands));
                    total += v; cum[i] = total;
                }
                if (total <= 0) continue;
                // EA: nearest spawner a median 14-47 m away (tools/placement.py); with a Like world, its own spacing
                float sameM = 30f, anyM = 12f;
                if (shape != null && !float.IsNaN(shape.NnMedian))
                {
                    sameM = Math.Max(8f, Math.Min(30f, 0.8f * shape.NnMedian));
                    anyM = Math.Max(5f, Math.Min(12f, 0.4f * shape.NnMedian));
                }
                float sameGap = sameM * mpm, anyGap = anyM * mpm;
                int placed = 0;
                for (int tries = 0; tries < counts[g] * 60 && placed < counts[g]; tries++)
                {
                    // a cell with probability proportional to its suitability (binary search in the running sum)
                    int i = Array.BinarySearch(cum, rng.NextDouble() * total);
                    if (i < 0) i = ~i;
                    if (i >= cum.Length) i = cum.Length - 1;
                    float mx = (i % W + (float)rng.NextDouble()) * scale, my = (i / W + (float)rng.NextDouble()) * scale;
                    bool clash = false;
                    foreach (float[] t in taken)
                    {
                        float gap = (int)t[2] == g ? sameGap : anyGap;
                        if ((t[0] - mx) * (t[0] - mx) + (t[1] - my) * (t[1] - my) < gap * gap) { clash = true; break; }
                    }
                    if (!clash && occupied != null)
                        foreach (float[] o in occupied)
                        {
                            float d = o[2] + 2f * mpm;
                            if ((o[0] - mx) * (o[0] - mx) + (o[1] - my) * (o[1] - my) < d * d) { clash = true; break; }
                        }
                    if (clash) continue;
                    SpawnPlacement sp = new SpawnPlacement();
                    sp.Item = PickFor(items, pool, (SpawnGroup)g, m, i, rng); sp.MapX = mx; sp.MapY = my;
                    sp.Yaw = (float)(rng.NextDouble() * Math.PI * 2); sp.OnWater = g <= 1;
                    result.Add(sp); taken.Add(new[] { mx, my, g }); placed++;
                }
            }
            return result;
        }

        /// <summary>How suitable working-grid cell i is for a group (0 = never).</summary>
        private static float Habitat(SpawnGroup g, PlacementEngine.Maps m, int i)
        {
            bool water = m.Water.D[i] > 0;
            float sl = m.Slope.D[i];
            switch (g)
            {
                case SpawnGroup.FishFresh:   // on rivers, streams and ponds, 3-15 m out from the bank
                    return water && m.Fresh.D[i] > 0 ? Grid.Ramp(m.FromLand.D[i], 2f, 5f) * (1 - Grid.Ramp(m.FromLand.D[i], 15f, 30f)) : 0;
                case SpawnGroup.FishOcean:   // the sea, 8-40 m out
                    return water && m.Fresh.D[i] == 0 ? Grid.Ramp(m.FromLand.D[i], 6f, 10f) * (1 - Grid.Ramp(m.FromLand.D[i], 40f, 60f)) : 0;
            }
            if (water || m.Blocked.D[i] > 0) return 0;
            float gentle = 1 - Grid.Ramp(sl, 8, 18), dry = 1 - 0.8f * Grid.Ramp(m.NearWater.D[i], 0.02f, 0.2f);
            float wood = m.Forest.D[i], open = 1 - Grid.Ramp(wood, 0.2f, 0.6f);
            switch (g)
            {
                case SpawnGroup.Seeds: return gentle * dry * (0.4f + 0.6f * Grid.Ramp(wood, 0.05f, 0.4f) * (1 - Grid.Ramp(wood, 0.7f, 1f)) + 0.3f * open);
                case SpawnGroup.Gems:
                case SpawnGroup.Metals: return dry * (1 - Grid.Ramp(sl, 22, 32)) * (0.3f + m.Rock.D[i] + 0.6f * m.Rel.D[i]);
                case SpawnGroup.Meteorites: return dry * (1 - Grid.Ramp(sl, 6, 12)) * open;
                case SpawnGroup.Butterflies: return (1 - Grid.Ramp(sl, 6, 14)) * (0.4f + open) * (1.2f - m.Rel.D[i]);
                case SpawnGroup.Bugs: return gentle * (0.2f + wood);
                case SpawnGroup.PetAnimals: return gentle * 0.6f + 0.4f * m.FreshBank.D[i];
                default: return gentle * dry * open;
            }
        }

        /// <summary>Item for this spot: pet spawners by their animal (turtles by water, birds in woods, snakes and
        /// lizards on rocky ground, rodents in the open), others by weight.</summary>
        private static int PickFor(List<PlaceItem> items, List<int> pool, SpawnGroup g, PlacementEngine.Maps m, int i, Random rng)
        {
            float[] w = new float[pool.Count]; float total = 0;
            for (int k = 0; k < pool.Count; k++)
            {
                string n = items[pool[k]].Name.ToLowerInvariant();
                float v = Math.Max(0.01f, items[pool[k]].Weight);
                if (g == SpawnGroup.PetAnimals)
                {
                    if (n.Contains("turtle")) v *= 0.2f + 4 * m.FreshBank.D[i];
                    else if (n.Contains("bird")) v *= 0.3f + 2 * m.Forest.D[i];
                    else if (n.Contains("snake") || n.Contains("lizard")) v *= 0.3f + 2 * m.Rock.D[i];
                    else if (n.Contains("rodent")) v *= 1.3f - m.Forest.D[i];
                }
                w[k] = v; total += v;
            }
            double r = rng.NextDouble() * total;
            for (int k = 0; k < pool.Count; k++) { r -= w[k]; if (r <= 0) return pool[k]; }
            return pool[pool.Count - 1];
        }
    }
}
