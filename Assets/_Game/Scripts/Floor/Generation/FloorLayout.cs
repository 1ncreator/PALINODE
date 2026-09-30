using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Floor
{
    public enum RoomKind { Start, Combat, Big, Corrector, Shop, Memory, Secret, Boss }

    /// <summary>A passage between two rooms through the wall of one grid cell.</summary>
    public struct DoorLink
    {
        public Vector2Int Cell;     // grid cell of this room the door is in
        public Direction Side;      // wall of that cell
        public int Neighbor;        // room index on the other side
        public bool Secret;         // a "*" wall that opens after a few hits (door to / from the secret room)
    }

    /// <summary>One room of the floor map: pure data, one or several grid cells (1×1, 2×1 hall, 2×2 boss arena).</summary>
    public sealed class RoomNode
    {
        public RoomNode(int index, Vector2Int origin, Vector2Int size)
        {
            Index = index;
            Origin = origin;
            Size = size;
            Kind = RoomKind.Combat;
            TemplateIndex = -1;
        }

        public int Index { get; }
        /// <summary>Lowest-left grid cell (grid y goes up).</summary>
        public Vector2Int Origin { get; internal set; }
        /// <summary>Size in grid cells.</summary>
        public Vector2Int Size { get; internal set; }
        public RoomKind Kind { get; internal set; }
        /// <summary>BFS distance (rooms) from the start room through ordinary doors; -1 for the secret room.</summary>
        public int Distance { get; internal set; }
        public int TemplateIndex { get; internal set; }
        public int ContentSeed { get; internal set; }
        public bool Dark { get; internal set; }
        public readonly List<DoorLink> Doors = new List<DoorLink>();

        public IEnumerable<Vector2Int> Cells
        {
            get
            {
                for (int x = 0; x < Size.x; x++)
                for (int y = 0; y < Size.y; y++)
                    yield return Origin + new Vector2Int(x, y);
            }
        }

        public int OpenDoorCount
        {
            get
            {
                int n = 0;
                foreach (var d in Doors) if (!d.Secret) n++;
                return n;
            }
        }

        public bool IsDeadEnd => OpenDoorCount == 1;
        public bool IsSpecial => Kind != RoomKind.Combat && Kind != RoomKind.Big;
        public bool HasEnemies => Kind == RoomKind.Combat || Kind == RoomKind.Big || Kind == RoomKind.Boss;

        public override string ToString() => $"{Kind}#{Index}@{Origin} {Size.x}x{Size.y} d={Distance}";
    }

    /// <summary>Generated floor: a grid of room indices plus derived data.</summary>
    public sealed class FloorLayout
    {
        private readonly int[,] _grid;
        private readonly List<RoomNode> _rooms = new List<RoomNode>();

        public FloorLayout(int seed, int width, int height)
        {
            Seed = seed;
            Width = width;
            Height = height;
            _grid = new int[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _grid[x, y] = -1;
        }

        public int Seed { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<RoomNode> Rooms => _rooms;
        public RoomNode Start { get; internal set; }
        public RoomNode Boss { get; internal set; }
        public RoomNode Secret { get; internal set; }

        /// <summary>Rooms excluding the secret room (the "9–12 rooms" of the floor).</summary>
        public int MainRoomCount
        {
            get
            {
                int n = 0;
                foreach (var r in _rooms) if (r.Kind != RoomKind.Secret) n++;
                return n;
            }
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;
        public bool IsOccupied(Vector2Int c) => InBounds(c) && _grid[c.x, c.y] >= 0;
        public int IndexAt(Vector2Int c) => InBounds(c) ? _grid[c.x, c.y] : -1;
        public RoomNode RoomAt(Vector2Int c) { int i = IndexAt(c); return i >= 0 ? _rooms[i] : null; }

        public int CountOccupiedNeighbors(Vector2Int c)
        {
            int n = 0;
            foreach (var d in DirectionUtil.All) if (IsOccupied(c + d.ToOffset())) n++;
            return n;
        }

        internal RoomNode AddRoom(Vector2Int cell)
        {
            var node = new RoomNode(_rooms.Count, cell, Vector2Int.one);
            _rooms.Add(node);
            _grid[cell.x, cell.y] = node.Index;
            return node;
        }

        /// <summary>Adds grid cells to an existing room (merging a 2×1 hall or growing the 2×2 arena).</summary>
        internal void Grow(RoomNode room, Vector2Int origin, Vector2Int size)
        {
            room.Origin = origin;
            room.Size = size;
            foreach (var c in room.Cells) _grid[c.x, c.y] = room.Index;
        }

        /// <summary>Rebuilds door links between neighbouring rooms (the secret room's links are added separately).</summary>
        internal void ComputeDoors()
        {
            foreach (var room in _rooms)
            {
                room.Doors.Clear();
                if (room.Kind == RoomKind.Secret) continue;
                foreach (var cell in room.Cells)
                foreach (var d in DirectionUtil.All)
                {
                    var n = RoomAt(cell + d.ToOffset());
                    if (n == null || n == room || n.Kind == RoomKind.Secret) continue;
                    room.Doors.Add(new DoorLink { Cell = cell, Side = d, Neighbor = n.Index });
                }
            }
        }

        internal void ComputeDistances()
        {
            foreach (var r in _rooms) r.Distance = -1;
            var q = new Queue<RoomNode>();
            Start.Distance = 0;
            q.Enqueue(Start);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var d in cur.Doors)
                {
                    if (d.Secret) continue;
                    var n = _rooms[d.Neighbor];
                    if (n.Distance >= 0) continue;
                    n.Distance = cur.Distance + 1;
                    q.Enqueue(n);
                }
            }
        }

        public bool IsFullyConnected()
        {
            foreach (var r in _rooms) if (r.Kind != RoomKind.Secret && r.Distance < 0) return false;
            return true;
        }

        /// <summary>
        /// True if four grid cells in a square are occupied by more than one room (the 2×2 arena itself is allowed;
        /// the hidden secret room, which by design nestles between rooms, is ignored).
        /// </summary>
        public bool HasTwoByTwoBlock()
        {
            int G(int x, int y)
            {
                int i = _grid[x, y];
                return i >= 0 && _rooms[i].Kind == RoomKind.Secret ? -1 : i;
            }
            for (int x = 0; x < Width - 1; x++)
            for (int y = 0; y < Height - 1; y++)
            {
                int a = G(x, y), b = G(x + 1, y), c = G(x, y + 1), d = G(x + 1, y + 1);
                if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                if (a == b && b == c && c == d) continue;
                return true;
            }
            return false;
        }

        public RoomNode Neighbor(RoomNode room, DoorLink link) => _rooms[link.Neighbor];
    }
}
