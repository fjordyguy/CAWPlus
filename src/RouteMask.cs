using System;
using System.Collections.Generic;

namespace CawPlus
{
    /// <summary>
    /// Where Sims shouldn't walk: ground steeper than a limit, as EA blocks its mountains with CAW's Routable Surface
    /// paint. Works on a coarse grid (about 4 m); patches smaller than 'minArea' are dropped and gaps of one cell closed,
    /// so the blocked areas are whole slopes, not speckle. Water and anything 'keep' marks (roads, lots) stay walkable.
    /// </summary>
    internal static class RouteMask
    {
        /// <summary>
        /// heights: metres per map pixel, 'cell' metres per pixel; returns the blocked cells of a grid of 'step' pixels
        /// (W = ceil(heights.W / step)), and the cell size in metres.
        /// </summary>
        public static bool[] Steep(Grid heights, float cell, int step, float sea, float maxDeg, float minArea, bool[] keep, out int w, out int h)
        {
            w = (heights.W + step - 1) / step; h = (heights.H + step - 1) / step;
            float m = cell * step;
            // mean height per cell
            float[] g = new float[w * h]; int[] n = new int[w * h];
            for (int y = 0; y < heights.H; y++)
                for (int x = 0; x < heights.W; x++) { int c = (y / step) * w + x / step; g[c] += heights.D[y * heights.W + x]; n[c]++; }
            for (int c = 0; c < g.Length; c++) g[c] /= Math.Max(1, n[c]);
            double tanMax = Math.Tan(maxDeg * Math.PI / 180);
            bool[] steep = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int c = y * w + x;
                    if (g[c] < sea + 0.5f || (keep != null && keep[c])) continue;
                    int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1), y0 = Math.Max(0, y - 1), y1 = Math.Min(h - 1, y + 1);
                    double gx = (g[y * w + x1] - g[y * w + x0]) / ((x1 - x0) * m), gy = (g[y1 * w + x] - g[y0 * w + x]) / ((y1 - y0) * m);
                    steep[c] = Math.Sqrt(gx * gx + gy * gy) > tanMax;
                }
            // close one-cell gaps (dilate, then erode), keeping roads, lots and water open
            bool[] dil = Morph(steep, w, h, true), closed = Morph(dil, w, h, false);
            for (int c = 0; c < closed.Length; c++)
                if (closed[c] && (g[c] < sea + 0.5f || (keep != null && keep[c]))) closed[c] = false;
            // drop small patches
            int minCells = Math.Max(1, (int)(minArea / (m * m)));
            int[] label = new int[w * h];
            List<int> stack = new List<int>(), members = new List<int>();
            for (int s = 0; s < closed.Length; s++)
            {
                if (!closed[s] || label[s] != 0) continue;
                members.Clear(); stack.Clear(); stack.Add(s); label[s] = 1;
                while (stack.Count > 0)
                {
                    int c = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); members.Add(c);
                    int cx = c % w, cy = c / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int d = ny * w + nx;
                        if (closed[d] && label[d] == 0) { label[d] = 1; stack.Add(d); }
                    }
                }
                if (members.Count < minCells) foreach (int c in members) closed[c] = false;
            }
            return closed;
        }

        private static bool[] Morph(bool[] a, int w, int h, bool dilate)
        {
            bool[] o = new bool[a.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool v = !dilate;
                    for (int dy = -1; dy <= 1 && v != dilate; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            bool b = nx < 0 || ny < 0 || nx >= w || ny >= h ? a[y * w + x] : a[ny * w + nx];
                            if (b == dilate) { v = dilate; break; }
                        }
                    o[y * w + x] = v;
                }
            return o;
        }

        /// <summary>Marks cells within 'r' metres of a polyline (points in cell coordinates scaled by 'm' metres per cell).</summary>
        public static void MarkLine(bool[] keep, int w, int h, List<float[]> cellPts, float r, float m)
        {
            float rc = r / m;
            for (int i = 0; i + 1 < cellPts.Count; i++)
            {
                float ax = cellPts[i][0], ay = cellPts[i][1], bx = cellPts[i + 1][0], by = cellPts[i + 1][1];
                int x0 = Math.Max(0, (int)(Math.Min(ax, bx) - rc - 1)), x1 = Math.Min(w - 1, (int)(Math.Max(ax, bx) + rc + 1));
                int y0 = Math.Max(0, (int)(Math.Min(ay, by) - rc - 1)), y1 = Math.Min(h - 1, (int)(Math.Max(ay, by) + rc + 1));
                float dx = bx - ax, dy = by - ay, len2 = Math.Max(1e-6f, dx * dx + dy * dy);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float t = Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / len2));
                        float ex = ax + t * dx - px, ey = ay + t * dy - py;
                        if (ex * ex + ey * ey <= rc * rc) keep[y * w + x] = true;
                    }
            }
        }

        /// <summary>Marks cells within 'r' metres of a point (cell coordinates).</summary>
        public static void MarkDisc(bool[] keep, int w, int h, float cx, float cy, float r, float m)
        {
            float rc = r / m;
            for (int y = Math.Max(0, (int)(cy - rc - 1)); y <= Math.Min(h - 1, (int)(cy + rc + 1)); y++)
                for (int x = Math.Max(0, (int)(cx - rc - 1)); x <= Math.Min(w - 1, (int)(cx + rc + 1)); x++)
                {
                    float ex = x + 0.5f - cx, ey = y + 0.5f - cy;
                    if (ex * ex + ey * ey <= rc * rc) keep[y * w + x] = true;
                }
        }
    }
}
