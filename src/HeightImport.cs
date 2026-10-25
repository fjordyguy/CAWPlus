using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// Puts a height grid (metres, one value per height-image vertex) into the open world through CAW's own height
    /// import: CAW's grey -> metres mapping is measured with a ramp first, then the map is imported and checked, up to
    /// three passes. Used by Apply terrain and by Generate Roads (smoothing under roads, lot pads).
    /// </summary>
    internal static class HeightImport
    {
        /// <summary>Returns the mean error (m); throws if CAW's heights can't be read or the import misses by over 5 m.
        /// clipNote: a warning when the world's height scale cuts the map's peaks off ("" if not).</summary>
        public static float Apply(object world, TerrainInfo ti, Grid wanted, int W, int H, ProgressFn progress, out string clipNote)
        {
            string tmp = TerrainSampler.TempDir;
            clipNote = "";
            if (progress != null) progress("Calibrating CAW's height import", 88);
            float a, b;
            Calibrate(world, ti, W, H, tmp, out a, out b);
            Log.Info(string.Format("Height import: height = {0:F3} * grey + {1:F3} (grey 0..1)", a, b));
            // the world's height scale caps what it can hold: CAW's New World dialog sets it (100/200/300 m or typed)
            float hiGen, loGen; wanted.MinMax(out loGen, out hiGen);
            if (hiGen > a + b + 0.5f)
            {
                clipNote = string.Format(" This world's height scale is {0:F0} m, so the map's peaks above it ({1:F0} m) are cut off: lower Max height, " +
                    "or make the world with a height scale of {2} m (File > New World).", a + b, hiGen, Math.Max(100, (int)Math.Ceiling(hiGen / 100f) * 100));
                Log.Info("Height import:" + clipNote);
            }
            float err = 0;
            for (int pass = 1; pass <= 3; pass++)
            {
                if (progress != null) progress("Importing heights (pass " + pass + ")", 90 + pass * 3);
                Grid img = new Grid(W, H);
                for (int i = 0; i < img.D.Length; i++) img.D[i] = (wanted.D[i] - b) / a;
                string path = Path.Combine(tmp, "height_generated.png");
                ImageIO.WriteGray16Png(img, path);
                if (!Caw.ImportHeightImage(world, path)) throw new IOException("CAW refused the generated height image.");
                // check, and refit if CAW scaled the image differently from the ramp
                float a2, b2;
                err = Measure(world, ti, img, wanted, W, H, out a2, out b2);
                Log.Info(string.Format("Height import pass {0}: mean error {1:F2} m (refit {2:F3} * grey + {3:F3})", pass, err, a2, b2));
                if (err < 0.5f) break;
                if (Math.Abs(a2) > 1e-3f) { a = a2; b = b2; } // CAW's actual mapping for this import; redo with it
            }
            // a big error means the heights didn't land (2026-10-22 log: 125 m): the caller puts the backup back
            if (err > 5f) throw new InvalidOperationException(string.Format("the heights didn't import correctly (mean error {0:F0} m)", err));
            return err;
        }

        /// <summary>The world's current heights at every height-image vertex (W x H), read from CAW.</summary>
        public static Grid ReadVertices(object world, TerrainInfo ti, int W, int H, ProgressFn progress)
        {
            HeightFn heightAt = Caw.MakeHeightFn(world, "GetTerrainHeight");
            Grid g = new Grid(W, H);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float wx, wz; PixelWorld(ti, W, H, x, y, out wx, out wz);
                    g.D[y * W + x] = heightAt(wx, wz);
                }
                if ((y & 63) == 0 && progress != null) progress("Reading the heights", 5 + 70 * y / H);
            }
            return g;
        }

        /// <summary>The world's current heights as a grid of height-image vertices (W x H), from the paint-map samples.</summary>
        public static Grid CurrentHeights(TerrainInfo ti, int W, int H)
        {
            Grid g = new Grid(W, H);
            float sx = (float)ti.Heights.W / (W - 1), sy = (float)ti.Heights.H / (H - 1);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    g[x, y] = TerrainGen.Sample(ti.Heights, x * sx - 0.5f, y * sy - 0.5f);
            return g;
        }

        /// <summary>World (x, z) of height-image pixel (px, py).</summary>
        private static void PixelWorld(TerrainInfo ti, int W, int H, float px, float py, out float x, out float z)
        {
            float wx, wz;
            TerrainSampler.Map(ti.Orientation, px / (W - 1), py / (H - 1), out wx, out wz);
            x = ti.Origin + wx * ti.WorldSize; z = ti.Origin + wz * ti.WorldSize;
        }

        /// <summary>
        /// Imports a known ramp and reads heights back, to learn grey -> metres. The ramp runs along both axes (0.7 u +
        /// 0.3 v), so the reading also shows which way the image lies in the world: on a flat new world the orientation
        /// can't be told from the terrain beforehand (the tester's 2026-10-22 log: "orientation match is weak (0.00)", then a
        /// fit of 94 x grey - 116 and a 125 m error, imported anyway). The readings must follow the ramp closely
        /// (correlation 0.98), else CAW's heights aren't ready: wait and read again, then give up rather than import.
        /// </summary>
        private static void Calibrate(object world, TerrainInfo ti, int W, int H, string tmp, out float a, out float b)
        {
            Grid ramp = new Grid(W, H);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) ramp[x, y] = 0.7f * x / (W - 1) + 0.3f * y / (H - 1);
            string path = Path.Combine(tmp, "height_ramp.png");
            ImageIO.WriteGray16Png(ramp, path);
            if (!Caw.ImportHeightImage(world, path)) throw new IOException("CAW refused the calibration height image.");
            HeightFn heightAt = Caw.MakeHeightFn(world, "GetTerrainHeight");
            double bestR = -2; int bestT = ti.Orientation; float bestO = ti.Origin; a = 0; b = 0;
            // the terrain reading can also get the origin wrong on flat or sea-ringed land (logs: origin -1024 at
            // correlation 0.001): then half the samples fall off the map and the ramp reads ~0.67 (the tester, Aurora Skies, #167)
            float[] origins = ti.Origin == 0 ? new[] { 0f, -ti.WorldSize / 2 } : new[] { ti.Origin, 0f };
            for (int attempt = 0; attempt < 4 && bestR < 0.98; attempt++)
            {
                if (attempt > 0) { Application.DoEvents(); Thread.Sleep(400); Application.DoEvents(); }
                foreach (float o in origins)
                for (int t = 0; t < 8; t++)
                {
                    Random r = new Random(5);
                    double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0; int n = 0;
                    for (int k = 0; k < 200; k++)
                    {
                        int px = 4 + r.Next(W - 8), py = 4 + r.Next(H - 8);
                        float wx, wz; TerrainSampler.Map(t, (float)px / (W - 1), (float)py / (H - 1), out wx, out wz);
                        float hgt = heightAt(o + wx * ti.WorldSize, o + wz * ti.WorldSize), v = ramp[px, py];
                        sx += v; sy += hgt; sxx += v * v; syy += (double)hgt * hgt; sxy += v * hgt; n++;
                    }
                    double cov = n * sxy - sx * sy, vx = n * sxx - sx * sx, vy = n * syy - sy * sy;
                    double rr = vx > 0 && vy > 0 ? cov / Math.Sqrt(vx * vy) : 0;
                    if (rr > bestR)
                    {
                        bestR = rr; bestT = t; bestO = o;
                        a = (float)(cov / vx); b = (float)((sy - a * sx) / n);
                    }
                }
            }
            Log.Info(string.Format("Height calibration: correlation {0:F3}, image orientation {1}, origin {2} (terrain reading gave {3}, origin {4})", bestR, bestT, bestO, ti.Orientation, ti.Origin));
            if (bestR < 0.98)
                throw new InvalidOperationException(string.Format("CAW's heights didn't follow the calibration image (correlation {0:F2}); " +
                    "the world may still be loading. Nothing was changed; try Apply terrain again.", bestR));
            if (bestT != ti.Orientation) { Log.Info("Height calibration: using orientation " + bestT + " (from the calibration image)"); ti.Orientation = bestT; }
            if (bestO != ti.Origin) { Log.Info("Height calibration: using origin " + bestO + " (from the calibration image)"); ti.Origin = bestO; }
            if (a < 1) throw new InvalidOperationException("The world's height range is too small to calibrate (" + a + " m).");
        }

        /// <summary>Fits CAW height = a·grey + b at 200 points; returns mean |height − wanted|.</summary>
        private static float Measure(object world, TerrainInfo ti, Grid img, Grid wanted, int W, int H, out float a, out float b)
        {
            HeightFn heightAt = Caw.MakeHeightFn(world, "GetTerrainHeight");
            Random r = new Random(5);
            double sx = 0, sy = 0, sxx = 0, sxy = 0, e = 0; int n = 0;
            for (int k = 0; k < 200; k++)
            {
                int px = 4 + r.Next(W - 8), py = 4 + r.Next(H - 8);
                float x, z; PixelWorld(ti, W, H, px, py, out x, out z);
                float hgt = heightAt(x, z), v = img[px, py];
                sx += v; sy += hgt; sxx += v * v; sxy += v * hgt; n++;
                e += Math.Abs(hgt - wanted[px, py]);
            }
            double den = n * sxx - sx * sx;
            a = den != 0 ? (float)((n * sxy - sx * sy) / den) : 0;
            b = (float)((sy - a * sx) / n);
            return (float)(e / n);
        }

    }
}
