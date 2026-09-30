using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Palinode.Core;
using Palinode.Editor;
using Palinode.Localization;
using UnityEditor;

namespace Palinode.Tests
{
    public sealed class DataIntegrityTests
    {
        private static JNode Prologue => JNode.Parse(File.ReadAllText(ProjectBuilder.ProloguePath));
        private static LocalizationTable Table => LocalizationTable.FromCsv(File.ReadAllText(ProjectBuilder.LocalizationPath));
        private static GameConfig Config => AssetDatabase.LoadAssetAtPath<GameConfig>(ProjectBuilder.ConfigPath);

        [Test]
        public void PrologueJson_Parses_And_HasFlow()
        {
            var data = Prologue;
            Assert.That(data["flow"].Count, Is.GreaterThan(0), "prologue.json has an empty flow");
            foreach (var id in data["flow"].Items())
                Assert.IsFalse(data["chapters"][id.AsString()].IsNull, $"Flow chapter '{id.AsString()}' is not defined");
        }

        [Test]
        public void AllAssetsReferencedByPrologue_Exist()
        {
            var config = Config;
            Assert.IsNotNull(config, "GameConfig missing — run Tools/PALINODE/Build Everything");
            var refs = PrologueValidator.Collect(Prologue);
            var missing = new List<string>();
            foreach (var s in refs.Sprites) if (!config.HasSprite(s)) missing.Add("sprite " + s);
            foreach (var c in refs.Clips) if (!config.HasClip(c)) missing.Add("clip " + c);
            Assert.IsEmpty(missing, string.Join(", ", missing));
        }

        [Test]
        public void AllScenesReferencedByPrologue_AreInBuildSettings()
        {
            var inBuild = new HashSet<string>();
            foreach (var s in EditorBuildSettings.scenes) inBuild.Add(Path.GetFileNameWithoutExtension(s.path));
            foreach (var s in PrologueValidator.Collect(Prologue).Scenes)
                Assert.IsTrue(inBuild.Contains(s), $"Scene '{s}' is not in build settings");
        }

        [Test]
        public void AllLocalizationKeys_HaveBothLanguages()
        {
            var table = Table;
            var incomplete = new List<string>();
            foreach (var k in table.Keys) if (!table.HasBoth(k)) incomplete.Add(k);
            Assert.IsEmpty(incomplete, "Keys missing EN or RU: " + string.Join(", ", incomplete));
        }

        [Test]
        public void AllLocalizationKeysUsedByPrologue_Exist()
        {
            var table = Table;
            var missing = new List<string>();
            foreach (var k in PrologueValidator.Collect(Prologue).LocKeys) if (!table.Contains(k)) missing.Add(k);
            Assert.IsEmpty(missing, "Missing keys: " + string.Join(", ", missing));
        }

        [Test]
        public void AllVoiceFiles_ExistInEnAndRu()
        {
            string en = AssetImport.AudioRoot + "/Voice/en", ru = AssetImport.AudioRoot + "/Voice/ru";
            var enNames = Names(en);
            var ruNames = Names(ru);
            CollectionAssert.AreEquivalent(enNames, ruNames, "EN and RU voice folders differ");
            foreach (var v in PrologueValidator.Collect(Prologue).Voices)
            {
                Assert.IsTrue(enNames.Contains(v), $"EN voice {v} missing");
                Assert.IsTrue(ruNames.Contains(v), $"RU voice {v} missing");
                Assert.IsTrue(Config.HasClip("en/" + v) && Config.HasClip("ru/" + v), $"Voice {v} not registered in GameConfig");
            }
        }

        [Test]
        public void InsertedWord_AnchorsExistInLine()
        {
            var t = Table;
            foreach (var lang in new[] { Language.EN, Language.RU })
            {
                string line = t.Get("PAGE_L3", lang), anchor = t.Get("W_INSERT_ANCHOR", lang);
                Assert.That(line, Does.Contain(anchor), $"{lang}: anchor '{anchor}' not found in '{line}'");
                // The corrected sentence must equal the line with the red word inserted before the anchor.
                string expected = line.Replace(anchor, t.Get("W_DRUNK", lang) + " " + anchor);
                Assert.AreEqual(t.Get("PAGE_L3_FINAL", lang), expected, $"{lang}: insertion does not produce the final line");
            }
        }

        [Test]
        public void Occluders_HaveSpriteAndBase()
        {
            var config = Config;
            var missing = new List<string>();
            foreach (var lvl in Prologue["levels"].Pairs())
                foreach (var o in lvl.Value["occluders"].Items())
                {
                    string s = o.Str("sprite");
                    if (!config.HasSprite(s) || !config.ArtPoint(s, "base").HasValue) missing.Add(lvl.Key + "/" + s);
                }
            Assert.IsEmpty(missing, "Occluders without processed sprite/base: " + string.Join(", ", missing));
        }

