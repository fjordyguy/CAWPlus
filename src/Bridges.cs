using System;
using System.Collections.Generic;
using System.Globalization;

namespace CawPlus
{
    /// <summary>A road bridge object: its catalog key, the EA worlds that use it and its span (port to port), metres.</summary>
    internal sealed class BridgeType
    {
        public string Name;
        public uint Group;
        public ulong Instance;
        public float Length;          // 0 = not measured yet (Self-test, Measure bridges)
        public string[] Worlds;       // world keys whose layer files place it
        public BridgeType(string name, uint group, ulong instance, float length, params string[] worlds)
        { Name = name; Group = group; Instance = instance; Length = length; Worlds = worlds; }
    }

    /// <summary>
    /// The bridges EA's worlds use (from their layer files: library/&lt;world&gt;_inventory.json, category bridge_infrastructure;
    /// the broken rail bridge and retaining walls left out). Spans were measured in CAW for the EP7 bridges (v0.11 self-test,
    /// Vermont); the rest are measured by the self-test's Measure bridges and kept in Documents\CAWPlus\bridges.txt.
    /// </summary>
    internal static class Bridges
    {
        public static readonly BridgeType[] All =
        {
            new BridgeType("BridgeFlatEP7", 0x70000000, 0x98d89bUL, 72.0f, "vermont"),
            new BridgeType("BridgeWoodenEP7", 0x70000000, 0x98d899UL, 72.0f, "vermont"),
            new BridgeType("bridgePlank", 0x18000000, 0x989e21UL, 65.1f, "twinbrook", "vermont"),
            new BridgeType("bridgeStructural", 0x18000000, 0x989eeaUL, 0, "twinbrook"),
            new BridgeType("bridgeEgyptLarge", 0x08000000, 0x989788UL, 0, "al_simhara"),
            new BridgeType("bridgeEgyptSmall", 0x08000000, 0x989787UL, 0, "al_simhara"),
            new BridgeType("bridgeChinaLarge", 0x08000000, 0x989785UL, 0, "shang_simla"),
            new BridgeType("bridgeChinaSmall", 0x08000000, 0x989786UL, 0, "shang_simla"),
            new BridgeType("bridgePier", 0x28000000, 0x98a135UL, 0, "bridgeport"),
            new BridgeType("bridgeSuspension", 0x28000000, 0x98a102UL, 0, "bridgeport"),
            new BridgeType("bridgePirate", 0x00000000, 0xf4c9bUL, 0, "barnacle_bay"),
            new BridgeType("bridgeCeltic_DOT09", 0x01000000, 0xf5b53UL, 0, "dragon_valley"),
            new BridgeType("bridgeCeltic_DOT09small", 0x01000000, 0xf5b90UL, 0, "dragon_valley"),
            new BridgeType("bridgeTuscan_DOT07", 0x01000000, 0xf58d3UL, 0, "monte_vista"),
            new BridgeType("bridgeLarge_DOT11", 0x01000000, 0xf5fcbUL, 0, "roaring_heights"),
            new BridgeType("bridgeStructural_DOT11", 0x01000000, 0xf5e59UL, 0, "roaring_heights"),
        };

        /// <summary>The shortest and longest span a bridge may have to count (shorter: a footbridge or decoration).</summary>
        public const float MinSpan = 24f, MaxSpan = 400f;

        /// <summary>Spans measured in CAW, by instance, read from bridges.txt lines "instance hex|name|length" (0 = not a
        /// road bridge). A measured length replaces the built-in one.</summary>
        public static void ApplyMeasured(string[] lines)
        {
            if (lines == null) return;
            foreach (string raw in lines)
            {
                string[] p = raw.Trim().Split('|');
                if (p.Length < 3 || raw.StartsWith("#")) continue;
                ulong inst; float len;
                if (!ulong.TryParse(p[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out inst)) continue;
                if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out len)) continue;
                foreach (BridgeType b in All) if (b.Instance == inst) b.Length = len;
            }
        }

        /// <summary>
        /// The bridges a world type builds with: its own road bridges with a known span, longest first; if it has none
        /// (or none measured), EA's EP7 bridges (72 m), which every world type may use.
        /// </summary>
        public static List<BridgeType> ForWorld(string worldKey)
        {
            List<BridgeType> own = new List<BridgeType>();
            foreach (BridgeType b in All)
                if (b.Length >= MinSpan && b.Length <= MaxSpan && Array.IndexOf(b.Worlds, worldKey ?? "") >= 0) own.Add(b);
            if (own.Count == 0)
                foreach (BridgeType b in All) if (b.Instance == 0x98d89bUL || b.Instance == 0x98d899UL) own.Add(b);
            own.Sort(delegate (BridgeType a, BridgeType b) { return b.Length.CompareTo(a.Length); });
            return own;
        }

        /// <summary>The spans available, longest first (RoadSettings.BridgeSpans).</summary>
        public static float[] Spans(List<BridgeType> types)
        {
            float[] s = new float[types.Count];
            for (int i = 0; i < s.Length; i++) s[i] = types[i].Length;
            return s;
        }

        /// <summary>For a crossing whose banks are 'water' metres apart: the shortest bridge that still reaches 8 m onto
        /// land at both ends, else the longest.</summary>
        public static BridgeType Pick(List<BridgeType> types, float water)
        {
            BridgeType best = null;
            foreach (BridgeType b in types)
                if (b.Length >= water + 16 && (best == null || b.Length < best.Length)) best = b;
            return best ?? (types.Count > 0 ? types[0] : null);
        }
    }
}
