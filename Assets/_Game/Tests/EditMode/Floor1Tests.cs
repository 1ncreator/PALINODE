using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Palinode.Core;
using Palinode.Editor;
using Palinode.Floor;
using Palinode.Localization;
using UnityEditor;
using UnityEngine;

namespace Palinode.Tests
{
    /// <summary>Floor I: generation rules, templates, data, meta save, texts and audio manifest.</summary>
    public sealed class Floor1Tests
    {
        private static FloorDB Db() => new FloorDB(File.ReadAllText(Floor1Builder.FloorJsonPath), File.ReadAllText(Floor1Builder.TemplatesPath),
            File.ReadAllText(Floor1Builder.ManifestPath));

        private static GameConfig Config => AssetDatabase.LoadAssetAtPath<GameConfig>(ProjectBuilder.ConfigPath);

        private static string Signature(FloorLayout l)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in l.Rooms) sb.Append($"{r.Kind}{r.Origin}{r.Size}{r.TemplateIndex}{r.Dark}{r.ContentSeed};");
            return sb.ToString();
        }

        [Test]
        public void Generation_IsDeterministicBySeed()
        {
            var s = Db().GenSettings();
            for (int seed = 1; seed < 40; seed++)
            {
                var a = new FloorGenerator(s).Generate(seed);
                var b = new FloorGenerator(s).Generate(seed);
                Assert.AreEqual(Signature(a), Signature(b), "seed " + seed);
            }
            Assert.AreNotEqual(Signature(new FloorGenerator(s).Generate(1)), Signature(new FloorGenerator(s).Generate(2)));
        }

        [Test]
        public void Generation_FollowsTheRules()
        {
            var db = Db();
            var s = db.GenSettings();
            for (int seed = 1; seed <= 300; seed++)
            {
                var l = new FloorGenerator(s).Generate(seed * 7919);
                string at = " (seed " + seed * 7919 + ")";
                Assert.That(l.MainRoomCount, Is.InRange(9, 12), "room count" + at);
                Assert.IsFalse(l.HasTwoByTwoBlock(), "2×2 block" + at);
                Assert.IsTrue(l.IsFullyConnected(), "connected" + at);
                Assert.AreEqual(new Vector2Int(l.GridWidth() / 2, l.GridHeight() / 2), l.Start.Origin, "start in the centre" + at);

                // Boss: a dead end, the farthest one, a 2×2 arena.
                Assert.IsNotNull(l.Boss, "boss" + at);
                Assert.IsTrue(l.Boss.IsDeadEnd, "boss is a dead end" + at);
                Assert.AreEqual(new Vector2Int(2, 2), l.Boss.Size, "arena 2×2" + at);
                foreach (var r in l.Rooms)
                    if (r != l.Boss && r.Kind != RoomKind.Secret && r.IsDeadEnd)
                        Assert.LessOrEqual(r.Distance, l.Boss.Distance, $"{r} is farther than the boss" + at);

                int combat = 0, corrector = 0, memory = 0, shop = 0, big = 0;
                foreach (var r in l.Rooms)
                {
                    switch (r.Kind)
                    {
                        case RoomKind.Combat: combat++; break;
                        case RoomKind.Big: combat++; big++; Assert.GreaterOrEqual(r.Distance, 2, "big hall next to the start" + at); break;
                        case RoomKind.Corrector: corrector++; Assert.IsTrue(r.IsDeadEnd, "corrector dead end" + at); break;
                        case RoomKind.Memory: memory++; Assert.IsTrue(r.IsDeadEnd, "memory dead end" + at); break;
                        case RoomKind.Shop: shop++; Assert.IsTrue(r.IsDeadEnd, "shop dead end" + at); break;
                    }
                }
                Assert.That(combat, Is.InRange(6, 9), "combat rooms" + at);
                Assert.AreEqual(1, corrector, "corrector" + at);
                Assert.AreEqual(1, memory, "memory" + at);
                Assert.LessOrEqual(shop, 1, "shop" + at);
                Assert.LessOrEqual(big, 1, "big halls" + at);

                // Secret: an empty cell next to 3+ rooms, never the arena.
                Assert.IsNotNull(l.Secret, "secret" + at);
                var near = new HashSet<int>();
                foreach (var d in l.Secret.Doors) near.Add(d.Neighbor);
                Assert.GreaterOrEqual(near.Count, 3, "secret neighbours" + at);
                Assert.IsFalse(near.Contains(l.Boss.Index), "secret next to the boss" + at);

                // Dark rooms only far from the start.
                foreach (var r in l.Rooms) if (r.Dark) Assert.GreaterOrEqual(r.Distance, 4, "dark too close" + at);
            }
        }

        [Test]
        public void ShopChance_IsAboutFifteenPercent()
        {
            var s = Db().GenSettings();
            int shops = 0, n = 400;
            for (int i = 1; i <= n; i++)
                foreach (var r in new FloorGenerator(s).Generate(i * 104729).Rooms) if (r.Kind == RoomKind.Shop) shops++;
            float f = shops / (float)n;
            Assert.That(f, Is.InRange(0.07f, 0.25f), "shop frequency " + f);
        }

        [Test]
        public void Templates_AreValid_AndDoorsConnected()
        {
            var db = Db();
            Assert.GreaterOrEqual(db.Normal.Count, 12);
            foreach (var t in db.Normal)
            {
                Assert.AreEqual(13, t.Width, t.Name);
                Assert.AreEqual(7, t.Height, t.Name);
                Assert.IsTrue(t.Validate(out string err), err);
                // Every enemy point reachable from every door.
                var pf = t.CreatePathfinder();
                foreach (var door in RoomTemplate.AllDoorCells(t.Width, t.Height))
                foreach (var e in t.SpawnPoints)
                    Assert.IsTrue(pf.Reachable(door, e), $"{t.Name}: {e} not reachable from {door}");
            }
            foreach (var t in db.Big) Assert.IsTrue(t.Validate(out string err), err);
        }

        [Test]
        public void Chances_AreInData()
        {
            var d = Db().Data;
            Assert.AreEqual(0.75f, d["redPencil"].Num("chance", -1f), 1e-4f);
            Assert.AreEqual(0.15f, d["generation"].Num("shopChance", -1f), 1e-4f);
            Assert.AreEqual(0.35f, d["rewards"].Num("chance", -1f), 1e-4f);
        }

        [Test]
        public void Budget_And_Headlights()
        {
            var db = Db();
            Assert.AreEqual(4, db.Budget(1));
            Assert.AreEqual(4, db.Budget(2));
            Assert.AreEqual(6, db.Budget(3));
            Assert.AreEqual(6, db.Budget(4));
            Assert.AreEqual(8, db.Budget(7));
            var s = db.GenSettings();
            for (int seed = 1; seed < 120; seed++)
            {
                var l = new FloorGenerator(s).Generate(seed);
                foreach (var r in l.Rooms)
                {
                    if (r.Kind != RoomKind.Combat) continue;
                    var t = db.Normal[r.TemplateIndex];
                    var plan = SpawnPlanner.Plan(db, r, t.SpawnPoints, new SeededRandom(r.ContentSeed), db.Budget(r.Distance));
                    int spent = 0;
                    foreach (var e in plan)
                    {
                        spent += SpawnPlanner.Cost(db.Enemy(e.Type), e.Type, e.Group);
                        if (e.Type == "headlights")
                        {
                            Assert.IsTrue(r.Dark, "Headlights outside a dark room");
                            Assert.GreaterOrEqual(r.Distance, 3, "Headlights too close to the start");
                        }
                    }
                    Assert.LessOrEqual(spent, db.Budget(r.Distance), "budget exceeded");
                }
            }
        }

        [Test]
        public void MetaSave_DeathIncrementsRevision_AndPersists()
        {
            string path = Path.Combine(Path.GetTempPath(), "palinode_meta_test_" + System.Guid.NewGuid().ToString("N") + ".json");
            MetaSave.PathOverride = path;
            try
            {
                var m = MetaSave.Load();
                Assert.AreEqual(1, m.revision, "starts at 01 after the prologue");
                m.RegisterDeath(1, "bug", 4, new[] { "coffee" });
                Assert.AreEqual(2, m.revision);
                var again = MetaSave.Load();
                Assert.AreEqual(2, again.revision);
                Assert.AreEqual("bug", again.lastDeath.killer);
                Assert.AreEqual(4, again.lastDeath.page);
                CollectionAssert.AreEqual(new[] { "coffee" }, again.lastDeath.items);
                Assert.AreEqual("02", again.RevisionLabel);
            }
            finally
            {
                MetaSave.PathOverride = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void MetaSave_RedPencilEdits_AreCountedForever()
        {
            string path = Path.Combine(Path.GetTempPath(), "palinode_meta_test_" + System.Guid.NewGuid().ToString("N") + ".json");
            MetaSave.PathOverride = path;
            try
            {
                var m = MetaSave.Load();
                m.RegisterEdit();
                m.RegisterEdit();
                m.RegisterDeath(1, "blot", 2, null);
                var again = MetaSave.Load();
                Assert.AreEqual(2, again.edits);
                again.RegisterEdit();
                Assert.AreEqual(3, MetaSave.Load().edits);
            }
            finally
            {
                MetaSave.PathOverride = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void InkHealth_Works()
        {
            var h = new InkHealth(6, 12, 1f);
            Assert.AreEqual(6, h.Current);
            Assert.AreEqual(3, h.Containers);
            Assert.IsTrue(h.TryDamage(1, 0f));
            Assert.IsFalse(h.TryDamage(1, 0.5f), "invulnerable for 1 s");
            Assert.IsTrue(h.TryDamage(2, 1.1f));
            Assert.AreEqual(3, h.Current);
            h.Heal(10);
            Assert.AreEqual(6, h.Current, "capped by containers");
            Assert.IsTrue(h.WouldKill(6, 5f));
        }

        [Test]
        public void Localization_HasAllFloorKeys_InBothLanguages()
        {
            var table = LocalizationTable.FromCsv(File.ReadAllText(ProjectBuilder.LocalizationPath));
            foreach (var k in FloorValidator.AllKeys()) Assert.IsTrue(table.HasBoth(k), "missing EN/RU: " + k);
        }

        [Test]
        public void AudioManifest_EveryFileExists_OrIsExpectedWithFallback()
        {
            var config = Config;
            Assert.IsNotNull(config, "GameConfig not built");
            var db = Db();
            foreach (var kv in db.Audio["events"].Pairs())
            {
                string file = kv.Value.Str("file");
                if (config.HasClip(file)) continue;
                Assert.IsTrue(kv.Value.Bool("expected"), $"{kv.Key}: {file} missing and not marked expected");
                Assert.IsTrue(kv.Value.Has("fallback"), $"{kv.Key}: no fallback");
                var fb = kv.Value["fallback"];
                if (fb.Has("clip")) Assert.IsTrue(config.HasClip(fb.Str("clip")), $"{kv.Key}: fallback clip {fb.Str("clip")} missing");
            }
            foreach (var kv in db.Audio["music"].Pairs())
                if (!config.HasClip(kv.Value.Str("file")))
                    Assert.IsTrue(kv.Value.Bool("expected") && kv.Value.Has("fallback"), kv.Key);
        }

        [Test]
        public void FloorData_Validates()
        {
            var table = LocalizationTable.FromCsv(File.ReadAllText(ProjectBuilder.LocalizationPath));
            var errors = FloorValidator.Validate(Config, table);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Floor1Scene_IsInBuildSettings_AndPrologueExitsThere()
        {
            bool found = false;
            foreach (var s in EditorBuildSettings.scenes) if (s.path.EndsWith("/Floor1.unity") && s.enabled) found = true;
            Assert.IsTrue(found, "Floor1 scene missing from Build Settings");
            var data = JNode.Parse(File.ReadAllText(ProjectBuilder.ProloguePath));
            Assert.AreEqual("Floor1", data.Str("exitScene"));
        }
    }

    internal static class LayoutTestExt
    {
        public static int GridWidth(this FloorLayout l) => l.Width;
        public static int GridHeight(this FloorLayout l) => l.Height;
    }
}
