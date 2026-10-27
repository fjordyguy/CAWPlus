using System;
using System.Collections.Generic;

namespace CawPlus
{
    public enum CoastType { None, OceanSide, Island, Lake, Islands, Iceland, Volcano, Orleans }

    internal sealed class GenSettings
    {
        public int Seed = 1;
        public float Mountains = 45, Hills = 50, Roughness = 40, FlatLand = 40, Water = 15; // 0..100
        public float Erosion = 50;          // 0..100: how deeply streams cut branching valleys
        public int Pockets = 5;             // gentler pockets round the town for neighbourhoods
        public int Rivers = 2;
        public float MaxHeight = 120;       // metres
        public float SeaLevel = 10;         // metres, CAW's sea level
        public CoastType Coast = CoastType.OceanSide;
        // learned from the world type (tools/Fidelity): land height above the sea, as a share of the top, at 0, 10 ... 100 %
        // of the land area (lowest to highest); null = the built-in curve (lowlands wider than peaks)
        public float[] HeightCurve;
        public bool InlandLakes = true;     // false: hollows below the sea away from the coast become dry land (most EA worlds)
        // the world type's own water split (percent of the map, measured; -1 = not set): sea touching the map edge, and
        // lakes at sea level. With them the sea level is set from the sea alone and inland hollows are kept as lakes only
        // up to the lake share (largest first), so rivers and ponds add their own water instead of flooding the lowlands.
        public float SeaShare = -1, LakeShare = -1;
        // sea side: -1 = any direction; 0 = straight along one map edge ... 100 = into a corner (sea on two edges, as
        // Riverview, Bridgeport, Sunset Valley); learned per world type (tools/Fidelity)
        public float CoastCorner = -1;
        public float RiverWidth = 1;        // x the built-in river width (EA's rivers: Riverview 2.8 % of the map; tuned per world type)
        public int Ponds;                   // ponds above sea level (EA: 0-10 per world, 0x707CA200), dug as hollows; Apply terrain fills them
        public float PondRadius = 50;       // metres, their typical size (EA: 16-270 m)
        // world type style (worlds.txt column 36, per EA world)
        public float Crag;                  // 0..100: hills dissected into gully networks with sharp ridges between (Aurora Skies)
        public float CragDepth = 10;        // metres: deepest gully cut at Crag 100
        public float Terrace;               // 0..100: land stepped into flat treads and steep risers (mesas: Lucky Palms, Oasis Landing)
        public float TerraceStep = 20;      // metres between steps
        public float Talus = 34;            // degrees: thermal erosion's slope limit on the full map (lower = softer hills)
        public float TownFlat = 0.85f;      // how far the town site is levelled (0..1)
        public float TownSize = 1;          // x the town site's size
        // rises and mounds on the levelled ground (town site, neighbourhood pockets, flat zones; #170, the tester: towns too flat):
        // EA's unused town ground has 4.6 bumps per hectare, 3.5 m high and 24 m across (medians of 22 worlds,
        // library/town_detail.json), plus a few broader knolls. Build roads and Place lots level them where they build.
        public float BumpHeight = 3.5f, BumpWidth = 24f, BumpDensity = 4.6f; // m, m (half-height width), per hectare
        // landform recipe: separate buttes on an open floor (Lucky Palms: 9 hill masses, median 10 ha, the largest 29% of
        // the raised ground, 97% of the floor one connected piece; tools/terrainreport.py "structure")
        public int Buttes;                  // how many (0 = no recipe: the usual continent / hills / mountains)
        public float ButteHa = 10;          // median footprint, hectares
        public int Tiers = 3;               // layers stepped into each butte's sides
        // landform recipe: the world type's hill masses laid out (#121; its structure figures, `<key>_terrain.json`): how
        // many, the median size, the largest's share of the raised ground and the raised share of the land. The mountains
        // and most of the hills stand inside them; the floor between stays open (EA's Sunset Valley: one 176 ha massif,
        // 58% of the land raised; Monte Vista: 11 small hills).
        public int Massifs;                 // how many (0 = no layout: the mountains go where a 1400 m noise puts them)
        public float MassifHa = 30;         // median size of the others, hectares of raised ground
        public float MassifLargest = 0.6f;  // the largest's share of all the raised ground
        public float MassifRaised = 0.5f;   // raised ground (over 15 m above the floor) as a share of the land
        // rounded hills (#139, #140): 0 = ridged crests, 1 = broad rounded mountains and rolling hills with soft tops and
        // fewer small bumps (the tester: Sunset Valley's peaks "crazy pointy", Riverview should be softer rolling hills)
        public float Round;
        // river fork (#139, the tester: "a pointy island like Pittsburgh"): the second river joins the first beside the town
        // centre, so the town stands on the point between them; RiverIsland splits the river round an island below it
        public bool Fork, RiverIsland;
        public float Meander = 1;           // x the rivers' bends (amplitude; the wavelength grows with it): Shang Simla's wind (#144)
        // despike (#140, #143, #147): summits narrower than ~2 x DespikeRadius are lowered towards the ground round them
        // (morphological opening), by Despike (0..1). EA's mountains are broad masses, ours had needle peaks
        public float Despike, DespikeRadius = 120;
        // keeptop (the tester's Aurora Skies test, 2026-10-25: the islands looked half-drowned): the despike cut lowers broad
        // highland masses too (land 99.5th percentile 119 -> 75 m), so the land above the plain is stretched back by this
        // share of the loss; the shapes stay rounded, the heights stay the world type's
        public float KeepTop;
        // nile (#145 Al Simhara): with the valley recipe, the first river runs down the middle of the valley floor
        public bool Nile;
        public bool TownLake;               // the largest pond beside the town centre, the town built round it (Twinbrook, #141)
        public bool NoCurve;                // heights scaled straight instead of by the world type's height curve, so a raised flat stays flat (#150)
        // raised plain (#150 Aurora Skies' lowland, #155 Bridgewood's town on a height): low land away from the water is
        // lifted by PlainLift metres, rising from the shore over PlainRamp metres (a tapered cliff); high land unchanged
        public float PlainLift, PlainRamp = 120;
        public float CragFloor = 3;         // no dissection below this height above the sea, full from ~22 m higher (Aurora Skies: its town lowland stays smooth)
        // escarpment (the tester's 2026-10-26 Aurora Skies test: a cliff between lowland and highland, lakes on top with
        // waterfalls over the edge): Scarp 0..1 narrows the Iceland highland's edge from ~250 m to ~25 m;
        // ScarpLakes ponds go on the highland just behind the cliff
        public float Scarp; public int ScarpLakes;
        public float Beaches = 1;           // < 1: fewer beaches (cliffs and rocky shore instead), the one by the town kept (#146)
        // valley recipe (#151, #152): how many valleys out from the town, floor width and side run (m), how strongly
        public int Valleys; public float ValleyWidth = 360, ValleySide = 220, ValleyStrength = 0.9f, ValleyFloorLevel = 0.42f;

        /// <summary>The style fields from a worlds.txt style column ("crag=60,depth=12,terrace=0,step=20,talus=34,town=0.85,townsize=1").</summary>
        public void SetStyle(string style)
        {
            if (string.IsNullOrEmpty(style)) return;
            foreach (string kv in style.Split(','))
            {
                string[] p = kv.Split('=');
                float v;
                if (p.Length != 2 || !float.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                switch (p[0].Trim().ToLowerInvariant())
                {
                    case "crag": Crag = v; break;
                    case "depth": CragDepth = v; break;
                    case "terrace": Terrace = v; break;
                    case "step": TerraceStep = Math.Max(3, v); break;
                    case "talus": Talus = Math.Max(15, Math.Min(60, v)); break;
                    case "town": TownFlat = Math.Max(0, Math.Min(1, v)); break;
                    case "townsize": TownSize = Math.Max(0.4f, Math.Min(2.5f, v)); break;
                    case "bumph": BumpHeight = Math.Max(0, Math.Min(10, v)); break;
                    case "bumpw": BumpWidth = Math.Max(10, Math.Min(60, v)); break;
                    case "bumpd": BumpDensity = Math.Max(0, Math.Min(12, v)); break;
                    case "buttes": Buttes = Math.Max(0, Math.Min(30, (int)v)); break;
                    case "butteha": ButteHa = Math.Max(1, Math.Min(80, v)); break;
                    case "tiers": Tiers = Math.Max(1, Math.Min(6, (int)v)); break;
                    case "massifs": Massifs = Math.Max(0, Math.Min(20, (int)v)); break;
                    case "massifha": MassifHa = Math.Max(1, Math.Min(400, v)); break;
                    case "largest": MassifLargest = Math.Max(0.1f, Math.Min(1, v)); break;
                    case "raised": MassifRaised = Math.Max(0.05f, Math.Min(0.9f, v)); break;
                    case "round": Round = Math.Max(0, Math.Min(1, v)); break;
                    case "fork": Fork = v > 0; break;
                    case "meander": Meander = Math.Max(0, Math.Min(5, v)); break;
                    case "despike": Despike = Math.Max(0, Math.Min(1, v)); break;
                    case "townlake": TownLake = v > 0; break;
                    case "scarp": Scarp = Math.Max(0, Math.Min(1, v)); break;
                    case "scarplakes": ScarpLakes = Math.Max(0, Math.Min(6, (int)v)); break;
                    case "beaches": Beaches = Math.Max(0, Math.Min(1, v)); break;
                    case "cragfloor": CragFloor = Math.Max(0, Math.Min(200, v)); break;
                    case "valleys": Valleys = Math.Max(0, Math.Min(5, (int)v)); break;
                    case "vwidth": ValleyWidth = Math.Max(60, Math.Min(900, v)); break;
                    case "vside": ValleySide = Math.Max(40, Math.Min(700, v)); break;
                    case "vfloor": ValleyFloorLevel = Math.Max(0, Math.Min(1, v)); break;
                    case "vstrength": ValleyStrength = Math.Max(0, Math.Min(1, v)); break;
                    case "plainlift": PlainLift = Math.Max(0, Math.Min(150, v)); break;
                    case "plainramp": PlainRamp = Math.Max(20, Math.Min(500, v)); break;
                    case "nocurve": NoCurve = v > 0; if (NoCurve) HeightCurve = null; break;
                    case "nile": Nile = v > 0; break;
                    case "keeptop": KeepTop = Math.Max(0, Math.Min(1, v)); break;
                    case "spiker": DespikeRadius = Math.Max(24, Math.Min(400, v)); break;
                    case "island": RiverIsland = v > 0; break;
                }
            }
        }

        public string Style()
        {
            System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c, "crag={0:0.#},depth={1:0.#},terrace={2:0.#},step={3:0.#},talus={4:0.#},town={5:0.##},townsize={6:0.##}",
                Crag, CragDepth, Terrace, TerraceStep, Talus, TownFlat, TownSize)
                + (Buttes > 0 ? string.Format(c, ",buttes={0},butteha={1:0.#},tiers={2}", Buttes, ButteHa, Tiers) : "")
                + (Massifs > 0 ? string.Format(c, ",massifs={0},massifha={1:0.#},largest={2:0.##},raised={3:0.##}", Massifs, MassifHa, MassifLargest, MassifRaised) : "")
                + string.Format(c, ",bumph={0:0.##},bumpw={1:0.#},bumpd={2:0.##}", BumpHeight, BumpWidth, BumpDensity)
                + (Round > 0 ? string.Format(c, ",round={0:0.##}", Round) : "")
                + (Meander != 1 ? string.Format(c, ",meander={0:0.##}", Meander) : "")
                + (Despike > 0 ? string.Format(c, ",despike={0:0.##},spiker={1:0}", Despike, DespikeRadius) : "")
                + (KeepTop > 0 ? string.Format(c, ",keeptop={0:0.##}", KeepTop) : "") + (Nile ? ",nile=1" : "")
                + (TownLake ? ",townlake=1" : "") + (NoCurve ? ",nocurve=1" : "")
                + (PlainLift > 0 ? string.Format(c, ",plainlift={0:0},plainramp={1:0}", PlainLift, PlainRamp) : "")
                + (Scarp > 0 ? string.Format(c, ",scarp={0:0.##}", Scarp) : "") + (ScarpLakes > 0 ? ",scarplakes=" + ScarpLakes : "")
                + (Beaches < 1 ? string.Format(c, ",beaches={0:0.##}", Beaches) : "") + (CragFloor != 3 ? string.Format(c, ",cragfloor={0:0}", CragFloor) : "")
                + (Valleys > 0 ? string.Format(c, ",valleys={0},vwidth={1:0},vside={2:0},vstrength={3:0.##}", Valleys, ValleyWidth, ValleySide, ValleyStrength) + string.Format(c, ",vfloor={0:0.##}", ValleyFloorLevel) : "")
                + (Fork ? ",fork=1" : "") + (RiverIsland ? ",island=1" : "");
        }

        public GenSettings Clone() { return (GenSettings)MemberwiseClone(); } // arrays are only read, so sharing them is fine
    }

    /// <summary>
    /// Random terrain. Everything is a function of position in metres, so a 384² preview and the full-size map have
    /// the same hills, rivers and coast; the full map only adds fine detail and erosion.
    /// </summary>
    internal static class TerrainGen
    {
        public const float CoarseCell = 8f; // metres; rivers and roads are planned on this grid

        /// <summary>Offline tools only: called with the output grid after each shaping stage (null in the plugin).</summary>
        public delegate void StageFn(string stage, Grid h);
        public static StageFn DebugStage = null;

        public sealed class Result
        {
            public Grid Heights;          // metres, size W x W, cell = WorldSize / (W - 1)
            public float Cell;
            public Grid Coarse;           // the same terrain on the 8 m planning grid
            public List<List<float[]>> Rivers = new List<List<float[]>>(); // polylines in metres
            public List<float[]> Ponds = new List<float[]>();                // x, z, radius (m), water level
            public float[] TownHint;      // metres: where the town belongs (a river fork's point, the river island); null = anywhere
        }

        // ---------- gradient noise ----------

        internal sealed class Perlin
        {
            private readonly int[] p = new int[512];
            public Perlin(int seed)
            {
                Random r = new Random(seed);
                int[] a = new int[256];
                for (int i = 0; i < 256; i++) a[i] = i;
                for (int i = 255; i > 0; i--) { int j = r.Next(i + 1); int t = a[i]; a[i] = a[j]; a[j] = t; }
                for (int i = 0; i < 512; i++) p[i] = a[i & 255];
            }
            private static float Fade(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }
            private static float Grad(int h, float x, float y)
            {
                switch (h & 7)
                {
                    case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                    case 4: return x; case 5: return -x; case 6: return y; default: return -y;
                }
            }
            /// <summary>About -1..1.</summary>
            public float Noise(float x, float y)
            {
                int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
                float xf = x - xi, yf = y - yi;
                int X = xi & 255, Y = yi & 255;
                float u = Fade(xf), v = Fade(yf);
                int aa = p[p[X] + Y], ab = p[p[X] + Y + 1], ba = p[p[X + 1] + Y], bb = p[p[X + 1] + Y + 1];
                float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
                float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
                return Lerp(x1, x2, v) * 0.7f;
            }
            public float Fbm(float x, float y, int oct)
            {
                float s = 0, a = 1, n = 0;
                for (int o = 0; o < oct; o++) { s += Noise(x, y) * a; n += a; x = x * 2.03f + 17.1f; y = y * 2.03f + 9.7f; a *= 0.5f; }
                return s / n;
            }
            /// <summary>Ridged multifractal, 0..1: sharp crests like mountain ranges.</summary>
            public float Ridged(float x, float y, int oct)
            {
                float s = 0, a = 1, n = 0, w = 1;
                for (int o = 0; o < oct; o++)
                {
                    float r = 1 - Math.Abs(Noise(x, y) * 1.4f);
                    r = r * r * w;
                    w = Math.Max(0, Math.Min(1, r * 1.5f));
                    s += r * a; n += a;
                    x = x * 2.1f + 5.3f; y = y * 2.1f + 3.1f; a *= 0.5f;
                }
                return s / n;
            }
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        private static float Smooth(float e0, float e1, float x) { float t = Math.Max(0, Math.Min(1, (x - e0) / (e1 - e0))); return t * t * (3 - 2 * t); }

        // ---------- generation ----------

        /// <summary>Raw relief (0..~1, before sea level and rivers) at (x, y) metres; 'detail' adds the finest octaves.
        /// Roughness is always included so the preview shows it.</summary>
        private static float Relief(Perlin pn, GenSettings s, float worldSize, float x, float y, float coastAngle, bool detail)
        {
            // town site: the middle of the map is kept gentle (mountains, hills and bumps fade towards it)
            // neighbourhood pockets: the mountain's texture is levelled to its average instead, so a pocket in the
            // mountains is a raised terrace or saddle, not a hollow that fills with water
            float town = TownSite(s, worldSize, x, y), pocket = Pocket(s, worldSize, x, y), settled = Math.Max(town, pocket);
            float h, ridgedRaw, massLift = 0;
            if (s.Buttes > 0)
            {
                // staged recipe (#95). Macro: a gentle open floor (the coast term below adds the sea). Meso: separate
                // layered buttes (ButteField). Micro: light bumps; the height curve, Dissect and the full map's erosion
                // and gullies follow as for every world.
                float floorN = pn.Fbm(x / 1100f, y / 1100f, 3) * 0.07f + pn.Fbm(x / 260f + 40, y / 260f + 40, 3) * 0.012f * (1 - 0.6f * town);
                // badlands: parts of the floor (away from the town) are rough low ridges and gullies, the rest open plain
                // (EA's Lucky Palms: half its land is over 15 deg, but only 41% of it is in the buttes)
                float bad = Smooth(-0.15f, 0.1f, pn.Fbm(x / 650f + 1800, y / 650f + 1800, 3)) * (1 - town);
                float near;
                float butte = ButteField(pn, s, worldSize, x, y, out near);
                // (not round the buttes' feet: each butte stands on open floor, as EA's do, instead of joining the others)
                float badlands = bad * (1 - near) * (pn.Ridged(x / 170f + 1850, y / 170f + 1850, 2) * 0.16f + pn.Ridged(x / 70f + 1880, y / 70f + 1880, 2) * 0.04f);
                h = 0.35f + floorN + badlands + butte * 0.9f - LakeBasins(pn, s, worldSize, x, y);
                h += (pn.Fbm(x / 90f + 260, y / 90f + 260, 3) * 0.012f + pn.Fbm(x / 35f + 300, y / 35f + 300, detail ? 3 : 2) * 0.006f) * s.Roughness / 100f;
                ridgedRaw = 0.5f;
            }
            else
            {
            float cont = pn.Fbm(x / 1100f, y / 1100f, 4) * (1 - 0.4f * town);
            float hills = pn.Fbm(x / 230f + 40, y / 230f + 40, detail ? 5 : 3) * (1 - 0.6f * settled);
            float mtnMask;
            if (s.Massifs > 0)
            {
                // the world type's hill masses (#121): mountains inside them, hills mostly there too, open floor between
                float m = MassifField(pn, s, worldSize, x, y);
                mtnMask = m * (1 - 0.9f * town);
                hills *= MassifHills + (1 - MassifHills) * m;
                cont *= 0.6f;
                // the whole mass stands up from the floor, cut by narrow valleys every few hundred metres (EA's massifs are
                // uplands between valleys, so most of them stands over 15 m above the valley floors)
                float cut = pn.Ridged(x / MassifValley + 3400, y / MassifValley + 3400, 2);
                massLift = m * (MassifLift - MassifCut * cut) * (1 - 0.9f * town);
            }
            else
                mtnMask = Smooth(0.55f - s.Mountains / 160f, 0.85f - s.Mountains / 160f, pn.Fbm(x / 1400f + 90, y / 1400f + 90, 3) * 0.5f + 0.5f)
                          * (1 - 0.9f * town);
            // mountains: ridged noise rounded with smooth noise and fewer octaves (the tester's v0.38 test: spiky, unrealistic
            // peaks; EA's mountains, e.g. Sunset Valley's, are broad with smooth flanks)
            ridgedRaw = 0.65f * pn.Ridged(x / 480f + 200, y / 480f + 200, detail ? 4 : 3) + 0.35f * (pn.Fbm(x / 420f + 230, y / 420f + 230, 3) * 0.5f + 0.5f);
            if (s.Round > 0)
            {
                // rounded: broad smooth domes (two octaves, eased so tops are soft) instead of crests
                float dome = Smooth(-0.55f, 0.65f, pn.Fbm(x / 520f + 200, y / 520f + 200, 2));
                ridgedRaw = Lerp(ridgedRaw, dome, s.Round);
                hills = Lerp(hills, pn.Fbm(x / 300f + 40, y / 300f + 40, 2) * (1 - 0.6f * settled), 0.6f * s.Round);
            }
            float ridged = Lerp(ridgedRaw, 0.3f, 0.85f * pocket);
            h = 0.35f + cont * 0.45f + massLift
                    + hills * (0.05f + 0.35f * s.Hills / 100f)
                    + ridged * mtnMask * (0.1f + 0.9f * s.Mountains / 100f);
            // roughness: bumps at 90 m and 35 m, stronger in the mountains (flat zones are calmed later in ToMetres)
            float rough = s.Roughness / 100f * (0.6f + 0.8f * mtnMask) * (1 - 0.6f * settled) * (1 - 0.6f * s.Round);
            // (EA's land is smooth at these scales outside its mountains: bumps under 25 m 2-7 m RMS, tools/Fidelity rel_fine_m)
            h += (pn.Fbm(x / 90f + 260, y / 90f + 260, 3) * 0.045f + pn.Fbm(x / 35f + 300, y / 35f + 300, detail ? 3 : 2) * 0.018f) * rough;
            }

            // coast shape
            float u = x / worldSize - 0.5f, v = y / worldSize - 0.5f;
            switch (s.Coast)
            {
                case CoastType.OceanSide:
                    float d = u * (float)Math.Cos(coastAngle) + v * (float)Math.Sin(coastAngle); // -0.7..0.7, + = inland
                    if (s.Buttes > 0) // the floor stays level; only the sea edge dips, along a ragged shore (EA's Lucky Palms: 7.6x a smooth line)
                        h += Math.Min(0, d + 0.3f + pn.Fbm(x / 260f + 500, y / 260f + 500, 3) * 0.08f + pn.Fbm(x / 70f + 540, y / 70f + 540, 2) * 0.02f) * 2.2f;
                    else h += (d + 0.1f) * 0.9f + pn.Fbm(x / 300f + 500, y / 300f + 500, 3) * 0.12f;
                    break;
                case CoastType.Island:
                    float r = (float)Math.Sqrt(u * u + v * v) + pn.Fbm(x / 350f + 600, y / 350f + 600, 3) * 0.12f;
                    h += 0.45f - r * 1.6f;
                    break;
                case CoastType.Islands: // an archipelago (Isla Paradiso, Sunlit Tides): several islands, sea all round the edge
                    float ri = (float)Math.Sqrt(u * u + v * v);
                    h += 0.2f + pn.Fbm(x / 520f + 700, y / 520f + 700, 4) * 1.2f - ri * 1.1f;
                    break;
                case CoastType.Iceland: // Aurora Skies: one big island, a rugged highland cut by fjords on one side, flat
                                        // lowland with small lakes on the other, sea all round
                {
                    float ca = (float)Math.Cos(coastAngle), sa = (float)Math.Sin(coastAngle);
                    float rI = (float)Math.Sqrt(u * u + v * v) + pn.Fbm(x / 350f + 600, y / 350f + 600, 3) * 0.10f;
                    float rug = Smooth(-0.12f, 0.12f, IcelandEdge(pn, u, v, x, y, coastAngle)); // ~60% highland (the escarpment: ApplyScarp)
                    // highland: broad ridges plus crisp 160 m ridges everywhere (EA's Aurora Skies is craggy all over its
                    // highland, not a few smooth hills); lowland: flat with low hummocks
                    float crest = pn.Ridged(x / 220f + 1500, y / 220f + 1500, 4);
                    // (#150, the tester: the lowland raised above the sea with natural bumps, the highland one joined mass with
                    // no spikes: a gentler crest, the lowland a raised plain that drops to the sea near the coast)
                    float coastDrop = Smooth(0.30f, 0.44f, rI);
                    h += 0.55f - rI * 0.5f - coastDrop * IcelandDrop + rug * (0.18f + 0.14f * ridgedRaw + IcelandCrest * crest)
                         + (1 - rug) * (IcelandLowland + pn.Fbm(x / 140f + 1550, y / 140f + 1550, 3) * 0.05f + pn.Fbm(x / 55f + 1560, y / 55f + 1560, 2) * 0.015f);
                    // fjords: a few wide zero lines of a warped noise, cut only from the coast partway in, so the highland
                    // behind them stays joined
                    float wx = pn.Fbm(x / 500f + 880, y / 500f + 880, 2) * 300f;
                    float fn = pn.Fbm((x + wx) / 1100f + 900, (y - wx) / 1100f + 900, 3);
                    float fj = 1 - Math.Min(1, Math.Abs(fn) / 0.045f);
                    h -= fj * fj * rug * Smooth(0.24f, 0.40f, rI) * 0.5f;
                    // lowland lakes
                    h -= (1 - rug) * Smooth(0f, 0.25f, 0.42f - rI) * Math.Max(0, pn.Fbm(x / 160f + 950, y / 160f + 950, 2) - 0.32f) * 0.9f;
                    break;
                }
                case CoastType.Volcano: // Sunlit Tides: islands of different sizes round a lagoon, the biggest a volcano
                {
                    // #122 (the tester: a volcanic archipelago, not a ring): EA's land is one main volcanic island, hilly lobes,
                    // a hooked peninsula round a second lagoon, thin strips and sandbars; land width along its centre line
                    // 11/51/189 m (p10/50/90), ours was a uniform ring 40/227/312 m. Each seed draws its own islands.
                    float wu = u + pn.Fbm(x / 420f + 1810, y / 420f + 1810, 3) * 0.04f + pn.Fbm(x / 160f + 1830, y / 160f + 1830, 3) * 0.022f;
                    float wv = v + pn.Fbm(x / 420f + 1820, y / 420f + 1820, 3) * 0.04f + pn.Fbm(x / 160f + 1840, y / 160f + 1840, 3) * 0.022f;
                    float lobeLand, lowLand, lobeHill, volcanoAt;
                    float edgeNoise = pn.Fbm(x / 190f + 1660, y / 190f + 1660, 3) + 0.5f * pn.Fbm(x / 70f + 1670, y / 70f + 1670, 2);
                    Archipelago(s.Seed, coastAngle, wu, wv, pn.Fbm(x / 300f + 1650, y / 300f + 1650, 2), edgeNoise, out lobeLand, out lowLand, out lobeHill, out volcanoAt);
                    // the volcano: a steep, rough cone (ridges and gullies down its flanks) with a crater, on the biggest island
                    float rc = volcanoAt;
                    float cx = rc * (float)Math.Cos(coastAngle), cy = rc * (float)Math.Sin(coastAngle);
                    float dV = (float)Math.Sqrt(Sq(u - cx) + Sq(v - cy));
                    float cone = (float)Math.Exp(-Sq(dV / 0.07f));
                    float flank = pn.Ridged(x / 90f + 1600, y / 90f + 1600, 4);
                    float crater = (float)Math.Exp(-Sq(dV / 0.013f));
                    float hillNoise = 0.6f + 0.4f * pn.Fbm(x / 160f + 1700, y / 160f + 1700, 3);
                    // sea floors well below the land, so the water calibration can't lift the lagoon floor out of the water;
                    // low land (strips, spits, sandbars) just above the water, lobes hilly
                    // hills follow the base terrain's own noise inside each island, not a dome
                    lowLand *= 1 - lobeLand; // strips stop at the islands' shores (no flat skirt round them)
                    float landH = -0.25f * (1 - Math.Max(lobeLand, lowLand)) + Math.Max(lobeLand * (0.8f + lobeHill * (0.12f + 0.45f * Math.Max(0, h - 0.3f)) * hillNoise), lowLand * 0.78f);
                    // the open sea deepens beyond the islands, so the water share is made from the islands' shores, not
                    // from sea-floor noise rising at the map edges
                    float far = Smooth(0.36f, 0.46f, (float)Math.Sqrt(u * u + v * v));
                    h = 0.1f * h - 0.95f - 0.5f * far + landH + cone * (1.5f + 0.3f * ridgedRaw + 0.8f * flank) - crater * 0.5f;
                    break;
                }
                case CoastType.Orleans: // Roaring Heights: low flat land almost to the map edge, cut into long strips by
                                        // channels, with round harbours
                {
                    // strips run along a map axis, as Roaring Heights' do
                    float oa = (float)(Math.Round(coastAngle / (Math.PI / 2)) * (Math.PI / 2));
                    float ca = (float)Math.Cos(oa), sa = (float)Math.Sin(oa);
                    // outline: between a circle and a square, warped by broad and fine noise, so the land is an irregular
                    // mass with bays and headlands, not a square (the tester: "basically a square")
                    // shifted off-centre so the land runs off one side of the map and the sea opens on the other, as EA's does
                    float uo = u + 0.22f * (float)Math.Cos(coastAngle), vo = v + 0.22f * (float)Math.Sin(coastAngle);
                    float rO = (float)Math.Sqrt(uo * uo + vo * vo) * 0.6f + Math.Max(Math.Abs(uo), Math.Abs(vo)) * 0.4f
                               + pn.Fbm(x / 520f + 600, y / 520f + 600, 3) * 0.13f + pn.Fbm(x / 170f + 640, y / 170f + 640, 2) * 0.035f;
                    // channels: winding lines (zero lines of a stretched noise, its coordinates warped) running roughly
                    // along a map axis, as Roaring Heights' channels between its islands do
                    float wx = pn.Fbm(x / 600f + 810, y / 600f + 810, 2) * 260f, wy = pn.Fbm(x / 600f + 830, y / 600f + 830, 2) * 260f;
                    float xw = x + wx, yw = y + wy;
                    float along = xw * ca + yw * sa, across = -xw * sa + yw * ca;
                    float cn = pn.Fbm(along / 1400f + 820, across / 520f + 820, 3);
                    float ch = 1 - Math.Min(1, Math.Abs(cn) / 0.07f);
                    // parallel channels (#153, the tester: long islands side by side, as EA's): offset copies of one gently
                    // winding line every OrleansSpacing metres, each present over long stretches only, so long narrow
                    // islands form between them
                    if (OrleansSpacing > 0)
                    {
                        float aw = across + pn.Fbm(along / 900f + 870, 2.3f, 2) * OrleansSpacing * 0.5f;
                        float kq = (float)Math.Round(aw / OrleansSpacing);
                        float off = (aw - kq * OrleansSpacing);
                        float half = OrleansChannel * (0.75f + 0.5f * (pn.Fbm(along / 300f + kq * 13 + 880, 4.1f, 2) + 0.5f));
                        float gate = Smooth(-0.05f, 0.12f, pn.Fbm(along / 1100f + kq * 7.3f + 890, kq * 3.7f + 5, 2) + OrleansGate);
                        ch = Math.Max(ch, (1 - Smooth(half * 0.6f, half, Math.Abs(off))) * gate);
                    }
                    // inland lakes and bays, and islets off the shore
                    float harbour = Math.Max(0, pn.Fbm(x / 230f + 840, y / 230f + 840, 2) - 0.33f) * 1.8f;
                    float islets = Math.Max(0, pn.Fbm(x / 120f + 860, y / 120f + 860, 2) - 0.2f) * 1.4f * Smooth(0.40f, 0.5f, rO) * (1 - Smooth(0.6f, 0.7f, rO));
                    h = 0.12f * h + 0.35f - Smooth(0.38f, 0.52f, rO) * 0.9f - ch * 0.6f - harbour + islets * 0.6f; // flat-topped land with steep banks
                    break;
                }
                case CoastType.Lake:
                    float rl = (float)Math.Sqrt(u * u + v * v) + pn.Fbm(x / 250f + 700, y / 250f + 700, 3) * 0.1f;
                    h -= Math.Max(0, 0.22f - rl) * 2.5f;
                    break;
            }
            if (s.Valleys > 0 && s.Coast == CoastType.OceanSide) h = ValleyLand(pn, s, worldSize, x, y, coastAngle, h);
            return h;
        }

