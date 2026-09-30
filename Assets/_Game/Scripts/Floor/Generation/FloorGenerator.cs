using System;
using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>Plain generation settings (filled from floor1.json "generation", or directly in tests).</summary>
    public sealed class FloorGenSettings
    {
        public int GridWidth = 9, GridHeight = 8;
        public int MinRooms = 9, MaxRooms = 12;
        public float RejectChance = 0.5f;
        public int MinBossDistance = 3;
        public int MinCombat = 6, MaxCombat = 9;
        public int BigRoomMax = 1;
        public float BigRoomChance = 0.6f;
        public int BigRoomMinDistance = 2;
        public float ShopChance = 0.15f;
        public float DarkChance = 0.25f;
        public int DarkMinDistance = 4;
        public int SecretMinNeighbors = 3;
        public int MaxAttempts = 5000;

        /// <summary>Normal (13×7) templates: weight and "dark" flag. Big (26×7) template count.</summary>
        public float[] NormalWeights = { 1f };
        public bool[] NormalDark = { false };
        public int BigTemplates = 1;
    }

    /// <summary>
    /// Isaac-style floor generator (from RogueDungeon, extended). A tree of single cells grows from the centre: a
    /// neighbour is added only if it is free, touches exactly one occupied cell (no 2×2 blobs) and a random roll does
    /// not reject it. Then: the farthest dead end becomes the 2×2 boss arena (grown away from its only door), other
    /// dead ends become the special rooms, one ordinary room may grow into a 2×1 hall, and an empty cell touching
    /// three or more rooms hides the secret room. Layouts that break a rule are thrown away (deterministically).
    /// </summary>
    public sealed class FloorGenerator
    {
        private readonly FloorGenSettings _s;
        private readonly Direction[] _dirs = new Direction[4];

        public FloorGenerator(FloorGenSettings settings)
        {
            _s = settings ?? throw new ArgumentNullException(nameof(settings));
            if (_s.GridWidth < 5 || _s.GridHeight < 5) throw new ArgumentException("Floor grid must be at least 5×5.");
            if (_s.MinRooms < 6 || _s.MaxRooms < _s.MinRooms) throw new ArgumentException($"Invalid room count {_s.MinRooms}..{_s.MaxRooms}.");
        }

        public FloorLayout Generate(int seed)
        {
            var rng = new SeededRandom(seed);
            for (int attempt = 0; attempt < _s.MaxAttempts; attempt++)
            {
                var layout = TryGenerate(seed, rng);
                if (layout != null) return layout;
            }
            throw new InvalidOperationException($"Could not generate a floor for seed {seed} in {_s.MaxAttempts} attempts.");
        }

        private FloorLayout TryGenerate(int seed, SeededRandom rng)
        {
            int target = rng.Range(_s.MinRooms, _s.MaxRooms + 1);
            var layout = new FloorLayout(seed, _s.GridWidth, _s.GridHeight);
            var startCell = new Vector2Int(_s.GridWidth / 2, _s.GridHeight / 2);
            layout.Start = layout.AddRoom(startCell);
            layout.Start.Kind = RoomKind.Start;

            // 1. Tree of single cells.
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(startCell);
            while (queue.Count > 0 && layout.Rooms.Count < target)
            {
                var cell = queue.Dequeue();
                DirectionUtil.All.CopyTo(_dirs, 0);
                rng.Shuffle(_dirs);
                foreach (var d in _dirs)
                {
                    if (layout.Rooms.Count >= target) break;
                    var next = cell + d.ToOffset();
                    if (!layout.InBounds(next) || layout.IsOccupied(next)) continue;
                    if (layout.CountOccupiedNeighbors(next) > 1) continue;
                    if (rng.Chance(_s.RejectChance)) continue;
                    layout.AddRoom(next);
                    queue.Enqueue(next);
                }
            }
            if (layout.Rooms.Count < target) return null;
            layout.ComputeDoors();
            layout.ComputeDistances();

            // 2. Boss: the farthest dead end, grown into a 2×2 arena that only touches its parent through the door.
            RoomNode boss = null;
            foreach (var r in layout.Rooms)
                if (r != layout.Start && r.IsDeadEnd && (boss == null || r.Distance > boss.Distance)) boss = r;
            if (boss == null || boss.Distance < _s.MinBossDistance) return null;
            if (!GrowArena(layout, boss, rng)) return null;
            boss.Kind = RoomKind.Boss;
            layout.Boss = boss;

            // 3. Special rooms on the other dead ends.
            var deadEnds = new List<RoomNode>();
            foreach (var r in layout.Rooms)
                if (r != layout.Start && r != boss && r.IsDeadEnd) deadEnds.Add(r);
            rng.Shuffle(deadEnds);
            var specials = new List<RoomKind> { RoomKind.Corrector, RoomKind.Memory };
            if (rng.Chance(_s.ShopChance)) specials.Add(RoomKind.Shop);
            if (deadEnds.Count < specials.Count) return null;
            for (int i = 0; i < specials.Count; i++) deadEnds[i].Kind = specials[i];

            // 4. One ordinary room may become a 2×1 hall (not next to the start).
            int bigs = 0;
            if (_s.BigRoomMax > 0 && rng.Chance(_s.BigRoomChance))
            {
                var candidates = new List<RoomNode>();
                foreach (var r in layout.Rooms)
                    if (r.Kind == RoomKind.Combat && r.Distance >= _s.BigRoomMinDistance) candidates.Add(r);
                rng.Shuffle(candidates);
                foreach (var r in candidates)
                {
                    if (bigs >= _s.BigRoomMax) break;
                    if (TryGrowHall(layout, r, rng)) bigs++;
                }
            }
            layout.ComputeDoors();
            layout.ComputeDistances();
            if (!layout.IsFullyConnected()) return null;

            int combat = 0;
            foreach (var r in layout.Rooms) if (r.Kind == RoomKind.Combat || r.Kind == RoomKind.Big) combat++;
            if (combat < _s.MinCombat || combat > _s.MaxCombat) return null;

            // 5. Secret room: the empty cell touching the most rooms (at least SecretMinNeighbors), never the boss arena.
            if (!PlaceSecret(layout, rng)) return null;

            // 6. Templates, darkness, content seeds.
            foreach (var r in layout.Rooms)
            {
                if (r.Kind == RoomKind.Combat)
                {
                    int t = PickNormalTemplate(rng, r.Distance >= _s.DarkMinDistance);
                    r.TemplateIndex = t;
                    r.Dark = _s.NormalDark[t] || (r.Distance >= _s.DarkMinDistance && rng.Chance(_s.DarkChance));
                }
                else if (r.Kind == RoomKind.Big)
                {
                    r.TemplateIndex = rng.Range(0, Math.Max(1, _s.BigTemplates));
                    r.Dark = r.Distance >= _s.DarkMinDistance && rng.Chance(_s.DarkChance);
                }
                r.ContentSeed = rng.NextInt();
            }
            return layout;
        }

        private int PickNormalTemplate(SeededRandom rng, bool darkAllowed)
        {
            var w = new float[_s.NormalWeights.Length];
            for (int i = 0; i < w.Length; i++)
                w[i] = !darkAllowed && i < _s.NormalDark.Length && _s.NormalDark[i] ? 0f : _s.NormalWeights[i];
            int pick = rng.Weighted(w);
            return pick < 0 ? 0 : pick;
        }

        private static bool GrowArena(FloorLayout layout, RoomNode b, SeededRandom rng)
        {
            if (b.Doors.Count != 1) return false;
            var cell = b.Origin;
            var toParent = b.Doors[0].Side.ToOffset();
            var parentCell = cell + toParent;
            var away = -toParent;
            var side = new Vector2Int(away.y, away.x);   // perpendicular
            var sides = new List<Vector2Int> { side, -side };
            rng.Shuffle(sides);
            foreach (var s in sides)
            {
                var block = new[] { cell, cell + away, cell + s, cell + away + s };
                bool ok = true;
                for (int i = 1; i < block.Length && ok; i++)
                    if (!layout.InBounds(block[i]) || layout.IsOccupied(block[i])) ok = false;
                if (!ok) continue;
                foreach (var c in block)
                {
                    foreach (var d in DirectionUtil.All)
                    {
                        var n = c + d.ToOffset();
                        if (Array.IndexOf(block, n) >= 0) continue;
                        if (!layout.IsOccupied(n)) continue;
                        if (c == cell && n == parentCell) continue;
                        ok = false;
                    }
                }
                if (!ok) continue;
                var min = Vector2Int.Min(Vector2Int.Min(block[0], block[1]), Vector2Int.Min(block[2], block[3]));
                layout.Grow(b, min, new Vector2Int(2, 2));
                return true;
            }
            return false;
        }

        private static bool TryGrowHall(FloorLayout layout, RoomNode r, SeededRandom rng)
        {
            var dirs = new List<Direction> { Direction.Left, Direction.Right };
            rng.Shuffle(dirs);
            foreach (var d in dirs)
            {
                var e = r.Origin + d.ToOffset();
                if (!layout.InBounds(e) || layout.IsOccupied(e)) continue;
                bool alone = true;
                foreach (var dd in DirectionUtil.All)
                {
                    var n = e + dd.ToOffset();
                    if (n != r.Origin && layout.IsOccupied(n)) alone = false;
                }
                if (!alone) continue;
                layout.Grow(r, Vector2Int.Min(r.Origin, e), new Vector2Int(2, 1));
                r.Kind = RoomKind.Big;
                return true;
            }
            return false;
        }

        private bool PlaceSecret(FloorLayout layout, SeededRandom rng)
        {
            var best = new List<Vector2Int>();
            int bestCount = 0;
            for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
            {
                var c = new Vector2Int(x, y);
                if (layout.IsOccupied(c)) continue;
                var rooms = new HashSet<int>();
                bool nearBoss = false;
                foreach (var d in DirectionUtil.All)
                {
                    var n = layout.RoomAt(c + d.ToOffset());
                    if (n == null) continue;
                    if (n.Kind == RoomKind.Boss) nearBoss = true;
                    rooms.Add(n.Index);
                }
                if (nearBoss || rooms.Count < _s.SecretMinNeighbors) continue;
                if (rooms.Count > bestCount) { bestCount = rooms.Count; best.Clear(); }
                if (rooms.Count == bestCount) best.Add(c);
            }
            if (best.Count == 0) return false;
            var cell = best[rng.Range(0, best.Count)];
            var secret = layout.AddRoom(cell);
            secret.Kind = RoomKind.Secret;
            secret.Distance = -1;
            layout.Secret = secret;
            foreach (var d in DirectionUtil.All)
            {
                var n = layout.RoomAt(cell + d.ToOffset());
                if (n == null || n == secret) continue;
                secret.Doors.Add(new DoorLink { Cell = cell, Side = d, Neighbor = n.Index, Secret = true });
                n.Doors.Add(new DoorLink { Cell = cell + d.ToOffset(), Side = d.Opposite(), Neighbor = secret.Index, Secret = true });
            }
            return true;
        }
    }
}
