using System;
using System.Collections.Generic;

namespace CawPlus
{
    public enum PlaceKind { Tree = 0, Bush = 1, Rock = 2, Other = 3, Streetlight = 4, Sign = 5, Spawner = 6, Fence = 7, Detail = 8 }

    /// <summary>The base-game street details Auto-Place lays (PlacementEngine.StreetDetails).</summary>
    public enum StreetDetail { None, Drain, Manhole, Hydrant, Fence, FencePost, Welcome, NbhdSign, Billboard }

    public enum Biome { Temperate, MixedForest, Boreal, Tropical, Desert, Mediterranean }

    /// <summary>One placeable thing from CAW's catalog, classified.</summary>
    internal sealed class PlaceItem
    {
        public Caw.CatalogItem Catalog;
        public string Name;
        public PlaceKind Kind;
        public bool Selected;
        public float Weight = 1;     // relative frequency among selected items of the same kind
        // habitat preferences, from the name
        public bool Conifer, WaterLover, Lowland;
        public bool Palm;                // palms and coconut trees: the only trees on a sandy beach (the tester, 2026-10-24)
        public int RockSize = -1;        // 0 small, 1 medium, 2 large, 3 extra large (from the EA name), -1 unknown
        public bool Indoor;              // furniture and household items: never placed outdoors
        public bool BuyPlant;            // a tree/bush/flower that is a buy-mode game object (garden plants, harvestables), not EA scenery
        public bool WaterPlant;          // reeds, cattails, lilies: on river and pond banks
        public RefObject Ref;            // this item in the reference world, if any
        public StreetDetail Detail;      // for PlaceKind.Detail: which street detail it is
        public override string ToString() { return Name; }
    }

    internal sealed class PlaceSettings
    {
        public int Seed = 7;
        public float ForestCover = 45;   // % of suitable land that is woodland
        public bool AltitudeZones = true; // fewer trees higher up, conifers taking over, a tree line near the tops
        public float TreeDensity = 40;   // how tightly packed the woods are
        public float Bushes = 40, Rocks = 30;
        public float EdgeMargin = 0;
        public float WaterLevel;
        public Grid WaterOffset;
        public ObjectLibrary Library;    // optional reference world: habitat per item and density target
        public bool MatchLibraryDensity;
        public float[] FillScale = { 1, 1, 1, 1, 1, 1, 1, 1, 1 }; // per PlaceKind, set by FitDensity
        public bool WaterPlants = true;            // reeds etc. on river and pond banks
        public int MaxObjects = 10000;             // budget for the whole world, objects already there included
        public float StreetlightSpacing = 32;      // metres along a road (Appaloosa Plains: ~32 m)
        public List<List<float[]>> Roads;          // road centre lines, map coordinates
        public List<float[]> Junctions;            // map coordinates
        public bool DriveRight = true;             // signs on the right-hand verge (Sims drive on the right)
        public List<List<float[]>> Bridges;        // road pieces on bridges, map coordinates: no streetlights or signs near them
        public float TreeSlopeStart = 24;          // trees thin from here to none 14 deg steeper: where rock paint starts
        public float MapW, MapH;                   // full map size in map pixels (for Bridges)
        public byte[] PlanCodes; public int PlanN; // the painted plan (PlanArea per cell, PlanN x PlanN over the whole map, #171); null = none
        public bool WoodsOnlyInForest;            // World Builder (#173): woodland only in the forest drawn, a few trees elsewhere
        public List<float[]> Lots;                 // lots as (mapX, mapY, radius) in map pixels: town roads are lot-lined
        public const int Kinds = 9;
    }

    public struct Placement
    {
        public int Item;          // index into the item list
        public float MapX, MapY;  // continuous map-pixel coordinates (0..MapW)
        public float Yaw;
        public uint Variant;
    }

    /// <summary>Decides where trees, bushes and rocks go: density maps from the terrain, then spaced random points.</summary>
    internal static class PlacementEngine
    {
        // ---------- terrain bands (shared with ObjectLibrary) ----------

        public static readonly float[] SlopeBands = { 5, 10, 20, 30, 40 };   // degrees
        public static readonly float[] RelBands = { 0.2f, 0.4f, 0.6f, 0.8f }; // 0 = lowest point, 1 = highest
        public static readonly float[] WaterBands = { 0.02f, 0.15f };        // blurred water share: far / near / at the shore
        public static readonly float[] RoadBands = { 15, 30, 60, 120, 240 };  // metres from the nearest road centre line

        public static int Band(float v, float[] edges)
        {
            int b = 0;
            while (b < edges.Length && v >= edges[b]) b++;
            return b;
        }

        // ---------- classification ----------

        // Whole words (names are split on punctuation, spaces and camelCase).
        private static readonly string[] BushWords = { "bush", "bushes", "shrub", "shrubs", "hedge", "fern", "ferns", "flower", "flowers", "reed", "reeds",
            "grass", "grasses", "weed", "weeds", "lavender", "bramble", "ivy", "sunflower", "sunflowers", "dianthus", "wildflower", "wildflowers", "cattail", "cattails" };
        private static readonly string[] RockWords = { "rock", "rocks", "boulder", "boulders", "stone", "stones", "cliff", "pebble", "pebbles", "outcrop", "crag" };
        private static readonly string[] TreeWords = { "tree", "trees" };
        private static readonly string[] SpawnerWords = { "spawner", "spawners" };
        private static readonly string[] LightWords = { "light", "lights", "lighting", "lamp", "lamppost", "streetlight", "streetlamp" };
        private static readonly string[] SignWords = { "sign", "signs", "signpost" };
        // a sign only counts as a road sign with one of these (so "Signs-of-Springs Bed" isn't one)
        private static readonly string[] RoadSignWords = { "traffic", "street", "road", "stop", "yield", "speed", "crossing", "parking", "direction",
            "directions", "curve", "curved", "school", "highway", "route", "pedestrian", "railroad", "detour", "limit" };
        private static readonly string[] StreetWords = { "street", "road", "outdoor", "lamppost", "streetlight", "streetlamp" };
        // furniture and household things: never put outdoors by Auto-Place, whatever else the name says
        private static readonly string[] IndoorWords = { "bed", "beds", "bunk", "crib", "mattress", "pillow", "blanket", "sofa", "couch", "loveseat",
            "armchair", "recliner", "chair", "chairs", "table", "desk", "dresser", "wardrobe", "armoire", "closet", "nightstand", "endtable",
            "toilet", "shower", "bath", "bathtub", "tub", "sink", "vanity", "hamper", "counter", "fridge", "refrigerator", "stove", "oven",
            "microwave", "dishwasher", "tv", "television", "computer", "laptop", "bookcase", "bookshelf", "shelf", "shelves", "cabinet",
            "rug", "mirror", "painting", "curtain", "curtains", "blinds", "chandelier", "sconce", "stool", "piano", "guitar", "stereo",
            "speaker", "speakers", "console", "phone", "clock", "toy", "toys", "dollhouse", "easel", "aquarium", "bedroom", "bathroom",
            "kitchen", "dining", "nursery", "office", "pc", "poster", "wallpaper", "floor", "ceiling", "doorway", "window", "door" };
        // EA's own outdoor objects by catalog instance, from the bundled reference worlds (signs, streetlights, spawners, rocks...)
        public static readonly Dictionary<string, PlaceKind> Known = new Dictionary<string, PlaceKind>();

        /// <summary>Fills Known from every reference library (EA objects by instance).</summary>
        public static void LoadKnown()
        {
            Known.Clear();
            foreach (ObjectLibrary l in ObjectLibrary.LoadAll())
                foreach (RefObject r in l.Items)
                    if (r.Instance != null && r.Instance.Length == 16 && !Known.ContainsKey(r.Instance)) Known[r.Instance] = r.Kind;
        }
        private static readonly string[] FenceWords = { "fence", "fences", "fencing" };
        // Anything with one of these words is not natural scenery, whatever else its name says.
        private static readonly string[] NotNature = { "statue", "relic", "fence", "fencing", "gate", "gating", "fireplace", "firepit", "fire",
            "light", "lights", "lighting", "lamp", "chair", "rocker", "rocking", "scooter", "tombstone", "tomb", "troll", "keystone", "panel",
            "wall", "rail", "gem", "gems", "gemstone", "remains", "urnstone", "hydrant", "sculpture", "sign", "mailbox", "bench", "table",
            "bed", "door", "window", "toy", "rug", "painting", "plaque", "fountain", "bridge", "sphinx", "ruins", "skeleton", "unicorn",
            "magic", "jelly", "ghost", "soil", "tomato", "chili", "gnome", "collectible", "space", "monument", "trash", "board", "breaker",
            "mobile", "boogie", "riddler", "robert", "professor", "snake", "misspelled", "fearless", "werkbunnst", "incinerating", "relax",
            "multi", "harvest", "seed", "seeds", "planter", "pot", "vase", "garden", "spawner", "spawners", "fx", "effect", "test", "do", "not",
            "simhenge", "stakeout", "egg", "meteor", "crystal", "metal", "ore", "column", "pillar", "arch", "stairs", "step", "steps", "path",
            // collectables and novelties that look like rocks or plants (the tester, 2026-10-20: dinosaur bones placed as rocks)
            "bone", "bones", "fossil", "fossils", "dino", "dinosaur", "skull", "skulls", "meteorite", "meteorites", "geode", "geodes",
            "mineral", "minerals", "nugget", "treasure", "chest", "crate", "birdbath", "grave", "gravestone", "headstone", "memorial",
            "ruin", "altar", "shrine", "idol", "totem", "dig", "excavation", "artifact", "relics", "insect", "bug", "beetle", "butterfly",
            "fish", "frog", "gnomes", "death", "harvestable", "plantable", "cowplant", "bonsai", "potted", "basket", "box", "sack", "bag",
            // from the tester's catalog.csv: pet stones, cars, accessories and kits whose names mention rocks or plants
            "pet", "car", "bus", "lizard", "agama", "philosopher", "petroglyphs", "cavern", "accessory", "kit", "sponge", "brush",
            "dust", "bee", "bouquet", "fantasy", "stump", "hollow", "log", "burned", "charred", "scorched", "pano", "panoramic", "hang", "mans",
            "cemetery", "creepy", "spooky", "waterfall" };
        // gardening and buy-mode plants (Death Flower etc.): never picked by default
        private static readonly string[] GardenWords = { "death", "flame", "life", "money", "omni", "cow", "snapdragon", "flowerbed", "flowerpot",
            "plot", "trellis", "harvest", "crop", "crops", "veggie", "vegetable", "herb", "herbs", "orchard", "garden", "gardening" };
        private static readonly string[] WaterPlantWords = { "reed", "reeds", "cattail", "cattails", "lily", "lilies", "lilypad", "lilypads", "waterlily",
            "rush", "rushes", "bulrush", "bulrushes", "papyrus", "lotus", "sedge", "sedges", "marsh", "swamp" };

        public static List<string> Words(string text)
        {
            List<string> words = new List<string>();
            System.Text.StringBuilder cur = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool split = !char.IsLetter(c) || (char.IsUpper(c) && i > 0 && char.IsLower(text[i - 1]));
                if (split && cur.Length > 0) { words.Add(cur.ToString().ToLowerInvariant()); cur.Length = 0; }
                if (char.IsLetter(c)) cur.Append(c);
            }
            if (cur.Length > 0) words.Add(cur.ToString().ToLowerInvariant());
            return words;
        }

        private static bool AnyWord(List<string> words, string[] set)
        {
            foreach (string w in words) if (Array.IndexOf(set, w) >= 0) return true;
            return false;
        }

        public static readonly Dictionary<Biome, string[]> BiomeWords = new Dictionary<Biome, string[]>();

        static PlacementEngine()
        {
            BiomeWords[Biome.Temperate] = new[] { "oak", "maple", "birch", "elm", "ash", "willow", "beech", "poplar", "aspen", "cherry", "apple",
                "dogwood", "deciduous", "sycamore", "hickory", "linden", "chestnut", "walnut", "magnolia", "redbud", "lilac", "hazel", "alder" };
            BiomeWords[Biome.Boreal] = new[] { "pine", "spruce", "fir", "cedar", "hemlock", "conifer", "larch", "evergreen", "sequoia", "redwood", "juniper", "alpine" };
            BiomeWords[Biome.Tropical] = new[] { "palm", "banana", "coconut", "jungle", "tropical", "bamboo", "mangrove", "frangipani", "plumeria",
                "hibiscus", "banyan", "bird of paradise", "monstera", "kapok" };
            BiomeWords[Biome.Desert] = new[] { "cactus", "saguaro", "joshua", "yucca", "agave", "mesquite", "palo", "desert", "dead", "acacia", "sage", "scrub", "creosote", "ocotillo" };
            BiomeWords[Biome.Mediterranean] = new[] { "olive", "cypress", "cork", "lemon", "orange", "citrus", "fig", "oleander", "umbrella", "stone pine", "lavender", "bougainvillea" };
        }

        private static bool Any(string n, string[] words) { foreach (string w in words) if (n.Contains(w)) return true; return false; }

        /// <summary>The EA resource name in brackets at the end of a catalog name ("Rock - Mountain Small A - (rockMountainSmallA)"), or "".</summary>
        public static string ResourceName(string name)
        {
            if (name == null) return "";
            int a = name.LastIndexOf('('), b = name.LastIndexOf(')');
            return a >= 0 && b > a ? name.Substring(a + 1, b - a - 1).Trim() : "";
        }

        /// <summary>EA's scenery rocks are named rock..., nbhRock..., RockGranite..., GiantBoulder...: the resource name starts with it.</summary>
        private static bool IsRockResource(List<string> rw)
        {
            if (rw.Count == 0) return false;
            if (rw[0] == "rock" || rw[0] == "rocks" || rw[0] == "boulder") return true;
            if (rw.Count > 1 && (rw[0] == "nbh" || rw[0] == "giant") && (rw[1] == "rock" || rw[1] == "boulder")) return true;
            return false;
        }

        // street furniture that isn't a roadside streetlight or sign (wall lights, searchlights, stage props...)
        private static readonly string[] NotStreet = { "wall", "search", "sculpture", "stage", "fence", "path", "garden", "rabbit", "hole", "sign" };

        private static readonly string[] NotSign = { "wall", "sculpture", "stage" };

        private static bool IsStreetlightResource(List<string> rw)
        {
            bool street = rw.Contains("street") || rw.Contains("streetlight") || rw.Contains("streetlamp");
            bool light = rw.Contains("lighting") || rw.Contains("light") || rw.Contains("streetlight") || rw.Contains("lamp") || rw.Contains("streetlamp");
            return street && light && !AnyWord(rw, NotStreet);
        }