        private const float ValleyPlateau = 1.0f;

        /// <summary>
        /// Valley recipe (#151 Dragon Valley, #152 Midnight Hollow): valleys run out from the town centre as rays (the first
        /// from the coast through the town to the far edge), each a flat floor ValleyWidth across that rises gently inland,
        /// sides over ValleySide metres up to a slightly hilly plateau. Near the coast it fades so the valley opens onto the
        /// shore. Relief units; heights scaled straight (nocurve) so floor and plateau stay level.
        /// </summary>
        private static float ValleyLand(Perlin pn, GenSettings s, float worldSize, float x, float y, float coastAngle, float h)
        {
            float ca = (float)Math.Cos(coastAngle), sa = (float)Math.Sin(coastAngle);
            float u = x / worldSize - 0.5f, v = y / worldSize - 0.5f;
            float inland = u * ca + v * sa; // + = inland (as OceanSide)
            float[] town = TownCentre(s, worldSize);
            float tx = town[0], ty = town[1];
            double baseA = Math.Atan2(sa, ca); // inland
            Random r = new Random(s.Seed * 9133 + 5);
            float shape = 1;
            for (int k = 0; k < s.Valleys; k++)
            {
                // the first runs straight inland (and back out to the sea); the others branch off at 55-110 degrees
                double a = k == 0 ? baseA : baseA + (k % 2 == 1 ? 1 : -1) * (55 + 55 * r.NextDouble()) * Math.PI / 180;
                float ax = (float)Math.Cos(a), ay = (float)Math.Sin(a);
                float px = x - tx, py = y - ty;
                float along = px * ax + py * ay, across = -px * ay + py * ax;
                if (k > 0 && along < 0) continue; // branches start at the town
                float wid = s.ValleyWidth * (k == 0 ? 1 : 0.7f) * (0.85f + 0.3f * (pn.Fbm(along / 500f + 70 * k + 5000, 3.1f, 2) + 0.5f));
                float wave = 900f + 300f * k, amp = s.ValleyWidth * 0.8f;
                float off = amp * (float)Math.Sin(along / wave * 6.2832f + r.NextDouble() * 6.28) + pn.Fbm(along / 300f + 40 * k + 5100, 1.7f, 2) * amp * 0.6f;
                float dist = Math.Abs(across - off * Smooth(0, 200, Math.Abs(along)));
                float sh = Smooth(wid * 0.5f, wid * 0.5f + s.ValleySide, dist + pn.Fbm(x / 90f + 5200, y / 90f + 5200, 2) * 25f);
                shape = Math.Min(shape, sh);
            }
            float floorH = s.ValleyFloorLevel + 0.25f * Math.Max(0, inland) + pn.Fbm(x / 160f + 5300, y / 160f + 5300, 2) * 0.03f;
            float plat = ValleyPlateau + 0.25f * Math.Max(0, inland) + pn.Fbm(x / 240f + 5400, y / 240f + 5400, 3) * 0.10f
                         + pn.Fbm(x / 80f + 5450, y / 80f + 5450, 2) * 0.025f * s.Roughness / 50f;
            float target = floorH + (plat - floorH) * shape;
            float w = Smooth(-0.12f, 0.04f, inland - 0.04f * pn.Fbm(x / 300f + 5500, y / 300f + 5500, 2)); // the shore keeps its own shape
            return Lerp(h, target, w * s.ValleyStrength);
        }

        /// <summary>
        /// Generate with the terrain checks: tries up to 'tries' variations of the seed (seed + k x 7919) on the planning
        /// grid, measures each as EA's worlds were measured and keeps the one passing most checks against 'target' (the
        /// world type's library/&lt;key&gt;_terrain.json), then generates it at 'size'. s.Seed is left at the variation kept.
        /// water: the water share to aim for. Without a target this is plain Generate.
        /// </summary>
        public static Result GenerateChecked(GenSettings s, int size, float worldSize, bool full, ProgressFn progress,
                                             TerrainProfile target, float water, int tries, out TerrainChecks.Result checks)
        {
            return GenerateChecked(s, size, worldSize, full, progress, target, water, tries, null, out checks);
        }

        /// <summary>As above; the variations are tried in parallel, and 'stop' abandons them (result null).</summary>
        public static Result GenerateChecked(GenSettings s, int size, float worldSize, bool full, ProgressFn progress,
                                             TerrainProfile target, float water, int tries, Work.Stop stop, out TerrainChecks.Result checks)
        {
            checks = null;
            if (target != null && tries > 0)
            {
                int baseSeed = s.Seed;
                TerrainChecks.Result[] found = new TerrainChecks.Result[tries];
                Work.For(tries, delegate (int k)
                {
                    GenSettings v = s.Clone(); v.Seed = baseSeed + k * 7919;
                    Result c = Generate(v, 0, worldSize, false, null);
                    List<float[]> ponds = new List<float[]>();
                    foreach (float[] q in c.Ponds) ponds.Add(new[] { q[3], q[0] - q[2], q[1] - q[2], q[0] + q[2], q[1] + q[2], (float)Math.PI * q[2] * q[2] });
                    TerrainProfile p = new TerrainProfile();
                    TerrainProfile.Fill(p, c.Coarse, CoarseCell, v.SeaLevel, ponds);
                    TerrainChecks.Result r = TerrainChecks.Check(p, target, water);
                    r.Seed = v.Seed;
                    found[k] = r;
                }, stop);
                if (stop != null && stop.Requested) return null;
                foreach (TerrainChecks.Result r in found) // the most checks passed, then the closest; ties: the lowest variation
                    if (r != null && (checks == null || r.Passed > checks.Passed || (r.Passed == checks.Passed && r.Score > checks.Score))) checks = r;
                if (checks != null) s.Seed = checks.Seed;
            }
            return Generate(s, size, worldSize, full, progress);
        }

        public static Result Generate(GenSettings s, int size, float worldSize, bool full, ProgressFn progress)
        {
            Perlin pn = new Perlin(s.Seed);
            Random rng = new Random(s.Seed * 7919 + 13);
            float coastAngle = CoastAngle(s, rng);
            Result res = new Result();

            // 1. Planning grid: relief, flat zones, sea level, rivers (identical for preview and full map).
            int cw = (int)(worldSize / CoarseCell) + 1;
            Grid coarse = new Grid(cw, cw);
            for (int j = 0; j < cw; j++)
                for (int i = 0; i < cw; i++)
                    coarse[i, j] = Relief(pn, s, worldSize, i * CoarseCell, j * CoarseCell, coastAngle, false);
            if (progress != null) progress("Shaping terrain", 2);
            bool split = s.SeaShare >= 0 && s.Coast != CoastType.None && s.Coast != CoastType.Lake;
            float want = split ? s.SeaShare / 100f : s.Coast == CoastType.None ? Math.Min(s.Water, 5) / 100f : s.Water / 100f;
            float top = Percentile(coarse, 0.995f);
            Grid flat = FlatMask(pn, s, worldSize, cw, CoarseCell);
            // the sea cut-off: the Water share of the relief, then corrected so the finished map really has that much
            // water (the dry town site and the height curve raise some of it)
            // (with the sea alone as the target, much of the low ground is inland hollows, so the search reaches further)
            float qLo = 0, qHi = Math.Min(0.97f, split ? want * 4 + 0.2f : want * 2 + 0.05f), q = want;
            float seaCut = 0; float[] landRanks = null;
            int its = split ? 10 : 7;
            for (int it = 0; it < its; it++)
            {
                seaCut = Percentile(coarse, q);
                landRanks = LandRanks(coarse, flat, s, seaCut);
                if (want <= 0 || it == its - 1) break;
                Grid trial = new Grid(cw, cw); Array.Copy(coarse.D, trial.D, coarse.D.Length);
                ToMetres(trial, flat, s, seaCut, top, CoarseCell, worldSize, landRanks);
                SmoothCoast(trial, CoarseCell, s.SeaLevel);
                int wet = 0;
                if (split) { foreach (bool o in EdgeWater(trial, s.SeaLevel)) if (o) wet++; }
                else foreach (float v in trial.D) if (v < s.SeaLevel) wet++;
                float got = (float)wet / trial.D.Length;
                if (Math.Abs(got - want) < 0.005f) break;
                if (got < want) qLo = q; else qHi = q;
                q = (qLo + qHi) / 2;
            }
            if (progress != null) progress("Shaping terrain", 6);
            Grid coarseSmooth = coarse.Blur(60f / CoarseCell); // the broad shape, reused by the output grid
            ToMetres(coarse, flat, s, seaCut, top, CoarseCell, worldSize, landRanks, coarseSmooth);
            if (s.PlainLift > 0) LiftPlain(coarse, CoarseCell, s, pn);
            if (s.Scarp > 0 && s.Coast == CoastType.Iceland) ApplyScarp(coarse, CoarseCell, s, pn, worldSize, coastAngle);
            // drainage valleys, worked out on the planning grid and applied to every grid the same way
            Grid spikes = s.Despike > 0 ? DespikeCut(coarse, s) : null;
            float keepBase = s.SeaLevel + s.PlainLift, keepGain = 1;
            if (spikes != null)
            {
                float top0 = LandPercentile(coarse, keepBase, 0.995f);
                for (int i = 0; i < coarse.D.Length; i++) coarse.D[i] -= spikes.D[i];
                if (s.KeepTop > 0)
                {
                    float top1 = LandPercentile(coarse, keepBase, 0.995f);
                    if (top1 > keepBase + 1) keepGain = Math.Max(1, Math.Min(2.5f, 1 + s.KeepTop * ((top0 - keepBase) / (top1 - keepBase) - 1)));
                    KeepHeight(coarse, keepBase, keepGain);
                }
            }
            Grid valleys = ValleyCut(coarse, flat, s);
            if (progress != null) progress("Shaping terrain", 10);
            for (int i = 0; i < coarse.D.Length; i++) coarse.D[i] -= valleys.D[i];
            Grid dissect = s.Crag > 0.5f ? Dissect(coarse, s, worldSize, pn) : null;
            if (dissect != null)
            {
                Grid soft = dissect.Blur(1f); // onto the 8 m grid without aliasing
                float k = CoarseCell / 4f;
                for (int j = 0; j < cw; j++)
                    for (int i = 0; i < cw; i++) coarse[i, j] += soft.At(i * k, j * k);
            }
            SmoothCoast(coarse, CoarseCell, s.SeaLevel);
            ShapeCoast(coarse, CoarseCell, s, pn);
            Grid dry = split && s.LakeShare >= 0 ? FillInland(coarse, s.SeaLevel, s.LakeShare / 100f)
                     : s.InlandLakes || s.Coast == CoastType.Lake ? null : FillInland(coarse, s.SeaLevel, 0);
            if (progress != null) progress("Shaping terrain", 13);
            if (s.Rivers > 0) res.Rivers = CarveRivers(coarse, s, rng, worldSize, pn, out res.TownHint);
            if (progress != null) progress("Shaping terrain", 17);
            if (s.Ponds > 0 || s.ScarpLakes > 0) res.Ponds = PlacePonds(coarse, s, worldSize, new Random(s.Seed * 6311 + 17), pn, coastAngle);
            if (s.Valleys > 0 && res.TownHint == null) res.TownHint = TownCentre(s, worldSize); // the town on the valley floor
            if (s.TownLake && res.TownHint == null && res.Ponds.Count > 0)
            {
                // the town lake was placed first, beside the town centre: the town is planned round it
                float[] tc = TownCentre(s, worldSize);
                if (Sq(res.Ponds[0][0] - tc[0]) + Sq(res.Ponds[0][1] - tc[1]) < Sq(0.12f * worldSize)) res.TownHint = tc;
            }
            foreach (float[] pond in res.Ponds) DigPond(coarse, CoarseCell, pond);
            foreach (float[] pond in res.Ponds) FitPond(coarse, CoarseCell, pond);
            res.Coarse = coarse;
            if (progress != null) progress("Shaping terrain", 20);
            if (size <= 0) return res; // planning grid only (GenerateChecked's candidates)

            // 2. Output grid.
            float cell = worldSize / (size - 1);
            Grid h = new Grid(size, size);
            // the flat-land mask and the broad shape are smooth (700 m, 60 m): taken from the planning grid
            // (the flat mask is softened ~32 m first: its edge switched within a few metres, which showed as a crease along
            // every flat zone, and as an 8 m staircase once interpolated)
            Grid flatOut = Resample(flat.Blur(32f / CoarseCell), size, cell), smoothOut = Resample(coarseSmooth, size, cell);
            // the relief noise's finest layer is 35 m: evaluated every ~2 m and interpolated (4x fewer noise calls on a
            // 1 m map; the tester's v0.40 test: Apply terrain took 2 minutes in CAW)
            int step = Math.Max(1, (int)(2f / cell));
            if (step > 1)
            {
                int m = (size - 1) / step + 2;
                Grid rel = new Grid(m, m);
                for (int j = 0; j < m; j++)
                {
                    for (int i = 0; i < m; i++)
                        rel[i, j] = Relief(pn, s, worldSize, i * step * cell, j * step * cell, coastAngle, full);
                    if (progress != null && (j & 15) == 0) progress("Shaping terrain", 20 + 50 * j / m);
                }
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                        h[i, j] = SampleCubic(rel, (float)i / step, (float)j / step);
            }
            else
                for (int j = 0; j < size; j++)
                {
                    for (int i = 0; i < size; i++)
                        h[i, j] = Relief(pn, s, worldSize, i * cell, j * cell, coastAngle, full);
                    if (progress != null && (j & 63) == 0) progress("Shaping terrain", 20 + 50 * j / size);
                }
            if (DebugStage != null) DebugStage("relief", h);
            ToMetres(h, flatOut, s, seaCut, top, cell, worldSize, landRanks, smoothOut);
            if (s.PlainLift > 0) LiftPlain(h, cell, s, pn);
            if (s.Scarp > 0 && s.Coast == CoastType.Iceland) ApplyScarp(h, cell, s, pn, worldSize, coastAngle);
            if (DebugStage != null) DebugStage("metres", h);
            // (softened a little first: the valley widening meets in sharp creases on the 8 m grid, which interpolated
            // onto a fine grid showed as an 8 m staircase along ridges)
            if (spikes != null)
            {
                Grid spikesOut = spikes.Blur(0.8f);
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                        h[i, j] -= SampleCubic(spikesOut, i * cell / CoarseCell, j * cell / CoarseCell);
                KeepHeight(h, keepBase, keepGain);
            }
            Grid valleysOut = valleys.Blur(0.8f);
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                    h[i, j] -= SampleCubic(valleysOut, i * cell / CoarseCell, j * cell / CoarseCell);
            if (dissect != null)
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                        h[i, j] += SampleCubic(dissect, i * cell / 4f, j * cell / 4f);
            if (DebugStage != null) DebugStage("valleys", h);
            SmoothCoast(h, cell, s.SeaLevel);
            if (DebugStage != null) DebugStage("smoothcoast", h);
            ShapeCoast(h, cell, s, pn);
            if (DebugStage != null) DebugStage("shapecoast", h);
            if (dry != null)
                for (int j = 0; j < size; j++)
                    for (int i = 0; i < size; i++)
                    {
                        float lift = TerrainGen.Sample(dry, i * cell / CoarseCell, j * cell / CoarseCell);
                        if (lift > 0.01f) h[i, j] = Math.Max(h[i, j], s.SeaLevel + 0.6f);
                    }
            // rivers: carve the same channels at this resolution
            foreach (List<float[]> river in res.Rivers) CarveChannel(h, cell, river, s, worldSize, pn);
            foreach (float[] pond in res.Ponds) DigPond(h, cell, pond);
            if (DebugStage != null) DebugStage("ponds", h);
            if (full)
            {
                if (progress != null) progress("Erosion", 75);
                ThermalErosion(h, cell, s.Talus, 4, s.Scarp > 0 && s.Coast == CoastType.Iceland ? ScarpFace(h, cell, s, pn, worldSize, coastAngle) : null);
            if (DebugStage != null) DebugStage("thermal", h);
                if (progress != null) progress("Gullies", 85);
                Gullies(h, cell, s);
            if (DebugStage != null) DebugStage("gullies", h);
            }
            FillPuddles(h, cell, s.SeaLevel, 1000f);
            if (full) { RemoveSlivers(h, cell, s.SeaLevel, 8f); LiftLowland(h, cell, s.SeaLevel); }
            if (full) { if (progress != null) progress("Rises and mounds", 92); TownBumps(h, cell, s, flatOut, pn); }
            if (DebugStage != null) DebugStage("bumps", h);
            foreach (float[] pond in res.Ponds) FitPond(h, cell, pond);
            res.Heights = h; res.Cell = cell;
            if (progress != null) progress("Terrain ready", 100);
            return res;
        }

        /// <summary>1 in the middle of the map (within ~12% of its size), fading out by ~30%; 0 when the town isn't central.</summary>
        private static float TownSite(GenSettings s, float worldSize, float x, float y)
        {
            // (a hilly archipelago keeps its own islands: a town site levelled and kept dry there became one flat plain a
            // metre above the sea, as big as the rest of the land (Isla Paradiso); the road planner finds the flattest
            // island instead. Low island worlds do build their town on a flat island: Roaring Heights, land under 30 m.)
            if (s.Coast == CoastType.Islands && s.MaxHeight > 60 || s.Coast == CoastType.Volcano) return 0;
            double[] t = TownLayout(s);
            float du = x / worldSize - 0.5f - (float)t[0], dv = y / worldSize - 0.5f - (float)t[1];
            float dist = (float)Math.Sqrt(du * du + dv * dv);
            if (dist > 0.4f * Math.Max(1, s.TownSize)) return 0;
            // irregular outline (the tester's v0.38 test: the town always sat on a flat circle in the middle of the map)
            double th = Math.Atan2(dv, du);
            dist *= (float)(1 + 0.25 * Math.Sin(2 * th + t[2]) + 0.15 * Math.Sin(3 * th + t[3]) + 0.08 * Math.Sin(5 * th + t[4]));
            return s.TownFlat * (1 - Smooth((float)t[5] * s.TownSize, (float)t[5] * s.TownSize + 0.16f, dist));
        }

        /// <summary>Sea on every edge (an island, archipelago, Iceland, atoll or Orleans-style delta).</summary>
        public static bool IsIslandCoast(CoastType c)
        {
            return c == CoastType.Island || c == CoastType.Islands || c == CoastType.Iceland || c == CoastType.Volcano || c == CoastType.Orleans;
        }

