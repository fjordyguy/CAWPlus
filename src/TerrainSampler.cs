using System;
using System.Collections.Generic;
using System.IO;

namespace CawPlus
{
    public sealed class TerrainInfo
    {
        public int MapW, MapH;          // paint-map resolution (from exporting a layer)
        public float WorldSize;          // metres across
        public float CellMeters;         // metres per paint-map pixel
        public int Orientation;          // 0..7 dihedral transform image(u,v) -> world(x,z)
        public float Origin;             // world coordinate of the map's first edge (0 or -WorldSize/2)
        public float WaterLevel;         // sea level, metres
        public Grid WaterOffset;         // per pixel: pond surface minus sea level where a pond is higher (else 0); may be null
        public List<float[]> Ponds = new List<float[]>();
        public Grid Heights;             // metres, in paint-map pixel order
        public string Notes = "";
    }

    /// <summary>
    /// Reads the terrain out of the open CAW world, in the same pixel layout CAW uses for paint-layer images,
    /// so generated masks line up with no guessing.
    /// </summary>
    public static class TerrainSampler
    {
        public static string TempDir { get { string d = Path.Combine(Log.Dir, "temp"); Directory.CreateDirectory(d); return d; } }

        public static TerrainInfo Analyze(object world, int layerCount, ProgressFn progress)
        {
            TerrainInfo ti = new TerrainInfo();
            HeightFn heightAt = Caw.MakeHeightFn(world, "GetTerrainHeight");

            // 1. Height image from CAW: tells us world size and lets us detect orientation.
            Report(progress, "Reading height image", 2);
            Grid hImg = null;
            string hPath = Path.Combine(TempDir, "height_probe.png");
            try
            {
                if (File.Exists(hPath)) File.Delete(hPath);
                if (Caw.SaveHeightImage(world, hPath) && File.Exists(hPath)) hImg = ImageIO.ReadPngChannel0(hPath);
            }
            catch (Exception ex) { Log.Error("Height image export/read failed", ex); }

            // 2. Paint-map resolution: export an existing layer and read its size.
            Report(progress, "Reading paint map size", 6);
            int mapW = 0, mapH = 0;
            for (int i = 0; i < layerCount && mapW == 0; i++)
            {
                string p = Path.Combine(TempDir, "layer_probe.png");
                try
                {
                    if (File.Exists(p)) File.Delete(p);
                    if (Caw.ExportLayer(world, i, p) && File.Exists(p)) ImageIO.PngSize(p, out mapW, out mapH);
                }
                catch (Exception ex) { Log.Error("Layer " + i + " export probe failed", ex); }
            }

            if (hImg != null) ti.WorldSize = (hImg.W % 2 == 1) ? hImg.W - 1 : hImg.W; // 1537 vertices -> 1536 m
            if (mapW == 0) { mapW = hImg != null ? (int)ti.WorldSize : 1024; mapH = mapW; ti.Notes += "Paint map size not readable; assumed " + mapW + ". "; }
            if (ti.WorldSize <= 0) { ti.WorldSize = mapW; ti.Notes += "Height image not readable; assumed world size " + mapW + " m. "; }
            ti.MapW = mapW; ti.MapH = mapH;
            ti.CellMeters = ti.WorldSize / mapW;

            // 3. Orientation + origin: compare CAW's height image with live height queries.
            Report(progress, "Matching orientation", 10);
            ti.Orientation = 0; ti.Origin = 0;
            if (hImg != null)
            {
                float[] origins = { 0f, -ti.WorldSize / 2f };
                const int K = 40;
                double best = double.MinValue;
                foreach (float origin in origins)
                {
                    for (int t = 0; t < 8; t++)
                    {
                        // two passes (means first): the one-pass float sums read 1.09-1.53 on nearly flat worlds (logs)
                        double[] xs = new double[K * K], ys = new double[K * K]; double mx = 0, my = 0;
                        for (int j = 0; j < K; j++)
                            for (int i = 0; i < K; i++)
                            {
                                float u = (i + 0.5f) / K, v = (j + 0.5f) / K;
                                float wx, wz; Map(t, u, v, out wx, out wz);
                                xs[j * K + i] = hImg[(int)(u * (hImg.W - 1)), (int)(v * (hImg.H - 1))];
                                ys[j * K + i] = heightAt(origin + wx * ti.WorldSize, origin + wz * ti.WorldSize);
                                mx += xs[j * K + i]; my += ys[j * K + i];
                            }
                        mx /= xs.Length; my /= ys.Length;
                        double cov = 0, vx = 0, vy = 0;
                        for (int k = 0; k < xs.Length; k++) { double dx = xs[k] - mx, dy = ys[k] - my; cov += dx * dy; vx += dx * dx; vy += dy * dy; }
                        // no real spread in the live heights (under ~5 cm RMS): nothing to match
                        double r = (vx > 1e-12 && vy / ys.Length > 0.0025) ? cov / Math.Sqrt(vx * vy) : -2;
                        if (r > best) { best = r; ti.Orientation = t; ti.Origin = origin; }
                    }
                }
                // no real match (a flat new world reads 0.00): every world seen so far has origin 0 and orientation 0, and a
                // guess of origin -1024 on such a world put half of Apply terrain's calibration samples off the map (#167)
                if (best < 0.5) { ti.Orientation = 0; ti.Origin = 0; best = Math.Max(best, -1); }
                Log.Info(string.Format("Orientation {0}, origin {1}, correlation {2:F3}", ti.Orientation, ti.Origin, best));
                if (best < 0.9) ti.Notes += string.Format("Orientation match is weak ({0:F2}); check the preview against the CAW view. ", best);
            }
            else ti.Notes += "Orientation not verified. ";

            // 4. Sample heights at every paint-map pixel.
            Grid h = new Grid(mapW, mapH);
            for (int py = 0; py < mapH; py++)
            {
                float v = (py + 0.5f) / mapH;
                for (int px = 0; px < mapW; px++)
                {
                    float u = (px + 0.5f) / mapW;
                    float wx, wz; Map(ti.Orientation, u, v, out wx, out wz);
                    h.D[py * mapW + px] = heightAt(ti.Origin + wx * ti.WorldSize, ti.Origin + wz * ti.WorldSize);
                }
                if ((py & 31) == 0)
                {
                    Report(progress, "Sampling terrain", 12 + (int)(80.0 * py / mapH));
                    Injector.Pump();
                }
            }
            FixEdgeZeros(h);
            ti.Heights = h;

            // 5. Water level = median of (height - heightAboveWater) over dry points.
            Report(progress, "Finding water level", 95);
            try
            {
                HeightFn above = Caw.MakeHeightFn(world, "GetTerrainHeightAboveWater");
                List<float> diffs = new List<float>();
                const int K = 64;
                for (int j = 0; j < K; j++)
                    for (int i = 0; i < K; i++)
                    {
                        float u = (i + 0.5f) / K, v = (j + 0.5f) / K, wx, wz;
                        Map(ti.Orientation, u, v, out wx, out wz);
                        float x = ti.Origin + wx * ti.WorldSize, z = ti.Origin + wz * ti.WorldSize;
                        float a = above(x, z);
                        if (a > 0.01f) diffs.Add(heightAt(x, z) - a);
                    }
                if (diffs.Count > 0) { diffs.Sort(); ti.WaterLevel = diffs[diffs.Count / 2]; }
            }
            catch (Exception ex) { Log.Error("Water level detection failed", ex); ti.Notes += "Water level not detected; set it manually. "; }
            try
            {
                float sea = Caw.SeaLevel(world);
                Log.Info(string.Format("Sea level from CAW: {0:F2} m (height-above-water estimate {1:F2} m)", sea, ti.WaterLevel));
                ti.WaterLevel = sea;
            }
            catch (Exception ex) { Log.Error("Reading sea level failed", ex); }
            try
            {
                ti.Ponds = Caw.Ponds(world);
                if (ti.Ponds.Count > 0) ti.WaterOffset = PondOffsets(ti, h);
                Log.Info("Ponds: " + ti.Ponds.Count);
            }
            catch (Exception ex) { Log.Error("Reading ponds failed", ex); }

            Log.Info(string.Format("Terrain: map {0}x{1}, world {2} m, cell {3} m, water {4:F2} m. {5}",
                mapW, mapH, ti.WorldSize, ti.CellMeters, ti.WaterLevel, ti.Notes));
            Report(progress, "Terrain ready", 100);
            return ti;
        }

