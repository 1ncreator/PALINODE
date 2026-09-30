using System.Collections.Generic;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>One planned enemy (or Letters group) in a room.</summary>
    public struct SpawnEntry
    {
        public string Type;
        public Vector2Int Cell;
        public int Group;      // Letters per group
        public bool Elite;
    }

    /// <summary>
    /// Spends a room's enemy points (floor1.json "difficulty.budget" by distance) on enemies picked by weight
    /// (adapted from RogueDungeon's EnemySpawnPlanner). Headlights only in dark rooms, far enough from the start.
    /// Deterministic for a room seed.
    /// </summary>
    public static class SpawnPlanner
    {
        public static int Cost(JNode spec, string type, int group)
        {
            if (type != "letters") return spec.Int("points", 1);
            int per = Mathf.Max(1, spec.Int("perPoints", 3));
            return spec.Int("points", 2) * Mathf.CeilToInt(group / (float)per);
        }

        public static List<SpawnEntry> Plan(FloorDB db, RoomNode node, IReadOnlyList<Vector2Int> points, SeededRandom rng, int budget)
        {
            var list = new List<SpawnEntry>();
            if (points == null || points.Count == 0) return list;
            var cells = new List<Vector2Int>(points);
            rng.Shuffle(cells);
            var enemies = db["enemies"];
            int left = budget, next = 0;
            int safety = 0;
            while (left > 0 && safety++ < 32)
            {
                var ids = new List<string>();
                var weights = new List<float>();
                foreach (var id in FloorDB.EnemyIds)
                {
                    var e = enemies[id];
                    if (e.IsNull) continue;
                    if (node.Distance < e.Int("minDistance", 1)) continue;
                    if (e.Bool("darkOnly") && !node.Dark) continue;
                    int minCost = Cost(e, id, e.Int("groupMin", 3));
                    if (minCost > left) continue;
                    ids.Add(id);
                    weights.Add(e.Num("weight", 1f));
                }
                int pick = rng.Weighted(weights);
                if (pick < 0) break;
                string type = ids[pick];
                var spec = enemies[type];
                int group = 1;
                if (type == "letters")
                {
                    int gmin = spec.Int("groupMin", 3), gmax = spec.Int("groupMax", 6);
                    group = rng.Range(gmin, gmax + 1);
                    while (group > gmin && Cost(spec, type, group) > left) group--;
                }
                left -= Cost(spec, type, group);
                list.Add(new SpawnEntry { Type = type, Cell = cells[next % cells.Count], Group = group });
                next++;
            }
            return list;
        }
    }

    public static class EnemyFactory
    {
        public static EnemyBase Create(string type, RoomView room, Vector2 pos, bool elite)
        {
            var spec = Game.DB.Enemy(type);
            var go = new GameObject($"{type}{(elite ? " (Witness)" : "")}");
            go.transform.SetParent(room.ActorRoot, false);
            EnemyBase e;
            switch (type)
            {
                case "bug": e = go.AddComponent<BugEnemy>(); break;
                case "blot": e = go.AddComponent<BlotEnemy>(); break;
                case "compositor": e = go.AddComponent<CompositorEnemy>(); break;
                case "headlights": e = go.AddComponent<HeadlightsEnemy>(); break;
                default: e = go.AddComponent<LetterEnemy>(); break;
            }
            e.Setup(type, spec, room, pos, elite);
            return e;
        }

        /// <summary>A Letters group around a point; 5% of groups of four or more first spell MARA.</summary>
        public static List<EnemyBase> CreateLetters(RoomView room, Vector2 center, int count, bool eliteFirst, bool allowMara = true)
        {
            var spec = Game.DB.Enemy("letters");
            string pool = spec.Str("letters", "MMAARREELLIISSTON");
            var swarm = new LetterSwarm();
            var list = new List<EnemyBase>();
            bool mara = allowMara && count >= 4 && Random.value < spec.Num("maraChance", 0.05f);
            const string word = "MARA";
            for (int i = 0; i < count; i++)
            {
                Vector2 p = room.Frame.ClampToFloor(center + Random.insideUnitCircle * 0.8f, 0.3f);
                var go = new GameObject("letter");
                go.transform.SetParent(room.ActorRoot, false);
                var l = go.AddComponent<LetterEnemy>();
                l.Setup("letters", spec, room, p, eliteFirst && i == 0);
                l.Configure(swarm, mara && i < 4 ? word[i] : pool[Random.Range(0, pool.Length)]);
                list.Add(l);
            }
            if (mara)
            {
                swarm.Mara = true;
                swarm.MaraAt = center;
                swarm.MaraUntil = Time.time + Game.DB.Data["enemies"].Num("spawnGrace", 0.5f) + 1.2f;
                // Only the four spelling letters stand in line; the others wait with them.
            }
            return list;
        }
    }
}