        /// <summary>A number 0..1 from the seed and 'k' (the same on every call, for per-seed shape choices made per pixel).</summary>
        private static float SeedHash(int seed, int k)
        {
            uint h = (uint)seed * 2654435761u ^ (uint)k * 2246822519u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>
        /// The atoll ring's radius (map fractions) in direction 'th': about 0.27-0.39, swelling and narrowing with 2-, 3- and
        /// 5-lobed waves of seeded size and phase, so each seed's ring is a different lopsided loop.
        /// </summary>
        public static float AtollRadius(int seed, float th)
        {
            // EA's ring spans most of the map (0.33 x 0.41 of it from the middle): larger and more elongated than a circle
            float r0 = 0.335f + 0.02f * (2 * SeedHash(seed, 3) - 1);
            float a2 = 0.06f + 0.08f * SeedHash(seed, 4), a3 = 0.02f + 0.04f * SeedHash(seed, 6), a5 = 0.02f * SeedHash(seed, 8);
            float r = r0 * (1 + a2 * (float)Math.Cos(2 * th + 6.2832f * SeedHash(seed, 5)) + a3 * (float)Math.Cos(3 * th + 6.2832f * SeedHash(seed, 7))
                         + a5 * (float)Math.Cos(5 * th + 6.2832f * SeedHash(seed, 9)));
            return Math.Min(0.39f, r); // clear of the map edge
        }

        /// <summary>
        /// Sunlit Tides' islands (#122) at (u, v) from the map's middle (already warped): lobeLand 0..1 for the hilly islands
        /// (the volcano's is the biggest), lowLand 0..1 for the low strips joining some of them, a hooked spit round a
        /// small lagoon and thin sandbars outside; lobeHill 0..1 rises to each island's middle. Per seed: 4-6 islands of
        /// different sizes round a lopsided ring (0.86 x AtollRadius), strips between some, 2-4 open channels, one hook,
        /// a pond in a large island, 2-5 sandbars. volcanoAt = the volcano's distance from the middle.
        /// </summary>
        private const float Ring = 0.78f; // EA's land: median 556 m from its middle (0.27 of the map)
        public static void Archipelago(int seed, float coastAngle, float u, float v, float wideNoise, float edgeNoise, out float lobeLand, out float lowLand, out float lobeHill, out float volcanoAt)
        {
            float th = (float)Math.Atan2(v, u), r = (float)Math.Sqrt(u * u + v * v);
            float R = Ring * AtollRadius(seed, th);
            int n = 4 + (int)(SeedHash(seed, 50) * 2.99f);
            lobeLand = 0; lowLand = 0; lobeHill = 0;
            volcanoAt = Ring * AtollRadius(seed, coastAngle);
            float[] ang = new float[n], rad = new float[n];
            // channels: 2-3 narrow cuts (bridgeable, 25-50 m) at the middle of spans between islands, through the strips
            // and any island edge there, so the lagoon always reaches the sea (an enclosed lagoon would be filled as an
            // inland lake); never beside the volcano
            float channel = 1;
            int nch = 2 + (int)(SeedHash(seed, 90) * 1.99f);
            for (int c = 0; c < nch; c++)
            {
                int sp = 1 + (int)(SeedHash(seed, 91 + c) * (n - 2) * 0.999f);
                float at = coastAngle + 6.2832f * (sp + 0.5f) / n;
                float da = (float)Math.Abs(Math.IEEERemainder(th - at, 6.2832f)) * Math.Max(r, 0.05f);
                float half = 0.006f + 0.006f * SeedHash(seed, 95 + c);
                channel *= Math.Max(Smooth(half, half + 0.004f, da), Smooth(0.12f, 0.16f, Math.Abs(r - R)));
            }

            // island angles and sizes first: the volcano's island biggest (~290-350 m across), the others 120-250 m;
            // only the volcano's island and the biggest other one are hilly (EA: two hill masses, ~27 ha each), the
            // rest low
            int biggest = -1;
            for (int k = 0; k < n; k++)
            {
                ang[k] = coastAngle + 6.2832f * (k + (k == 0 ? 0 : 0.55f * (SeedHash(seed, 51 + k) - 0.5f))) / n;
                rad[k] = k == 0 ? 0.14f + 0.03f * SeedHash(seed, 59) : 0.06f + 0.065f * SeedHash(seed, 60 + k);
                if (k > 0 && (biggest < 0 || rad[k] > rad[biggest])) biggest = k;
            }
            for (int k = 0; k < n; k++)
            {
                float ca = Ring * AtollRadius(seed, ang[k]) + (k == 0 ? 0 : 0.025f * (2 * SeedHash(seed, 70 + k) - 1));
                float cxk = ca * (float)Math.Cos(ang[k]), cyk = ca * (float)Math.Sin(ang[k]);
                // an island is three overlapping blobs (a main one and two smaller ones off it), so it isn't round
                float dd = (float)Math.Sqrt(Sq(u - cxk) + Sq(v - cyk)) / rad[k];
                for (int b = 0; b < 2; b++)
                {
                    float ba = 6.2832f * SeedHash(seed, 150 + 3 * k + b), bo = rad[k] * (0.45f + 0.35f * SeedHash(seed, 151 + 3 * k + b));
                    float br = rad[k] * (0.45f + 0.3f * SeedHash(seed, 152 + 3 * k + b));
                    float db = (float)Math.Sqrt(Sq(u - cxk - bo * (float)Math.Cos(ba)) + Sq(v - cyk - bo * (float)Math.Sin(ba))) / br;
                    dd = Math.Min(dd, db);
                }
                // ragged shores: bays and points a third of the island's size
                dd *= 1 + 0.32f * edgeNoise;
                float m = 1 - Smooth(0.7f, 1.0f, dd);
                m *= channel;
                if (m > lobeLand) lobeLand = m;
                float hk = Smooth(0.95f, 0.2f, dd) * (k == 0 || k == biggest ? 1 : 0.2f);
                if (hk > lobeHill) lobeHill = hk;
            }
            if (biggest > 0 && rad[biggest] > 0.075f)
            {
                float ca = Ring * AtollRadius(seed, ang[biggest]) + 0.025f * (2 * SeedHash(seed, 70 + biggest) - 1);
                float px = ca * (float)Math.Cos(ang[biggest]) + 0.3f * rad[biggest] * (float)Math.Cos(ang[biggest] + 2.0f);
                float py = ca * (float)Math.Sin(ang[biggest]) + 0.3f * rad[biggest] * (float)Math.Sin(ang[biggest] + 2.0f);
                float dp = (float)Math.Sqrt(Sq(u - px) + Sq(v - py)) / (0.22f * rad[biggest]);
                float pond = 1 - Smooth(0.8f, 1.1f, dp);
                lobeLand *= 1 - pond; lobeHill *= 1 - pond;
            }
            // strips along the ring between neighbouring islands: some spans open sea (separate islands), the rest a thin
            // strip of land (10-50 m wide) cut by a channel or two
            float sw = 0.006f + 0.02f * Smooth(-0.35f, 0.45f, wideNoise);
            float strip = 1 - Smooth(sw, sw + 0.006f, Math.Abs(r - R));
            if (strip > 0)
            {
                // which span (between island k and k+1) this direction is in
                float rel = (float)Math.IEEERemainder(th - coastAngle, 6.2832f); if (rel < 0) rel += 6.2832f;
                int span = Math.Min(n - 1, (int)(rel / 6.2832f * n));
                bool open = span > 0 && span < n - 1 && SeedHash(seed, 80 + span) < 0.2f;   // never beside the volcano, either side (span n - 1 wraps round to it)
                if (open) strip = 0;
                lowLand = Math.Max(lowLand, strip * channel);
            }
            // a hooked peninsula closing round a lagoon of its own (EA's south-west one: ~8% of the map as lake water):
            // a loop of land off one low island; everything inside it is water
            {
                int hk = 1 + (int)(SeedHash(seed, 100) * (n - 1) * 0.999f);
                if (hk == biggest && n > 2) hk = 1 + (hk % (n - 1));
                float side = SeedHash(seed, 101) < 0.5f ? 1 : -1;
                float ah = ang[hk] + side * (0.3f + 0.2f * SeedHash(seed, 104));
                float rho = 0.075f + 0.035f * SeedHash(seed, 102);
                float ch = Ring * AtollRadius(seed, ah) + 0.35f * rho;
                float hx = ch * (float)Math.Cos(ah), hy = ch * (float)Math.Sin(ah);
                float dh = (float)Math.Sqrt(Sq(u - hx) + Sq(v - hy)) * (1 + 0.12f * edgeNoise);
                float w = 0.008f + 0.006f * SeedHash(seed, 103);
                // wider on the side away from the ring (a low island along the loop)
                float outward = Math.Max(0, ((u - hx) * (float)Math.Cos(ah) + (v - hy) * (float)Math.Sin(ah)) / Math.Max(dh, 1e-4f));
                float wl = w * (1 + 1.5f * outward);
                float loop = 1 - Smooth(wl, wl + 0.005f, Math.Abs(dh - rho));
                float inside = Smooth(rho - wl - 0.004f, rho - wl, dh); // 0 inside the loop
                lobeLand *= inside; lowLand *= inside; lobeHill *= inside;
                lowLand = Math.Max(lowLand, loop);
            }
            // thin sandbars outside the ring
            int nb = 2 + (int)(SeedHash(seed, 110) * 3.99f);
            for (int b = 0; b < nb; b++)
            {
                float at = 6.2832f * SeedHash(seed, 111 + b);
                float span2 = 0.12f + 0.2f * SeedHash(seed, 120 + b);
                float rb = Ring * AtollRadius(seed, at) + 0.08f + 0.05f * SeedHash(seed, 130 + b);
                float da = (float)Math.IEEERemainder(th - at, 6.2832f);
                float along = 1 - Smooth(span2 * 0.6f, span2, Math.Abs(da));
                float wb = (0.003f + 0.003f * SeedHash(seed, 140 + b)) * along;
                if (along > 0) lowLand = Math.Max(lowLand, (1 - Smooth(wb, wb + 0.004f, Math.Abs(r - rb))) * along * 0.97f);
            }
        }

        /// <summary>The sea's direction for a seed (shared by the terrain and the town site).</summary>
        private static float CoastAngle(GenSettings s, Random rng)
        {
            float a = (float)(rng.NextDouble() * Math.PI * 2);
            if (s.CoastCorner >= 0) // the same draw picks the side; the setting turns it towards a corner
                a = (float)(Math.Floor(a / (Math.PI / 2)) * (Math.PI / 2) + Math.Min(100, s.CoastCorner) / 100f * Math.PI / 4);
            return a;
        }

        private sealed class TownSet { public string Key; public double[] T; }
        private static TownSet townSet;

        /// <summary>
        /// The town site for a seed: centre (u, v from the map's middle), outline phases and core size. It varies per seed:
        /// up to a quarter of the map off-centre, inland of the sea side on a coast (on the shore of a central lake), less
        /// far out on islands; the core is 7-12% of the map across. Cached per settings.
        /// </summary>
        private static double[] TownLayout(GenSettings s)
        {
            string key = s.Seed + "/" + s.Coast + "/" + s.CoastCorner;
            TownSet cached = townSet;
            if (cached != null && cached.Key == key) return cached.T;
            Random r = new Random(s.Seed * 4271 + 3);
            double a = r.NextDouble() * Math.PI * 2, d = 0.06 + 0.17 * r.NextDouble();
            double u = Math.Cos(a) * d, v = Math.Sin(a) * d;
            if (s.Coast == CoastType.OceanSide)
            {
                // a little inland of the coast: shift along the inland direction so the town sits on the land side
                double ca = CoastAngle(s, new Random(s.Seed * 7919 + 13));
                double along = u * Math.Cos(ca) + v * Math.Sin(ca);
                // by the water, as EA's towns are (centre a median ~145 m from the sea, 50-430 m; library/*_districts.json):
                // the shore sits about where the sea's share of the map ends, the town 0.07-0.16 of the map inland of it
                double w = (s.SeaShare > 0 ? s.SeaShare : s.Water) / 100.0;
                double want = Math.Max(-0.32, -0.5 + w) + 0.07 + 0.09 * r.NextDouble();
                u += Math.Cos(ca) * (want - along); v += Math.Sin(ca) * (want - along);
            }
            else if (s.Coast == CoastType.Iceland)
            {
                // on the lowland side, away from the fjords (EA's Aurora Skies: the town on the flat south, the highland north)
                double ca = CoastAngle(s, new Random(s.Seed * 7919 + 13));
                double perp = (r.NextDouble() - 0.5) * 0.16;
                u = -Math.Cos(ca) * 0.15 - Math.Sin(ca) * perp; v = -Math.Sin(ca) * 0.15 + Math.Cos(ca) * perp;
            }
            else if (IsIslandCoast(s.Coast)) { u *= 0.5; v *= 0.5; }
            else if (s.Coast == CoastType.Lake) { u = Math.Cos(a) * 0.26; v = Math.Sin(a) * 0.26; } // on the lake shore
            double[] t = { u, v, r.NextDouble() * 6.3, r.NextDouble() * 6.3, r.NextDouble() * 6.3, 0.07 + 0.05 * r.NextDouble() };
            TownSet set = new TownSet(); set.Key = key; set.T = t; townSet = set;
            return t;
        }

        /// <summary>
        /// The town site plus gentler pockets for neighbourhoods round it (like villages on terraces and in valleys in
        /// hilly worlds): 0..1. Pockets sit 30-40% of the map from the middle, spread round it, 0.9 at their heart.
        /// Only the town site itself is kept above the sea; pockets just calm the ground.
        /// </summary>
        private static float Settled(GenSettings s, float worldSize, float x, float y)
        {
            return Math.Max(TownSite(s, worldSize, x, y), Pocket(s, worldSize, x, y));
        }

        /// <summary>The neighbourhood pockets alone (0..0.9).</summary>
        private static float Pocket(GenSettings s, float worldSize, float x, float y)
        {
            if (s.Pockets <= 0) return 0;
            double[] p = PocketLayout(s);
            float u = x / worldSize - 0.5f, v = y / worldSize - 0.5f, best = 0;
            for (int k = 0; k < p.Length; k += 4)
            {
                float du = u - (float)p[k] - (float)TownLayout(s)[0], dv = v - (float)p[k + 1] - (float)TownLayout(s)[1], dist = (float)Math.Sqrt(du * du + dv * dv);
                if (dist > 0.35f) continue; // beyond the edge even where the outline reaches furthest (0.23 / 0.66)
                // irregular outline, not a circle
                double th = Math.Atan2(dv, du);
                dist *= (float)(1 + 0.22 * Math.Sin(3 * th + p[k + 2]) + 0.12 * Math.Sin(5 * th + p[k + 3]));
                best = Math.Max(best, 0.9f * (1 - Smooth(0.09f, 0.23f, dist))); // wide, gentle edges
            }
            return best;
        }

        /// <summary>
        /// The buttes (0..1 height): each an irregular footprint (lobes, domain-warped outline), its outer 45% stepped
        /// into Tiers layers (narrow ledges, steep risers whose edges wander, the first at the foot), a low rounded top
        /// (EA's Lucky Palms buttes have no flat tops: the flattest tenth of its high ground slopes 12 deg or more).
        /// </summary>
        private static float ButteField(Perlin pn, GenSettings s, float worldSize, float x, float y, out float near)
        {
            double[] b = ButteLayout(s, worldSize);
            float best = 0;
            near = 0; // 1 within a butte's footprint, fading to 0 by 1.7 x its radius
            for (int k = 0; k < b.Length; k += 5)
            {
                float dx = x - (float)b[k], dy = y - (float)b[k + 1], r = (float)b[k + 2];
                float d2 = dx * dx + dy * dy;
                if (d2 > r * r * 3.2f) continue;
                near = Math.Max(near, 1 - Smooth(1.1f, 1.7f, (float)Math.Sqrt(d2) / r));
                float o = (float)b[k + 4];
                // an irregular outline: the point is pushed about by noise before measuring (domain warp), and the
                // radius swells and narrows round the lobe
                float wx = dx + pn.Fbm(x / 160f + o, y / 160f + o, 3) * 0.75f * r, wy = dy + pn.Fbm(x / 160f + o + 70, y / 160f + o + 70, 3) * 0.75f * r;
                float warp = 1 + 0.25f * pn.Fbm(x / 90f + o + 50, y / 90f + o + 50, 2);
                float dn = (float)Math.Sqrt(wx * wx + wy * wy) / (r * Math.Max(0.4f, warp));
                if (dn >= 1) continue;
                float t = 1 - dn, u = Math.Min(1, t / 0.45f);
                // the layers' edges wander a little round the butte
                // (+0.45: the first riser stands right at the foot, so the floor meets a steep side, as EA's buttes do)
                float f = u * s.Tiers + 0.45f + 0.3f * pn.Fbm(x / 110f + o + 90, y / 110f + o + 90, 2);
                int fl = (int)Math.Floor(f);
                float stair = u >= 1 ? 1 : Math.Max(0, Math.Min(1, (fl + Smooth(0.35f, 1f, f - fl)) / (s.Tiers + 0.45f)));
                float prof = 0.88f * stair + 0.12f * Smooth(0.45f, 1f, t);
                best = Math.Max(best, (float)b[k + 3] * prof);
            }
            return best;
        }

        /// <summary>
        /// Lake basins dug into a recipe world's floor (EA's Lucky Palms has three inland lakes, 3.2% of the map): up to
        /// three, together the world's lake share (LakeShare), on open floor away from the buttes and the town, with
        /// irregular shores. Depth in relief units, enough to go below the sea cut-off.
        /// </summary>
        private static float LakeBasins(Perlin pn, GenSettings s, float worldSize, float x, float y)
        {
            double[] b = ButteLayout(s, worldSize), lk = LakeLayout(s, worldSize, b);
            float dig = 0;
            for (int k = 0; k < lk.Length; k += 3)
            {
                float dx = x - (float)lk[k], dy = y - (float)lk[k + 1], r = (float)lk[k + 2];
                float d2 = dx * dx + dy * dy;
                if (d2 > r * r * 12) continue;
                float wobble = 1 + 0.35f * pn.Fbm(x / 90f + (float)k * 31 + 2400, y / 90f + 2400, 2);
                float dn = (float)Math.Sqrt(d2) / (r * wobble);
                // the lake, and a wide gentle dip round it (the floor slopes down to the shore, as round EA's lakes,
                // instead of standing 25 m over the water)
                dig = Math.Max(dig, 1.6f * (1 - Smooth(0.75f, 1.1f, dn)) + 0.12f * (1 - Smooth(1.1f, 3.2f, dn)));
            }
            return dig;
        }

        private static double[] LakeLayout(GenSettings s, float worldSize, double[] buttesB)
        {
            if (s.LakeShare <= 0) return new double[0];
            string key = s.Seed + "/" + s.LakeShare + "/" + worldSize + "/" + s.Buttes + "/" + s.ButteHa + "/" + s.Coast + "/" + s.CoastCorner;
            ButteSet cached = lakes;
            if (cached != null && cached.Key == key) return cached.B;
            Random r = new Random(s.Seed * 6197 + 29);
            double[] town = TownLayout(s);
            double tx = (0.5 + town[0]) * worldSize, ty = (0.5 + town[1]) * worldSize;
            int count = 3;
            double area = s.LakeShare / 100.0 * worldSize * worldSize / count;
            double rad = Math.Sqrt(area / Math.PI) * 1.4; // the water is the inner part of the basin
            System.Collections.Generic.List<double> list = new System.Collections.Generic.List<double>();
            for (int tries = 0; tries < 3000 && list.Count < count * 3; tries++)
            {
                double rr = rad * (0.7 + 0.6 * r.NextDouble());
                double cx = 0.1 * worldSize + r.NextDouble() * 0.8 * worldSize, cy = 0.1 * worldSize + r.NextDouble() * 0.8 * worldSize;
                if (Math.Sqrt(Sq((float)(cx - tx)) + Sq((float)(cy - ty))) < 0.12 * worldSize + rr) continue;
                if (s.Coast == CoastType.OceanSide) // inland, not joined to the sea
                {
                    double ca = CoastAngle(s, new Random(s.Seed * 7919 + 13));
                    if ((cx / worldSize - 0.5) * Math.Cos(ca) + (cy / worldSize - 0.5) * Math.Sin(ca) - rr / worldSize < -0.12) continue;
                }
                bool clash = false;
                double keep = tries < 1500 ? 1.1 : 0.75; // big buttes leave little room: then lakes may sit closer to their feet
                for (int k = 0; k < buttesB.Length && !clash; k += 5)
                    if (Math.Sqrt(Sq((float)(cx - buttesB[k])) + Sq((float)(cy - buttesB[k + 1]))) < buttesB[k + 2] * keep + rr + 20) clash = true;
                for (int k = 0; k < list.Count && !clash; k += 3)
                    if (Math.Sqrt(Sq((float)(cx - list[k])) + Sq((float)(cy - list[k + 1]))) < list[k + 2] + rr + 120) clash = true;
                if (clash) continue;
                list.Add(cx); list.Add(cy); list.Add(rr);
            }
            int made = list.Count / 3; // fewer lakes than wanted: make those bigger, so the lake share stays
            if (made > 0 && made < count)
                for (int k = 2; k < list.Count; k += 3) list[k] *= Math.Sqrt((double)count / made);
            ButteSet set = new ButteSet(); set.Key = key; set.B = list.ToArray(); lakes = set;
            return set.B;
        }

        private sealed class ButteSet { public string Key; public double[] B; }
        private static ButteSet buttes, lakes, massifs;

        /// <summary>Massif recipe: how high a whole mass is lifted (relief units) and how much of the hills stay outside the masses.</summary>
        public static float MassifLift = 0.4f, MassifHills = 0.3f, MassifCut = 0.3f, MassifValley = 320;

        /// <summary>0..1: how far (x, y) is inside one of the world type's hill masses (MassifLayout); broad domed
        /// flanks, an irregular outline (domain warp), lobes.</summary>
        private static float MassifField(Perlin pn, GenSettings s, float worldSize, float x, float y)
        {
            double[] b = MassifLayout(s, worldSize);
            float best = 0;
            for (int k = 0; k < b.Length; k += 5)
            {
                float dx = x - (float)b[k], dy = y - (float)b[k + 1], r = (float)b[k + 2];
                float d2 = dx * dx + dy * dy;
                if (d2 > r * r * 3.2f) continue;
                float o = (float)b[k + 4];
                float wx = dx + pn.Fbm(x / 260f + o, y / 260f + o, 3) * 0.55f * r, wy = dy + pn.Fbm(x / 260f + o + 70, y / 260f + o + 70, 3) * 0.55f * r;
                float warp = 1 + 0.2f * pn.Fbm(x / 140f + o + 50, y / 140f + o + 50, 2);
                float dn = (float)Math.Sqrt(wx * wx + wy * wy) / (r * Math.Max(0.5f, warp));
                if (dn >= 1) continue;
                best = Math.Max(best, (float)b[k + 3] * Smooth(0f, 0.65f, 1 - dn));
            }
            return best;
        }

        /// <summary>
        /// The world type's hill masses: centres (x, z m), radius (m), height (0..1), noise offset, 5 values each, cached
        /// per seed. Total raised ground = MassifRaised x the land (1 - Water); the largest takes MassifLargest of it, the
        /// others lognormal sizes round MassifHa, scaled to the rest. Largest first; not on the town site, not out at sea
        /// (OceanSide), apart from each other (the floor between stays open); 1-2 lobes each.
        /// </summary>
        private static double[] MassifLayout(GenSettings s, float worldSize)
        {
            string key = s.Seed + "/" + s.Massifs + "/" + s.MassifHa + "/" + s.MassifLargest + "/" + s.MassifRaised + "/" + worldSize + "/" + s.Coast + "/" + s.CoastCorner + "/" + s.Water;
            ButteSet cached = massifs;
            if (cached != null && cached.Key == key) return cached.B;
            Random r = new Random(s.Seed * 4513 + 17);
            double land = worldSize * (double)worldSize * Math.Max(0.15, 1 - s.Water / 100.0);
            double total = s.MassifRaised * land;
            int n = s.Massifs;
            double[] area = new double[n];
            area[0] = n == 1 ? total : s.MassifLargest * total;
            if (n > 1)
            {
                double sum = 0;
                for (int i = 1; i < n; i++)
                {
                    double g = Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
                    area[i] = s.MassifHa * 1e4 * Math.Exp(0.6 * g); sum += area[i];
                }
                double rest = Math.Max(0, total - area[0]);
                for (int i = 1; i < n; i++) area[i] *= rest / Math.Max(1, sum);
                Array.Sort(area); Array.Reverse(area);
            }
            double[] town = TownLayout(s);
            double tx = (0.5 + town[0]) * worldSize, ty = (0.5 + town[1]) * worldSize;
            double ca = CoastAngle(s, new Random(s.Seed * 7919 + 13));
            System.Collections.Generic.List<double> list = new System.Collections.Generic.List<double>();
            System.Collections.Generic.List<double[]> placed = new System.Collections.Generic.List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double rad = Math.Sqrt(Math.Max(1e4, area[i]) / Math.PI) * 1.25; // footprint: the raised core plus its flanks
                double[] best = null; double bestScore = double.MaxValue;
                for (int tries = 0; tries < 400; tries++)
                {
                    double margin = Math.Min(0.25 * worldSize, 0.3 * rad);
                    double cx = margin + r.NextDouble() * (worldSize - 2 * margin), cy = margin + r.NextDouble() * (worldSize - 2 * margin);
                    double pen = 0;
                    double dt = Math.Sqrt(Sq((float)(cx - tx)) + Sq((float)(cy - ty)));
                    if (dt < 0.1 * worldSize + 0.8 * rad) pen += 10 + (0.1 * worldSize + 0.8 * rad - dt) / worldSize;
                    if (s.Coast == CoastType.OceanSide)
                    {
                        double dd = (cx / worldSize - 0.5) * Math.Cos(ca) + (cy / worldSize - 0.5) * Math.Sin(ca);
                        if (dd - 0.5 * rad / worldSize < -0.25) pen += 10 + (-0.25 - dd + 0.5 * rad / worldSize);
                    }
                    foreach (double[] q in placed)
                    {
                        double gap = Math.Sqrt(Sq((float)(cx - q[0])) + Sq((float)(cy - q[1]))) - (rad + q[2]) * 1.05 - 40;
                        if (gap < 0) pen += 5 - gap / worldSize;
                    }
                    pen += r.NextDouble() * 0.01;
                    if (pen < bestScore) { bestScore = pen; best = new[] { cx, cy, rad }; }
                    if (pen < 0.01) break;
                }
                placed.Add(best);
                double height = 0.8 + 0.2 * r.NextDouble();
                double off = r.NextDouble() * 900 + 3000;
                list.Add(best[0]); list.Add(best[1]); list.Add(rad); list.Add(height); list.Add(off);
                int lobes = 1 + r.Next(2);
                double ang = r.NextDouble() * Math.PI * 2;
                for (int l = 0; l < lobes; l++)
                {
                    ang += 1.5 + r.NextDouble() * 2;
                    double lr = rad * (0.45 + 0.25 * r.NextDouble()), ld = rad * (0.55 + 0.3 * r.NextDouble());
                    list.Add(best[0] + Math.Cos(ang) * ld); list.Add(best[1] + Math.Sin(ang) * ld); list.Add(lr); list.Add(height * (0.75 + 0.25 * r.NextDouble())); list.Add(off + 300 + l * 97);
                }
            }
            ButteSet set = new ButteSet(); set.Key = key; set.B = list.ToArray(); massifs = set;
            return set.B;
        }

