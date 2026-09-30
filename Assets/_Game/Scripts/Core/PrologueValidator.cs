using System.Collections.Generic;
using Palinode.Localization;

namespace Palinode.Core
{
    /// <summary>
    /// Static checks of prologue.json against the asset registry and the localization table.
    /// Used by the project builder (fails the build) and by EditMode tests.
    /// </summary>
    public static class PrologueValidator
    {
        public sealed class References
        {
            public readonly HashSet<string> Sprites = new HashSet<string>();
            public readonly HashSet<string> Clips = new HashSet<string>();
            public readonly HashSet<string> Voices = new HashSet<string>();
            public readonly HashSet<string> LocKeys = new HashSet<string>();
            public readonly HashSet<string> Scenes = new HashSet<string>();
        }

        private static readonly HashSet<string> SpriteKeys = new HashSet<string> { "sprite", "face", "hour", "minute", "background", "car" };
        private static readonly HashSet<string> LocKeyFields = new HashSet<string> { "key", "sub", "subKey", "anchorKey", "prompt" };


        public static References Collect(JNode root)
        {
            var refs = new References();
            Walk(root, null, refs);

            foreach (var kv in root["chapters"].Pairs()) refs.Scenes.Add(kv.Value.Str("scene", "Prologue_Cinematic"));
            refs.Scenes.Add(root.Str("exitScene", "Floor1"));
            refs.Scenes.Add(root.Str("menuScene", "Menu"));
            return refs;
        }

        private static void Walk(JNode node, string stepType, References refs)
        {
            if (node.IsArray)
            {
                foreach (var item in node.Items()) Walk(item, stepType, refs);
                return;
            }
            if (!node.IsObject) return;
            string type = node.Str("type") ?? stepType;
            // Frame sequences: { "prefix": "G_walk_down_", "count": 8 } → G_walk_down_01 … _08.
            if (node.Has("prefix") && node.Has("count"))
                for (int i = 1; i <= node.Int("count"); i++) refs.Sprites.Add(node.Str("prefix") + i.ToString("00"));
            foreach (var kv in node.Pairs())
            {
                var v = kv.Value;
                if (v.IsString)
                {
                    string s = v.AsString();
                    if (SpriteKeys.Contains(kv.Key)) refs.Sprites.Add(s);
                    else if (kv.Key == "clip" || kv.Key == "footsteps") (type == "vo" ? refs.Voices : refs.Clips).Add(s);
                    else if (kv.Key == "sfx") refs.Clips.Add(s);
                    else if (LocKeyFields.Contains(kv.Key)) refs.LocKeys.Add(s);
                }
                else if (kv.Key == "cars")
                {
                    // Traffic: lane car lists (arrays of names) and per-car specs (keyed by sprite name).
                    foreach (var c in v.Items()) if (c.IsString) refs.Sprites.Add(c.AsString());
                    foreach (var c in v.Pairs()) refs.Sprites.Add(c.Key);
                }
                else Walk(v, type, refs);
            }
        }

        public static List<string> Validate(JNode root, GameConfig config, LocalizationTable table)
        {
            var errors = new List<string>();
            var refs = Collect(root);
            foreach (var s in refs.Sprites)
                if (!config.HasSprite(s)) errors.Add($"Missing sprite: {s}");
            foreach (var c in refs.Clips)
                if (!config.HasClip(c)) errors.Add($"Missing audio clip: {c}");
            foreach (var v in refs.Voices)
            {
                if (!config.HasClip("en/" + v)) errors.Add($"Missing EN voice: {v}");
                if (!config.HasClip("ru/" + v)) errors.Add($"Missing RU voice: {v}");
            }
            foreach (var k in refs.LocKeys)
                if (!table.HasBoth(k)) errors.Add($"Localization key missing or incomplete: {k}");
            foreach (var id in root["flow"].Items())
                if (root["chapters"][id.AsString()].IsNull) errors.Add($"Flow references unknown chapter: {id.AsString()}");
            return errors;
        }
    }
}