        public static PlaceItem Classify(Caw.CatalogItem c)
        {
            PlaceItem p = new PlaceItem();
            p.Catalog = c; p.Name = c.Name;
            List<string> words = Words(c.Name);
            string res = ResourceName(c.Name);
            List<string> resWords = Words(res);
            string nameOnly = c.Name.ToLowerInvariant();
            PlaceKind known;
            string inst = c.InstanceHex;
            if (inst.Length > 0 && Known.TryGetValue(inst, out known))
                p.Kind = resWords.Contains("pano") || resWords.Contains("panoramic") ? PlaceKind.Other : known; // what EA used it as (not backdrop trees)
            else if (!c.IsTree && AnyWord(words, IndoorWords)) { p.Kind = PlaceKind.Other; p.Indoor = true; }
            else if (AnyWord(words, SpawnerWords)) p.Kind = PlaceKind.Spawner;
            else if (res.Length > 0 ? IsStreetlightResource(resWords)
                     : (AnyWord(words, LightWords) && AnyWord(words, StreetWords)) || nameOnly.Contains("streetlight") || nameOnly.Contains("streetlamp"))
                p.Kind = PlaceKind.Streetlight;
            else if (res.Length > 0 ? resWords.Count > 0 && resWords[0] == "sign" && !AnyWord(resWords, NotSign)
                     : AnyWord(words, SignWords) && AnyWord(words, RoadSignWords)) p.Kind = PlaceKind.Sign;
            else if (AnyWord(words, FenceWords)) p.Kind = PlaceKind.Fence;
            else if (AnyWord(words, NotNature)) p.Kind = PlaceKind.Other;
            else if (c.IsTree || AnyWord(words, TreeWords)) p.Kind = AnyWord(words, BushWords) ? PlaceKind.Bush : PlaceKind.Tree;
            else if (res.Length > 0 ? IsRockResource(resWords) : AnyWord(words, RockWords)) p.Kind = PlaceKind.Rock;
            else if (AnyWord(words, BushWords)) p.Kind = PlaceKind.Bush;
            else p.Kind = PlaceKind.Other;
            bool known2 = inst.Length > 0 && Known.ContainsKey(inst);
            p.WaterPlant = (p.Kind == PlaceKind.Bush || p.Kind == PlaceKind.Other) && AnyWord(words, WaterPlantWords) && !AnyWord(words, IndoorWords);
            if (p.WaterPlant) p.Kind = PlaceKind.Bush;
            // a plant that isn't a SpeedTree and isn't one EA used as scenery is probably a buy-mode or garden plant
            // EA's neighbourhood plants are named in lower case (plantRanchWeeds, plantSwampCattail, plantHuckleberryEP07);
            // the buy-mode ones start with a capital (PlantLavender, PlantHedge, TreeOak, DeathFlower)
            bool scenery = res.StartsWith("plant") || res.StartsWith("bush") || res.StartsWith("shrub") || res.StartsWith("nbh");
            p.BuyPlant = (p.Kind == PlaceKind.Tree || p.Kind == PlaceKind.Bush) && !known2 && ((!c.IsTree && !scenery) || AnyWord(words, GardenWords));
            if (p.BuyPlant) p.WaterPlant = false;
            p.Conifer = Any(nameOnly, BiomeWords[Biome.Boreal]);
            p.WaterLover = Any(nameOnly, new[] { "willow", "cypress", "mangrove", "reed", "cattail", "palm", "birch", "alder" });
            p.Lowland = Any(nameOnly, new[] { "palm", "banana", "willow", "fruit", "apple", "orange", "lemon" });
            p.Palm = p.Kind == PlaceKind.Tree && (Any(nameOnly, new[] { "palm", "coconut" }) || Any(res.ToLowerInvariant(), new[] { "palm", "coconut" }));
            if (p.Kind == PlaceKind.Rock) p.RockSize = RockSizeOf(res.Length > 0 ? res : c.Name);
            return p;
        }

        /// <summary>Size class from EA's rock names (rockMountainSmallA, nbhRockBeachLargeB, RockGraniteXL01, rockChina_L_02...).</summary>
        public static int RockSizeOf(string name)
        {
            string l = name.ToLowerInvariant();
            if (l.Contains("xlarge") || l.Contains("extra large") || l.Contains("giant") || l.Contains("boulder") || l.Contains("_xl") || l.Contains("granitexl") || l.Contains("tall")) return 3;
            if (l.Contains("xsmall") || l.Contains("small")) return 0;
            if (l.Contains("medium")) return 1;
            if (l.Contains("large") || l.Contains("_l_") || l.Contains("smoothl")) return 2;
            return -1;
        }

        /// <summary>Writes every catalog item with what Auto-Place makes of it, for checking the classification.</summary>
        public static void DumpCatalog(List<PlaceItem> items, string path)
        {
            using (System.IO.StreamWriter w = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                w.WriteLine("kind,indoor,conifer,known_from_ea,name,instance,folder");
                foreach (PlaceItem p in items)
                {
                    string inst = p.Catalog.InstanceHex;
                    w.WriteLine(string.Format("{0},{1},{2},{3},\"{4}\",{5},\"{6}\"", p.Kind, p.Indoor ? 1 : 0, p.Conifer ? 1 : 0,
                        inst.Length > 0 && Known.ContainsKey(inst) ? 1 : 0, (p.Name ?? "").Replace("\"", "'"), inst, (p.Catalog.Path ?? "").Replace("\"", "'")));
                }
            }
        }

        public static bool InBiome(PlaceItem p, Biome b)
        {
            string n = p.Name.ToLowerInvariant();
            if (p.Kind == PlaceKind.Rock) return true;
            switch (b)
            {
                case Biome.MixedForest: return Any(n, BiomeWords[Biome.Temperate]) || Any(n, BiomeWords[Biome.Boreal]) || (p.Kind == PlaceKind.Bush && !Any(n, BiomeWords[Biome.Desert]) && !Any(n, BiomeWords[Biome.Tropical]));
                default:
                    if (Any(n, BiomeWords[b])) return true;
                    // generic bushes fit temperate-ish biomes
                    return p.Kind == PlaceKind.Bush && (b == Biome.Temperate || b == Biome.Boreal) &&
                           !Any(n, BiomeWords[Biome.Desert]) && !Any(n, BiomeWords[Biome.Tropical]) && !Any(n, BiomeWords[Biome.Mediterranean]);
            }
        }

        /// <summary>Guesses the biome from trees already in the world, else from the base ground colour.</summary>
        public static Biome DetectBiome(ICollection<string> existingNames, TextureFeatures baseGround, out string reason)
        {
            Dictionary<Biome, int> votes = new Dictionary<Biome, int>();
            foreach (string raw in existingNames)
            {
                string n = (raw ?? "").ToLowerInvariant();
                foreach (KeyValuePair<Biome, string[]> kv in BiomeWords)
                    if (Any(n, kv.Value)) { int v; votes.TryGetValue(kv.Key, out v); votes[kv.Key] = v + 1; }
            }
            Biome best = Biome.Temperate; int bestV = 0, total = 0;
            foreach (KeyValuePair<Biome, int> kv in votes) { total += kv.Value; if (kv.Value > bestV) { bestV = kv.Value; best = kv.Key; } }
            if (bestV >= 5)
            {
                int temp, bor; votes.TryGetValue(Biome.Temperate, out temp); votes.TryGetValue(Biome.Boreal, out bor);
                if ((best == Biome.Temperate || best == Biome.Boreal) && Math.Min(temp, bor) > 0.25f * Math.Max(temp, bor)) best = Biome.MixedForest;
                reason = "from " + total + " trees already in the world";
                return best;
            }
            if (baseGround != null)
            {
                if (baseGround.Hue < 50 && baseGround.Sat < 0.45f && baseGround.Light > 0.35f) { reason = "base ground looks dry and sandy"; return Biome.Desert; }
                if (baseGround.Hue >= 60 && baseGround.Hue <= 140 && baseGround.Sat > 0.45f && baseGround.Light > 0.3f) { reason = "base ground is lush green"; return Biome.Tropical; }
                if (baseGround.Hue >= 50 && baseGround.Hue < 70 && baseGround.Light > 0.35f) { reason = "base ground is yellow-green"; return Biome.Mediterranean; }
            }
            reason = "default";
            return Biome.MixedForest;
        }

        // ---------- density maps ----------

        public sealed class Maps
        {
            public Grid Forest, Bush, Rock, Blocked;   // 0..1 each; Blocked 1 = nothing may go here
            public bool WoodsOnlyInForest;             // PlaceSettings.WoodsOnlyInForest: the trees keep to the forest drawn, not to the roads
            public Grid Hard;                          // water only (roadside objects may go near roads; lots are only in Blocked)
            public Grid Slope, Rel, NearWater;
            public Grid Alt;           // height above the water as a share of the highest land (0 shore .. 1 peaks)
            public Grid FreshBank;     // 1 on the banks of rivers, streams and ponds (not the open sea)
            public Grid Water, Fresh;  // 1 = water / fresh (river, stream, pond) water
            public Grid FromLand;      // in water: metres to the nearest land
            public Grid Edge;          // 1 in the edge border (nothing placed there)
            public Grid RoadDist;      // metres to the nearest road centre line (null without roads)
            public Grid Beach;         // 1 on sandy beaches (by the sea, low and gentle): only palms there
            public Grid TreeSlope;     // share of trees kept for the slope: none where the World type paints rock
            public Grid NearBridge;    // 1 on and around bridges: no streetlights or signs
            // features for the learned placement score (#96)
            public Grid Slope40;       // degrees, 20 m-blurred heights (as tools/placescore.py)
            public Grid Hollow;        // metres above (+) or below (-) the mean ground within 30 m
            public Grid AboveSea;      // metres above the sea
            public Grid WaterDist;     // metres to the nearest water
            public bool Zones;         // mountain tree zones on
        }

        /// <summary>
        /// Density maps on a working grid ('h' at 'cell' metres). 'blockedExtra' marks roads/lots/edges already
        /// rasterised by the caller (1 = blocked).
        /// </summary>
        /// <summary>
        /// The painted plan at map cell (x, y) (#171): forest = full woodland with bushes at its edges, park = scattered
        /// trees, town centre and houses = a few garden trees, keep clear = nothing at all.
        /// </summary>
        private static void ApplyPlan(Maps m, PlaceSettings s, int x, int y, int W, int H, float clump, float scatter)
        {
            int n = s.PlanN, i = y * W + x;
            int px = Math.Min(n - 1, x * n / W), py = Math.Min(n - 1, y * n / H);
            switch ((PlanArea)s.PlanCodes[py * n + px])
            {
                case PlanArea.Forest:
                    m.Forest.D[i] = Math.Max(m.Forest.D[i], 0.75f + 0.25f * clump);
                    m.Bush.D[i] = Math.Max(m.Bush.D[i], 0.35f);
                    break;
                case PlanArea.Park:
                    m.Forest.D[i] = 0.35f * Grid.Ramp(scatter, 0.45f, 0.8f);
                    m.Rock.D[i] *= 0.3f;
                    break;
                case PlanArea.Centre:
                case PlanArea.Houses:
                    m.Forest.D[i] *= 0.3f; m.Rock.D[i] *= 0.3f;
                    break;
                case PlanArea.KeepClear:
                    m.Forest.D[i] = m.Bush.D[i] = m.Rock.D[i] = 0;
                    m.Blocked.D[i] = 1;
                    break;
                case PlanArea.None:
                case PlanArea.Countryside:
                    if (s.WoodsOnlyInForest) { m.Forest.D[i] *= 0.15f; m.Bush.D[i] *= 0.5f; }
                    break;
            }
        }