        /// <summary>
        /// Butte centres (x, z metres), radius (m), height (0..1) and a noise offset, 5 values each; cached per seed.
        /// Footprints: lognormal round the world's median size (2-60 ha); never on the town site (EA's Lucky Palms has none
        /// of its high ground in the middle third of the map, where the town is); open floor between them (gaps of 40 m+).
        /// </summary>
        private static double[] ButteLayout(GenSettings s, float worldSize)
        {
            string key = s.Seed + "/" + s.Buttes + "/" + s.ButteHa + "/" + worldSize + "/" + s.Coast + "/" + s.CoastCorner;
            ButteSet cached = buttes;
            if (cached != null && cached.Key == key) return cached.B;
            Random r = new Random(s.Seed * 9137 + 5);
            double[] town = TownLayout(s);
            double tx = (0.5 + town[0]) * worldSize, ty = (0.5 + town[1]) * worldSize;
            System.Collections.Generic.List<double> list = new System.Collections.Generic.List<double>();
            int made = 0;
            for (int tries = 0; tries < 2000 && made < s.Buttes; tries++)
            {
                double g = Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
                double ha = Math.Max(2, Math.Min(60, s.ButteHa * Math.Exp(0.8 * g)));
                double rad = Math.Sqrt(ha * 1e4 / Math.PI) * 1.35; // the raised part (over 15 m) is smaller than the footprint
                double margin = 0.03 * worldSize;
                double cx = margin + r.NextDouble() * (worldSize - 2 * margin), cy = margin + r.NextDouble() * (worldSize - 2 * margin);
                if (Math.Sqrt(Sq((float)(cx - tx)) + Sq((float)(cy - ty))) < 0.18 * worldSize + rad) continue;
                if (s.Coast == CoastType.OceanSide) // not out at sea: the sea side is where the coast term dips
                {
                    double ca = CoastAngle(s, new Random(s.Seed * 7919 + 13));
                    double dd = (cx / worldSize - 0.5) * Math.Cos(ca) + (cy / worldSize - 0.5) * Math.Sin(ca);
                    if (dd - rad / worldSize < -0.22) continue;
                }
                bool clash = false;
                for (int k = 0; k < list.Count && !clash; k += 5) // (lobes count too: an extra 20% for their reach)
                    if (Math.Sqrt(Sq((float)(cx - list[k])) + Sq((float)(cy - list[k + 1]))) < (rad * 1.5 + list[k + 2]) + 60) clash = true;
                if (clash) continue;
                double height = Math.Min(1, 0.55 + 0.45 * Math.Min(1, ha / 40)) * (0.75 + 0.25 * r.NextDouble());
                double off = r.NextDouble() * 900 + 2000;
                // 1-3 extra lobes round the main one (EA's buttes are lobed masses with several summits, not cones)
                int lobes = 2 + r.Next(3);
                double ang = r.NextDouble() * Math.PI * 2;
                made++;
                list.Add(cx); list.Add(cy); list.Add(rad); list.Add(height); list.Add(off);
                for (int l = 0; l < lobes; l++)
                {
                    ang += 1.2 + r.NextDouble() * 2.2;
                    double lr = rad * (0.4 + 0.3 * r.NextDouble()), ld = rad * (0.6 + 0.4 * r.NextDouble());
                    list.Add(cx + Math.Cos(ang) * ld); list.Add(cy + Math.Sin(ang) * ld); list.Add(lr); list.Add(height * (0.7 + 0.3 * r.NextDouble())); list.Add(off + 300 + l * 97);
                }
            }
            ButteSet set = new ButteSet(); set.Key = key; set.B = list.ToArray(); buttes = set;
            return set.B;
        }

        private sealed class PocketSet { public string Key; public double[] P; }
        private static PocketSet pockets; // swapped as a whole, so a preview and an apply on another thread can't mix

        /// <summary>Pocket centres (u, v from the middle) and outline phases, 4 values each; cached per settings.</summary>
        private static double[] PocketLayout(GenSettings s)
        {
            string key = s.Seed + "/" + s.Pockets + "/" + s.Coast;
            PocketSet cached = pockets;
            if (cached != null && cached.Key == key) return cached.P;
            Random r = new Random(s.Seed * 7349 + 11);
            double phase = r.NextDouble() * Math.PI * 2;
            double[] p = new double[s.Pockets * 4];
            for (int k = 0; k < s.Pockets; k++)
            {
                double a = phase + 2 * Math.PI * k / s.Pockets + (r.NextDouble() - 0.5) * 0.8;
                double d = IsIslandCoast(s.Coast) ? 0.18 + 0.08 * r.NextDouble() : 0.30 + 0.10 * r.NextDouble(); // islands: stay on land
                p[k * 4] = Math.Cos(a) * d; p[k * 4 + 1] = Math.Sin(a) * d;
                p[k * 4 + 2] = r.NextDouble() * 6.3; p[k * 4 + 3] = r.NextDouble() * 6.3;
            }
            PocketSet set = new PocketSet(); set.Key = key; set.P = p; pockets = set;
            return p;
        }

        /// <summary>A planning-grid map interpolated to an output grid of 'size' points 'cell' metres apart.</summary>
        private static Grid Resample(Grid c, int size, float cell)
        {
            Grid o = new Grid(size, size);
            float f = cell / CoarseCell;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++) o[i, j] = Sample(c, i * f, j * f);
            return o;
        }

        /// <summary>
        /// Rolling hills on the ground the generator levelled (#170, the tester: towns too flat; where there are no roads, put
        /// hills): EA's town ground is broad smooth rises 100-150 m across with the lots and roads cut into flat shelves
        /// among them (Sunset Valley, Appaloosa Plains, Twinbrook drawn side by side). Fine bumps and round mounds were
        /// tried first: they matched EA's bump figures (library/town_detail.json) but looked like crumpled paper and
        /// molehills. Smooth warped noise at ~5 x the World type's bump width, kept to its upper part (rises with gentle
        /// ground between, no hollows to hold water), up to a few times its bump height, in calmer and rougher stretches
        /// (350 m), scaled by how levelled the spot was (the flat mask: town site, pockets, flat zones; at least half) and
        /// faded out within ~4 m of the sea. Build roads and Place lots level it under what they build.
        /// </summary>
        private static void TownBumps(Grid h, float cell, GenSettings s, Grid flat, Perlin pn)
        {
            if (s.BumpHeight <= 0 || s.BumpDensity <= 0) return;
            int n = h.W;
            float wl = s.BumpWidth * 5f * (float)Math.Sqrt(4.6f / Math.Max(0.5f, s.BumpDensity)); // ~120 m; denser = closer rises
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = i * cell, y = j * cell;
                    float wx = x + 40 * pn.Fbm(x / 260f + 7100, y / 260f + 7100, 2), wy = y + 40 * pn.Fbm(x / 260f + 7300, y / 260f + 7300, 2);
                    float r = Math.Max(0, pn.Fbm(wx / wl + 7500, wy / wl + 7500, 2) + 0.05f);
                    if (r <= 0) continue;
                    float w = Math.Max(0.5f, Math.Min(1, flat[i, j] * 1.5f));
                    w *= 0.4f + 1.2f * Smooth(-0.35f, 0.35f, pn.Fbm(x / 350f + 7900, y / 350f + 7900, 2)); // calmer and rougher stretches
                    int k = j * n + i;
                    h.D[k] += BumpGain * s.BumpHeight * (float)Math.Pow(r, 1.3) * w * Smooth(s.SeaLevel + 0.5f, s.SeaLevel + 4f, h.D[k]);
                }
        }
        /// <summary>Height of a full rise per metre of the World type's bump height.</summary>
        public static float BumpGain = 5f;

