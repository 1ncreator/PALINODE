using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Breadth-first pathfinder on a small grid with 8-way movement (no corner cutting), from RogueDungeon.
    /// Coordinates are (column, row) with row 0 at the top. Buffers are preallocated; queries do not allocate.
    /// </summary>
    public sealed class GridPathfinder
    {
        private static readonly Vector2Int[] Steps =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };

        private readonly bool[] _blocked;
        private readonly int[] _cameFrom;
        private readonly int[] _visitStamp;
        private readonly int[] _queue;
        private int _stamp;

        public GridPathfinder(int width, int height)
        {
            Width = width;
            Height = height;
            int size = width * height;
            _blocked = new bool[size];
            _cameFrom = new int[size];
            _visitStamp = new int[size];
            _queue = new int[size];
        }

        public int Width { get; }
        public int Height { get; }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public void SetBlocked(int x, int y, bool blocked)
        {
            if (InBounds(x, y)) _blocked[y * Width + x] = blocked;
        }

        public bool IsWalkable(int x, int y) => InBounds(x, y) && !_blocked[y * Width + x];

        /// <summary>Shortest path, start and goal inclusive. False when either end is blocked or unreachable.</summary>
        public bool FindPath(Vector2Int start, Vector2Int goal, List<Vector2Int> result)
        {
            result.Clear();
            if (!IsWalkable(start.x, start.y) || !IsWalkable(goal.x, goal.y)) return false;
            int startIndex = start.y * Width + start.x;
            int goalIndex = goal.y * Width + goal.x;
            if (!Search(startIndex, goalIndex)) return false;
            int current = goalIndex;
            while (current != startIndex)
            {
                result.Add(new Vector2Int(current % Width, current / Width));
                current = _cameFrom[current];
            }
            result.Add(start);
            result.Reverse();
            return true;
        }

        /// <summary>Walkable cells reachable from <paramref name="start"/> (including it).</summary>
        public int CountReachable(Vector2Int start)
        {
            if (!IsWalkable(start.x, start.y)) return 0;
            Search(start.y * Width + start.x, -1);
            int count = 0;
            for (int i = 0; i < _visitStamp.Length; i++) if (_visitStamp[i] == _stamp) count++;
            return count;
        }

        public bool Reachable(Vector2Int a, Vector2Int b)
        {
            if (!IsWalkable(a.x, a.y) || !IsWalkable(b.x, b.y)) return false;
            return Search(a.y * Width + a.x, b.y * Width + b.x);
        }

        private bool Search(int startIndex, int goalIndex)
        {
            _stamp++;
            int head = 0, tail = 0;
            _queue[tail++] = startIndex;
            _visitStamp[startIndex] = _stamp;
            _cameFrom[startIndex] = startIndex;
            while (head < tail)
            {
                int current = _queue[head++];
                if (current == goalIndex) return true;
                int cx = current % Width, cy = current / Width;
                for (int i = 0; i < Steps.Length; i++)
                {
                    var step = Steps[i];
                    int nx = cx + step.x, ny = cy + step.y;
                    if (!IsWalkable(nx, ny)) continue;
                    // Diagonal moves may not squeeze between two blocked orthogonal cells.
                    if (step.x != 0 && step.y != 0 && (!IsWalkable(cx + step.x, cy) || !IsWalkable(cx, cy + step.y))) continue;
                    int next = ny * Width + nx;
                    if (_visitStamp[next] == _stamp) continue;
                    _visitStamp[next] = _stamp;
                    _cameFrom[next] = current;
                    _queue[tail++] = next;
                }
            }
            return false;
        }
    }
}