        public static Maps BuildMaps(Grid h, float cell, PlaceSettings s, Grid blockedExtra)
        {
            int W = h.W, H = h.H, N = W * H;
            float px = 1f / Math.Max(cell, 1e-3f);
            Maps m = new Maps();
            Grid hs = h.Blur(1.5f * px);
            m.Slope = new Grid(W, H);
            for (int y = 0; y < H; y++)
            {
                int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                for (int x = 0; x < W; x++)
                {
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1);
                    float gx = (hs[xp, y] - hs[xm, y]) / ((xp - xm) * cell), gy = (hs[x, yp] - hs[x, ym]) / ((yp - ym) * cell);
                    m.Slope.D[y * W + x] = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI);
                }
            }
            float lo, hi; h.MinMax(out lo, out hi);
            float range = Math.Max(1e-3f, hi - lo);
            Grid water = new Grid(W, H);
            m.Rel = new Grid(W, H); m.Alt = new Grid(W, H); m.Zones = s.AltitudeZones;
            float landSpan = Math.Max(1, hi - s.WaterLevel);
            for (int i = 0; i < N; i++)
            {
                float wl = s.WaterLevel + (s.WaterOffset != null ? s.WaterOffset.D[i] : 0);
                water.D[i] = h.D[i] < wl + 0.3f ? 1 : 0;
                m.Rel.D[i] = (h.D[i] - lo) / range;
                m.Alt.D[i] = Math.Max(0, Math.Min(1, (h.D[i] - s.WaterLevel) / landSpan));
            }
            m.NearWater = water.Blur(15f * px);
            // fresh water: everything but the sea. The sea = open water (over ~120 m across) joined to the map edge, or a
            // very large body, plus the water within 65 m of it. A river stays a river right down to its mouth and where it
            // widens inland (the tester's 2026-10-26 test: stretches of river near the coast and wider bends counted as sea;
            // the old rule called any water over ~60 m wide, or within 30 m of the map edge, sea)
            Grid inWater = LandDistance(water, cell), fresh = new Grid(W, H);
            Grid seaCore = OpenSea(water, inWater, cell);
            Grid notSea = new Grid(W, H);
            for (int i = 0; i < N; i++) notSea.D[i] = seaCore.D[i] > 0 ? 0 : 1;
            Grid toSea = LandDistance(notSea, cell);
            for (int i = 0; i < N; i++)
            {
                bool pond = s.WaterOffset != null && s.WaterOffset.D[i] > 0;
                bool atSea = seaCore.D[i] > 0 || toSea.D[i] <= SeaCore + 5;
                fresh.D[i] = water.D[i] > 0 && (pond || !atSea) ? 1 : 0;
            }
            m.Water = water; m.Fresh = fresh; m.FromLand = inWater;
            // learned placement score features (tools/placescore.py)
            Grid hs40 = h.Blur(20f * px), mean30 = h.Blur(17f * px), dry = new Grid(W, H);
            m.Slope40 = new Grid(W, H); m.Hollow = new Grid(W, H); m.AboveSea = new Grid(W, H);
            for (int y = 0; y < H; y++)
            {
                int ym = Grid.Clampi(y - 1, 0, H - 1), yp = Grid.Clampi(y + 1, 0, H - 1);
                for (int x = 0; x < W; x++)
                {
                    int xm = Grid.Clampi(x - 1, 0, W - 1), xp = Grid.Clampi(x + 1, 0, W - 1), i = y * W + x;
                    float gx = (hs40[xp, y] - hs40[xm, y]) / ((xp - xm) * cell), gy = (hs40[x, yp] - hs40[x, ym]) / ((yp - ym) * cell);
                    m.Slope40.D[i] = (float)(Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI);
                    m.Hollow.D[i] = h.D[i] - mean30.D[i];
                    m.AboveSea.D[i] = h.D[i] - s.WaterLevel;
                    dry.D[i] = 1 - water.D[i];
                }
            }
            m.WaterDist = LandDistance(dry, cell);
            Grid near = fresh.Blur(5f * px);
            m.FreshBank = new Grid(W, H);
            for (int i = 0; i < N; i++) m.FreshBank.D[i] = water.D[i] > 0 ? 0 : Grid.Ramp(near.D[i], 0.03f, 0.2f);

            Random rng = new Random(s.Seed);
            Grid woods = Grid.Fbm(W, H, 220f * px, 5, new Random(rng.Next()));
            Equalize(woods);
            Grid clumps = Grid.Fbm(W, H, 25f * px, 3, new Random(rng.Next()));
            Grid scatter = Grid.Fbm(W, H, 40f * px, 3, new Random(rng.Next()));

            m.WoodsOnlyInForest = s.PlanCodes != null && s.WoodsOnlyInForest;
            m.Forest = new Grid(W, H); m.Bush = new Grid(W, H); m.Rock = new Grid(W, H); m.Blocked = new Grid(W, H); m.Hard = new Grid(W, H);
            // the edge border: the distant terrain covers the map's rim, so nothing is placed there
            m.Edge = new Grid(W, H);
            int border = (int)Math.Ceiling(s.EdgeMargin * px);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (x < border || y < border || x >= W - border || y >= H - border) m.Edge.D[y * W + x] = 1;
            for (int i = 0; i < N; i++) m.Hard.D[i] = water.D[i] > 0 ? 1 : 0;
            float cover = s.ForestCover / 100f;
            for (int i = 0; i < N; i++)
            {
                float sl = m.Slope.D[i];
                bool blocked = water.D[i] > 0 || m.Edge.D[i] > 0 || (blockedExtra != null && blockedExtra.D[i] > 0);
                m.Blocked.D[i] = blocked ? 1 : 0;
                if (blocked) continue;
                // Woods: large connected patches covering 'cover' of the land, thinning on steep ground and at the tree line.
                float wood = Grid.Ramp(cover - woods.D[i] + 0.08f, 0, 0.16f);
                wood = Math.Max(wood, 0.6f * Grid.Ramp(m.NearWater.D[i], 0.03f, 0.25f) * Grid.Ramp(cover, 0, 0.3f)); // riverbanks
                wood *= (1 - Grid.Ramp(sl, 32, 45)) * (1 - 0.7f * Grid.Ramp(m.Rel.D[i], 0.9f, 1f));
                wood *= 0.7f + 0.6f * clumps.D[i];
                m.Forest.D[i] = Math.Min(1, wood);
                // Bushes: woodland edges and understorey, plus a light scatter in the open.
                float edge = Grid.Ramp(m.Forest.D[i], 0.05f, 0.4f) * (1 - Grid.Ramp(m.Forest.D[i], 0.6f, 1f));
                m.Bush.D[i] = Math.Min(1, (0.9f * edge + 0.25f * m.Forest.D[i] + 0.15f * scatter.D[i]) * (1 - Grid.Ramp(sl, 35, 50)));
                // Rocks: steep ground, high ground, and the odd one in the open.
                m.Rock.D[i] = Math.Min(1, 0.8f * Grid.Ramp(sl, 15, 35) + 0.3f * Grid.Ramp(m.Rel.D[i], 0.75f, 1f) * Grid.Ramp(sl, 5, 15)
                                          + 0.08f * Grid.Ramp(scatter.D[i], 0.7f, 1f));
                if (s.PlanCodes != null) ApplyPlan(m, s, i % W, i / W, W, H, clumps.D[i], scatter.D[i]);
            }
            // beaches: land within 15 m of the sea (not rivers or ponds), under 2 m above it and gentle, where Auto-Paint
            // puts sand; trees there only if they are palms (EA's beaches are bare or palm-lined)
            Grid sea = new Grid(W, H);
            for (int i = 0; i < N; i++) sea.D[i] = water.D[i] > 0 && fresh.D[i] == 0 ? 1 : 0;
            Grid nearSea = sea.Blur(6f * px);
            m.Beach = new Grid(W, H);
            for (int i = 0; i < N; i++)
                if (water.D[i] == 0 && nearSea.D[i] > 0.02f && h.D[i] < s.WaterLevel + 2f && m.Slope.D[i] < 10f) m.Beach.D[i] = 1;
            // trees thin out where the hillside becomes rock paint (the tester, 2026-10-24): from TreeSlopeStart (the World
            // type's style, or Auto-Paint's built-in rock rule from ~24 deg) to almost none 14 deg steeper
            m.TreeSlope = new Grid(W, H);
            for (int i = 0; i < N; i++) m.TreeSlope.D[i] = 1 - 0.95f * Grid.Ramp(m.Slope.D[i], s.TreeSlopeStart, s.TreeSlopeStart + 14);
            // bridges: no streetlights or signs on a bridge or within 15 m of its ends (EA's nearest lights are 20-60 m
            // from a bridge's middle, i.e. off the span)
            m.NearBridge = new Grid(W, H);
            if (s.Bridges != null)
                foreach (List<float[]> b in s.Bridges)
                    foreach (float[] q in b)
                    {
                        float r = 15f * px, cx = q[0] * W / Math.Max(1f, s.MapW), cy = q[1] * H / Math.Max(1f, s.MapH);
                        int x0 = Math.Max(0, (int)(cx - r)), x1 = Math.Min(W - 1, (int)(cx + r)), y0 = Math.Max(0, (int)(cy - r)), y1 = Math.Min(H - 1, (int)(cy + r));
                        for (int y = y0; y <= y1; y++)
                            for (int x = x0; x <= x1; x++)
                                if ((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy) <= r * r) m.NearBridge.D[y * W + x] = 1;
                    }
            return m;
        }

        /// <summary>
        /// The slope where a style's rock paint takes over: where its rock share has risen half way from flat ground to
        /// its steepest bins (Sunset Valley ~37 deg, Barnacle Bay ~38, Bridgeport ~8 -> 24), clamped to 24-40 deg.
        /// Styles whose rock does not grow with slope (Hidden Springs, Moonlight Falls) use 30 deg.
        /// </summary>
        public static float RockStart(float[] bins)
        {
            float[] centre = { 1.5f, 4.5f, 8, 12.5f, 17.5f, 22.5f, 27.5f, 32.5f, 37.5f, 45, 55 };
            if (bins == null || bins.Length < centre.Length) return 24;
            float flat = float.NaN, top = float.NaN;
            for (int k = 0; k < 3 && float.IsNaN(flat); k++) flat = bins[k];
            for (int k = centre.Length - 1; k >= 8 && float.IsNaN(top); k--) top = bins[k];
            if (float.IsNaN(flat) || float.IsNaN(top) || top - flat < 0.05f) return 30;
            float half = flat + 0.5f * (top - flat);
            for (int k = 1; k < centre.Length; k++)
                if (!float.IsNaN(bins[k]) && bins[k] >= half) return Math.Max(24, Math.Min(40, centre[k]));
            return 40;
        }

        /// <summary>For water cells (water = 1): metres to the nearest land (two-pass chamfer); 0 on land.</summary>
        /// <summary>
        /// The open sea's core: water at least SeaCore m from land (over ~120 m across), in bodies that touch the map edge
        /// or lie in a body of open water of 80 ha or more (a lagoon; Riverview's lakes are ~30 ha). Wide stretches of a river (Champs Les Sims' is 60-100 m) or an inland lake are left out.
        /// </summary>
        private const float SeaCore = 60f;

        private static Grid OpenSea(Grid water, Grid inWater, float cell)
        {
            int W = water.W, H = water.H;
            Grid core = new Grid(W, H);
            int[] comp = new int[W * H];
            float ha = cell * cell / 10000f;
            int id = 0;
            Queue<int> q = new Queue<int>();
            List<int> cells = new List<int>();
            for (int start = 0; start < comp.Length; start++)
            {
                if (comp[start] != 0 || water.D[start] <= 0 || inWater.D[start] < SeaCore) continue;
                id++; cells.Clear(); bool edge = false;
                comp[start] = id; q.Enqueue(start);
                while (q.Count > 0)
                {
                    int k = q.Dequeue(), x = k % W, y = k / W;
                    cells.Add(k);
                    if (x == 0 || y == 0 || x == W - 1 || y == H - 1) edge = true;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int n = ny * W + nx;
                        if (comp[n] != 0 || water.D[n] <= 0 || inWater.D[n] < SeaCore) continue;
                        comp[n] = id; q.Enqueue(n);
                    }
                }
                // a body touching the map edge reaches its 30 m line only short of the edge: count cells near it too
                if (!edge)
                    foreach (int k in cells)
                    {
                        int x = k % W, y = k / W;
                        if (Math.Min(Math.Min(x, y), Math.Min(W - 1 - x, H - 1 - y)) * cell <= 40f) { edge = true; break; }
                    }
                if (!edge && BodyHectares(water, inWater, cells[0], W, H, ha, 80f) >= 80f) edge = true; // a lagoon or large bay
                if (edge) foreach (int k in cells) core.D[k] = 1;
            }
            // open water joined to that sea by a short channel (a lagoon behind its reef, a harbour: within 400 m of open
            // sea through the water) is sea too; a lake a
            // river runs into further up is not
            int[] steps = new int[W * H];
            for (int k = 0; k < steps.Length; k++) steps[k] = -1;
            int reach = (int)Math.Ceiling(400f / cell);
            for (int k = 0; k < steps.Length; k++) if (core.D[k] > 0) { steps[k] = 0; q.Enqueue(k); }
            Dictionary<int, bool> joined = new Dictionary<int, bool>();
            while (q.Count > 0)
            {
                int k = q.Dequeue(), x = k % W, y = k / W;
                if (comp[k] > 0 && core.D[k] <= 0) joined[comp[k]] = true;
                if (steps[k] >= reach) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int n = ny * W + nx;
                    if (steps[n] >= 0 || water.D[n] <= 0) continue;
                    steps[n] = steps[k] + 1; q.Enqueue(n);
                }
            }
            if (joined.Count > 0)
                for (int k = 0; k < comp.Length; k++) if (comp[k] > 0 && joined.ContainsKey(comp[k])) core.D[k] = 1;
            return core;
        }

        /// <summary>Hectares of the open water (over ~30 m across) joined to cell 'start', counted up to 'cap'.</summary>
        private static float BodyHectares(Grid water, Grid inWater, int start, int W, int H, float ha, float cap)
        {
            Dictionary<int, bool> seen = new Dictionary<int, bool>();
            Queue<int> q = new Queue<int>(); q.Enqueue(start); seen[start] = true;
            while (q.Count > 0 && seen.Count * ha < cap)
            {
                int k = q.Dequeue(), x = k % W, y = k / W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int n = ny * W + nx;
                    if (seen.ContainsKey(n) || water.D[n] <= 0 || inWater.D[n] < 15f) continue;
                    seen[n] = true; q.Enqueue(n);
                }
            }
            return seen.Count * ha;
        }

        private static Grid LandDistance(Grid water, float cell)
        {
            int W = water.W, H = water.H;
            Grid d = new Grid(W, H);
            for (int i = 0; i < d.D.Length; i++) d.D[i] = water.D[i] > 0 ? 1e6f : 0;
            float a = cell, b = cell * 1.414f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = d[x, y];
                    if (v == 0) continue;
                    if (x > 0) v = Math.Min(v, d[x - 1, y] + a);
                    if (y > 0) v = Math.Min(v, d[x, y - 1] + a);
                    if (x > 0 && y > 0) v = Math.Min(v, d[x - 1, y - 1] + b);
                    if (x < W - 1 && y > 0) v = Math.Min(v, d[x + 1, y - 1] + b);
                    d[x, y] = v;
                }
            for (int y = H - 1; y >= 0; y--)
                for (int x = W - 1; x >= 0; x--)
                {
                    float v = d[x, y];
                    if (v == 0) continue;
                    if (x < W - 1) v = Math.Min(v, d[x + 1, y] + a);
                    if (y < H - 1) v = Math.Min(v, d[x, y + 1] + a);
                    if (x < W - 1 && y < H - 1) v = Math.Min(v, d[x + 1, y + 1] + b);
                    if (x > 0 && y < H - 1) v = Math.Min(v, d[x - 1, y + 1] + b);
                    d[x, y] = v;
                }
            return d;
        }

        private static void Equalize(Grid g)
        {
            const int B = 2048;
            int[] hist = new int[B];
            for (int i = 0; i < g.D.Length; i++) hist[Math.Min(B - 1, (int)(g.D[i] * B))]++;
            float[] cdf = new float[B]; long acc = 0;
            for (int b = 0; b < B; b++) { acc += hist[b]; cdf[b] = (float)acc / g.D.Length; }
            for (int i = 0; i < g.D.Length; i++) g.D[i] = cdf[Math.Min(B - 1, (int)(g.D[i] * B))];
        }

        // ---------- point placement ----------

        /// <summary>
        /// Picks positions. 'occupied' = existing objects as (mapX, mapY, radius in map pixels). Returns placements in
        /// map-pixel coordinates of the full map ('scale' = map pixels per working-grid cell).
        /// </summary>
        public static List<Placement> Place(Maps m, float cell, float scale, List<PlaceItem> items, PlaceSettings s, List<float[]> occupied)
        {
            List<Placement> result = new List<Placement>();
            Random rng = new Random(s.Seed * 31 + 5);
            int W = m.Forest.W, H = m.Forest.H;
            float mapPerMeter = scale / cell;
            SpatialHash hash = new SpatialHash(8f * mapPerMeter);
            if (occupied != null) foreach (float[] o in occupied) hash.Add(o[0], o[1], o[2]);

            m.RoadDist = s.Roads != null && s.Roads.Count > 0 ? RoadDistance(m, s.Roads, cell, scale) : null;
            // roadside things first, so nature keeps clear of them
            Streetlights(m, cell, scale, items, s, rng, hash, result);
            Signs(m, cell, scale, items, s, rng, hash, result);
            StreetDetails(m, cell, scale, items, s, rng, hash, result);
            // kind, spacing (m), keep-out radius (m), map, peak fill probability
            float treeFill = (0.05f + 0.75f * s.TreeDensity / 100f) * s.FillScale[0];
            // a Like world with its forests decoded (#66): trees in its groups and spacing (EA's nearest tree is 3-15 m)
            bool forests = s.Library != null && !s.Library.TreeCountIncomplete;
            // rocks in clusters (in EA's Appaloosa Plains 91% of rocks are within 15 m of another): seeds, then 2-6 around each.
            // Rocks go first; then each rock's own size keeps trees and bushes out of it (the tester's v0.38 test: trees
            // inside boulders). Rocks of a cluster may still touch, as EA's rock sets do.
            int before = result.Count;
            Pass(m, m.Rock, PlaceKind.Rock, 30f, 2.5f, s.Rocks / 100f * 0.9f * s.FillScale[2], cell, scale, items, rng, hash, result, s.Library, 4, false);
            for (int i = before; i < result.Count; i++)
                hash.Add(result[i].MapX, result[i].MapY, (RockRadius(items[result[i].Item].Name) + 0.8f) * mapPerMeter);
            Pass(m, m.Forest, PlaceKind.Tree, 9f, forests ? 1.8f : 3.5f, treeFill, cell, scale, items, rng, hash, result, s.Library, forests ? 6 : 1, false);
            Pass(m, m.Bush, PlaceKind.Bush, 7f, 1.5f, s.Bushes / 100f * 0.45f * s.FillScale[1], cell, scale, items, rng, hash, result, s.Library, 1, false);
            // water plants (reeds, cattails...) in clumps along river and pond banks, not by the sea
            if (s.WaterPlants)
                Pass(m, m.FreshBank, PlaceKind.Bush, 9f, 1.2f, 0.45f * s.FillScale[1], cell, scale, items, rng, hash, result, null, 3, true);
            // spawners have their own tool (SpawnerForm); 'other props' are not placed automatically
            return result;
        }

        /// <summary>
        /// With a World type's density matched, the sliders scale its figures (the tester, 2026-10-25: Woodland cover and Tree
        /// density did nothing, as the match rescaled every kind back to the world's own count). At the defaults (cover 45,
        /// trees 40, bushes 40, rocks 30) the world's own density; 0 = a tenth, 100 = 2.5x. Trees: density x cover/45
        /// (woodland cover = how much land is wooded, density = how packed the woods are).
        /// </summary>
        public static float SliderFactor(PlaceKind kind, PlaceSettings s)
        {
            switch (kind)
            {
                case PlaceKind.Tree: return Scale(s.TreeDensity, 40) * Math.Max(0.05f, s.ForestCover / 45f);
                case PlaceKind.Bush: return Scale(s.Bushes, 40);
                case PlaceKind.Rock: return Scale(s.Rocks, 30);
                default: return 1;
            }
        }

        private static float Scale(float v, float def) { return v <= def ? 0.1f + 0.9f * v / def : 1 + 1.5f * (v - def) / (100 - def); }

        /// <summary>
        /// Multi-pass: places, compares trees / bushes / rocks per hectare with the reference world, rescales each
        /// kind's fill, repeats (up to 4 passes). Returns the final placements; 'note' summarises the match.
        /// </summary>
        public static List<Placement> FitDensity(Maps m, float cell, float scale, List<PlaceItem> items, PlaceSettings s,
                                                 List<float[]> occupied, float landHectares, out string note)
        {
            List<Placement> plan = null;
            for (int k = 0; k < PlaceSettings.Kinds; k++) s.FillScale[k] = 1;
            note = "";
            for (int pass = 1; pass <= 4; pass++)
            {
                plan = Place(m, cell, scale, items, s, occupied);
                if (s.Library == null || !s.MatchLibraryDensity || landHectares <= 0) return plan;
                int[] n = new int[PlaceSettings.Kinds];
                foreach (Placement p in plan) n[(int)items[p.Item].Kind]++;
                bool done = true;
                note = "";
                foreach (PlaceKind kind in new[] { PlaceKind.Tree, PlaceKind.Bush, PlaceKind.Rock })
                {
                    if (kind == PlaceKind.Tree && s.Library.TreeCountIncomplete) continue; // the Trees sliders decide
                    if (n[(int)kind] == 0 && s.FillScale[(int)kind] == 1) continue; // kind not being placed
                    float want = s.Library.PerHectare[(int)kind] * SliderFactor(kind, s), have = n[(int)kind] / landHectares;
                    note += string.Format(" {0}: {1:F1}/ha (ref {2:F1})", kind, have, want);
                    if (want <= 0 || have <= 0) continue;
                    float ratio = want / have;
                    if (Math.Abs(ratio - 1) > 0.08f) done = false;
                    s.FillScale[(int)kind] = Math.Max(0.05f, Math.Min(20f, s.FillScale[(int)kind] * ratio));
                }
                note = "pass " + pass + ":" + note;
                if (done) break;
            }
            return plan;
        }

        /// <summary>
        /// Keeps the world within 'max' objects (the tester: ~4000 for performance). If the plan plus 'existing' is over,
        /// trees, bushes and rocks are thinned evenly (every kind by the same share, spread over the map); roadside
        /// signs and streetlights are kept. Returns the share kept (1 = nothing removed).
        /// </summary>
        public static float Budget(List<Placement> plan, List<PlaceItem> items, int existing, int max, int seed)
        {
            int fixedCount = 0, nature = 0;
            foreach (Placement p in plan) { PlaceKind k = items[p.Item].Kind; if (k == PlaceKind.Streetlight || k == PlaceKind.Sign) fixedCount++; else nature++; }
            int room = max - existing - fixedCount;
            if (nature == 0 || room >= nature) return 1;
            float keep = Math.Max(0, room) / (float)nature;
            Random rng = new Random(seed * 13 + 1);
            plan.RemoveAll(delegate (Placement p)
            {
                PlaceKind k = items[p.Item].Kind;
                return k != PlaceKind.Streetlight && k != PlaceKind.Sign && rng.NextDouble() >= keep;
            });
            return keep;
        }

        /// <summary>
        /// A rock's footprint radius in metres, from its EA resource name (the catalog has no sizes): giant boulders and
        /// cliffs ~6 m, boulders and large rocks ~4 m, small rocks and pebbles ~1.2 m, others ~2.5 m.
        /// </summary>
        public static float RockRadius(string name)
        {
            string n = (ResourceName(name) + " " + name).ToLowerInvariant();
            if (n.Contains("giant") || n.Contains("cliff") || n.Contains("huge") || n.Contains("formation") || n.Contains("arch")) return 6f;
            if (n.Contains("boulder") || n.Contains("large") || n.Contains("big")) return 4f;
            if (n.Contains("small") || n.Contains("pebble") || n.Contains("tiny")) return 1.2f;
            return 2.5f;
        }

        /// <summary>Share of trees kept at a height (0 shore .. 1 highest land): all up to half way, ~30% at 78%, ~4% at the tops.</summary>
        public static float TreeKeep(float alt)
        {
            return Math.Max(0.04f, 1 - 0.7f * Grid.Ramp(alt, 0.5f, 0.78f) - 0.26f * Grid.Ramp(alt, 0.78f, 0.92f));
        }

        /// <summary>The learned score (#96) in place of the habitat bands. Off by default: on generated worlds it scored
        /// slightly lower than the bands in tools/Fidelity (placement 81.7 -> 81.3%, seed 4; rocks on worlds with few of
        /// them, e.g. Champs Les Sims' 30, stood too far from roads). tools/Fidelity turns it on with FIDELITY_SCORE.</summary>
        public static bool UseLearnedScore = false;

        /// <summary>The learned placement score at cell i: the product of the library's likelihood ratios over the features
        /// the maps carry (slope, hollow, height above the sea, water and road distance), kept within 0.02-50.</summary>
        private static float Score(Maps m, ObjectLibrary lib, Dictionary<string, float[][]> kind, int i)
        {
            float v = lib.ScoreRatio(kind, "slope", m.Slope40.D[i]) * lib.ScoreRatio(kind, "hollow", m.Hollow.D[i])
                    * lib.ScoreRatio(kind, "above_sea", m.AboveSea.D[i]) * lib.ScoreRatio(kind, "water", m.WaterDist.D[i]);
            if (m.RoadDist != null) v *= lib.ScoreRatio(kind, "road", m.RoadDist.D[i]);
            return Math.Max(0.02f, Math.Min(50f, v));
        }

        private static void Pass(Maps m, Grid density, PlaceKind kind, float spacing, float radius, float fill, float cell, float scale,
                                 List<PlaceItem> items, Random rng, SpatialHash hash, List<Placement> result, ObjectLibrary lib, int clusterMax, bool waterPlants)
        {
            List<int> pool = new List<int>();
            for (int i = 0; i < items.Count; i++) if (items[i].Selected && items[i].Kind == kind && items[i].WaterPlant == waterPlants) pool.Add(i);
            if (pool.Count == 0 || fill <= 0) return;
            int W = density.W, H = density.H;
            float step = spacing / cell;                 // working-grid cells between candidates
            float mapR = radius / cell * scale;
            float[] w = new float[pool.Count];
            // the reference world's arrangement of this kind: how close to roads it ever comes (10th percentile of
            // EA's distances), and the groups it stands in (mean / 90th-percentile size, nearest-neighbour spacing)
            KindShape shape = lib != null && !waterPlants ? lib.ShapeOf(kind) : null;
            float minRoad = shape != null && !float.IsNaN(shape.RoadP10) ? Math.Min(40f, shape.RoadP10) : 0;
            // as near to roads as the reference world puts them (EA's rocks are mostly 30-240 m from a road, rarely
            // further). Trees only when the library holds the forests (cluster trees), not just the town's single trees.
            bool forests = kind != PlaceKind.Tree || (lib != null && !lib.TreeCountIncomplete);
            bool roadPref = shape != null && shape.Road != null && forests && !(m.WoodsOnlyInForest && kind == PlaceKind.Tree);
            // and where on the land: the reference world's slope / height / water bands for the whole kind, times its
            // road distances, replace the built-in rule (kept at half strength for local variety)
            RefObject habitat = lib != null && !waterPlants && forests && !(m.WoodsOnlyInForest && kind == PlaceKind.Tree) ? lib.KindHabitat(kind) : null;
            // the learned score (#96): EA's likelihood ratios for this kind over slope, hollows, height above the sea and
            // the distances to water and roads, multiplied (softened to the 0.8th power), replace the habitat bands
            Dictionary<string, float[][]> learnedScore = null;
            if (lib != null && !waterPlants && forests && UseLearnedScore && !(m.WoodsOnlyInForest && kind == PlaceKind.Tree)) lib.Scores.TryGetValue(kind.ToString(), out learnedScore);
            if (learnedScore != null)
            {
                Grid learned = new Grid(W, H);
                float max = 1e-6f;
                for (int i = 0; i < learned.D.Length; i++)
                {
                    float v = (float)Math.Pow(Score(m, lib, learnedScore, i), 0.8);
                    v *= kind == PlaceKind.Tree ? 0.1f + 0.9f * density.D[i] : 0.5f + 0.5f * density.D[i];
                    learned.D[i] = v; if (v > max) max = v;
                }
                for (int i = 0; i < learned.D.Length; i++) learned.D[i] /= max;
                density = learned;
                roadPref = false; habitat = null;
            }
            if (habitat != null)
            {
                Grid learned = new Grid(W, H);
                float max = 1e-6f;
                for (int i = 0; i < learned.D.Length; i++)
                {
                    float v = lib.Preference(habitat, Band(m.Slope.D[i], SlopeBands), Band(m.Rel.D[i], RelBands), Band(m.NearWater.D[i], WaterBands));
                    if (roadPref && m.RoadDist != null) v *= lib.RoadPreference(shape, Band(m.RoadDist.D[i], RoadBands));
                    // trees keep to the woodland patches (Woodland cover); other kinds keep the built-in rule at half strength
                    v *= kind == PlaceKind.Tree ? 0.1f + 0.9f * density.D[i] : 0.5f + 0.5f * density.D[i];
                    learned.D[i] = v; if (v > max) max = v;
                }
                for (int i = 0; i < learned.D.Length; i++) learned.D[i] /= max;
                density = learned;
                roadPref = false; // already in
            }
            int groupMax = clusterMax;
            float groupExtra = -1, groupGap = float.NaN;
            if (shape != null && clusterMax > 1 && !float.IsNaN(shape.GroupMean) && !float.IsNaN(shape.GroupP90))
            {
                groupMax = Math.Max(2, Math.Min(8, (int)Math.Round(shape.GroupP90)));
                groupExtra = Math.Max(0, shape.GroupMean - 1);
                // the share with a neighbour within 15 m: with pairs, w15 = 2q / (1 + q) for q partners per seed
                if (!float.IsNaN(shape.Within15)) groupExtra = Math.Max(groupExtra, shape.Within15 / (2 - Math.Min(1.9f, shape.Within15)));
                groupExtra = Math.Min(groupExtra, groupMax - 1);
                groupGap = float.IsNaN(shape.NnMedian) ? float.NaN : Math.Max(2f, Math.Min(15f, shape.NnMedian));
            }
            for (float gy = 0; gy < H; gy += step)
                for (float gx = 0; gx < W; gx += step)
                {
                    float x = gx + (float)rng.NextDouble() * step, y = gy + (float)rng.NextDouble() * step;
                    int ix = Math.Min(W - 1, (int)x), iy = Math.Min(H - 1, (int)y), i = iy * W + ix;
                    if (m.Blocked.D[i] > 0) continue;
                    if (minRoad > 0 && m.RoadDist != null && m.RoadDist.D[i] < minRoad) continue;
                    float near = roadPref && m.RoadDist != null ? lib.RoadPreference(shape, Band(m.RoadDist.D[i], RoadBands)) : 1;
                    if (rng.NextDouble() > density.D[i] * fill * near) continue;
                    // mountain zones: trees thin from half the land's height to a sparse tree line near the tops
                    if (m.Zones && kind == PlaceKind.Tree && rng.NextDouble() > TreeKeep(m.Alt.D[i])) continue;
                    if ((kind == PlaceKind.Tree || kind == PlaceKind.Bush) && !waterPlants && m.TreeSlope != null && rng.NextDouble() > m.TreeSlope.D[i]) continue;
                    bool beach = kind == PlaceKind.Tree && m.Beach != null && m.Beach.D[i] > 0;
                    if (beach && !AnyPalm(items, pool)) continue;
                    float mx = x * scale, my = y * scale;
                    if (hash.Near(mx, my, mapR)) continue;
                    // choose which item suits this spot
                    float total = 0;
                    for (int k = 0; k < pool.Count; k++)
                    {
                        PlaceItem it = items[pool[k]];
                        float pref = it.Weight;
                        if (lib != null && it.Ref != null && it.Ref.HasBands)
                            pref *= lib.Preference(it.Ref, Band(m.Slope.D[i], SlopeBands), Band(m.Rel.D[i], RelBands), Band(m.NearWater.D[i], WaterBands));
                        else
                        {
                            // habitat from the name: conifers higher (unless zones handle it), willows by water, fruit trees low
                            if (it.Conifer && !m.Zones) pref *= 0.6f + 1.2f * m.Rel.D[i] + 0.02f * m.Slope.D[i];
                            if (it.WaterLover) pref *= 0.4f + 4f * m.NearWater.D[i];
                            if (it.Lowland) pref *= 1.3f - m.Rel.D[i];
                        }
                        // a beach takes palms only; palms keep to the low ground near water
                        if (beach && !it.Palm) pref = 0;
                        // rocks by size: EA's bigger rocks stand on steeper ground (Sunset Valley median slope: small 9,
                        // medium 19, large 27, extra large 42 deg; Bridgeport, Hidden Springs, Twinbrook alike) and
                        // big ones by the water (Barnacle Bay, Sunlit Tides)
                        Dictionary<string, float[][]> sizeScore = null;
                        if (it.Kind == PlaceKind.Rock && it.RockSize >= 0 && learnedScore != null && lib.Scores.TryGetValue("Rock" + it.RockSize, out sizeScore))
                            pref *= (float)Math.Pow(Score(m, lib, sizeScore, i) / Math.Max(1e-3f, Score(m, lib, learnedScore, i)), 0.8); // EA's own sizes by place
                        else if (it.Kind == PlaceKind.Rock && it.RockSize >= 0)
                        {
                            float centre = 10 + 10 * it.RockSize, dz = (m.Slope.D[i] - centre) / 12f;
                            pref *= 0.15f + (float)Math.Exp(-dz * dz);
                            if (it.RockSize >= 2) pref *= 1 + 1.5f * Grid.Ramp(m.NearWater.D[i], 0.15f, 0.4f);
                        }
                        // mountain zones: leafy trees in the lowlands, mixed on the hills, spruce, pine and fir higher up
                        if (m.Zones && it.Kind == PlaceKind.Tree)
                        {
                            float a = m.Alt.D[i];
                            pref *= it.Conifer ? 0.35f + 2.2f * Grid.Ramp(a, 0.3f, 0.7f) : Math.Max(0.15f, 1.3f - 1.15f * Grid.Ramp(a, 0.35f, 0.75f));
                        }
                        w[k] = beach && !it.Palm ? 0 : Math.Max(0.01f, pref); total += w[k];
                    }
                    double r = rng.NextDouble() * total; int pick = 0;
                    for (; pick < pool.Count - 1; pick++) { r -= w[pick]; if (r <= 0) break; }
                    Add(result, hash, pool[pick], mx, my, (float)(rng.NextDouble() * Math.PI * 2), rng, mapR);
                    // a group around the seed, items picked the same way: as the reference world groups this kind
                    // (extra members drawn so the mean group size matches, up to its 90th-percentile size, spaced
                    // like its nearest neighbours), else 1..clusterMax more within 3-9 m
                    if (groupMax > 1)
                    {
                        int extra = 0;
                        if (groupExtra >= 0) { for (int e = 0; e < groupMax - 1; e++) if (rng.NextDouble() < groupExtra / (groupMax - 1)) extra++; }
                        else extra = rng.Next(1, clusterMax + 1);
                        float mpm = scale / cell; // map pixels per metre
                        for (int e = 0; e < extra; e++)
                        {
                            double ang = rng.NextDouble() * Math.PI * 2;
                            double dist = float.IsNaN(groupGap) ? 3 + rng.NextDouble() * 6 : groupGap * (0.6 + 0.8 * rng.NextDouble());
                            float cx = mx + (float)(Math.Cos(ang) * dist) * mpm, cy = my + (float)(Math.Sin(ang) * dist) * mpm;
                            int cix = (int)(cx / scale), ciy = (int)(cy / scale);
                            if (cix < 0 || ciy < 0 || cix >= W || ciy >= H || m.Blocked.D[ciy * W + cix] > 0) continue;
                            if (minRoad > 0 && m.RoadDist != null && m.RoadDist.D[ciy * W + cix] < minRoad) continue;
                            if (hash.Near(cx, cy, mapR * 0.8f)) continue;
                            double rr = rng.NextDouble() * total; int pk = 0;
                            for (; pk < pool.Count - 1; pk++) { rr -= w[pk]; if (rr <= 0) break; }
                            int ci = ciy * W + cix;
                            if (kind == PlaceKind.Tree && m.Beach != null && m.Beach.D[ci] > 0 && !items[pool[pk]].Palm) continue;
                            if ((kind == PlaceKind.Tree || kind == PlaceKind.Bush) && m.TreeSlope != null && rng.NextDouble() > m.TreeSlope.D[ci]) continue;
                            Add(result, hash, pool[pk], cx, cy, (float)(rng.NextDouble() * Math.PI * 2), rng, mapR * 0.8f);
                        }
                    }
                }
        }

        private static bool AnyPalm(List<PlaceItem> items, List<int> pool) { foreach (int i in pool) if (items[i].Palm) return true; return false; }

        private static void Add(List<Placement> result, SpatialHash hash, int item, float mx, float my, float yaw, Random rng, float r)
        {
            Placement p;
            p.Item = item; p.MapX = mx; p.MapY = my; p.Yaw = yaw; p.Variant = (uint)rng.Next();
            result.Add(p);
            hash.Add(mx, my, r);
        }

        private static List<int> PoolOf(List<PlaceItem> items, PlaceKind kind)
        {
            List<int> pool = new List<int>();
            for (int i = 0; i < items.Count; i++) if (items[i].Selected && items[i].Kind == kind) pool.Add(i);
            return pool;
        }

        private static int PickWeighted(List<PlaceItem> items, List<int> pool, Random rng)
        {
            float total = 0; foreach (int i in pool) total += Math.Max(0.01f, items[i].Weight);
            double r = rng.NextDouble() * total;
            foreach (int i in pool) { r -= Math.Max(0.01f, items[i].Weight); if (r <= 0) return i; }
            return pool[pool.Count - 1];
        }

        private static bool HardAt(Maps m, float mx, float my, float scale)
        {
            int x = (int)(mx / scale), y = (int)(my / scale);
            return x < 0 || y < 0 || x >= m.Hard.W || y >= m.Hard.H || m.Hard.D[y * m.Hard.W + x] > 0;
        }

        /// <summary>
        /// Streetlights along each road, one per 'StreetlightSpacing' m of road, facing the road, 6.2 m off the centre line
        /// (or the World type's offset); alternating sides, or in opposite pairs at the World type's share of stops; with a
        /// World type, only on the road nearest the junctions, up to the share of road that world lights.
        /// </summary>
        private static void Streetlights(Maps m, float cell, float scale, List<PlaceItem> items, PlaceSettings s, Random rng, SpatialHash hash, List<Placement> result)
        {
            List<int> pool = PoolOf(items, PlaceKind.Streetlight);
            if (pool.Count == 0 || s.Roads == null) return;
            float mpm = scale / cell, step = Math.Max(10f, s.StreetlightSpacing) * mpm, off = 6.2f * mpm; // EA: 6.2 m (Roaring Heights)
            ObjectLibrary lib = s.Library;
            if (lib != null && !float.IsNaN(lib.LightOffset)) off = Math.Max(3f, Math.Min(9f, lib.LightOffset)) * mpm;
            // EA lights only part of its roads, those in town: lit road is a median ~23 m from a junction, unlit ~80 m
            // (tools/placement.py, 15 worlds). Light the road nearest the junctions until the reference world's lit share.
            float maxJunction = float.MaxValue;
            if (lib != null && !float.IsNaN(lib.LitShare) && lib.LitShare < 0.95f && s.Junctions != null && s.Junctions.Count > 0)
                maxJunction = JunctionQuantile(s.Roads, s.Junctions, lib.LitShare, 4f * mpm);
            // some worlds stand their lights in opposite pairs (Riverview 74% with a light within 15 m, Shang Simla 70%,
            // Dragon Valley 49%), others alternate sides: a share of the stops get a light on both sides, and the stops
            // are spaced so the lights per km stay the same ('step' is metres of road per light)
            float pairs = lib != null ? lib.LightPairs() : 0;
            float stop = step * (1 + pairs), keep = Math.Min(step, 2 * off) * 0.4f;
            // one lamp type along each road (the tester: mixed lamps on one road looked random): CAW's pieces joined into
            // roads between junctions, each given one type from the pool
            List<Chain> chains = Chains(s.Roads, s.Junctions ?? new List<float[]>(), mpm, m.Forest.W * scale, m.Forest.H * scale);
            foreach (Chain chain in chains)
            {
                List<float[]> road = chain.P;
                int lamp = PickWeighted(items, pool, rng);
                float carry = stop * 0.5f; int side = rng.Next(2) == 0 ? 1 : -1;
                for (int k = 1; k < road.Count; k++)
                {
                    float ax = road[k - 1][0], ay = road[k - 1][1], bx = road[k][0], by = road[k][1];
                    float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    if (len < 1e-3f) continue;
                    float tx = (bx - ax) / len, ty = (by - ay) / len;
                    for (float d = carry; d < len; d += stop)
                    {
                        int first = side; side = -side;
                        if (maxJunction < float.MaxValue && NearestJunction(s.Junctions, ax + tx * d, ay + ty * d) > maxJunction) continue;
                        bool both = rng.NextDouble() < pairs;
                        for (int sd = first, n = 0; n < (both ? 2 : 1); n++, sd = -sd)
                        {
                            float px = ax + tx * d - ty * off * sd, py = ay + ty * d + tx * off * sd;
                            if (HardAt(m, px, py, scale) || BlockedEdge(m, px, py, scale) || NearBridge(m, px, py, scale) || hash.Near(px, py, keep)) continue;
                            // face the road: the lamp's front points back towards the centre line
                            float yaw = (float)Math.Atan2(ty * sd, -tx * sd) + LampTurn(items[lamp]);
                            Add(result, hash, lamp, px, py, yaw, rng, 2f * mpm);
                        }
                    }
                    carry = (carry - len) % stop; if (carry < 0) carry += stop;
                }
            }
        }

        /// <summary>
        /// Extra turn (radians) for lamp models whose front isn't the side they light: measured on EA's own worlds
        /// (library/lamp_facing.json, 30 lamp models in 22 worlds): most face the road (Basic 86%, City 92%, Country 98%),
        /// these three stand turned a quarter (their arm reaches sideways from the front). Lamps EA turns every way (round
        /// or two-armed: Aurora Skies' DOT08, Resort, Gothic, Future) face the road too.
        /// </summary>
        private static float LampTurn(PlaceItem item)
        {
            string r = ResourceName(item.Name).ToLowerInvariant();
            if (r == "lightingoutdoorstreetfrance" || r == "lightingoutdoorstreetcampus" || r == "lightingoutdoorstreeteuro") return -(float)Math.PI / 2;
            return 0;
        }

        private static float NearestJunction(List<float[]> js, float x, float y)
        {
            float best = float.MaxValue;
            foreach (float[] j in js) { float d = (j[0] - x) * (j[0] - x) + (j[1] - y) * (j[1] - y); if (d < best) best = d; }
            return (float)Math.Sqrt(best);
        }

        /// <summary>The distance to the nearest junction below which 'share' of the road length lies (sampled every 'step').</summary>
        private static float JunctionQuantile(List<List<float[]>> roads, List<float[]> js, float share, float step)
        {
            List<float> d = new List<float>();
            foreach (List<float[]> road in roads)
                for (int k = 1; k < road.Count; k++)
                {
                    float ax = road[k - 1][0], ay = road[k - 1][1], bx = road[k][0], by = road[k][1];
                    float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    for (float t = 0; t < len; t += step) d.Add(NearestJunction(js, ax + (bx - ax) * t / len, ay + (by - ay) * t / len));
                }
            if (d.Count == 0) return float.MaxValue;
            d.Sort();
            return d[Math.Min(d.Count - 1, (int)(share * d.Count))];
        }

        /// <summary>Metres from each working-grid cell to the nearest road centre line (roads in map coordinates).</summary>
        public static Grid RoadDistance(Maps m, List<List<float[]>> roads, float cell, float scale)
        {
            int W = m.Slope.W, H = m.Slope.H;
            Grid notRoad = new Grid(W, H);
            for (int i = 0; i < notRoad.D.Length; i++) notRoad.D[i] = 1;
            foreach (List<float[]> road in roads)
                for (int k = 1; k < road.Count; k++)
                {
                    float ax = road[k - 1][0] / scale, ay = road[k - 1][1] / scale, bx = road[k][0] / scale, by = road[k][1] / scale;
                    int n = (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) * 2) + 1;
                    for (int t = 0; t <= n; t++)
                    {
                        int x = (int)(ax + (bx - ax) * t / n), y = (int)(ay + (by - ay) * t / n);
                        if (x >= 0 && y >= 0 && x < W && y < H) notRoad.D[y * W + x] = 0;
                    }
                }
            return LandDistance(notRoad, cell); // chamfer distance from the road cells, in metres
        }

        // ---------- road signs ----------

        public enum SignRole { GiveWay, Curve, CurveLeft, CurveRight, Speed, Crossing, Directions }
        private delegate bool RoleTest(SignRole r);
        private delegate float RoleValue(SignRole r);

        /// <summary>What a road sign is for, from its EA resource name (signTraffic_yield, signTraffic_curvedLeft...).</summary>
        public static SignRole RoleOfSign(string name)
        {
            List<string> w = Words(ResourceName(name).Length > 0 ? ResourceName(name) : name);
            if (w.Contains("yield") || w.Contains("stop")) return SignRole.GiveWay;
            if (w.Contains("curved") || w.Contains("curve"))
                return w.Contains("left") ? SignRole.CurveLeft : w.Contains("right") ? SignRole.CurveRight : SignRole.Curve;
            if (w.Contains("speed") || w.Contains("limit") || w.Contains("slow")) return SignRole.Speed;
            if (w.Contains("directions") || w.Contains("direction") || w.Contains("route")) return SignRole.Directions;
            return SignRole.Crossing; // crossings (pedestrian, school, animal), "no llamas" and the like
        }

        private sealed class Chain { public List<float[]> P = new List<float[]>(); public bool StartAtJunction, StartAtEdge, EndAtEdge; }

        /// <summary>
        /// Road signs where a driver needs them, on the right-hand verge (or left), facing the traffic they're for:
        /// give way on the minor road at each junction; curve signs (left/right as the bend goes) before sharp bends;
        /// speed signs where roads come in from the map edge and on long roads; crossing signs spread along long roads;
        /// direction signs at crossroads. Only the kinds of sign that are ticked are used.
        /// </summary>
        private static void Signs(Maps m, float cell, float scale, List<PlaceItem> items, PlaceSettings s, Random rng, SpatialHash hash, List<Placement> result)
        {
            List<int> pool = PoolOf(items, PlaceKind.Sign);
            if (pool.Count == 0 || s.Roads == null) return;
            Dictionary<SignRole, List<int>> byRole = new Dictionary<SignRole, List<int>>();
            foreach (int i in pool)
            {
                SignRole r = RoleOfSign(items[i].Name);
                if (!byRole.ContainsKey(r)) byRole[r] = new List<int>();
                byRole[r].Add(i);
            }
            float mpm = scale / cell;                       // map pixels per metre
            // measured on Roaring Heights (EA): signs 6.2-6.4 m from the centre line, give way ~10 m from the junction centre
            float side = 6.3f * mpm, back = 10f * mpm;
            float hand = s.DriveRight ? 1 : -1;
            float mapW = m.Forest.W * scale, mapH = m.Forest.H * scale;
            List<float[]> js = s.Junctions ?? new List<float[]>();
            List<Chain> chains = Chains(s.Roads, js, mpm, mapW, mapH);
            // with a Like world: as many of each sign role per km of road as it has (tools/signrates.py), at its distance
            // from the centre line; without one, the built-in rules below
            ObjectLibrary lib = s.Library;
            float km = 0;
            foreach (Chain ch in chains) for (int k = 1; k < ch.P.Count; k++) km += Dist(ch.P[k - 1], ch.P[k]) / mpm / 1000f;
            Dictionary<SignRole, int> target = new Dictionary<SignRole, int>(), placed = new Dictionary<SignRole, int>();
            bool learned = lib != null && !float.IsNaN(lib.SignsPerKm(SignRole.GiveWay));
            if (learned)
                foreach (SignRole r in Enum.GetValues(typeof(SignRole)))
                {
                    target[r] = (int)Math.Round(lib.SignsPerKm(r) * km); placed[r] = 0;
                }
            RoleTest room = delegate (SignRole r) { return !learned || placed[r] < target[r]; };
            RoleValue sideOf = delegate (SignRole r)
            {
                float[] st; return learned && lib.SignStats.TryGetValue(r, out st) ? Math.Max(3.5f, Math.Min(8f, st[1])) * mpm : side;
            };

            // 1. give way on the minor road(s) at each junction
            List<int> giveWay = Pick(byRole, SignRole.GiveWay);
            List<float[]> signedJunctions = new List<float[]>();
            foreach (float[] j in js)
            {
                List<float[]> app = Approaches(chains, j, 15f * mpm, 20f * mpm); // unit directions towards the junction
                if (app.Count < 3) continue;
                // the through road is the pair of approaches most nearly opposite; the others give way
                int a0 = -1, b0 = -1; float best = 2;
                for (int a = 0; a < app.Count; a++)
                    for (int b = a + 1; b < app.Count; b++)
                    {
                        float d = app[a][0] * app[b][0] + app[a][1] * app[b][1];
                        if (d < best) { best = d; a0 = a; b0 = b; }
                    }
                // built-in: one sign, on a minor approach, and not at every crossing of a downtown grid (100 m apart);
                // learned: every minor approach until the Like world's count per km is reached
                if (giveWay != null && (learned ? room(SignRole.GiveWay) : !NearAny(signedJunctions, j, 100 * mpm)))
                    for (int a = 0; a < app.Count; a++)
                    {
                        if (a == a0 || a == b0 || !room(SignRole.GiveWay)) continue;
                        float[] d = app[a]; float sd = sideOf(SignRole.GiveWay);
                        float px = j[0] - d[0] * back + Right(d, hand)[0] * sd, py = j[1] - d[1] * back + Right(d, hand)[1] * sd;
                        if (PutSign(m, scale, items, giveWay, rng, hash, result, px, py, d, mpm, learned ? 8f : 20f)) // EA: signs, lights at a junction a few m apart
                        {
                            signedJunctions.Add(j);
                            if (!learned) break;
                            placed[SignRole.GiveWay]++;
                        }
                    }
                // a direction sign at crossroads
                List<int> dir = Pick(byRole, SignRole.Directions);
                if (dir != null && app.Count >= 4 && rng.NextDouble() < 0.5)
                {
                    float[] d = app[a0];
                    PutSign(m, scale, items, dir, rng, hash, result, j[0] - d[0] * (back + 4 * mpm) + Right(d, hand)[0] * side,
                            j[1] - d[1] * (back + 4 * mpm) + Right(d, hand)[1] * side, d, mpm);
                }
            }

            // 2. curve signs before sharp bends, for traffic in both directions
            List<int> curveL = Pick(byRole, SignRole.CurveLeft), curveR = Pick(byRole, SignRole.CurveRight), curveAny = Pick(byRole, SignRole.Curve);
            if (curveL != null || curveR != null || curveAny != null)
                foreach (Chain ch in chains)
                    for (int dirn = 0; dirn < 2; dirn++)
                    {
                        List<float[]> p = Resample(ch.P, 4f * mpm);
                        if (dirn == 1) p.Reverse();
                        float lastSign = -1e9f, along = 0;
                        for (int k = 1; k + 12 < p.Count; k++)
                        {
                            along += Dist(p[k - 1], p[k]);
                            // the turn over the next ~48 m
                            float[] d1 = Unit(p[k - 1], p[k]), d2 = Unit(p[k + 11], p[k + 12]);
                            float turn = (float)(Math.Acos(Math.Max(-1, Math.Min(1, d1[0] * d2[0] + d1[1] * d2[1]))) * 180 / Math.PI);
                            if (turn < 45 || along - lastSign < 150 * mpm) continue;
                            // the sign 35 m before the bend starts
                            int at = k - (int)(35f / 4f);
                            if (at < 1) continue;
                            float cross = d1[0] * d2[1] - d1[1] * d2[0];
                            bool rightBend = cross * hand > 0;
                            List<int> use = (rightBend ? curveR : curveL) ?? curveAny ?? (rightBend ? curveL : curveR);
                            SignRole ur = use == curveR ? SignRole.CurveRight : use == curveL ? SignRole.CurveLeft : SignRole.Curve;
                            // learned: once that direction's quota is full, the general "curved road" sign
                            if (!room(ur) && curveAny != null && room(SignRole.Curve)) { use = curveAny; ur = SignRole.Curve; }
                            if (!room(ur)) continue;
                            float[] d = Unit(p[at - 1], p[at]); float sd = sideOf(ur);
                            if (NearAny(js, p[at], 30 * mpm)) continue;
                            if (PutSign(m, scale, items, use, rng, hash, result, p[at][0] + Right(d, hand)[0] * sd, p[at][1] + Right(d, hand)[1] * sd, d, mpm))
                            {
                                lastSign = along;
                                if (learned) placed[ur]++;
                            }
                        }
                    }

            // 3. speed signs where a road comes in from the map edge, and one on each long road
            List<int> speed = Pick(byRole, SignRole.Speed);
            if (speed != null)
                foreach (Chain ch in chains)
                {
                    List<float[]> p = Resample(ch.P, 4f * mpm);
                    if (p.Count < 30 || !room(SignRole.Speed)) continue;
                    float sd = sideOf(SignRole.Speed);
                    int before = result.Count;
                    if (ch.StartAtEdge || ch.EndAtEdge)
                    {
                        if (!ch.StartAtEdge) p.Reverse(); // travel inwards from the edge
                        int at = Math.Min(p.Count - 2, (int)(90f / 4f)); // 90 m in (past the edge border)
                        float[] d = Unit(p[at - 1], p[at]);
                        PutSign(m, scale, items, speed, rng, hash, result, p[at][0] + Right(d, hand)[0] * sd, p[at][1] + Right(d, hand)[1] * sd, d, mpm);
                    }
                    else if (p.Count * 4 > 400 && ch.StartAtJunction)
                    {
                        int at = (int)(40f / 4f);
                        float[] d = Unit(p[at - 1], p[at]);
                        PutSign(m, scale, items, speed, rng, hash, result, p[at][0] + Right(d, hand)[0] * sd, p[at][1] + Right(d, hand)[1] * sd, d, mpm);
                    }
                    if (learned) placed[SignRole.Speed] += result.Count - before;
                }

            // 4. crossing signs, about one per 500 m of road, away from junctions; with a Like world whose crossing signs
            // stand at junctions (median < 30 m from one, e.g. Sunset Valley ~10 m), on the junction approaches first
            List<int> crossing = Pick(byRole, SignRole.Crossing);
            float[] cst;
            if (crossing != null && learned && lib.SignStats.TryGetValue(SignRole.Crossing, out cst) && cst[2] > 0 && cst[2] < 30)
            {
                float sd = sideOf(SignRole.Crossing), dist = Math.Max(6f, cst[2]) * mpm;
                foreach (float[] j in js)
                    foreach (float[] d in Approaches(chains, j, 15f * mpm, 20f * mpm))
                    {
                        if (!room(SignRole.Crossing)) break;
                        if (rng.NextDouble() < 0.3) continue; // not every approach
                        // for the traffic leaving the junction (the give-way sign has the incoming side)
                        float[] o = { -d[0], -d[1] };
                        float px = j[0] - d[0] * dist + Right(o, hand)[0] * sd, py = j[1] - d[1] * dist + Right(o, hand)[1] * sd;
                        if (PutSign(m, scale, items, crossing, rng, hash, result, px, py, o, mpm, 8f)) placed[SignRole.Crossing]++;
                    }
            }
            if (crossing != null)
                foreach (Chain ch in chains)
                {
                    List<float[]> p = Resample(ch.P, 4f * mpm);
                    // built-in: one per 500 m; learned: the Like world's count per km (random rounding)
                    double want = learned ? lib.SignsPerKm(SignRole.Crossing) * p.Count * 4 / 1000.0 : p.Count * 4 / 500.0;
                    int n = (int)want + (learned && rng.NextDouble() < want - Math.Floor(want) ? 1 : 0);
                    float sd = sideOf(SignRole.Crossing);
                    int here = 0;
                    for (int c = 0; c < (learned ? n * 4 : n) && here < n && room(SignRole.Crossing); c++) // learned: up to 4 tries per sign
                    {
                        int at = 15 + rng.Next(Math.Max(1, p.Count - 30));
                        if (at < 1 || at >= p.Count || NearAny(js, p[at], 50 * mpm)) continue;
                        float[] d = Unit(p[at - 1], p[at]);
                        if (rng.Next(2) == 1) d = new[] { -d[0], -d[1] };
                        if (PutSign(m, scale, items, crossing, rng, hash, result, p[at][0] + Right(d, hand)[0] * sd, p[at][1] + Right(d, hand)[1] * sd, d, mpm, learned ? 8f : 20f))
                        {
                            here++;
                            if (learned) placed[SignRole.Crossing]++;
                        }
                    }
                }
        }

        private static List<int> Pick(Dictionary<SignRole, List<int>> byRole, SignRole r)
        {
            List<int> l; return byRole.TryGetValue(r, out l) && l.Count > 0 ? l : null;
        }

        /// <summary>The right-hand side of travel direction d (x right, z down as on the map), or the left with hand = -1.</summary>
        private static float[] Right(float[] d, float hand) { return new[] { -d[1] * hand, d[0] * hand }; }

        /// <summary>Places one sign at (px, py) facing traffic travelling along d; false if the spot is taken or unusable.</summary>
        private static bool PutSign(Maps m, float scale, List<PlaceItem> items, List<int> pool, Random rng, SpatialHash hash, List<Placement> result,
                                    float px, float py, float[] d, float mpm, float keepOut = 20f)
        {
            if (pool == null || HardAt(m, px, py, scale) || BlockedEdge(m, px, py, scale) || NearBridge(m, px, py, scale) || hash.Near(px, py, keepOut * mpm)) return false;
            // the sign's face towards the oncoming traffic (front = -d; same yaw convention as the streetlights)
            float yaw = (float)Math.Atan2(-d[0], -d[1]);
            Add(result, hash, PickWeighted(items, pool, rng), px, py, yaw, rng, 2f * mpm);
            return true;
        }

        // ---------- street details ----------

        /// <summary>The base-game street details (resource name, instance; group 0), measured in EA's layer files.</summary>
        public static readonly string[] DetailNames = { "RoadGrateDrain", "RoadSewer", "FireHydrant", "nbhFenceSplitrail", "nbhFenceSplitrailPost",
            "SignWelcome", "SignNbhd_traditional", "SignNbhd_beach", "SignNbhd_contemporary", "SignNbhd_country", "SignNbhd_mission", "SignNbhd_Modern",
            "SignBillboard", "SignBillboard1", "SignBillboard2", "SignBillboard3", "SignBillboard4" };
        public static readonly ulong[] DetailIds = { 0xdafUL, 0xcebUL, 0xd19UL, 0xb37UL, 0xb38UL,
            0xd3dUL, 0xca0UL, 0xca1UL, 0xca2UL, 0xca3UL, 0xca4UL, 0xcb9UL,
            0x7ccUL, 0xd78UL, 0xd79UL, 0xd7aUL, 0xd7bUL };
        public static readonly StreetDetail[] DetailRoles = { StreetDetail.Drain, StreetDetail.Manhole, StreetDetail.Hydrant, StreetDetail.Fence, StreetDetail.FencePost,
            StreetDetail.Welcome, StreetDetail.NbhdSign, StreetDetail.NbhdSign, StreetDetail.NbhdSign, StreetDetail.NbhdSign, StreetDetail.NbhdSign, StreetDetail.NbhdSign,
            StreetDetail.Billboard, StreetDetail.Billboard, StreetDetail.Billboard, StreetDetail.Billboard, StreetDetail.Billboard };

        /// <summary>True for the town signs and billboards (their own Auto-Place option), false for the street details.</summary>
        public static bool IsTownSign(StreetDetail d) { return d == StreetDetail.Welcome || d == StreetDetail.NbhdSign || d == StreetDetail.Billboard; }

        /// <summary>
        /// Drains, manholes, fire hydrants and split-rail fences, placed as EA's worlds place them (scratch street study on
        /// the layer files of Sunset Valley, Hidden Springs, Sims University and 9 more):
        /// - drains (RoadGrateDrain): at town junctions (3+ lots within 80 m), two on each road arriving, one at each kerb,
        ///   6.5 m from the junction centre and 3.7 m off the centre line, facing across the road (Sunset Valley: 6 at a T,
        ///   8 at a crossroads, at 18 of its 30 junctions; the rest have no lots near); two more before a town dead end;
        /// - manholes (RoadSewer): in the lane, 1.9 m off the centre line, on lot-lined roads, at least 70 m apart (Sunset
        ///   Valley: 40 on 7.9 km, 5.1 per km, nearest 46 m apart);
        /// - hydrants (FireHydrant): on the verge 5.5 m out facing the road, on lot-lined roads, at least 140 m apart, and at
        ///   about a third of the town junctions 8 m in (Hidden Springs, Sims University: 0.8-2.4 per km, 58-90 m apart,
        ///   75-83% beside a lot);
        /// - split-rail fences: chains of 3-8 rails 6 m long, each rail turned across the run, a post at each end, about one
        ///   chain per km of road: along country roads 10-14 m out, or out from a lot into its field (12 worlds: 3-9 chains
        ///   of 15-60 m each).
        /// </summary>
        private static void StreetDetails(Maps m, float cell, float scale, List<PlaceItem> items, PlaceSettings s, Random rng, SpatialHash hash, List<Placement> result)
        {
            if (s.Roads == null || s.Roads.Count == 0) return;
            int[] pick = new int[(int)StreetDetail.Billboard + 1];
            for (int i = 0; i < pick.Length; i++) pick[i] = -1;
            for (int i = 0; i < items.Count; i++) if (items[i].Selected && items[i].Kind == PlaceKind.Detail) pick[(int)items[i].Detail] = i;
            bool any = false;
            foreach (int k in pick) if (k >= 0) any = true;
            if (!any) return; // (and the random sequence for the rest stays as it was)
            float mpm = scale / cell;
            TownSigns(m, scale, mpm, items, s, rng, hash, result);
            List<float[]> lots = s.Lots ?? new List<float[]>();
            // lots within 'r' metres of a map point
            Func2 lotsNear = delegate (float x, float y, float r)
            {
                int n = 0; float rr = r * mpm;
                foreach (float[] l in lots) if ((l[0] - x) * (l[0] - x) + (l[1] - y) * (l[1] - y) < (rr + l[2]) * (rr + l[2])) n++;
                return n;
            };
            List<float[]> junctions = s.Junctions ?? new List<float[]>();
            float km = 0;
            foreach (List<float[]> road in s.Roads)
                for (int k = 1; k < road.Count; k++) km += Dist(road[k - 1], road[k]) / mpm / 1000f;

            // drains at town junctions and before town dead ends
            if (pick[(int)StreetDetail.Drain] >= 0)
            {
                foreach (float[] j in junctions)
                {
                    if (lotsNear(j[0], j[1], 80) < 3) continue;
                    foreach (float[] u in RoadsLeaving(s.Roads, j, 15 * mpm))
                        Kerbs(m, scale, mpm, hash, result, rng, pick[(int)StreetDetail.Drain], j[0] + u[0] * 6.5f * mpm, j[1] + u[1] * 6.5f * mpm, u, 3.7f);
                }
                foreach (List<float[]> road in s.Roads)
                    for (int e = 0; e < 2; e++)
                    {
                        float[] tip = e == 0 ? road[0] : road[road.Count - 1], prev = e == 0 ? road[Math.Min(1, road.Count - 1)] : road[Math.Max(0, road.Count - 2)];
                        if (NearAny(junctions, tip, 15 * mpm) || EndJoined(s.Roads, road, tip, 6 * mpm) || BlockedEdge(m, tip[0], tip[1], scale)) continue;
                        if (lotsNear(tip[0], tip[1], 60) < 1) continue;
                        float l = Dist(prev, tip); if (l < 1e-3f) continue;
                        float[] u = { (prev[0] - tip[0]) / l, (prev[1] - tip[1]) / l };
                        Kerbs(m, scale, mpm, hash, result, rng, pick[(int)StreetDetail.Drain], tip[0] + u[0] * 6.5f * mpm, tip[1] + u[1] * 6.5f * mpm, u, 3.7f);
                    }
            }
            // manholes and hydrants along lot-lined roads, kept apart from others of their kind (the roads come in pieces of
            // ~45 m, so the spacing is a distance to the nearest one, not a count along each piece)
            List<float[]> manholes = new List<float[]>(), hydrants = new List<float[]>();
            float manholeGap = 70 * mpm, hydrantGap = 140 * mpm; // tuned to EA's rates: manholes ~5 per km (Sunset Valley), hydrants 1-2.5
            foreach (List<float[]> road in s.Roads)
                for (int k = 1; k < road.Count; k++)
                {
                    float ax = road[k - 1][0], ay = road[k - 1][1], len = Dist(road[k - 1], road[k]);
                    if (len < 1e-3f) continue;
                    float tx = (road[k][0] - ax) / len, ty = (road[k][1] - ay) / len;
                    for (float d = (float)rng.NextDouble() * 6 * mpm; d < len; d += 6 * mpm)
                    {
                        float x = ax + tx * d, y = ay + ty * d;
                        if (lotsNear(x, y, 40) == 0) continue;
                        float[] here = { x, y };
                        if (pick[(int)StreetDetail.Manhole] >= 0 && !NearAny(manholes, here, manholeGap) && rng.NextDouble() < 0.5)
                        {
                            float sd = rng.Next(2) == 0 ? 1 : -1;
                            float px = x - ty * 1.9f * mpm * sd, py = y + tx * 1.9f * mpm * sd;
                            if (!NearBridge(m, px, py, scale) && !BlockedEdge(m, px, py, scale) && !hash.Near(px, py, 6 * mpm))
                            {
                                Add(result, hash, pick[(int)StreetDetail.Manhole], px, py, (float)(rng.NextDouble() * Math.PI * 2), rng, 1f * mpm);
                                manholes.Add(here);
                            }
                        }
                        if (pick[(int)StreetDetail.Hydrant] >= 0 && !NearAny(hydrants, here, hydrantGap) && !NearAny(junctions, here, 12 * mpm) && rng.NextDouble() < 0.3)
                        {
                            float sd = rng.Next(2) == 0 ? 1 : -1;
                            float px = x - ty * 5.5f * mpm * sd, py = y + tx * 5.5f * mpm * sd;
                            if (!HardAt(m, px, py, scale) && !NearBridge(m, px, py, scale) && !BlockedEdge(m, px, py, scale) && !hash.Near(px, py, 4 * mpm))
                            {
                                Add(result, hash, pick[(int)StreetDetail.Hydrant], px, py, (float)Math.Atan2(ty * sd, -tx * sd), rng, 1f * mpm);
                                hydrants.Add(here);
                            }
                        }
                    }
                }
            // a hydrant at about a third of the town junctions, 8 m in along one road, on the verge
            if (pick[(int)StreetDetail.Hydrant] >= 0)
                foreach (float[] j in junctions)
                {
                    if (lotsNear(j[0], j[1], 80) < 3 || rng.NextDouble() > 0.3) continue;
                    List<float[]> ways = RoadsLeaving(s.Roads, j, 15 * mpm);
                    if (ways.Count == 0) continue;
                    float[] u = ways[rng.Next(ways.Count)];
                    float sd = rng.Next(2) == 0 ? 1 : -1;
                    float px = j[0] + u[0] * 8 * mpm - u[1] * 5.5f * mpm * sd, py = j[1] + u[1] * 8 * mpm + u[0] * 5.5f * mpm * sd;
                    if (!HardAt(m, px, py, scale) && !BlockedEdge(m, px, py, scale) && !hash.Near(px, py, 4 * mpm) && !NearAny(hydrants, new[] { px, py }, 40 * mpm))
                    {
                        Add(result, hash, pick[(int)StreetDetail.Hydrant], px, py, (float)Math.Atan2(u[1] * sd, -u[0] * sd), rng, 1f * mpm);
                        hydrants.Add(new[] { px, py });
                    }
                }
            // split-rail fences
            if (pick[(int)StreetDetail.Fence] >= 0)
            {
                int want = Math.Max(2, Math.Min(12, (int)Math.Round(km))), made = 0;
                for (int tries = 0; tries < want * 40 && made < want; tries++)
                {
                    List<float[]> road = s.Roads[rng.Next(s.Roads.Count)];
                    if (road.Count < 2) continue;
                    int k = 1 + rng.Next(road.Count - 1);
                    float len = Dist(road[k - 1], road[k]); if (len < 1e-3f) continue;
                    float tx = (road[k][0] - road[k - 1][0]) / len, ty = (road[k][1] - road[k - 1][1]) / len;
                    float t = (float)rng.NextDouble() * len, x = road[k - 1][0] + tx * t, y = road[k - 1][1] + ty * t;
                    if (NearAny(junctions, new[] { x, y }, 30 * mpm)) continue;
                    float sd = rng.Next(2) == 0 ? 1 : -1;
                    float[] start, dir;
                    if (lotsNear(x, y, 70) == 0)
                    {
                        // a country road: the fence runs beside it, 10-14 m out
                        float off = (10 + 4 * (float)rng.NextDouble()) * mpm;
                        start = new[] { x - ty * off * sd, y + tx * off * sd }; dir = new[] { tx, ty };
                    }
                    else
                    {
                        // out from the side of a lot into its field: start just beyond the lot, run away from the road
                        float[] lot = NearestLot(lots, x, y);
                        if (lot == null) continue;
                        float vx = lot[0] - x, vy = lot[1] - y, vl = (float)Math.Sqrt(vx * vx + vy * vy);
                        if (vl < 1e-3f || vl > 60 * mpm) continue;
                        float side = (tx * vy - ty * vx) > 0 ? 1 : -1; // along the road to the lot's far side
                        start = new[] { lot[0] + tx * (lot[2] + 2 * mpm) * side * sd, lot[1] + ty * (lot[2] + 2 * mpm) * side * sd };
                        dir = new[] { vx / vl, vy / vl };
                    }
                    if (Fence(m, cell, scale, mpm, hash, result, rng, pick[(int)StreetDetail.Fence], pick[(int)StreetDetail.FencePost], start, dir, 3 + rng.Next(6))) made++;
                }
            }
        }

        /// <summary>
        /// Welcome signs, neighbourhood signs and billboards, placed as in EA's layer files (scratch townobj study, 21 worlds;
        /// facing = (sin yaw, cos yaw)). Distances from the town core are in units of the median lot's distance from it.
        /// - welcome (SignWelcome; 7 in 5 worlds): on the roads into town just beyond the last lots (1.9 x, no lot within
        ///   100 m), 7.8 m from the centre line, facing away from the town (read by traffic coming in); 71% on main roads;
        /// - neighbourhood (SignNbhd_*; 32 in 11 worlds, 0.34 per km): 8.5 m out, ~38 m from a junction on the road into a
        ///   group of lots (5 within 100 m), 0.7-1.4 x from the core, facing along the road; 72% on main roads and collectors;
        /// - billboards (SignBillboard*; 100 in 10 worlds, 1.1 per km where a world has them): 12.6 m from the centre line
        ///   (7.5-20), at least 15 m from a junction (median 43), never on a lot (median 21 m from its edge), 0.6-1.3 x from
        ///   the core, facing along the road, 67% on the right of the traffic they face.
        /// </summary>
        private static void TownSigns(Maps m, float scale, float mpm, List<PlaceItem> items, PlaceSettings s, Random rng, SpatialHash hash, List<Placement> result)
        {
            List<int> welcome = new List<int>(), nbhd = new List<int>(), bill = new List<int>();
            for (int i = 0; i < items.Count; i++)
                if (items[i].Selected && items[i].Kind == PlaceKind.Detail)
                {
                    if (items[i].Detail == StreetDetail.Welcome) welcome.Add(i);
                    else if (items[i].Detail == StreetDetail.NbhdSign) nbhd.Add(i);
                    else if (items[i].Detail == StreetDetail.Billboard) bill.Add(i);
                }
            List<float[]> lots = s.Lots ?? new List<float[]>();
            if (welcome.Count + nbhd.Count + bill.Count == 0 || lots.Count < 5) return;
            // the town core = the lot with the most lots within 200 m; distances from it in median-lot units
            float[] core = null; int best = -1;
            foreach (float[] a in lots)
            {
                int n = 0;
                foreach (float[] b in lots) if (Dist(a, b) < 200 * mpm) n++;
                if (n > best) { best = n; core = a; }
            }
            List<float> ds = new List<float>();
            foreach (float[] l in lots) ds.Add(Dist(l, core));
            ds.Sort();
            float med = Math.Max(50 * mpm, ds[ds.Count / 2]);
            Func2 lotsNear = delegate (float x, float y, float r)
            {
                int n = 0; float rr = r * mpm;
                foreach (float[] l in lots) if ((l[0] - x) * (l[0] - x) + (l[1] - y) * (l[1] - y) < (rr + l[2]) * (rr + l[2])) n++;
                return n;
            };
            List<float[]> junctions = s.Junctions ?? new List<float[]>();
            float km = 0;
            foreach (List<float[]> road in s.Roads)
                for (int k = 1; k < road.Count; k++) km += Dist(road[k - 1], road[k]) / mpm / 1000f;
            float hand = s.DriveRight ? 1 : -1;
            List<float[]> placed = new List<float[]>();

            // welcome signs: where a road crosses 1.9 x the median lot distance going out of town, with no lot near
            if (welcome.Count > 0)
            {
                List<float[]> spots = new List<float[]>(); // x, y, outward dx, dy, how far the road reaches out
                foreach (List<float[]> road in s.Roads)
                {
                    float reach = 0;
                    foreach (float[] p in road) reach = Math.Max(reach, Dist(p, core));
                    for (int k = 1; k < road.Count; k++)
                    {
                        float r0 = Dist(road[k - 1], core) / med, r1 = Dist(road[k], core) / med;
                        if ((r0 < 1.9f) == (r1 < 1.9f)) continue;
                        float t = (1.9f - r0) / (r1 - r0), x = road[k - 1][0] + (road[k][0] - road[k - 1][0]) * t, y = road[k - 1][1] + (road[k][1] - road[k - 1][1]) * t;
                        float l = Dist(road[k - 1], road[k]); if (l < 1e-3f || lotsNear(x, y, 100) > 0) continue;
                        float sg = r1 > r0 ? 1 : -1; // outward along the road
                        spots.Add(new[] { x, y, (road[k][0] - road[k - 1][0]) / l * sg, (road[k][1] - road[k - 1][1]) / l * sg, reach });
                    }
                }
                spots.Sort(delegate (float[] a, float[] b) { return b[4].CompareTo(a[4]); }); // roads reaching furthest out first
                int want = Math.Max(1, Math.Min(3, (int)Math.Round(km * 0.17f + 0.5f)));
                foreach (float[] sp in spots)
                {
                    if (want == 0) break;
                    if (NearAny(placed, sp, 200 * mpm)) continue;
                    float[] d = { -sp[2], -sp[3] }; // traffic coming into town
                    float[] rt = Right(d, hand);
                    if (PutSign(m, scale, items, welcome, rng, hash, result, sp[0] + rt[0] * 7.8f * mpm, sp[1] + rt[1] * 7.8f * mpm, d, mpm, 8f))
                    { placed.Add(new[] { sp[0], sp[1] }); want--; }
                }
            }
            // neighbourhood signs: ~38 m along a road from a junction into a group of lots
            if (nbhd.Count > 0)
            {
                int want = Math.Max(1, (int)Math.Round(km * 0.34f));
                List<float[]> js = new List<float[]>(junctions);
                for (int i = js.Count - 1; i > 0; i--) { int k = rng.Next(i + 1); float[] t = js[i]; js[i] = js[k]; js[k] = t; }
                foreach (float[] j in js)
                {
                    if (want == 0) break;
                    float rc = Dist(j, core) / med;
                    if (rc < 0.7f || rc > 1.6f || NearAny(placed, j, 250 * mpm)) continue;
                    float[] bestU = null; int most = 2;
                    foreach (float[] u in RoadsLeaving(s.Roads, j, 15 * mpm))
                    {
                        int n = lotsNear(j[0] + u[0] * 80 * mpm, j[1] + u[1] * 80 * mpm, 100);
                        if (n > most) { most = n; bestU = u; }
                    }
                    if (bestU == null) continue;
                    float[] d = { -bestU[0], -bestU[1] }; // facing traffic heading out to the junction, read on the way in
                    float[] rt = Right(d, hand);
                    float px = j[0] + bestU[0] * 38 * mpm + rt[0] * 8.5f * mpm, py = j[1] + bestU[1] * 38 * mpm + rt[1] * 8.5f * mpm;
                    if (lotsNear(px, py, 3) > 0) continue;
                    if (PutSign(m, scale, items, nbhd, rng, hash, result, px, py, d, mpm, 8f)) { placed.Add(new[] { j[0], j[1] }); want--; }
                }
            }
            // billboards along roads round the town, off the lots
            if (bill.Count > 0)
            {
                int want = Math.Max(1, (int)Math.Round(km * 1.1f)), tries = 0, maxTries = 80 * want;
                List<float[]> boards = new List<float[]>();
                while (want > 0 && tries++ < maxTries)
                {
                    List<float[]> road = s.Roads[rng.Next(s.Roads.Count)];
                    if (road.Count < 2) continue;
                    int k = 1 + rng.Next(road.Count - 1);
                    float len = Dist(road[k - 1], road[k]); if (len < 1e-3f) continue;
                    float t = (float)rng.NextDouble(), x = road[k - 1][0] + (road[k][0] - road[k - 1][0]) * t, y = road[k - 1][1] + (road[k][1] - road[k - 1][1]) * t;
                    float rc = Dist(new[] { x, y }, core) / med;
                    if (rc < 0.6f || rc > 1.3f || NearAny(junctions, new[] { x, y }, 15 * mpm) || NearAny(boards, new[] { x, y }, 120 * mpm)) continue;
                    float sg = rng.Next(2) == 0 ? 1 : -1;
                    float[] d = { (road[k][0] - road[k - 1][0]) / len * sg, (road[k][1] - road[k - 1][1]) / len * sg };
                    float[] rt = Right(d, rng.NextDouble() < 0.67 ? hand : -hand);
                    float off = (9 + 7 * (float)rng.NextDouble()) * mpm, px = x + rt[0] * off, py = y + rt[1] * off;
                    if (lotsNear(px, py, 6) > 0) continue;
                    if (PutSign(m, scale, items, bill, rng, hash, result, px, py, d, mpm, 10f)) { boards.Add(new[] { x, y }); want--; }
                }
            }
        }

        private delegate int Func2(float x, float y, float r);

        /// <summary>Unit directions of the roads leaving junction j (from road ends within 'reach'), measured ~10 m along.</summary>
        private static List<float[]> RoadsLeaving(List<List<float[]>> roads, float[] j, float reach)
        {
            List<float[]> ways = new List<float[]>();
            foreach (List<float[]> r in roads)
                for (int e = 0; e < 2; e++)
                {
                    float[] end = e == 0 ? r[0] : r[r.Count - 1];
                    if (Dist(end, j) > reach || r.Count < 2) continue;
                    float[] far = e == 0 ? r[Math.Min(2, r.Count - 1)] : r[Math.Max(0, r.Count - 3)];
                    float l = Dist(far, j); if (l < 1e-3f) continue;
                    float[] u = { (far[0] - j[0]) / l, (far[1] - j[1]) / l };
                    bool same = false;
                    foreach (float[] w in ways) if (w[0] * u[0] + w[1] * u[1] > 0.94f) same = true; // the same road split in pieces
                    if (!same) ways.Add(u);
                }
            return ways;
        }

        /// <summary>Whether another road starts or ends at this road's end (a joint, not a dead end).</summary>
        private static bool EndJoined(List<List<float[]>> roads, List<float[]> self, float[] tip, float reach)
        {
            foreach (List<float[]> r in roads)
                if (r != self && (Dist(r[0], tip) < reach || Dist(r[r.Count - 1], tip) < reach)) return true;
            return false;
        }

        private static float[] NearestLot(List<float[]> lots, float x, float y)
        {
            float[] best = null; float bd = float.MaxValue;
            foreach (float[] l in lots) { float d = (l[0] - x) * (l[0] - x) + (l[1] - y) * (l[1] - y); if (d < bd) { bd = d; best = l; } }
            return best;
        }

        /// <summary>A pair of drains at (x, y) on a road running along 'u': one at each kerb, 'off' m from the centre line,
        /// facing across the road.</summary>
        private static void Kerbs(Maps m, float scale, float mpm, SpatialHash hash, List<Placement> result, Random rng, int item, float x, float y, float[] u, float off)
        {
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float px = x - u[1] * off * mpm * sd, py = y + u[0] * off * mpm * sd;
                if (HardAt(m, px, py, scale) || BlockedEdge(m, px, py, scale) || NearBridge(m, px, py, scale) || hash.Near(px, py, 1.5f * mpm)) continue;
                Add(result, hash, item, px, py, (float)Math.Atan2(u[1] * sd, -u[0] * sd), rng, 0.8f * mpm);
            }
        }

        /// <summary>A run of 'n' split rails from 'start' along 'dir' (rails 6 m long, centres 6 m apart, each turned across
        /// the run as EA's are), with a post at each end; laid only if every rail is on dry land, not steep, clear.</summary>
        private static bool Fence(Maps m, float cell, float scale, float mpm, SpatialHash hash, List<Placement> result, Random rng, int rail, int post, float[] start, float[] dir, int n)
        {
            const float L = 6f;
            float yaw = (float)Math.Atan2(dir[0], dir[1]) + (float)Math.PI / 2;
            List<float[]> at = new List<float[]>();
            for (int i = 0; i < n; i++)
            {
                float px = start[0] + dir[0] * (L / 2 + L * i) * mpm, py = start[1] + dir[1] * (L / 2 + L * i) * mpm;
                int gx = (int)(px / scale), gy = (int)(py / scale);
                if (gx < 0 || gy < 0 || gx >= m.Slope.W || gy >= m.Slope.H) return false;
                if (HardAt(m, px, py, scale) || BlockedEdge(m, px, py, scale) || m.Blocked.D[gy * m.Blocked.W + gx] > 0.5f || m.Slope.D[gy * m.Slope.W + gx] > 15
                    || (m.RoadDist != null && m.RoadDist.D[gy * m.RoadDist.W + gx] < 7) || hash.Near(px, py, 2.5f * mpm)) return false;
                at.Add(new[] { px, py });
            }
            foreach (float[] p in at) Add(result, hash, rail, p[0], p[1], yaw, rng, 2.5f * mpm);
            if (post >= 0)
                for (int e = 0; e < 2; e++)
                {
                    float d = e == 0 ? 0 : L * n;
                    Add(result, hash, post, start[0] + dir[0] * d * mpm, start[1] + dir[1] * d * mpm, yaw, rng, 0.5f * mpm);
                }
            return true;
        }

        private static bool NearBridge(Maps m, float mx, float my, float scale)
        {
            if (m.NearBridge == null) return false;
            int x = (int)(mx / scale), y = (int)(my / scale);
            return x >= 0 && y >= 0 && x < m.NearBridge.W && y < m.NearBridge.H && m.NearBridge.D[y * m.NearBridge.W + x] > 0;
        }

        /// <summary>Outside the working grid or in the edge border.</summary>
        private static bool BlockedEdge(Maps m, float mx, float my, float scale)
        {
            int x = (int)(mx / scale), y = (int)(my / scale);
            return x < 0 || y < 0 || x >= m.Blocked.W || y >= m.Blocked.H || (m.Edge != null && m.Edge.D[y * m.Edge.W + x] > 0);
        }

        private static bool NearAny(List<float[]> pts, float[] p, float r)
        {
            foreach (float[] q in pts) if ((q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]) < r * r) return true;
            return false;
        }

        /// <summary>Unit directions of the roads arriving at junction j (pointing towards it), from road ends within 'reach'.</summary>
        private static List<float[]> Approaches(List<Chain> chains, float[] j, float reach, float look)
        {
            List<float[]> a = new List<float[]>();
            foreach (Chain ch in chains)
            {
                for (int e = 0; e < 2; e++)
                {
                    List<float[]> p = ch.P;
                    float[] end = e == 0 ? p[0] : p[p.Count - 1];
                    if (Dist(end, j) > reach) continue;
                    // a point ~'look' back along the road from this end
                    float acc = 0; float[] q = end;
                    for (int k = 1; k < p.Count; k++)
                    {
                        float[] cur = e == 0 ? p[k] : p[p.Count - 1 - k], prev = e == 0 ? p[k - 1] : p[p.Count - k];
                        acc += Dist(cur, prev); q = cur;
                        if (acc >= look) break;
                    }
                    if (Dist(q, end) < 1e-3f) continue;
                    a.Add(Unit(q, end));
                }
            }
            return a;
        }

        /// <summary>
        /// Joins CAW's road pieces into continuous roads: pieces whose ends meet where only two ends meet (not a
        /// junction) are one road. Notes which ends are at a junction or the map edge.
        /// </summary>
        private static List<Chain> Chains(List<List<float[]>> pieces, List<float[]> junctions, float mpm, float mapW, float mapH)
        {
            List<List<float[]>> segs = new List<List<float[]>>();
            foreach (List<float[]> p in pieces) if (p.Count >= 2) segs.Add(new List<float[]>(p));
            float snap = 4f * mpm;
            bool[] used = new bool[segs.Count];
            List<Chain> chains = new List<Chain>();
            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                List<float[]> line = new List<float[]>(segs[i]);
                List<int> mine = new List<int>(); mine.Add(i);
                // grow at both ends through pass-through joins
                for (int side = 0; side < 2; side++)
                {
                    bool grown = true;
                    while (grown)
                    {
                        grown = false;
                        float[] end = side == 0 ? line[line.Count - 1] : line[0];
                        if (NearAny(junctions, end, 15f * mpm)) break;
                        int found = -1; bool atStart = false, extra = false;
                        for (int k = 0; k < segs.Count; k++)
                        {
                            if (mine.Contains(k)) continue; // already part of this road
                            bool s0 = Dist(segs[k][0], end) < snap, s1 = Dist(segs[k][segs[k].Count - 1], end) < snap;
                            if (!s0 && !s1) continue;
                            if (used[k] || found >= 0) { extra = true; continue; }
                            found = k; atStart = s0;
                        }
                        if (found < 0 || extra) break;
                        used[found] = true; mine.Add(found);
                        List<float[]> add = new List<float[]>(segs[found]);
                        if (side == 0) { if (!atStart) add.Reverse(); add.RemoveAt(0); line.AddRange(add); }
                        else { if (atStart) add.Reverse(); add.RemoveAt(add.Count - 1); line.InsertRange(0, add); }
                        grown = true;
                    }
                }
                Chain c = new Chain(); c.P = line;
                float edge = 60f * mpm;
                c.StartAtJunction = NearAny(junctions, line[0], 15f * mpm);
                c.StartAtEdge = AtEdge(line[0], edge, mapW, mapH);
                c.EndAtEdge = AtEdge(line[line.Count - 1], edge, mapW, mapH);
                chains.Add(c);
            }
            return chains;
        }

        private static bool AtEdge(float[] p, float e, float w, float h) { return p[0] < e || p[1] < e || p[0] > w - e || p[1] > h - e; }

        private static List<float[]> Resample(List<float[]> p, float step)
        {
            List<float[]> o = new List<float[]>(); o.Add(p[0]);
            float acc = 0;
            for (int i = 1; i < p.Count; i++)
            {
                float[] a = p[i - 1], b = p[i];
                float L = Dist(a, b);
                while (acc + L >= step && L > 1e-6f)
                {
                    float t = (step - acc) / L;
                    a = new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t };
                    o.Add(a); L = Dist(a, b); acc = 0;
                }
                acc += L;
            }
            if (Dist(o[o.Count - 1], p[p.Count - 1]) > step * 0.3f) o.Add(p[p.Count - 1]);
            return o;
        }

        private static float Dist(float[] a, float[] b) { return (float)Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }
        private static float[] Unit(float[] a, float[] b) { float l = Math.Max(1e-6f, Dist(a, b)); return new[] { (b[0] - a[0]) / l, (b[1] - a[1]) / l }; }

        private sealed class SpatialHash
        {
            private readonly float size;
            private readonly Dictionary<long, List<float[]>> cells = new Dictionary<long, List<float[]>>();
            private float maxR;
            public SpatialHash(float size) { this.size = Math.Max(1f, size); }
            private long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }
            public void Add(float x, float y, float r)
            {
                long k = Key((int)Math.Floor(x / size), (int)Math.Floor(y / size));
                List<float[]> l;
                if (!cells.TryGetValue(k, out l)) { l = new List<float[]>(); cells[k] = l; }
                l.Add(new[] { x, y, r });
                if (r > maxR) maxR = r;
            }
            public bool Near(float x, float y, float r)
            {
                int reach = (int)Math.Ceiling((r + maxR) / size);
                int cx = (int)Math.Floor(x / size), cy = (int)Math.Floor(y / size);
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        List<float[]> l;
                        if (!cells.TryGetValue(Key(cx + dx, cy + dy), out l)) continue;
                        foreach (float[] o in l)
                        {
                            float d = r + o[2], ex = o[0] - x, ey = o[1] - y;
                            if (ex * ex + ey * ey < d * d) return true;
                        }
                    }
                return false;
            }
        }
    }
}
