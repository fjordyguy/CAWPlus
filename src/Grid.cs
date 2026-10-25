using System;

namespace CawPlus
{
    /// <summary>Row-major float image. Index = y * W + x.</summary>
    public sealed class Grid
    {
        public readonly int W, H;
        public readonly float[] D;

        public Grid(int w, int h) { W = w; H = h; D = new float[w * h]; }

        public float this[int x, int y] { get { return D[y * W + x]; } set { D[y * W + x] = value; } }

        public Grid Clone() { Grid g = new Grid(W, H); Array.Copy(D, g.D, D.Length); return g; }

        public void MinMax(out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            for (int i = 0; i < D.Length; i++) { float v = D[i]; if (v < min) min = v; if (v > max) max = v; }
        }

        public void Normalize()
        {
            float lo, hi; MinMax(out lo, out hi);
            float r = hi - lo; if (r <= 1e-9f) r = 1;
            for (int i = 0; i < D.Length; i++) D[i] = (D[i] - lo) / r;
        }

        public void Clamp01()
        {
            for (int i = 0; i < D.Length; i++) { float v = D[i]; D[i] = v < 0 ? 0 : (v > 1 ? 1 : v); }
        }

        public static float Ramp(float v, float a, float b)
        {
            float t = (v - a) / (b - a);
            return t < 0 ? 0 : (t > 1 ? 1 : t);
        }

        // ---------- Gaussian blur (3 box passes, O(n) regardless of sigma) ----------

        /// <summary>Bilinear value at fractional cell (x, y), clamped to the grid.</summary>
        public float At(float x, float y)
        {
            x = Math.Max(0, Math.Min(W - 1.001f, x)); y = Math.Max(0, Math.Min(H - 1.001f, y));
            int ix = (int)x, iy = (int)y; float fx = x - ix, fy = y - iy;
            int i = iy * W + ix;
            float a = D[i] + (D[i + 1] - D[i]) * fx, b = D[i + W] + (D[i + W + 1] - D[i + W]) * fx;
            return a + (b - a) * fy;
        }

        public Grid Blur(float sigma)
        {
            Grid g = Clone();
            if (sigma < 0.3f) return g;
            int[] boxes = BoxesForGauss(sigma, 3);
            float[] tmp = new float[D.Length];
            for (int i = 0; i < 3; i++)
            {
                int r = (boxes[i] - 1) / 2;
                BoxH(g.D, tmp, W, H, r);
                BoxV(tmp, g.D, W, H, r);
            }
            return g;
        }

        private static int[] BoxesForGauss(float sigma, int n)
        {
            double wIdeal = Math.Sqrt((12 * sigma * sigma / n) + 1);
            int wl = (int)Math.Floor(wIdeal); if (wl % 2 == 0) wl--;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            int[] sizes = new int[n];
            for (int i = 0; i < n; i++) sizes[i] = i < m ? wl : wu;
            return sizes;
        }

        private static void BoxH(float[] src, float[] dst, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float acc = 0;
                for (int k = -r; k <= r; k++) acc += src[row + Clampi(k, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    dst[row + x] = acc * inv;
                    acc += src[row + Clampi(x + r + 1, 0, w - 1)] - src[row + Clampi(x - r, 0, w - 1)];
                }
            }
        }

        private static void BoxV(float[] src, float[] dst, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int k = -r; k <= r; k++) acc += src[Clampi(k, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * inv;
                    acc += src[Clampi(y + r + 1, 0, h - 1) * w + x] - src[Clampi(y - r, 0, h - 1) * w + x];
                }
            }
        }

        public static int Clampi(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ---------- smooth value noise ----------

        /// <summary>Smooth random field in 0..1 with features roughly 'cell' pixels across.</summary>
        public static Grid ValueNoise(int w, int h, float cell, Random rng)
        {
            Grid g = new Grid(w, h);
            AddValueNoise(g, cell, 1f, rng);
            return g;
        }

        /// <summary>Adds amp x value noise to g in place (no full-size temporary: at 2048 m each one is 16 MB, and CAW's
        /// 32-bit process ran out of room for them, the tester's 2026-10-24 test).</summary>
        private static void AddValueNoise(Grid g, float cell, float amp, Random rng)
        {
            if (cell < 1f) cell = 1f;
            int w = g.W, h = g.H;
            int gw = (int)(w / cell) + 3, gh = (int)(h / cell) + 3;
            float[] lattice = new float[gw * gh];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();
            for (int y = 0; y < h; y++)
            {
                float fy = y / cell; int iy = (int)fy; float ty = S(fy - iy);
                for (int x = 0; x < w; x++)
                {
                    float fx = x / cell; int ix = (int)fx; float tx = S(fx - ix);
                    float a = lattice[iy * gw + ix], b = lattice[iy * gw + ix + 1];
                    float c = lattice[(iy + 1) * gw + ix], d = lattice[(iy + 1) * gw + ix + 1];
                    g.D[y * w + x] += ((a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty) * amp;
                }
            }
        }

        /// <summary>
        /// Fractal value noise in 0..1: 'octaves' layers, each half the size and half the strength of the last.
        /// Gives ragged, natural-looking patch edges instead of round blobs.
        /// </summary>
        public static Grid Fbm(int w, int h, float cell, int octaves, Random rng)
        {
            Grid sum = new Grid(w, h);
            float amp = 1f;
            for (int o = 0; o < octaves; o++)
            {
                AddValueNoise(sum, cell, amp, rng);
                cell *= 0.5f; amp *= 0.5f;
            }
            sum.Normalize();
            return sum;
        }

        private static float S(float t) { return t * t * (3 - 2 * t); }
    }
}
