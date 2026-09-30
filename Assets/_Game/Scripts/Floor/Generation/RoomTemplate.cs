using System;
using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Floor
{
    public enum Tile
    {
        Floor, Press, Stack, TypeCase, Drying, Puddle, BeltUp, BeltDown, BeltLeft, BeltRight
    }

    /// <summary>
    /// Room layout parsed from a text grid (Floor1/Data/room_templates.json), adapted from RogueDungeon.
    /// Legend: . floor · P press (always "PP", 2×1) · S paper stack · C type case · D drying sheets · ~ ink puddle ·
    /// &gt; &lt; ^ v feed belt · E enemy point. Coordinates are (column, row) with row 0 at the top (back) wall.
    /// </summary>
    public sealed class RoomTemplate
    {
        public const int CellCols = 13, CellRows = 7;
        public const int MinSpawnDistanceFromDoor = 3;

        private readonly Tile[,] _tiles;
        private readonly List<Vector2Int> _spawns;

        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Dark { get; private set; }
        public bool WallClock { get; private set; }
        public float Weight { get; private set; } = 1f;
        public IReadOnlyList<Vector2Int> SpawnPoints => _spawns;

        private RoomTemplate(string name, Tile[,] tiles, List<Vector2Int> spawns)
        {
            Name = name;
            _tiles = tiles;
            _spawns = spawns;
            Width = tiles.GetLength(0);
            Height = tiles.GetLength(1);
        }

        public static RoomTemplate Empty(string name, int width, int height) =>
            new RoomTemplate(name, new Tile[width, height], new List<Vector2Int>());

        public Tile At(int x, int row) => x < 0 || row < 0 || x >= Width || row >= Height ? Tile.Press : _tiles[x, row];

        public static bool IsSolid(Tile t) => t == Tile.Press || t == Tile.Stack || t == Tile.TypeCase;

        public bool IsBlocked(int x, int row) => IsSolid(At(x, row));

        public static Vector2Int BeltVector(Tile t)
        {
            switch (t)
            {
                case Tile.BeltUp: return new Vector2Int(0, 1);
                case Tile.BeltDown: return new Vector2Int(0, -1);
                case Tile.BeltLeft: return new Vector2Int(-1, 0);
                case Tile.BeltRight: return new Vector2Int(1, 0);
                default: return Vector2Int.zero;
            }
        }

        public static RoomTemplate Parse(string name, IList<string> rows, bool dark = false, bool wallClock = false, float weight = 1f)
        {
            if (rows == null || rows.Count == 0) throw new FormatException($"Room template '{name}' has no rows.");
            int h = rows.Count, w = rows[0].Trim().Length;
            var tiles = new Tile[w, h];
            var spawns = new List<Vector2Int>();
            for (int r = 0; r < h; r++)
            {
                string line = rows[r].Trim();
                if (line.Length != w) throw new FormatException($"Room template '{name}' row {r} has {line.Length} columns, expected {w}.");
                for (int x = 0; x < w; x++)
                {
                    char c = line[x];
                    switch (c)
                    {
                        case '.': break;
                        case 'E': spawns.Add(new Vector2Int(x, r)); break;
                        case 'P':
                            // A press is 2×1: the pair "PP" is one machine.
                            if (x + 1 >= w || line[x + 1] != 'P')
                                throw new FormatException($"Room template '{name}' has a lone 'P' at ({x},{r}); presses are 'PP'.");
                            tiles[x, r] = Tile.Press;
                            tiles[x + 1, r] = Tile.Press;
                            x++;
                            break;
                        case 'S': tiles[x, r] = Tile.Stack; break;
                        case 'C': tiles[x, r] = Tile.TypeCase; break;
                        case 'D': tiles[x, r] = Tile.Drying; break;
                        case '~': tiles[x, r] = Tile.Puddle; break;
                        case '^': tiles[x, r] = Tile.BeltUp; break;
                        case 'v': tiles[x, r] = Tile.BeltDown; break;
                        case '<': tiles[x, r] = Tile.BeltLeft; break;
                        case '>': tiles[x, r] = Tile.BeltRight; break;
                        default: throw new FormatException($"Room template '{name}' has unknown symbol '{c}' at ({x},{r}).");
                    }
                }
            }
            return new RoomTemplate(name, tiles, spawns) { Dark = dark, WallClock = wallClock, Weight = weight };
        }

        /// <summary>
        /// The press anchor cells (left half of each "PP").
        /// </summary>
        public IEnumerable<Vector2Int> PressAnchors()
        {
            for (int r = 0; r < Height; r++)
            for (int x = 0; x < Width; x++)
            {
                if (_tiles[x, r] != Tile.Press) continue;
                yield return new Vector2Int(x, r);
                x++;
            }
        }

        /// <summary>Interior cells next to every possible door of a room made of (w/13)×(h/7) grid cells.</summary>
        public static List<Vector2Int> AllDoorCells(int width, int height)
        {
            var list = new List<Vector2Int>();
            int gx = width / CellCols, gy = height / CellRows;
            for (int i = 0; i < gx; i++)
            {
                list.Add(new Vector2Int(i * CellCols + CellCols / 2, 0));
                list.Add(new Vector2Int(i * CellCols + CellCols / 2, height - 1));
            }
            for (int j = 0; j < gy; j++)
            {
                list.Add(new Vector2Int(0, j * CellRows + CellRows / 2));
                list.Add(new Vector2Int(width - 1, j * CellRows + CellRows / 2));
            }
            return list;
        }

        public GridPathfinder CreatePathfinder()
        {
            var pf = new GridPathfinder(Width, Height);
            for (int x = 0; x < Width; x++)
            for (int r = 0; r < Height; r++)
                pf.SetBlocked(x, r, IsBlocked(x, r));
            return pf;
        }

        /// <summary>
        /// Door cells free, every free cell reachable from every door (obstacles never cut the room apart), enemy
        /// points on free cells, reachable and away from the doors.
        /// </summary>
        public bool Validate(out string error)
        {
            if (Width % CellCols != 0 || Height % CellRows != 0)
            {
                error = $"'{Name}': size {Width}×{Height} is not a multiple of {CellCols}×{CellRows}.";
                return false;
            }
            var doors = AllDoorCells(Width, Height);
            foreach (var d in doors)
                if (IsBlocked(d.x, d.y)) { error = $"'{Name}': door cell {d} is blocked."; return false; }

            var pf = CreatePathfinder();
            int free = 0;
            for (int x = 0; x < Width; x++)
            for (int r = 0; r < Height; r++)
                if (!IsBlocked(x, r)) free++;
            foreach (var d in doors)
            {
                int reach = pf.CountReachable(d);
                if (reach != free) { error = $"'{Name}': only {reach} of {free} free cells are reachable from door {d}."; return false; }
            }
            foreach (var s in _spawns)
            {
                foreach (var d in doors)
                {
                    int dist = Mathf.Abs(d.x - s.x) + Mathf.Abs(d.y - s.y);
                    if (dist < MinSpawnDistanceFromDoor) { error = $"'{Name}': enemy point {s} is too close to door {d}."; return false; }
                }
            }
            if (_spawns.Count == 0) { error = $"'{Name}': no enemy points."; return false; }
            error = null;
            return true;
        }
    }
}
