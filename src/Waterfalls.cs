using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>A pond-step waterfall site: the lip (map pixels), where its water lands and how far it drops.</summary>
    public sealed class WaterfallSite
    {
        public float MapX, MapY;         // the lip, paint-map pixels
        public float LandX, LandY;       // where the fall meets the water below, paint-map pixels
        public float DirX, DirY;         // downhill, unit, paint-map pixel axes
        public float LipY, WaterY, Drop; // metres
        /// <summary>EA's size name in the effect name: short (under 15 m), med (15-30 m) or tall.</summary>
        public string Size { get { return Drop < 15 ? "short" : Drop < 30 ? "med" : "tall"; } }
    }

    /// <summary>
    /// Pond-step waterfall sites (#158): an upper pond spilling over a cliff. Only this kind is found; falls on sea
    /// cliffs or dry cliffs are not supported. The rule was tuned on 8 EA worlds and checked on 7 others (scratch
    /// detector, pass 2, then this code on the same heightmaps and EA ponds): every site it keeps is a real EA fall
    /// (6 of 6: 2 of 2 on the tuning worlds, 4 of 4 held out), none on the 8 EA worlds without falls; it finds 5 of the
    /// 35 EA waterfall sites (in Aurora Skies, Isla Paradiso, Lunar Lakes and Dragon Valley).
    /// On a ~2 m grid: from each land cell a straight ray downhill (sampled every 2 m) must meet water within 20 m,
    /// dropping at least 12 m (6 m let two 10-11 m false sites through on Lucky Palms, a tuning world), with a stretch of
    /// at least 70 deg over 4 m and at least 30 deg on average; candidates are kept 60 m apart, highest drop first; then
    /// pond water (at the lip's height - 3 m or higher) must lie within 6 m behind the lip.
    /// </summary>
    public static class Waterfalls
    {
        const float Step = 2f, Reach = 20f, MinDrop = 12f, MinFace = 70f, MinSlope = 30f, Apart = 60f, Behind = 6f, BehindTol = 3f;

        /// <summary>
        /// heights: metres per paint-map pixel (cellM metres apart); pondLevel: the surface of the raised pond whose
        /// rectangle holds the pixel (the highest if several), else 0, exactly inside each rectangle (TerrainInfo.WaterOffset
        /// reaches 15 m past it, which floods the ground below a pond step). Sites sorted by drop, highest first.
        /// </summary>
        public static List<WaterfallSite> Find(Grid heights, float cellM, float sea, Grid pondLevel)
        {
            List<WaterfallSite> found = new List<WaterfallSite>();
            if (pondLevel == null) return found; // pond-step falls only
            int f = Math.Max(1, (int)Math.Round(Step / cellM));
            float F = f * cellM;
            int w = (heights.W - 1) / f + 1, h = (heights.H - 1) / f + 1;
            Grid g = new Grid(w, h);
            float[] water = new float[w * h]; // water surface, or NaN for land
            bool[] pond = new bool[w * h];
            bool anyPond = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int src = (y * f) * heights.W + x * f, i = y * w + x;
                    float z = heights.D[src];
                    g.D[i] = z;
                    float lvl = Math.Max(sea, pondLevel.D[src]);
                    water[i] = z < lvl ? lvl : float.NaN;
                    pond[i] = z < lvl && lvl > sea + 0.01f;
                    anyPond |= pond[i];
                }
            if (!anyPond) return found;
            Grid gs = g.Blur(1f);
            int steps = (int)(Reach / Step);
            List<WaterfallSite> cands = new List<WaterfallSite>();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (!float.IsNaN(water[i])) continue;
                    float gx = (gs.D[i + 1] - gs.D[i - 1]) / (2 * F), gz = (gs.D[i + w] - gs.D[i - w]) / (2 * F);
                    float gn = (float)Math.Sqrt(gx * gx + gz * gz) + 1e-9f;
                    float dx = -gx / gn, dz = -gz / gn, g0 = g.D[i];
                    float[] hist = new float[steps + 1]; hist[0] = g0;
                    float maxFace = 0, hitD = float.NaN, hitL = 0; int hx = 0, hy = 0;
                    for (int st = 1; st <= steps; st++)
                    {
                        int jj = Clamp((int)Math.Round(x + dx * st), 0, w - 1), ii = Clamp((int)Math.Round(y + dz * st), 0, h - 1);
                        float hs = g.D[ii * w + jj];
                        if (st >= 2) maxFace = Math.Max(maxFace, (float)(Math.Atan((hist[st - 2] - hs) / (2 * F)) * 180 / Math.PI));
                        hist[st] = hs;
                        float wl = water[ii * w + jj];
                        if (!float.IsNaN(wl)) { hitD = st * F; hitL = wl; hx = jj; hy = ii; break; }
                    }
                    if (float.IsNaN(hitD)) continue;
                    float drop = g0 - hitL;
                    if (drop < MinDrop || maxFace < MinFace || Math.Atan(drop / hitD) * 180 / Math.PI < MinSlope) continue;
                    WaterfallSite s = new WaterfallSite();
                    s.MapX = x * f; s.MapY = y * f; s.LandX = hx * f; s.LandY = hy * f; s.DirX = dx; s.DirY = dz;
                    s.LipY = g0; s.WaterY = hitL; s.Drop = drop;
                    cands.Add(s);
                }
            cands.Sort(delegate (WaterfallSite a, WaterfallSite b) { return b.Drop.CompareTo(a.Drop); });
            float apart = Apart / cellM;
            List<WaterfallSite> kept = new List<WaterfallSite>();
            foreach (WaterfallSite c in cands)
            {
                bool near = false;
                foreach (WaterfallSite k in kept)
                {
                    float ex = k.MapX - c.MapX, ey = k.MapY - c.MapY;
                    if (ex * ex + ey * ey < apart * apart) { near = true; break; }
                }
                if (!near) kept.Add(c);
            }
            int r = (int)Math.Ceiling(Behind / F);
            foreach (WaterfallSite c in kept)
            {
                int cx = (int)(c.MapX / f), cy = (int)(c.MapY / f);
                bool ok = false;
                for (int oy = -r; oy <= r && !ok; oy++)
                    for (int ox = -r; ox <= r && !ok; ox++)
                    {
                        int px = cx + ox, py = cy + oy;
                        if (px < 0 || py < 0 || px >= w || py >= h || (ox == 0 && oy == 0)) continue;
                        int i = py * w + px;
                        if (!pond[i] || water[i] < c.LipY - BehindTol) continue;
                        float d = (float)Math.Sqrt(ox * ox + oy * oy) * F;
                        if (d > Behind) continue;
                        if (-(ox * c.DirX + oy * c.DirY) * F / d > 0.3f) ok = true; // behind the lip
                    }
                if (ok) found.Add(c);
            }
            return found;
        }

        /// <summary>Pond surface per paint-map pixel inside each pond's rectangle (ponds: level, x0, z0, x1, z1 in world metres).</summary>
        public static Grid PondLevels(int w, int h, List<float[]> ponds, PixelToWorldFn toWorld)
        {
            if (ponds == null || ponds.Count == 0) return null;
            Grid g = new Grid(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx, wz; toWorld(x, y, out wx, out wz);
                    foreach (float[] p in ponds)
                        if (wx >= p[1] && wx <= p[3] && wz >= p[2] && wz <= p[4] && p[0] > g.D[y * w + x]) g.D[y * w + x] = p[0];
                }
            return g;
        }

        public delegate void PixelToWorldFn(float px, float py, out float x, out float z);

        /// <summary>The effect names CAW lists for user worlds (BaseGameWorldObjects.ValidUserFX): variant 1-3.</summary>
        public static string FallEffect(int variant, string size) { return "waterfall_" + Clamp(variant, 1, 3) + "_" + size; }
        public static string SplashEffect(int variant, string size) { return FallEffect(variant, size) + "splash"; }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