        [Test]
        public void ClipsWithLoudnessTargets_AreMeasured()
        {
            var config = Config;
            Assert.IsNotNull(config.AudioLevels, "audio_levels.json not registered — run Build Everything");
            var missing = new List<string>();
            void Walk(JNode n)
            {
                if (n.IsArray) { foreach (var i in n.Items()) Walk(i); return; }
                if (!n.IsObject) return;
                if (n.Has("lufs") && n.Str("type") != "musicLevel" && n.Str("type") != "vo")
                {
                    string clip = n.Str("type") == "stamp" || n.Str("type") == "strike" || n.Str("type") == "dot" || n.Str("type") == "phoneMessage"
                        ? n.Str("sfx") : n.Str("clip");
                    if (clip != null && float.IsNaN(config.ClipLufs(clip))) missing.Add(clip);
                }
                foreach (var kv in n.Pairs()) Walk(kv.Value);
            }
            Walk(Prologue["chapters"]);
            Assert.IsEmpty(missing, "Clips with a LUFS target but no measurement: " + string.Join(", ", missing));
        }

        [Test]
        public void HandRigSprites_HaveTipAndElbow()
        {
            foreach (var s in new[] { "P1-03_hand_pen", "P3-01_hand_redpencil", "P2-06_hand" })
            {
                Assert.IsTrue(Config.ArtPoint(s, "tip").HasValue, s + " has no tip");
                Assert.IsTrue(Config.ArtPoint(s, "elbow").HasValue, s + " has no elbow");
            }
        }

        [Test]
        public void Csv_HandlesQuotesAndCommas()
        {
            var rows = Csv.Parse("key,en,ru\nA,\"x, y\",\"он сказал \"\"да\"\"\"\n");
            Assert.AreEqual("x, y", rows[1][1]);
            Assert.AreEqual("он сказал \"да\"", rows[1][2]);
        }

        [Test]
        public void Json_ParsesCommentsAndTrailingCommas()
        {
            var n = JNode.Parse("{ // note\n \"a\": [1, 2, ], \"b\": {\"c\": \"d\"}, }");
            Assert.AreEqual(2, n["a"].Count);
            Assert.AreEqual("d", n["b"].Str("c"));
        }

        private static HashSet<string> Names(string dir)
        {
            var set = new HashSet<string>();
            foreach (var f in Directory.GetFiles(dir))
                if (!f.EndsWith(".meta")) set.Add(Path.GetFileNameWithoutExtension(f));
            return set;
        }

        private static IEnumerable<JNode> Objects(JNode n)
        {
            if (n.IsObject)
            {
                yield return n;
                foreach (var kv in n.Pairs()) foreach (var o in Objects(kv.Value)) yield return o;
            }
            else if (n.IsArray)
                foreach (var item in n.Items()) foreach (var o in Objects(item)) yield return o;
        }

        [Test]
        public void FrameSets_IdleAndPlantsAreValidFrames()
        {
            var bad = new List<string>();
            foreach (var o in Objects(Prologue))
            {
                if (!o.Has("prefix") || !o.Has("count")) continue;
                int n = o.Int("count");
                if (o.Has("idle") && (o.Int("idle") < 0 || o.Int("idle") >= n)) bad.Add($"{o.Str("prefix")} idle {o.Int("idle")}");
                foreach (var p in o["plants"].Items())
                    if (p.AsInt() < 0 || p.AsInt() >= n) bad.Add($"{o.Str("prefix")} plant {p.AsInt()}");
            }
            Assert.IsEmpty(bad, "Frame indices out of range: " + string.Join(", ", bad));
        }

        [Test]
        public void Traffic_EveryCarHasASpecAndEveryVehicleStepALane()
        {
            var data = Prologue;
            var bad = new List<string>();
            foreach (var level in data["levels"].Pairs())
            {
                var tr = level.Value["traffic"];
                if (tr.IsNull) continue;
                foreach (var lane in tr["lanes"].Pairs())
                {
                    if (lane.Value["path"].Count != 2) bad.Add($"{level.Key}: lane {lane.Key} needs a 2-point path");
                    foreach (var car in lane.Value["cars"].Items())
                        if (!tr["cars"][car.AsString()].Has("anchor")) bad.Add($"{level.Key}: {car.AsString()} has no anchor");
                }
            }
            var street = data["levels"]["street"]["traffic"];
            foreach (var o in Objects(data["chapters"]))
            {
                if (o.Str("type") != "vehicle") continue;
                if (street["lanes"][o.Str("lane", "A")].IsNull) bad.Add($"vehicle step: unknown lane {o.Str("lane")}");
                if (o.Has("car") && !street["cars"][o.Str("car")].Has("anchor")) bad.Add($"vehicle step: {o.Str("car")} has no car spec");
            }
            Assert.IsEmpty(bad, string.Join("; ", bad));
        }
    }
}
