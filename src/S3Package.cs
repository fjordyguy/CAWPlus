using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CawPlus
{
    /// <summary>
    /// Writes Sims 3 object packages: the DBPF container, RCOL model resources (MODL with its MLOD, material, vertex
    /// format, vertex and index buffers), DDS textures (DXT1/DXT5 with mipmaps), and copies of an existing object's
    /// definition (OBJD, OBJK, VPXY) with their references pointed at the new resources. Layouts as decoded from EA's
    /// and community distant terrain packages (see CLAUDE.md v0.53).
    /// </summary>
    internal static class S3Pkg
    {
        public const uint OBJD = 0x319E4F1D, OBJK = 0x02DC343F, VPXY = 0x736884F1, MODL = 0x01661233, MLOD = 0x01D10F34,
            MATD = 0x01D0E75D, VRTF = 0x01D0E723, VBUF = 0x01D0E6FB, IBUF = 0x01D0E70F, DDS = 0x00B2D882,
            FTPT = 0xD382BF57, LITE = 0x03B4C61D;

        /// <summary>FNV-1 64 of the lower-case name (EA's resource instance hash).</summary>
        public static ulong Fnv64(string s)
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (byte b in Encoding.ASCII.GetBytes(s.ToLowerInvariant())) { h *= 0x100000001B3UL; h ^= b; }
            return h;
        }

        public static uint Fnv32(string s)
        {
            uint h = 0x811C9DC5u;
            foreach (byte b in Encoding.ASCII.GetBytes(s.ToLowerInvariant())) { h *= 0x01000193u; h ^= b; }
            return h;
        }

        public struct Key
        {
            public uint Type, Group; public ulong Instance;
            public Key(uint type, uint group, ulong instance) { Type = type; Group = group; Instance = instance; }
            public override string ToString() { return string.Format("{0:X8}:{1:X8}:{2:X16}", Type, Group, Instance); }
        }

        // ---------- DBPF ----------

        public sealed class Dbpf
        {
            private readonly List<Key> keys = new List<Key>();
            private readonly List<byte[]> data = new List<byte[]>();

            public void Add(Key k, byte[] d) { keys.Add(k); data.Add(d); }

            public byte[] ToBytes()
            {
                MemoryStream ms = new MemoryStream();
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(new byte[96]);
                long[] offs = new long[data.Count];
                for (int i = 0; i < data.Count; i++) { offs[i] = ms.Position; w.Write(data[i]); }
                long index = ms.Position;
                w.Write(0u);                                  // index flags: nothing shared
                for (int i = 0; i < data.Count; i++)
                {
                    w.Write(keys[i].Type); w.Write(keys[i].Group);
                    w.Write((uint)(keys[i].Instance >> 32)); w.Write((uint)keys[i].Instance);
                    w.Write((uint)offs[i]); w.Write((uint)data[i].Length | 0x80000000u); w.Write((uint)data[i].Length);
                    w.Write((ushort)0); w.Write((ushort)1);   // not compressed, committed
                }
                long end = ms.Position;
                ms.Position = 0;
                w.Write(Encoding.ASCII.GetBytes("DBPF")); w.Write(2u); w.Write(0u);
                ms.Position = 36; w.Write((uint)data.Count);
                ms.Position = 44; w.Write((uint)(end - index));
                ms.Position = 60; w.Write(3u);
                ms.Position = 64; w.Write((uint)index);
                return ms.ToArray();
            }
        }

        // ---------- RCOL ----------

        public sealed class Rcol
        {
            public int Public;
            public readonly List<Key> ChunkKeys = new List<Key>();
            public readonly List<byte[]> Chunks = new List<byte[]>();
            public readonly List<Key> External = new List<Key>();

            public byte[] ToBytes()
            {
                MemoryStream ms = new MemoryStream();
                BinaryWriter w = new BinaryWriter(ms);
                w.Write(3u); w.Write((uint)Public); w.Write(0u); w.Write((uint)External.Count); w.Write((uint)Chunks.Count);
                foreach (Key k in ChunkKeys) { w.Write(k.Instance); w.Write(k.Type); w.Write(k.Group); }
                foreach (Key k in External) { w.Write(k.Instance); w.Write(k.Type); w.Write(k.Group); }
                uint pos = (uint)(20 + 16 * (ChunkKeys.Count + External.Count) + 8 * Chunks.Count);
                foreach (byte[] c in Chunks) { w.Write(pos); w.Write((uint)c.Length); pos += (uint)c.Length; }
                foreach (byte[] c in Chunks) w.Write(c);
                return ms.ToArray();
            }

            /// <summary>Reference to chunk 'index' as stored in chunk data (public: index + 1; private: type 1).</summary>
            public uint Ref(int index) { return index < Public ? (uint)(index + 1) : 0x10000000u | (uint)(index - Public + 1); }
            public static uint ExternalRef(int index) { return 0x30000000u | (uint)(index + 1); }
        }

        /// <summary>One Phong material parameter: name hash, type (1 float, 2 int, 4 texture), values (4 words for a texture).</summary>
        public sealed class Param
        {
            public uint Name, Type; public float[] F; public uint[] U;
            public static Param Floats(uint name, params float[] f) { Param p = new Param(); p.Name = name; p.Type = 1; p.F = f; return p; }
            public static Param Texture(uint name, uint reference) { Param p = new Param(); p.Name = name; p.Type = 4; p.U = new uint[] { reference, 0, 0, 0 }; return p; }
            public int Count { get { return Type == 4 ? 4 : F.Length; } }
        }

        public static byte[] Matd(uint materialName, uint shader, IList<Param> ps)
        {
            MemoryStream mtnf = new MemoryStream();
            BinaryWriter m = new BinaryWriter(mtnf);
            int dataSize = 0;
            foreach (Param p in ps) dataSize += 4 * p.Count;
            m.Write(Encoding.ASCII.GetBytes("MTNF")); m.Write(0u); m.Write((uint)dataSize); m.Write((uint)ps.Count);
            uint off = (uint)(16 + 16 * ps.Count);
            foreach (Param p in ps) { m.Write(p.Name); m.Write(p.Type); m.Write((uint)p.Count); m.Write(off); off += (uint)(4 * p.Count); }
            foreach (Param p in ps)
                if (p.Type == 4) foreach (uint u in p.U) m.Write(u);
                else foreach (float f in p.F) m.Write(f);
            byte[] body = mtnf.ToArray();
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("MATD")); w.Write(0x103u); w.Write(materialName); w.Write(shader);
            w.Write((uint)body.Length); w.Write(0u); w.Write(0u); w.Write(body);
            return ms.ToArray();
        }

        // ---------- the model ----------

        /// <summary>A static triangle mesh: positions (x, y, z), unit normals, UVs 0..1, triangles (front face: (b-a) x (c-a) points up).</summary>
        public sealed class Mesh
        {
            public float[] Pos, Nrm, Uv;
            public int[] Tri;
            public int VertexCount { get { return Pos.Length / 3; } }
        }

        public const uint Phong = 0xB9105A6D;

        /// <summary>
        /// The model resource: MODL (one LOD), MLOD (one mesh), Phong material with a diffuse and a normal map, vertex
        /// format pos Short4 / normal UByte4 / uv Short2 / tangent UByte4 (20 bytes), differenced 16-bit indices.
        /// </summary>
        public static byte[] Model(Mesh mesh, ulong modelInstance, string meshName, Key diffuse, Key normalMap)
        {
            int vc = mesh.VertexCount;
            if (vc > 65535) throw new ArgumentException("too many vertices for 16-bit indices: " + vc);
            float[] bb = Bounds(mesh.Pos);
            Rcol r = new Rcol();
            r.Public = 2;
            r.External.Add(normalMap); r.External.Add(diffuse);
            ulong lodInstance = modelInstance & 0x00FFFFFFFFFFFFFFUL;
            r.ChunkKeys.Add(new Key(MODL, 1, modelInstance));       // 0
            r.ChunkKeys.Add(new Key(MLOD, 0, lodInstance));          // 1
            r.ChunkKeys.Add(new Key(MATD, 0, Fnv64(meshName + "_material")));  // 2
            r.ChunkKeys.Add(new Key(VRTF, 0, 0));                     // 3
            r.ChunkKeys.Add(new Key(VBUF, 1, lodInstance));          // 4
            r.ChunkKeys.Add(new Key(IBUF, 1, lodInstance));          // 5
            r.ChunkKeys.Add(new Key(MATD, 0, 0));                     // 6 position scale/offset
            r.ChunkKeys.Add(new Key(0, 0, 0));                        // 7 vertex swizzle info

            // MODL
            MemoryStream s = new MemoryStream(); BinaryWriter w = new BinaryWriter(s);
            w.Write(Encoding.ASCII.GetBytes("MODL")); w.Write(0x102u); w.Write(1u);
            foreach (float f in bb) w.Write(f);
            w.Write(0u); w.Write(0u); w.Write(0f);                     // extra bounds, fade type, custom fade distance
            w.Write(r.Ref(1)); w.Write(0u); w.Write(0u); w.Write(0f); w.Write(float.MaxValue);
            r.Chunks.Add(s.ToArray());

            // MLOD
            s = new MemoryStream(); w = new BinaryWriter(s);
            w.Write(Encoding.ASCII.GetBytes("MLOD")); w.Write(0x203u); w.Write(1u);
            w.Write(108u); w.Write(Fnv32(meshName));
            w.Write(r.Ref(2)); w.Write(r.Ref(3)); w.Write(r.Ref(4)); w.Write(r.Ref(5));
            w.Write(0x00004003u);                                      // triangle list, pickable
            w.Write(0u); w.Write(0); w.Write(0); w.Write(0);           // stream offset, start vertex, start index, min vertex index
            w.Write(vc); w.Write(mesh.Tri.Length / 3);
            foreach (float f in bb) w.Write(f);
            w.Write(0u); w.Write(0u); w.Write(r.Ref(6)); w.Write(0u); w.Write(0u);  // skin, joints, scale/offset, geometry states, parent
            w.Write(0f); w.Write(0f); w.Write(1f); w.Write(0f);        // mirror plane
            r.Chunks.Add(s.ToArray());

            // Phong material (parameter set and order as in the community distant terrains)
            const float uvs = 1f / 32767f;
            List<Param> ps = new List<Param>();
            ps.Add(Param.Texture(0x6E56548A, Rcol.ExternalRef(0)));  // NormalMap
            ps.Add(Param.Floats(0x9554D40F, 2f));
            ps.Add(Param.Floats(0x05D22FD3, 1f));                     // Transparency
            ps.Add(Param.Floats(0x583DF357, 1f));
            ps.Add(Param.Floats(0x2CE11842, 0f, 0f, 0f));             // Specular
            ps.Add(Param.Floats(0x420520E9, uvs, 0f, 0f));            // UVScales
            ps.Add(Param.Floats(0x637DAA05, 1f, 1f, 1f, 1f));         // Diffuse
            ps.Add(Param.Floats(0xF2EEA6EC, 0f, 0f));
            ps.Add(Param.Floats(0xF12E27C3, 1f, 1f));
            ps.Add(Param.Floats(0xE77A2B60, 0f));                     // AlphaMaskThreshold
            ps.Add(Param.Texture(0x6CC0FD85, Rcol.ExternalRef(1)));  // DiffuseMap
            ps.Add(Param.Floats(0x3C45E334, 1f));
            ps.Add(Param.Floats(0x2D4E507E, 1f, 1f));                 // DiffuseUVScale
            ps.Add(Param.Floats(0xBA2D1AB9, 1f, 1f));                 // NormalUVScale
            ps.Add(Param.Floats(0xF755F7FF, 20f));                    // Shininess
            ps.Add(Param.Floats(0x415368B4, uvs, 0f, 0f));
            ps.Add(Param.Floats(0x91EEBAFF, uvs, 0f, 0f));
            ps.Add(Param.Floats(0xFB66A8CB, 0.5f));
            r.Chunks.Add(Matd(Fnv32(meshName + "_material"), Phong, ps));

            // VRTF
            s = new MemoryStream(); w = new BinaryWriter(s);
            w.Write(Encoding.ASCII.GetBytes("VRTF")); w.Write(2u); w.Write(20u); w.Write(4u); w.Write(0u);
            w.Write(new byte[] { 0, 0, 7, 0, 1, 0, 5, 8, 2, 0, 6, 12, 5, 0, 5, 16 });
            r.Chunks.Add(s.ToArray());

            // VBUF
            s = new MemoryStream(); w = new BinaryWriter(s);
            w.Write(Encoding.ASCII.GetBytes("VBUF")); w.Write(0x101u); w.Write(0u); w.Write(r.Ref(7));
            for (int i = 0; i < vc; i++)
            {
                float x = mesh.Pos[3 * i], y = mesh.Pos[3 * i + 1], z = mesh.Pos[3 * i + 2];
                float m = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
                short sc = (short)Math.Max(1, Math.Min(32767, (int)(32767f / Math.Max(m, 1e-3f))));
                w.Write(Q(x * sc)); w.Write(Q(y * sc)); w.Write(Q(z * sc)); w.Write(sc);
                float nx = mesh.Nrm[3 * i], ny = mesh.Nrm[3 * i + 1], nz = mesh.Nrm[3 * i + 2];
                w.Write(B(nz)); w.Write(B(ny)); w.Write(B(nx)); w.Write((byte)127);
                w.Write(Q(mesh.Uv[2 * i] * 32767f)); w.Write(Q(mesh.Uv[2 * i + 1] * 32767f));
                // tangent along +x (u), in the surface: (1, dy/dx, 0) from the normal
                float tx = ny, ty = -nx, tz = 0, tl = (float)Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-4f) { tx = 1; ty = 0; tl = 1; }
                w.Write(B(tz / tl)); w.Write(B(ty / tl)); w.Write(B(tx / tl)); w.Write((byte)255);
            }
            r.Chunks.Add(s.ToArray());

            // IBUF (differenced)
            s = new MemoryStream(); w = new BinaryWriter(s);
            w.Write(Encoding.ASCII.GetBytes("IBUF")); w.Write(2u); w.Write(1u); w.Write(0u);
            int prev = 0;
            foreach (int i in mesh.Tri) { w.Write((short)(i - prev)); prev = i; }
            r.Chunks.Add(s.ToArray());

            // position scale/offset (identity)
            List<Param> so = new List<Param>();
            so.Add(Param.Floats(0x57582869, 0f, 0f, 0f, 0f));
            so.Add(Param.Floats(0x487648E5, 1f, 1f, 1f, 1f));
            so.Add(Param.Floats(0x790EBF2C, 0f, 0f, 0f, 0f));
            so.Add(Param.Floats(0x159BA53E, 1f, 1f, 1f, 1f));
            r.Chunks.Add(Matd(0, 0, so));

            // vertex swizzle info: one segment, one command per 4 bytes (2 = two 16-bit values, 1 = one 32-bit value)
            s = new MemoryStream(); w = new BinaryWriter(s);
            w.Write(1u); w.Write(20u); w.Write((uint)vc); w.Write(0u);
            w.Write(2u); w.Write(2u); w.Write(1u); w.Write(2u); w.Write(1u);
            r.Chunks.Add(s.ToArray());

            return r.ToBytes();
        }

        private static short Q(float v) { int i = (int)Math.Round(v); return (short)(i < -32767 ? -32767 : (i > 32767 ? 32767 : i)); }
        private static byte B(float v) { int i = (int)Math.Round((v + 1f) * 127.5f); return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i)); }

        public static float[] Bounds(float[] p)
        {
            float[] b = { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < p.Length; i += 3)
                for (int k = 0; k < 3; k++) { b[k] = Math.Min(b[k], p[i + k]); b[3 + k] = Math.Max(b[3 + k], p[i + k]); }
            return b;
        }

        // ---------- copies of an existing object's definition ----------

        /// <summary>Reads (type, group, instance) TGI entries: u32 count then count x (type, group, u64 instance).</summary>
        private static void PatchTgis(byte[] d, int start, Dictionary<uint, Key> byType)
        {
            int n = BitConverter.ToInt32(d, start);
            if (n < 0 || start + 4 + 16 * n > d.Length) throw new InvalidDataException("bad TGI block");
            for (int i = 0; i < n; i++)
            {
                int o = start + 4 + 16 * i;
                Key k;
                if (!byType.TryGetValue(BitConverter.ToUInt32(d, o), out k)) continue;
                Put(d, o + 4, k.Group); Put(d, o + 8, k.Instance);
            }
        }

        private static void Put(byte[] d, int o, uint v) { byte[] b = BitConverter.GetBytes(v); Array.Copy(b, 0, d, o, 4); }
        private static void Put(byte[] d, int o, ulong v) { byte[] b = BitConverter.GetBytes(v); Array.Copy(b, 0, d, o, 8); }
        private static void Put(byte[] d, int o, float v) { byte[] b = BitConverter.GetBytes(v); Array.Copy(b, 0, d, o, 4); }

        /// <summary>
        /// Copy of a catalog object definition (OBJD) with its object key reference pointed at 'objk' and, from
        /// version 0x16, its instance name replaced. The rest (catalog flags, price...) stays as in the template.
        /// </summary>
        public static byte[] CopyObjd(byte[] t, Key objk, string instanceName)
        {
            uint version = BitConverter.ToUInt32(t, 0);
            int tgiStart = 8 + BitConverter.ToInt32(t, 4);
            byte[] d = (byte[])t.Clone();
            if (version >= 0x16 && instanceName != null)
            {
                int len, lenBytes;
                if (ReadLen(t, 16, out len, out lenBytes) && 16 + lenBytes + len < tgiStart)
                {
                    byte[] name = Encoding.BigEndianUnicode.GetBytes(instanceName);
                    byte[] prefix = LenBytes(name.Length);
                    MemoryStream ms = new MemoryStream();
                    ms.Write(t, 0, 16); ms.Write(prefix, 0, prefix.Length); ms.Write(name, 0, name.Length);
                    int rest = 16 + lenBytes + len;
                    ms.Write(t, rest, t.Length - rest);
                    d = ms.ToArray();
                    int shift = prefix.Length + name.Length - (lenBytes + len);
                    tgiStart += shift;
                    Put(d, 4, (uint)(tgiStart - 8));
                }
            }
            Dictionary<uint, Key> map = new Dictionary<uint, Key>();
            map[OBJK] = objk;
            PatchTgis(d, tgiStart, map);
            return d;
        }

        /// <summary>Copy of an object key resource (OBJK) with its model (VPXY) reference pointed at 'vpxy'.</summary>
        public static byte[] CopyObjk(byte[] t, Key vpxy)
        {
            byte[] d = (byte[])t.Clone();
            Dictionary<uint, Key> map = new Dictionary<uint, Key>();
            map[VPXY] = vpxy;
            PatchTgis(d, 8 + BitConverter.ToInt32(d, 4), map);
            return d;
        }

        /// <summary>
        /// Copy of a visual proxy (VPXY, an RCOL with one chunk): its own key, its references to models, LODs,
        /// footprint and lights (by type, from 'refs'), and its bounding box.
        /// </summary>
        public static byte[] CopyVpxy(byte[] t, Key self, Dictionary<uint, Key> refs, float[] bounds)
        {
            byte[] d = (byte[])t.Clone();
            if (BitConverter.ToUInt32(d, 16) != 1) throw new InvalidDataException("VPXY with more than one chunk");
            Put(d, 20, self.Instance); Put(d, 28, self.Type); Put(d, 32, self.Group);
            int ext = (int)BitConverter.ToUInt32(d, 12);
            int chunk = (int)BitConverter.ToUInt32(d, 20 + 16 * (1 + ext));
            if (Encoding.ASCII.GetString(d, chunk, 4) != "VPXY") throw new InvalidDataException("not a VPXY chunk");
            int tgiStart = chunk + 12 + BitConverter.ToInt32(d, chunk + 8);
            PatchTgis(d, tgiStart, refs);
            if (bounds != null)
            {
                int bb = d[tgiStart - 1] == 0 ? tgiStart - 29 : tgiStart - 33;   // modular flag 0: no footprint index before it
                for (int k = 0; k < 6; k++) Put(d, bb + 4 * k, bounds[k]);
            }
            return d;
        }

        /// <summary>Copy of a single-chunk RCOL (footprint, lights) under a new key.</summary>
        public static byte[] CopyRcol1(byte[] t, Key self)
        {
            byte[] d = (byte[])t.Clone();
            Put(d, 20, self.Instance); Put(d, 28, self.Type); Put(d, 32, self.Group);
            return d;
        }

        private static bool ReadLen(byte[] d, int o, out int len, out int bytes)
        {
            len = 0; bytes = 0; int shift = 0;
            while (o + bytes < d.Length && bytes < 4)
            {
                byte b = d[o + bytes++];
                len |= (b & 0x7F) << shift; shift += 7;
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }

        private static byte[] LenBytes(int len)
        {
            List<byte> b = new List<byte>();
            do { byte x = (byte)(len & 0x7F); len >>= 7; if (len > 0) x |= 0x80; b.Add(x); } while (len > 0);
            return b.ToArray();
        }

        // ---------- DDS ----------

        /// <summary>DXT1 (dxt5 false) or DXT5 DDS with a full mip chain; pixels are 0xAARRGGBB, w and h powers of two.</summary>
        public static byte[] Dds(int[] argb, int w, int h, bool dxt5)
        {
            int mips = 1; for (int s = Math.Max(w, h); s > 1; s >>= 1) mips++;
            MemoryStream ms = new MemoryStream();
            BinaryWriter o = new BinaryWriter(ms);
            o.Write(Encoding.ASCII.GetBytes("DDS ")); o.Write(124u); o.Write(0x000A1007u); o.Write((uint)h); o.Write((uint)w);
            o.Write((uint)(Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * (dxt5 ? 16 : 8))); o.Write(0u); o.Write((uint)mips);
            for (int i = 0; i < 11; i++) o.Write(0u);
            o.Write(32u); o.Write(4u); o.Write(Encoding.ASCII.GetBytes(dxt5 ? "DXT5" : "DXT1"));
            for (int i = 0; i < 5; i++) o.Write(0u);
            o.Write(0x00401008u); o.Write(0u); o.Write(0u); o.Write(0u); o.Write(0u);
            int[] level = argb; int lw = w, lh = h;
            for (int m = 0; m < mips; m++)
            {
                Compress(o, level, lw, lh, dxt5);
                if (lw == 1 && lh == 1) break;
                int nw = Math.Max(1, lw / 2), nh = Math.Max(1, lh / 2);
                level = Half(level, lw, lh, nw, nh); lw = nw; lh = nh;
            }
            return ms.ToArray();
        }

        private static int[] Half(int[] src, int w, int h, int nw, int nh)
        {
            int[] dst = new int[nw * nh];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int a = 0, r = 0, g = 0, b = 0, n = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = Math.Min(w - 1, 2 * x + dx), sy = Math.Min(h - 1, 2 * y + dy);
                            int p = src[sy * w + sx];
                            a += (p >> 24) & 255; r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255; n++;
                        }
                    dst[y * nw + x] = ((a / n) << 24) | ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
            return dst;
        }

        private static void Compress(BinaryWriter o, int[] px, int w, int h, bool dxt5)
        {
            int[] blk = new int[16];
            for (int by = 0; by < Math.Max(1, (h + 3) / 4); by++)
                for (int bx = 0; bx < Math.Max(1, (w + 3) / 4); bx++)
                {
                    for (int k = 0; k < 16; k++)
                        blk[k] = px[Math.Min(h - 1, by * 4 + k / 4) * w + Math.Min(w - 1, bx * 4 + (k & 3))];
                    if (dxt5) AlphaBlock(o, blk);
                    ColourBlock(o, blk);
                }
        }

        private static void AlphaBlock(BinaryWriter o, int[] blk)
        {
            int lo = 255, hi = 0;
            for (int k = 0; k < 16; k++) { int a = (blk[k] >> 24) & 255; lo = Math.Min(lo, a); hi = Math.Max(hi, a); }
            o.Write((byte)hi); o.Write((byte)lo);
            ulong bits = 0;
            for (int k = 0; k < 16; k++)
            {
                int a = (blk[k] >> 24) & 255, idx;
                if (hi == lo) idx = 0;
                else
                {
                    int step = (int)Math.Round((hi - a) * 7.0 / (hi - lo));   // 0 = hi .. 7 = lo
                    idx = step == 0 ? 0 : (step == 7 ? 1 : step + 1);         // codes: 0 hi, 1 lo, 2..7 between
                }
                bits |= (ulong)idx << (3 * k);
            }
            for (int i = 0; i < 6; i++) o.Write((byte)(bits >> (8 * i)));
        }

        private static void ColourBlock(BinaryWriter o, int[] blk)
        {
            // endpoints: the block's extremes along its main colour axis (bounding box diagonal), inset a little
            int[] mn = { 255, 255, 255 }, mx = { 0, 0, 0 };
            for (int k = 0; k < 16; k++)
                for (int c = 0; c < 3; c++) { int v = (blk[k] >> (16 - 8 * c)) & 255; mn[c] = Math.Min(mn[c], v); mx[c] = Math.Max(mx[c], v); }
            for (int c = 0; c < 3; c++) { int inset = (mx[c] - mn[c]) / 16; mn[c] += inset; mx[c] -= inset; }
            // pick the diagonal (of the 4 through the box) that best follows the colours
            float bestScore = -1; int sg = 1, sb = 1;
            for (int g = -1; g <= 1; g += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    float score = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        int r0 = ((blk[k] >> 16) & 255) - (mn[0] + mx[0]) / 2, g0 = ((blk[k] >> 8) & 255) - (mn[1] + mx[1]) / 2, b0 = (blk[k] & 255) - (mn[2] + mx[2]) / 2;
                        float proj = r0 * (mx[0] - mn[0]) + g * g0 * (mx[1] - mn[1]) + b * b0 * (mx[2] - mn[2]);
                        score += proj * proj;
                    }
                    if (score > bestScore) { bestScore = score; sg = g; sb = b; }
                }
            int[] c0 = { mx[0], sg > 0 ? mx[1] : mn[1], sb > 0 ? mx[2] : mn[2] };
            int[] c1 = { mn[0], sg > 0 ? mn[1] : mx[1], sb > 0 ? mn[2] : mx[2] };
            ushort e0 = To565(c0), e1 = To565(c1);
            if (e0 < e1) { ushort t = e0; e0 = e1; e1 = t; }
            int[][] pal = new int[4][];
            pal[0] = From565(e0); pal[1] = From565(e1);
            if (e0 == e1) { pal[2] = pal[0]; pal[3] = pal[0]; }
            else
            {
                pal[2] = new int[3]; pal[3] = new int[3];
                for (int c = 0; c < 3; c++) { pal[2][c] = (2 * pal[0][c] + pal[1][c]) / 3; pal[3][c] = (pal[0][c] + 2 * pal[1][c]) / 3; }
            }
            uint bits = 0;
            for (int k = 0; k < 16; k++)
            {
                int best = 0, bestD = int.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    int dr = ((blk[k] >> 16) & 255) - pal[i][0], dg = ((blk[k] >> 8) & 255) - pal[i][1], db = (blk[k] & 255) - pal[i][2];
                    int dd = dr * dr + dg * dg + db * db;
                    if (dd < bestD) { bestD = dd; best = i; }
                }
                bits |= (uint)best << (2 * k);
            }
            o.Write(e0); o.Write(e1); o.Write(bits);
        }

        private static ushort To565(int[] c) { return (ushort)(((c[0] * 31 + 127) / 255 << 11) | ((c[1] * 63 + 127) / 255 << 5) | ((c[2] * 31 + 127) / 255)); }
        private static int[] From565(ushort v)
        {
            int r = (v >> 11) & 31, g = (v >> 5) & 63, b = v & 31;
            return new int[] { (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2) };
        }
    }
}