        private static Grid FlatMask(Perlin pn, GenSettings s, float worldSize, int n, float cell)
        {
            Grid m = new Grid(n, n);
            float thr = 1 - s.FlatLand / 100f;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float v = pn.Fbm(i * cell / 700f + 800, j * cell / 700f + 800, 3) * 0.5f + 0.5f;
                    m[i, j] = Math.Max(Smooth(thr * 0.7f, thr * 0.7f + 0.12f, v), 0.85f * Settled(s, worldSize, i * cell, j * cell));
                }
            return m;
        }

        /// <summary>Relief value -> metres: seaCut lands just below sea level, 'top' at MaxHeight; flat zones are calmed.</summary>
        private static void ToMetres(Grid g, Grid flat, GenSettings s, float seaCut, float top, float cell, float worldSize, float[] landRanks)
        {
            ToMetres(g, flat, s, seaCut, top, cell, worldSize, landRanks, null);
        }

        /// <summary>smooth: the relief blurred over 60 m, if already known (else worked out here).</summary>
        private static void ToMetres(Grid g, Grid flat, GenSettings s, float seaCut, float top, float cell, float worldSize, float[] landRanks, Grid smooth)
        {
            float span = Math.Max(1e-3f, top - seaCut);
            float land = s.MaxHeight - s.SeaLevel;
            if (smooth == null) smooth = g.Blur(60f / cell);
            for (int i = 0; i < g.D.Length; i++)
            {
                float v = g.D[i];
                // flat zones: pull towards the broad shape, then lower the contrast (gentle land for towns and farms)
                // (a landform recipe shapes its own flat floor and steep sides: no flat-zone smoothing, no height curve)
                float f = s.Buttes > 0 ? 0 : flat.D[i] * (v > seaCut ? 1 : 0);
                v = Lerp(v, smooth.D[i], 0.8f * f);
                float t = (v - seaCut) / span;
                float m;
                if (t >= 0)
                {
                    if (s.Buttes > 0)
                    {
                        // heights as the recipe built them (the height curve squashed the lower layers), in two straight
                        // parts: the land's median at the world's median height (the floor), linear from there to the top
                        t = Math.Min(1.2f, t);
                        if (landRanks != null && landRanks.Length > 2 && s.HeightCurve != null)
                        {
                            float tk = Math.Max(0.02f, Math.Min(0.9f, (landRanks[landRanks.Length / 2] - seaCut) / span));
                            float hk = CurveAt(s.HeightCurve, 0.5f * (s.HeightCurve.Length - 1));
                            t = t < tk ? t / tk * hk : hk + (t - tk) / (1 - tk) * (1 - hk);
                        }
                    }
                    else if (landRanks != null)
                    {
                        // the world type's own height distribution: this spot's share of the land below it -> its height
                        // (fractional rank between the two nearest planning-grid values: a whole-number rank put thousands
                        // of tiny flat terraces into a 1 m map, seen as contour rings in the tester's v0.40 test)
                        int r = Array.BinarySearch(landRanks, v); if (r < 0) r = ~r;
                        float fr = r;
                        if (r > 0 && r < landRanks.Length)
                        {
                            float lo = landRanks[r - 1], hi = landRanks[r];
                            fr = r - 1 + (hi > lo ? (v - lo) / (hi - lo) : 0.5f);
                        }
                        float q = Math.Max(0, Math.Min(1, fr / (landRanks.Length - 1))) * (s.HeightCurve.Length - 1);
                        t = CurveAt(s.HeightCurve, q);
                    }
                    else t = (float)Math.Pow(Math.Min(1.2f, t), s.NoCurve ? 1.15f : 1.6f); // lowlands wider than peaks
                    // (flat zones lowered too, unless the world wants raised plains: nocurve, #150, #155)
                    if (!s.NoCurve) t = Lerp(t, t * 0.35f, f);
                    m = s.SeaLevel + 0.6f + t * land;
                    if (s.Terrace > 0.5f)
                    {
                        // mesas: flat treads and steep risers every TerraceStep metres (the lowest ground stays as it is)
                        float k = (m - s.SeaLevel) / s.TerraceStep, fl = (float)Math.Floor(k), fr = k - fl;
                        float stepped = s.SeaLevel + (fl + Smooth(0.7f, 1f, fr)) * s.TerraceStep;
                        m = Lerp(m, stepped, Math.Min(1, s.Terrace / 100f) * Smooth(0.3f, 1f, k) * (1 - f));
                    }
                }
                else m = Math.Max(0.5f, s.SeaLevel - 1.5f + t * 12f); // gentle sea floor, never below 0
                // the town site stays dry land, a few metres above the sea, even where the map's lowest ground is (so rivers
                // pass through without flooding it); with a central lake the town sits beside the lake instead
                {
                    float town = TownSite(s, worldSize, (i % g.W) * cell, (i / g.W) * cell); // beside the lake with a central lake
                    if (town > 0) m = Math.Max(m, s.SeaLevel + 5f * town);
                }
                g.D[i] = Math.Min(m, s.MaxHeight * 1.1f);
            }
        }

        /// <summary>
        /// Pond sites above the sea (as EA's raised ponds and lakes): fairly flat land at least 3 m above the sea, away from
        /// the coast, rivers, the town centre, the map rim and each other. Each pond's water level is the lowest ground on its
        /// rim, so the hollow holds water. Radius varies 0.6-1.5 x PondRadius.
        /// </summary>
        private static List<float[]> PlacePonds(Grid c, GenSettings s, float worldSize, Random rng, Perlin pn, float coastAngle)
        {
            List<float[]> ponds = new List<float[]>();
            int n = c.W;
            if (s.TownLake && s.Ponds > 0)
            {
                // a lake at the edge of the town centre (the town site is levelled, so the rim is level): the town round it
                float[] town = TownCentre(s, worldSize);
                float r = s.PondRadius * 1.8f, d0 = 0.06f * worldSize;
                for (int k = 0; k < 48; k++)
                {
                    double a = rng.NextDouble() * Math.PI * 2;
                    float d = d0 * (0.6f + 0.8f * (float)rng.NextDouble());
                    float x = town[0] + (float)Math.Cos(a) * d, z = town[1] + (float)Math.Sin(a) * d;
                    if (x < r + 150 || z < r + 150 || x > worldSize - r - 150 || z > worldSize - r - 150) continue;
                    float rimMin = float.MaxValue, rimMax = float.MinValue; bool ok = true;
                    for (int q = 0; q < 24 && ok; q++)
                    {
                        double b = q * Math.PI * 2 / 24;
                        float h = Sample(c, (x + (float)Math.Cos(b) * r) / CoarseCell, (z + (float)Math.Sin(b) * r) / CoarseCell);
                        if (h < s.SeaLevel + 2) ok = false;
                        rimMin = Math.Min(rimMin, h); rimMax = Math.Max(rimMax, h);
                    }
                    if (!ok || rimMax - rimMin > Math.Max(5, r * 0.15f)) continue;
                    ponds.Add(new[] { x, z, r, rimMin - 0.3f });
                    break;
                }
            }
            // lakes on top of the escarpment, their shore a few metres behind the edge, each with a small plunge pool at the
            // foot (EA's waterfalls run from pond to pond: Auto-Place's pond-step falls find these)
            int lakes = 0;
            int[] why = new int[6];
            for (int tries = 0; tries < 6000 && lakes < s.ScarpLakes && s.Coast == CoastType.Iceland && s.Scarp > 0; tries++)
            {
                float x = 150 + (float)rng.NextDouble() * (worldSize - 300), z = 150 + (float)rng.NextDouble() * (worldSize - 300);
                Func2 line = delegate (float px, float pz) { return IcelandEdge(pn, px / worldSize - 0.5f, pz / worldSize - 0.5f, px, pz, coastAngle) + ScarpWander(pn, px, pz); };
                float e = line(x, z);
                if (Math.Abs(e) > 0.03f) { why[0]++; continue; }
                // onto the line (two Newton steps), n = towards the highland
                float nx = 0, nz = 0;
                for (int it = 0; it < 3; it++)
                {
                    float gx = (line(x + 10, z) - line(x - 10, z)) / 20f, gz = (line(x, z + 10) - line(x, z - 10)) / 20f, gg = gx * gx + gz * gz;
                    if (gg < 1e-14f) break;
                    e = line(x, z); x -= e * gx / gg; z -= e * gz / gg;
                    float gl = (float)Math.Sqrt(gg); nx = gx / gl; nz = gz / gl;
                }
                if (nx == 0 && nz == 0 || Math.Abs(line(x, z)) > 0.002f) { why[1]++; continue; }
                float r = s.PondRadius * (0.5f + 0.4f * (float)rng.NextDouble()), rp = 14f;
                // (the outline's lobes reach 1.42 x r: kept on top of the cliff, its nearest shore a few metres from the lip)
                float lx = x + nx * (ScarpHalf + 2 + 1.1f * r), lz = z + nz * (ScarpHalf + 2 + 1.1f * r);
                float px2 = x - nx * (ScarpHalf + 2 + rp), pz2 = z - nz * (ScarpHalf + 2 + rp);
                if (Math.Min(lx, lz) < r + 150 || Math.Max(lx, lz) > worldSize - r - 150 || Math.Min(px2, pz2) < 150 || Math.Max(px2, pz2) > worldSize - 150) continue;
                if (TownSite(s, worldSize, lx, lz) > 0.2f || TownSite(s, worldSize, px2, pz2) > 0.2f) { why[2]++; continue; }
                bool ok = true;
                foreach (float[] q in ponds) if (Sq(q[0] - lx) + Sq(q[1] - lz) < Sq(q[2] + r + 200)) { ok = false; break; }
                if (!ok) continue;
                float rimMin, rimMax, pMin, pMax;
                Rim(c, lx, lz, r, -nx, -nz, out rimMin, out rimMax); // not the side facing the cliff (the 8 m grid blurs it; the leak dam holds that edge)
                Rim(c, px2, pz2, rp, nx, nz, out pMin, out pMax);
                if (rimMin < s.SeaLevel + 15 || rimMax - rimMin > Math.Max(8, r * 0.4f)) { why[3]++; continue; }
                if (pMin < s.SeaLevel + 3 || pMax - pMin > 4) { why[4]++; continue; }
                if (rimMin - pMin < 14) { why[5]++; continue; }
                ponds.Add(new[] { lx, lz, r, rimMin - 0.3f });
                ponds.Add(new[] { px2, pz2, rp, pMin - 0.3f, 1 });
                lakes++;
            }
            if (Environment.GetEnvironmentVariable("CAWSCARPDBG") != null) Console.WriteLine("scarp lakes " + lakes + ": far " + why[0] + ", line " + why[1] + ", town " + why[2] + ", lake rim " + why[3] + ", pool " + why[4] + ", drop " + why[5]);
            for (int tries = 0; tries < 800 && ponds.Count < s.Ponds; tries++)
            {
                float r = s.PondRadius * (0.6f + 0.9f * (float)rng.NextDouble());
                float x = 150 + r + (float)rng.NextDouble() * (worldSize - 300 - 2 * r), z = 150 + r + (float)rng.NextDouble() * (worldSize - 300 - 2 * r);
                if (TownSite(s, worldSize, x, z) > 0.2f) continue;
                bool ok = true; float rimMin = float.MaxValue, rimMax = float.MinValue;
                foreach (float[] q in ponds) if (Sq(q[0] - x) + Sq(q[1] - z) < Sq(q[2] + r + 120)) { ok = false; break; }
                for (int k = 0; k < 24 && ok; k++)
                {
                    double a = k * Math.PI * 2 / 24;
                    foreach (float f in new[] { 0.5f, 1f, 1.3f, 2f })
                    {
                        float h = Sample(c, (x + (float)Math.Cos(a) * r * f) / CoarseCell, (z + (float)Math.Sin(a) * r * f) / CoarseCell);
                        if (h < s.SeaLevel + 3) { ok = false; break; } // keeps away from the sea and river channels
                        if (f == 1f) { rimMin = Math.Min(rimMin, h); rimMax = Math.Max(rimMax, h); }
                    }
                }
                // too steep a spot (a pond on a hillside needs a dam): the first tries want gentle ground, later ones accept
                // the hollows of hilly worlds (EA's Lunar Lakes, Moonlight Falls)
                if (!ok || rimMax - rimMin > Math.Max(4, r * (tries < 150 ? 0.12f : 0.22f))) continue;
                ponds.Add(new[] { x, z, r, rimMin - 0.3f });
            }
            return ponds;
        }

        private delegate float Func2(float x, float z);

        /// <summary>Lowest and highest ground round a circle, leaving out the side facing (sx, sz).</summary>
        private static void Rim(Grid c, float x, float z, float r, float sx, float sz, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int k = 0; k < 24; k++)
            {
                double a = k * Math.PI * 2 / 24;
                if (Math.Cos(a) * sx + Math.Sin(a) * sz > 0.5) continue;
                float h = Sample(c, (x + (float)Math.Cos(a) * r) / CoarseCell, (z + (float)Math.Sin(a) * r) / CoarseCell);
                lo = Math.Min(lo, h); hi = Math.Max(hi, h);
            }
        }

        /// <summary>A generated pond's outline: distance from its middle as a share of its (lobed) radius; under 1 = water.
        /// The radius varies with the direction by a few smooth lobes (the tester's v0.38 test: ponds showed as perfect circles),
        /// phases from the pond's position, so every grid gets the same shape.</summary>
        internal static float PondD(float[] q, float px, float pz)
        {
            float x = q[0], z = q[1], r = q[2];
            double p1 = (x * 0.013 + z * 0.007) % 6.283, p2 = (x * 0.005 + z * 0.019) % 6.283, p3 = (x * 0.011 + z * 0.003) % 6.283;
            float dx = px - x, dz = pz - z;
            double a = Math.Atan2(dz, dx);
            float lobes = 1 + 0.22f * (float)Math.Sin(2 * a + p1) + 0.13f * (float)Math.Sin(3 * a + p2) + 0.07f * (float)Math.Sin(5 * a + p3);
            return (float)Math.Sqrt(dx * dx + dz * dz) / (r * lobes);
        }

        private static void FitPond(Grid g, float cell, float[] q)
        {
            // a plunge pool (q[4] = 1) keeps the cliff behind it: no bank shaping
            FitPond(g, cell, q[0], q[1], q[2] * 1.45f, q[3], delegate (float x, float z) { float gh = g.At(x / cell, z / cell); return PondD(q, x, z) < 1 && gh < q[3] && gh > q[3] - 9; },
                q.Length < 5 || q[4] == 0);
        }

        /// <summary>Whether (x, z) metres is meant to be under a pond's water.</summary>
        public delegate bool InPond(float x, float z);

        /// <summary>
        /// Fits the land round a raised pond (water level 'level', middle (cx, cz), meant to stay within 'reach' m) to CAW's
        /// pond, which floods out from its seed over all connected ground below the level (the tester's 2026-10-26 test:
        /// a strip of water further down the hill, and vertical walls round the ponds):
        /// 1. banks: ground outside the water no higher than a natural bank rising from the shore (about 10 deg at the
        ///    water, 30 deg 20 m back, 45 deg 35 m back), so the shore is a slope, not a wall;
        /// 2. no leaks: where ground below the level leads out of the pond (a gully, a dip in the rim), a low dam half a
        ///    metre above the water closes it, eased back into the land.
        /// </summary>
        public static void FitPond(Grid g, float cell, float cx, float cz, float reach, float level, InPond inside) { FitPond(g, cell, cx, cz, reach, level, inside, true); }

        public static void FitPond(Grid g, float cell, float cx, float cz, float reach, float level, InPond inside, bool banks)
        {
            if (Environment.GetEnvironmentVariable("CAWNOFITPOND") != null) return; // offline before/after checks
            const float Bank = 80f;
            int x0 = Math.Max(0, (int)((cx - reach - Bank) / cell)), x1 = Math.Min(g.W - 1, (int)((cx + reach + Bank) / cell) + 1);
            int z0 = Math.Max(0, (int)((cz - reach - Bank) / cell)), z1 = Math.Min(g.H - 1, (int)((cz + reach + Bank) / cell) + 1);
            int w = x1 - x0 + 1, hgt = z1 - z0 + 1;
            if (w < 3 || hgt < 3) return;
            bool[] wet = new bool[w * hgt];
            float[] e = new float[w * hgt];
            for (int j = 0; j < hgt; j++)
                for (int i = 0; i < w; i++)
                {
                    wet[j * w + i] = inside((x0 + i) * cell, (z0 + j) * cell);
                    e[j * w + i] = wet[j * w + i] ? 0 : 1e9f;
                }
            // distance from the water (chamfer, metres)
            float d1 = cell, d2 = cell * 1.41421f;
            for (int j = 0; j < hgt; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i; float v = e[k];
                    if (i > 0) v = Math.Min(v, e[k - 1] + d1);
                    if (j > 0) { v = Math.Min(v, e[k - w] + d1); if (i > 0) v = Math.Min(v, e[k - w - 1] + d2); if (i < w - 1) v = Math.Min(v, e[k - w + 1] + d2); }
                    e[k] = v;
                }
            for (int j = hgt - 1; j >= 0; j--)
                for (int i = w - 1; i >= 0; i--)
                {
                    int k = j * w + i; float v = e[k];
                    if (i < w - 1) v = Math.Min(v, e[k + 1] + d1);
                    if (j < hgt - 1) { v = Math.Min(v, e[k + w] + d1); if (i < w - 1) v = Math.Min(v, e[k + w + 1] + d2); if (i > 0) v = Math.Min(v, e[k + w - 1] + d2); }
                    e[k] = v;
                }
            // the water's own edge: ground under the water shelves down from the shore (erosion after the bowl was dug
            // can pile earth back into its edge: 10 m mounds inside Aurora Skies' ponds)
            float[] ei = new float[w * hgt];
            for (int k = 0; k < ei.Length; k++) ei[k] = wet[k] ? 1e9f : 0;
            for (int j = 0; j < hgt; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i; float v = ei[k];
                    if (i > 0) v = Math.Min(v, ei[k - 1] + d1);
                    if (j > 0) { v = Math.Min(v, ei[k - w] + d1); if (i > 0) v = Math.Min(v, ei[k - w - 1] + d2); if (i < w - 1) v = Math.Min(v, ei[k - w + 1] + d2); }
                    ei[k] = v;
                }
            for (int j = hgt - 1; j >= 0; j--)
                for (int i = w - 1; i >= 0; i--)
                {
                    int k = j * w + i; float v = ei[k];
                    if (i < w - 1) v = Math.Min(v, ei[k + 1] + d1);
                    if (j < hgt - 1) { v = Math.Min(v, ei[k + w] + d1); if (i < w - 1) v = Math.Min(v, ei[k + w + 1] + d2); if (i > 0) v = Math.Min(v, ei[k + w - 1] + d2); }
                    ei[k] = v;
                }
            for (int j = 0; j < hgt; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i;
                    if (!wet[k]) continue;
                    float cap = level + 0.2f - 0.15f * ei[k];
                    if (g[x0 + i, z0 + j] > cap) g[x0 + i, z0 + j] = cap;
                }
            // 1. natural banks
            for (int j = 0; j < hgt; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = j * w + i;
                    if (!banks || wet[k] || e[k] > Bank) continue;
                    // ~7 deg at the water, ~30 deg 30 m back; faded out by 45 m so it never leaves a cut face in a hill
                    float cap = level + 0.3f + 0.12f * e[k] + 0.008f * e[k] * e[k];
                    float h0 = g[x0 + i, z0 + j];
                    if (h0 > cap) g[x0 + i, z0 + j] = Lerp(cap, h0, Smooth(10f, 45f, e[k]));
                }
            // 2. leaks: flood from the pond's lowest wet cell as CAW does; dry ground reached is dammed
            // (eight neighbours: CAW's water also runs corner to corner, the tester's v1.3.2 test: a pond on Aurora Skies' cliff top
            // leaked through a diagonal gap in its rim and flooded the map to 69 m)
            for (int round = 0; round < 8; round++)
            {
                // from every wet cell under the level, not just the lowest: CAW seeds the flood at the pond's centre, and a
                // pond can stand in two hollows (Aurora Skies seed 5: the centre's hollow leaked, the lowest one didn't)
                bool[] seen = new bool[w * hgt];
                List<int> leaks = new List<int>();
                Queue<int> open = new Queue<int>();
                for (int k = 0; k < wet.Length; k++)
                    if (wet[k] && g[x0 + k % w, z0 + k / w] < level) { open.Enqueue(k); seen[k] = true; }
                if (open.Count == 0) return;
                while (open.Count > 0)
                {
                    int k = open.Dequeue(), i = k % w, j = k / w;
                    if (!wet[k] || i == 0 || j == 0 || i == w - 1 || j == hgt - 1) { leaks.Add(k); continue; }
                    int[] nb = { k - 1, k + 1, k - w, k + w, k - w - 1, k - w + 1, k + w - 1, k + w + 1 };
                    foreach (int n in nb)
                        if (!seen[n] && g[x0 + n % w, z0 + n / w] < level + 0.3f) { seen[n] = true; open.Enqueue(n); }
                }
                if (leaks.Count == 0) return;
                // a narrow dam (its outer face ~60 deg): a wide one built out over a cliff below the lip (an escarpment
                // lake) as a mound tens of metres across
                int rad = (int)Math.Ceiling(8f / cell);
                foreach (int k in leaks)
                {
                    int i = k % w, j = k / w;
                    for (int b = Math.Max(0, j - rad); b <= Math.Min(hgt - 1, j + rad); b++)
                        for (int a = Math.Max(0, i - rad); a <= Math.Min(w - 1, i + rad); a++)
                        {
                            if (wet[b * w + a]) continue;
                            float d = (float)Math.Sqrt((a - i) * (a - i) + (b - j) * (b - j)) * cell;
                            float want = level + 0.5f - Math.Max(0, d - Math.Max(2.5f, cell)) * 1.7f;
                            if (g[x0 + a, z0 + b] < want) g[x0 + a, z0 + b] = want;
                        }
                }
            }
        }

        /// <summary>Digs a pond's bowl: up to ~3 m (bigger ponds deeper, max 6 m) below its water level in the middle.</summary>
        private static void DigPond(Grid g, float cell, float[] q)
        {
            float x = q[0], z = q[1], r = q[2], level = q[3], depth = Math.Min(6f, 1.5f + r / 40f);
            float reach = r * 1.4f;
            int x0 = Math.Max(0, (int)((x - reach) / cell)), x1 = Math.Min(g.W - 1, (int)((x + reach) / cell) + 1);
            int z0 = Math.Max(0, (int)((z - reach) / cell)), z1 = Math.Min(g.H - 1, (int)((z + reach) / cell) + 1);
            for (int j = z0; j <= z1; j++)
                for (int i = x0; i <= x1; i++)
                {
                    float d = PondD(q, i * cell, j * cell);
                    if (d >= 1) continue;
                    float bed = level - 0.4f - depth * (1 - d * d);
                    // the shore: the last 25% rises gently to the water line, so the edge is a soft bank
                    if (d > 0.75f) bed = Lerp(bed, level + 0.2f, Smooth(0.75f, 1f, d));
                    // the water takes the land's shape: ground standing well above the level stays as the shore
                    // (digging the whole outline cut a bowl into hillsides: the tester's 2026-10-26 test, "like a crater");
                    // the middle is always dug, so CAW's pond seed is under water
                    float orig = g[i, j];
                    float wgt = Math.Max(1 - Smooth(10f, 20f, orig - level), 1 - Smooth(0.3f, 0.5f, d));
                    g[i, j] = Lerp(orig, Math.Min(orig, bed), wgt);
                }
        }

        /// <summary>The Iceland coast's highland/lowland line: over 0 = highland side (map units, ~0.1 = 200 m on a 2 km map).</summary>
        private static float IcelandEdge(Perlin pn, float u, float v, float x, float y, float coastAngle)
        {
            float ca = (float)Math.Cos(coastAngle), sa = (float)Math.Sin(coastAngle);
            return u * ca + v * sa + IcelandSplit + pn.Fbm(x / 420f + 650, y / 420f + 650, 3) * 0.12f;
        }

        private const float ScarpHalf = 6f; // m: half the cliff's width (a 75-80 deg face, as EA's pond-step falls)
        private static float ScarpWander(Perlin pn, float x, float y) { return pn.Fbm(x / 130f + 2650, y / 130f + 2650, 2) * 0.012f; }

        /// <summary>Cells on the escarpment's face (kept steep by the thermal erosion).</summary>
        private static bool[] ScarpFace(Grid g, float cell, GenSettings s, Perlin pn, float worldSize, float coastAngle)
        {
            bool[] m = new bool[g.D.Length];
            float band = 1.6f * ScarpHalf / worldSize;
            for (int j = 0; j < g.H; j++)
                for (int i = 0; i < g.W; i++)
                {
                    float x = i * cell, y = j * cell;
                    float e = IcelandEdge(pn, x / worldSize - 0.5f, y / worldSize - 0.5f, x, y, coastAngle);
                    if (Math.Abs(e) > 0.02f) continue;
                    m[j * g.W + i] = Math.Abs(e + ScarpWander(pn, x, y)) < band && g[i, j] > s.SeaLevel + 1;
                }
            return m;
        }

        /// <summary>
        /// The Iceland highland's edge as a cliff (the tester's 2026-10-26 Aurora Skies test: lowland and highland distinct,
        /// a cliff drop between them with lakes on top and waterfalls over it). The broad rise of the relief is swapped
        /// for a step about 12 m across, Scarp x 48 m high, its line wandering; the lowland at its foot is never pushed
        /// down to the sea.
        /// </summary>
        private static void ApplyScarp(Grid g, float cell, GenSettings s, Perlin pn, float worldSize, float coastAngle)
        {
            float H = 48f * s.Scarp;
            for (int j = 0; j < g.H; j++)
                for (int i = 0; i < g.W; i++)
                {
                    float hh = g[i, j];
                    if (hh < s.SeaLevel + 0.5f) continue;
                    float x = i * cell, y = j * cell;
                    float e = IcelandEdge(pn, x / worldSize - 0.5f, y / worldSize - 0.5f, x, y, coastAngle);
                    if (e < -0.14f || e > 0.14f) continue;
                    float es = e + ScarpWander(pn, x, y);
                    // in metres across the line (the noise terms change the line's gradient)
                    float gx = (IcelandEdge(pn, (x + 2) / worldSize - 0.5f, y / worldSize - 0.5f, x + 2, y, coastAngle) + ScarpWander(pn, x + 2, y) - es) / 2f;
                    float gy = (IcelandEdge(pn, x / worldSize - 0.5f, (y + 2) / worldSize - 0.5f, x, y + 2, coastAngle) + ScarpWander(pn, x, y + 2) - es) / 2f;
                    float gl = Math.Max(1e-7f, (float)Math.Sqrt(gx * gx + gy * gy));
                    float d = H * (Smooth(-ScarpHalf, ScarpHalf, es / gl) - Smooth(-0.12f, 0.12f, e));
                    float nh = hh + d;
                    if (d < 0) nh = Math.Max(nh, Math.Min(hh, s.SeaLevel + 3f));
                    g[i, j] = nh;
                }
        }

        /// <summary>Distance (m) from every cell to the nearest seed cell (chamfer).</summary>
        private static float[] Chamfer(bool[] seed, int w, int hh, float cell)
        {
            int N = w * hh; float[] d = new float[N];
            for (int i = 0; i < N; i++) d[i] = seed[i] ? 0 : 1e9f;
            float d1 = cell, d2 = cell * 1.4142f;
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + d1);
                    if (y > 0) { v = Math.Min(v, d[i - w] + d1); if (x > 0) v = Math.Min(v, d[i - w - 1] + d2); if (x < w - 1) v = Math.Min(v, d[i - w + 1] + d2); }
                    d[i] = v;
                }
            for (int y = hh - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x; float v = d[i];
                    if (x < w - 1) v = Math.Min(v, d[i + 1] + d1);
                    if (y < hh - 1) { v = Math.Min(v, d[i + w] + d1); if (x < w - 1) v = Math.Min(v, d[i + w + 1] + d2); if (x > 0) v = Math.Min(v, d[i + w - 1] + d2); }
                    d[i] = v;
                }
            return d;
        }

        /// <summary>
        /// Land narrower than about 2 x halfWidth standing in the water (a fin left between two fjord or river cuts:
        /// the tester's 2026-10-26 test) goes under the water: a morphological opening of the land, eased over a few metres.
        /// </summary>
        private static void RemoveSlivers(Grid h, float cell, float sea, float halfWidth)
        {
            int w = h.W, hh = h.H, N = w * hh;
            bool[] water = new bool[N];
            for (int i = 0; i < N; i++) water[i] = h.D[i] < sea;
            float[] dw = Chamfer(water, w, hh, cell);
            bool[] core = new bool[N];
            for (int i = 0; i < N; i++) core[i] = dw[i] >= halfWidth;
            float[] dc = Chamfer(core, w, hh, cell);
            float[] cut = new float[N];
            int n = 0;
            for (int i = 0; i < N; i++)
                if (!water[i] && dc[i] > halfWidth + cell * 1.5f) { cut[i] = h.D[i] - (sea - 1.2f); n++; }
            if (n == 0) return;
            // soften the cut's edge (where a fin meets the hillside)
            Grid c = new Grid(w, hh); Array.Copy(cut, c.D, N);
            c = c.Blur(Math.Max(1f, 3f / cell));
            for (int i = 0; i < N; i++)
            {
                float k = Math.Max(cut[i], Math.Min(c.D[i], h.D[i] - sea + 0.6f));
                if (k > 0 && !water[i]) h.D[i] -= k;
            }
        }

        /// <summary>
        /// Flat land just above the sea is lifted a little away from the shore: CAW's water and waves seem to cover ground
        /// within about a metre of the sea level (the tester's 2026-10-26 test, "water slightly too high"). The shore itself
        /// keeps its gentle slope; 30 m inland ground under 2.5 m above the sea is up to 1.2 m higher.
        /// </summary>
        private static void LiftLowland(Grid h, float cell, float sea)
        {
            bool[] wet = new bool[h.D.Length];
            for (int i = 0; i < wet.Length; i++) wet[i] = h.D[i] < sea + 0.2f;
            float[] d = Chamfer(wet, h.W, h.H, cell);
            for (int i = 0; i < h.D.Length; i++)
            {
                float a = h.D[i] - sea;
                if (a < 0 || a > 2.5f) continue;
                h.D[i] += 1.2f * Smooth(0f, 30f, d[i]) * (1 - a / 2.5f);
            }
        }

        /// <summary>The height curve at fractional knot q, smooth through the knots (monotone cubic: no creases, so no
        /// contour lines where the slope of the curve changes; never overshoots between knots).</summary>
        private static float CurveAt(float[] c, float q)
        {
            int n = c.Length, k = Math.Max(0, Math.Min(n - 2, (int)q));
            float u = Math.Max(0, Math.Min(1, q - k));
            float d0 = Tangent(c, k), d1 = Tangent(c, k + 1), y0 = c[k], y1 = c[k + 1];
            float u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * y0 + (u3 - 2 * u2 + u) * d0 + (-2 * u3 + 3 * u2) * y1 + (u3 - u2) * d1;
        }

        private static float Tangent(float[] c, int k)
        {
            int n = c.Length;
            float a = k > 0 ? c[k] - c[k - 1] : c[1] - c[0], b = k < n - 1 ? c[k + 1] - c[k] : c[n - 1] - c[n - 2];
            if (a * b <= 0) return 0;                  // a flat spot or a turn: no overshoot
            return 2 * a * b / (a + b);                // harmonic mean (Fritsch-Carlson style)
        }

        /// <summary>The land's relief values in order (for the world type's height curve), or null without a curve.</summary>
        private static float[] LandRanks(Grid coarse, Grid flat, GenSettings s, float seaCut)
        {
            if (s.HeightCurve == null || s.HeightCurve.Length < 2) return null;
            List<float> l = new List<float>();
            Grid sm = coarse.Blur(60f / CoarseCell);
            for (int i = 0; i < coarse.D.Length; i++)
                if (coarse.D[i] > seaCut) l.Add(Lerp(coarse.D[i], sm.D[i], 0.8f * flat.D[i]));
            float[] a = l.ToArray(); Array.Sort(a);
            if (a.Length < 2) return null;
            // 257 quantiles, lightly smoothed: every planning-grid value as a knot gave thousands of tiny slope kinks,
            // which showed as faint contour bands on a 1 m map
            const int Q = 257;
            float[] q = new float[Q];
            for (int k = 0; k < Q; k++) q[k] = a[(int)((long)k * (a.Length - 1) / (Q - 1))];
            float[] o = (float[])q.Clone();
            for (int k = 1; k < Q - 1; k++) o[k] = 0.25f * q[k - 1] + 0.5f * q[k] + 0.25f * q[k + 1];
            for (int k = 1; k < Q; k++) if (o[k] <= o[k - 1]) o[k] = o[k - 1] + 1e-6f; // strictly increasing
            return o;
        }

        /// <summary>
        /// Evens out ground within a few metres of the sea level towards its 40 m average, so the coastline follows broad
        /// forms instead of every small bump (the tester's v0.38 test; EA's coastlines are 1.4-10 times a circle's length,
        /// ours were twice as ragged on Sunset Valley). The same in metres on every grid, so preview and full map agree.
        /// </summary>
        private static void SmoothCoast(Grid g, float cell, float sea)
        {
            Grid b = g.Blur(40f / cell);
            for (int i = 0; i < g.D.Length; i++)
            {
                float w = 1 - Smooth(0, 6, Math.Abs(g.D[i] - sea));
                if (w > 0) g.D[i] = Lerp(g.D[i], b.D[i], 0.7f * w);
            }
        }

        /// <summary>
        /// Below-sea pockets smaller than 'minArea' m2 that don't touch the map edge (puddles beside a river or behind the
        /// shore, where noise dips a few cells under the water line) are raised just above the sea.
        /// </summary>
        private static void FillPuddles(Grid g, float cell, float sea, float minArea)
        {
            int n = g.W, N = g.D.Length, maxCells = (int)(minArea / (cell * cell));
            bool[] seen = new bool[N];
            List<int> comp = new List<int>(); Stack<int> st = new Stack<int>();
            for (int s0 = 0; s0 < N; s0++)
            {
                if (seen[s0] || g.D[s0] >= sea) continue;
                comp.Clear(); bool edge = false; seen[s0] = true; st.Push(s0);
                while (st.Count > 0)
                {
                    int i = st.Pop(), x = i % n, y = i / n; comp.Add(i);
                    if (x == 0 || y == 0 || x == n - 1 || y == g.H - 1) edge = true;
                    foreach (int j in new[] { x > 0 ? i - 1 : -1, x < n - 1 ? i + 1 : -1, y > 0 ? i - n : -1, y < g.H - 1 ? i + n : -1 })
                        if (j >= 0 && !seen[j] && g.D[j] < sea) { seen[j] = true; st.Push(j); }
                }
                if (!edge && comp.Count <= maxCells) foreach (int i in comp) g.D[i] = sea + 0.2f;
            }
        }

        /// <summary>Cells below the sea joined to the map edge (the sea itself).</summary>
        private static bool[] EdgeWater(Grid g, float sea)
        {
            int n = g.W, N = g.D.Length;
            bool[] open = new bool[N];
            Stack<int> st = new Stack<int>();
            for (int k = 0; k < n; k++)
                foreach (int i in new[] { k, (n - 1) * n + k, k * n, k * n + n - 1 })
                    if (g.D[i] < sea && !open[i]) { open[i] = true; st.Push(i); }
            while (st.Count > 0)
            {
                int i = st.Pop(), x = i % n, y = i / n;
                foreach (int j in new[] { x > 0 ? i - 1 : -1, x < n - 1 ? i + 1 : -1, y > 0 ? i - n : -1, y < n - 1 ? i + n : -1 })
                    if (j >= 0 && !open[j] && g.D[j] < sea) { open[j] = true; st.Push(j); }
            }
            return open;
        }

        /// <summary>
        /// Hollows below the sea away from the coast: the largest are kept as lakes up to 'keep' of the map (0 = none),
        /// the rest become dry land just above the sea. Returns where land was raised.
        /// </summary>
        private static Grid FillInland(Grid g, float sea, float keep)
        {
            int n = g.W, N = g.D.Length;
            bool[] open = EdgeWater(g, sea);
            int[] comp = new int[N];
            for (int i = 0; i < N; i++) comp[i] = -1;
            List<List<int>> hollows = new List<List<int>>();
            Stack<int> st = new Stack<int>();
            for (int s0 = 0; s0 < N; s0++)
            {
                if (open[s0] || comp[s0] >= 0 || g.D[s0] >= sea) continue;
                List<int> hol = new List<int>();
                comp[s0] = hollows.Count; st.Push(s0);
                while (st.Count > 0)
                {
                    int i = st.Pop(), x = i % n, y = i / n; hol.Add(i);
                    foreach (int j in new[] { x > 0 ? i - 1 : -1, x < n - 1 ? i + 1 : -1, y > 0 ? i - n : -1, y < n - 1 ? i + n : -1 })
                        if (j >= 0 && comp[j] < 0 && !open[j] && g.D[j] < sea) { comp[j] = hollows.Count; st.Push(j); }
                }
                hollows.Add(hol);
            }
            // largest first, while they fit the share (a little over is fine; one lake is kept if the share asks for any)
            hollows.Sort(delegate (List<int> a, List<int> b) { return b.Count.CompareTo(a.Count); });
            float budget = keep * N, kept = 0;
            Grid dry = new Grid(n, g.H);
            foreach (List<int> hol in hollows)
            {
                if (keep > 0 && kept + hol.Count <= budget * 1.25f + 1) { kept += hol.Count; continue; }
                foreach (int i in hol) { g.D[i] = sea + 0.6f; dry.D[i] = 1; }
            }
            return dry;
        }

        /// <summary>The q-th share of the heights above 'floor' (NaN-free; floor + 0 if none).</summary>
        private static float LandPercentile(Grid g, float floor, float q)
        {
            List<float> a = new List<float>();
            foreach (float v in g.D) if (v > floor) a.Add(v);
            if (a.Count == 0) return floor;
            a.Sort();
            return a[Math.Max(0, Math.Min(a.Count - 1, (int)(q * (a.Count - 1))))];
        }

        /// <summary>keeptop: the land above 'floor' stretched by 'gain' (the same on every grid, so preview and full map match).</summary>
        private static void KeepHeight(Grid g, float floor, float gain)
        {
            if (gain <= 1.0001f) return;
            for (int i = 0; i < g.D.Length; i++) if (g.D[i] > floor) g.D[i] = floor + (g.D[i] - floor) * gain;
        }

        private static float Percentile(Grid g, float q)
        {
            float[] a = (float[])g.D.Clone();
            Array.Sort(a);
            return a[Math.Max(0, Math.Min(a.Length - 1, (int)(q * (a.Length - 1))))];
        }

        // ---------- rivers ----------

        /// <summary>
        /// Rivers on the 8 m grid: on mainland coasts each runs in from a map edge along the lowest ground to the sea, a
        /// lake or an earlier river (ThroughRiver; the fork and Nile recipes first); otherwise the longest descent from a
        /// low source, or a river in from the edge (EdgeRiver). Each is carved to just below sea level so Sims 3 fills it
        /// with water.
        /// </summary>
        private static List<List<float[]>> CarveRivers(Grid c, GenSettings s, Random rng, float worldSize, Perlin pn, out float[] townHint)
        {
            townHint = null;
            List<List<float[]>> rivers = new List<List<float[]>>();
            int n = c.W;
            Grid filled = PriorityFlood(c, s.SeaLevel);
            float lo, hi; c.MinMax(out lo, out hi);
            int k0 = 0;
            if (s.Fork && s.Rivers >= 2 && !IsIslandCoast(s.Coast)) k0 = ForkRivers(c, s, rng, worldSize, pn, hi, rivers, out townHint);
            for (int k = k0; k < s.Rivers; k++)
            {
                // mainland worlds: a river that flows in from beyond the map edge and runs all the way to the sea (or into
                // an earlier river, or across to the far edge when there is no sea), as Riverview's do (the tester: ours
                // stopped halfway)
                if (!IsIslandCoast(s.Coast))
                {
                    // nile: the first river runs down the middle of the first valley's floor, from the far map edge to the
                    // sea (Al Simhara, #145: found its own way, it often left the valley)
                    if (k == 0 && s.Nile && s.Valleys > 0 && s.Coast == CoastType.OceanSide)
                    {
                        List<float[]> axis = ValleyAxis(c, s, worldSize, pn);
                        if (axis != null) { rivers.Add(Meander(Chaikin(Chaikin(axis)), rng, pn, worldSize, s.Meander * 0.5f)); continue; }
                    }
                    List<int[]> through = ThroughRiver(c, s.SeaLevel, hi, rng, rivers, s.Buttes > 0);
                    if (through != null)
                    {
                        List<float[]> tc = new List<float[]>();
                        foreach (int[] q in through) tc.Add(new[] { q[0] * CoarseCell, q[1] * CoarseCell });
                        List<float[]> sm = Chaikin(Chaikin(tc));
                        sm[0] = tc[0]; // keep the start on the map edge
                        List<float[]> mr = Meander(sm, rng, pn, worldSize, s.Meander);
                        rivers.Add(mr);
                        continue;
                    }
                }
                // try many sources on low-to-middle ground and keep the longest course that reaches the sea (or the
                // edge), away from other rivers
                List<int[]> best = null; float bestScore = float.MinValue;
                for (int t = 0; t < 240; t++)
                {
                    int x = n / 10 + rng.Next(n * 8 / 10), y = n / 10 + rng.Next(n * 8 / 10);
                    // low ground only: river water shows at sea level, so a source high up means carving a canyon or a
                    // terraced bowl down to the sea (the tester's v0.38 test: 'carved with a spoon'); EA's rivers run
                    // through lowland (Riverview, Twinbrook, Champs Les Sims)
                    float above = c[x, y] - s.SeaLevel, range = Math.Max(1, hi - s.SeaLevel);
                    if (above < Math.Max(1.5f, 0.02f * range) || above > Math.Max(5f, Math.Min(0.4f * range, 14f))) continue;
                    bool near = false;
                    foreach (List<float[]> r in rivers) foreach (float[] p in r)
                            if (Sq(p[0] / CoarseCell - x) + Sq(p[1] / CoarseCell - y) < Sq(n / 6f)) { near = true; break; }
                    if (near) continue;
                    bool toSea;
                    List<int[]> path = Descend(c, filled, s.SeaLevel, x, y, out toSea);
                    float score = path.Count * (toSea ? 1.3f : 1f) * (0.8f + 0.4f * (float)rng.NextDouble());
                    foreach (List<float[]> r in rivers) foreach (int[] q in path)
                            foreach (float[] p in r) if (Sq(p[0] / CoarseCell - q[0]) + Sq(p[1] / CoarseCell - q[1]) < 36) { score -= 0.5f; break; }
                    if (score > bestScore) { bestScore = score; best = path; }
                }
                // a river flowing in from beyond the map edge along the low ground (Riverview's do): kept when it is
                // longer than the best source found on the map
                List<int[]> edge = EdgeRiver(c, s.SeaLevel, hi, rng, rivers);
                if (edge != null && (best == null || edge.Count > best.Count * 1.2f)) best = edge;
                if (best == null || best.Count < 40) break;
                List<float[]> course = new List<float[]>();
                foreach (int[] q in best) course.Add(new[] { q[0] * CoarseCell, q[1] * CoarseCell });
                rivers.Add(Meander(Chaikin(Chaikin(course)), rng, pn, worldSize, s.Meander));
            }
            if (s.RiverIsland && rivers.Count > 0)
            {
                List<float[]> side = IslandChannel(rivers[0], s, worldSize, c);
                if (side != null) { rivers.Add(side); if (townHint == null) townHint = side[side.Count / 2]; }
            }
            foreach (List<float[]> r in rivers) CarveChannel(c, CoarseCell, r, s, worldSize, pn);
            return rivers;
        }

        private const float DespikeFloor = 0.3f;

        /// <summary>
        /// How much to lower each cell so peaks become broad: the opening (min then max over DespikeRadius) is the ground
        /// with every summit narrower than ~2 x the radius removed; the cut is Despike x (height - opening), smoothed so
        /// the lowered tops are rounded, never below the sea.
        /// </summary>
        private static Grid DespikeCut(Grid c, GenSettings s)
        {
            int n = c.W, r = Math.Max(2, (int)(s.DespikeRadius / CoarseCell));
            float[] open = TerrainProfile.SlideMax(TerrainProfile.SlideMin(c.D, n, c.H, r), n, c.H, r);
            Grid o = new Grid(n, c.H);
            Array.Copy(open, o.D, open.Length);
            o = o.Blur(r * 0.5f); // the opening is made of squares: rounded
            // (only the upper part of tall summits: low hills and small islands keep their shape)
            float lo, hi; c.MinMax(out lo, out hi);
            float floor = s.SeaLevel + DespikeFloor * Math.Max(0, hi - s.SeaLevel);
            Grid cut = new Grid(n, c.H);
            for (int i = 0; i < cut.D.Length; i++)
            {
                float above = c.D[i] - Math.Max(o.D[i], floor);
                cut.D[i] = above > 0 ? s.Despike * above : 0;
            }
            return cut.Blur(1.5f);
        }

        /// <summary>The town centre in metres (TownLayout's offset from the map middle).</summary>
        private static float[] TownCentre(GenSettings s, float worldSize)
        {
            double[] t = TownLayout(s);
            return new[] { (float)(0.5 + t[0]) * worldSize, (float)(0.5 + t[1]) * worldSize };
        }

        /// <summary>
        /// The fork (#139, Pittsburgh's point): a trunk from the town centre to the sea, then two arms from the map edges
        /// that both end on the trunk's head, so the town stands on the point between them. Returns how many of the
        /// world's rivers it used (0 when the town isn't on land or no route is found: the usual rivers instead).
        /// </summary>
        private static int ForkRivers(Grid c, GenSettings s, Random rng, float worldSize, Perlin pn, float hi, List<List<float[]>> rivers, out float[] hint)
        {
            hint = null;
            int n = c.W;
            float[] town = TownCentre(s, worldSize);
            int tx = Math.Max(4, Math.Min(n - 5, (int)(town[0] / CoarseCell))), ty = Math.Max(4, Math.Min(n - 5, (int)(town[1] / CoarseCell)));
            if (c[tx, ty] < s.SeaLevel) return 0;
            List<int[]> trunk = ThroughRiver(c, s.SeaLevel, hi, rng, rivers, s.Buttes > 0, null, ty * n + tx);
            if (trunk == null || trunk.Count < 12) return 0;
            List<float[]> tp = new List<float[]>();
            foreach (int[] q in trunk) tp.Add(new[] { q[0] * CoarseCell, q[1] * CoarseCell });
            List<float[]> trunkM = Meander(Chaikin(Chaikin(tp)), rng, pn, worldSize, Math.Min(1, s.Meander));
            trunkM[0] = new[] { tp[0][0], tp[0][1], 1f }; // full width from the point (CarveChannel)
            List<List<float[]>> made = new List<List<float[]>>();
            made.Add(trunkM);
            // the arms may only end at the trunk's head
            bool[] goal = new bool[n * n];
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++) goal[(ty + dy) * n + tx + dx] = true;
            // each arm stays in its own wedge: ~60 deg either side of straight upstream (away from the trunk), so the two
            // come in from different directions and the point between them is a broad triangle of land
            int near = (int)(ForkNear / CoarseCell);
            int ti = Math.Min(trunk.Count - 1, (int)(300f / CoarseCell));
            double back = Math.Atan2(ty - trunk[ti][1], tx - trunk[ti][0]);
            for (int arm = 0; arm < 2; arm++)
            {
                List<List<float[]>> others = new List<List<float[]>>(rivers); others.AddRange(made);
                double dir = back + (arm == 0 ? 1 : -1) * ForkAngle * Math.PI / 180;
                bool[] block = new bool[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int dx = x - tx, dy = y - ty;
                        if (dx * dx + dy * dy <= near * near) continue;
                        double d = Math.Atan2(dy, dx) - dir;
                        while (d > Math.PI) d -= 2 * Math.PI;
                        while (d < -Math.PI) d += 2 * Math.PI;
                        if (Math.Abs(d) > ForkWedge * Math.PI / 180) block[y * n + x] = true;
                    }
                List<int[]> path = ThroughRiver(c, s.SeaLevel, hi, rng, others, s.Buttes > 0, goal, -1, block);
                if (path == null) break;
                List<float[]> ap = new List<float[]>();
                foreach (int[] q in path) ap.Add(new[] { q[0] * CoarseCell, q[1] * CoarseCell });
                List<float[]> sm = Chaikin(Chaikin(ap));
                sm[0] = ap[0];
                List<float[]> mr = Meander(sm, rng, pn, worldSize, s.Meander);
                mr[mr.Count - 1] = new[] { tp[0][0], tp[0][1] };
                made.Add(mr);
            }
            if (made.Count < 3) return 0; // no point without both arms
            rivers.AddRange(made);
            hint = new[] { tp[0][0], tp[0][1] };
            return 2;
        }

        private const float ForkAngle = 60f, ForkWedge = 32f, ForkNear = 60f;
        private const float IslandLength = 420f, IslandWidth = 70f;

        /// <summary>
        /// A side channel that leaves the river and rejoins it further down, so an island stands between them (EA's
        /// Riverview has one in the middle of town). Placed past the town (or the middle), bowed out to one side.
        /// </summary>
        private static List<float[]> IslandChannel(List<float[]> river, GenSettings s, float worldSize, Grid c)
        {
            int n = river.Count;
            if (n < 20) return null;
            float[] L = new float[n];
            for (int k = 1; k < n; k++) L[k] = L[k - 1] + (float)Math.Sqrt(Sq(river[k][0] - river[k - 1][0]) + Sq(river[k][1] - river[k - 1][1]));
            float total = L[n - 1], len = IslandLength * RiverScale(worldSize);
            if (total < len * 2.5f) return null;
            // start at the river point nearest the town (so the island is in town, like Riverview's), kept clear of the ends
            float[] town = TownCentre(s, worldSize);
            int a = -1; float bd = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (L[k] < total * 0.2f || L[k] > total - len - total * 0.1f) continue;
                float d = Sq(river[k][0] - town[0]) + Sq(river[k][1] - town[1]);
                if (d < bd) { bd = d; a = k; }
            }
            if (a < 0) return null;
            int b = a; while (b < n - 1 && L[b] - L[a] < len) b++;
            // bow out to the side with the lower ground
            float side = 0;
            for (int k = a; k <= b; k++)
            {
                float tx = river[Math.Min(n - 1, k + 1)][0] - river[Math.Max(0, k - 1)][0], ty = river[Math.Min(n - 1, k + 1)][1] - river[Math.Max(0, k - 1)][1];
                float tl = (float)Math.Sqrt(tx * tx + ty * ty) + 1e-3f, nx = -ty / tl, ny = tx / tl, w = IslandWidth;
                side += Sample(c, (river[k][0] - nx * w) / CoarseCell, (river[k][1] - ny * w) / CoarseCell)
                      - Sample(c, (river[k][0] + nx * w) / CoarseCell, (river[k][1] + ny * w) / CoarseCell);
            }
            float sgn = side > 0 ? 1 : -1; // + side is lower
            List<float[]> ch = new List<float[]>();
            ch.Add(new[] { river[a][0], river[a][1], 1f }); // (a third value: a full channel from its first point, CarveChannel)
            for (int k = a + 1; k <= b; k++)
            {
                float t = (L[k] - L[a]) / Math.Max(1, L[b] - L[a]);
                float tx = river[Math.Min(n - 1, k + 1)][0] - river[Math.Max(0, k - 1)][0], ty = river[Math.Min(n - 1, k + 1)][1] - river[Math.Max(0, k - 1)][1];
                float tl = (float)Math.Sqrt(tx * tx + ty * ty) + 1e-3f, nx = -ty / tl, ny = tx / tl;
                // lens shape, pointed at both ends (an eye-shaped island)
                float off = sgn * IslandWidth * RiverScale(worldSize) * (float)Math.Pow(Math.Sin(Math.PI * t), 0.8);
                ch.Add(new[] { river[k][0] + nx * off, river[k][1] + ny * off });
            }
            return ch;
        }

        /// <summary>
        /// The longest low route from the map edge to the sea: one search outwards from the sea (each step costs
        /// 1 + (height above the sea / 5 m)^2, so routes keep to the lowest ground), then the edge cell whose route is
        /// longest without crossing ground more than ~8-15% of the land's height above the sea, followed back down.
        /// Null if there is no sea or no such route (at least ~600 m). Away from other rivers.
        /// </summary>
        private static List<int[]> EdgeRiver(Grid c, float sea, float hi, Random rng, List<List<float[]>> rivers)
        {
            int n = c.W, N = n * n;
            float range = Math.Max(1, hi - sea), maxAbove = Math.Max(8f, 0.12f * range);
            float[] cost = new float[N]; int[] next = new int[N]; float[] peak = new float[N];
            for (int i = 0; i < N; i++) { cost[i] = float.MaxValue; next[i] = -1; }
            SortedDictionary<float, Queue<int>> open = new SortedDictionary<float, Queue<int>>();
            int seaCells = 0;
            for (int i = 0; i < N; i++) if (c.D[i] < sea - 0.5f) { cost[i] = 0; Push(open, 0, i); seaCells++; }
            if (seaCells == 0) return null;
            while (open.Count > 0)
            {
                IEnumerator<KeyValuePair<float, Queue<int>>> it = open.GetEnumerator(); it.MoveNext();
                float key = it.Current.Key; Queue<int> q = it.Current.Value;
                int cur = q.Dequeue(); if (q.Count == 0) open.Remove(key);
                if (key > cost[cur]) continue;
                int cx = cur % n, cy = cur / n;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= n || y >= n) continue;
                        int j = y * n + x;
                        float above = Math.Max(0, c.D[j] - sea);
                        if (above > maxAbove * 1.5f) continue;
                        float step = (dx != 0 && dy != 0 ? 1.414f : 1f) * (1 + Sq(above / 5f));
                        float nc = cost[cur] + step;
                        if (nc < cost[j]) { cost[j] = nc; next[j] = cur; peak[j] = Math.Max(peak[cur], above); Push(open, nc, j); }
                    }
            }
            List<int[]> best = null;
            for (int t = 0; t < 4 * n; t++)
            {
                int side = rng.Next(4), u = n / 10 + rng.Next(n * 8 / 10);
                int x = side == 0 ? 0 : side == 1 ? n - 1 : u, y = side == 2 ? 0 : side == 3 ? n - 1 : u;
                int i = y * n + x;
                if (next[i] < 0 || c.D[i] < sea + 1 || peak[i] > maxAbove) continue;
                List<int[]> path = new List<int[]>();
                for (int k = i, guard = 0; k >= 0 && guard < N; k = next[k], guard++)
                {
                    path.Add(new[] { k % n, k / n });
                    if (c.D[k] < sea - 0.5f) break;
                }
                bool near = false;
                foreach (List<float[]> r in rivers)
                {
                    foreach (int[] q in path)
                    {
                        foreach (float[] p in r) if (Sq(p[0] / CoarseCell - q[0]) + Sq(p[1] / CoarseCell - q[1]) < Sq(n / 8f)) { near = true; break; }
                        if (near) break;
                    }
                    if (near) break;
                }
                if (near) continue;
                if (path.Count * CoarseCell >= 600 && (best == null || path.Count > best.Count)) best = path;
            }
            return best;
        }

        /// <summary>
        /// A river all the way through: one search outwards from where it may end (the sea, an earlier river's course, or
        /// with neither one whole map edge chosen at random), each step costing 1 + (height above the sea / h)^2 with h
        /// ~6% of the land's height, so the route keeps to the lowest ground and cuts through higher land only where it
        /// must; ground over 45% of the land's height is never crossed. The start is the map edge cell (away from water,
        /// other rivers' ends and the end edge) with the longest, lowest route; the course runs from it to the end.
        /// Null if no route of at least a quarter of the map's size exists.
        /// </summary>
        private const float RiverEndLakeM2 = 25000; // a lake a river may end in: at least 2.5 ha

        /// <summary>Cells below 'level' that belong to the sea (joined to the map edge) or to a water body of at least minCells.</summary>
        private static bool[] BigWater(Grid g, float level, int minCells)
        {
            int n = g.W, N = g.D.Length;
            bool[] big = new bool[N], seen = new bool[N];
            List<int> comp = new List<int>(); Stack<int> st = new Stack<int>();
            for (int s0 = 0; s0 < N; s0++)
            {
                if (seen[s0] || g.D[s0] >= level) continue;
                comp.Clear(); bool edge = false; seen[s0] = true; st.Push(s0);
                while (st.Count > 0)
                {
                    int i = st.Pop(), x = i % n, y = i / n; comp.Add(i);
                    if (x == 0 || y == 0 || x == n - 1 || y == g.H - 1) edge = true;
                    if (x > 0 && !seen[i - 1] && g.D[i - 1] < level) { seen[i - 1] = true; st.Push(i - 1); }
                    if (x < n - 1 && !seen[i + 1] && g.D[i + 1] < level) { seen[i + 1] = true; st.Push(i + 1); }
                    if (y > 0 && !seen[i - n] && g.D[i - n] < level) { seen[i - n] = true; st.Push(i - n); }
                    if (y < g.H - 1 && !seen[i + n] && g.D[i + n] < level) { seen[i + n] = true; st.Push(i + n); }
                }
                if (edge || comp.Count >= minCells) foreach (int i in comp) big[i] = true;
            }
            return big;
        }

        /// <summary>
        /// The first valley's centre line (as ValleyLand lays it: straight inland from the town centre, swinging by the
        /// same sine and noise), in metres, from where it leaves the map inland to where it reaches the sea on the
        /// planning grid. Null if it never reaches the sea within the map.
        /// </summary>
        private static List<float[]> ValleyAxis(Grid c, GenSettings s, float worldSize, Perlin pn)
        {
            double a = CoastAngle(s, new Random(s.Seed * 7919 + 13));
            float ax = (float)Math.Cos(a), ay = (float)Math.Sin(a);
            float[] town = TownCentre(s, worldSize);
            float phase = (float)(new Random(s.Seed * 9133 + 5).NextDouble() * 6.28); // ValleyLand's first draw
            float amp = s.ValleyWidth * 0.8f;
            List<float[]> pts = new List<float[]>();
            bool sea = false;
            for (float t = worldSize * 1.5f; t > -worldSize * 1.5f; t -= 8)
            {
                float off = amp * (float)Math.Sin(t / 900f * 6.2832f + phase) + pn.Fbm(t / 300f + 5100, 1.7f, 2) * amp * 0.6f;
                float across = off * Smooth(0, 200, Math.Abs(t));
                float x = town[0] + t * ax - across * ay, y = town[1] + t * ay + across * ax;
                bool inside = x >= 0 && y >= 0 && x <= worldSize && y <= worldSize;
                if (!inside) { if (pts.Count > 0) break; continue; } // before the inland edge: keep walking; after: done
                pts.Add(new[] { x, y });
                if (Sample(c, x / CoarseCell, y / CoarseCell) < s.SeaLevel - 1 && t < 0) { sea = true; break; }
            }
            return sea && pts.Count > 20 ? pts : null;
        }

        private static List<int[]> ThroughRiver(Grid c, float sea, float hi, Random rng, List<List<float[]>> rivers, bool seaOnly = false, bool[] onlyGoal = null, int forcedStart = -1, bool[] blocked = null)
        {
            int n = c.W, N = n * n;
            float range = Math.Max(1, hi - sea), hs = Math.Max(6f, 0.06f * range), cap = 0.45f * range;
            float[] cost = new float[N]; int[] next = new int[N]; float[] peak = new float[N];
            for (int i = 0; i < N; i++) { cost[i] = float.MaxValue; next[i] = -1; }
            Heap open = new Heap(n * 8);
            bool[] goal = new bool[N];
            int goals = 0;
            // (seaOnly: a recipe's inland lakes are not river ends, and the river keeps out of them, so they stay lakes)
            bool[] seaW = seaOnly ? EdgeWater(c, sea) : null;
            // (only the sea or a real lake: a river used to end in a pocket a few cells across that is below the sea on
            // this 8 m grid but dry land on the full map, so it stopped short in the middle of the land, #139)
            bool[] bigW = seaOnly ? seaW : BigWater(c, sea - 0.5f, (int)(RiverEndLakeM2 / (CoarseCell * CoarseCell)));
            if (onlyGoal != null) { for (int i = 0; i < N; i++) if (onlyGoal[i]) { goal[i] = true; goals++; } }
            else for (int i = 0; i < N; i++) if (bigW[i]) { goal[i] = true; goals++; }
            if (onlyGoal == null)
            foreach (List<float[]> r in rivers)
                foreach (float[] p in r)
                {
                    int x = (int)Math.Round(p[0] / CoarseCell), y = (int)Math.Round(p[1] / CoarseCell);
                    if (x >= 0 && y >= 0 && x < n && y < n && !goal[y * n + x]) { goal[y * n + x] = true; goals++; }
                }
            int endSide = -1;
            if (goals == 0)
            {
                endSide = rng.Next(4);
                for (int u = 0; u < n; u++)
                {
                    int x = endSide == 0 ? 0 : endSide == 1 ? n - 1 : u, y = endSide == 2 ? 0 : endSide == 3 ? n - 1 : u;
                    goal[y * n + x] = true;
                }
            }
            for (int i = 0; i < N; i++) if (goal[i]) { cost[i] = 0; open.Push(0, i); }
            while (open.Count > 0)
            {
                int cur = open.Pop();
                int cx = cur % n, cy = cur / n;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= n || y >= n) continue;
                        int j = y * n + x;
                        if (goal[j]) continue;
                        if (seaW != null && c.D[j] < sea - 0.5f) continue; // keeps out of the inland lakes
                        if (blocked != null && blocked[j]) continue;
                        float above = Math.Max(0, c.D[j] - sea);
                        if (above > cap) continue;
                        float nc = cost[cur] + (dx != 0 && dy != 0 ? 1.414f : 1f) * (1 + Sq(above / hs));
                        if (nc < cost[j]) { cost[j] = nc; next[j] = cur; peak[j] = Math.Max(peak[cur], above); open.Push(nc, j); }
                    }
            }
            // the start: a map edge cell on dry land, not on the end edge, away from other rivers
            List<int[]> best = null; float bestScore = float.MinValue;
            if (forcedStart >= 0) { if (next[forcedStart] < 0) return null; best = new List<int[]>(); best.Add(new[] { forcedStart, 0 }); }
            for (int side = 0; side < 4 && forcedStart < 0; side++)
            {
                if (side == endSide) continue;
                for (int u = 2; u < n - 2; u += 2)
                {
                    int x = side == 0 ? 0 : side == 1 ? n - 1 : u, y = side == 2 ? 0 : side == 3 ? n - 1 : u;
                    int i = y * n + x;
                    if (next[i] < 0 || c.D[i] < sea + 1) continue;
                    // no water along the edge next to the start (a river doesn't start beside the sea)
                    bool wet = false;
                    for (int d = -6; d <= 6 && !wet; d++)
                    {
                        int xx = side < 2 ? x : Math.Max(0, Math.Min(n - 1, x + d)), yy = side < 2 ? Math.Max(0, Math.Min(n - 1, y + d)) : y;
                        if (c[xx, yy] < sea) wet = true;
                    }
                    if (wet) continue;
                    bool near = false;
                    foreach (List<float[]> r in rivers)
                    {
                        float[] p = r[0];
                        if (Sq(p[0] / CoarseCell - x) + Sq(p[1] / CoarseCell - y) < Sq(n / 4f)) { near = true; break; }
                    }
                    if (near) continue;
                    int len = 0;
                    for (int k2 = i; k2 >= 0 && !goal[k2] && len < N; k2 = next[k2]) len++;
                    if (len < n / 4) continue;
                    float score = len * (1 - 0.8f * peak[i] / cap) * (0.85f + 0.3f * (float)rng.NextDouble());
                    if (score > bestScore) { bestScore = score; best = new List<int[]>(); best.Add(new[] { i, len }); }
                }
            }
            if (Environment.GetEnvironmentVariable("CAWDBG") != null) Console.Error.WriteLine("DBG through river: goals " + goals + " endSide " + endSide + " -> " + (best == null ? "none" : "start " + (best[0][0] % n) + "," + (best[0][0] / n) + " len " + best[0][1] + " cells"));
            if (best == null) return null;
            List<int[]> path = new List<int[]>();
            int s0 = best[0][0];
            for (int k2 = s0, guard = 0; k2 >= 0 && guard < N; k2 = next[k2], guard++)
            {
                path.Add(new[] { k2 % n, k2 / n });
                if (goal[k2]) break;
            }
            return path;
        }

        /// <summary>Steepest descent on the pit-filled surface from (sx, sy) to the sea or the map edge.</summary>
        private static List<int[]> Descend(Grid c, Grid filled, float sea, int sx, int sy, out bool toSea)
        {
            int n = c.W, cx = sx, cy = sy, guard = 0;
            List<int[]> path = new List<int[]>();
            toSea = false;
            while (guard++ < n * 6)
            {
                path.Add(new[] { cx, cy });
                if (c[cx, cy] < sea - 0.5f) { toSea = true; break; }
                if (cx == 0 || cy == 0 || cx == n - 1 || cy == n - 1) break;
                int bx = -1, by = -1; float bestDrop = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= n || y >= n) continue;
                        float drop = (filled[cx, cy] - filled[x, y]) / ((dx != 0 && dy != 0) ? 1.414f : 1f);
                        if (drop > bestDrop) { bestDrop = drop; bx = x; by = y; }
                    }
                if (bx < 0) break;
                cx = bx; cy = by;
            }
            return path;
        }

        /// <summary>Fills pits so every cell drains to the sea or the edge (Barnes' priority flood, epsilon variant).</summary>
        private static Grid PriorityFlood(Grid g, float sea)
        {
            int n = g.W;
            Grid f = g.Clone();
            bool[] done = new bool[n * n];
            Heap open = new Heap(n * 4);
            for (int i = 0; i < n * n; i++)
            {
                int x = i % n, y = i / n;
                if (x == 0 || y == 0 || x == n - 1 || y == n - 1 || g.D[i] < sea) { open.Push(f.D[i], i); done[i] = true; }
            }
            while (open.Count > 0)
            {
                int c = open.Pop();
                int cx = c % n, cy = c / n;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= n || y >= n) continue;
                        int k = y * n + x;
                        if (done[k]) continue;
                        done[k] = true;
                        f.D[k] = Math.Max(f.D[k], f.D[c] + 0.001f);
                        open.Push(f.D[k], k);
                    }
            }
            return f;
        }

        /// <summary>Binary min-heap of (key, cell); equal keys come out in no fixed order.</summary>
        private sealed class Heap
        {
            private float[] k; private int[] v; public int Count;
            public Heap(int cap) { k = new float[Math.Max(16, cap)]; v = new int[k.Length]; }
            public void Push(float key, int val)
            {
                if (Count == k.Length) { Array.Resize(ref k, Count * 2); Array.Resize(ref v, Count * 2); }
                int i = Count++;
                while (i > 0) { int p = (i - 1) >> 1; if (k[p] <= key) break; k[i] = k[p]; v[i] = v[p]; i = p; }
                k[i] = key; v[i] = val;
            }
            public int Pop()
            {
                int top = v[0]; Count--;
                float key = k[Count]; int val = v[Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1; if (c >= Count) break;
                    if (c + 1 < Count && k[c + 1] < k[c]) c++;
                    if (key <= k[c]) break;
                    k[i] = k[c]; v[i] = v[c]; i = c;
                }
                k[i] = key; v[i] = val;
                return top;
            }
        }

        private static void Push(SortedDictionary<float, Queue<int>> open, float key, int v)
        {
            Queue<int> q;
            if (!open.TryGetValue(key, out q)) { q = new Queue<int>(); open[key] = q; }
            q.Enqueue(v);
        }

        /// <summary>Irregular sideways wander (noise, growing downstream) so rivers don't follow the 8 m grid or a regular wave.</summary>
        private static List<float[]> Meander(List<float[]> p, Random rng, Perlin pn, float worldSize, float strength)
        {
            float seed = (float)rng.NextDouble() * 100, scale = RiverScale(worldSize);
            List<float[]> o = new List<float[]>();
            float along = 0, total = 0;
            for (int i = 1; i < p.Count; i++) total += (float)Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
            for (int i = 0; i < p.Count; i++)
            {
                if (i > 0) along += (float)Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
                int a = Math.Max(0, i - 2), b = Math.Min(p.Count - 1, i + 2);
                float tx = p[b][0] - p[a][0], ty = p[b][1] - p[a][1], tl = (float)Math.Sqrt(tx * tx + ty * ty) + 1e-3f;
                float amp = (8 + 22 * along / Math.Max(1, total)) * scale * Math.Min(1, along / 120f) * Math.Min(1, (total - along) / 60f + 0.3f);
                // stronger meanders: bigger and longer bends (the wavelength grows with the square root, so bends stay
                // round instead of folding back on themselves)
                float off = pn.Fbm(along / 210f + seed, seed * 0.37f, 3) * 2.2f * amp * Math.Min(1, strength);
                o.Add(new[] { p[i][0] - ty / tl * off, p[i][1] + tx / tl * off });
            }
            return strength > 1 ? Wind(o, strength, seed, pn, worldSize) : o;
        }

        /// <summary>
        /// Winding river (#144): regular looping bends of a sine-generated curve laid along the river's smoothed line.
        /// Amplitude 25 m per unit of strength over 1 (x world scale), wavelength ~6.5x the amplitude, varied by noise; the
        /// ends (map edge, mouth, a fork) stay where they are.
        /// </summary>
        private static List<float[]> Wind(List<float[]> p, float strength, float seed, Perlin pn, float worldSize)
        {
            int n = p.Count;
            if (n < 8) return p;
            float amp = 25f * (strength - 1) * RiverScale(worldSize), wave = 6.5f * amp + 120f;
            float[] L = new float[n];
            for (int i = 1; i < n; i++) L[i] = L[i - 1] + (float)Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
            float total = L[n - 1];
            if (total < wave * 1.5f) return p;
            // the base line, smoothed over half a wavelength so its normals turn slowly (the bends can't fold over)
            float[] bx = new float[n], by = new float[n];
            int j0 = 0, j1 = 0; float sx = 0, sy = 0;
            for (int i = 0; i < n; i++)
            {
                while (j1 < n && L[j1] <= L[i] + wave / 4) { sx += p[j1][0]; sy += p[j1][1]; j1++; }
                while (L[j0] < L[i] - wave / 4) { sx -= p[j0][0]; sy -= p[j0][1]; j0++; }
                bx[i] = sx / (j1 - j0); by[i] = sy / (j1 - j0);
            }
            List<float[]> o = new List<float[]>();
            float phase = seed * 3.1f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) phase += (L[i] - L[i - 1]) / wave * 6.2832f * (0.75f + 0.5f * (pn.Fbm(L[i] / 900f + seed, 3.7f, 2) + 0.5f));
                int a = Math.Max(0, i - 3), b = Math.Min(n - 1, i + 3);
                float tx = bx[b] - bx[a], ty = by[b] - by[a], tl = (float)Math.Sqrt(tx * tx + ty * ty) + 1e-3f;
                float fade = Smooth(0, 1, Math.Min(L[i], total - L[i]) / Math.Max(60f, wave * 0.6f));
                float a2 = amp * fade * (0.7f + 0.6f * (pn.Fbm(L[i] / 600f + seed + 9, 1.3f, 2) + 0.5f));
                float off = a2 * (float)Math.Sin(phase);
                // the river's own small wiggles (p - base) are kept, the bends added across the base line
                o.Add(new[] { p[i][0] - ty / tl * off, p[i][1] + tx / tl * off });
            }
            return o;
        }

        /// <summary>Rivers narrower in small worlds (full size from 1536 m up).</summary>
        private static float RiverScale(float worldSize) { return Math.Max(0.55f, Math.Min(1.15f, (float)Math.Sqrt(worldSize / 1536f))); }

        // valley walls beyond the floodplain: how far (m) and how steep (rise per m). 110 m at 0.30 (~17 deg) cut a trench
        // through every hill a river crosses (the tester, #139: "carved with a spoon")
        public static float Wall = 110f, WallSlope = 0.45f, PlainWidth = 1f; // 0.45: as close to EA as the old 0.30 (seeds 7-12)
        private const float WallMax = 700f;
        // channel half-width at the source (x3 at the mouth) and bank run per metre of rise: EA's river water is 20-60 m
        // wide; wider than ~60 m it reads as sea (tools/Fidelity: Riverview's generated rivers measured as sea)
        public static float RiverHalf = 2f, RiverBankRun = 3f; // swept on 4 river worlds (seeds 7-9)

        /// <summary>
        /// Carves a river into its valley: a gully at the source that deepens until the bed drops below sea level (where
        /// Sims 3 shows water), a channel that widens downstream with an uneven width and ragged edges, a steep bank on
        /// the outside of bends and a gently shelving one (point bar) inside, and a flat floodplain alongside that
        /// widens towards the mouth. Ground is only ever lowered.
        /// </summary>
        private static void CarveChannel(Grid g, float cell, List<float[]> river, GenSettings s, float worldSize, Perlin pn)
        {
            int n = river.Count;
            if (n < 3) return;
            float sea = s.SeaLevel, scale = RiverScale(worldSize);
            float[] L = new float[n], half = new float[n], bed = new float[n], bank = new float[n], plain = new float[n], plainH = new float[n], curve = new float[n], wallR = new float[n];
            for (int k = 1; k < n; k++) L[k] = L[k - 1] + (float)Math.Sqrt(Sq(river[k][0] - river[k - 1][0]) + Sq(river[k][1] - river[k - 1][1]));
            float total = Math.Max(1, L[n - 1]);
            float edgeM = (g.W - 1) * cell;
            bool fromEdge = river[0].Length > 2 || river[0][0] < 3 * CoarseCell || river[0][1] < 3 * CoarseCell || river[0][0] > edgeM - 3 * CoarseCell || river[0][1] > edgeM - 3 * CoarseCell;
            // ground along the river, never rising downstream
            float[] prof = new float[n];
            for (int k = 0; k < n; k++)
            {
                prof[k] = Sample(g, river[k][0] / cell, river[k][1] / cell);
                if (k > 0) prof[k] = Math.Min(prof[k], prof[k - 1]);
            }
            for (int k = 0; k < n; k++)
            {
                float t = L[k] / total;
                float ground = Sample(g, river[k][0] / cell, river[k][1] / cell);
                // rivers that come in from beyond the map edge are full rivers from the start; others begin as a stream
                float head = fromEdge ? 1 : Smooth(0, Math.Min(300, total * 0.25f), L[k]);
                // channel half-width 4-12 m (x world scale, x River width): with its banks the water is ~20-60 m wide,
                // as EA's rivers measure (tools/Fidelity: Riverview 2.8 % of the map is water under 60 m wide)
                half[k] = scale * s.RiverWidth * RiverHalf * (1 + 2 * t) * (1 + 0.35f * pn.Fbm(L[k] / 90f + 31, 7.7f, 2)) * (0.5f + 0.5f * head);
                // the bed: a stream just under the ground at the source, descending steadily over ~300 m to below
                // sea level (where Sims 3 shows water), then staying there
                bed[k] = Math.Max(sea - 1.8f, Lerp(prof[k] - 0.6f, sea - 1.8f, head));
                bed[k] += pn.Fbm(L[k] / 40f + 57, 3.3f, 2) * 0.35f * head;
                plain[k] = scale * (6 + 70 * t * t) * head * PlainWidth;
                plainH[k] = Math.Max(sea + 0.6f + 1.2f * (1 - t), bed[k] + 2.2f);
                bank[k] = Math.Max(6, (plainH[k] - bed[k]) * RiverBankRun) * scale; // low, shelving banks from the water up to the floodplain
                // signed curvature (left bend > 0) from neighbours 3 apart
                int a = Math.Max(0, k - 3), b = Math.Min(n - 1, k + 3);
                float ax = river[k][0] - river[a][0], ay = river[k][1] - river[a][1], bx = river[b][0] - river[k][0], by = river[b][1] - river[k][1];
                float la = (float)Math.Sqrt(ax * ax + ay * ay) + 1e-3f, lb = (float)Math.Sqrt(bx * bx + by * by) + 1e-3f;
                curve[k] = (ax * by - ay * bx) / (la * lb);
                // valley wall reach: wide enough that the wall climbs to the land beside the river at WallSlope. Where a
                // river crosses high ground it is cut down to the sea (Sims 3 shows water only there), and a fixed 110 m
                // reach ended the cut in a cliff tens of metres high (#139, #144: "carved with a spoon")
                float nx = -(bx + ax) / (la + lb), ny = (by + ay) / (la + lb), hmax = ground;
                for (int q = 40; q <= 360; q += 40)
                {
                    hmax = Math.Max(hmax, Sample(g, (river[k][0] + ny * q) / cell, (river[k][1] + nx * q) / cell));
                    hmax = Math.Max(hmax, Sample(g, (river[k][0] - ny * q) / cell, (river[k][1] - nx * q) / cell));
                }
                wallR[k] = Math.Max(Wall, Math.Min(WallMax, (hmax - plainH[k]) / WallSlope * 1.25f + Wall * 0.3f));
            }
            // smooth the profile along the river so nothing jumps between neighbouring points (radial streaks)
            float[][] arrays = { half, bed, bank, plain, plainH, curve, wallR };
            foreach (float[] arr in arrays) SmoothAlong(arr, 6, 2);
            // every segment carves its own valley shape; each pixel takes the lowest of them (a union with no seams)
            float reach = 0;
            for (int k = 0; k < n; k++) reach = Math.Max(reach, half[k] * 1.4f + bank[k] * 1.6f + plain[k] + wallR[k]);
            int W = g.W, H = g.H;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (float[] p in river) { minX = Math.Min(minX, p[0]); maxX = Math.Max(maxX, p[0]); minY = Math.Min(minY, p[1]); maxY = Math.Max(maxY, p[1]); }
            int bx0 = Math.Max(0, (int)((minX - reach) / cell)), bx1 = Math.Min(W - 1, (int)((maxX + reach) / cell) + 1);
            int by0 = Math.Max(0, (int)((minY - reach) / cell)), by1 = Math.Min(H - 1, (int)((maxY + reach) / cell) + 1);
            int bw = bx1 - bx0 + 1, bh = by1 - by0 + 1;
            float[] target = new float[bw * bh], ragged = new float[bw * bh], terraceN = new float[bw * bh], wallN = new float[bw * bh], reachRatio = new float[bw * bh];
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int bi = (y - by0) * bw + (x - bx0);
                    float wx = x * cell, wy = y * cell;
                    target[bi] = float.MaxValue; reachRatio[bi] = float.MaxValue;
                    ragged[bi] = pn.Fbm(wx / 38f + 11, wy / 38f + 11, 2);
                    terraceN[bi] = Math.Max(0, pn.Fbm(wx / 60f + 23, wy / 60f + 23, 2)) * 0.8f;
                    wallN[bi] = 1 + 0.35f * pn.Fbm(wx / 80f + 41, wy / 80f + 41, 2);
                }
            int step = Math.Max(1, (int)(4f / Math.Max(0.5f, total / Math.Max(1, n - 1)))); // segments ~4 m long
            for (int k = 0; k < n - 1; k += step)
            {
                int k2 = Math.Min(n - 1, k + step);
                float ax = river[k][0], ay = river[k][1], dx = river[k2][0] - ax, dy = river[k2][1] - ay;
                float l2 = dx * dx + dy * dy;
                if (l2 < 1e-6f) continue;
                float r = Math.Max(half[k], half[k2]) * 1.4f + Math.Max(bank[k], bank[k2]) * 1.6f + Math.Max(plain[k], plain[k2]) + Math.Max(wallR[k], wallR[k2]);
                int x0 = Math.Max(bx0, (int)((Math.Min(ax, ax + dx) - r) / cell)), x1 = Math.Min(bx1, (int)((Math.Max(ax, ax + dx) + r) / cell) + 1);
                int y0 = Math.Max(by0, (int)((Math.Min(ay, ay + dy) - r) / cell)), y1 = Math.Min(by1, (int)((Math.Max(ay, ay + dy) + r) / cell) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x * cell - ax, py = y * cell - ay;
                        float u = Math.Max(0, Math.Min(1, (px * dx + py * dy) / l2));
                        float qx = px - u * dx, qy = py - u * dy, dist = (float)Math.Sqrt(qx * qx + qy * qy);
                        if (dist > r) continue;
                        float rr = dist / r;
                        int bi = (y - by0) * bw + (x - bx0);
                        float hw = Lerp(half[k], half[k2], u), bd = Lerp(bed[k], bed[k2], u), bk = Lerp(bank[k], bank[k2], u);
                        float pl = Lerp(plain[k], plain[k2], u), ph = Lerp(plainH[k], plainH[k2], u), cv = Lerp(curve[k], curve[k2], u);
                        float d = dist + ragged[bi] * hw * 0.3f;                       // ragged edges
                        // outside of a bend: steep cut bank; inside: gentle point bar
                        float outer = Math.Max(-1, Math.Min(1, -Math.Sign(dx * py - dy * px) * cv * 4));
                        float bankW = bk * (outer > 0 ? 1 - 0.45f * outer : 1 - 0.6f * outer);
                        float t;
                        if (d <= hw) t = bd;
                        else if (d <= hw + bankW)
                        {
                            float v = (d - hw) / bankW;
                            float shape = outer > 0 ? (float)Math.Pow(Smooth(0, 1, v), 0.6) : (float)Math.Pow(Smooth(0, 1, v), 1 + 0.8f * -outer);
                            t = bd + (Math.Max(ph, bd + 0.5f) - bd) * shape;
                        }
                        else
                        {
                            // flat floodplain, then valley walls rising ~19° (uneven) to meet the land
                            // the floodplain rises gently (3%) and curves into the valley wall over ~20 m
                            float across = d - hw - bankW, terrace = ph + terraceN[bi] + across * 0.03f;
                            float beyond = across - pl;
                            float ease = Wall * 0.55f; // eased in: no crease at the valley's foot
                            t = terrace + (beyond <= 0 ? 0 : (beyond < ease ? beyond * beyond / (2 * ease) : beyond - ease / 2)) * WallSlope * wallN[bi];
                        }
                        if (t < target[bi]) target[bi] = t;
                        if (rr < reachRatio[bi]) reachRatio[bi] = rr;
                    }
            }
            // depth to remove (soft minimum, so the valley blends into the land without a crease), lightly blurred
            // to hide the facets where neighbouring segments' shapes meet
            Grid cut = new Grid(bw, bh);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int bi = (y - by0) * bw + (x - bx0);
                    if (target[bi] == float.MaxValue) continue;
                    float ground = g[x, y];
                    // the soft minimum dips up to 1.7 m below both surfaces where they meet; on the floodplain (0.6 m
                    // above the sea) that flooded a band either side of the river, so ground the shape keeps dry stays dry
                    float lowest = target[bi] >= sea ? Math.Min(ground, sea + 0.3f) : float.MinValue;
                    cut.D[bi] = Math.Max(0, ground - Math.Max(lowest, SoftMin(ground, target[bi], 5f))); // 5 m: a wide, soft join with the land
                }
            Grid soft = cut.Blur(6f / cell);
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int bi = (y - by0) * bw + (x - bx0);
                    // keep the full depth in the water so the channel stays below sea level
                    float c2 = target[bi] < sea ? Math.Max(cut.D[bi], soft.D[bi]) : soft.D[bi];
                    // fade the cut out over the outer quarter of the carving reach, so the valley has no step at its rim
                    if (target[bi] >= sea && reachRatio[bi] < float.MaxValue) c2 *= 1 - Smooth(0.75f, 1f, reachRatio[bi]);
                    g[x, y] -= c2;
                }
        }

        /// <summary>Moving average over ±r points, repeated.</summary>
        private static void SmoothAlong(float[] a, int r, int passes)
        {
            float[] t = new float[a.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < a.Length; i++)
                {
                    float sum = 0; int cnt = 0;
                    for (int j = Math.Max(0, i - r); j <= Math.Min(a.Length - 1, i + r); j++) { sum += a[j]; cnt++; }
                    t[i] = sum / cnt;
                }
                Array.Copy(t, a, a.Length);
            }
        }

        private static float SoftMin(float a, float b, float k)
        {
            float m = Math.Min(a, b);
            return m - k * (float)Math.Log(Math.Exp(-(a - m) / k) + Math.Exp(-(b - m) / k));
        }

        // ---------- valleys ----------

        /// <summary>
        /// Drainage erosion: every cell drains to its lowest neighbour (on the pit-filled surface) and is cut down in
        /// proportion to the area draining through it, so streams form branching valleys that deepen downstream.
        /// Returns the depth to remove, smoothed into V/U-shaped valleys; land never drops to sea level this way.
        /// </summary>
        private static Grid ValleyCut(Grid c, Grid flat, GenSettings s)
        {
            int n = c.W, N = n * n;
            Grid cut = new Grid(n, n);
            float k = s.Erosion / 100f;
            if (k <= 0) return cut;
            // flow spreads to every lower neighbour in proportion to slope^1.5 (multiple-flow), on the pit-filled surface
            // lightly smoothed, so drainage branches naturally instead of running in straight grid lines over flats
            Grid cb = c.Blur(1f), f = PriorityFlood(cb, s.SeaLevel);
            int[] order = new int[N]; float[] keys = new float[N];
            for (int i = 0; i < N; i++) { order[i] = i; keys[i] = -f.D[i]; }
            Array.Sort(keys, order); // highest first
            float[] acc = new float[N];
            for (int i = 0; i < N; i++) acc[i] = 1;
            float[] w = new float[8]; int[] to = new int[8];
            foreach (int i in order)
            {
                int cx = i % n, cy = i / n, m = 0; float sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= n || y >= n) continue;
                        int j = y * n + x;
                        float drop = (f.D[i] - f.D[j]) / (dx != 0 && dy != 0 ? 1.41421f : 1f);
                        if (drop <= 0) continue;
                        float wt = (float)Math.Pow(drop, 1.5); w[m] = wt; to[m] = j; m++; sum += wt;
                    }
                for (int q = 0; q < m; q++) acc[to[q]] += acc[i] * w[q] / sum;
            }
            // local relief: hills get deep V valleys, lowlands broad shallow ones, flat zones (towns, farms) are spared
            Grid slope = new Grid(n, n);
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    float gx = (c[x + 1, y] - c[x - 1, y]) / (2 * CoarseCell), gy = (c[x, y + 1] - c[x, y - 1]) / (2 * CoarseCell);
                    slope[x, y] = (float)Math.Sqrt(gx * gx + gy * gy);
                }
            slope = slope.Blur(60f / CoarseCell);
            for (int i = 0; i < N; i++)
            {
                float above = c.D[i] - s.SeaLevel;
                if (above < 0.5f) continue;
                if (f.D[i] - cb.D[i] > 0.4f) continue; // inside a filled hollow the flow runs in straight lines: no valley
                float d = k * 3f * Math.Max(0, (float)Math.Pow(acc[i], 0.4) - 3f); // only real streams (~16+ cells draining)
                float relief = 2 + slope.D[i] * 110;  // ~5 m deep on gentle land, up to 30 m in the hills
                cut.D[i] = Math.Min(d, Math.Min(Math.Min(30f * k, relief), above * 0.7f)) * (1 - 0.75f * flat.D[i]);
            }
            // valley sides: each cut spreads sideways, losing 0.12 m per metre on gentle land up to 0.45 (~24°) in hills
            float[] fallC = new float[N];
            for (int i = 0; i < N; i++) fallC[i] = Math.Max(0.12f, Math.Min(0.45f, slope.D[i] * 2.5f)) * CoarseCell;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int i = y * n + x; float v = cut.D[i], fall = fallC[i], fallD = fall * 1.41421f;
                        if (x > 0) v = Math.Max(v, cut.D[i - 1] - fall);
                        if (y > 0) { v = Math.Max(v, cut.D[i - n] - fall); if (x > 0) v = Math.Max(v, cut.D[i - n - 1] - fallD); if (x < n - 1) v = Math.Max(v, cut.D[i - n + 1] - fallD); }
                        cut.D[i] = v;
                    }
                for (int y = n - 1; y >= 0; y--)
                    for (int x = n - 1; x >= 0; x--)
                    {
                        int i = y * n + x; float v = cut.D[i], fall = fallC[i], fallD = fall * 1.41421f;
                        if (x < n - 1) v = Math.Max(v, cut.D[i + 1] - fall);
                        if (y < n - 1) { v = Math.Max(v, cut.D[i + n] - fall); if (x < n - 1) v = Math.Max(v, cut.D[i + n + 1] - fallD); if (x > 0) v = Math.Max(v, cut.D[i + n - 1] - fallD); }
                        cut.D[i] = v;
                    }
            }
            // never below the sea-level margin where the spread reaches low ground
            for (int i = 0; i < N; i++) cut.D[i] = Math.Min(cut.D[i], Math.Max(0, (c.D[i] - s.SeaLevel) * 0.7f));
            return cut.Blur(0.8f);
        }

        // ---------- coasts ----------

        /// <summary>
        /// Natural shores. Along the coast a noise field mixes beaches (a low shelf 25-60 m wide and shallow water),
        /// cliffs (where the land behind is high: a steep rise and deep water close in) and rocky shores (broken edges,
        /// reefs and the odd rock or islet offshore). Works on any grid from metres, so preview and full map agree.
        /// </summary>
        private static void ShapeCoast(Grid g, float cell, GenSettings s, Perlin pn)
        {
            int W = g.W, H = g.H;
            float sea = s.SeaLevel;
            float[] toWater = Distance(g, sea, true, cell), toLand = Distance(g, sea, false, cell);
            Grid behind = g.Blur(45f / cell);
            // how far inland the land around here reaches (largest distance to the water within ~200 m), so small
            // islands get narrow beaches instead of being flattened into a shelf (Isla Paradiso, Sunlit Tides)
            int B = Math.Max(1, (int)(16 / cell)), bw = (W + B - 1) / B, bh = (H + B - 1) / B, rb = (int)Math.Ceiling(200 / (B * cell));
            float[] blk = new float[bw * bh], reach = new float[bw * bh];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x, b = (y / B) * bw + x / B;
                    if (g.D[i] >= sea && toWater[i] > blk[b]) blk[b] = toWater[i];
                }
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    float m = 0;
                    for (int yy = Math.Max(0, by - rb); yy <= Math.Min(bh - 1, by + rb); yy++)
                        for (int xx = Math.Max(0, bx - rb); xx <= Math.Min(bw - 1, bx + rb); xx++) m = Math.Max(m, blk[yy * bw + xx]);
                    reach[by * bw + bx] = m;
                }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    bool land = g.D[i] >= sea;
                    float D = land ? toWater[i] : -toLand[i];
                    if (D > 90 || D < -140) continue;
                    float wx = x * cell, wy = y * cell;
                    float type = pn.Fbm(wx / 420f + 900, wy / 420f + 900, 3) * 0.9f + 0.5f;
                    if (s.Beaches < 1)
                    {
                        // fewer beaches, but the shore nearest the town stays one (a beach the town can reach)
                        float ws = (W - 1) * cell;
                        float[] tc = TownCentre(s, ws);
                        float near = 1 - Smooth(0.12f * ws, 0.22f * ws, (float)Math.Sqrt(Sq(wx - tc[0]) + Sq(wy - tc[1])));
                        type += (1 - s.Beaches) * 0.35f * (1 - near) - 0.4f * near * (1 - s.Beaches);
                    }
                    float high = Smooth(5, 22, behind.D[i] - sea);
                    float cliff = Smooth(0.5f, 0.68f, type) * high;
                    float beach = (1 - Smooth(0.32f, 0.5f, type)) * (1 - 0.6f * high);
                    float rocky = Math.Max(0, 1 - cliff - beach);
                    float h = g.D[i], nh;
                    float rock = pn.Ridged(wx / 24f + 970, wy / 24f + 970, 2);
                    if (land)
                    {
                        float bwid = 25 + 35 * (pn.Fbm(wx / 150f + 950, wy / 150f + 950, 2) * 0.5f + 0.5f);
                        bwid = Math.Max(4, Math.Min(bwid, 0.35f * reach[(y / B) * bw + x / B]));
                        float bt = Smooth(0, bwid, D);
                        float beachH = sea + 0.3f + Math.Max(0, h - sea - 0.3f) * bt * bt;
                        // cliff: the land near the water is lifted towards the higher ground behind, only within ~30 m,
                        // so the shore drops steeply instead of shelving
                        float lift = Math.Max(0, Math.Min(15, behind.D[i] - h)) * (1 - Smooth(10, 30, D));
                        float cliffH = sea + 0.4f + (h + lift - sea - 0.4f) * Smooth(0, 6, D + 1);
                        float rockyH = h + (rock - 0.45f) * 1.2f * (1 - Smooth(0, 30, D));
                        if (D > 6) rockyH = Math.Max(rockyH, sea + 0.4f); // no puddles behind the shore
                        nh = beach * beachH + cliff * cliffH + rocky * rockyH;
                        nh = Lerp(nh, h, Smooth(60, 90, D));
                    }
                    else
                    {
                        float d = -D;
                        float beachFloor = sea - 0.3f - d * 0.035f;
                        float cliffFloor = sea - 1.5f - d * 0.2f;
                        float rockyFloor = sea - 0.8f - d * 0.08f + Math.Max(0, rock - 0.72f) * 3f * (1 - Smooth(0, 40, d)); // the odd rock offshore, few islets
                        nh = beach * beachFloor + cliff * cliffFloor + rocky * rockyFloor;
                        nh = Math.Max(0.3f, Lerp(nh, h, Smooth(80, 140, d)));
                    }
                    g.D[i] = nh;
                }
        }

        /// <summary>Raised plain: land below ~2x PlainLift above the sea is lifted, fully once PlainRamp metres from the water.</summary>
        private static void LiftPlain(Grid g, float cell, GenSettings s, Perlin pn)
        {
            float[] d = Distance(g, s.SeaLevel, true, cell);
            float L = s.PlainLift;
            for (int i = 0; i < g.D.Length; i++)
            {
                float above = g.D[i] - s.SeaLevel;
                if (above <= 0) continue;
                float x = (i % g.W) * cell, y = (i / g.W) * cell;
                // stretches of coast stay low (beach coves); the ramp width wanders (tapered, uneven cliffs); low hills on top
                float cove = Smooth(-0.32f, -0.12f, pn.Fbm(x / 420f + 4100, y / 420f + 4100, 2));
                float rampW = s.PlainRamp * (0.7f + 0.6f * (pn.Fbm(x / 260f + 4150, y / 260f + 4150, 2) + 0.5f));
                float ramp = Smooth(0, rampW, d[i]) * Lerp(0.15f, 1f, cove);
                float bumps = 1 + 0.35f * pn.Fbm(x / 170f + 4200, y / 170f + 4200, 3);
                g.D[i] += L * bumps * ramp * (1 - Smooth(L * 0.5f, L * 2.5f, above));
            }
        }

        /// <summary>Chamfer distance in metres from each cell on the given side of sea level to the nearest cell on the other side.</summary>
        private static float[] Distance(Grid g, float sea, bool fromLand, float cell)
        {
            int W = g.W, H = g.H;
            float[] d = new float[W * H];
            const float Inf = 1e9f;
            for (int i = 0; i < d.Length; i++) d[i] = (g.D[i] >= sea) == fromLand ? Inf : 0;
            float a = cell, b = cell * 1.41421f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x; float v = d[i];
                    if (v == 0) continue;
                    if (x > 0) v = Math.Min(v, d[i - 1] + a);
                    if (y > 0) { v = Math.Min(v, d[i - W] + a); if (x > 0) v = Math.Min(v, d[i - W - 1] + b); if (x < W - 1) v = Math.Min(v, d[i - W + 1] + b); }
                    d[i] = v;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    int i = y * W + x; float v = d[i];
                    if (v == 0) continue;
                    if (x < W - 1) v = Math.Min(v, d[i + 1] + a);
                    if (y < H - 1) { v = Math.Min(v, d[i + W] + a); if (x < W - 1) v = Math.Min(v, d[i + W + 1] + b); if (x > 0) v = Math.Min(v, d[i + W - 1] + b); }
                    d[i] = v;
                }
            return d;
        }

        // ---------- erosion ----------

        private const float GullyDrops = 0.35f, GullyMaxCut = 2.5f; // drops per cell at Erosion 100; deepest cut (m)

        /// <summary>
        /// Fine water erosion on the full map: small branching gullies down the slopes, as hand-made and EA worlds have
        /// (Bridgewood's hills; the tester's v0.40 test: our hills looked polished). Raindrops are dropped at random on a
        /// ~2 m grid and roll downhill along the smooth (bilinear) gradient with a little inertia, picking up soil where they
        /// speed up and dropping it where they slow, so gullies branch naturally and follow no grid direction. The net
        /// change is capped per cell, then added back to the map smoothly. Strength follows the Erosion slider; water and
        /// ground right by the sea are left alone. Deterministic per seed.
        /// </summary>
        public static void Gullies(Grid h, float cell, GenSettings s)
        {
            float amount = Math.Max(0, Math.Min(1, s.Erosion / 100f));
            if (amount <= 0.01f) return;
            int st = Math.Max(1, (int)Math.Round(2f / cell));
            float wc = cell * st;
            int n = (h.W - 1) / st + 1;
            Grid w = new Grid(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) w[x, y] = h[Math.Min(h.W - 1, x * st), Math.Min(h.H - 1, y * st)];
            float maxCut = GullyMaxCut * (0.3f + amount);
            Grid change = Droplets(w, wc, s.SeaLevel, maxCut, (int)(GullyDrops * amount * n * n), new Random(s.Seed * 3571 + 29), 48);
            for (int y = 0; y < h.H; y++)
                for (int x = 0; x < h.W; x++)
                    h[x, y] += change.At((float)x / st, (float)y / st);
        }

        /// <summary>
        /// Raindrop erosion on 'w' (heights in metres, cells 'wc' metres): drops at random roll downhill along the smooth
        /// gradient with a little inertia, picking up soil where they speed up and dropping it where they slow. Returns the
        /// change (capped at maxCut either way, softened a little); w itself is left eroded. Deterministic for 'rng'.
        /// </summary>
        private static Grid Droplets(Grid w, float wc, float sea, float maxCut, int drops, Random rng, int maxSteps)
        {
            int n = w.W;
            Grid start = w.Clone();
            // the usual droplet constants, in cells and metres (heights are metres, steps one cell)
            const float Inertia = 0.1f, Capacity = 3f, MinSlope = 0.02f, Erode = 0.35f, Deposit = 0.25f, Evaporate = 0.02f, Gravity = 4f;
            for (int d = 0; d < drops; d++)
            {
                float px = 1 + (float)rng.NextDouble() * (n - 3), py = 1 + (float)rng.NextDouble() * (n - 3);
                if (w.At(px, py) < sea + 2) continue;
                float dx = 0, dy = 0, speed = 1, water = 1, sediment = 0;
                for (int step = 0; step < maxSteps; step++)
                {
                    int ix = (int)px, iy = (int)py;
                    float fx = px - ix, fy = py - iy;
                    int i = iy * n + ix;
                    float h00 = w.D[i], h10 = w.D[i + 1], h01 = w.D[i + n], h11 = w.D[i + n + 1];
                    float gx = (h10 - h00) * (1 - fy) + (h11 - h01) * fy, gy = (h01 - h00) * (1 - fx) + (h11 - h10) * fx;
                    float hOld = h00 * (1 - fx) * (1 - fy) + h10 * fx * (1 - fy) + h01 * (1 - fx) * fy + h11 * fx * fy;
                    dx = dx * Inertia - gx * (1 - Inertia); dy = dy * Inertia - gy * (1 - Inertia);
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-6f) break;
                    dx /= len; dy /= len;
                    float nx = px + dx, ny = py + dy;
                    if (nx < 1 || ny < 1 || nx >= n - 2 || ny >= n - 2) break;
                    float hNew = w.At(nx, ny);
                    if (hNew < sea + 0.5f) break; // reached the water
                    float diff = hNew - hOld;
                    float cap = Math.Max(-diff / wc, MinSlope) * speed * water * Capacity;
                    if (diff > 0 || sediment > cap)
                    {
                        float dep = diff > 0 ? Math.Min(diff, sediment) : (sediment - cap) * Deposit;
                        sediment -= dep;
                        w.D[i] += dep * (1 - fx) * (1 - fy); w.D[i + 1] += dep * fx * (1 - fy);
                        w.D[i + n] += dep * (1 - fx) * fy; w.D[i + n + 1] += dep * fx * fy;
                    }
                    else
                    {
                        float ero = Math.Min((cap - sediment) * Erode, -diff);
                        // spread over the 3x3 cells round the drop (soft-walled gullies), never deeper than maxCut
                        float got = 0;
                        for (int oy = -1; oy <= 1; oy++)
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int j = i + oy * n + ox;
                                float wgt = (ox == 0 && oy == 0) ? 0.36f : (ox == 0 || oy == 0) ? 0.1f : 0.06f;
                                float room = maxCut - (start.D[j] - w.D[j]);
                                float e = Math.Max(0, Math.Min(ero * wgt, room));
                                w.D[j] -= e; got += e;
                            }
                        sediment += got;
                    }
                    speed = (float)Math.Sqrt(Math.Max(0, speed * speed + diff / wc * -Gravity));
                    water *= 1 - Evaporate;
                    px = nx; py = ny;
                }
            }
            // the change, softened a little (deposits too: fans at the foot of slopes)
            Grid change = new Grid(n, n);
            for (int i = 0; i < n * n; i++) change.D[i] = Math.Max(-maxCut, Math.Min(maxCut, w.D[i] - start.D[i]));
            return change.Blur(0.6f);
        }

        /// <summary>
        /// Dissection (worlds.txt "style": Crag): the hills cut into networks of gullies with sharp ridges between, as
        /// EA's highlands are (Aurora Skies, Sunset Valley's mountains, Champs Les Sims' hillsides); without it our hills
        /// were round bumps. On a 4 m copy of the planning grid (so the preview and the full map get the same gullies):
        /// water is routed downhill over the ground (roughened a little so the lines branch), each channel is cut by its
        /// catchment (deeper downstream, up to CragDepth), and the cuts are widened into V-shaped gullies whose walls rise
        /// at about 30 degrees. Cuts stay within the local relief, fade out by the sea and in the town site and pockets.
        /// Returns the change (negative = cut) on the 4 m grid.
        /// </summary>
        private static Grid Dissect(Grid coarse, GenSettings s, float worldSize, Perlin pn)
        {
            const float C = 4f;
            int n = (int)(worldSize / C) + 1, N = n * n;
            float f = C / CoarseCell, sea = s.SeaLevel;
            Grid w = new Grid(n, n);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    w[i, j] = SampleCubic(coarse, i * f, j * f) + pn.Fbm(i * C / 140f + 1300, j * C / 140f + 1300, 3) * DissectNoise;
            float amount = Math.Min(1, s.Crag / 100f), depth = s.CragDepth * amount;
            float a0 = DissectMinArea / (C * C), a1 = a0 * 400;
            int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 }, dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
            float[] dl = { 1, 1, 1, 1, 1.4142f, 1.4142f, 1.4142f, 1.4142f };
            int[] order = new int[N]; float[] keys = new float[N], area = new float[N], slope = new float[N];
            Grid cut = new Grid(n, n), ground = new Grid(n, n);
            // hollows filled first so the water always runs on to the sea or the map edge (else every little dip in the
            // roughened ground ends a gully); the ground's own slope decides where cutting happens
            Grid filled = PriorityFlood(w, sea);
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    float gx = (w.D[i + 1] - w.D[i - 1]) / (2 * C), gy = (w.D[i + n] - w.D[i - n]) / (2 * C);
                    slope[i] = (float)Math.Sqrt(gx * gx + gy * gy);
                }
            // three rounds: route water over the ground as cut so far, deepen the channels; later rounds follow the
            // channels already cut, so small gullies join bigger ones (a branching network, not parallel grooves)
            for (int round = 1; round <= 3; round++)
            {
                Grid wide = Widen(cut, C);
                for (int i = 0; i < N; i++) { ground.D[i] = filled.D[i] - wide.D[i]; order[i] = i; keys[i] = -ground.D[i]; area[i] = 1; }
                Array.Sort(keys, order);
                foreach (int i in order) // highest first: each cell's catchment goes to its steepest lower neighbour
                {
                    int x = i % n, y = i / n;
                    if (x == 0 || y == 0 || x == n - 1 || y == n - 1) continue;
                    int best = -1; float bs = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = (y + dy[k]) * n + x + dx[k];
                        float sl = (ground.D[i] - ground.D[j]) / (dl[k] * C);
                        if (sl > bs) { bs = sl; best = j; }
                    }
                    if (best >= 0) area[best] += area[i];
                }
                // channel depth: from DissectMinArea of catchment, growing with the catchment (log), on sloping ground
                // only, never deeper than half the height above the sea
                float part = depth * round / 3f;
                if (Environment.GetEnvironmentVariable("CAWDBG") != null) { int ca = 0, cs = 0, cl = 0; for (int i = 0; i < N; i++) { if (w.D[i] > sea + 1) cl++; if (area[i] >= a0 && w.D[i] > sea + 1) { ca++; if (slope[i] > 0.05f) cs++; } } Console.Error.WriteLine("DBG round " + round + " land " + cl + " channel cells " + ca + " sloped " + cs); }
                for (int i = 0; i < N; i++)
                {
                    if (area[i] < a0 || w.D[i] < sea + 1) continue;
                    float g = (float)(Math.Log(area[i] / a0) / Math.Log(a1 / a0));
                    float c = part * (0.3f + 0.7f * Math.Min(1, g)) * (0.4f + 0.6f * Smooth(0, DissectSlope, slope[i])) * Smooth(s.CragFloor, s.CragFloor + 22, w.D[i] - sea);
                    cut.D[i] = Math.Max(cut.D[i], Math.Min(c, 0.5f * (w.D[i] - sea)));
                }
            }
            cut = Widen(cut, C);
            if (DebugStage != null) DebugStage("dissectcut", cut);
            if (Environment.GetEnvironmentVariable("CAWDBG") != null) { int c5 = 0; float mx = 0; foreach (float v in cut.D) { if (v > 2) c5++; mx = Math.Max(mx, v); } Console.Error.WriteLine("DBG dissect: depth " + depth + " max " + mx + " share>2m " + (float)c5 / N); }
            cut = cut.Blur(0.7f); // a rounded floor instead of a 4 m crease
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float set = Settled(s, worldSize, i * C, j * C);
                    float keep = Math.Max(0.1f, 1 - set * 0.8f) * Smooth(1, 6, w[i, j] - sea);
                    cut[i, j] = -cut[i, j] * keep;
                }
            return cut;
        }
        /// <summary>Channel depths widened into V-shaped gullies (two-pass chamfer max: walls lose DissectWall m per m).</summary>
        private static Grid Widen(Grid chan, float C)
        {
            int n = chan.W;
            Grid cut = chan.Clone();
            float d1 = DissectWall * C, d2 = d1 * 1.4142f;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x; float v = cut.D[i];
                    v = Math.Max(v, cut.D[i - 1] - d1); v = Math.Max(v, cut.D[i - n] - d1);
                    v = Math.Max(v, cut.D[i - n - 1] - d2); v = Math.Max(v, cut.D[i - n + 1] - d2);
                    cut.D[i] = v;
                }
            for (int y = n - 2; y > 0; y--)
                for (int x = n - 2; x > 0; x--)
                {
                    int i = y * n + x; float v = cut.D[i];
                    v = Math.Max(v, cut.D[i + 1] - d1); v = Math.Max(v, cut.D[i + n] - d1);
                    v = Math.Max(v, cut.D[i + n + 1] - d2); v = Math.Max(v, cut.D[i + n - 1] - d2);
                    cut.D[i] = v;
                }
            return cut;
        }

        private const float OrleansGate = -0.02f;
        public static float OrleansSpacing = 340f, OrleansChannel = 34f; // channel 34 (was 24): EA's channels are wide water, 24 m ones counted as rivers (water score 70.0 -> 74.4, seeds 10-15)
        private const float IcelandCrest = 0.12f, IcelandLowland = 0.04f, IcelandDrop = 0.55f;
        internal static float IcelandSplit = 0.15f; // moves the highland/lowland line: larger = more highland
        public static float DissectNoise = 4f, DissectMinArea = 150f, DissectWall = 1.0f; // m; m2 of catchment where a gully starts; wall drop per m
        private const float DissectSlope = 0.06f;

        /// <summary>Moves material down slopes steeper than 'talus' degrees (softens cliffs and noise artefacts).</summary>
        public static void ThermalErosion(Grid g, float cell, float talusDeg, int iterations) { ThermalErosion(g, cell, talusDeg, iterations, null); }

        /// <summary>As above; cells marked in 'keep' (an escarpment's face) neither give nor take material.</summary>
        public static void ThermalErosion(Grid g, float cell, float talusDeg, int iterations, bool[] keep)
        {
            float maxDiff = (float)Math.Tan(talusDeg * Math.PI / 180) * cell;
            int W = g.W, H = g.H;
            float[] delta = new float[g.D.Length];
            for (int it = 0; it < iterations; it++)
            {
                Array.Clear(delta, 0, delta.Length);
                for (int y = 1; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                    {
                        int i = y * W + x;
                        if (keep != null && keep[i]) continue;
                        float h = g.D[i];
                        int[] nb = { i - 1, i + 1, i - W, i + W };
                        foreach (int k in nb)
                        {
                            if (keep != null && keep[k]) continue;
                            float d = h - g.D[k];
                            if (d > maxDiff) { float m = (d - maxDiff) * 0.2f; delta[i] -= m; delta[k] += m; }
                        }
                    }
                for (int i = 0; i < delta.Length; i++) g.D[i] += delta[i];
            }
        }

        /// <summary>
        /// Smooths the ground under planned roads so they sit at an even grade (8 m road + 12 m blend). Each road is
        /// resampled every 2 m and its height profile smoothed over ~40 m (three 21-sample moving averages); a road
        /// ending on another takes that road's height there, so they meet level; the profiles all come from the ground before
        /// any road is graded. Before, the strip was graded only at the road's stored points (up to 30 m apart) and
        /// smoothed by point count, which left steps along the road (the tester, 2026-10-24). Water (a river under a bridge)
        /// is left alone: the road's level over it is bridged between the banks.
        /// With bridges (bank to bank, metres, as RoadNet.Bridges) the road is also held at each bridge's deck
        /// height along the bridge object (bridgeLength m, centred on the crossing) and ramped back to its own profile
        /// beyond (at most ~7%), so the land meets the deck at both ends: CAW drapes roads on the terrain, and a deck left
        /// above the bank stood as a step over its approach road (the tester, 2026-10-25, #131). The deck height is
        /// BridgeDeck's: what Build roads then reads from the raised ground at the bridge ends.
        /// </summary>
        public static void GradeRoads(Grid g, float cell, List<List<float[]>> roads, float sea, List<float[]> bridges, float bridgeLength)
        {
            List<List<float[]>> lines = new List<List<float[]>>();
            List<float[]> profs = new List<float[]>();
            List<bool[]> wets = new List<bool[]>();
            foreach (List<float[]> road in roads)
            {
                if (road.Count < 2) continue;
                List<float[]> line = Resample2(road, 2f);
                int n = line.Count;
                float[] prof = new float[n];
                bool[] wet = new bool[n];
                for (int k = 0; k < n; k++) { prof[k] = Sample(g, line[k][0] / cell, line[k][1] / cell); wet[k] = prof[k] < sea + 0.5f; }
                // over water: a straight line between the banks
                for (int k = 0; k < n; k++)
                {
                    if (!wet[k]) continue;
                    int a = k, b = k;
                    while (a >= 0 && wet[a]) a--;
                    while (b < n && wet[b]) b++;
                    float va = a >= 0 ? prof[a] : (b < n ? prof[b] : sea + 1), vb = b < n ? prof[b] : va;
                    for (int q = Math.Max(0, a + 1); q < Math.Min(n, b); q++)
                        prof[q] = Math.Max(sea + 1, Lerp(va, vb, (float)(q - a) / Math.Max(1, b - a)));
                    k = b;
                }
                for (int pass = 0; pass < 3; pass++) prof = MovingAverage(prof, 10);
                if (bridges != null) foreach (float[] b in bridges) HoldAtDeck(g, cell, line, prof, b, bridgeLength, sea);
                lines.Add(line); profs.Add(prof); wets.Add(wet);
            }
            // where a road ends on another (a T, or ends meeting at a junction), its end takes the height the other
            // road's profile has there, eased over 20 m, so the two meet level
            List<float[]> deadEnds = new List<float[]>(); // x, z, level: a flat pad for each end cap
            for (int r = 0; r < lines.Count; r++)
            {
                float[] prof = profs[r]; int n = prof.Length;
                for (int e = 0; e < 2; e++)
                {
                    float[] end = lines[r][e == 0 ? 0 : n - 1];
                    float sum = 0; int cnt = 0;
                    for (int q = 0; q < lines.Count; q++)
                    {
                        if (q == r) continue;
                        List<float[]> o = lines[q];
                        int best = -1; float bd = 16f;
                        for (int k = 0; k < o.Count; k++) { float d = Sq(o[k][0] - end[0]) + Sq(o[k][1] - end[1]); if (d < bd) { bd = d; best = k; } }
                        if (best < 0) continue;
                        bool through = best > 2 && best < o.Count - 3;
                        sum += profs[q][best] * (through ? 4 : 1); cnt += through ? 4 : 1;
                    }
                    if (cnt == 0)
                    {
                        // a dead end (Build roads caps it with an intersection piece, ~12 m): the last DeadEndFlat m level
                        // at their mean height, eased in over the 20 m before, and a pad ground for the cap (#135). Not
                        // where the road runs off the map (no cap there).
                        float mapM = g.W * cell, edge = Math.Min(Math.Min(end[0], end[1]), Math.Min(mapM - end[0], mapM - end[1]));
                        if (edge < 40) continue;
                        int flatN = Math.Min(n, (int)(DeadEndFlat / 2f) + 1);
                        float level = 0;
                        for (int k = 0; k < flatN; k++) level += prof[e == 0 ? k : n - 1 - k];
                        level /= flatN;
                        for (int k = 0; k < n; k++)
                        {
                            float d = (e == 0 ? k : n - 1 - k) * 2f;
                            if (d >= DeadEndFlat + 20) break;
                            int idx = e == 0 ? k : n - 1 - k;
                            float wgt = d <= DeadEndFlat ? 1 : 1 - Smooth(DeadEndFlat, DeadEndFlat + 20, d);
                            prof[idx] += (level - prof[idx]) * wgt;
                        }
                        deadEnds.Add(new[] { end[0], end[1], level });
                        continue;
                    }
                    float target = (sum + prof[e == 0 ? 0 : n - 1]) / (cnt + 1);
                    for (int k = 0; k < n; k++)
                    {
                        float d = (e == 0 ? k : n - 1 - k) * 2f;
                        if (d >= 20) continue;
                        int idx = e == 0 ? k : n - 1 - k;
                        float delta = target - prof[e == 0 ? 0 : n - 1];
                        prof[idx] += delta * (1 - Smooth(0, 20, d));
                    }
                }
            }
            // every road's profile, weighted by nearness, averaged per point (where strips overlap at a junction the
            // roads share the ground), then the ground moved towards it by the strongest weight there
            float[] sumW = new float[g.D.Length], sumT = new float[g.D.Length], maxW = new float[g.D.Length];
            for (int r = 0; r < lines.Count; r++)
            {
                List<float[]> line = lines[r]; float[] prof = profs[r]; bool[] wet = wets[r];
                for (int k = 0; k < line.Count; k++)
                {
                    if (wet[k]) continue;
                    float cx = line[k][0] / cell, cy = line[k][1] / cell, rr = 20f / cell;
                    int x0 = Math.Max(0, (int)(cx - rr)), x1 = Math.Min(g.W - 1, (int)(cx + rr) + 1);
                    int y0 = Math.Max(0, (int)(cy - rr)), y1 = Math.Min(g.H - 1, (int)(cy + rr) + 1);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float d = (float)Math.Sqrt(Sq(x - cx) + Sq(y - cy)) * cell;
                            if (d > 20) continue;
                            float w = d <= 8 ? 1 : 1 - Smooth(8, 20, d);
                            int i = y * g.W + x;
                            float ww = w * w + 1e-4f;
                            sumW[i] += ww; sumT[i] += ww * prof[k];
                            if (w > maxW[i]) maxW[i] = w;
                        }
                }
            }
            // end-cap pads: level within DeadEndPad m of a dead end, eased out over 12 m beyond (the cap is ~12 m across)
            foreach (float[] de in deadEnds)
            {
                float cx = de[0] / cell, cy = de[1] / cell, rr = (DeadEndPad + 12f) / cell;
                int x0 = Math.Max(0, (int)(cx - rr)), x1 = Math.Min(g.W - 1, (int)(cx + rr) + 1);
                int y0 = Math.Max(0, (int)(cy - rr)), y1 = Math.Min(g.H - 1, (int)(cy + rr) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = (float)Math.Sqrt(Sq(x - cx) + Sq(y - cy)) * cell;
                        if (d > DeadEndPad + 12) continue;
                        float w = d <= DeadEndPad ? 1 : 1 - Smooth(DeadEndPad, DeadEndPad + 12, d);
                        int i = y * g.W + x;
                        float ww = 4 * w * w + 1e-4f; // the pad outweighs the road strip's own profile there
                        sumW[i] += ww; sumT[i] += ww * de[2];
                        if (w > maxW[i]) maxW[i] = w;
                    }
            }
            for (int i = 0; i < g.D.Length; i++)
            {
                if (maxW[i] <= 0 || g.D[i] < sea) continue; // never fill water
                float t = g.D[i] + (sumT[i] / sumW[i] - g.D[i]) * maxW[i] * 0.95f;
                g.D[i] = Math.Max(t, Math.Min(g.D[i], sea + 0.3f));
            }
        }

        // a dead end's last metres held level, and the radius of the level pad round it for its end cap (#135)
        private const float DeadEndFlat = 16f, DeadEndPad = 10f;

        /// <summary>A bridge's deck height (m): level with its higher bank end, at least 1.5 m over the sea.</summary>
        public static float BridgeDeck(Grid g, float cell, float[] b, float bridgeLength, float sea)
        {
            float mx = (b[0] + b[2]) / 2, my = (b[1] + b[3]) / 2, dx = b[2] - b[0], dy = b[3] - b[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return sea + 1.5f;
            dx /= len; dy /= len;
            float h = 0, half = bridgeLength / 2;
            foreach (float sgn in new[] { -1f, 1f })
            {
                float x = (mx + sgn * dx * half) / cell, y = (my + sgn * dy * half) / cell;
                x = Math.Max(0, Math.Min(g.W - 1.001f, x)); y = Math.Max(0, Math.Min(g.H - 1.001f, y));
                h = Math.Max(h, Sample(g, x, y));
            }
            return Math.Max(sea + 1.5f, h);
        }

        /// <summary>The road profile held at the deck height where the line runs along the bridge object, eased back over
        /// a ramp beyond each end.</summary>
        private static void HoldAtDeck(Grid g, float cell, List<float[]> line, float[] prof, float[] b, float bridgeLength, float sea)
        {
            float mx = (b[0] + b[2]) / 2, my = (b[1] + b[3]) / 2, dx = b[2] - b[0], dy = b[3] - b[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return;
            dx /= len; dy /= len;
            int n = line.Count, first = -1, last = -1;
            for (int k = 0; k < n; k++)
            {
                float px = line[k][0] - mx, py = line[k][1] - my, t = px * dx + py * dy, side = Math.Abs(px * dy - py * dx);
                if (Math.Abs(t) <= bridgeLength / 2 + 1 && side <= 10) { if (first < 0) first = k; last = k; }
            }
            if (first < 0) return; // this road doesn't cross this bridge
            float deck = BridgeDeck(g, cell, b, bridgeLength, sea);
            for (int k = first; k <= last; k++) prof[k] = deck;
            // ramps: the deck height eased back into the road's own profile, over as far as a 7% grade needs (30 m at least)
            for (int side = 0; side < 2; side++)
            {
                int edge = side == 0 ? first : last, step = side == 0 ? -1 : 1;
                float rise = Math.Abs(deck - prof[Math.Max(0, Math.Min(n - 1, edge + step * 15))]);
                float ramp = Math.Max(30f, rise / 0.07f);
                for (int k = edge + step, m = 1; k >= 0 && k < n && m * 2f < ramp; k += step, m++)
                {
                    float w = 1 - Smooth(0, ramp, m * 2f);
                    prof[k] += (deck - prof[k]) * w;
                }
            }
        }

        private static float[] MovingAverage(float[] v, int half)
        {
            int n = v.Length;
            float[] o = new float[n];
            double[] cum = new double[n + 1];
            for (int k = 0; k < n; k++) cum[k + 1] = cum[k] + v[k];
            for (int k = 0; k < n; k++)
            {
                int a = Math.Max(0, k - half), b = Math.Min(n - 1, k + half);
                o[k] = (float)((cum[b + 1] - cum[a]) / (b - a + 1));
            }
            return o;
        }

        /// <summary>Points every 'step' metres along a polyline (ends kept).</summary>
        private static List<float[]> Resample2(List<float[]> p, float step)
        {
            List<float[]> o = new List<float[]>();
            o.Add(p[0]);
            float carry = 0;
            for (int i = 1; i < p.Count; i++)
            {
                float[] a = p[i - 1], b = p[i];
                float len = (float)Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[1] - a[1]));
                if (len < 1e-4f) continue;
                float d = step - carry;
                while (d <= len) { o.Add(new[] { a[0] + (b[0] - a[0]) * d / len, a[1] + (b[1] - a[1]) * d / len }); d += step; }
                carry = len - (d - step);
            }
            if (Sq(o[o.Count - 1][0] - p[p.Count - 1][0]) + Sq(o[o.Count - 1][1] - p[p.Count - 1][1]) > 0.01f) o.Add(p[p.Count - 1]);
            return o;
        }

        /// <summary>Catmull-Rom bicubic sample: smooth when a coarse grid is enlarged (no 8 m facets).</summary>
        public static float SampleCubic(Grid g, float x, float y)
        {
            x = Math.Max(0, Math.Min(g.W - 1.001f, x)); y = Math.Max(0, Math.Min(g.H - 1.001f, y));
            int ix = (int)x, iy = (int)y; float fx = x - ix, fy = y - iy;
            float[] row = new float[4];
            for (int m = -1; m <= 2; m++)
            {
                int yy = Math.Max(0, Math.Min(g.H - 1, iy + m));
                float p0 = g[Math.Max(0, ix - 1), yy], p1 = g[ix, yy], p2 = g[Math.Min(g.W - 1, ix + 1), yy], p3 = g[Math.Min(g.W - 1, ix + 2), yy];
                row[m + 1] = CatmullRom(p0, p1, p2, p3, fx);
            }
            return CatmullRom(row[0], row[1], row[2], row[3], fy);
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
        }

        public static float Sample(Grid g, float x, float y) { return g.At(x, y); }

        public static List<float[]> Chaikin(List<float[]> p)
        {
            if (p.Count < 3) return p;
            List<float[]> o = new List<float[]>();
            o.Add(p[0]);
            for (int i = 0; i < p.Count - 1; i++)
            {
                o.Add(new[] { 0.75f * p[i][0] + 0.25f * p[i + 1][0], 0.75f * p[i][1] + 0.25f * p[i + 1][1] });
                o.Add(new[] { 0.25f * p[i][0] + 0.75f * p[i + 1][0], 0.25f * p[i][1] + 0.75f * p[i + 1][1] });
            }
            o.Add(p[p.Count - 1]);
            return o;
        }

        private static float Sq(float v) { return v * v; }
    }
}
