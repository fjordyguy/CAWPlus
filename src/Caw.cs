using System;
using System.Collections.Generic;
using System.Reflection;

namespace CawPlus
{
    public delegate float HeightFn(float x, float z);

    /// <summary>
    /// Thin reflection bridge into CAW. All names were confirmed from WorldBuilderCS.exe / CSHostWrap.dll metadata
    /// (SuperCAW build for CAW 1.19.x):
    ///   WorldBuilderCS.WorldBuilderMainForm: static Main(string[]), static Instance, GetCurrentWorld(),
    ///       fields mTerrainToolBar / mUtilitiesToolBar (ToolStrip)
    ///   Sims3.CSHost.World: GetTerrain(), GetTerrainHeight(float,float), GetTerrainHeightAboveWater(float,float),
    ///       ImportBlendLayerMap(byte,string), ExportBlendLayerMap(byte,string), SaveHeightImage(string), GetSaveFilePath()
    ///   Sims3.CSHost.WorldTerrain: GetPaintingLayersCount() uint, GetLayerName(byte), GetLayerTerrainType(byte) enum
    /// </summary>
    internal static class Caw
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static Assembly Asm;
        public static Type MainFormType;

        public static void Init(Assembly asm)
        {
            Asm = asm;
            MainFormType = asm.GetType("WorldBuilderCS.WorldBuilderMainForm", true);
        }

        public static object MainForm
        {
            get
            {
                PropertyInfo p = MainFormType.GetProperty("Instance", All);
                if (p != null) return p.GetValue(null, null);
                FieldInfo f = MainFormType.GetField("sInstance", All);
                return f != null ? f.GetValue(null) : null;
            }
        }

        public static object CurrentWorld
        {
            get
            {
                object form = MainForm;
                return form == null ? null : Call(form, "GetCurrentWorld");
            }
        }

