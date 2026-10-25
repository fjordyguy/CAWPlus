using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CawPlus
{
    /// <summary>
    /// The paint textures of EA's worlds, copied into the SourceTextures folder so Auto-Paint can use them by name.
    /// A CAW world file (.world, a DBPF package) lists its paint textures in its terrain info resource (0x9063660D: key +
    /// name per texture) and, for most worlds, holds each one as a DDS resource (0x00B2D882) under the same instance.
    /// Textures it doesn't hold can come from the game files CAW has loaded (the instance is FNV-1 64 of the name).
    /// Nothing is copied anywhere but the user's own SourceTextures folder.
    /// </summary>
    public static class WorldTextures
    {
        public const uint TerrainInfo = 0x9063660D, Dds = 0x00B2D882;
        public const string FolderName = "CAWPlus EA textures";

        /// <summary>A texture a world's paint layers use: its resource key and name (no folder or extension).</summary>
        public sealed class Texture
        {
            public uint Group;
            public ulong Instance;
            public string Name;
        }

        public sealed class Result
        {
            public int Worlds, Written, Present;
            public readonly List<string> Missing = new List<string>();   // listed by a world but not held in its file
            public readonly List<string> Failed = new List<string>();    // world files that couldn't be read
        }

        /// <summary>Reads a package's index and single resources from a file (no need to load a 100 MB world).</summary>
        public sealed class Package : IDisposable
        {
            public sealed class Entry
            {
                public uint Type, Group;
                public ulong Instance;
                public long Offset;
                public int FileSize, MemSize;
                public ushort Compression;
            }

            private readonly FileStream fs;
            public readonly List<Entry> Entries = new List<Entry>();

            public Package(string path)
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] h = ReadAt(0, 96);
                if (h[0] != 'D' || h[1] != 'B' || h[2] != 'P' || h[3] != 'F') throw new InvalidDataException("not a DBPF package");
                int count = BitConverter.ToInt32(h, 36);
                long pos = BitConverter.ToUInt32(h, 64);
                if (pos == 0) pos = BitConverter.ToUInt32(h, 40);
                if (count < 0 || count > 2000000 || pos <= 0 || pos >= fs.Length) throw new InvalidDataException("bad package index");
                byte[] idx = ReadAt(pos, (int)Math.Min(fs.Length - pos, 4 + 12 + (long)count * 32));
                int p = 0;
                uint flags = U32(idx, ref p);
                uint cType = 0, cGroup = 0, cHi = 0;
                if ((flags & 1) != 0) cType = U32(idx, ref p);
                if ((flags & 2) != 0) cGroup = U32(idx, ref p);
                if ((flags & 4) != 0) cHi = U32(idx, ref p);
                for (int i = 0; i < count; i++)
                {
                    Entry e = new Entry();
                    e.Type = (flags & 1) != 0 ? cType : U32(idx, ref p);
                    e.Group = (flags & 2) != 0 ? cGroup : U32(idx, ref p);
                    uint hi = (flags & 4) != 0 ? cHi : U32(idx, ref p);
                    uint lo = U32(idx, ref p);
                    e.Instance = ((ulong)hi << 32) | lo;
                    e.Offset = U32(idx, ref p);
                    e.FileSize = (int)(U32(idx, ref p) & 0x7FFFFFFF);
                    e.MemSize = (int)U32(idx, ref p);
                    e.Compression = BitConverter.ToUInt16(idx, p); p += 4; // compression, then a u16 we don't need
                    Entries.Add(e);
                }
            }

            public byte[] Read(Entry e)
            {
                byte[] raw = ReadAt(e.Offset, e.FileSize);
                if (e.Compression == 0xFFFF) return RefPack(raw);
                if (e.Compression == 0x5A42)
                {
                    // zlib: a 2-byte header, then deflate
                    using (MemoryStream ms = new MemoryStream(raw, 2, raw.Length - 2))
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                    {
                        byte[] o = new byte[e.MemSize]; int got = 0, n;
                        while (got < o.Length && (n = ds.Read(o, got, o.Length - got)) > 0) got += n;
                        return o;
                    }
                }
                return raw;
            }

            private byte[] ReadAt(long pos, int len)
            {
                if (len < 0 || pos < 0 || pos + len > fs.Length) throw new InvalidDataException("resource outside the file");
                byte[] b = new byte[len];
                fs.Position = pos;
                int got = 0, n;
                while (got < len && (n = fs.Read(b, got, len - got)) > 0) got += n;
                return b;
            }

            private static uint U32(byte[] b, ref int p) { uint v = BitConverter.ToUInt32(b, p); p += 4; return v; }

            public void Dispose() { fs.Close(); }
        }

        /// <summary>EA's RefPack (QFS) decompression, as used by Sims 3 packages.</summary>
        public static byte[] RefPack(byte[] d)
        {
            int i = 2;
            int sizeLen = (d[0] & 0x80) != 0 ? 4 : 3;
            int usize = 0;
            for (int k = 0; k < sizeLen; k++) usize = (usize << 8) | d[i + k];
            i += sizeLen;
            byte[] o = new byte[usize];
            int op = 0, n = d.Length;
            while (i < n)
            {
                int c0 = d[i], pl, cl = 0, off = 0;
                bool stop = false;
                if (c0 < 0x80) { int c1 = d[i + 1]; i += 2; pl = c0 & 3; cl = ((c0 & 0x1C) >> 2) + 3; off = ((c0 & 0x60) << 3) + c1 + 1; }
                else if (c0 < 0xC0) { int c1 = d[i + 1], c2 = d[i + 2]; i += 3; pl = (c1 & 0xC0) >> 6; cl = (c0 & 0x3F) + 4; off = ((c1 & 0x3F) << 8) + c2 + 1; }
                else if (c0 < 0xE0) { int c1 = d[i + 1], c2 = d[i + 2], c3 = d[i + 3]; i += 4; pl = c0 & 3; cl = ((c0 & 0x0C) << 6) + c3 + 5; off = ((c0 & 0x10) << 12) + (c1 << 8) + c2 + 1; }
                else if (c0 < 0xFC) { pl = ((c0 & 0x1F) << 2) + 4; i++; }
                else { pl = c0 & 3; i++; stop = true; }
                if (op + pl > usize || i + pl > n) throw new InvalidDataException("bad compressed data");
                Array.Copy(d, i, o, op, pl); i += pl; op += pl;
                if (stop) break;
                if (cl > 0)
                {
                    int s = op - off;
                    if (s < 0 || op + cl > usize) throw new InvalidDataException("bad compressed data");
                    for (int k = 0; k < cl; k++) o[op++] = o[s + k];
                }
            }
            return o;
        }

        /// <summary>The textures a world's paint layers use (from its terrain info resource); empty if none.</summary>
        public static List<Texture> Listed(Package pkg)
        {
            List<Texture> list = new List<Texture>();
            foreach (Package.Entry e in pkg.Entries)
            {
                if (e.Type != TerrainInfo) continue;
                byte[] d = pkg.Read(e);
                // u32 count, then per texture: type, group, instance (u64), u32 length, UTF-16 name
                int o = -1;
                for (int k = 4; k + 4 <= d.Length; k++)
                    if (BitConverter.ToUInt32(d, k) == Dds) { uint c = BitConverter.ToUInt32(d, k - 4); if (c > 0 && c < 256) { o = k; break; } }
                if (o < 0) return list;
                int count = (int)BitConverter.ToUInt32(d, o - 4);
                for (int t = 0; t < count && o + 20 <= d.Length; t++)
                {
                    Texture tx = new Texture();
                    tx.Group = BitConverter.ToUInt32(d, o + 4);
                    tx.Instance = BitConverter.ToUInt64(d, o + 8);
                    int len = (int)BitConverter.ToUInt32(d, o + 16);
                    if (len < 0 || o + 20 + 2 * len > d.Length) break;
                    tx.Name = CleanName(Encoding.Unicode.GetString(d, o + 20, 2 * len));
                    o += 20 + 2 * len;
                    if (tx.Name.Length > 0) list.Add(tx);
                }
                break;
            }
            return list;
        }

        /// <summary>A texture's name as Auto-Paint matches it: no folder, no image extension, no characters a file can't have.</summary>
        public static string CleanName(string n)
        {
            n = (n ?? "").Trim();
            int slash = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/'));
            if (slash >= 0) n = n.Substring(slash + 1);
            string ext = Path.GetExtension(n).ToLowerInvariant();
            if (ext == ".dds" || ext == ".tga" || ext == ".png" || ext == ".jpg" || ext == ".bmp") n = n.Substring(0, n.Length - ext.Length);
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Trim();
        }

        /// <summary>The world's name for its folder: the file name without "Unpopulated" and the like.</summary>
        public static string WorldName(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
            foreach (string w in new[] { "Un-Populated", "UnPopulated", "Unpopulated", "Unpop", "Suburb", "Caw Files" })
            {
                int k;
                while ((k = n.IndexOf(w, StringComparison.OrdinalIgnoreCase)) >= 0) n = n.Remove(k, w.Length);
            }
            n = CleanName(n.Replace("  ", " ").Trim(' ', '-'));
            return n.Length > 0 ? n : "World";
        }

        /// <summary>
        /// Every .world file under 'folder': each paint texture its file holds is written to outRoot\&lt;world&gt;\&lt;name&gt;.dds,
        /// unless 'have' (names already in SourceTextures, any folder) has it. 'have' gets the new names.
        /// </summary>
        public static Result FromWorldFiles(string folder, string outRoot, HashSet<string> have, ProgressFn progress)
        {
            Result r = new Result();
            List<string> files = new List<string>();
            try { files.AddRange(Directory.GetFiles(folder, "*.world", SearchOption.AllDirectories)); }
            catch (Exception ex) { r.Failed.Add(folder + ": " + ex.Message); return r; }
            HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int f = 0; f < files.Count; f++)
            {
                if (progress != null) progress("Reading " + Path.GetFileName(files[f]), 100 * f / Math.Max(1, files.Count));
                try
                {
                    using (Package pkg = new Package(files[f]))
                    {
                        List<Texture> listed = Listed(pkg);
                        if (listed.Count == 0) continue;
                        r.Worlds++;
                        // DDS resources by instance (the group differs between the list and the resource in some worlds)
                        Dictionary<ulong, Package.Entry> byInst = new Dictionary<ulong, Package.Entry>();
                        foreach (Package.Entry e in pkg.Entries)
                            if (e.Type == Dds && !byInst.ContainsKey(e.Instance)) byInst[e.Instance] = e;
                        string dir = Path.Combine(outRoot, WorldName(files[f]));
                        foreach (Texture t in listed)
                        {
                            if (have.Contains(t.Name)) { r.Present++; continue; }
                            Package.Entry e;
                            if (!byInst.TryGetValue(t.Instance, out e)) { missing.Add(t.Name); continue; }
                            byte[] dds = pkg.Read(e);
                            if (!IsDds(dds)) { missing.Add(t.Name); continue; }
                            Directory.CreateDirectory(dir);
                            File.WriteAllBytes(Path.Combine(dir, t.Name + ".dds"), dds);
                            have.Add(t.Name); r.Written++;
                        }
                    }
                }
                catch (Exception ex) { r.Failed.Add(Path.GetFileName(files[f]) + ": " + ex.Message); }
            }
            foreach (string m in missing) if (!have.Contains(m)) r.Missing.Add(m);
            r.Missing.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }

        /// <summary>A resource reader for the game files (CAW's resource manager in the plugin; null offline).</summary>
        public delegate byte[] ResourceReader(uint type, uint group, ulong instance);

        /// <summary>
        /// Each named texture not in 'have', looked up in the game files: instance = FNV-1 64 of the name, in the groups EA
        /// worlds use (0, 0x02000000 world-embedded, 0x08000000 World Adventures). Written to outRoot\Game\&lt;name&gt;.dds.
        /// Returns the names found; 'have' gets them.
        /// </summary>
        public static List<string> FromGame(IEnumerable<string> names, string outRoot, HashSet<string> have, ResourceReader read)
        {
            List<string> got = new List<string>();
            if (read == null) return got;
            foreach (string raw in names)
            {
                string n = CleanName(raw);
                if (n.Length == 0 || have.Contains(n)) continue;
                ulong inst = S3Pkg.Fnv64(n);
                foreach (uint g in new uint[] { 0, 0x02000000, 0x08000000 })
                {
                    byte[] d = null;
                    try { d = read(Dds, g, inst); } catch { }
                    if (!IsDds(d)) continue;
                    string dir = Path.Combine(outRoot, "Game");
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, n + ".dds"), d);
                    have.Add(n); got.Add(n);
                    break;
                }
            }
            return got;
        }

        private static bool IsDds(byte[] d) { return d != null && d.Length > 128 && d[0] == 'D' && d[1] == 'D' && d[2] == 'S' && d[3] == ' '; }
    }
}
