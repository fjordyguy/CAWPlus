using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CawPlus
{
    /// <summary>
    /// The files CAW Plus and CAWPlusHelper.exe (a 64-bit .NET program, helper/) pass each other: a job folder holds
    /// job.txt (settings, one name=value per line), the helper writes progress.txt as it goes and result.bin / checks.txt
    /// at the end. Compiled into both programs, so both read and write the same format.
    /// </summary>
    internal static class HelperLink
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------- name=value settings ----------

        /// <summary>Every public field of the GenSettings, by reflection (int, float, bool, enum, float[]).</summary>
        public static void WriteSettings(StringBuilder sb, GenSettings g) { WriteFields(sb, "g.", g); }

        public static GenSettings ReadSettings(Dictionary<string, string> kv)
        {
            GenSettings g = new GenSettings();
            ReadFields(kv, "g.", g);
            return g;
        }

        /// <summary>An object's public fields of simple types (int, float, double, bool, string, enum, float[], int[]) as
        /// prefix + name=value lines; other fields (grids, objects) are left out.</summary>
        public static void WriteFields(StringBuilder sb, string prefix, object o)
        {
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type t = f.FieldType;
                if (!(t == typeof(float[]) || t == typeof(int[]) || t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(bool) || t == typeof(string) || t.IsEnum)) continue;
                object v = f.GetValue(o);
                string s;
                if (v == null) s = t == typeof(string) ? "" : "null";
                else if (v is float[])
                {
                    float[] a = (float[])v; string[] p = new string[a.Length];
                    for (int i = 0; i < a.Length; i++) p[i] = a[i].ToString("R", Inv);
                    s = "[" + string.Join(",", p) + "]";
                }
                else if (v is int[])
                {
                    int[] a = (int[])v; string[] p = new string[a.Length];
                    for (int i = 0; i < a.Length; i++) p[i] = a[i].ToString(Inv);
                    s = "[" + string.Join(",", p) + "]";
                }
                else if (v is float) s = ((float)v).ToString("R", Inv);
                else if (v is double) s = ((double)v).ToString("R", Inv);
                else if (v is Enum) s = Convert.ToInt32(v).ToString(Inv);
                else s = Convert.ToString(v, Inv);
                sb.Append(prefix).Append(f.Name).Append('=').Append(OneLine(s)).Append('\n');
            }
        }

        public static void ReadFields(Dictionary<string, string> kv, string prefix, object o)
        {
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string s;
                if (!kv.TryGetValue(prefix + f.Name, out s)) continue;
                Type t = f.FieldType;
                if (t == typeof(float[]))
                {
                    if (s == "null" || s.Length < 2) { f.SetValue(o, null); continue; }
                    string body = s.Substring(1, s.Length - 2);
                    if (body.Length == 0) { f.SetValue(o, new float[0]); continue; }
                    string[] p = body.Split(',');
                    float[] a = new float[p.Length];
                    for (int i = 0; i < p.Length; i++) a[i] = float.Parse(p[i], Inv);
                    f.SetValue(o, a);
                }
                else if (t == typeof(int[]))
                {
                    if (s == "null" || s.Length < 2) { f.SetValue(o, null); continue; }
                    string body = s.Substring(1, s.Length - 2);
                    string[] p = body.Length == 0 ? new string[0] : body.Split(',');
                    int[] a = new int[p.Length];
                    for (int i = 0; i < p.Length; i++) a[i] = int.Parse(p[i], Inv);
                    f.SetValue(o, a);
                }
                else if (t == typeof(float)) f.SetValue(o, float.Parse(s, Inv));
                else if (t == typeof(double)) f.SetValue(o, double.Parse(s, Inv));
                else if (t == typeof(int)) f.SetValue(o, int.Parse(s, Inv));
                else if (t == typeof(bool)) f.SetValue(o, bool.Parse(s));
                else if (t == typeof(string)) f.SetValue(o, s);
                else if (t.IsEnum) f.SetValue(o, Enum.ToObject(t, int.Parse(s, Inv)));
            }
        }

        /// <summary>A value safe to write as one key=value line (line breaks become spaces).</summary>
        public static string OneLine(string s) { return s == null ? "" : s.Replace('\r', ' ').Replace('\n', ' '); }

        /// <summary>A job file's key=value lines. A key given twice is refused: each is written once, so a repeat means a
        /// value broke its line.</summary>
        public static Dictionary<string, string> ReadKeyValues(string path)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(path))
            {
                int e = line.IndexOf('=');
                if (e <= 0) continue;
                string k = line.Substring(0, e);
                if (kv.ContainsKey(k)) throw new InvalidDataException("Job file " + Path.GetFileName(path) + ": '" + k + "' is given twice");
                kv[k] = line.Substring(e + 1);
            }
            return kv;
        }

        public static string F(float v) { return v.ToString("R", Inv); }
        public static float PF(string s) { return float.Parse(s, Inv); }

        // ---------- binary results ----------

        public static void WriteGrid(BinaryWriter w, Grid g)
        {
            if (g == null) { w.Write(-1); return; }
            w.Write(g.W); w.Write(g.H);
            byte[] b = new byte[g.D.Length * 4];
            Buffer.BlockCopy(g.D, 0, b, 0, b.Length);
            w.Write(b);
        }

        public static Grid ReadGrid(BinaryReader r)
        {
            int w = r.ReadInt32();
            if (w < 0) return null;
            int h = r.ReadInt32();
            Grid g = new Grid(w, h);
            byte[] b = r.ReadBytes(g.D.Length * 4);
            Buffer.BlockCopy(b, 0, g.D, 0, b.Length);
            return g;
        }

        private static void WriteFloats(BinaryWriter w, float[] a) { w.Write(a.Length); foreach (float v in a) w.Write(v); }
        private static float[] ReadFloats(BinaryReader r) { float[] a = new float[r.ReadInt32()]; for (int i = 0; i < a.Length; i++) a[i] = r.ReadSingle(); return a; }

        public static void WriteResult(string path, TerrainGen.Result res, int seed)
        {
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write(0x43415750); // "CAWP"
                w.Write(seed);
                w.Write(res.Cell);
                WriteGrid(w, res.Heights);
                WriteGrid(w, res.Coarse);
                w.Write(res.Rivers.Count);
                foreach (List<float[]> river in res.Rivers) { w.Write(river.Count); foreach (float[] p in river) WriteFloats(w, p); }
                w.Write(res.Ponds.Count);
                foreach (float[] p in res.Ponds) WriteFloats(w, p);
                w.Write(res.TownHint != null ? 1 : 0);
                if (res.TownHint != null) WriteFloats(w, res.TownHint);
            }
        }

        public static TerrainGen.Result ReadResult(string path, out int seed)
        {
            using (BinaryReader r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadInt32() != 0x43415750) throw new InvalidDataException("not a CAW Plus helper result");
                seed = r.ReadInt32();
                TerrainGen.Result res = new TerrainGen.Result();
                res.Cell = r.ReadSingle();
                res.Heights = ReadGrid(r);
                res.Coarse = ReadGrid(r);
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int m = r.ReadInt32();
                    List<float[]> river = new List<float[]>(m);
                    for (int k = 0; k < m; k++) river.Add(ReadFloats(r));
                    res.Rivers.Add(river);
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) res.Ponds.Add(ReadFloats(r));
                if (r.BaseStream.Position < r.BaseStream.Length && r.ReadInt32() == 1) res.TownHint = ReadFloats(r);
                return res;
            }
        }

        // ---------- plain objects (RoadNet, RoadChecks.Result ...) ----------

        /// <summary>
        /// Writes an object graph of plain classes: every instance field (public or not, sorted by name) of types int,
        /// long, float, double, bool, string, enum, float[], int[], List&lt;T&gt; and other such classes. Both programs
        /// compile the same classes, so ReadObject rebuilds it exactly.
        /// </summary>
        public static void WriteObject(BinaryWriter w, object o, Type t)
        {
            if (t == typeof(int)) w.Write((int)o);
            else if (t == typeof(long)) w.Write((long)o);
            else if (t == typeof(float)) w.Write((float)o);
            else if (t == typeof(double)) w.Write((double)o);
            else if (t == typeof(bool)) w.Write((bool)o);
            else if (t.IsEnum) w.Write(Convert.ToInt32(o));
            else if (t == typeof(string)) { w.Write(o != null); if (o != null) w.Write((string)o); }
            else if (t == typeof(float[])) { float[] a = (float[])o; w.Write(a == null ? -1 : a.Length); if (a != null) foreach (float v in a) w.Write(v); }
            else if (t == typeof(int[])) { int[] a = (int[])o; w.Write(a == null ? -1 : a.Length); if (a != null) foreach (int v in a) w.Write(v); }
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                System.Collections.IList l = (System.Collections.IList)o;
                Type et = t.GetGenericArguments()[0];
                w.Write(l == null ? -1 : l.Count);
                if (l != null) foreach (object e in l) WriteObject(w, e, et);
            }
            else if (t.IsClass)
            {
                w.Write(o != null);
                if (o != null) foreach (FieldInfo f in Fields(t)) WriteObject(w, f.GetValue(o), f.FieldType);
            }
            else throw new NotSupportedException("HelperLink can't write " + t);
        }

        public static object ReadObject(BinaryReader r, Type t)
        {
            if (t == typeof(int)) return r.ReadInt32();
            if (t == typeof(long)) return r.ReadInt64();
            if (t == typeof(float)) return r.ReadSingle();
            if (t == typeof(double)) return r.ReadDouble();
            if (t == typeof(bool)) return r.ReadBoolean();
            if (t.IsEnum) return Enum.ToObject(t, r.ReadInt32());
            if (t == typeof(string)) return r.ReadBoolean() ? r.ReadString() : null;
            if (t == typeof(float[])) { int n = r.ReadInt32(); if (n < 0) return null; float[] a = new float[n]; for (int i = 0; i < n; i++) a[i] = r.ReadSingle(); return a; }
            if (t == typeof(int[])) { int n = r.ReadInt32(); if (n < 0) return null; int[] a = new int[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt32(); return a; }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                int n = r.ReadInt32();
                if (n < 0) return null;
                System.Collections.IList l = (System.Collections.IList)Activator.CreateInstance(t);
                Type et = t.GetGenericArguments()[0];
                for (int i = 0; i < n; i++) l.Add(ReadObject(r, et));
                return l;
            }
            if (t.IsClass)
            {
                if (!r.ReadBoolean()) return null;
                object o = Activator.CreateInstance(t, true);
                foreach (FieldInfo f in Fields(t)) f.SetValue(o, ReadObject(r, f.FieldType));
                return o;
            }
            throw new NotSupportedException("HelperLink can't read " + t);
        }

        private static List<FieldInfo> Fields(Type t)
        {
            List<FieldInfo> l = new List<FieldInfo>(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
            l.Sort(delegate (FieldInfo a, FieldInfo b) { return string.CompareOrdinal(a.Name, b.Name); });
            return l;
        }

        // ---------- terrain checks ----------

        public static void WriteChecks(string path, TerrainChecks.Result c)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("passed=").Append(c.Passed).Append('\n');
            sb.Append("total=").Append(c.Total).Append('\n');
            sb.Append("score=").Append(c.Score.ToString("R", Inv)).Append('\n');
            sb.Append("seed=").Append(c.Seed).Append('\n');
            sb.Append("failed=").Append(string.Join("|", c.Failed.ToArray())).Append('\n');
            sb.Append("landforms=").Append(c.Landforms.Replace('\n', ' ')).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        public static TerrainChecks.Result ReadChecks(string path)
        {
            if (!File.Exists(path)) return null;
            Dictionary<string, string> kv = ReadKeyValues(path);
            TerrainChecks.Result c = new TerrainChecks.Result();
            c.Passed = int.Parse(kv["passed"], Inv);
            c.Total = int.Parse(kv["total"], Inv);
            c.Score = double.Parse(kv["score"], Inv);
            c.Seed = int.Parse(kv["seed"], Inv);
            if (kv["failed"].Length > 0) c.Failed.AddRange(kv["failed"].Split('|'));
            c.Landforms = kv["landforms"];
            return c;
        }
    }
}
