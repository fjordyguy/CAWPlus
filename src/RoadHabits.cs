using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>
    /// Road growth habits learned from road graphs (EA's worlds, or real towns of a region; scratch rl/habits.py ->
    /// library/road_habits.txt): junction-to-junction run and dead-end street length (log-normal), the share of square
    /// branches, of crossroads, bends by land slope (degrees per 100 m in four bands: under 4, 8, 15 % and steeper), the
    /// share following the contour on slopes over 8 %, spacing from the next road, and the share of side streets left as dead
    /// ends. Used by RoadPlanner.Grow.
    /// </summary>
    public sealed class RoadHabits
    {
        public string Key = "";
        public float RunMu = 4.3f, RunSd = 0.7f, DeadMu = 4.4f, DeadSd = 1.0f;
        public float Angle90 = 0.65f, Cross = 0.25f, Contour = 0.55f, Gap = 70f, DeadShare = 0.3f;
        public float[] Turn = { 5f, 40f, 50f, 60f };

        /// <summary>Lines "key|runMu,runSd|deadMu,deadSd|angle90|cross|t0,t1,t2,t3|contour|gap|deadShare" ('#' comments).</summary>
        public static Dictionary<string, RoadHabits> Parse(string text)
        {
            Dictionary<string, RoadHabits> d = new Dictionary<string, RoadHabits>();
            CultureInfo ci = CultureInfo.InvariantCulture;
            foreach (string raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l[0] == '#') continue;
                string[] p = l.Split('|');
                if (p.Length < 9) continue;
                try
                {
                    RoadHabits h = new RoadHabits(); h.Key = p[0];
                    string[] a = p[1].Split(','); h.RunMu = float.Parse(a[0], ci); h.RunSd = float.Parse(a[1], ci);
                    a = p[2].Split(','); h.DeadMu = float.Parse(a[0], ci); h.DeadSd = float.Parse(a[1], ci);
                    h.Angle90 = float.Parse(p[3], ci); h.Cross = float.Parse(p[4], ci);
                    a = p[5].Split(',');
                    for (int k = 0; k < 4 && k < a.Length; k++) h.Turn[k] = float.Parse(a[k], ci);
                    h.Contour = float.Parse(p[6], ci); h.Gap = float.Parse(p[7], ci); h.DeadShare = float.Parse(p[8], ci);
                    d[h.Key] = h;
                }
                catch (FormatException) { }
            }
            return d;
        }
    }
}
