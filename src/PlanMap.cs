using System;
using System.IO;

namespace CawPlus
{
    /// <summary>
    /// The painted plan of a world (#171): one PlanArea per cell of the road planner's 8 m grid (N x N, rows = map y),
    /// kept as Documents\CAWPlus\Worlds\&lt;world&gt;\plan.png (8-bit grey, value = area x 32).
    /// </summary>
    internal sealed class PlanMap
    {
        public readonly int N;
        public readonly byte[] Codes;

        public PlanMap(int n) { N = n; Codes = new byte[n * n]; }

        public static readonly string[] Names = { "Erase", "Town centre", "Houses", "Countryside", "Forest", "Park / open", "Keep clear", "Main road" };

        /// <summary>Paints a disc of radius 'r' cells round (x, y) (cells, fractional) with 'a' (None erases).</summary>
        public void Paint(float x, float y, float r, PlanArea a)
        {
            int x0 = Math.Max(0, (int)Math.Floor(x - r)), x1 = Math.Min(N - 1, (int)Math.Ceiling(x + r));
            int y0 = Math.Max(0, (int)Math.Floor(y - r)), y1 = Math.Min(N - 1, (int)Math.Ceiling(y + r));
            for (int j = y0; j <= y1; j++)
                for (int i = x0; i <= x1; i++)
                    if ((i - x) * (i - x) + (j - y) * (j - y) <= r * r) Codes[j * N + i] = (byte)a;
        }

        public bool Any { get { foreach (byte b in Codes) if (b != 0) return true; return false; } }

        public bool Has(PlanArea a) { foreach (byte b in Codes) if (b == (byte)a) return true; return false; }

        public void Clear() { Array.Clear(Codes, 0, Codes.Length); }

        /// <summary>The codes as the planner takes them (RoadSettings.Plan), or null if nothing is painted.</summary>
        public int[] ToSettings()
        {
            if (!Any) return null;
            int[] a = new int[Codes.Length];
            for (int i = 0; i < a.Length; i++) a[i] = Codes[i];
            return a;
        }

        public void Save(string path)
        {
            Grid g = new Grid(N, N);
            for (int i = 0; i < Codes.Length; i++) g.D[i] = Codes[i] * 32 / 255f;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ImageIO.WriteGrayPng(g, path);
        }

        /// <summary>The plan saved at 'path' at whatever size it was saved, or null if there is none.</summary>
        public static PlanMap Load(string path)
        {
            int w, h;
            try { if (!File.Exists(path) || !ImageIO.PngSize(path, out w, out h) || w != h || w < 2) return null; }
            catch (Exception) { return null; }
            PlanMap m = Load(path, w);
            return m.Any ? m : null;
        }

        /// <summary>The plan saved at 'path' if it is N x N, else an empty one.</summary>
        public static PlanMap Load(string path, int n)
        {
            PlanMap m = new PlanMap(n);
            try
            {
                if (!File.Exists(path)) return m;
                Grid g = ImageIO.ReadPngChannel0(path);
                if (g.W != n || g.H != n) return m;
                for (int i = 0; i < m.Codes.Length; i++)
                {
                    int v = (int)Math.Round(g.D[i] * 255 / 32);
                    m.Codes[i] = (byte)(v >= 0 && v <= (int)PlanArea.MainRoad ? v : 0);
                }
            }
            catch (Exception ex) { Log.Error("Reading the plan " + path + " failed", ex); }
            return m;
        }
    }
}