        /// <summary>
        /// CAW can report the ground on the map's outermost rows and columns as 0 m (seen on the far edges with Distant
        /// Terrain, v0.55); those cells then read as sea, and Auto-Paint drew water and shore sand along two edges (#163).
        /// A cell in the outer two rows/columns at ~0 m beside land further in takes the height from just inside.
        /// </summary>
        internal static void FixEdgeZeros(Grid h)
        {
            int w = h.W, n = h.H, fixedCells = 0;
            for (int pass = 1; pass >= 0; pass--) // the second row/column in first, then the outermost from it
                for (int k = 0; k < Math.Max(w, n); k++)
                {
                    if (k < w)
                    {
                        if (Fix(h, k, pass, k, pass + 1)) fixedCells++;
                        if (Fix(h, k, n - 1 - pass, k, n - 2 - pass)) fixedCells++;
                    }
                    if (k < n)
                    {
                        if (Fix(h, pass, k, pass + 1, k)) fixedCells++;
                        if (Fix(h, w - 1 - pass, k, w - 2 - pass, k)) fixedCells++;
                    }
                }
            if (fixedCells > 0) Log.Info("Terrain: " + fixedCells + " edge cell(s) read as 0 m by CAW, taken from the cells inside");
        }

        private static bool Fix(Grid h, int x, int y, int ix, int iy)
        {
            float v = h[x, y], inner = h[ix, iy];
            if (Math.Abs(v) > 0.01f || inner < 1f) return false;
            h[x, y] = inner;
            return true;
        }

