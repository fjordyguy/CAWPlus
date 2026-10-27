using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CawPlus
{
    public enum PaintRole { Meadow = 0, Shore = 1, Dirt = 2, Rock = 3 }

    public sealed class PaintSettings
    {
        public int Seed = 7;
        // All sliders are 0..100, 50 = the default look from the first preview.
        public float Rock = 50;      // how readily slopes become rock
        public float Shore = 50;     // width of sand band above the waterline
        public float Dirt = 50;      // hollows / drainage / forest floor
        public float Meadow = 50;    // patches of the second grass
        public float Softness = 50;  // edge blur between layers
        public float Detail = 50;    // pattern size: higher = finer, more intricate patches (50 = default)
        public float WaterLevel = 0; // metres; filled in automatically from CAW
        public Grid WaterOffset;     // optional per-pixel addition to WaterLevel (ponds above sea level)
    }

    /// <summary>
    /// Rule-based terrain paint (the built-in rules; styles use StyledGenerator), with fractal noise and edge breakup
    /// for natural blending.
    /// Input: height grid in metres (one cell = cellMeters). Output: one 0..1 weight grid per role.
    /// </summary>
    public static class PaintEngine
    {
        public static readonly PaintRole[] Roles = { PaintRole.Meadow, PaintRole.Shore, PaintRole.Dirt, PaintRole.Rock };

        public static string RoleLabel(PaintRole r)
        {
            switch (r)
            {
                case PaintRole.Rock: return "Rock (steep slopes, high ground)";
                case PaintRole.Shore: return "Shore (sand at the waterline)";
                case PaintRole.Dirt: return "Dirt (hollows, drainage, scree)";
                default: return "Meadow (second grass, patchy)";
            }
        }

        public static Color RoleColor(PaintRole r)
        {
            switch (r)
            {
                case PaintRole.Rock: return Color.FromArgb(120, 118, 112);
                case PaintRole.Shore: return Color.FromArgb(150, 135, 100);
                case PaintRole.Dirt: return Color.FromArgb(98, 80, 58);
                default: return Color.FromArgb(118, 138, 62);
            }
        }

        public static readonly Color BaseColor = Color.FromArgb(88, 112, 52);
        public static readonly Color WaterColor = Color.FromArgb(55, 85, 110);

        public static Dictionary<PaintRole, Grid> Compute(Grid h, float cellMeters, PaintSettings s, ProgressFn progress)
        {
            int W = h.W, H = h.H, N = W * H;
            float px = 1f / Math.Max(cellMeters, 1e-3f); // pixels per metre
            Report(progress, "Slope", 5);

            // --- slope in degrees (central differences) ---
            Grid slope = new Grid(W, H);
            for (int y = 0; y < H; y++)
            {
                int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                for (int x = 0; x < W; x++)
                {
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1);
                    float gx = (h[xp, y] - h[xm, y]) / ((xp - xm) * cellMeters);
                    float gy = (h[x, yp] - h[x, ym]) / ((yp - ym) * cellMeters);
                    slope.D[y * W + x] = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI);
                }
            }
            Grid slopeS = slope.Blur(2f * px);
            slope = null;

            Report(progress, "Concavity", 20);
            // positive = hollow / valley, negative = ridge: broad (12 m) for valleys, fine (4 m) for gullies and knolls
            Grid broad = h.Blur(12f * px), mid = h.Blur(4f * px), fine = h.Blur(1f * px);
            Grid curv = new Grid(W, H), curvF = new Grid(W, H);
            for (int i = 0; i < N; i++) { curv.D[i] = broad.D[i] - fine.D[i]; curvF.D[i] = mid.D[i] - fine.D[i]; }
            broad = null; mid = null; fine = null;

            Report(progress, "Noise", 35);
            Random rng = new Random(s.Seed);
            // Pattern size: Detail 50 = 1x; 100 = half-size patches, 0 = 1.6x. Real ground cover is intricate: patches
            // of a few metres inside larger ones, so each layer mixes a broad and a fine scale.
            float ps = Math.Max(0.4f, 1.6f - s.Detail * 0.012f);
            // (the finest patterns no smaller than ~3 m, the size CAW's 1 m paint maps draw as hard flecks, and never
            // under two cells, which on the preview's coarse grid is random speckle the painted map doesn't have)
            Grid n1 = Grid.Fbm(W, H, Math.Max(2f, 28f * ps * px), 4, rng);        // meadow patches
            Grid n2 = Grid.Fbm(W, H, Math.Max(2f, 12f * ps * px), 3, rng);        // variation inside layers
            Grid nR = Grid.Fbm(W, H, Math.Max(2f, 16f * ps * px), 4, rng);        // where rock breaks through
            Grid nD = Grid.Fbm(W, H, Math.Max(2f, Math.Max(4f, 5f * ps) * px), 3, rng); // fine breakup of every layer
            Grid nEdge = Grid.Fbm(W, H, Math.Max(2f, Math.Max(3f, 2.5f * ps) * px), 2, rng); // ragged edges

            float hMin, hMax; h.MinMax(out hMin, out hMax);
            float highStart = hMin + (hMax - hMin) * 0.79f;
            float highFull = hMin + (hMax - hMin) * 0.98f;

            float rockMin = 34f - 0.2f * s.Rock, rockMax = rockMin + 16f;
            float highRockStrength = Math.Min(1f, 0.8f * s.Rock / 50f);
            float sandWidth = 0.5f + s.Shore * 0.04f;
            float dirtK = s.Dirt / 50f;
            float meadowLo = 0.62f - s.Meadow * 0.006f, meadowHi = meadowLo + 0.22f;
            float meadowMax = 0.45f + s.Meadow * 0.004f; // a second grass mixes in; it never fully replaces the base

            Report(progress, "Layers", 55);
            Grid rock = new Grid(W, H), sand = new Grid(W, H), dirt = new Grid(W, H), meadow = new Grid(W, H);
            for (int i = 0; i < N; i++)
            {
                float hv = h.D[i], nn2 = n2.D[i], d5 = nD.D[i], e = nEdge.D[i] - 0.5f;
                // slope seen through two noises: large wander (+-9 deg) makes outcrops irregular, fine (+-5 deg) ragged
                float sl = slopeS.D[i] + (nR.D[i] - 0.5f) * 18f + (d5 - 0.5f) * 10f;
                float above = hv - s.WaterLevel - (s.WaterOffset != null ? s.WaterOffset.D[i] : 0) + e * 0.8f + (d5 - 0.5f) * 0.6f;
                float cv = curv.D[i] + e * 0.6f, cf = curvF.D[i];

                // Rock: steep ground, but soil and grass hold in small hollows and where the fine noise is low, so
                // a slope becomes outcrops and grassy breaks rather than one grey sheet; knolls and edges stay bare.
                float holds = Grid.Ramp(cf, 0.05f, 0.35f) * 0.7f;                       // small hollows keep soil
                float r = Grid.Ramp(sl, rockMin, rockMax) * Grid.Ramp(d5 + 0.25f - holds + Grid.Ramp(-cf, 0.05f, 0.3f) * 0.3f, 0.3f, 0.6f);
                float hr = Grid.Ramp(hv, highStart, highFull) * Grid.Ramp(sl, 8, 20) * highRockStrength * Grid.Ramp(d5 + 0.2f, 0.35f, 0.65f);
                rock.D[i] = Math.Max(r, hr) * (0.8f + 0.25f * nn2);

                sand.D[i] = (1 - Grid.Ramp(above, 0.3f, sandWidth)) * (1 - Grid.Ramp(slopeS.D[i], 10, 22)) * (0.75f + 0.35f * d5) * (s.Shore > 0 ? 1 : 0);

                // Dirt: drainage lines (gullies, fine hollows), bare patches in hollows, and scree in patches below rock.
                float gully = Grid.Ramp(cf, 0.15f, 0.5f) * Grid.Ramp(slopeS.D[i], 4, 12) * (1 - Grid.Ramp(slopeS.D[i], 30, 40)) * Grid.Ramp(d5, 0.35f, 0.6f);
                float hollow = Grid.Ramp(cv, 0.8f, 2.5f) * (1 - Grid.Ramp(slopeS.D[i], 20, 30)) * Grid.Ramp(nn2 * 0.6f + d5 * 0.4f, 0.45f, 0.75f);
                float scree = Grid.Ramp(sl, rockMin - 14, rockMin - 4) * (1 - r) * Grid.Ramp(d5, 0.55f, 0.8f) * 0.8f;
                dirt.D[i] = Math.Max(Math.Max(gully * 0.8f, hollow), scree) * dirtK;

                // Meadow: patches at two scales, favouring flat, damp ground, with partial, mottled cover.
                float damp = 0.85f + 0.3f * Grid.Ramp(cv, 0f, 1.5f);
                float pat = (0.65f * n1.D[i] + 0.35f * d5) * damp + e * 0.08f;
                meadow.D[i] = Grid.Ramp(pat, meadowLo, meadowHi) * (1 - Grid.Ramp(slopeS.D[i], 18, 28))
                              * meadowMax * (0.7f + 0.3f * nn2) * (s.Meadow > 0 ? 1 : 0);
            }

            Report(progress, "Softening", 75);
            float soft = (0.8f + s.Softness * 0.07f); // metres; 4.3 m at the default (0.8-7.8 m)
            Dictionary<PaintRole, Grid> result = new Dictionary<PaintRole, Grid>();
            // Soft edges, then fine mottling added back inside the transitions only (cores stay solid), so edges are
            // feathered and intricate rather than smooth blurred lines.
            Grid mottle = new Grid(W, H);
            for (int i = 0; i < N; i++) mottle.D[i] = 0.6f * (nEdge.D[i] - 0.5f) + 0.4f * (nD.D[i] - 0.5f);
            result[PaintRole.Rock] = Mottle(Soft(rock, soft * px), mottle, 1.6f);
            result[PaintRole.Shore] = Mottle(Soft(sand, soft * px), mottle, 1.0f);
            result[PaintRole.Dirt] = Mottle(Soft(dirt, soft * px), mottle, 1.6f);
            result[PaintRole.Meadow] = Mottle(Soft(meadow, soft * px), mottle, 1.2f);
            Report(progress, "Done", 100);
            return result;
        }

        private static Grid Soft(Grid g, float sigma) { g.Clamp01(); Grid b = g.Blur(sigma); b.Clamp01(); return b; }

        /// <summary>Perturbs mid values (edges) by the mottle noise; 0 and 1 stay put.</summary>
        private static Grid Mottle(Grid g, Grid mottle, float k)
        {
            for (int i = 0; i < g.D.Length; i++)
            {
                float v = g.D[i];
                g.D[i] = Math.Max(0, Math.Min(1, v + mottle.D[i] * k * 4 * v * (1 - v)));
            }
            return g;
        }

        /// <summary>Visible share of each role when stacked as intended (Rock over Dirt over Shore over Meadow).</summary>
        public static Dictionary<PaintRole, Grid> VisibleShares(Dictionary<PaintRole, Grid> masks)
        {
            PaintRole[] intendedTopDown = { PaintRole.Rock, PaintRole.Dirt, PaintRole.Shore, PaintRole.Meadow };
            Grid any = masks[PaintRole.Rock];
            Dictionary<PaintRole, Grid> vis = new Dictionary<PaintRole, Grid>();
            foreach (PaintRole r in intendedTopDown) vis[r] = new Grid(any.W, any.H);
            for (int i = 0; i < any.D.Length; i++)
            {
                float remaining = 1f;
                foreach (PaintRole r in intendedTopDown)
                {
                    float a = masks[r].D[i];
                    vis[r].D[i] = a * remaining;
                    remaining *= (1 - a);
                }
            }
            return vis;
        }

        /// <summary>
        /// Keeps each 256 m terrain chunk to at most 'limit' painted layers (CAW's base layer counts as one; 'others' =
        /// other layers' current maps; 'edge' = the world-edge layer is painted too). Where a chunk has more, the Auto-Paint
        /// layer with the least paint there is left out of that chunk, faded over 24 m outside it so no seam shows.
        /// Returns a line for the log.
        /// </summary>
        public static string LimitPerChunk<TKey>(Dictionary<TKey, Grid> stacked, Dictionary<int, Grid> others, bool edge, float cell, int limit)
        {
            Grid any = null; foreach (Grid g in stacked.Values) { any = g; break; }
            if (any == null) return "Layers per chunk: nothing painted";
            int W = any.W, H = any.H;
            int cp = Math.Max(1, (int)Math.Round(256f / cell)), nx = (W + cp - 1) / cp, ny = (H + cp - 1) / cp;
            const float Used = 3f / 255f;
            int over = 0, removed = 0, still = 0, worst = 0;
            Dictionary<TKey, Grid> weight = new Dictionary<TKey, Grid>();
            int fade = Math.Max(1, (int)Math.Round(24f / cell));
            for (int cy = 0; cy < ny; cy++)
                for (int cx = 0; cx < nx; cx++)
                {
                    int x0 = cx * cp, y0 = cy * cp, x1 = Math.Min(W, x0 + cp), y1 = Math.Min(H, y0 + cp);
                    int n = 1 + (edge && (cx == 0 || cy == 0 || cx == nx - 1 || cy == ny - 1) ? 1 : 0);
                    foreach (Grid o in others.Values) if (AnyAbove(o, x0, y0, x1, y1, Used)) n++;
                    List<KeyValuePair<double, TKey>> ours = new List<KeyValuePair<double, TKey>>();
                    foreach (KeyValuePair<TKey, Grid> kv in stacked)
                    {
                        double sum = 0; bool hit = false;
                        for (int y = y0; y < y1; y++)
                            for (int x = x0; x < x1; x++) { float v = kv.Value.D[y * W + x]; sum += v; if (v > Used) hit = true; }
                        if (hit) ours.Add(new KeyValuePair<double, TKey>(sum, kv.Key));
                    }
                    n += ours.Count;
                    worst = Math.Max(worst, n);
                    if (n <= limit) continue;
                    over++;
                    ours.Sort(delegate (KeyValuePair<double, TKey> a, KeyValuePair<double, TKey> b) { return a.Key.CompareTo(b.Key); });
                    for (int k = 0; k < ours.Count && n > limit; k++, n--)
                    {
                        Grid w;
                        if (!weight.TryGetValue(ours[k].Value, out w)) { w = new Grid(W, H); for (int i = 0; i < w.D.Length; i++) w.D[i] = 1; weight[ours[k].Value] = w; }
                        // 0 inside the chunk, rising to 1 over 'fade' pixels outside it
                        for (int y = Math.Max(0, y0 - fade); y < Math.Min(H, y1 + fade); y++)
                            for (int x = Math.Max(0, x0 - fade); x < Math.Min(W, x1 + fade); x++)
                            {
                                int dx = x < x0 ? x0 - x : x >= x1 ? x - x1 + 1 : 0, dy = y < y0 ? y0 - y : y >= y1 ? y - y1 + 1 : 0;
                                float f = Math.Min(1f, Math.Max(dx, dy) / (float)fade);
                                if (f < w.D[y * W + x]) w.D[y * W + x] = f;
                            }
                        removed++;
                    }
                    if (n > limit) still++;
                }
            foreach (KeyValuePair<TKey, Grid> kv in weight)
            {
                Grid m = stacked[kv.Key];
                for (int i = 0; i < m.D.Length; i++) m.D[i] *= kv.Value.D[i];
            }
            return string.Format("Layers per 256 m chunk: limit {0}, most used {1}; {2} chunk(s) over, {3} Auto-Paint layer(s) left out there{4}",
                limit, worst, over, removed, still > 0 ? string.Format("; {0} chunk(s) still over (other layers)", still) : "");
        }

        private static bool AnyAbove(Grid g, int x0, int y0, int x1, int y1, float t)
        {
            if (g.W <= x0 || g.H <= y0) return false;
            for (int y = y0; y < Math.Min(y1, g.H); y++)
                for (int x = x0; x < Math.Min(x1, g.W); x++) if (g.D[y * g.W + x] > t) return true;
            return false;
        }

        /// <summary>
        /// Like ToCawStack, but each role may paint with several textures (EA's worlds: Sunset Valley's meadow is four
        /// grasses) and an EA base texture may lie under everything. 'layers' gives, per role, its CAW layer indices with
        /// their share of the role (main texture first); each role's visible ground is split between its textures in
        /// irregular patches (fBm, ~'patchPx' pixels across) in proportion to the shares. baseLayer (-1 = none) shows
        /// wherever no role does. Returns each CAW layer's opacity map, keyed by layer index.
        /// </summary>
        public static Dictionary<int, Grid> ToCawLayers(Dictionary<PaintRole, Grid> masks, Dictionary<PaintRole, List<KeyValuePair<int, float>>> layers,
                                                        int baseLayer, float patchPx, int seed)
        {
            PaintRole[] intendedTopDown = { PaintRole.Rock, PaintRole.Dirt, PaintRole.Shore, PaintRole.Meadow };
            Grid any = null; foreach (Grid g in masks.Values) { any = g; break; }
            int W = any.W, H = any.H, N = any.D.Length;
            // patch fields: one per extra texture, at a coarse resolution (they vary over ~patchPx cells) and read
            // bilinearly, so they don't each take a full-size grid
            Random rng = new Random(seed * 31 + 5);
            int step = Math.Max(1, (int)(patchPx / 8));
            int cw = (W - 1) / step + 2, ch = (H - 1) / step + 2;
            Dictionary<int, Grid> field = new Dictionary<int, Grid>();
            foreach (List<KeyValuePair<int, float>> l in layers.Values)
                if (l.Count > 1) foreach (KeyValuePair<int, float> kv in l) field[kv.Key] = Grid.Fbm(cw, ch, Math.Max(4, patchPx) / step, 4, rng);
            Dictionary<int, Grid> visible = new Dictionary<int, Grid>();
            foreach (List<KeyValuePair<int, float>> l in layers.Values) foreach (KeyValuePair<int, float> kv in l) visible[kv.Key] = new Grid(W, H);
            if (baseLayer >= 0) visible[baseLayer] = new Grid(W, H);
            float inv = 1f / step;
            for (int i = 0; i < N; i++)
            {
                float remaining = 1f;
                float fx = (i % W) * inv, fy = (i / W) * inv;
                foreach (PaintRole r in intendedTopDown)
                {
                    Grid m; List<KeyValuePair<int, float>> l;
                    if (!masks.TryGetValue(r, out m)) continue;
                    float a = m.D[i], c = a * remaining;
                    remaining *= 1 - a; // (a role with no layer still covers what lies under it, as in ToCawStack)
                    if (!layers.TryGetValue(r, out l) || l.Count == 0) continue;
                    if (l.Count == 1) { visible[l[0].Key].D[i] += c; continue; }
                    // weights: share x a patchy field (exp makes patches, not an even mix)
                    float sum = 0;
                    foreach (KeyValuePair<int, float> kv in l) sum += Math.Max(1e-4f, kv.Value) * (float)Math.Exp(3.2 * (field[kv.Key].At(fx, fy) - 0.5));
                    foreach (KeyValuePair<int, float> kv in l)
                        visible[kv.Key].D[i] += c * Math.Max(1e-4f, kv.Value) * (float)Math.Exp(3.2 * (field[kv.Key].At(fx, fy) - 0.5)) / sum;
                }
                if (baseLayer >= 0) visible[baseLayer].D[i] = remaining;
            }
            // visible shares -> opacities in CAW's order (higher index on top), written over the shares in place
            List<int> order = new List<int>(visible.Keys);
            order.Sort();
            Dictionary<int, Grid> outp = new Dictionary<int, Grid>();
            foreach (int k in order) outp[k] = visible[k];
            for (int i = 0; i < N; i++)
            {
                float rem = 1f;
                for (int k = order.Count - 1; k >= 0; k--)
                {
                    float[] d = visible[order[k]].D;
                    float c = d[i];
                    d[i] = rem > 1e-4f ? Math.Min(1, c / rem) : 0f;
                    rem -= c; if (rem < 0) rem = 0;
                }
            }
            return outp;
        }

        /// <summary>
        /// The intended look stacks (bottom -> top) Meadow, Shore, Dirt, Rock. CAW stacks paint layers in its own
        /// order, so convert each role's weight into the opacity it needs at its CAW position to give the same
        /// final picture. 'cawBottomToTop' = mapped roles sorted by CAW layer index. Unmapped roles fall back to base.
        /// </summary>
        public static Dictionary<PaintRole, Grid> ToCawStack(Dictionary<PaintRole, Grid> masks, IList<PaintRole> cawBottomToTop)
        {
            PaintRole[] intendedTopDown = { PaintRole.Rock, PaintRole.Dirt, PaintRole.Shore, PaintRole.Meadow };
            Grid any = masks[PaintRole.Rock];
            int N = any.D.Length;
            Dictionary<PaintRole, Grid> outp = new Dictionary<PaintRole, Grid>();
            foreach (PaintRole r in cawBottomToTop) outp[r] = new Grid(any.W, any.H);
            Dictionary<PaintRole, float> contrib = new Dictionary<PaintRole, float>();

            for (int i = 0; i < N; i++)
            {
                // final visible share of each role under the intended stacking
                float remaining = 1f;
                foreach (PaintRole r in intendedTopDown)
                {
                    float a = masks[r].D[i];
                    contrib[r] = a * remaining;
                    remaining *= (1 - a);
                }
                // re-express those shares as opacities in CAW's stacking order (top layer first)
                remaining = 1f;
                for (int k = cawBottomToTop.Count - 1; k >= 0; k--)
                {
                    PaintRole r = cawBottomToTop[k];
                    float c = contrib[r];
                    float alpha = remaining > 1e-4f ? c / remaining : 0f;
                    if (alpha > 1) alpha = 1;
                    outp[r].D[i] = alpha;
                    remaining -= c;
                    if (remaining < 0) remaining = 0;
                }
            }
            return outp;
        }

        private static void Report(ProgressFn p, string stage, int pct) { if (p != null) p(stage, pct); }

        /// <summary>
        /// Colour preview with hillshade; 'order' = roles bottom-to-top as they will stack in CAW. Each role is drawn in
        /// 'colours' (the mean colour of the texture chosen for it, #163) where given, over
        /// 'baseColour' (the base layer's texture), so the preview shows the world's own colours rather than role colours.
        /// </summary>
        public static Bitmap RenderPreview(Grid h, Dictionary<PaintRole, Grid> masks, IList<PaintRole> order,
                                           float waterLevel, float cellMeters, int size, Grid waterOffset,
                                           Dictionary<PaintRole, Color> colours, Color baseColour)
        {
            int W = h.W, H = h.H;
            int ow = Math.Min(size, W), oh = Math.Min(size, H);
            Bitmap bmp = new Bitmap(ow, oh, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            byte[] row = new byte[bd.Stride];
            for (int oy = 0; oy < oh; oy++)
            {
                int y = (int)((oy + 0.5f) * H / oh);
                for (int ox = 0; ox < ow; ox++)
                {
                    int x = (int)((ox + 0.5f) * W / ow);
                    float r = baseColour.R, g = baseColour.G, b = baseColour.B;
                    foreach (PaintRole role in order)
                    {
                        float a = masks[role][x, y];
                        Color c;
                        if (colours == null || !colours.TryGetValue(role, out c)) c = RoleColor(role);
                        r = r * (1 - a) + c.R * a; g = g * (1 - a) + c.G * a; b = b * (1 - a) + c.B * a;
                    }
                    float hv = h[x, y];
                    if (hv < waterLevel + (waterOffset != null ? waterOffset[x, y] : 0)) { r = WaterColor.R; g = WaterColor.G; b = WaterColor.B; }

                    // hillshade (light from the top-left)
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1);
                    int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                    float gx = (h[xp, y] - h[xm, y]) / (2 * cellMeters), gy = (h[x, yp] - h[x, ym]) / (2 * cellMeters);
                    float shade = (gx + gy) / 1.41421f * 0.6f + 0.85f;
                    shade = shade < 0.5f ? 0.5f : (shade > 1.2f ? 1.2f : shade);

                    row[ox * 3 + 0] = ToByte(b * shade);
                    row[ox * 3 + 1] = ToByte(g * shade);
                    row[ox * 3 + 2] = ToByte(r * shade);
                }
                Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)oy * bd.Stride), bd.Stride);
            }
            bmp.UnlockBits(bd);
            return bmp;
        }

        /// <summary>
        /// A stack of CAW layers drawn bottom to top, each in its texture's colour at its opacity, over the base colour
        /// (#163: the preview shows what Paint world writes, variety and base textures included).
        /// </summary>
        public static Bitmap RenderStack(Grid h, IList<Grid> layers, IList<Color> colours, Color baseColour,
                                         float waterLevel, float cellMeters, int size, Grid waterOffset)
        {
            int W = h.W, H = h.H;
            int ow = Math.Min(size, W), oh = Math.Min(size, H);
            Bitmap bmp = new Bitmap(ow, oh, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            byte[] row = new byte[bd.Stride];
            for (int oy = 0; oy < oh; oy++)
            {
                int y = (int)((oy + 0.5f) * H / oh);
                for (int ox = 0; ox < ow; ox++)
                {
                    int x = (int)((ox + 0.5f) * W / ow);
                    float r = baseColour.R, g = baseColour.G, b = baseColour.B;
                    for (int k = 0; k < layers.Count; k++)
                    {
                        float a = layers[k][x, y];
                        if (a <= 0) continue;
                        Color c = colours[k];
                        r = r * (1 - a) + c.R * a; g = g * (1 - a) + c.G * a; b = b * (1 - a) + c.B * a;
                    }
                    if (h[x, y] < waterLevel + (waterOffset != null ? waterOffset[x, y] : 0)) { r = WaterColor.R; g = WaterColor.G; b = WaterColor.B; }
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1);
                    int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                    float gx = (h[xp, y] - h[xm, y]) / (2 * cellMeters), gy = (h[x, yp] - h[x, ym]) / (2 * cellMeters);
                    float shade = (gx + gy) / 1.41421f * 0.6f + 0.85f;
                    shade = shade < 0.5f ? 0.5f : (shade > 1.2f ? 1.2f : shade);
                    row[ox * 3 + 0] = ToByte(b * shade);
                    row[ox * 3 + 1] = ToByte(g * shade);
                    row[ox * 3 + 2] = ToByte(r * shade);
                }
                Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)oy * bd.Stride), bd.Stride);
            }
            bmp.UnlockBits(bd);
            return bmp;
        }

        private static byte ToByte(float v) { return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v)); }
    }
}