        public static object GetField(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, All);
                if (f != null) return f.GetValue(target);
            }
            return null;
        }

        // Lookups by name are cached: Place() needs about a dozen per object, and some of them used to scan every
        // method of CAW's main window class (Form + Control: over a thousand) each time.
        private static readonly Dictionary<string, MethodInfo> methodCache = new Dictionary<string, MethodInfo>();
        private static readonly Dictionary<string, ConstructorInfo> ctorCache = new Dictionary<string, ConstructorInfo>();

        public static MethodInfo FindMethod(Type type, string name, int argCount)
        {
            string key = type.AssemblyQualifiedName + "|" + name + "|" + argCount;
            lock (methodCache)
            {
                MethodInfo hit;
                if (methodCache.TryGetValue(key, out hit)) return hit;
            }
            MethodInfo found = null;
            for (Type t = type; t != null && found == null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(All | BindingFlags.DeclaredOnly))
                    if (m.Name == name && m.GetParameters().Length == argCount) { found = m; break; }
            }
            lock (methodCache) methodCache[key] = found;
            return found;
        }

        /// <summary>Calls a CAW method by name, converting simple arguments (int -> byte etc.) to the declared types.</summary>
        public static object Call(object target, string name, params object[] args)
        {
            if (target == null) throw new ArgumentNullException("target", "CAW object is null when calling " + name);
            MethodInfo m = FindMethod(target.GetType(), name, args.Length);
            if (m == null) throw new MissingMethodException(target.GetType().FullName, name);
            ParameterInfo[] ps = m.GetParameters();
            object[] conv = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Type pt = ps[i].ParameterType;
                object a = args[i];
                conv[i] = (a == null || pt.IsInstanceOfType(a)) ? a : Convert.ChangeType(a, pt);
            }
            try { return m.Invoke(target, conv); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
        }

        /// <summary>Fast delegate for per-pixel height sampling (millions of calls).</summary>
        public static HeightFn MakeHeightFn(object world, string methodName)
        {
            MethodInfo m = FindMethod(world.GetType(), methodName, 2);
            if (m == null) throw new MissingMethodException(world.GetType().FullName, methodName);
            try
            {
                return (HeightFn)Delegate.CreateDelegate(typeof(HeightFn), world, m);
            }
            catch (ArgumentException)
            {
                // Signature mismatch: fall back to slower reflection calls.
                return delegate (float x, float z) { return Convert.ToSingle(m.Invoke(world, new object[] { x, z })); };
            }
        }

        // ---- convenience wrappers ----

        public static object Terrain(object world) { return Call(world, "GetTerrain"); }

        public static int LayerCount(object terrain) { return Convert.ToInt32(Call(terrain, "GetPaintingLayersCount")); }

        public static string LayerName(object terrain, int idx)
        {
            try { return Convert.ToString(Call(terrain, "GetLayerName", (byte)idx)); } catch { return "Layer " + idx; }
        }

        /// <summary>The texture file CAW says a paint layer uses (may be empty or relative).</summary>
        public static string LayerTexturePath(object terrain, int idx)
        {
            try { return Convert.ToString(Call(terrain, "GetPaintingLayerTextureFilePath", (byte)idx)); } catch { return null; }
        }

        public static string LayerTerrainType(object terrain, int idx)
        {
            try { return Convert.ToString(Call(terrain, "GetLayerTerrainType", (byte)idx)); } catch { return ""; }
        }

        public static bool ExportLayer(object world, int idx, string path)
        {
            return Convert.ToBoolean(Call(world, "ExportBlendLayerMap", (byte)idx, path));
        }

        public static bool ImportLayer(object world, int idx, string path)
        {
            return Convert.ToBoolean(Call(world, "ImportBlendLayerMap", (byte)idx, path));
        }

        public static bool SaveHeightImage(object world, string path)
        {
            return Convert.ToBoolean(Call(world, "SaveHeightImage", path));
        }

        public static object GetProperty(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(target, null);
            }
            return null;
        }

        /// <summary>CAW's terrain texture folder (UserToolData\SourceTextures for the user build).</summary>
        public static string TerrainTexturesPath()
        {
            try
            {
                object form = MainForm;
                string p = form == null ? null : GetProperty(form, "TerrainTexturesPath") as string;
                if (!string.IsNullOrEmpty(p) && System.IO.Directory.Exists(p)) return p;
            }
            catch (Exception ex) { Log.Error("Reading CAW's texture folder failed", ex); }
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            string fallback = System.IO.Path.Combine(docs, @"Electronic Arts\The Sims 3 Create A World Tool\UserToolData\SourceTextures");
            return System.IO.Directory.Exists(fallback) ? fallback : null;
        }

        /// <summary>
        /// Adds a paint layer the same way CAW's own "Add Layer" button does (an undoable AddPaintingLayerCommand),
        /// so CAW converts the texture and updates its layer list. Returns the new layer index, or -1.
        /// </summary>
        public static int AddLayer(object world, string texturePath, string terrainType, string layerName)
        {
            object terr = Terrain(world);
            int before = LayerCount(terr);
            Type cmdType = Asm.GetType("WorldBuilderCS.AddPaintingLayerCommand", true);
            ConstructorInfo ctor = null;
            foreach (ConstructorInfo c in cmdType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (c.GetParameters().Length == 3) ctor = c;
            if (ctor == null) throw new MissingMethodException(cmdType.FullName, ".ctor(string, TerrainTypes, string)");
            Type ttType = ctor.GetParameters()[1].ParameterType;
            object tt;
            try { tt = Enum.Parse(ttType, terrainType, true); } catch { tt = Enum.ToObject(ttType, 0); }
            object cmd = ctor.Invoke(new object[] { texturePath, tt, layerName });
            bool ok = Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd));
            int after = LayerCount(terr);
            Log.Info(string.Format("Add layer '{0}' ({1}) from {2}: submit={3}, layers {4} -> {5}",
                layerName, terrainType, texturePath, ok, before, after));
            return ok && after > before ? after - 1 : -1;
        }

        // ---------------- water, lots, roads ----------------

        public static float SeaLevel(object world)
        {
            return Convert.ToSingle(Call(Call(world, "GetSea"), "GetSeaLevel"));
        }

        /// <summary>
        /// Sets the sea level the way CAW's Change Sea Level tool does (decompiled: an undoable SeaLevelCommand, whose
        /// Execute runs Sea.BeginEditMode, SetSeaLevel and EndEditMode over the whole world). After a height import the
        /// water doesn't show until this runs (the tester's v0.40 test: he had to set it by hand).
        /// </summary>
        public static bool RefreshSea(object world, float level)
        {
            Type t = Asm.GetType("WorldBuilderCS.SeaLevelCommand", true);
            ConstructorInfo c = Ctor(t, 2, typeof(float));
            object cmd = c.Invoke(new object[] { level, SeaLevel(world) });
            return Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd));
        }

        /// <summary>
        /// Smooths the ground along built roads with CAW's own Road Grading tool settings (decompiled RoadGradingTool,
        /// "Flatten slope": RoadSpline.FlattenRoadSlope / RoadIntersection.FlattenSlope(edge width, edge sculpting mode,
        /// apply mode, ref dirty rect), repeated), inside one undoable SculptingBrushCmd as the tool does. edgeMode 1 =
        /// Smooth (adjacent terrain sculpting), applyMode 0 = this segment. Returns how many road objects were graded.
        /// </summary>
        public static int GradeRoads(object world, IList<object> roads, IList<object> skip, float edgeWidth, int repeats)
        {
            object terrain = Terrain(world);
            MethodInfo fs = null;
            foreach (object r in roads) { if (r != null && (fs = FindMethod(r.GetType(), "FlattenRoadSlope", 4)) != null) break; }
            if (fs == null) { Log.Info("Road grading: CAW's FlattenRoadSlope was not found"); return 0; }
            Type rectType = fs.GetParameters()[3].ParameterType.GetElementType();
            object rect = Activator.CreateInstance(rectType);
            SetField(rect, "mLeft", int.MaxValue); SetField(rect, "mTop", int.MaxValue);
            SetField(rect, "mRight", int.MinValue); SetField(rect, "mBottom", int.MinValue);
            Type cmdType = Asm.GetType("WorldBuilderCS.SculptingBrushCmd", true);
            ConstructorInfo cc = null;
            foreach (ConstructorInfo c in cmdType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (c.GetParameters().Length == 3) cc = c;
            object cmd = cc.Invoke(new object[] { terrain, rect, false }); // backs up the heights first
            int graded = 0, failed = 0;
            foreach (object r in roads)
            {
                if (r == null || (skip != null && skip.Contains(r))) continue;
                MethodInfo m = FindMethod(r.GetType(), "FlattenRoadSlope", 4) ?? FindMethod(r.GetType(), "FlattenSlope", 4);
                if (m == null) continue;
                try
                {
                    for (int k = 0; k < repeats; k++)
                    {
                        object[] args = { edgeWidth, 1u, 0u, rect };
                        m.Invoke(r, args);
                        rect = args[3];
                    }
                    graded++;
                }
                catch (Exception ex) { if (failed++ < 3) Log.Error("Grading a road piece failed", ex); }
            }
            // the area changed, one cell wider (as the tool does), for the command's lot-edge and sea updates
            SetField(rect, "mLeft", (int)GetField(rect, "mLeft") - 1); SetField(rect, "mTop", (int)GetField(rect, "mTop") - 1);
            SetField(rect, "mRight", (int)GetField(rect, "mRight") + 1); SetField(rect, "mBottom", (int)GetField(rect, "mBottom") + 1);
            SetField(cmd, "mDirtyRect", rect); SetField(cmd, "mDirtyRectForSea", rect);
            Call(MainForm, "SubmitUndoCommand", cmd);
            Log.Info(string.Format("Road grading (CAW's Flatten slope, edge {0} m, smooth edges, x{1}): {2} road objects{3}",
                edgeWidth, repeats, graded, failed > 0 ? ", " + failed + " failed" : ""));
            return graded;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) f.SetValue(target, value);
        }

        /// <summary>
        /// Creates a pond the way CAW's Pond tool does (decompiled names: World.CreateNewPond, Pond.Init(seed vertex x, z,
        /// water level), World.AddPond): the water floods out from the seed vertex up to the level. Returns the pond, or null.
        /// Vertices are the height grid's points, 'spacing' metres apart.
        /// </summary>
        public static object CreatePond(object world, float x, float z, float level, float spacing)
        {
            object pond = Call(world, "CreateNewPond");
            if (pond == null) return null;
            int vx = (int)Math.Round(x / spacing), vz = (int)Math.Round(z / spacing);
            if (!Convert.ToBoolean(Call(pond, "Init", vx, vz, level))) return null;
            return Convert.ToBoolean(Call(world, "AddPond", pond)) ? pond : null;
        }

        public static void RemovePond(object world, object pond) { Call(world, "RemovePond", pond); }

        /// <summary>Ponds as (water level, minX, minZ, maxX, maxZ) in world metres.</summary>
        public static System.Collections.Generic.List<float[]> Ponds(object world)
        {
            System.Collections.Generic.List<float[]> list = new System.Collections.Generic.List<float[]>();
            uint n = Convert.ToUInt32(Call(world, "GetPondsCount"));
            MethodInfo rect = FindMethod(world.GetType(), "GetPondRect", 3);
            for (uint i = 0; i < n; i++)
            {
                object pond = Call(world, "GetPondById", i);
                if (pond == null) continue;
                float level = Convert.ToSingle(Call(pond, "GetWaterLevel"));
                Type v2 = rect.GetParameters()[1].ParameterType.GetElementType();
                object[] args = { i, Activator.CreateInstance(v2), Activator.CreateInstance(v2) };
                if (!Convert.ToBoolean(rect.Invoke(world, args))) continue;
                float ax = F(args[1], "x"), az = F(args[1], "y"), bx = F(args[2], "x"), bz = F(args[2], "y");
                list.Add(new[] { level, Math.Min(ax, bx), Math.Min(az, bz), Math.Max(ax, bx), Math.Max(az, bz) });
            }
            return list;
        }

        /// <summary>Lots as (centreX, centreZ, radius) circles in world metres.</summary>
        public static System.Collections.Generic.List<float[]> LotCircles()
        {
            System.Collections.Generic.List<float[]> list = new System.Collections.Generic.List<float[]>();
            System.Collections.IEnumerable lots = Call(MainForm, "GetAllLots") as System.Collections.IEnumerable;
            if (lots == null) return list;
            foreach (object lot in lots)
            {
                MethodInfo m = FindMethod(lot.GetType(), "GetCenterPos", 1);
                object[] args = { Activator.CreateInstance(m.GetParameters()[0].ParameterType.GetElementType()) };
                m.Invoke(lot, args);
                float r = Convert.ToSingle(Call(lot, "GetBoundingShereRadius"));
                // an empty lot's bounding sphere covers only what is built on it (the tester saw spawners on lots):
                // at least half the lot's diagonal (Lot.SizeX/SizeZ, metres)
                try
                {
                    float sx = Convert.ToSingle(GetProperty(lot, "SizeX")), sz = Convert.ToSingle(GetProperty(lot, "SizeZ"));
                    r = Math.Max(r, 0.5f * (float)Math.Sqrt(sx * sx + sz * sz));
                }
                catch { }
                list.Add(new[] { F(args[0], "x"), F(args[0], "z"), r });
            }
            return list;
        }

        /// <summary>Road splines as their 4 control points (x, z pairs) in world metres; footpaths left out unless asked for.</summary>
        public static System.Collections.Generic.List<float[]> RoadSplines(object world, bool footpaths = false)
        {
            System.Collections.Generic.List<float[]> paths, bridges;
            System.Collections.Generic.List<float[]> roads = RoadSplines(world, out paths, out bridges);
            if (footpaths) roads.AddRange(paths);
            return roads;
        }

        /// <summary>As above, with the footpath pieces and the road pieces on bridges (RoadSpline.IsRoadBridge) also
        /// returned on their own; bridge pieces are in the road list too.</summary>
        public static System.Collections.Generic.List<float[]> RoadSplines(object world, out System.Collections.Generic.List<float[]> paths,
            out System.Collections.Generic.List<float[]> bridges)
        {
            System.Collections.Generic.List<float[]> list = new System.Collections.Generic.List<float[]>();
            paths = new System.Collections.Generic.List<float[]>(); bridges = new System.Collections.Generic.List<float[]>();
            int skipped = 0;
            object terr = Terrain(world);
            uint sets = Convert.ToUInt32(Call(terr, "GetRoadTextureArraySize"));
            for (uint idx = 0; idx < sets; idx++)
            {
                System.Collections.IEnumerable splines = null;
                try { splines = Call(terr, "GetRoadTextureSetRoadSplines", (ushort)idx) as System.Collections.IEnumerable; }
                catch (Exception ex) { Log.Error("Reading road set " + idx + " failed", ex); }
                if (splines == null) continue;
                foreach (object sp in splines)
                {
                    // footpaths (CAW's sidewalk pieces) are in the same texture set; signs and streetlights belong to roads
                    Array knots = Call(sp, "GetKnotArray") as Array;
                    if (knots == null) continue;
                    float[] k = new float[knots.Length * 2];
                    for (int i = 0; i < knots.Length; i++) { k[i * 2] = F(knots.GetValue(i), "x"); k[i * 2 + 1] = F(knots.GetValue(i), "y"); }
                    if (IsFootpath(sp)) { skipped++; paths.Add(k); continue; }
                    if (Flag(sp, "IsRoadBridge")) bridges.Add(k);
                    list.Add(k);
                }
            }
            if (skipped > 0 || bridges.Count > 0) Log.Info("Road splines: " + list.Count + " roads (" + bridges.Count + " on bridges), " + skipped + " footpath pieces");
            return list;
        }

        /// <summary>Sims3.CSHost.RoadSpline.IsFootpath / IsRoadBridge (CSHostWrap); false if the property can't be read.</summary>
        private static bool IsFootpath(object spline) { return Flag(spline, "IsFootpath"); }

        private static bool Flag(object spline, string name)
        {
            try
            {
                PropertyInfo p = spline.GetType().GetProperty(name, All);
                return p != null && Convert.ToBoolean(p.GetValue(spline, null));
            }
            catch { return false; }
        }

        private static float F(object o, string field)
        {
            FieldInfo f = o.GetType().GetField(field, All);
            return f == null ? 0 : Convert.ToSingle(f.GetValue(o));
        }

        // ---------------- object catalog and placement ----------------

        public sealed class CatalogItem
        {
            public string Name, Path;   // Path = the Metadata panel folders above it, e.g. "Game Objects/Environment/Rocks"
            public bool IsTree;
            public object Key;          // Sims3.CSHost.ResourceKey
            /// <summary>The key's instance as 16 hex digits (matches the reference library), or "".</summary>
            public string InstanceHex
            {
                get
                {
                    if (Key == null) return "";
                    FieldInfo f = Key.GetType().GetField("mInstance");
                    return f == null ? "" : Convert.ToUInt64(f.GetValue(Key)).ToString("x16");
                }
            }
            public override string ToString() { return Name; }
        }

        /// <summary>A Sims3.CSHost.ResourceKey(type, group, instance), e.g. for an object CAW Plus made itself.</summary>
        public static object MakeKey(uint type, uint group, ulong instance)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type kt = a.GetType("Sims3.CSHost.ResourceKey", false);
                if (kt != null) return Activator.CreateInstance(kt, new object[] { type, group, instance });
            }
            throw new InvalidOperationException("Sims3.CSHost.ResourceKey not found");
        }

        /// <summary>Everything in CAW's Metadata panel that can be placed: SpeedTrees and game objects.</summary>
        public static System.Collections.Generic.List<CatalogItem> Catalog()
        {
            System.Collections.Generic.List<CatalogItem> list = new System.Collections.Generic.List<CatalogItem>();
            object view = GetProperty(MainForm, "MetadataView");
            object nodes = view == null ? null : GetProperty(view, "Nodes");
            Walk(nodes as System.Collections.IEnumerable, "", list);

            // SpeedTrees straight from the resource manager (SuperCAW's panel may not list them), as CAW's
            // GetSpeedTreeNodes does: type 0x00B552EA, name from the file name, localised "kTreeName_<name>".
            System.Collections.Generic.Dictionary<string, bool> have = new System.Collections.Generic.Dictionary<string, bool>();
            foreach (CatalogItem c in list) if (c.IsTree) have[c.Name] = true;
            try
            {
                Type rm = FindType("Sims3.CSHost.ResourceMgr");
                MethodInfo keys = rm == null ? null : rm.GetMethod("GetKeyList", new[] { typeof(uint), typeof(uint), typeof(uint) });
                MethodInfo fname = rm == null ? null : rm.GetMethod("GetFilenameForKey", BindingFlags.Static | BindingFlags.Public);
                Type utils = Asm.GetType("WorldBuilderCS.WorldBuilderUtils");
                MethodInfo loc = utils == null ? null : utils.GetMethod("GetLocalizedString", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(string), typeof(string) }, null);
                Array arr = keys == null ? null : keys.Invoke(null, new object[] { 0u, 0x00B552EAu, 0u }) as Array;
                int added = 0;
                if (arr != null)
                    foreach (object key in arr)
                    {
                        string file = Convert.ToString(fname.Invoke(null, new[] { key }));
                        if (string.IsNullOrEmpty(file) || string.Equals(file, "treemissingtree", StringComparison.OrdinalIgnoreCase)) continue;
                        string name = file;
                        try { if (loc != null) name = Convert.ToString(loc.Invoke(null, new object[] { "kTreeName_", file })); } catch { }
                        if (string.IsNullOrEmpty(name)) name = file;
                        if (have.ContainsKey(name)) continue;
                        have[name] = true;
                        CatalogItem c = new CatalogItem(); c.IsTree = true; c.Name = name + " (" + file + ")"; c.Key = key; c.Path = "SpeedTrees";
                        list.Add(c); added++;
                    }
                Log.Info("SpeedTrees from resource manager: " + added + (arr == null ? " (list unavailable)" : " of " + arr.Length));
            }
            catch (Exception ex) { Log.Error("Listing SpeedTrees failed", ex); }

            // Top-level catalog folders, to tune filtering.
            System.Collections.Generic.Dictionary<string, int> folders = new System.Collections.Generic.Dictionary<string, int>();
            foreach (CatalogItem c in list)
            {
                string[] parts = (c.Path ?? "").Split('/');
                string top = parts.Length > 2 ? parts[0] + "/" + parts[1] + "/" + parts[2] : c.Path;
                int n; folders.TryGetValue(top ?? "", out n); folders[top ?? ""] = n + 1;
            }
            foreach (System.Collections.Generic.KeyValuePair<string, int> kv in folders) Log.Info("Catalog folder '" + kv.Key + "': " + kv.Value);
            return list;
        }

        // ---------------- routable surface ----------------

        /// <summary>
        /// Paints CAW's Routable Surface at each world (x, z) with a square brush of 'size', the way its Routable Surface
        /// brush does (World.Start/Apply/FinishRoutableSurfacePaint; surface 0 = erase, 1 = no Sims, 2 = no camera),
        /// then submits one ApplyBrushCommand so Ctrl+Z takes it all back.
        /// </summary>
        public static int PaintRoutable(object world, System.Collections.Generic.List<float[]> points, uint size, uint surface, ProgressFn progress)
        {
            MethodInfo apply = FindMethod(world.GetType(), "ApplyRoutableSurfacePaint", 1);
            if (apply == null) throw new InvalidOperationException("World.ApplyRoutableSurfacePaint wasn't found");
            Type pt = apply.GetParameters()[0].ParameterType;
            if (pt.IsByRef) pt = pt.GetElementType();
            object prm = Activator.CreateInstance(pt);
            SetNum(prm, "mShape", 1);          // square (TerrainBrushesManager.Brush.Shape.kSquare)
            SetNum(prm, "mSize", size);
            SetNum(prm, "mColor", surface);
            SetNum(prm, "mOpacity", 100);
            SetNum(prm, "mFallOff", 100);
            FieldInfo fPt = pt.GetField("mCurPt", All);
            if (fPt == null) throw new InvalidOperationException("TerrainPaintBrushParams.mCurPt wasn't found");
            object[] args = { prm };
            Call(world, "StartRoutableSurfacePaint");
            int done = 0;
            try
            {
                foreach (float[] p in points)
                {
                    fPt.SetValue(args[0], Vec2(fPt.FieldType, p[0], p[1]));
                    apply.Invoke(world, args);
                    if ((++done & 1023) == 0 && progress != null) { progress("Blocking steep ground", 100 * done / points.Count); System.Windows.Forms.Application.DoEvents(); }
                }
            }
            finally
            {
                Call(world, "FinishRoutableSurfacePaint");
                Type ct = FindType("WorldBuilderCS.ApplyBrushCommand");
                if (ct != null) Call(MainForm, "SubmitUndoCommand", Activator.CreateInstance(ct, true));
            }
            return done;
        }

        private static void SetNum(object boxed, string field, uint v)
        {
            FieldInfo f = boxed.GetType().GetField(field, All);
            if (f != null) f.SetValue(boxed, Convert.ChangeType(v, f.FieldType));
        }

        // ---------------- road looks (texture sets) ----------------

        /// <summary>The world's road texture sets by index (CAW's Road panel list); set 0 is the base game road.</summary>
        public static System.Collections.Generic.List<string> RoadSets(object world, out int selected)
        {
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            selected = -1;
            object terr = Terrain(world);
            uint size = Convert.ToUInt32(Call(terr, "GetRoadTextureArraySize"));
            for (uint i = 0; i < size; i++)
            {
                string n = null;
                try { n = Convert.ToString(Call(terr, "GetRoadName", (ushort)i)); } catch { }
                if (string.IsNullOrEmpty(n)) n = i == 0 ? "Base game road" : "Road look " + i;
                names.Add(n);
            }
            try { selected = Convert.ToInt32(Call(terr, "GetRoadTextureIdx")); } catch { }
            if (selected >= names.Count) selected = -1;
            return names;
        }

        /// <summary>Makes a road texture set active, as CAW's Road panel 'Set active' does: roads built after this use it.</summary>
        public static void SelectRoadSet(object world, int idx)
        {
            Call(Terrain(world), "SetRoadTextureIdx", (ushort)idx);
        }

        /// <summary>
        /// CAW's own 'Add road textures' dialog (RoadTexturesForm), then what its Road panel does with the result:
        /// each picture converted to a DDS resource (WorldBuilderUtils.ConvertImageToDDS) and the set added with
        /// WorldTerrain.AddRoadTextures. Returns the new set's index, or -1 if cancelled or refused.
        /// </summary>
        public static int AddRoadSetWithDialog(System.Windows.Forms.IWin32Window owner, object world, out string name)
        {
            name = null;
            Type ft = FindType("WorldBuilderCS.RoadTexturesForm"), ut = FindType("WorldBuilderCS.WorldBuilderUtils");
            if (ft == null || ut == null) throw new InvalidOperationException("CAW's road texture dialog wasn't found");
            System.Windows.Forms.Form f = (System.Windows.Forms.Form)Activator.CreateInstance(ft);
            try
            {
                if (f.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return -1;
                object terr = Terrain(world);
                MethodInfo add = null;
                foreach (MethodInfo m in terr.GetType().GetMethods())
                    if (m.Name == "AddRoadTextures" && m.GetParameters().Length == 17) { add = m; break; }
                MethodInfo conv = null;
                foreach (MethodInfo m in ut.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (m.Name == "ConvertImageToDDS" && m.GetParameters().Length == 2) { conv = m; break; }
                if (add == null || conv == null) throw new InvalidOperationException("CAW's AddRoadTextures / ConvertImageToDDS wasn't found");
                Type keyType = add.GetParameters()[2].ParameterType;
                string[] props = { "StreetBaseFilePath", "StreetDetailFilePath", "SidewalkBaseFilePath", "SidewalkNormalMapFilePath",
                    "SidewalkOpacityMapFilePath", "CornerFilePath", "CornerNormalMapFilePath", "CrosswalkFilePath" };
                object[] args = new object[17];
                name = Convert.ToString(GetProperty(f, "RoadName"));
                args[0] = name;
                for (int k = 0; k < props.Length; k++)
                {
                    string path = Convert.ToString(GetProperty(f, props[k]));
                    object key = ZeroOf(keyType);
                    if (!string.IsNullOrEmpty(path))
                    {
                        object[] ca = { path, null };
                        bool ok = Convert.ToBoolean(conv.Invoke(null, ca));
                        if (ok && ca[1] != null) key = ca[1];
                        Log.Info(string.Format("Road look '{0}': {1} = {2} ({3})", name, props[k], path, ok ? "converted" : "not converted"));
                    }
                    args[1 + 2 * k] = path ?? "";
                    args[2 + 2 * k] = key;
                }
                uint before = Convert.ToUInt32(Call(terr, "GetRoadTextureArraySize"));
                add.Invoke(terr, args);
                uint after = Convert.ToUInt32(Call(terr, "GetRoadTextureArraySize"));
                return after > before ? (int)before : -1;
            }
            finally { f.Dispose(); }
        }

        private static object ZeroOf(Type t)
        {
            PropertyInfo p = t.GetProperty("Zero", BindingFlags.Static | BindingFlags.Public);
            if (p != null) return p.GetValue(null, null);
            FieldInfo fi = t.GetField("Zero", BindingFlags.Static | BindingFlags.Public);
            if (fi != null) return fi.GetValue(null);
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void Walk(System.Collections.IEnumerable nodes, string path, System.Collections.Generic.List<CatalogItem> list)
        {
            if (nodes == null) return;
            foreach (object node in nodes)
            {
                string text = Convert.ToString(GetProperty(node, "Text"));
                object tag = GetProperty(node, "Tag");
                if (tag != null)
                {
                    string tn = tag.GetType().Name;
                    if (tn == "SpeedTreeInfo")
                    {
                        CatalogItem c = new CatalogItem(); c.IsTree = true; c.Name = Convert.ToString(GetProperty(tag, "Name"));
                        c.Key = GetProperty(tag, "Key"); c.Path = path; list.Add(c);
                    }
                    else if (tn == "ResourceKey")
                    {
                        CatalogItem c = new CatalogItem(); c.IsTree = false; c.Name = text; c.Key = tag; c.Path = path; list.Add(c);
                    }
                }
                Walk(GetProperty(node, "Nodes") as System.Collections.IEnumerable, path.Length == 0 ? text : path + "/" + text, list);
            }
        }

        public static bool HasActiveLayer(object world)
        {
            object m = Call(world, "GetActiveObjectManager");
            if (m is IDisposable) ((IDisposable)m).Dispose();
            return m != null;
        }

        private static object SubType(string name)
        {
            Type t = Asm.GetType("WorldBuilderCS.PlaceNewObjectTool+eToolSubType", true);
            return Enum.Parse(t, name);
        }

        private static ConstructorInfo Ctor(Type t, int n, Type firstParam)
        {
            string key = t.AssemblyQualifiedName + "|" + n + "|" + (firstParam == null ? "" : firstParam.AssemblyQualifiedName);
            lock (ctorCache)
            {
                ConstructorInfo hit;
                if (ctorCache.TryGetValue(key, out hit)) return hit;
            }
            foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                ParameterInfo[] ps = c.GetParameters();
                if (ps.Length == n && (firstParam == null || ps[0].ParameterType == firstParam))
                {
                    lock (ctorCache) ctorCache[key] = c;
                    return c;
                }
            }
            throw new MissingMethodException(t.FullName, ".ctor/" + n);
        }

        // Where Place() spends its time (Stopwatch ticks), so a log shows whether CAW or CAW Plus is the slow part.
        private static readonly long[] placeTicks = new long[5];
        private static int placeCount;
        private static readonly string[] placeSteps = { "create", "AddGameObject", "position", "SubmitUndoCommand", "Move" };

        public static void ResetPlaceTiming() { Array.Clear(placeTicks, 0, placeTicks.Length); placeCount = 0; }

        // Where CreateLot() spends its time: CAW's IsValidLotLocation test, building the description, CAW's CmdPlaceNewLot
        // (the tester's 2026-10-25 log: 4 s a lot on a generated Roaring Heights with 267 road pieces; 0.2 s on 24 Oct).
        private static readonly long[] lotTicks = new long[3];
        private static int lotCount;
        public static void ResetLotTiming() { Array.Clear(lotTicks, 0, lotTicks.Length); lotCount = 0; }
        public static string LotTimingSummary()
        {
            if (lotCount == 0) return "no lots timed";
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency / lotCount;
            return string.Format("{0} lots, ms each: IsValidLotLocation {1:F0}, description {2:F0}, CmdPlaceNewLot {3:F0}", lotCount, lotTicks[0] * f, lotTicks[1] * f, lotTicks[2] * f);
        }

        /// <summary>"N objects, x ms each: create a, AddGameObject b, ..." for the log.</summary>
        public static string PlaceTimingSummary()
        {
            if (placeCount == 0) return "no objects timed";
            double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency / placeCount;
            long all = 0; foreach (long t in placeTicks) all += t;
            List<string> parts = new List<string>();
            for (int i = 0; i < placeTicks.Length; i++) parts.Add(string.Format("{0} {1:F1}", placeSteps[i], placeTicks[i] * f));
            return string.Format("{0} objects, {1:F1} ms each in Place: {2} (ms)", placeCount, all * f, string.Join(", ", parts.ToArray()));
        }

        /// <summary>
        /// Places one catalog item at world (x, z) with heading 'yaw' (radians) in the active World Layer, the same
        /// way CAW's object tool does, and submits an undo command ('group' joins it to the previous one, so one
        /// Ctrl+Z removes a whole Auto-Place run). Returns the placed object or null.
        /// </summary>
        public static object Place(CatalogItem item, float x, float z, float yaw, uint variant, bool group, float y = float.NaN)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Type argsType = Asm.GetType("WorldBuilderCS.AddGameObjectEventArgs", true);
            Type keyType = item.Key.GetType();
            object e, cmd;
            if (item.IsTree)
            {
                e = Ctor(argsType, 7, keyType).Invoke(new object[] { item.Key, variant, null, SubType("kSpeedTree"), x, z, 0u });
                cmd = Ctor(Asm.GetType("WorldBuilderCS.CmdAddTreeObject", true), 4, keyType)
                    .Invoke(new object[] { item.Key, item.Name, variant, group });
            }
            else
            {
                FieldInfo fg = keyType.GetField("mGroup"), fi = keyType.GetField("mInstance");
                object key = Ctor(keyType, 3, typeof(uint)).Invoke(new object[] { 47985727u, fg.GetValue(item.Key), fi.GetValue(item.Key) });
                Type simObj = keyType.Assembly.GetType("Sims3.CSHost.SimulationObject", true);
                ulong id = Convert.ToUInt64(simObj.GetMethod("CreateGameObject", BindingFlags.Static | BindingFlags.Public).Invoke(null, new[] { key }));
                Type rcsType = Asm.GetType("WorldBuilderCS.WorldHost.RCSGameObject", true);
                object rcs = Ctor(rcsType, 1, typeof(ulong)).Invoke(new object[] { id });
                if (!Convert.ToBoolean(GetProperty(rcs, "Valid"))) return null;
                e = Ctor(argsType, 6, keyType).Invoke(new object[] { key, rcs, SubType("kDataBase"), x, z, 0u });
                Type cmdType = Asm.GetType("WorldBuilderCS.CmdAddDatabaseObject", true);
                Type keyKind = cmdType.GetNestedType("KeyType", BindingFlags.Public | BindingFlags.NonPublic);
                cmd = Ctor(cmdType, 4, keyType).Invoke(new object[] { key, Enum.Parse(keyKind, "kResourceKey"), item.Name, group });
            }
            return Finish(e, cmd, item.Name, x, z, yaw, y, t0);
        }

        /// <summary>
        /// Places one effect (CAW's Effects in the Metadata panel, e.g. "waterfall_1_med") at world (x, z) and height y
        /// (NaN = the ground), turned by yaw, the way CAW's object tool does (CmdAddEffectObject, eToolSubType.kEffect).
        /// The name must be in EffectNames(). Returns the placed object or null.
        /// </summary>
        public static object PlaceEffect(string effectName, string label, float x, float z, float yaw, bool group, float y = float.NaN)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Type argsType = Asm.GetType("WorldBuilderCS.AddGameObjectEventArgs", true);
            object e = Ctor(argsType, 7, typeof(string)).Invoke(new object[] { null, effectName, null, SubType("kEffect"), x, z, 0u });
            object cmd = Ctor(Asm.GetType("WorldBuilderCS.CmdAddEffectObject", true), 3, typeof(string)).Invoke(new object[] { effectName, label, group });
            return Finish(e, cmd, label, x, z, yaw, y, t0);
        }

        /// <summary>The effect names CAW knows (Sims3.CSHost.EffectComponent.GetEffectNameList), as its Metadata panel lists them.</summary>
        public static List<string> EffectNames()
        {
            List<string> names = new List<string>();
            Type ec = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                ec = a.GetType("Sims3.CSHost.EffectComponent", false);
                if (ec != null) break;
            }
            if (ec == null) return names;
            System.Collections.IEnumerable list = ec.GetMethod("GetEffectNameList", BindingFlags.Static | BindingFlags.Public).Invoke(null, null) as System.Collections.IEnumerable;
            if (list != null) foreach (object o in list) if (o != null) names.Add(o.ToString());
            return names;
        }

        private static object Finish(object e, object cmd, string name, float x, float z, float yaw, float y, long t0)
        {
            long t1;
            object form = MainForm;
            PropertyInfo nameProp = e.GetType().GetProperty("InstanceName", All);
            if (nameProp != null) nameProp.SetValue(e, name, null);

            MethodInfo add = FindMethod(form.GetType(), "AddGameObject", 2);
            object[] a = { e, null };
            t1 = System.Diagnostics.Stopwatch.GetTimestamp(); placeTicks[0] += t1 - t0; t0 = t1;
            bool added = Convert.ToBoolean(add.Invoke(form, a));
            t1 = System.Diagnostics.Stopwatch.GetTimestamp(); placeTicks[1] += t1 - t0; t0 = t1;
            if (!added || a[1] == null) return null;
            object placed = a[1];

            // Same steps as CAW's object tool: put it on the ground (plus a tree's own vertical offset), turn it,
            // submit the add command, then a grouped Move so undo/redo keep the position.
            object world = CurrentWorld;
            MethodInfo setPos = FindMethod(placed.GetType(), "SetPreliminaryPosition", 1);
            Type v3Type = setPos.GetParameters()[0].ParameterType.GetElementType();
            object pos = Vec3(v3Type, x, float.IsNaN(y) ? Convert.ToSingle(Call(world, "GetTerrainHeight", x, z)) : y, z);
            MethodInfo offset = FindMethod(placed.GetType(), "ApplyTreeVerticalOffset", 1);
            object[] pa = { pos };
            if (offset != null && float.IsNaN(y)) { offset.Invoke(placed, pa); pos = pa[0]; }
            pa = new[] { pos };
            setPos.Invoke(placed, pa);
            MethodInfo rot = FindMethod(placed.GetType(), "ReplaceEulerRotation", 1);
            if (rot != null) rot.Invoke(placed, new[] { Vec3(v3Type, 0, yaw, 0) });

            for (Type t = cmd.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField("mGameObject", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(cmd, placed); break; }
            }
            t1 = System.Diagnostics.Stopwatch.GetTimestamp(); placeTicks[2] += t1 - t0; t0 = t1;
            Call(form, "SubmitUndoCommand", cmd);
            t1 = System.Diagnostics.Stopwatch.GetTimestamp(); placeTicks[3] += t1 - t0; t0 = t1;
            object finalPos = GetProperty(placed, "Position"), finalRot = GetProperty(placed, "Rotation");
            Call(placed, "Move", finalPos, finalRot, Vec3(v3Type, 0, 0, 0), Vec3(v3Type, 0, 0, 0), true);
            placeTicks[4] += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            placeCount++;
            return placed;
        }

        /// <summary>
        /// Places a road bridge the way CAW's object tool does: the bridge object (deck at height y, turned by yaw), then
        /// CmdCreateRoadBridge, which lays a bridge road piece between the object's first and last routing slots.
        /// Returns that road piece (connect roads to its ports 0 and 1), or null if the object isn't a road bridge.
        /// </summary>
        public static object PlaceBridge(CatalogItem item, float x, float z, float y, float yaw, bool group)
        {
            object placed = Place(item, x, z, yaw, 0, group, y);
            if (placed == null) { Log.Info("  bridge object '" + item.Name + "' could not be placed"); return null; }
            Type t = Asm.GetType("WorldBuilderCS.CmdCreateRoadBridge", true);
            object cmd = Ctor(t, 3, null).Invoke(new object[] { Terrain(CurrentWorld), placed, true });
            if (!Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd)))
            {
                Log.Info("  '" + item.Name + "' has no routing slots (not a road bridge); removed it");
                try { Delete(placed, true); } catch (Exception ex) { Log.Error("Removing the bridge object failed", ex); }
                return null;
            }
            object bridge = GetField(cmd, "mNewRoadBridge");
            return bridge == null ? null : GetProperty(bridge, "RoadSpline");
        }

        /// <summary>A catalog item for a game object by instance, for objects the Metadata panel doesn't list.</summary>
        public static CatalogItem ObjectByInstance(string name, ulong instance)
        {
            Type keyType = FindType("Sims3.CSHost.ResourceKey");
            if (keyType == null) return null;
            CatalogItem c = new CatalogItem();
            c.Name = name; c.Path = ""; c.IsTree = false;
            c.Key = Ctor(keyType, 3, typeof(uint)).Invoke(new object[] { 0x02DC343Fu, 0u, instance });
            return c;
        }

        private static object Vec3(Type t, float x, float y, float z)
        {
            object v = Activator.CreateInstance(t);
            t.GetField("x").SetValue(v, x); t.GetField("y").SetValue(v, y); t.GetField("z").SetValue(v, z);
            return v;
        }

        /// <summary>
        /// Makes the World Layer called 'name' the active one (where new objects go), creating it if needed the way
        /// CAW's own "Add layer" menu does (World.CreateNewLayer + tree node + editable list), then naming it.
        /// </summary>
        public static bool UseLayer(object world, string name)
        {
            object control = GetField(MainForm, "mWorldLayersControl");
            if (control == null) return false;
            object layer = FindLayer(control, name);
            if (layer == null)
            {
                System.Collections.Generic.List<object> before = Layers(control);
                object tree = GetField(control, "mTreeControl");
                System.Collections.IList roots = GetProperty(tree, "Nodes") as System.Collections.IList;
                if (roots == null || roots.Count == 0) return false;
                FieldInfo ctx = control.GetType().GetField("mContextMenuNode", All);
                ctx.SetValue(control, roots[0]); // the world node, as when right-clicking it
                MethodInfo add = FindMethod(control.GetType(), "OnAddLayer", 2);
                add.Invoke(control, new object[] { null, EventArgs.Empty });
                foreach (object l in Layers(control))
                {
                    bool isNew = true;
                    foreach (object b in before) if (ReferenceEquals(b, l) || Equals(GetProperty(b, "Id"), GetProperty(l, "Id"))) { isNew = false; break; }
                    if (isNew) { layer = l; break; }
                }
                if (layer == null) return false;
                Call(layer, "SetName", name);
                // show the name in the World Layers panel
                try
                {
                    object node = Call(tree, "GetNodeFromKey", GetProperty(layer, "Id"));
                    if (node != null) node.GetType().GetProperty("Text").SetValue(node, name, null);
                }
                catch { }
                Log.Info("Created World Layer '" + name + "'");
            }
            Call(world, "SetActiveObjectManager", layer);
            return true;
        }

        /// <summary>
        /// CAW only places objects into an active World Layer that isn't the Global Layer ("Please add a new Layer before
        /// adding an object"). If there isn't one, use (or create) the layer called 'name'.
        /// </summary>
        public static bool EnsureLayer(object world, string name)
        {
            try
            {
                object active = Call(world, "GetActiveObjectManager"), global = Call(world, "GetGlobalObjectManager");
                bool ok = active != null && !(global != null && Equals(GetProperty(active, "Id"), GetProperty(global, "Id")));
                if (active is IDisposable) ((IDisposable)active).Dispose();
                if (global is IDisposable) ((IDisposable)global).Dispose();
                if (ok) return true;
                Log.Info("No usable World Layer is active; using '" + name + "'");
                return UseLayer(world, name);
            }
            catch (Exception ex) { Log.Error("Checking the active World Layer failed", ex); return UseLayer(world, name); }
        }

        private static System.Collections.Generic.List<object> Layers(object control)
        {
            System.Collections.Generic.List<object> list = new System.Collections.Generic.List<object>();
            System.Collections.IEnumerable all = Call(control, "GetAllLayers") as System.Collections.IEnumerable;
            if (all != null) foreach (object l in all) list.Add(l);
            return list;
        }

        private static object FindLayer(object control, string name)
        {
            foreach (object l in Layers(control))
            {
                string n = Convert.ToString(Call(l, "GetName"));
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return l;
            }
            return null;
        }

        /// <summary>Removes an object the undoable way (CAW's own delete command).</summary>
        /// <summary>Removes road pieces (undoable, as CAW's Delete key does).</summary>
        public static void DeleteRoads(object[] roads, bool group)
        {
            Type t = Asm.GetType("WorldBuilderCS.CmdDeleteRoadObjects", true);
            ConstructorInfo c = Ctor(t, 2, typeof(bool));
            Array arr = Array.CreateInstance(c.GetParameters()[1].ParameterType.GetElementType(), roads.Length);
            for (int i = 0; i < roads.Length; i++) arr.SetValue(roads[i], i);
            Call(MainForm, "SubmitUndoCommand", c.Invoke(new object[] { group, arr }));
        }

        public static void Delete(object placed, bool group)
        {
            Type t = Asm.GetType("WorldBuilderCS.CmdDeleteGameObject", true);
            object cmd = Ctor(t, 2, null).Invoke(new object[] { placed, group });
            Call(MainForm, "SubmitUndoCommand", cmd);
        }

        /// <summary>Every object already in the world (outside lots) as name + world x, z.</summary>
        public static System.Collections.Generic.List<object[]> ExistingObjects()
        {
            System.Collections.Generic.List<object[]> list = new System.Collections.Generic.List<object[]>();
            System.Collections.IEnumerable objs = Call(MainForm, "GetAllWorldObjects") as System.Collections.IEnumerable;
            if (objs == null) return list;
            foreach (object o in objs)
            {
                try
                {
                    object pos = GetProperty(o, "Position");
                    list.Add(new object[] { Convert.ToString(GetProperty(o, "InstanceName")), F(pos, "x"), F(pos, "z") });
                }
                catch { }
            }
            return list;
        }

        public sealed class WorldObject
        {
            public string Name, Layer;
            public float X, Z, Radius;   // world metres; Radius = half the footprint (bounding box across x/z)
            public bool Tree;            // a SpeedTree
        }

        /// <summary>Every object in every World Layer (not on lots), with its layer and footprint.</summary>
        public static System.Collections.Generic.List<WorldObject> WorldObjects()
        {
            System.Collections.Generic.List<WorldObject> list = new System.Collections.Generic.List<WorldObject>();
            object control = GetField(MainForm, "mWorldLayersControl");
            if (control == null) return list;
            foreach (object layer in Layers(control))
            {
                string lname = "";
                try { lname = Convert.ToString(Call(layer, "GetName")); } catch { }
                uint count = Convert.ToUInt32(GetProperty(layer, "ObjectCount"));
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        object o = Call(layer, "GetObjectAtIndex", i);
                        if (o == null) continue;
                        object mgr = GetProperty(o, "Manager");
                        if (mgr != null && mgr.GetType().Name == "Lot") continue;
                        object pos = GetProperty(o, "Position");
                        WorldObject w = new WorldObject();
                        w.Name = Convert.ToString(GetProperty(o, "InstanceName")); w.Layer = lname;
                        w.X = F(pos, "x"); w.Z = F(pos, "z"); w.Radius = 1.5f;
                        try
                        {
                            object go = GetProperty(o, "GameObject");
                            w.Tree = go != null && GetProperty(go, "TreeComponent") != null;
                            MethodInfo bb = FindMethod(o.GetType(), "GetBoundingBox", 1);
                            object[] args = new object[1];
                            if (bb != null && Convert.ToBoolean(bb.Invoke(o, args)) && args[0] != null)
                            {
                                object mn = args[0].GetType().GetField("min").GetValue(args[0]), mx = args[0].GetType().GetField("max").GetValue(args[0]);
                                float ex = Math.Max(Math.Abs(F(mn, "x")), Math.Abs(F(mx, "x"))), ez = Math.Max(Math.Abs(F(mn, "z")), Math.Abs(F(mx, "z")));
                                w.Radius = Math.Min(20f, (float)Math.Sqrt(ex * ex + ez * ez));
                            }
                        }
                        catch { }
                        list.Add(w);
                    }
                    catch { }
                }
            }
            return list;
        }

        /// <summary>Names of the world's World Layers.</summary>
        public static System.Collections.Generic.List<string> LayerNames()
        {
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            object control = GetField(MainForm, "mWorldLayersControl");
            if (control == null) return names;
            foreach (object l in Layers(control)) { try { names.Add(Convert.ToString(Call(l, "GetName"))); } catch { } }
            return names;
        }

        /// <summary>CAW's own Cluster Trees (the toolbar button; CAW also does this when saving): merges SpeedTrees into
        /// clusters, which render much faster.</summary>
        public static void ClusterTrees()
        {
            object control = GetField(MainForm, "mWorldLayersControl");
            if (control == null) throw new InvalidOperationException("no World Layers panel");
            MethodInfo m = control.GetType().GetMethod("AutoClusterTrees", All, null, new[] { typeof(bool) }, null);
            m.Invoke(control, new object[] { true });
        }

        /// <summary>Screenshot of CAW's 3D view (for picking colours).</summary>
        public static System.Drawing.Bitmap CaptureRenderPanel()
        {
            System.Windows.Forms.Control panel = GetField(MainForm, "mRenderPanel") as System.Windows.Forms.Control;
            if (panel == null || panel.Width < 2 || panel.Height < 2) return null;
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(panel.Width, panel.Height);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(panel.PointToScreen(System.Drawing.Point.Empty), System.Drawing.Point.Empty, panel.Size);
            return bmp;
        }

        // ---------------- heights and roads ----------------

        public static bool ImportHeightImage(object world, string path)
        {
            return Convert.ToBoolean(Call(world, "ImportHeightImage", path));
        }

        private static object Vec2(Type t, float x, float y)
        {
            object v = Activator.CreateInstance(t);
            t.GetField("x").SetValue(v, x); t.GetField("y").SetValue(v, y);
            return v;
        }

        /// <summary>Undoable road intersection at world (x, z), rotated 'rot' radians (atan2 direction, x towards z).
        /// Returns the RoadIntersection. CAW turns the other way (self-test, 2026-10-20), so the angle is negated here.</summary>
        public static object CreateIntersection(object world, float x, float z, float rot, bool group)
        {
            Type t = Asm.GetType("WorldBuilderCS.CmdCreateRoadIntersection", true);
            ConstructorInfo c = Ctor(t, 6, typeof(bool));
            Type v2 = c.GetParameters()[2].ParameterType;
            object cmd = c.Invoke(new object[] { group, Terrain(world), Vec2(v2, x, z), -rot, false, false });
            if (!Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd))) return null;
            return GetProperty(cmd, "NewRoadIntersection");
        }

        /// <summary>Undoable road spline: ends k0 and k3 (ports 0 and 1), controls k1 and k2; points are world (x, z).
        /// footpath = CAW's sidewalk (isSidewalk), the footpath network Sims walk on.</summary>
        public static object CreateSpline(object world, float[] k0, float[] k1, float[] k2, float[] k3, bool group, bool footpath = false)
        {
            Type t = Asm.GetType("WorldBuilderCS.CmdCreateRoadSpline", true);
            ConstructorInfo c = Ctor(t, 8, typeof(bool));
            Type v2 = c.GetParameters()[2].ParameterType;
            object cmd = c.Invoke(new object[] { group, Terrain(world), Vec2(v2, k0[0], k0[1]), Vec2(v2, k1[0], k1[1]),
                                                 Vec2(v2, k2[0], k2[1]), Vec2(v2, k3[0], k3[1]), footpath, false });
            if (!Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd))) return null;
            return GetProperty(cmd, "NewRoadSpline");
        }

        /// <summary>
        /// An undoable lot, the way CAW's Place Lot tool makes one (PlaceNewLotTool.MouseUp, decompiled): the four corners
        /// (world x, z) ordered so corner 0 -> 1 runs towards +x and 0 -> 3 towards +z, checked with World.IsValidLotLocation,
        /// then CmdPlaceNewLot(world, tNewLotDescription, group). Width = |1 - 0|, depth = |3 - 0| (whole metres), rotation
        /// = -angle(1 - 0, +x) when 1 - 0 points to +z. community: LotType.Commercial with 'subtype'
        /// (Sims3.SimIFace.CommercialLotSubType, e.g. 5 small park, 13 beach), else Residential. Returns false if refused.
        /// </summary>
        public static bool CreateLot(object world, float[][] corners, bool community, uint subtype, bool group, out string why)
        {
            why = "";
            // order the corners as the tool does
            float[][] p = (float[][])corners.Clone();
            if ((int)((p[1][0] - p[0][0]) * 1000) == 0) { float[] t = p[1]; p[1] = p[3]; p[3] = t; }
            if ((int)((p[1][0] - p[0][0]) * 1000) < 0) { float[] a = p[0]; p[0] = p[1]; p[1] = a; a = p[3]; p[3] = p[2]; p[2] = a; }
            if ((int)((p[3][1] - p[0][1]) * 1000) < 0) { float[] a = p[0]; p[0] = p[3]; p[3] = a; a = p[1]; p[1] = p[2]; p[2] = a; }
            float wx = p[1][0] - p[0][0], wz = p[1][1] - p[0][1], dx = p[3][0] - p[0][0], dz = p[3][1] - p[0][1];
            uint width = (uint)Math.Round(Math.Sqrt(wx * wx + wz * wz)), depth = (uint)Math.Round(Math.Sqrt(dx * dx + dz * dz));
            if (width == 0 || depth == 0) { why = "zero size"; return false; }
            lotCount++;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(), t1;
            Type cmdType = Asm.GetType("WorldBuilderCS.CmdPlaceNewLot", true);
            ConstructorInfo c = Ctor(cmdType, 3, null);
            Type descType = c.GetParameters()[1].ParameterType;
            Type v3 = descType.GetField("mPosition").FieldType;
            // valid here? (outside the world's edge, over water, on another lot or a road: refused, as the tool shows red)
            MethodInfo valid = world.GetType().GetMethod("IsValidLotLocation");
            if (valid != null)
            {
                Type arrType = valid.GetParameters()[0].ParameterType;
                if (arrType.IsByRef) arrType = arrType.GetElementType();
                Array pts = Array.CreateInstance(arrType.GetElementType(), 4);
                for (int k = 0; k < 4; k++) pts.SetValue(Vec3(v3, p[k][0], Convert.ToSingle(Call(world, "GetTerrainHeight", p[k][0], p[k][1])), p[k][1]), k);
                object[] args = { pts };
                bool isValid = Convert.ToBoolean(valid.Invoke(world, args));
                t1 = System.Diagnostics.Stopwatch.GetTimestamp(); lotTicks[0] += t1 - t0; t0 = t1;
                if (!isValid) { why = "not a valid lot location"; return false; }
            }
            object desc = Activator.CreateInstance(descType);
            descType.GetField("mPosition").SetValue(desc, Vec3(v3, p[0][0], Convert.ToSingle(Call(world, "GetTerrainHeight", p[0][0], p[0][1])), p[0][1]));
            descType.GetField("mWidth").SetValue(desc, Convert.ChangeType(width, descType.GetField("mWidth").FieldType));
            descType.GetField("mHeight").SetValue(desc, Convert.ChangeType(depth, descType.GetField("mHeight").FieldType));
            double len = Math.Sqrt(wx * wx + wz * wz);
            float rot = (float)Math.Acos(Math.Max(-1, Math.Min(1, wx / len)));
            if ((int)(wz / len * 1000) >= 0) rot = -rot;
            descType.GetField("mRotation").SetValue(desc, rot);
            FieldInfo lt = descType.GetField("mLotType");
            lt.SetValue(desc, Enum.ToObject(lt.FieldType, community ? 1 : 0));
            descType.GetField("mAdditionalPropertyValue").SetValue(desc, 0f);
            descType.GetField("mBeautifulVistaBuf").SetValue(desc, 50f);
            FieldInfo st = descType.GetField("mLotSubtype");
            st.SetValue(desc, Convert.ChangeType(community ? subtype : 0u, st.FieldType));
            object cmd = c.Invoke(new object[] { world, desc, group });
            t1 = System.Diagnostics.Stopwatch.GetTimestamp(); lotTicks[1] += t1 - t0; t0 = t1;
            bool placed = Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd));
            lotTicks[2] += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            if (!placed) { why = "CAW refused the lot"; return false; }
            return true;
        }

        public static void ConnectPorts(object a, uint portA, object b, uint portB)
        {
            Type t = Asm.GetType("WorldBuilderCS.CmdConnectRoadPorts", true);
            object cmd = Ctor(t, 5, typeof(bool)).Invoke(new object[] { true, a, portA, b, portB });
            Call(MainForm, "SubmitUndoCommand", cmd);
        }

        public static float TerrainHeight(object world, float x, float z) { return Convert.ToSingle(Call(world, "GetTerrainHeight", x, z)); }

        /// <summary>The road object joined to this port, or null if the port is free.</summary>
        public static object ConnectedTo(object road, uint port) { return Call(road, "GetConnectedObject", port); }

        /// <summary>CAW's own test (as its road tool uses) for an empty port near this road end to snap to.</summary>
        public static bool CanSnap(object world, object road, uint port, out object found, out uint foundPort)
        {
            found = null; foundPort = 0;
            try
            {
                object terr = Terrain(world);
                MethodInfo m = terr.GetType().GetMethod("CanSnapRoadObjectToAnyEmptyPort", All);
                if (m == null) return false;
                object[] a = { road, port, null, 0u };
                if (!Convert.ToBoolean(m.Invoke(terr, a))) return false;
                found = a[2]; foundPort = Convert.ToUInt32(a[3]);
                return found != null;
            }
            catch (Exception ex) { Log.Error("CanSnapRoadObjectToAnyEmptyPort failed", ex); return false; }
        }

        /// <summary>
        /// Moves one end of a road spline onto 'pos' (undoable, CmdTranslateSplineKnots like CAW's road tool), with its
        /// control point along 'outward' so the road leaves that spot straight; 'outward' points away from what it joins.
        /// </summary>
        public static bool MoveSplineEnd(object spline, uint port, float[] pos, float[] outward, bool group)
        {
            try
            {
                MethodInfo getKnot = spline.GetType().GetMethod("GetKnot", All);
                if (getKnot == null) return false;
                object k0 = getKnot.Invoke(spline, new object[] { 0 }), k3 = getKnot.Invoke(spline, new object[] { 3 });
                float len = (float)Math.Sqrt(Math.Pow(F(k3, "x") - F(k0, "x"), 2) + Math.Pow(F(k3, "y") - F(k0, "y"), 2));
                float arm = Math.Max(2f, len / 3);
                int end = port == 0 ? 0 : 3, ctrl = port == 0 ? 1 : 2;
                Type cmdType = Asm.GetType("WorldBuilderCS.CmdTranslateSplineKnots", true);
                object cmd = Ctor(cmdType, 1, typeof(bool)).Invoke(new object[] { group });
                Type v2 = k0.GetType();
                MethodInfo set = cmdType.GetMethod("AddSet", All);
                set.Invoke(cmd, new object[] { spline, end, Vec2(v2, pos[0], pos[1]) });
                set.Invoke(cmd, new object[] { spline, ctrl, Vec2(v2, pos[0] + outward[0] * arm, pos[1] + outward[1] * arm) });
                return Convert.ToBoolean(Call(MainForm, "SubmitUndoCommand", cmd));
            }
            catch (Exception ex) { Log.Error("Moving a road end failed", ex); return false; }
        }

        public static int PortCount(object road) { return Convert.ToInt32(Call(road, "GetNumPorts")); }
        public static float[] PortPosition(object road, uint port) { object v = Call(road, "GetPortPosition", port); return new[] { F(v, "x"), F(v, "y") }; }
        public static float[] PortNormal(object road, uint port) { object v = Call(road, "GetPortNormal", port); return new[] { F(v, "x"), F(v, "y") }; }

        public static string WorldName(object world)
        {
            string p = SavePath(world);
            string n = p.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(p).Trim() : "";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            n = n.Trim('.', ' ');
            return n.Length > 0 ? n : "UnsavedWorld";
        }

        /// <summary>
        /// Folder name for this world's backups and edit records: its name plus a hash of the full save path, so two
        /// worlds with the same file name (copies in other folders) never share or restore each other's data.
        /// </summary>
        public static string WorldKey(object world)
        {
            uint h = 2166136261;                                  // FNV-1a over the lower-case path
            foreach (char c in SavePath(world).ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return WorldName(world) + "_" + h.ToString("x8");
        }

        /// <summary>
        /// root\WorldKey; a folder from before v0.35 (keyed by name only) is moved there the first time.
        /// </summary>
        public static string WorldDir(string root, object world)
        {
            string dir = System.IO.Path.Combine(root, WorldKey(world)), old = System.IO.Path.Combine(root, WorldName(world));
            try { if (!System.IO.Directory.Exists(dir) && System.IO.Directory.Exists(old)) System.IO.Directory.Move(old, dir); }
            catch (Exception ex) { Log.Error("Moving " + old + " failed", ex); }
            return dir;
        }

        private static string SavePath(object world)
        {
            try { return Convert.ToString(Call(world, "GetSaveFilePath")) ?? ""; }
            catch { return ""; }
        }
    }
}