        /// <summary>World (x, z) of continuous map coordinates (0..MapW, 0..MapH).</summary>
        public static void MapToWorld(TerrainInfo ti, float mx, float my, out float x, out float z)
        {
            float wx, wz;
            Map(ti.Orientation, mx / ti.MapW, my / ti.MapH, out wx, out wz);
            x = ti.Origin + wx * ti.WorldSize; z = ti.Origin + wz * ti.WorldSize;
        }

        /// <summary>Inverse of MapToWorld.</summary>
        public static void WorldToMap(TerrainInfo ti, float x, float z, out float mx, out float my)
        {
            float wx = (x - ti.Origin) / ti.WorldSize, wz = (z - ti.Origin) / ti.WorldSize;
            float u, v; Unmap(ti.Orientation, wx, wz, out u, out v);
            mx = u * ti.MapW; my = v * ti.MapH;
        }

        /// <summary>World (x, z) of a map pixel centre.</summary>
        public static void PixelToWorld(TerrainInfo ti, float px, float py, out float x, out float z)
        {
            float wx, wz;
            Map(ti.Orientation, (px + 0.5f) / ti.MapW, (py + 0.5f) / ti.MapH, out wx, out wz);
            x = ti.Origin + wx * ti.WorldSize; z = ti.Origin + wz * ti.WorldSize;
        }

        /// <summary>
        /// Raises the effective water level inside each pond's rectangle (plus a 15 m margin for its shore), where the
        /// pond surface is above sea level and the ground there is below the pond surface + 3 m.
        /// </summary>
        private static Grid PondOffsets(TerrainInfo ti, Grid h)
        {
            Grid off = new Grid(h.W, h.H);
            for (int py = 0; py < h.H; py++)
                for (int px = 0; px < h.W; px++)
                {
                    float x, z; PixelToWorld(ti, px, py, out x, out z);
                    foreach (float[] p in ti.Ponds)
                    {
                        if (p[0] <= ti.WaterLevel) continue;
                        if (x < p[1] - 15 || x > p[3] + 15 || z < p[2] - 15 || z > p[4] + 15) continue;
                        if (h.D[py * h.W + px] > p[0] + 3) continue;
                        off.D[py * h.W + px] = Math.Max(off.D[py * h.W + px], p[0] - ti.WaterLevel);
                    }
                }
            return off;
        }

        /// <summary>The 8 rotations/flips: image (u,v) in 0..1 -> world (x,z) in 0..1.</summary>
        public static void Map(int t, float u, float v, out float x, out float z)
        {
            switch (t)
            {
                case 1: x = 1 - u; z = v; break;
                case 2: x = u; z = 1 - v; break;
                case 3: x = 1 - u; z = 1 - v; break;
                case 4: x = v; z = u; break;
                case 5: x = 1 - v; z = u; break;
                case 6: x = v; z = 1 - u; break;
                case 7: x = 1 - v; z = 1 - u; break;
                default: x = u; z = v; break;
            }
        }

        /// <summary>The inverse of Map: world (x, z) as shares of the world size -> image (u, v).</summary>
        public static void Unmap(int t, float x, float z, out float u, out float v)
        {
            switch (t)
            {
                case 1: u = 1 - x; v = z; break;
                case 2: u = x; v = 1 - z; break;
                case 3: u = 1 - x; v = 1 - z; break;
                case 4: u = z; v = x; break;
                case 5: u = z; v = 1 - x; break;
                case 6: u = 1 - z; v = x; break;
                case 7: u = 1 - z; v = 1 - x; break;
                default: u = x; v = z; break;
            }
        }

        /// <summary>Box-average downsample for fast live previews.</summary>
        public static Grid Downsample(Grid g, int factor)
        {
            if (factor <= 1) return g;
            int w = g.W / factor, h = g.H / factor;
            Grid o = new Grid(w, h);
            float inv = 1f / (factor * factor);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int dy = 0; dy < factor; dy++)
                        for (int dx = 0; dx < factor; dx++)
                            s += g.D[(y * factor + dy) * g.W + x * factor + dx];
                    o.D[y * w + x] = s * inv;
                }
            return o;
        }

        private static void Report(ProgressFn p, string s, int pct) { if (p != null) p(s, pct); }
    }
}
