using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Palinode.Core;
using Palinode.Floor;
using Palinode.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Palinode.Tests
{
    /// <summary>
    /// Floor I in play mode. The meta save is redirected to a temporary file, so the player's real revision number is
    /// never touched. Any error or exception in the log fails a test.
    ///   FullRun      prologue (×10, autopilot) → Floor I → autopilot with invulnerability and auto-kill → boss →
    ///                corridor → "To be continued". PALINODE_TEST_SKIP_PROLOGUE=1 starts at Floor I.
    ///   Death        death → REVISION screen → a new run, revision +1.
    ///   Screenshots  every room kind, each enemy type, a dark room with Headlights, the boss in each phase, the
    ///                newspaper, the REVISION screen, the corridor with the Archivist, HUD and map → Screenshots/F1_*.png
    /// </summary>
    public sealed class Floor1SmokeTest
    {
        private string _metaPath;
        private static string Env(string k) => Environment.GetEnvironmentVariable(k);

        [SetUp]
        public void SetUp()
        {
            _metaPath = Path.Combine(Path.GetTempPath(), "palinode_meta_play_" + Guid.NewGuid().ToString("N") + ".json");
            MetaSave.PathOverride = _metaPath;
            FloorController.SeedOverride = 0;
            FloorController.BotEnabled = false;
            Game.GodMode = false;
            Game.AutoKill = false;
        }

        /// <summary>Leave an empty scene behind (other play-mode tests add their own scene and lights).</summary>
        [UnityTearDown]
        public IEnumerator UnloadFloor()
        {
            var empty = SceneManager.CreateScene("Empty_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s != empty && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s);
            }
            if (GameRoot.Instance != null) GameRoot.Instance.Audio.StopEverything(0f);
        }

        [TearDown]
        public void TearDown()
        {
            FloorController.BotEnabled = false;
            FloorController.SeedOverride = 0;
            FloorController.Marker -= OnMarker;
            LevelController.AutoPilotEnabled = false;
            Game.GodMode = false;
            Game.AutoKill = false;
            MetaSave.PathOverride = null;
            if (File.Exists(_metaPath)) File.Delete(_metaPath);
            if (GameRoot.Instance != null) GameRoot.Instance.SpeedMultiplier = 1f;
        }

        private readonly Queue<string> _markers = new Queue<string>();
        private void OnMarker(string id) => _markers.Enqueue(id);

        private static float Speed()
        {
            float s = 10f;
            if (float.TryParse(Env("PALINODE_TEST_SPEED"), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) s = v;
            return s;
        }

        private static IEnumerator WaitFloorStarted(int runsBefore, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while ((FloorController.RunsStarted <= runsBefore || Game.Current == null || Game.Player == null) && Time.realtimeSinceStartup < end)
                yield return null;
            Assert.Greater(FloorController.RunsStarted, runsBefore, "Floor I did not start");
            Assert.IsNotNull(Game.Current, "no current room");
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // ------------------------------------------------------------------ full run

        [UnityTest]
        [Timeout(3600000)]
        public IEnumerator FullRun_PrologueToFloorToBossToCorridor()
        {
            FloorController.Marker += OnMarker;
            int runs0 = FloorController.RunsStarted;
            if (Env("PALINODE_TEST_SKIP_PROLOGUE") == "1")
            {
                SceneManager.LoadScene("Floor1");
                yield return null;
            }
            else
            {
                SceneManager.LoadScene("Menu");
                yield return null;
                yield return null;
                var root = GameRoot.Instance;
                Assert.IsNotNull(root);
                LevelController.AutoPilotEnabled = true;
                root.SpeedMultiplier = Speed();
                root.Flow.StartNewGame();
            }
            yield return WaitFloorStarted(runs0, 1500f);
            LevelController.AutoPilotEnabled = false;
            Game.GodMode = true;
            Game.Player.Health.GodMode = true;
            Game.AutoKill = true;
            FloorController.BotEnabled = true;
            GameRoot.Instance.SpeedMultiplier = Mathf.Min(Speed(), 4f);

            float end = Time.realtimeSinceStartup + 900f;
            var visited = new HashSet<int>();
            bool sawBoss = false, sawCorridor = false;
            while (!FloorController.Reached && Time.realtimeSinceStartup < end)
            {
                if (Game.Current != null)
                {
                    visited.Add(Game.Current.Node.Index);
                    if (Game.Current.Node.Kind == RoomKind.Boss) sawBoss = true;
                }
                if (Game.Ctrl != null && Game.Ctrl.InCorridor) sawCorridor = true;
                while (_markers.Count > 0) TestCapture.Shot("run_" + _markers.Dequeue());
                yield return null;
            }
            Assert.IsTrue(sawBoss, "the boss room was never reached");
            Assert.IsTrue(sawCorridor, "the corridor was never reached");
            Assert.IsTrue(FloorController.Reached, "'To be continued' was not reached");
            Debug.Log($"[PALINODE] Full run: {visited.Count} rooms visited");
            // Back to the menu afterwards.
            float menuEnd = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().name != "Menu" && Time.realtimeSinceStartup < menuEnd) yield return null;
            Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
        }

        // ------------------------------------------------------------------ death

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Death_ShowsRevision_AndStartsANewRun()
        {
            int runs0 = FloorController.RunsStarted;
            SceneManager.LoadScene("Floor1");
            yield return null;
            yield return WaitFloorStarted(runs0, 120f);
            int revision = Game.Meta.revision;
            int seed = Game.Run.Seed;
            // Take all ink away.
            Game.Player.Hurt(100, Vector2.down, "compositor");
            Assert.IsTrue(Game.Ctrl.Dead, "Elias did not die");
            float shotAt = -1f;
            yield return Wait(1.5f);
            int runs1 = FloorController.RunsStarted;
            float end = Time.realtimeSinceStartup + 60f;
            while (FloorController.RunsStarted == runs1 && Time.realtimeSinceStartup < end)
            {
                if (shotAt < 0f && Time.realtimeSinceStartup > end - 60f + 6.5f) { TestCapture.Shot("F1_revision"); shotAt = Time.realtimeSinceStartup; }
                yield return null;
            }
            yield return WaitFloorStarted(runs1, 30f);
            Assert.AreEqual(revision + 1, Game.Meta.revision, "revision number did not increase");
            Assert.AreEqual(revision + 1, MetaSave.Load().revision, "revision number not saved");
            Assert.AreEqual("compositor", MetaSave.Load().lastDeath.killer);
            Assert.AreEqual(RoomKind.Start, Game.Current.Node.Kind, "the new run does not start in the first room");
            Assert.AreNotEqual(seed, Game.Run.Seed, "same floor again");
        }

        // ------------------------------------------------------------------ screenshots

        private static int FindSeed()
        {
            var db = FloorDB.FromConfig(GameConfig.Load());
            var gen = new FloorGenerator(db.GenSettings());
            for (int s = 1; s < 5000; s++)
            {
                var l = gen.Generate(s);
                bool shop = false, big = false, dark = false;
                foreach (var r in l.Rooms)
                {
                    if (r.Kind == RoomKind.Shop) shop = true;
                    if (r.Kind == RoomKind.Big) big = true;
                    if (r.Kind == RoomKind.Combat && r.Dark) dark = true;
                }
                if (shop && big && dark) return s;
            }
            return 1;
        }

        private static RoomView FirstRoom(Func<RoomView, bool> pred)
        {
            foreach (var r in Game.Ctrl.Rooms) if (pred(r)) return r;
            return null;
        }

        private static void KillAll()
        {
            foreach (var e in new List<EnemyBase>(Game.Current.Enemies)) if (e != null && e.Alive) e.Kill(false);
        }

        [UnityTest]
        [Timeout(1200000)]
        public IEnumerator Screenshots()
        {
            int seed = FindSeed();
            FloorController.SeedOverride = seed;
            int runs0 = FloorController.RunsStarted;
            SceneManager.LoadScene("Floor1");
            yield return null;
            yield return WaitFloorStarted(runs0, 120f);
            Game.GodMode = true;
            Game.Player.Health.GodMode = true;
            yield return Wait(2.5f);
            TestCapture.Shot("F1_start");

            // HUD with items, sheets and the pencil; the map.
            Game.Run.Sheets = 7;
            Game.Ctrl.GiveItem("pencil", FloorSprites.Get("PR_redpencil"));
            Game.Ctrl.GiveItem("coffee", FloorSprites.Get("F1_item_coffee"));
            yield return Wait(0.6f);
            TestCapture.Shot("F1_hud_item");
            Game.Ctrl.DebugAction(4);
            Game.Ctrl.ShowFullMap(true);
            yield return Wait(0.4f);
            TestCapture.Shot("F1_map");
            Game.Ctrl.ShowFullMap(false);
            Game.Ctrl.DebugAction(4);

            // One lit combat room: each enemy type in turn.
            var combat = FirstRoom(r => r.Node.Kind == RoomKind.Combat && !r.IsDark);
            Assert.IsNotNull(combat);
            yield return Game.Ctrl.DebugTeleportTo(combat);
            yield return Wait(0.3f);
            KillAll();
            yield return Wait(1f);
            var f = combat.Frame;
            foreach (var type in new[] { "letters", "bug", "blot", "compositor" })
            {
                Game.Player.Teleport(f.CellCenter(2, 3));
                var at = f.CellCenter(8, 3);
                if (type == "letters") EnemyFactory.CreateLetters(combat, at, 5, false, false);
                else EnemyFactory.Create(type, combat, at, false);
                if (type == "compositor") EnemyFactory.Create(type, combat, f.CellCenter(10, 1), true);   // + a Witness
                yield return Wait(type == "compositor" ? 1.6f : 1.2f);
                TestCapture.Shot("F1_enemy_" + type);
                KillAll();
                yield return Wait(0.8f);
            }
            TestCapture.Shot("F1_room_cleared");

            // Dark room with Headlights.
            var dark = FirstRoom(r => r.IsDark && r.Node.Kind == RoomKind.Combat);
            if (dark != null)
            {
                yield return Game.Ctrl.DebugTeleportTo(dark);
                yield return Wait(0.3f);
                KillAll();
                Game.Player.Teleport(dark.Frame.CellCenter(3, 3));
                EnemyFactory.Create("headlights", dark, dark.Frame.CellCenter(9, 2), false);
                yield return Wait(1.6f);
                TestCapture.Shot("F1_dark_headlights");
                yield return Wait(1.4f);
                TestCapture.Shot("F1_dark_headlights_2");
                KillAll();
                yield return Wait(0.5f);
            }

            foreach (var kind in new[] { RoomKind.Memory, RoomKind.Corrector, RoomKind.Shop, RoomKind.Secret, RoomKind.Big })
            {
                var room = FirstRoom(r => r.Node.Kind == kind);
                if (room == null) { Debug.Log("[PALINODE] no room of kind " + kind); continue; }
                yield return Game.Ctrl.DebugTeleportTo(room);
                yield return Wait(0.3f);
                if (kind == RoomKind.Big) yield return Wait(1.2f);
                yield return Wait(1.3f);
                TestCapture.Shot("F1_room_" + kind.ToString().ToLowerInvariant());
                KillAll();
            }

            // Boss: each phase.
            var bossRoom = FirstRoom(r => r.Node.Kind == RoomKind.Boss);
            yield return Game.Ctrl.DebugTeleportTo(bossRoom);
            yield return Wait(3.2f);
            TestCapture.Shot("F1_boss_p1");
            yield return Wait(1.8f);
            TestCapture.Shot("F1_boss_p1_roller");
            var boss = Game.Ctrl.Boss;
            Assert.IsNotNull(boss);
            boss.TakeHit(new HitInfo { Damage = boss.MaxHp * 0.4f, Point = boss.Feet });
            yield return Wait(4.5f);
            TestCapture.Shot("F1_boss_p2");
            boss.TakeHit(new HitInfo { Damage = boss.MaxHp * 0.36f, Point = boss.Feet });
            yield return Wait(6.5f);
            TestCapture.Shot("F1_boss_p3");
            yield return Wait(3f);
            TestCapture.Shot("F1_boss_p3_b");
            boss.Kill(false);
            yield return Wait(4.2f);
            TestCapture.Shot("F1_newspaper");
            yield return Wait(6f);
            TestCapture.Shot("F1_boss_reward");

            // Down the trapdoor: the corridor and the Archivist.
            yield return Game.Ctrl.DebugDescend();
            yield return Wait(1.5f);
            TestCapture.Shot("F1_corridor");
            var arch = GameObject.Find("Archivist");
            if (arch != null) Game.Player.Teleport((Vector2)arch.transform.position + new Vector2(-2.2f, -0.3f));
            yield return Wait(1.2f);
            TestCapture.Shot("F1_corridor_archivist");
        }
    }
}
