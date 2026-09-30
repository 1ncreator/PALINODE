using System;
using System.Collections.Generic;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>Calibration of a painted room background (floor1.json "backgrounds").</summary>
    public sealed class BackgroundSpec
    {
        public string Sprite;
        public RectInt FloorPx;          // top-left origin, image pixels
        public Vector2Int Cells;
        public readonly List<Vector2Int> Blocked = new List<Vector2Int>();
        public readonly List<RectInt> Windows = new List<RectInt>();   // x, y, w, h (top-left origin)
    }

    /// <summary>
    /// Floor I data: floor1.json (balance), room_templates.json and audio_manifest.json, read from the GameConfig
    /// registry (or from files in tests). Everything tunable lives in those files.
    /// </summary>
    public sealed class FloorDB
    {
        public JNode Data { get; }
        public JNode Audio { get; }
        public List<RoomTemplate> Normal { get; } = new List<RoomTemplate>();
        public List<RoomTemplate> Big { get; } = new List<RoomTemplate>();
        public Dictionary<string, BackgroundSpec> Backgrounds { get; } = new Dictionary<string, BackgroundSpec>(StringComparer.Ordinal);

        public FloorDB(string floorJson, string templatesJson, string audioJson)
        {
            Data = JNode.Parse(floorJson ?? "{}");
            Audio = JNode.Parse(audioJson ?? "{}");
            var t = JNode.Parse(templatesJson ?? "{}");
            foreach (var n in t["normal"].Items()) Normal.Add(ParseTemplate(n));
            foreach (var n in t["big"].Items()) Big.Add(ParseTemplate(n));
            foreach (var kv in Data["backgrounds"].Pairs())
            {
                var b = new BackgroundSpec { Sprite = kv.Key };
                var f = kv.Value["floor"];
                b.FloorPx = new RectInt(f[0].AsInt(), f[1].AsInt(), f[2].AsInt() - f[0].AsInt(), f[3].AsInt() - f[1].AsInt());
                var c = kv.Value["cells"];
                b.Cells = new Vector2Int(c[0].AsInt(13), c[1].AsInt(7));
                foreach (var p in kv.Value["blocked"].Items()) b.Blocked.Add(new Vector2Int(p[0].AsInt(), p[1].AsInt()));
                foreach (var w in kv.Value["windows"].Items()) b.Windows.Add(new RectInt(w[0].AsInt(), w[1].AsInt(), w[2].AsInt(), w[3].AsInt()));
                Backgrounds[kv.Key] = b;
            }
        }

        private static RoomTemplate ParseTemplate(JNode n)
        {
            var rows = new List<string>();
            foreach (var r in n["rows"].Items()) rows.Add(r.AsString());
            return RoomTemplate.Parse(n.Str("name", "?"), rows, n.Bool("dark"), n.Bool("wallClock"), n.Num("weight", 1f));
        }

        public static FloorDB FromConfig(GameConfig config)
        {
            string Text(string name)
            {
                var t = config != null ? config.Text(name) : null;
                if (t == null) Debug.LogError($"[PALINODE] Floor data '{name}' is not registered in GameConfig. Run Tools/PALINODE/Build Everything.");
                return t != null ? t.text : null;
            }
            return new FloorDB(Text("floor1"), Text("floor1_templates"), Text("floor1_audio"));
        }

        [System.Runtime.CompilerServices.IndexerName("Section")]
        public JNode this[string key] => Data[key];

        public FloorGenSettings GenSettings()
        {
            var g = Data["generation"];
            var s = new FloorGenSettings
            {
                GridWidth = g["grid"][0].AsInt(9), GridHeight = g["grid"][1].AsInt(8),
                MinRooms = g["rooms"][0].AsInt(9), MaxRooms = g["rooms"][1].AsInt(12),
                MinCombat = g["combat"][0].AsInt(6), MaxCombat = g["combat"][1].AsInt(9),
                RejectChance = g.Num("reject", 0.5f),
                MinBossDistance = g.Int("minBossDistance", 3),
                BigRoomMax = g["bigRoom"].Int("max", 1),
                BigRoomChance = g["bigRoom"].Num("chance", 0.6f),
                BigRoomMinDistance = g["bigRoom"].Int("minDistance", 2),
                ShopChance = g.Num("shopChance", 0.15f),
                DarkChance = g.Num("darkChance", 0.25f),
                DarkMinDistance = g.Int("darkMinDistance", 4),
                SecretMinNeighbors = g.Int("secretMinNeighbors", 3),
                MaxAttempts = g.Int("maxAttempts", 5000),
                BigTemplates = Math.Max(1, Big.Count)
            };
            s.NormalWeights = new float[Math.Max(1, Normal.Count)];
            s.NormalDark = new bool[s.NormalWeights.Length];
            for (int i = 0; i < Normal.Count; i++)
            {
                s.NormalWeights[i] = Normal[i].Weight;
                s.NormalDark[i] = Normal[i].Dark;
            }
            if (Normal.Count == 0) s.NormalWeights[0] = 1f;
            return s;
        }

        /// <summary>Enemy points for a room at this distance from the start.</summary>
        public int Budget(int distance)
        {
            int points = 4;
            foreach (var b in Data["difficulty"]["budget"].Items())
            {
                points = b.Int("points", points);
                if (distance <= b.Int("upTo", 99)) return points;
            }
            return points;
        }

        public string BackgroundFor(RoomKind kind, SeededRandom rng)
        {
            var r = Data["rooms"];
            switch (kind)
            {
                case RoomKind.Start: return r.Str("start", "F1_room_b");
                case RoomKind.Big: return r.Str("big", "F1_room_big");
                case RoomKind.Boss: return r.Str("boss", "F1_boss_arena");
                case RoomKind.Corrector: return r.Str("corrector", "F1_room_corrector");
                case RoomKind.Shop: return r.Str("shop", "F1_room_shop");
                case RoomKind.Memory: return r.Str("memory", "F1_room_memory");
                case RoomKind.Secret: return r.Str("secret", "F1_room_secret");
                default:
                {
                    var names = new List<string>();
                    var weights = new List<float>();
                    foreach (var e in r["combat"].Items())
                    {
                        names.Add(e.Str("background"));
                        weights.Add(e.Num("weight", 1f));
                    }
                    int i = rng.Weighted(weights);
                    return i >= 0 ? names[i] : "F1_room_b";
                }
            }
        }

        public BackgroundSpec Background(string name) => name != null && Backgrounds.TryGetValue(name, out var b) ? b : null;

        public JNode Enemy(string id) => Data["enemies"][id];
        public JNode Item(string id) => Data["items"][id];

        public static readonly string[] ItemIds = { "typewriter", "carbon", "paperclips", "stamp", "coffee", "energy", "blotter", "magnifier" };
        public static readonly string[] EnemyIds = { "letters", "bug", "blot", "compositor", "headlights" };

        public List<string> Pool(string name)
        {
            var list = new List<string>();
            foreach (var n in Data["items"]["pools"][name].Items()) list.Add(n.AsString());
            return list;
        }
    }
}
