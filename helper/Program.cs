// CAWPlusHelper: CAW Plus's terrain generator in a 64-bit .NET process (see CAWPlusHelper.csproj, src/HelperLink.cs).
//   CAWPlusHelper terrain <job folder>   reads job.txt, writes progress.txt, result.bin, checks.txt; exit code 0 = done
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CawPlus
{
    internal static class Log
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "CAWPlus");
        public static void Info(string msg) { Write("INFO  " + msg); }
        public static void Error(string msg, Exception ex) { Write("ERROR " + msg + (ex != null ? ": " + ex : "")); }
        public static string JobLog;  // the job folder's log.txt: CAW Plus copies it into CAWPlus.log
        private static void Write(string line)
        {
            if (JobLog != null) try { File.AppendAllText(JobLog, line + Environment.NewLine); } catch { }
            try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "CAWPlusHelper.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine); }
            catch { }
        }
    }

    internal static class HelperMain
    {
        private static int Main(string[] args)
        {
            if (args.Length == 2) Log.JobLog = Path.Combine(args[1], "log.txt");
            // the Library folder (and worlds.txt overrides) sit next to CAWPlus.exe, one folder up
            WorldPreset.BaseDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (args.Length == 2 && args[0] == "terrain") return Terrain(args[1]);
            if (args.Length == 2 && args[0] == "paint") return Paint(args[1]);
            if (args.Length == 2 && args[0] == "roads") return Roads(args[1]);
            if (args.Length == 1 && args[0] == "version") { Console.WriteLine("CAWPlusHelper on .NET " + Environment.Version + (Environment.Is64BitProcess ? ", 64-bit" : ", 32-bit")); return 0; }
            Console.Error.WriteLine("usage: CAWPlusHelper terrain <job folder>");
            return 2;
        }

        private static ProgressFn ProgressTo(string dir)
        {
            string file = Path.Combine(dir, "progress.txt");
            return delegate (string stage, int pct) { try { File.WriteAllText(file, pct + "|" + stage); } catch { } };
        }

        private static int Roads(string dir)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                Dictionary<string, string> kv = HelperLink.ReadKeyValues(Path.Combine(dir, "job.txt"));
                Grid coarse;
                using (BinaryReader r = new BinaryReader(File.OpenRead(Path.Combine(dir, "roads_in.bin")))) coarse = HelperLink.ReadGrid(r);
                RoadSettings rs = new RoadSettings();
                HelperLink.ReadFields(kv, "r.", rs);
                if (rs.WorldType.Length > 0)
                {
                    WorldPreset w = WorldPreset.Find(rs.WorldType);
                    if (w == null) throw new InvalidOperationException("world type '" + rs.WorldType + "' not found");
                    if (bool.Parse(kv["hasTargets"])) { rs.Targets = w.RoadTargets; rs.LotsWorldM = w.RoadTargets != null ? w.RoadTargets.WorldM : 0; }
                    if (bool.Parse(kv["hasLots"])) rs.Lots = w.LotFigures;
                    if (bool.Parse(kv["hasLand"])) rs.LotsLand = w.Terrain;
                    if (bool.Parse(kv["hasCharacter"])) rs.Character = w.Character;
                    string ht; if (kv.TryGetValue("hasTown", out ht) && bool.Parse(ht)) rs.Town = w.Town;
                    string hg; if (kv.TryGetValue("hasGrid", out hg) && bool.Parse(hg)) rs.Grid = w.Grid;
                    string hp; if (kv.TryGetValue("hasProfile", out hp) && bool.Parse(hp)) rs.Profile = w.Profile;
                }
                RoadChecks.Result checks;
                RoadNet net = RoadPlanner.PlanChecked(coarse, rs, int.Parse(kv["tries"]), out checks);
                using (BinaryWriter wr = new BinaryWriter(File.Create(Path.Combine(dir, "roads_out.tmp"))))
                {
                    HelperLink.WriteObject(wr, net, typeof(RoadNet));
                    HelperLink.WriteObject(wr, checks, typeof(RoadChecks.Result));
                }
                File.Move(Path.Combine(dir, "roads_out.tmp"), Path.Combine(dir, "roads_out.bin"));
                Log.Info(string.Format("roads: {0} roads, {1:F1} km, {2} lots: {3:F1} s", net.Roads.Count, net.TotalKm, net.Lots.Count, sw.Elapsed.TotalSeconds));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("roads job " + dir + " failed", ex);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int Paint(string dir)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                PaintJob job = PaintJob.ReadFrom(dir);
                job.Run(ProgressTo(dir));
                job.WriteResult(dir);
                Log.Info(string.Format("paint {0} px, {1} layer(s) written, {2} emptied: {3:F1} s", job.Heights.W, job.Written.Count, job.Cleared.Count, sw.Elapsed.TotalSeconds));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("paint job " + dir + " failed", ex);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int Terrain(string dir)
        {
            string progressFile = Path.Combine(dir, "progress.txt");
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                Dictionary<string, string> kv = HelperLink.ReadKeyValues(Path.Combine(dir, "job.txt"));
                GenSettings g = HelperLink.ReadSettings(kv);
                int size = int.Parse(kv["size"]);
                float ws = HelperLink.PF(kv["worldSize"]);
                bool full = bool.Parse(kv["full"]);
                int tries = int.Parse(kv["tries"]);
                float water = HelperLink.PF(kv["water"]);
                string key; kv.TryGetValue("target", out key);
                TerrainProfile target = tries > 0 ? WorldPreset.FindTerrain(key) : null;
                ProgressFn progress = delegate (string stage, int pct)
                {
                    try { File.WriteAllText(progressFile, pct + "|" + stage); } catch { }
                };
                TerrainChecks.Result checks = null;
                TerrainGen.Result res = tries > 0 && target != null
                    ? TerrainGen.GenerateChecked(g, size, ws, full, progress, target, water, tries, out checks)
                    : TerrainGen.Generate(g, size, ws, full, progress);
                HelperLink.WriteResult(Path.Combine(dir, "result.tmp"), res, g.Seed);
                if (checks != null) HelperLink.WriteChecks(Path.Combine(dir, "checks.txt"), checks);
                File.Move(Path.Combine(dir, "result.tmp"), Path.Combine(dir, "result.bin"));
                Log.Info(string.Format("terrain {0} px, {1} variation(s){2}: {3:F1} s", size, tries, target == null && tries > 0 ? " (no target '" + key + "')" : "", sw.Elapsed.TotalSeconds));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("terrain job " + dir + " failed", ex);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
