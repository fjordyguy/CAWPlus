using System;
using System.Collections.Generic;
using System.IO;

namespace CawPlus
{
    /// <summary>Finds terrain textures in CAW's SourceTextures folder and sorts them by which paint role they suit.</summary>
    public static class TextureLibrary
    {
        private static readonly string[] Extensions = { ".dds", ".png", ".tga", ".jpg", ".bmp" };

        // Maps that are not colour textures (normal, specular, bump, height...).
        private static readonly string[] Skip = { "_n.", "_nm.", "normal", "_spec", "_s.", "bump", "_h.", "height", "mask", "alpha" };

        public static string[] Keywords(PaintRole r)
        {
            switch (r)
            {
                case PaintRole.Rock: return new[] { "rock", "cliff", "stone", "shale", "granite", "slate", "boulder", "basalt", "limestone" };
                case PaintRole.Dirt: return new[] { "dirt", "soil", "mud", "earth", "ground", "forest", "mulch", "leaf", "leaves", "needle", "duff", "gravel" };
                case PaintRole.Shore: return new[] { "sand", "beach", "shore", "pebble", "shingle" };
                default: return new[] { "grass", "meadow", "lawn", "field", "clover", "moss", "weed", "wild", "pasture", "trees", "green" };
            }
        }

        /// <summary>CAW TerrainTypes name used when a new layer is added for this role (affects footstep sounds etc.).</summary>
        public static string TerrainType(PaintRole r)
        {
            switch (r)
            {
                case PaintRole.Rock: return "Rock01";
                case PaintRole.Dirt: return "Dirt01";
                case PaintRole.Shore: return "Sand01";
                default: return "Grass01";
            }
        }

        public static bool Matches(PaintRole r, string name)
        {
            string n = name.ToLowerInvariant();
            foreach (string k in Keywords(r)) if (n.Contains(k)) return true;
            return false;
        }

        public static List<string> Scan(string folder)
        {
            List<string> files = new List<string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return files;
            try
            {
                foreach (string f in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) < 0) continue;
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    bool skip = false;
                    foreach (string k in Skip) if (n.Contains(k)) { skip = true; break; }
                    if (n.StartsWith(EdgeBlend.LayerPrefix.ToLowerInvariant())) skip = true; // our own recoloured edge grass
                    if (!skip) files.Add(f);
                }
            }
            catch (Exception ex) { Log.Error("Scanning textures in " + folder + " failed", ex); }
            files.Sort(delegate (string a, string b) { return string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase); });
            return files;
        }
    }
}
