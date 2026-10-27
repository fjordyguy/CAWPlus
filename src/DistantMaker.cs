using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>What the distant terrain is made from: the world's heights, sea level and look.</summary>
    internal sealed class DistantInput
    {
        public Grid Heights;            // metres, map pixel layout (TerrainInfo.Heights)
        public int Orientation;         // TerrainInfo.Orientation
        public float WorldSize, Sea;     // the ring is built round the map centre, in the object's own metres
        public int[] Albedo; public int AlbW, AlbH;   // optional ground colour (0xRRGGBB), map pixel layout, any resolution
        public Grid Trees;              // optional tree cover 0..1, map pixel layout, any resolution
    }

    internal sealed class DistantSettings
    {
        public float Ring = 1500;       // metres of land beyond the map edge
        public float Hills = 1f;        // relief of the distant land against the world's own (0..2)
        public float Rise = 1f;         // how far the land climbs towards the horizon (0..2)
        public float FarLand = 0.3f;    // beyond sea edges: 0 open sea .. 1 a far coast
        public float Variety = 1f;      // how much the land changes round the ring: lowland valleys .. mountain ranges (0..2)
        public float CarryOn = 0.6f;    // share of the rivers and inlets at the map edge that carry on to the horizon
        public float[] Gain = { 1, 1, 1 };   // colour calibration from CAW's 3D view (your ground / the distant terrain)
        public int Seed = 1;
        public int Field = 1024, TexSize = 2048, NormalSize = 1024;
    }

    internal sealed class DistantResult
    {
        public S3Pkg.Mesh Mesh;
        public Grid Field; public float FieldCell;   // heights over the whole square (world inside, ring outside), for previews
        public int[] Diffuse, Normal;   // 0xAARRGGBB, TexSize^2 and NormalSize^2, rows = +z
        public int TexSize, NormalSize;
        public float R, Half;   // ring half-size, map half-size (the object goes at the map centre, y 0)
        public string Summary = "";
        public string Water = "";       // what became of the water at the map edge
    }

    /// <summary>
    /// Makes a custom distant terrain for a world, the way community-made ones are built: a height-field ring of land
    /// from just inside the map edge out to the horizon, with a hole where the playable map is, and one top-down
    /// colour texture plus a normal map over the whole square. Where the world's edge is sea the ring continues as sea
    /// (no mesh: the game's ocean shows) unless 'FarLand' raises a far coast. At the map edge the ring starts at the
    /// world's own heights, so hills, valleys and rivers carry on outwards, smoothing and rising into distant hills.
    /// Colours are learned from the world itself: its ground colour by slope and height above the water, and how much
    /// of that ground is wooded.
    /// </summary>
    internal static class DistantMaker
    {
        private const float Inset = 12f;          // the ring starts this far inside the map (hidden under the world's terrain)

        public static DistantResult Make(DistantInput inp, DistantSettings s, ProgressFn progress)
        {
            DistantResult r = new DistantResult();
            float a = inp.WorldSize / 2f, R = a + s.Ring;
            r.Half = a; r.R = R;
            Report(progress, "Reading the world's edge", 2);

            // ---- statistics of the land ----
            List<float> land = new List<float>();
            int stride = Math.Max(1, inp.Heights.D.Length / 200000);
            for (int i = 0; i < inp.Heights.D.Length; i += stride) if (inp.Heights.D[i] > inp.Sea + 0.5f) land.Add(inp.Heights.D[i]);
            land.Sort();
            float p10 = Pct(land, 0.1f, inp.Sea + 5), p50 = Pct(land, 0.5f, inp.Sea + 15), p90 = Pct(land, 0.9f, inp.Sea + 40);
            float relief = Math.Max(10f, Math.Min(150f, (p90 - p10) * 0.45f));

            // ---- the edge profile, walked round the map 1 m inside its edge, and 80 m further in ----
            int n = Math.Max(64, (int)(8 * a / 4f));
            float[] edge = new float[n], inner = new float[n];
            for (int k = 0; k < n; k++)
            {
                float ex, ez, nx, nz; Perimeter(a, (k + 0.5f) / n, out ex, out ez, out nx, out nz);
                edge[k] = WorldH(inp, ex - nx, ez - nz);
                inner[k] = WorldH(inp, ex - 80 * nx, ez - 80 * nz);
            }
            float step = 8 * a / n;
            float[] windows = { 8, 32, 128, 512, 2048 };
            float[][] lev = new float[windows.Length][], levIn = new float[windows.Length][];
            for (int l = 0; l < windows.Length; l++) { lev[l] = CircBlur(edge, windows[l] / step); levIn[l] = CircBlur(inner, windows[l] / step); }
            int wetEdge = 0; foreach (float e in lev[3]) if (e < inp.Sea + 1.5f) wetEdge++;

            // ---- the height field over the whole square ----
            int M = s.Field;
            float f = 2 * R / (M - 1);
            Random rng = new Random(s.Seed);
            Report(progress, "Shaping the distant land", 10);
            Grid hills = Grid.Fbm(M, M, 300 / f, 5, rng), ridges = Grid.Fbm(M, M, 560 / f, 4, rng);
            Grid coast = Grid.Fbm(M, M, 900 / f, 4, rng), farVar = Grid.Fbm(M, M, 1300 / f, 3, rng);
            Grid warp = Grid.Fbm(M, M, 450 / f, 3, rng);
            Grid rough = Grid.Fbm(M, M, 110 / f, 4, rng), fine = Grid.Fbm(M, M, 40 / f, 3, rng);   // smaller hills and gullies
            Grid regions = Grid.Fbm(M, M, 1700 / f, 3, rng), valleys = Grid.Fbm(M, M, 650 / f, 3, rng);   // landscape character; broad valleys
            Grid field = new Grid(M, M);
            for (int j = 0; j < M; j++)
            {
                float z = -R + j * f;
                for (int i = 0; i < M; i++)
                {
                    float x = -R + i * f;
                    float dx = Math.Max(0, Math.Abs(x) - a), dz = Math.Max(0, Math.Abs(z) - a);
                    int id = j * M + i;
                    if (dx <= 0 && dz <= 0) { field.D[id] = WorldH(inp, x, z); continue; }
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    float qx = Math.Max(-a, Math.Min(a, x)), qz = Math.Max(-a, Math.Min(a, z));
                    // where along the edge this point takes its shape from: the nearest edge point, wandering sideways
                    // further out (up to +-0.6 d), so ridges and valleys bend instead of running out in straight lines
                    float t = PerimeterParam(a, qx, qz) * n - 0.5f + (2 * warp.D[id] - 1) * 0.6f * Math.Min(d, 700f) / step;
                    float win = 8 + 1.5f * d;
                    float eRaw = Level(lev, windows, t, 8), eS = Level(lev, windows, t, win);
                    float e = eRaw + (eS - eRaw) * Smooth(0, 60, d);
                    float outSlope = (Level(lev, windows, t, 128) - Level(levIn, windows, t, 128)) / 79f;
                    // land rising towards the edge keeps rising a little; land falling towards it levels out (carrying a
                    // fall on dug a trough along that side, below the world's edge: the tester's v0.55 test)
                    outSlope = Math.Max(0f, Math.Min(0.25f, outSlope));
                    float eDry = e + outSlope * 120f * (1f - (float)Math.Exp(-d / 120f));
                    float wet = Clamp01((inp.Sea + 1.5f - Level(lev, windows, t, 128 + 1.5f * d)) / 3f);   // narrow inlets close up further out

                    float ridge = 1 - Math.Abs(2 * ridges.D[id] - 1);
                    float noise = 0.55f * (2 * hills.D[id] - 1) + 0.45f * (2 * ridge - 1)
                                + 0.35f * (2 * rough.D[id] - 1) - 0.18f * Math.Abs(2 * fine.D[id] - 1);   // V-shaped little valleys
                    // landscape character, changing slowly round the ring: lowland with broad valleys, hills, mountain ranges
                    float rv = Math.Max(-1f, Math.Min(1f, (2 * regions.D[id] - 1) * 2.2f * s.Variety));
                    float mtn = Smooth(0.2f, 0.75f, rv), low = Smooth(0.2f, 0.75f, -rv);
                    float peaks = 0.25f * (2 * hills.D[id] - 1) + 1.0f * (2 * ridge * ridge - 1) + 0.25f * (2 * rough.D[id] - 1);
                    noise += (peaks - noise) * mtn;
                    float amp = s.Hills * relief * Smooth(0, 250, d) * (0.6f + 0.4f * Math.Min(1f, d / s.Ring)) * (1 + 1.6f * mtn - 0.55f * low);
                    float span = Math.Max(30f, 0.6f * (p90 - p10));
                    float far = Math.Max(Level(lev, windows, t, 2048), p50) + s.Rise * span * (0.6f + 0.8f * farVar.D[id] + 1.3f * mtn - 0.5f * low);
                    float vr = 1 - Math.Abs(2 * valleys.D[id] - 1);
                    float hDry = eDry + (far - eDry) * Smooth(0, 0.75f * s.Ring, d) + amp * noise
                               - low * vr * vr * vr * Math.Max(0f, far - inp.Sea - 4) * 0.7f * Smooth(150, 600, d);   // valley floors

                    float seabed = Math.Max(inp.Sea - 40, Math.Min(e, inp.Sea - 1) - 0.02f * d);
                    float landness = s.FarLand * Smooth(200, 0.8f * s.Ring, d) + 0.6f * (coast.D[id] - 0.5f);
                    float shore = inp.Sea + 1.5f + Math.Max(0, landness - 0.5f) * 2f * Math.Max(20f, far - inp.Sea) + Math.Max(0, amp * noise);
                    float hWet = seabed + (shore - seabed) * Smooth(0.45f, 0.6f, landness);
                    field.D[id] = hDry + (hWet - hDry) * wet;
                }
                if ((j & 63) == 0) Report(progress, "Shaping the distant land", 10 + 30 * j / M);
            }

            // ---- rivers and inlets that reach the map edge: carry on, open into a lake, or end in a valley ----
            Report(progress, "Opening the water at the edge", 40);
            r.Water = OpenWater(inp, s, edge, step, a, R, f, field, Math.Max(p90, inp.Sea + 40));

            // ---- the ring mesh ----
            Report(progress, "Building the ring mesh", 42);
            r.Mesh = BuildMesh(inp, field, a, R, f, s.Ring);
            r.Field = field; r.FieldCell = f;
            int tris = r.Mesh.Tri.Length / 3;

            // ---- textures ----
            Report(progress, "Learning the world's colours", 55);
            ColourModel cm = ColourModel.Learn(inp);
            r.TexSize = s.TexSize; r.NormalSize = s.NormalSize;
            float[][] edgeCol = EdgeColours(inp, a, n, step);
            r.Diffuse = Paint(inp, field, cm, a, R, f, s, edgeCol, step, progress);
            Report(progress, "Normal map", 90);
            r.Normal = NormalMap(field, f, s.NormalSize);

            r.Summary = string.Format("Ring {0:F0} m beyond the edge ({1:F0} m across), {2} vertices, {3} triangles; edge {4:F0}% sea; " +
                "land {5:F0}-{6:F0} m (median {7:F0}), relief {8:F0} m; colours {9}{10}{11}",
                s.Ring, 2 * R, r.Mesh.VertexCount, tris, 100f * wetEdge / n, p10, p90, p50, relief, cm.Note,
                r.Water.Length > 0 ? "; " + r.Water : "",
                s.Gain[0] != 1 || s.Gain[1] != 1 || s.Gain[2] != 1 ? string.Format("; colour matched x{0:F2}/{1:F2}/{2:F2}", s.Gain[0], s.Gain[1], s.Gain[2]) : "");
            Report(progress, "Done", 100);
            return r;
        }

        // ---------- the world ----------

        /// <summary>World height at local (x, z) metres from the map centre (bilinear on the map pixels).</summary>
        internal static float WorldH(DistantInput inp, float lx, float lz)
        {
            float mx, my; ToMap(inp, lx, lz, inp.Heights.W, inp.Heights.H, out mx, out my);
            // keep off the outermost row/column of samples: CAW can report the ground there as 0 on the map's far
            // edges (fjordyguy's world: one side of the ring dropped to the sea while the land there was high)
            float lo = 1.5f, hx = inp.Heights.W - 2.5f, hy = inp.Heights.H - 2.5f;
            return inp.Heights.At(Math.Max(lo, Math.Min(hx, mx - 0.5f)), Math.Max(lo, Math.Min(hy, my - 0.5f)));
        }

        private static void ToMap(DistantInput inp, float lx, float lz, int w, int h, out float mx, out float my)
        {
            float wx = (lx + inp.WorldSize / 2f) / inp.WorldSize, wz = (lz + inp.WorldSize / 2f) / inp.WorldSize;
            float u, v; TerrainSampler.Unmap(inp.Orientation, wx, wz, out u, out v);
            mx = u * w; my = v * h;
        }

        /// <summary>Point at share t (0..1) of the way round the square of half-size a, with its outward normal.</summary>
        private static void Perimeter(float a, float t, out float x, out float z, out float nx, out float nz)
        {
            t = t - (float)Math.Floor(t);
            float p = t * 4, q = (p - (float)Math.Floor(p)) * 2 * a - a;
            switch ((int)p)
            {
                case 0: x = q; z = -a; nx = 0; nz = -1; break;
                case 1: x = a; z = q; nx = 1; nz = 0; break;
                case 2: x = -q; z = a; nx = 0; nz = 1; break;
                default: x = -a; z = -q; nx = -1; nz = 0; break;
            }
        }

        /// <summary>Inverse of Perimeter for a point on the square's edge.</summary>
        private static float PerimeterParam(float a, float x, float z)
        {
            float s = 2 * a;
            if (Math.Abs(z + a) < 1e-3f && x < a) return ((x + a) / s) / 4f;
            if (Math.Abs(x - a) < 1e-3f && z < a) return (1 + (z + a) / s) / 4f;
            if (Math.Abs(z - a) < 1e-3f && x > -a) return (2 + (a - x) / s) / 4f;
            return (3 + (a - z) / s) / 4f;
        }

        private static float[] CircBlur(float[] v, float width)
        {
            int n = v.Length, r = Math.Max(0, (int)(width / 2));
            if (r == 0) return (float[])v.Clone();
            r = Math.Min(r, n / 2 - 1);
            float[] o = new float[n];
            double acc = 0;
            for (int k = -r; k <= r; k++) acc += v[((k % n) + n) % n];
            for (int i = 0; i < n; i++)
            {
                o[i] = (float)(acc / (2 * r + 1));
                acc += v[(i + r + 1) % n] - v[((i - r) % n + n) % n];
            }
            return o;
        }

        /// <summary>The edge profile at parameter t (in samples), smoothed over 'window' metres (log-interpolated between levels).</summary>
        private static float Level(float[][] lev, float[] windows, float t, float window)
        {
            int n = lev[0].Length;
            float ft = t - (float)Math.Floor(t); int i0 = (((int)Math.Floor(t)) % n + n) % n, i1 = (i0 + 1) % n;
            if (window <= windows[0]) return lev[0][i0] + (lev[0][i1] - lev[0][i0]) * ft;
            int l = 0; while (l < windows.Length - 2 && window > windows[l + 1]) l++;
            float w = Clamp01((float)(Math.Log(window / windows[l]) / Math.Log(windows[l + 1] / windows[l])));
            float a = lev[l][i0] + (lev[l][i1] - lev[l][i0]) * ft, b = lev[l + 1][i0] + (lev[l + 1][i1] - lev[l + 1][i0]) * ft;
            return a + (b - a) * w;
        }

        // ---------- water at the map edge ----------

        /// <summary>
        /// Every river or inlet that reaches the map edge (a run of edge samples at sea level, 8-1500 m wide) gets a
        /// channel cut into the distant land: most carry on, winding and widening, out to the horizon (some through a
        /// lake on the way), some open into a lake, some narrow and end in a valley. Which is chosen per opening by the
        /// seed, with 'CarryOn' the share that reach the horizon. The channel leaves the map in the direction the water
        /// runs inside it, then turns to run away from the map. Ground under the sea has no mesh, so the game's water shows.
        /// </summary>
        private static string OpenWater(DistantInput inp, DistantSettings s, float[] edge, float step, float a, float R, float f, Grid field, float high)
        {
            int n = edge.Length, M = field.W;
            bool[] wet = new bool[n]; int nw = 0;
            for (int k = 0; k < n; k++) if (wet[k] = edge[k] < inp.Sea + 0.3f) nw++;
            if (nw == 0 || nw == n) return "";
            int k0 = 0; while (wet[k0]) k0++;
            List<int> starts = new List<int>(), lens = new List<int>();
            int runStart = -1;
            for (int i = 1; i <= n; i++)
            {
                int k = (k0 + i) % n;
                if (wet[k] && runStart < 0) runStart = k0 + i;
                if (!wet[k] && runStart >= 0) { starts.Add(runStart); lens.Add(k0 + i - runStart); runStart = -1; }
            }
            Grid cut = new Grid(M, M);
            for (int i = 0; i < cut.D.Length; i++) cut.D[i] = float.MaxValue;
            int carry = 0, lakes = 0, ends = 0;
            for (int o = 0; o < starts.Count; o++)
            {
                float width = lens[o] * step;
                if (width < 8 || width > 1500) continue;
                float mid = starts[o] + lens[o] / 2f;
                float ex, ez, nx, nz; Perimeter(a, mid / n, out ex, out ez, out nx, out nz);
                // which way the water runs inside the map: from the middle of the water 60 m in to where it leaves
                float sx = 0, sz = 0; int cnt = 0, span = (int)((width / 2 + 80) / step);
                for (int q = -span; q <= span; q++)
                {
                    float px, pz, qnx, qnz; Perimeter(a, (mid + q) / n, out px, out pz, out qnx, out qnz);
                    float ix = px - 60 * qnx, iz = pz - 60 * qnz;
                    if (WorldH(inp, ix, iz) < inp.Sea + 0.3f) { sx += ix; sz += iz; cnt++; }
                }
                float ang0 = (float)Math.Atan2(nz, nx);
                if (cnt > 0)
                {
                    float vx = ex - sx / cnt, vz = ez - sz / cnt;
                    if (vx * vx + vz * vz > 1)
                        ang0 += Math.Max(-0.95f, Math.Min(0.95f, (float)Math.Atan2(nx * vz - nz * vx, nx * vx + nz * vz)));
                }
                Random rr = new Random(s.Seed * 31 + o * 977 + 17);
                double u = rr.NextDouble();
                if (width > 300) u *= 0.6;   // wide sounds and estuaries mostly stay open
                int kind = u < s.CarryOn ? 0 : (u < s.CarryOn + (1 - s.CarryOn) * 0.6 ? 1 : 2);
                float hw0 = Math.Max(6f, width / 2), slope = 0.16f + 0.12f * (float)rr.NextDouble();
                float reach = Math.Min(500f, (high + 30 - inp.Sea) / slope);
                float endX, endZ, endAng;
                if (kind == 0)
                {
                    carry++;
                    bool viaLake = rr.NextDouble() < 0.25;
                    float len = viaLake ? 300 + 400 * (float)rr.NextDouble() : float.MaxValue;
                    Trace(inp, rr, cut, ex, ez, ang0, hw0, len, 0, slope, reach, a, R, f, out endX, out endZ, out endAng);
                    if (viaLake)
                    {
                        float lx, lz, rad;
                        Lake(inp, rr, cut, endX, endZ, endAng, hw0, slope, reach, a, R, f, out lx, out lz, out rad);
                        float ox = lx + 0.9f * rad * (float)Math.Cos(endAng), oz = lz + 0.9f * rad * (float)Math.Sin(endAng);
                        Trace(inp, rr, cut, ox, oz, endAng, Math.Max(6f, hw0 * 0.7f), float.MaxValue, 0, slope, reach, a, R, f, out endX, out endZ, out endAng);
                    }
                }
                else if (kind == 1)
                {
                    lakes++;
                    Trace(inp, rr, cut, ex, ez, ang0, hw0, 250 + 500 * (float)rr.NextDouble(), 1, slope, reach, a, R, f, out endX, out endZ, out endAng);
                    float lx, lz, rad;
                    Lake(inp, rr, cut, endX, endZ, endAng, hw0, slope, reach, a, R, f, out lx, out lz, out rad);
                }
                else
                {
                    ends++;
                    Trace(inp, rr, cut, ex, ez, ang0, hw0, 150 + 300 * (float)rr.NextDouble() + 2 * hw0, 2, slope, reach, a, R, f, out endX, out endZ, out endAng);
                }
            }
            // lower the land into the channels (a soft minimum, so valley floors meet the slopes without a crease)
            for (int j = 0; j < M; j++)
            {
                float z = -R + j * f;
                for (int i = 0; i < M; i++)
                {
                    int id = j * M + i;
                    float c = cut.D[id];
                    if (c == float.MaxValue) continue;
                    float x = -R + i * f;
                    if (Math.Abs(x) <= a && Math.Abs(z) <= a) continue;   // the world itself
                    float h = field.D[id], k = 4f;
                    float t = Clamp01(0.5f + 0.5f * (h - c) / k);
                    field.D[id] = h + (c - h) * t - k * t * (1 - t);
                }
            }
            int all = carry + lakes + ends;
            if (all == 0) return "";
            return string.Format("water at the edge: {0} opening{1} ({2} carry on, {3} into a lake, {4} end in a valley)",
                all, all == 1 ? "" : "s", carry, lakes, ends);
        }

        /// <summary>
        /// One channel from (x, z): winds (two sine waves), turns away from the map over ~400 m and never back
        /// towards it. kind 0 widens on to the horizon (or to 'len'), 1 widens a little to 'len' (into a lake),
        /// 2 narrows and its bed rises above the water, so it ends as a dry valley.
        /// </summary>
        private static void Trace(DistantInput inp, Random rr, Grid cut, float x, float z, float ang0, float hw0, float len,
            int kind, float slope, float reach, float a, float R, float f, out float endX, out float endZ, out float endAng)
        {
            float g = 0.03f + 0.04f * (float)rr.NextDouble();
            float lam1 = 220 + 240 * (float)rr.NextDouble(), lam2 = 90 + 60 * (float)rr.NextDouble();
            float ph1 = 6.283f * (float)rr.NextDouble(), ph2 = 6.283f * (float)rr.NextDouble();
            float A = 0.3f + 0.3f * (float)rr.NextDouble();
            float wob0 = A * (0.6f * (float)Math.Sin(ph1) + 0.4f * (float)Math.Sin(ph2));
            float baseAng = ang0, ang = ang0, sd = 0;
            while (true)
            {
                float hw, bed;
                if (kind == 2)
                {
                    hw = Math.Max(2f, hw0 * (1 - (sd / len) * (sd / len)));
                    bed = inp.Sea - 2.5f + 9f * Smooth(0.55f * len, len, sd);
                }
                else
                {
                    hw = hw0 + g * sd * (kind == 1 ? 0.5f : 1f);
                    bed = inp.Sea - 2.5f - Math.Min(6f, sd * 0.004f);
                }
                float d = Math.Max(0, Math.Max(Math.Abs(x), Math.Abs(z)) - a);
                float sl = slope + (0.6f - slope) * (1 - Smooth(0, 300, d));   // steeper banks near the map edge: no wall where they meet the world
                Stamp(cut, x, z, hw, bed, inp.Sea, sl, reach, R, f);
                if (sd >= len || Math.Max(Math.Abs(x), Math.Abs(z)) > R + 30 || sd > 4 * R) break;
                float qx = Math.Max(-a, Math.Min(a, x)), qz = Math.Max(-a, Math.Min(a, z));
                float ox = x - qx, oz = z - qz;
                float outAng = ox * ox + oz * oz < 1 ? ang0 : (float)Math.Atan2(oz, ox);
                baseAng += AngDiff(outAng, baseAng) * (15f / 400f);
                float wob = A * (0.6f * (float)Math.Sin(sd / lam1 + ph1) + 0.4f * (float)Math.Sin(sd / lam2 + ph2)) - wob0;
                ang = outAng + Math.Max(-1.25f, Math.Min(1.25f, AngDiff(baseAng + wob, outAng)));
                x += 15 * (float)Math.Cos(ang); z += 15 * (float)Math.Sin(ang); sd += 15;
            }
            endX = x; endZ = z; endAng = ang;
        }

        /// <summary>A lake (irregular, a little longer than wide) beyond (x, z) along 'ang', kept clear of the map.</summary>
        private static void Lake(DistantInput inp, Random rr, Grid cut, float x, float z, float ang, float hw, float slope, float reach,
            float a, float R, float f, out float cx, out float cz, out float rad)
        {
            rad = Math.Max(2.5f * hw, 120 + 260 * (float)rr.NextDouble());
            float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
            cx = x + 0.8f * rad * ca; cz = z + 0.8f * rad * sa;
            for (int k = 0; k < 40; k++)   // a lake cut off by the map edge would show a straight shore
            {
                float d = Math.Max(Math.Abs(cx), Math.Abs(cz)) - a;
                if (d > 1.45f * rad + 40) break;
                cx += 20 * ca; cz += 20 * sa;
            }
            float p1 = 6.283f * (float)rr.NextDouble(), p2 = 6.283f * (float)rr.NextDouble();
            int M = cut.W; float rmax = 1.45f * rad + reach;
            int i0 = Math.Max(0, (int)((cx - rmax + R) / f)), i1 = Math.Min(M - 1, (int)((cx + rmax + R) / f) + 1);
            int j0 = Math.Max(0, (int)((cz - rmax + R) / f)), j1 = Math.Min(M - 1, (int)((cz + rmax + R) / f) + 1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float px = -R + i * f - cx, pz = -R + j * f - cz;
                    float uu = px * ca + pz * sa, vv = -px * sa + pz * ca;
                    float rho = (float)Math.Sqrt(uu * uu / (1.35f * 1.35f) + vv * vv);
                    float th = (float)Math.Atan2(vv, uu);
                    float edgeR = rad * (1 + 0.22f * (float)Math.Sin(3 * th + p1) + 0.12f * (float)Math.Sin(5 * th + p2));
                    if (rho > edgeR + reach) continue;
                    float h = rho <= edgeR ? inp.Sea - 3f - 4f * (1 - rho / edgeR) : inp.Sea - 0.6f + (rho - edgeR) * slope;
                    int id = j * M + i;
                    if (h < cut.D[id]) cut.D[id] = h;
                }
        }

        /// <summary>A cross-section of a channel at (x, z): a rounded bed under the water, then banks rising at 'slope'.</summary>
        private static void Stamp(Grid cut, float x, float z, float hw, float bed, float sea, float slope, float reach, float R, float f)
        {
            int M = cut.W; float r = hw + reach;
            int i0 = Math.Max(0, (int)((x - r + R) / f)), i1 = Math.Min(M - 1, (int)((x + r + R) / f) + 1);
            int j0 = Math.Max(0, (int)((z - r + R) / f)), j1 = Math.Min(M - 1, (int)((z + r + R) / f) + 1);
            float top = Math.Max(bed, sea - 0.6f);
            for (int j = j0; j <= j1; j++)
            {
                float dz = -R + j * f - z;
                for (int i = i0; i <= i1; i++)
                {
                    float dx = -R + i * f - x;
                    float dd = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dd > r) continue;
                    float t = dd / hw;
                    float h = dd <= hw ? bed + (top - bed) * t * t : top + (dd - hw) * slope;
                    int id = j * M + i;
                    if (h < cut.D[id]) cut.D[id] = h;
                }
            }
        }

        private static float AngDiff(float a, float b)
        {
            float d = a - b;
            while (d > Math.PI) d -= 2 * (float)Math.PI;
            while (d < -Math.PI) d += 2 * (float)Math.PI;
            return d;
        }

        // ---------- mesh ----------

        private static List<float> Lines(float a, float R, float ring)
        {
            List<float> pos = new List<float>();
            float inner = a - Inset;
            int cells = Math.Max(2, (int)Math.Ceiling(2 * inner / 24f));
            for (int i = 0; i <= cells; i++) pos.Add(-inner + 2 * inner * i / cells);
            List<float> outer = new List<float>();
            outer.Add(a);
            float p = a;
            while (true)
            {
                float t = (p - a) / ring;
                p += 24f + 136f * (float)Math.Pow(Math.Max(0, t), 1.3);
                if (p >= R - 10f) { outer.Add(R); break; }
                outer.Add(p);
            }
            List<float> all = new List<float>();
            for (int i = outer.Count - 1; i >= 0; i--) all.Add(-outer[i]);
            all.AddRange(pos);
            all.AddRange(outer);
            return all;
        }

        private static S3Pkg.Mesh BuildMesh(DistantInput inp, Grid field, float a, float R, float f, float ring)
        {
            List<float> L = Lines(a, R, ring);
            int n = L.Count;
            float inner = a - Inset;
            int[] vid = new int[n * n];
            for (int i = 0; i < vid.Length; i++) vid[i] = -1;
            List<float> pos = new List<float>(), nrm = new List<float>(), uv = new List<float>();
            List<int> tri = new List<int>();
            float[] h = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = L[i], z = L[j];
                    float m = Math.Max(Math.Abs(x), Math.Abs(z));
                    if (m < inner - 0.5f) { h[j * n + i] = float.NaN; continue; }
                    if (m <= a + 0.01f) h[j * n + i] = WorldH(inp, x, z) - (m < a - 0.5f ? 0.5f : 0f);
                    else h[j * n + i] = field.At((x + R) / f, (z + R) / f);
                }
            for (int j = 0; j + 1 < n; j++)
                for (int i = 0; i + 1 < n; i++)
                {
                    float cx = (L[i] + L[i + 1]) / 2, cz = (L[j] + L[j + 1]) / 2;
                    if (Math.Max(Math.Abs(cx), Math.Abs(cz)) < inner) continue;
                    int c00 = j * n + i, c10 = c00 + 1, c01 = c00 + n, c11 = c01 + 1;
                    float lim = inp.Sea - 1f;
                    if (h[c00] < lim && h[c10] < lim && h[c01] < lim && h[c11] < lim) continue;   // open sea: the ocean shows
                    int v00 = V(c00, L, h, n, vid, pos, nrm, uv, field, R, f), v10 = V(c10, L, h, n, vid, pos, nrm, uv, field, R, f);
                    int v01 = V(c01, L, h, n, vid, pos, nrm, uv, field, R, f), v11 = V(c11, L, h, n, vid, pos, nrm, uv, field, R, f);
                    tri.Add(v00); tri.Add(v01); tri.Add(v10);
                    tri.Add(v10); tri.Add(v01); tri.Add(v11);
                }
            S3Pkg.Mesh mesh = new S3Pkg.Mesh();
            mesh.Pos = pos.ToArray(); mesh.Nrm = nrm.ToArray(); mesh.Uv = uv.ToArray(); mesh.Tri = tri.ToArray();
            return mesh;
        }

        private static int V(int c, List<float> L, float[] h, int n, int[] vid, List<float> pos, List<float> nrm, List<float> uv, Grid field, float R, float f)
        {
            if (vid[c] >= 0) return vid[c];
            int i = c % n, j = c / n;
            float x = L[i], z = L[j];
            vid[c] = pos.Count / 3;
            pos.Add(x); pos.Add(h[c]); pos.Add(z);
            float gx = (field.At((x + R) / f + 1, (z + R) / f) - field.At((x + R) / f - 1, (z + R) / f)) / (2 * f);
            float gz = (field.At((x + R) / f, (z + R) / f + 1) - field.At((x + R) / f, (z + R) / f - 1)) / (2 * f);
            float l = (float)Math.Sqrt(gx * gx + gz * gz + 1);
            nrm.Add(-gx / l); nrm.Add(1 / l); nrm.Add(-gz / l);
            uv.Add((x + R) / (2 * R)); uv.Add((z + R) / (2 * R));
            return vid[c];
        }

        // ---------- colour ----------

        /// <summary>
        /// The world's ground colour and tree cover by slope and height above the water, read off its own paint: per class
        /// the means of its darkest to lightest quarters, so the distant land gets the world's own range of colours.
        /// </summary>
        private sealed class ColourModel
        {
            public static readonly float[] SlopeEdges = { 0, 4, 10, 18, 28, 40, 90 };
            public static readonly float[] HeightEdges = { -1000, 0.5f, 3, 12, 40, 100, 200, 10000 };
            public const int Q = 4;
            public float[,,,] Col;     // [slope bin, height bin, quarter (dark..light), rgb]
            public float[,] Tree;      // [slope bin, height bin]
            public string Note;

            public static ColourModel Learn(DistantInput inp)
            {
                int ns = SlopeEdges.Length - 1, nh = HeightEdges.Length - 1;
                ColourModel cm = new ColourModel();
                cm.Col = new float[ns, nh, Q, 3]; cm.Tree = new float[ns, nh];
                List<int>[,] cols = new List<int>[ns, nh];
                double[,] cnt = new double[ns, nh], tre = new double[ns, nh];
                for (int si = 0; si < ns; si++) for (int hi = 0; hi < nh; hi++) cols[si, hi] = new List<int>();
                Grid hgt = inp.Heights;
                float cell = inp.WorldSize / hgt.W;
                int step = Math.Max(1, hgt.W / 512);
                for (int y = 1; y < hgt.H - 1; y += step)
                    for (int x = 1; x < hgt.W - 1; x += step)
                    {
                        float gx = (hgt.D[y * hgt.W + x + 1] - hgt.D[y * hgt.W + x - 1]) / (2 * cell);
                        float gy = (hgt.D[(y + 1) * hgt.W + x] - hgt.D[(y - 1) * hgt.W + x]) / (2 * cell);
                        float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180 / Math.PI);
                        float hs = hgt.D[y * hgt.W + x] - inp.Sea;
                        int sb = Bin(SlopeEdges, slope), hb = Bin(HeightEdges, hs);
                        float u = (x + 0.5f) / hgt.W, v = (y + 0.5f) / hgt.H;
                        if (inp.Albedo != null && cols[sb, hb].Count < 6000)
                            cols[sb, hb].Add(inp.Albedo[Math.Min(inp.AlbH - 1, (int)(v * inp.AlbH)) * inp.AlbW + Math.Min(inp.AlbW - 1, (int)(u * inp.AlbW))]);
                        if (inp.Trees != null) tre[sb, hb] += inp.Trees.At(u * inp.Trees.W - 0.5f, v * inp.Trees.H - 0.5f);
                        cnt[sb, hb] += 1;
                    }
                bool[,] have = new bool[ns, nh];
                int filled = 0;
                for (int si = 0; si < ns; si++)
                    for (int hi = 0; hi < nh; hi++)
                    {
                        List<int> l = cols[si, hi];
                        have[si, hi] = l.Count >= 20;
                        if (have[si, hi])
                        {
                            filled++;
                            l.Sort(delegate (int p, int q) { return Lum(p).CompareTo(Lum(q)); });
                            for (int qi = 0; qi < Q; qi++)
                            {
                                int a = l.Count * qi / Q, b = Math.Max(a + 1, l.Count * (qi + 1) / Q);
                                double r = 0, g = 0, bl = 0;
                                for (int k = a; k < b; k++) { r += (l[k] >> 16) & 255; g += (l[k] >> 8) & 255; bl += l[k] & 255; }
                                cm.Col[si, hi, qi, 0] = (float)(r / (b - a)); cm.Col[si, hi, qi, 1] = (float)(g / (b - a)); cm.Col[si, hi, qi, 2] = (float)(bl / (b - a));
                            }
                        }
                        cm.Tree[si, hi] = inp.Trees != null && cnt[si, hi] >= 20 ? (float)(tre[si, hi] / cnt[si, hi]) : float.NaN;
                    }
                // classes the world doesn't have (e.g. no mountains): nearest class it has, leaning to the built-in palette
                for (int si = 0; si < ns; si++)
                    for (int hi = 0; hi < nh; hi++)
                    {
                        if (!have[si, hi])
                        {
                            int bs = -1, bh = -1, bd = int.MaxValue;
                            for (int s2 = 0; s2 < ns; s2++)
                                for (int h2 = 0; h2 < nh; h2++)
                                {
                                    if (!have[s2, h2]) continue;
                                    int dd = 2 * Math.Abs(s2 - si) + Math.Abs(h2 - hi);
                                    if (dd < bd) { bd = dd; bs = s2; bh = h2; }
                                }
                            float[] d = Default(si, hi);
                            for (int qi = 0; qi < Q; qi++)
                            {
                                float spread = 0.82f + 0.12f * qi;   // built-in quarters: darker to lighter
                                for (int k = 0; k < 3; k++)
                                    cm.Col[si, hi, qi, k] = bs < 0 ? d[k] * spread
                                        : 0.5f * cm.Col[bs, bh, qi, k] + 0.5f * d[k] * spread * (cm.Col[bs, bh, 1, k] / Math.Max(1f, Default(bs, bh)[k]));
                            }
                        }
                        if (float.IsNaN(cm.Tree[si, hi])) cm.Tree[si, hi] = inp.Trees != null ? 0.15f : DefaultTree(si, hi);
                    }
                cm.Note = inp.Albedo != null ? string.Format("from the world's paint ({0} of {1} slope/height classes seen)", filled, ns * nh)
                                             : "built-in (the world's paint wasn't read)";
                if (inp.Trees != null) cm.Note += ", tree cover from the world's trees";
                return cm;
            }

            private static int Lum(int c) { return 3 * ((c >> 16) & 255) + 6 * ((c >> 8) & 255) + (c & 255); }

            private static int Bin(float[] edges, float v)
            {
                for (int i = 1; i < edges.Length; i++) if (v < edges[i]) return i - 1;
                return edges.Length - 2;
            }

            private static float[] Default(int si, int hi)
            {
                if (hi == 0) return new float[] { 150, 142, 112 };                       // under water: sandy bed
                if (hi == 1 && si <= 1) return new float[] { 196, 184, 148 };           // beach
                if (si >= 4) return new float[] { 124, 118, 108 };                        // rock
                if (si == 3) return new float[] { 112, 116, 84 };
                if (hi >= 5) return new float[] { 110, 118, 80 };
                return new float[] { 98, 122, 66 };                                       // grass
            }

            private static float DefaultTree(int si, int hi)
            {
                if (hi <= 1 || si >= 5) return 0;
                return si >= 3 ? 0.25f : 0.4f;
            }

            /// <summary>
            /// Colour and tree cover at a slope and height, interpolated between class centres (no banding); 'pick' 0..1
            /// chooses within the class's colour range, dark to light.
            /// </summary>
            public void Look(float slope, float hs, float pick, float[] rgb, out float tree)
            {
                float sf = Pos(SlopeCentres, slope), hf = Pos(HeightCentres, hs);
                int s0 = (int)sf, h0 = (int)hf; float ts = sf - s0, th = hf - h0;
                int s1 = Math.Min(s0 + 1, SlopeEdges.Length - 2), h1 = Math.Min(h0 + 1, HeightEdges.Length - 2);
                float qf = Clamp01(pick) * (Q - 1); int q0 = Math.Min(Q - 2, (int)qf); float tq = qf - q0;
                for (int k = 0; k < 3; k++)
                {
                    float c00 = Col[s0, h0, q0, k] + (Col[s0, h0, q0 + 1, k] - Col[s0, h0, q0, k]) * tq;
                    float c10 = Col[s1, h0, q0, k] + (Col[s1, h0, q0 + 1, k] - Col[s1, h0, q0, k]) * tq;
                    float c01 = Col[s0, h1, q0, k] + (Col[s0, h1, q0 + 1, k] - Col[s0, h1, q0, k]) * tq;
                    float c11 = Col[s1, h1, q0, k] + (Col[s1, h1, q0 + 1, k] - Col[s1, h1, q0, k]) * tq;
                    float a = c00 + (c10 - c00) * ts, b = c01 + (c11 - c01) * ts;
                    rgb[k] = a + (b - a) * th;
                }
                float ta = Tree[s0, h0] + (Tree[s1, h0] - Tree[s0, h0]) * ts, tb = Tree[s0, h1] + (Tree[s1, h1] - Tree[s0, h1]) * ts;
                tree = ta + (tb - ta) * th;
            }

            private static readonly float[] SlopeCentres = { 2, 7, 14, 23, 34, 50 };
            private static readonly float[] HeightCentres = { -5, 1.5f, 7, 25, 70, 150, 300 };

            /// <summary>Fractional class index: 0 at the first class centre, linear between centres, clamped.</summary>
            private static float Pos(float[] c, float v)
            {
                if (v <= c[0]) return 0;
                for (int i = 0; i + 1 < c.Length; i++)
                    if (v < c[i + 1]) return i + (v - c[i]) / (c[i + 1] - c[i]);
                return c.Length - 1;
            }
        }

        /// <summary>
        /// The world's own paint colour along its edge (a few metres in), r, g, b at levels smoothed over 24, 96 and
        /// 384 m along the edge; null without paint.
        /// </summary>
        private static float[][] EdgeColours(DistantInput inp, float a, int n, float step)
        {
            if (inp.Albedo == null) return null;
            float[] r = new float[n], g = new float[n], b = new float[n];
            for (int k = 0; k < n; k++)
            {
                float ex, ez, nx, nz; Perimeter(a, (k + 0.5f) / n, out ex, out ez, out nx, out nz);
                float sr = 0, sg = 0, sb = 0;
                for (int q = 0; q < 3; q++)
                {
                    float mx, my; ToMap(inp, ex - (4 + 8 * q) * nx, ez - (4 + 8 * q) * nz, inp.AlbW, inp.AlbH, out mx, out my);
                    int c = inp.Albedo[Math.Min(inp.AlbH - 1, Math.Max(0, (int)my)) * inp.AlbW + Math.Min(inp.AlbW - 1, Math.Max(0, (int)mx))];
                    sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255;
                }
                r[k] = sr / 3; g[k] = sg / 3; b[k] = sb / 3;
            }
            float[] w = { 24, 96, 384 };
            float[][] o = new float[9][];
            for (int l = 0; l < 3; l++) { o[l * 3] = CircBlur(r, w[l] / step); o[l * 3 + 1] = CircBlur(g, w[l] / step); o[l * 3 + 2] = CircBlur(b, w[l] / step); }
            return o;
        }

        private static int[] Paint(DistantInput inp, Grid field, ColourModel cm, float a, float R, float f, DistantSettings s, float[][] edgeCol, float step, ProgressFn progress)
        {
            int n = edgeCol != null ? edgeCol[0].Length : 0;
            int T = s.TexSize;
            int[] px = new int[T * T];
            Random rng = new Random(s.Seed * 7 + 1);
            int Nn = Math.Min(T, 1024);
            float texM = 2 * R / T, nf = 2 * R / Nn;
            Grid woods = Grid.Fbm(Nn, Nn, 150 / nf, 4, rng), tone = Grid.Fbm(Nn, Nn, 70 / nf, 4, rng), grain = Grid.Fbm(Nn, Nn, 12 / nf, 2, rng);
            Grid patch = Grid.Fbm(Nn, Nn, 90 / nf, 4, rng), detail = Grid.Fbm(Nn, Nn, 22 / nf, 3, rng);
            Grid soft = field.Blur(Math.Max(1f, 15f / f));                       // ~15 m: ridges lighter, hollows darker
            float[] rgb = new float[3];
            float[] forest = { 46, 64, 36 };
            int[] o = new int[3];
            uint hash = (uint)(s.Seed * 2654435761u) | 1u;
            for (int y = 0; y < T; y++)
            {
                float z = -R + (y + 0.5f) * texM;
                for (int x = 0; x < T; x++)
                {
                    float lx = -R + (x + 0.5f) * texM;
                    float m = Math.Max(Math.Abs(lx), Math.Abs(z));
                    float fx = (lx + R) / f, fz = (z + R) / f;
                    float hgt = field.At(fx, fz);
                    hash ^= hash << 13; hash ^= hash >> 17; hash ^= hash << 5;   // per-pixel speckle (xorshift)
                    if (inp.Albedo != null && m < a - 1)
                    {
                        float mx, my; ToMap(inp, lx, z, inp.AlbW, inp.AlbH, out mx, out my);
                        int c = inp.Albedo[Math.Min(inp.AlbH - 1, Math.Max(0, (int)my)) * inp.AlbW + Math.Min(inp.AlbW - 1, Math.Max(0, (int)mx))];
                        px[y * T + x] = unchecked((int)0xFF000000) | (c & 0xFFFFFF);
                        continue;
                    }
                    float gx = (field.At(fx + 1, fz) - field.At(fx - 1, fz)) / (2 * f), gz = (field.At(fx, fz + 1) - field.At(fx, fz - 1)) / (2 * f);
                    float slope = (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
                    float nx = (lx + R) / nf - 0.5f, nz = (z + R) / nf - 0.5f;
                    float pick = (0.6f * patch.At(nx, nz) + 0.4f * detail.At(nx, nz) - 0.5f) * 2.4f + 0.5f;   // patchwork across the world's own colours
                    float tree;
                    cm.Look(slope, hgt - inp.Sea, pick, rgb, out tree);
                    float dOut = (float)Math.Sqrt(Math.Max(0, Math.Abs(lx) - a) * Math.Max(0, Math.Abs(lx) - a) + Math.Max(0, Math.Abs(z) - a) * Math.Max(0, Math.Abs(z) - a));
                    if (n > 0 && dOut < 260 && hgt > inp.Sea + 0.5f)
                    {
                        // next to the map: the world's own paint along that stretch of edge, fading into the learned colours,
                        // so the seam doesn't show (smoothed more along the edge further out: no streaks)
                        float qx = Math.Max(-a, Math.Min(a, lx)), qz = Math.Max(-a, Math.Min(a, z));
                        float t = PerimeterParam(a, qx, qz) * n - 0.5f + (2 * tone.At(nx, nz) - 1) * 0.5f * dOut / step;
                        float lw = Clamp01(dOut / 120f) * 2f; int l0 = Math.Min(1, (int)lw); float lt = lw - l0;
                        float w = 1 - Smooth(0, 260, dOut);
                        for (int k = 0; k < 3; k++)
                        {
                            float c0 = EdgeAt(edgeCol[l0 * 3 + k], t), c1 = EdgeAt(edgeCol[(l0 + 1) * 3 + k], t);
                            rgb[k] += (c0 + (c1 - c0) * lt - rgb[k]) * w;
                        }
                    }
                    float wv = woods.At(nx, nz), tv = tone.At(nx, nz), gv = grain.At(nx, nz);
                    float cover = Smooth(1 - tree - 0.06f, 1 - tree + 0.06f, wv) * (hgt > inp.Sea + 0.5f ? 1 : 0);
                    float relief = Math.Max(0.78f, Math.Min(1.12f, 1f + (hgt - soft.At(fx, fz)) * 0.03f));
                    float shade = (0.92f + 0.16f * tv) * relief;
                    float speck = (0.7f + 0.45f * (hash & 1023) / 1023f) * (0.8f + 0.4f * gv);     // tree crowns
                    for (int k = 0; k < 3; k++)
                    {
                        float ground = rgb[k] * shade;
                        float canopy = Math.Min(forest[k], rgb[k] * 0.6f) * speck * relief;
                        o[k] = Math.Max(0, Math.Min(255, (int)((ground + (canopy - ground) * cover) * s.Gain[k])));
                    }
                    px[y * T + x] = unchecked((int)0xFF000000) | (o[0] << 16) | (o[1] << 8) | o[2];
                }
                if ((y & 127) == 0) Report(progress, "Colouring the distant land", 58 + 30 * y / T);
            }
            return px;
        }

        private static float EdgeAt(float[] v, float t)
        {
            int n = v.Length;
            float ft = t - (float)Math.Floor(t); int i0 = (((int)Math.Floor(t)) % n + n) % n, i1 = (i0 + 1) % n;
            return v[i0] + (v[i1] - v[i0]) * ft;
        }

        /// <summary>
        /// A picture for the preview in the colours it will have in the game: the colour texture it is made with (colour
        /// match included), lightly hill-shaded (light from the north-west), and the water (sea and anything below it) as
        /// CAW's sea, deeper darker.
        /// </summary>
        public static int[] PreviewColours(DistantResult r, float sea)
        {
            int T = r.TexSize;
            int[] o = new int[T * T];
            float scale = (r.Field.W - 1f) / T, f = r.FieldCell;
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    float fx = (x + 0.5f) * scale, fz = (y + 0.5f) * scale;
                    float h = r.Field.At(fx, fz);
                    int c = r.Diffuse[y * T + x];
                    float cr = (c >> 16) & 255, cg = (c >> 8) & 255, cb = c & 255;
                    if (h < sea)
                    {
                        float depth = Clamp01((sea - h) / 12f);
                        // the game's own sea (the ring has no mesh there): CAW's dark teal water
                        cr = 46 - 16 * depth; cg = 82 - 22 * depth; cb = 100 - 20 * depth;
                    }
                    else
                    {
                        float gx = (r.Field.At(fx + 1, fz) - r.Field.At(fx - 1, fz)) / (2 * f), gz = (r.Field.At(fx, fz + 1) - r.Field.At(fx, fz - 1)) / (2 * f);
                        float l = (float)Math.Sqrt(gx * gx + gz * gz + 1);
                        // gentle shading round 1 (the texture's own colour on level ground), as the game lights it:
                        // the preview used to brighten slopes facing the light by up to 35%
                        float lit = Math.Max(0.7f, Math.Min(1.1f, 1f + 0.45f * (gx + gz) * 0.7f / l));
                        cr *= lit; cg *= lit; cb *= lit;
                    }
                    o[y * T + x] = unchecked((int)0xFF000000) | (Clamp255(cr) << 16) | (Clamp255(cg) << 8) | Clamp255(cb);
                }
            return o;
        }

        private static int Clamp255(float v) { return v < 0 ? 0 : (v > 255 ? 255 : (int)v); }

        /// <summary>Normal map in the game's format: X (along u = +x) in alpha, Y in red/green/blue (stored for +z downhill).</summary>
        private static int[] NormalMap(Grid field, float f, int N)
        {
            int[] px = new int[N * N];
            float scale = (field.W - 1f) / (N - 1f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x * scale, fz = y * scale;
                    float gx = (field.At(fx + 1, fz) - field.At(fx - 1, fz)) / (2 * f), gz = (field.At(fx, fz + 1) - field.At(fx, fz - 1)) / (2 * f);
                    float l = (float)Math.Sqrt(gx * gx + gz * gz + 1);
                    int ax = Enc(-gx / l), gy = Enc(gz / l);
                    px[y * N + x] = (ax << 24) | (gy << 16) | (gy << 8) | gy;
                }
            return px;
        }

        private static int Enc(float v) { int i = (int)Math.Round(128 + 127 * v); return i < 0 ? 0 : (i > 255 ? 255 : i); }
        private static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        private static float Smooth(float e0, float e1, float v) { float t = Clamp01((v - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        private static float Pct(List<float> sorted, float p, float fallback) { return sorted.Count == 0 ? fallback : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))]; }
        private static void Report(ProgressFn p, string stage, int pct) { if (p != null) p(stage, pct); }

        // ---------- the package ----------

        /// <summary>Resources of an existing distant terrain object to copy (its definition, key, proxy, footprint, lights).</summary>
        internal sealed class Template { public byte[] Objd, Objk, Vpxy, Ftpt, Lite; public string Source; }

        /// <summary>The object key everything is named after: one per world, so remaking replaces the same object.</summary>
        public static ulong Instance(string worldKey) { return S3Pkg.Fnv64("cawplus_distant_terrain_" + worldKey) & 0x00FFFFFFFFFFFFFFUL; }

        public static byte[] Package(DistantResult r, string worldKey, Template t)
        {
            ulong inst = Instance(worldKey);
            string name = "CAWPlus_DistantTerrain_" + worldKey;
            S3Pkg.Key objd = new S3Pkg.Key(S3Pkg.OBJD, 0, inst), objk = new S3Pkg.Key(S3Pkg.OBJK, 0, inst);
            S3Pkg.Key vpxy = new S3Pkg.Key(S3Pkg.VPXY, 1, inst), modl = new S3Pkg.Key(S3Pkg.MODL, 1, inst);
            S3Pkg.Key mlod = new S3Pkg.Key(S3Pkg.MLOD, 0, inst & 0x00FFFFFFFFFFFFFFUL);
            S3Pkg.Key ftpt = new S3Pkg.Key(S3Pkg.FTPT, 0, S3Pkg.Fnv64(name + "_footprint"));
            S3Pkg.Key lite = new S3Pkg.Key(S3Pkg.LITE, 0, S3Pkg.Fnv64(name + "_lights"));
            S3Pkg.Key diff = new S3Pkg.Key(S3Pkg.DDS, 0, S3Pkg.Fnv64(name + "_d"));
            S3Pkg.Key norm = new S3Pkg.Key(S3Pkg.DDS, 0, S3Pkg.Fnv64(name + "_n"));

            S3Pkg.Dbpf p = new S3Pkg.Dbpf();
            p.Add(objd, S3Pkg.CopyObjd(t.Objd, objk, name));
            p.Add(objk, S3Pkg.CopyObjk(t.Objk, vpxy));
            Dictionary<uint, S3Pkg.Key> refs = new Dictionary<uint, S3Pkg.Key>();
            refs[S3Pkg.MODL] = modl; refs[S3Pkg.MLOD] = mlod; refs[S3Pkg.FTPT] = ftpt; refs[S3Pkg.LITE] = lite;
            p.Add(vpxy, S3Pkg.CopyVpxy(t.Vpxy, vpxy, refs, S3Pkg.Bounds(r.Mesh.Pos)));
            p.Add(modl, S3Pkg.Model(r.Mesh, modl.Instance, name, diff, norm));
            if (t.Ftpt != null) p.Add(ftpt, S3Pkg.CopyRcol1(t.Ftpt, ftpt));
            if (t.Lite != null) p.Add(lite, S3Pkg.CopyRcol1(t.Lite, lite));
            p.Add(diff, S3Pkg.Dds(r.Diffuse, r.TexSize, r.TexSize, false));
            p.Add(norm, S3Pkg.Dds(r.Normal, r.NormalSize, r.NormalSize, true));
            return p.ToBytes();
        }
    }
}
