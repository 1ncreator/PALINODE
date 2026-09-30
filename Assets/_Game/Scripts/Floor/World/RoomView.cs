using System;
using System.Collections.Generic;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Scene representation of one room (adapted from RogueDungeon's RoomView): painted background, walls with door
    /// gaps, doors, template objects, floor surfaces (puddles slow, belts push), enemies and pickups. Doors lock on
    /// the first visit while enemies live and open for good once the room is cleared.
    /// </summary>
    public sealed class RoomView : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _splats = new List<SpriteRenderer>();
        private readonly List<(Rect rect, float until, SpriteRenderer sr)> _tempPuddles = new List<(Rect, float, SpriteRenderer)>();
        private bool[,] _puddle;
        private Vector2Int[,] _belt;
        private bool[,] _solid;
        private float _beltSpeed = 2f, _slow = 0.4f;
        private JNode _objects;

        public RoomNode Node { get; private set; }
        public RoomFrame Frame { get; private set; }
        public RoomTemplate Template { get; private set; }
        public GridPathfinder Pathfinder { get; private set; }
        public SpriteRenderer Background { get; private set; }
        public Transform DecalRoot { get; private set; }
        public Transform PropRoot { get; private set; }
        public Transform ActorRoot { get; private set; }
        public readonly List<DoorView> Doors = new List<DoorView>();
        public readonly List<EnemyBase> Enemies = new List<EnemyBase>();
        public readonly List<Pickup> Pickups = new List<Pickup>();
        public IReadOnlyList<SpriteRenderer> Splats => _splats;

        public bool Visited { get; private set; }
        public bool Cleared { get; private set; }
        public bool Spawned { get; set; }
        /// <summary>Called when the last enemy dies; returns true if another wave was started.</summary>
        public Func<RoomView, bool> NextWave;
        public event Action<RoomView> RoomCleared;
        public bool IsDark => Node.Dark;
        public string BackgroundName { get; private set; }

        // ------------------------------------------------------------------ build

        public void Build(RoomNode node, FloorLayout layout, Vector2 center, string background, RoomTemplate template, FloorDB db)
        {
            Node = node;
            BackgroundName = background;
            var spec = db.Background(background) ?? db.Background("F1_room_b");
            var sprite = FloorSprites.Get(spec.Sprite);
            Vector2 imgPx = sprite != null ? new Vector2(sprite.rect.width, sprite.rect.height) : new Vector2(1707, 921);
            Frame = new RoomFrame(spec, center, imgPx);
            Template = template ?? RoomTemplate.Empty("empty", Frame.Cells.x, Frame.Cells.y);
            if (Template.Width != Frame.Cells.x || Template.Height != Frame.Cells.y)
                Template = RoomTemplate.Empty("fit", Frame.Cells.x, Frame.Cells.y);
            _objects = db["objects"];
            _beltSpeed = _objects["belt"].Num("speed", 2f);
            _slow = _objects["puddle"].Num("slow", 0.4f);

            DecalRoot = new GameObject("Decals").transform;
            DecalRoot.SetParent(transform, false);
            PropRoot = new GameObject("Props").transform;
            PropRoot.SetParent(transform, false);
            ActorRoot = new GameObject("Actors").transform;
            ActorRoot.SetParent(transform, false);

            // Background: native pixels → uniform scale so the floor is exactly Cells wide.
            Background = FloorSprites.Make(transform, "Background", sprite, 0f, FloorSprites.BackgroundOrder);
            Background.transform.position = Frame.ImageCenter;
            Background.transform.localScale = Vector3.one * Frame.PxToUnit * (sprite != null ? sprite.pixelsPerUnit : 100f);

            int w = Frame.Cells.x, h = Frame.Cells.y;
            _puddle = new bool[w, h];
            _belt = new Vector2Int[w, h];
            _solid = new bool[w, h];
            Pathfinder = new GridPathfinder(w, h);

            BuildDoors(layout, db);
            BuildWalls();
            BuildTemplateObjects();
            foreach (var b in spec.Blocked) BlockCell(b, Layers.LowObstacle, "Furniture");
            for (int x = 0; x < w; x++)
            for (int r = 0; r < h; r++)
                Pathfinder.SetBlocked(x, r, _solid[x, r]);

            Cleared = !node.HasEnemies;
        }

        private void BuildDoors(FloorLayout layout, FloorDB db)
        {
            var doorsSpec = db["doors"];
            float sideScale = doorsSpec.Num("sideScale", 0.8f);
            foreach (var link in Node.Doors)
            {
                var neighbor = layout.Rooms[link.Neighbor];
                DoorKind kind = link.Secret ? DoorKind.Secret
                    : Node.Kind == RoomKind.Boss || neighbor.Kind == RoomKind.Boss ? DoorKind.Boss : DoorKind.Normal;
                var spec = kind == DoorKind.Secret ? doorsSpec["secret"] : kind == DoorKind.Boss ? doorsSpec["boss"] : doorsSpec["normal"];
                var cell = Frame.DoorCell(link.Cell - Node.Origin, Node.Size, link.Side);
                var edge = Frame.DoorEdge(cell, link.Side);
                bool vertical = link.Side == Direction.Up || link.Side == Direction.Down;
                float gap = vertical ? 1.15f : Mathf.Max(0.95f, Frame.CellSize.y * 1.35f);
                var go = new GameObject($"Door {link.Side} → {neighbor.Kind}#{neighbor.Index}");
                go.transform.SetParent(transform, false);
                var door = go.AddComponent<DoorView>();
                door.Init(this, link, kind, cell, edge, gap, spec, sideScale);
                Doors.Add(door);
            }
        }

        private void BuildWalls()
        {
            var r = Frame.FloorRect;
            float T = DoorView.Thickness;
            var walls = new GameObject("Walls") { layer = Layers.Wall };
            walls.transform.SetParent(transform, false);
            foreach (var side in DirectionUtil.All)
            {
                bool vertical = side == Direction.Up || side == Direction.Down;
                float from = vertical ? r.xMin - T : r.yMin - T;
                float to = vertical ? r.xMax + T : r.yMax + T;
                var gaps = new List<(float a, float b)>();
                foreach (var d in Doors)
                {
                    if (d.Side != side) continue;
                    float c = vertical ? d.Edge.x : d.Edge.y;
                    gaps.Add((c - d.Gap * 0.5f, c + d.Gap * 0.5f));
                }
                gaps.Sort((x, y) => x.a.CompareTo(y.a));
                float cur = from;
                foreach (var g in gaps)
                {
                    AddWall(walls, side, cur, g.a, r, T);
                    cur = g.b;
                }
                AddWall(walls, side, cur, to, r, T);
            }
        }

        private static void AddWall(GameObject walls, Direction side, float a, float b, Rect r, float T)
        {
            if (b - a < 0.01f) return;
            var box = walls.AddComponent<BoxCollider2D>();
            switch (side)
            {
                case Direction.Up: box.offset = new Vector2((a + b) * 0.5f, r.yMax + T * 0.5f); box.size = new Vector2(b - a, T); break;
                case Direction.Down: box.offset = new Vector2((a + b) * 0.5f, r.yMin - T * 0.5f); box.size = new Vector2(b - a, T); break;
                case Direction.Left: box.offset = new Vector2(r.xMin - T * 0.5f, (a + b) * 0.5f); box.size = new Vector2(T, b - a); break;
                default: box.offset = new Vector2(r.xMax + T * 0.5f, (a + b) * 0.5f); box.size = new Vector2(T, b - a); break;
            }
            // The wall GameObject sits at the origin; offsets are world coordinates.
        }

        private BoxCollider2D BlockCell(Vector2Int c, int layer, string name, int widthCells = 1)
        {
            if (c.x < 0 || c.y < 0 || c.x >= Frame.Cells.x || c.y >= Frame.Cells.y) return null;
            for (int i = 0; i < widthCells && c.x + i < Frame.Cells.x; i++) _solid[c.x + i, c.y] = true;
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(PropRoot, false);
            Vector2 center = Frame.CellCenter(c) + new Vector2((widthCells - 1) * 0.5f * Frame.CellSize.x, 0f);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(widthCells * Frame.CellSize.x * 0.92f, Frame.CellSize.y * 0.85f);
            return box;
        }

        private void BuildTemplateObjects()
        {
            var t = Template;
            // Presses (2×1).
            foreach (var a in t.PressAnchors())
            {
                var box = BlockCell(a, Layers.Obstacle, "Press", 2);
                var sr = FloorSprites.Make(box.transform, "Sprite", FloorSprites.Get(_objects["press"].Str("sprite")), _objects["press"].Num("width", 2f), 0);
                sr.transform.position = FloorBase(a, 2);
                sr.sortingOrder = FloorSprites.Order(sr.transform.position.y);
            }
            for (int x = 0; x < t.Width; x++)
            for (int r = 0; r < t.Height; r++)
            {
                var c = new Vector2Int(x, r);
                switch (t.At(x, r))
                {
                    case Tile.Stack:
                    {
                        var spec = _objects["stack"];
                        var stages = new List<Sprite>();
                        foreach (var s in spec["sprites"].Items()) stages.Add(FloorSprites.Get(s.AsString()));
                        var box = BlockCell(c, Layers.Obstacle, "Stack");
                        float width = spec.Num("width", 0.95f);
                        var sr = FloorSprites.Make(box.transform, "Sprite", stages.Count > 0 ? stages[0] : null, width, 0);
                        sr.transform.position = FloorBase(c, 1);
                        sr.sortingOrder = FloorSprites.Order(sr.transform.position.y);
                        sr.flipX = (x + r) % 2 == 0;
                        box.gameObject.AddComponent<PaperStack>().Init(this, sr, stages.ToArray(), box, spec.Int("hits", 3), width, spec.Num("sheetChance", 0.1f));
                        break;
                    }
                    case Tile.TypeCase:
                    {
                        var box = BlockCell(c, Layers.LowObstacle, "TypeCase");
                        var sr = FloorSprites.Make(box.transform, "Sprite", FloorSprites.Get(_objects["typecase"].Str("sprite")), _objects["typecase"].Num("width", 1f), 0);
                        sr.transform.position = FloorBase(c, 1);
                        sr.sortingOrder = FloorSprites.Order(sr.transform.position.y);
                        break;
                    }
                    case Tile.Drying:
                    {
                        var go = new GameObject("Drying") { layer = Layers.ShotScreen };
                        go.transform.SetParent(PropRoot, false);
                        go.transform.position = Frame.CellCenter(c);
                        var box = go.AddComponent<BoxCollider2D>();
                        box.size = new Vector2(Frame.CellSize.x * 0.95f, Frame.CellSize.y * 0.6f);
                        var sr = FloorSprites.Make(go.transform, "Sprite", FloorSprites.Get(_objects["drying"].Str("sprite")), _objects["drying"].Num("width", 1.05f), 0);
                        sr.transform.position = FloorBase(c, 1);
                        sr.sortingOrder = FloorSprites.Order(sr.transform.position.y);
                        go.AddComponent<DryingSheets>().Init(sr, box, _objects["drying"].Int("hits", 1));
                        break;
                    }
                    case Tile.Puddle:
                    {
                        _puddle[x, r] = true;
                        var sr = FloorSprites.Make(DecalRoot, "Puddle", FloorSprites.Get(_objects["puddle"].Str("sprite")), _objects["puddle"].Num("width", 1.25f),
                            FloorSprites.FloorDecalOrder + 100);
                        sr.transform.position = Frame.CellCenter(c);
                        var s = sr.transform.localScale;
                        sr.transform.localScale = new Vector3(s.x, s.y * 0.8f, 1f);
                        sr.flipX = (x * 7 + r) % 2 == 0;
                        break;
                    }
                    case Tile.BeltUp:
                    case Tile.BeltDown:
                    case Tile.BeltLeft:
                    case Tile.BeltRight:
                        _belt[x, r] = RoomTemplate.BeltVector(t.At(x, r));
                        break;
                }
            }
            BuildBeltVisuals();
        }

        /// <summary>Feet position for something standing on cells [c.x, c.x+width).</summary>
        public Vector2 FloorBase(Vector2Int c, int width) =>
            Frame.CellBase(c.x, c.y) + new Vector2((width - 1) * 0.5f * Frame.CellSize.x, 0f);

        private void BuildBeltVisuals()
        {
            var t = Template;
            var seg = FloorSprites.Get(_objects["belt"].Str("sprite"));
            var end = FloorSprites.Get(_objects["belt"].Str("end"));
            // Runs of the same belt tile along its axis.
            var done = new bool[t.Width, t.Height];
            for (int r = 0; r < t.Height; r++)
            for (int x = 0; x < t.Width; x++)
            {
                var v = _belt[x, r];
                if (v == Vector2Int.zero || done[x, r]) continue;
                bool horizontal = v.x != 0;
                int len = 0;
                while (true)
                {
                    int cx = horizontal ? x + len : x, cr = horizontal ? r : r + len;
                    if (cx >= t.Width || cr >= t.Height || _belt[cx, cr] != v) break;
                    done[cx, cr] = true;
                    len++;
                }
                var a = Frame.CellCenter(x, r);
                var b = Frame.CellCenter(horizontal ? x + len - 1 : x, horizontal ? r : r + len - 1);
                AddBeltRun(a, b, v, horizontal, len, seg, end);
            }
        }

        /// <summary>Draws a belt between two cell centres (also used by the boss arena's side belts).</summary>
        public GameObject AddBeltRun(Vector2 a, Vector2 b, Vector2Int dir, bool horizontal, int len, Sprite seg = null, Sprite end = null)
        {
            seg ??= FloorSprites.Get(_objects["belt"].Str("sprite"));
            end ??= FloorSprites.Get(_objects["belt"].Str("end"));
            var go = new GameObject("Belt");
            go.transform.SetParent(DecalRoot, false);
            Vector2 mid = (a + b) * 0.5f;
            float along = (horizontal ? Mathf.Abs(b.x - a.x) + Frame.CellSize.x : Mathf.Abs(b.y - a.y) + Frame.CellSize.y);
            float across = horizontal ? Frame.CellSize.y * 1.05f : Frame.CellSize.x * 0.95f;
            var sr = FloorSprites.Make(go.transform, "Segment", seg, 0f, FloorSprites.FloorDecalOrder + 400);
            sr.transform.position = mid;
            if (seg != null)
            {
                // The painted belt runs vertically: scale it to the belt's width and tile its segment along the run
                // (turned for horizontal belts), so rollers repeat instead of one stretched drawing. The tile is the
                // middle of the painted segment (no transparent margin, no shadowed ends), so tiles join without gaps.
                seg = BeltTile(seg);
                sr.sprite = seg;
                var sb = seg.bounds.size;
                float s = across / sb.x;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
                sr.size = new Vector2(sb.x, along / s);
                sr.transform.localScale = new Vector3(s, s, 1f);
                if (horizontal) sr.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
            if (end != null)
            {
                Vector2 tip = horizontal ? (dir.x > 0 ? Vector2.Max(a, b) : Vector2.Min(a, b)) : (dir.y > 0 ? (a.y > b.y ? a : b) : (a.y < b.y ? a : b));
                var e = FloorSprites.Make(go.transform, "End", end, 0f, FloorSprites.FloorDecalOrder + 401);
                var eb = end.bounds.size;
                float el = horizontal ? Frame.CellSize.x * 0.7f : Frame.CellSize.y * 0.7f;
                e.transform.position = tip + (Vector2)dir * 0.5f * (horizontal ? Frame.CellSize.x : Frame.CellSize.y);
                if (horizontal)
                {
                    e.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                    e.transform.localScale = new Vector3(across / eb.x, el / eb.y, 1f);
                }
                else e.transform.localScale = new Vector3(across / eb.x, el / eb.y, 1f);
            }
            var chev = new GameObject("Chevrons");
            chev.transform.SetParent(go.transform, false);
            Vector2 from = dir.x > 0 || dir.y > 0 ? Vector2.Min(a, b) : Vector2.Max(a, b);
            Vector2 to = dir.x > 0 || dir.y > 0 ? Vector2.Max(a, b) : Vector2.Min(a, b);
            if (!horizontal) { from = new Vector2(a.x, dir.y > 0 ? Mathf.Min(a.y, b.y) : Mathf.Max(a.y, b.y)); to = new Vector2(a.x, dir.y > 0 ? Mathf.Max(a.y, b.y) : Mathf.Min(a.y, b.y)); }
            chev.AddComponent<BeltVisual>().Init(from, to, _beltSpeed * (horizontal ? 1f : Frame.CellSize.y), Mathf.Max(2, len));
            return go;
        }

        private static Sprite _beltTile;
        private static Sprite _beltSource;

        private static Sprite BeltTile(Sprite seg)
        {
            if (_beltTile != null && _beltSource == seg) return _beltTile;
            var r = seg.rect;
            var inner = new Rect(r.x + r.width * 0.06f, r.y + r.height * 0.2f, r.width * 0.88f, r.height * 0.6f);
            _beltSource = seg;
            _beltTile = Sprite.Create(seg.texture, inner, new Vector2(0.5f, 0.5f), seg.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            _beltTile.name = seg.name + "_tile";
            return _beltTile;
        }

        /// <summary>Makes cells push (boss belts). Pass Vector2Int.zero to stop them.</summary>
        public void SetBelt(IEnumerable<Vector2Int> cells, Vector2Int dir)
        {
            foreach (var c in cells)
                if (c.x >= 0 && c.y >= 0 && c.x < Frame.Cells.x && c.y < Frame.Cells.y) _belt[c.x, c.y] = dir;
        }

        public void OnObstacleRemoved(Vector3 world)
        {
            var c = Frame.CellAt(world);
            _solid[c.x, c.y] = false;
            Pathfinder.SetBlocked(c.x, c.y, false);
        }

        // ------------------------------------------------------------------ surfaces

        public float SlowAt(Vector2 p)
        {
            if (!Frame.InsideFloor(p)) return 1f;
            var c = Frame.CellAt(p);
            if (_puddle[c.x, c.y]) return 1f - _slow;
            float now = Time.time;
            foreach (var t in _tempPuddles)
                if (t.until > now && t.rect.Contains(p)) return 1f - _slow;
            return 1f;
        }

        public Vector2 PushAt(Vector2 p)
        {
            if (!Frame.InsideFloor(p)) return Vector2.zero;
            var c = Frame.CellAt(p);
            var v = _belt[c.x, c.y];
            return v == Vector2Int.zero ? Vector2.zero : new Vector2(v.x * _beltSpeed, v.y * _beltSpeed * Frame.CellSize.y);
        }

        /// <summary>A temporary ink puddle (landing blot).</summary>
        public void AddPuddle(Vector2 center, float sizeCells, float duration)
        {
            var size = new Vector2(sizeCells * Frame.CellSize.x, sizeCells * Frame.CellSize.y);
            var sr = FloorSprites.Make(DecalRoot, "BlotPuddle", FloorSprites.Get(_objects["puddle"].Str("sprite")), size.x * 1.1f, FloorSprites.FloorDecalOrder + 150);
            sr.transform.position = center;
            var s = sr.transform.localScale;
            sr.transform.localScale = new Vector3(s.x, s.y * 0.8f, 1f);
            _tempPuddles.Add((new Rect(center - size * 0.5f, size), Time.time + duration, sr));
            Game.Fx?.FadeOut(sr, duration - 0.8f, 0.8f);
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _tempPuddles.Count - 1; i >= 0; i--)
                if (_tempPuddles[i].until <= now) _tempPuddles.RemoveAt(i);
        }

        public void RegisterSplat(SpriteRenderer sr) => _splats.Add(sr);

        public void RemoveSplat(SpriteRenderer sr)
        {
            _splats.Remove(sr);
            if (sr != null) Destroy(sr.gameObject);
        }

        public void ClearSplats()
        {
            foreach (var s in _splats) if (s != null) Destroy(s.gameObject);
            _splats.Clear();
        }

        public bool IsWalkableCell(Vector2Int c) => Pathfinder.IsWalkable(c.x, c.y);

        /// <summary>A random free cell centre at least <paramref name="minDist"/> from <paramref name="avoid"/>.</summary>
        public Vector2 RandomFreePoint(Vector2 avoid, float minDist)
        {
            for (int i = 0; i < 40; i++)
            {
                var c = new Vector2Int(UnityEngine.Random.Range(1, Frame.Cells.x - 1), UnityEngine.Random.Range(1, Frame.Cells.y - 1));
                if (!IsWalkableCell(c)) continue;
                var p = Frame.CellCenter(c);
                if ((p - avoid).sqrMagnitude >= minDist * minDist) return p;
            }
            return Frame.Center;
        }

        // ------------------------------------------------------------------ visit / clear

        public void Enter()
        {
            Visited = true;
            if (Cleared || Node.Kind == RoomKind.Secret)
            {
                SetDoorsOpen(true, false);
                return;
            }
            if (Enemies.Count > 0 || !Spawned) SetDoorsOpen(false, true);
        }

        public void Leave()
        {
            ClearSplats();
        }

        public void AddEnemy(EnemyBase e)
        {
            Enemies.Add(e);
            e.transform.SetParent(ActorRoot, true);
        }

        public void RemoveEnemy(EnemyBase e)
        {
            if (!Enemies.Remove(e)) return;
            if (Enemies.Count > 0 || Cleared) return;
            if (NextWave != null && NextWave(this)) return;
            MarkCleared(true);
        }

        public void MarkCleared(bool announce)
        {
            if (Cleared) return;
            Cleared = true;
            SetDoorsOpen(true, announce);
            if (announce) RoomCleared?.Invoke(this);
        }

        public void SetDoorsOpen(bool open, bool sound)
        {
            bool any = false;
            foreach (var d in Doors)
            {
                if (d.Kind == DoorKind.Secret) continue;
                if (d.IsOpen != open) any = true;
                d.SetOpen(open, false);
            }
            if (sound && any) Game.Audio?.Play(open ? "door_open" : "door_close");
        }

        public DoorView DoorFor(int neighborIndex)
        {
            foreach (var d in Doors) if (d.Link.Neighbor == neighborIndex) return d;
            return null;
        }

        /// <summary>Where Elias appears entering through <paramref name="door"/> (one step inside).</summary>
        public Vector2 EntryPoint(DoorView door)
        {
            var p = Frame.CellCenter(door.Cell);
            Vector2 inward = -door.Side.ToVector();
            return p + new Vector2(inward.x * 0.2f, inward.y * 0.15f);
        }

        public void SetActive(bool on) => gameObject.SetActive(on);
    }
}
