using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CawPlus
{
    /// <summary>Files built into CAWPlus.exe (library/*.props, worlds.txt; resource names "Library.&lt;file&gt;").</summary>
    internal static class Embedded
    {
        /// <summary>Embedded library files whose name ends with 'suffix': (file name, lines).</summary>
        public static List<KeyValuePair<string, string[]>> Files(string suffix)
        {
            List<KeyValuePair<string, string[]>> list = new List<KeyValuePair<string, string[]>>();
            Assembly asm = typeof(Embedded).Assembly;
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("Library.") || !res.EndsWith(suffix)) continue;
                using (Stream s = asm.GetManifestResourceStream(res))
                using (StreamReader r = new StreamReader(s))
                {
                    List<string> lines = new List<string>();
                    string line;
                    while ((line = r.ReadLine()) != null) lines.Add(line);
                    list.Add(new KeyValuePair<string, string[]>(res.Substring("Library.".Length), lines.ToArray()));
                }
            }
            return list;
        }
    }
}
