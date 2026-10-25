using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CawPlus
{
    /// <summary>
    /// Runs the terrain generator in CAWPlusHelper.exe (folder CAWPlusHelper next to CAWPlus.exe): a 64-bit .NET 8+
    /// program, so it's faster than CAW's 32-bit .NET 2 and isn't squeezed into CAW's memory. The job goes through a
    /// temporary folder (HelperLink). If the helper is missing, can't start (no .NET runtime) or fails, the same code runs
    /// inside CAW as before. Call from a worker thread: it waits for the helper.
    /// </summary>
    internal static class Helper
    {
        private static bool unusable;     // couldn't start this session (e.g. no .NET runtime): don't try again
        private static bool announced;

        public static string Exe
        {
            get { return Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CAWPlusHelper"), "CAWPlusHelper.exe"); }
        }

        public static bool Available { get { return !unusable && File.Exists(Exe); } }

        /// <summary>
        /// TerrainGen.GenerateChecked (tries &gt; 0 and a target) or TerrainGen.Generate, in the helper when it can be used.
        /// g.Seed is left at the variation kept, as GenerateChecked does. Null if 'stop' was requested.
        /// </summary>
        public static TerrainGen.Result Generate(GenSettings g, int size, float worldSize, bool full, ProgressFn progress,
                                                 TerrainProfile target, float water, int tries, Work.Stop stop, out TerrainChecks.Result checks)
        {
            checks = null;
            if (Available)
            {
                try
                {
                    TerrainGen.Result r = Run(g, size, worldSize, full, progress, target, water, tries, stop, out checks);
                    if (r != null || (stop != null && stop.Requested)) return r;
                }
                catch (Exception ex) { Log.Error("CAWPlusHelper failed; working it out inside CAW instead", ex); }
            }
            if (stop != null && stop.Requested) return null;
            if (tries > 0 && target != null) return TerrainGen.GenerateChecked(g, size, worldSize, full, progress, target, water, tries, stop, out checks);
            return TerrainGen.Generate(g, size, worldSize, full, progress);
        }

        /// <summary>
        /// Auto-Paint's heavy part (PaintJob): in the helper when it can be used, else here. Either way the layer PNGs end
        /// up in job.OutDir and job.Written / job.Cleared list them.
        /// </summary>
        public static void Paint(PaintJob job, ProgressFn progress)
        {
            if (Available)
            {
                string dir = NewJobDir();
                try
                {
                    job.WriteTo(dir);
                    if (RunJob("paint", dir, progress, null) && job.ReadResult(dir))
                    {
                        if (!announcedPaint) { announcedPaint = true; Log.Info("Auto-Paint worked out by CAWPlusHelper (64-bit)"); }
                        return;
                    }
                }
                catch (Exception ex) { Log.Error("CAWPlusHelper paint job failed; painting inside CAW instead", ex); }
                finally { try { Directory.Delete(dir, true); } catch { } }
                job.Written.Clear(); job.Cleared.Clear(); job.MatchNote = ""; job.Kept = "";
            }
            job.Run(progress);
        }

        private static bool announcedPaint, announcedRoads;

        /// <summary>
        /// RoadPlanner.PlanChecked in the helper when it can be used, else here. The world type's targets (checks, lots,
        /// character) are looked up again in the helper by RoadSettings.WorldType; without a name for them it runs here.
        /// </summary>
        public static RoadNet PlanRoads(Grid coarse, RoadSettings rs, int tries, Work.Stop stop, out RoadChecks.Result checks)
        {
            checks = null;
            bool targetsKnown = rs.WorldType.Length > 0 || (rs.Targets == null && rs.Lots == null && rs.Character == null && rs.LotsLand == null);
            if (Available && targetsKnown)
            {
                string dir = NewJobDir();
                try
                {
                    using (BinaryWriter w = new BinaryWriter(File.Create(Path.Combine(dir, "roads_in.bin")))) HelperLink.WriteGrid(w, coarse);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("tries=").Append(tries).Append('\n');
                    sb.Append("hasTargets=").Append(rs.Targets != null).Append('\n');
                    sb.Append("hasLots=").Append(rs.Lots != null).Append('\n');
                    sb.Append("hasLand=").Append(rs.LotsLand != null).Append('\n');
                    sb.Append("hasCharacter=").Append(rs.Character != null).Append('\n');
                    sb.Append("hasTown=").Append(rs.Town != null).Append('\n');
                    sb.Append("hasGrid=").Append(rs.Grid != null).Append('\n');
                    sb.Append("hasProfile=").Append(rs.Profile != null).Append('\n');
                    HelperLink.WriteFields(sb, "r.", rs);
                    File.WriteAllText(Path.Combine(dir, "job.txt"), sb.ToString());
                    if (RunJob("roads", dir, null, stop))
                    {
                        string res = Path.Combine(dir, "roads_out.bin");
                        if (File.Exists(res))
                            using (BinaryReader r = new BinaryReader(File.OpenRead(res)))
                            {
                                RoadNet net = (RoadNet)HelperLink.ReadObject(r, typeof(RoadNet));
                                checks = (RoadChecks.Result)HelperLink.ReadObject(r, typeof(RoadChecks.Result));
                                if (!announcedRoads) { announcedRoads = true; Log.Info("Roads planned by CAWPlusHelper (64-bit)"); }
                                return net;
                            }
                    }
                    if (stop != null && stop.Requested) return null;
                }
                catch (Exception ex) { Log.Error("CAWPlusHelper road job failed; planning inside CAW instead", ex); }
                finally { try { Directory.Delete(dir, true); } catch { } }
            }
            if (stop != null && stop.Requested) return null;
            return RoadPlanner.PlanChecked(coarse, rs, tries, stop, out checks);
        }

        private static string NewJobDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CAWPlus_job_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Runs "CAWPlusHelper command dir" and waits (forwarding progress.txt, killing it on 'stop'). The helper's own
        /// log lines for the job (log.txt) are copied into CAWPlus.log. False if it couldn't run or failed.
        /// </summary>
        private static bool RunJob(string command, string dir, ProgressFn progress, Work.Stop stop)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Exe, command + " \"" + dir + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.WorkingDirectory = Path.GetDirectoryName(Exe);
            psi.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "Major"; // built for .NET 8, runs on .NET 10 too
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception ex) { unusable = true; Log.Error("CAWPlusHelper could not be started; using CAW Plus's own code", ex); return false; }
            using (p)
            {
                StringBuilder err = new StringBuilder();
                p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { };
                p.BeginErrorReadLine(); p.BeginOutputReadLine();
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } // CAW's own window stays responsive
                string progressFile = Path.Combine(dir, "progress.txt"), last = "";
                while (!p.WaitForExit(100))
                {
                    if (stop != null && stop.Requested) { try { p.Kill(); } catch { } return false; }
                    if (progress != null && File.Exists(progressFile))
                    {
                        string t = "";
                        try { t = File.ReadAllText(progressFile); } catch { }
                        int bar = t.IndexOf('|'); int pct;
                        if (t != last && bar > 0 && int.TryParse(t.Substring(0, bar), out pct)) { last = t; progress(t.Substring(bar + 1), pct); }
                    }
                }
                p.WaitForExit();
                string jobLog = Path.Combine(dir, "log.txt");
                if (File.Exists(jobLog)) try { foreach (string line in File.ReadAllLines(jobLog)) Log.Info("  [helper] " + line); } catch { }
                if (p.ExitCode != 0)
                {
                    string e2; lock (err) e2 = err.ToString();
                    if (p.ExitCode < 0 || e2.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0 && e2.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        unusable = true; // the .NET runtime is missing: CAWPlusHelper needs the .NET 10 (or 8) Desktop Runtime x64
                        Log.Info("CAWPlusHelper can't run (exit code " + p.ExitCode + "); install Microsoft's .NET 10 Desktop Runtime (x64). " + Trim(e2));
                    }
                    else Log.Info("CAWPlusHelper " + command + " failed (exit code " + p.ExitCode + "): " + Trim(e2));
                    return false;
                }
                return true;
            }
        }

        private static TerrainGen.Result Run(GenSettings g, int size, float worldSize, bool full, ProgressFn progress,
                                             TerrainProfile target, float water, int tries, Work.Stop stop, out TerrainChecks.Result checks)
        {
            checks = null;
            string dir = Path.Combine(Path.GetTempPath(), "CAWPlus_job_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("size=").Append(size).Append('\n');
                sb.Append("worldSize=").Append(HelperLink.F(worldSize)).Append('\n');
                sb.Append("full=").Append(full).Append('\n');
                sb.Append("tries=").Append(target != null && target.Key != null ? tries : 0).Append('\n');
                sb.Append("water=").Append(HelperLink.F(water)).Append('\n');
                sb.Append("target=").Append(target != null && target.Key != null ? target.Key : "").Append('\n');
                HelperLink.WriteSettings(sb, g);
                File.WriteAllText(Path.Combine(dir, "job.txt"), sb.ToString());

                if (!RunJob("terrain", dir, progress, stop)) return null;
                string result = Path.Combine(dir, "result.bin");
                if (!File.Exists(result)) return null;
                {
                    int seed;
                    TerrainGen.Result res = HelperLink.ReadResult(result, out seed);
                    g.Seed = seed;
                    checks = HelperLink.ReadChecks(Path.Combine(dir, "checks.txt"));
                    if (!announced) { announced = true; Log.Info("Terrain worked out by CAWPlusHelper (64-bit)"); }
                    return res;
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static string Trim(string s) { s = s.Trim(); return s.Length > 1500 ? s.Substring(0, 1500) + "..." : s; }
    }
}
