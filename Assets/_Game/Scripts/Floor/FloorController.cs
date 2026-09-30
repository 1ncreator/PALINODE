using System;
using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.Effects;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Palinode.Floor
{
    /// <summary>
    /// Floor I "The Press": generates the floor (seeded), builds every room, runs Elias through it and handles room
    /// transitions, clearing, rewards, items, the red pencil, the boss, death as a revision and the way out
    /// (trapdoor → corridor with the Archivist → "Chapter II" → to be continued).
    /// </summary>
    public sealed partial class FloorController : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Light2D globalLight;

        /// <summary>Tests: fixed seed / autopilot.</summary>
        public static int SeedOverride;
        public static bool BotEnabled;
        /// <summary>True once "To be continued" was reached (tests).</summary>
        public static bool Reached { get; private set; }
        public static int RunsStarted { get; private set; }
        /// <summary>Screenshot markers (tests).</summary>
        public static event Action<string> Marker;

        private GameRoot _root;
        private FloorDB _db;
        private FloorLayout _layout;
        private RoomView[] _rooms;
        private Elias _elias;
        private FloorCamera _camera;
        private FloorHud _hud;
        private FloorMap _map;
        private SeededRandom _rng;
        private Light2D _eliasLight;
        private bool _transition, _dead, _ending, _paused, _started;
        private float _hitStopUntil;
        private float _lightTarget = 1f;
        private bool _promptShown;
        private bool _bossCleared;
        private TirazhBoss _boss;
        private GameObject _trapdoor;
        private int _ghostRoom = -1;
        private bool _ghostDone;
        private int _witnessRoom = -1;
        private string _witnessType;
        private readonly HashSet<string> _markersSent = new HashSet<string>();
        private bool _revealMap;

        public bool AllowHurtWhileLocked => false;
        public FloorLayout Layout => _layout;
        public IList<RoomView> Rooms => _rooms;
        public FloorHud Hud => _hud;
        public bool Transitioning => _transition;
        public bool Dead => _dead;
        public TirazhBoss Boss => _boss;
        public bool BossCleared => _bossCleared;
        public GameObject Trapdoor => _trapdoor;
        public bool InCorridor { get; private set; }

        public void Configure(Camera cam, Light2D light)
        {
            mainCamera = cam;
            globalLight = light;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Reached = false;
            Game.Clear();
            Game.Ctrl = this;
            _root = GameRoot.Ensure();
            _root.RegisterMainCamera(mainCamera);
            _root.CanSkip = false;
            Layers.ApplyCollisionMatrix();
            ImprintController.Reset();
            _db = FloorDB.FromConfig(_root.Config);
            Game.DB = _db;
            Game.Meta = MetaSave.Load();
            Game.Audio = new FloorAudio(_root.Config, _root.Audio, _db.Audio);
        }

        private void Start()
        {
            _root.Audio.StopEverything(0.6f);
            _root.Overlay.ResetAll();
            _root.Overlay.SetFade(Color.black, 1f);
            StartCoroutine(Begin());
        }

        private void OnDestroy()
        {
            if (Game.Ctrl == this) Game.Clear();
            Time.timeScale = _root != null ? _root.SpeedMultiplier * (_root.DebugFast ? 4f : 1f) : 1f;
        }

        private IEnumerator Begin()
        {
            RunsStarted++;
            var run = new RunState
            {
                Seed = SeedOverride != 0 ? SeedOverride : RunState.NewSeed(_db.Data.Int("seed", 0)),
                Revision = Game.Meta.revision
            };
            Game.Run = run;
            Game.Meta.runs++;
            Game.Meta.Save();
            Debug.Log($"[PALINODE] Floor I, revision {Game.Meta.RevisionLabel}, seed {run.Seed}");

            _layout = new FloorGenerator(_db.GenSettings()).Generate(run.Seed);
            _rng = new SeededRandom(SeededRandom.Mix(run.Seed, 0x51ED));

            Game.Fx = new GameObject("FX").AddComponent<FloorFx>();
            Game.Fx.Init();
            Game.Shots = new GameObject("Shots").AddComponent<ShotPool>();
            Game.Shots.Init();

            BuildRooms();
            PlaceTraces();
            ChooseGhostCar();
            SpawnElias();

            var cam = _db["camera"];
            _camera = mainCamera.gameObject.GetComponent<FloorCamera>() ?? mainCamera.gameObject.AddComponent<FloorCamera>();
            _camera.Init(mainCamera, cam.Num("ortho", 4.35f), cam.Num("follow", 7f), cam.Color("background", new Color(0.02f, 0.015f, 0.015f)));
            Game.Camera = _camera;

            var hudGo = new GameObject("HUD");
            _hud = hudGo.AddComponent<FloorHud>();
            _hud.Build(_root.Config, Game.Meta.edits);
            _map = hudGo.AddComponent<FloorMap>();
            _map.Build(_hud.Root, _root.Config, _layout);
            BuildPause();
            RefreshHud();

            Game.Audio.Music("floor", 3f);
            Game.Audio.Ambience("printhouse", _rng, 3f);
            Game.Audio.Ambience("rain", _rng, 4f);

            EnterRoom(_rooms[_layout.Start.Index], null, true);
            _started = true;
            yield return _root.Overlay.FadeTo(Color.black, 0f, 1.2f);
            RaiseMarker("F1_start");
        }

        // ------------------------------------------------------------------ building

        public static Vector2 RoomCenter(RoomNode n) =>
            new Vector2(n.Origin.x * 40f + (n.Size.x - 1) * 20f, n.Origin.y * 30f + (n.Size.y - 1) * 15f);

        private void BuildRooms()
        {
            _rooms = new RoomView[_layout.Rooms.Count];
            var parent = new GameObject("Rooms").transform;
            foreach (var node in _layout.Rooms)
            {
                var roomRng = new SeededRandom(SeededRandom.Mix(node.ContentSeed, 17));
                string bg = _db.BackgroundFor(node.Kind, roomRng);
                RoomTemplate template = null;
                if (node.Kind == RoomKind.Combat && _db.Normal.Count > 0) template = _db.Normal[Mathf.Clamp(node.TemplateIndex, 0, _db.Normal.Count - 1)];
                else if (node.Kind == RoomKind.Big && _db.Big.Count > 0) template = _db.Big[Mathf.Clamp(node.TemplateIndex, 0, _db.Big.Count - 1)];
                var go = new GameObject($"Room {node.Kind} #{node.Index} {node.Origin}");
                go.transform.SetParent(parent, false);
                var view = go.AddComponent<RoomView>();
                view.Build(node, _layout, RoomCenter(node), bg, template, _db);
                view.RoomCleared += OnRoomCleared;
                if (node.Kind == RoomKind.Big) view.NextWave = NextWave;
                _rooms[node.Index] = view;
                PopulateSpecial(view);
                if (template != null && template.WallClock) AddWallClock(view, _db["start"].Vec2("clock", new Vector2(1455f, 140f)), 1.1f);
                view.SetActive(false);
            }
        }

        private void SpawnElias()
        {
            var go = new GameObject("Elias");
            go.AddComponent<Rigidbody2D>();
            _elias = go.AddComponent<Elias>();
            _elias.Init(_db["player"]);
            Game.Player = _elias;
            _elias.Health.Changed += (c, m) => RefreshHud();
            var start = _rooms[_layout.Start.Index];
            _elias.Teleport(start.Frame.CellCenter(start.Frame.Cells.x / 2, start.Frame.Cells.y / 2 + 1));
            _elias.FaceTo(Gameplay.PlayerController.Facing.Down);

            var lg = new GameObject("EliasLight");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            _eliasLight = Cutscene.StageLight.CreateLight(lg, Light2D.LightType.Point);
            _eliasLight.color = new Color(0.95f, 0.9f, 0.82f);
            _eliasLight.intensity = 0f;
            _eliasLight.pointLightOuterRadius = _db["player"].Num("lightRadius", 4f);
            _eliasLight.pointLightInnerRadius = 1.2f;
            _eliasLight.falloffIntensity = 0.55f;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            ApplyTimeScale();
            if (!_started) return;
            _promptShownThisFrame = false;

            var input = _elias != null ? _elias.Input : null;
            if (_paused)
            {
                UpdatePause();
                return;
            }
            if (!_transition && !_dead && !_ending && PausePressed()) { SetPaused(true); return; }

            if (MapPressed()) _map.ShowFull(!_map.FullShown);

            if (globalLight != null) globalLight.intensity = Mathf.MoveTowards(globalLight.intensity, _lightTarget, Time.unscaledDeltaTime * 1.5f);

            if (!_transition && !_dead && !_ending && _elias != null && _elias.IsAlive && Game.Current != null)
            {
                foreach (var d in Game.Current.Doors)
                {
                    if (!d.Passed(_elias.Position, 0.22f)) continue;
                    StartCoroutine(Transition(d));
                    break;
                }
                UpdateTrapdoor();
                if (InCorridor) UpdateCorridor();
            }
            if (BotEnabled) UpdateBot();
            UpdateDebug();
            if (!_promptShown) _root.Overlay.HidePrompt();
            _promptShown = false;
        }

        private bool _promptShownThisFrame;

        private bool PausePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var gp = UnityEngine.InputSystem.Gamepad.current;
            return (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);
        }

        private bool MapPressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var gp = UnityEngine.InputSystem.Gamepad.current;
            return (kb != null && kb.tabKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame);
        }

        private void ApplyTimeScale()
        {
            if (_root == null) return;
            float baseScale = _root.SpeedMultiplier * (_root.DebugFast ? 4f : 1f);
            float s = _paused ? 0f : baseScale;
            if (Time.realtimeSinceStartup < _hitStopUntil) s *= 0.05f;
            Time.timeScale = s;
        }

        public void HitStop(float seconds) => _hitStopUntil = Mathf.Max(_hitStopUntil, Time.realtimeSinceStartup + seconds);

        public void ScreenFlash(Color c, float duration) => _hud?.Flash(c, duration);

        public void MicroSleep(float duration) => _hud?.Blink(duration);

        public void ShowBossBar(bool show, float fraction) => _hud?.BossBar(show, fraction, Game.T("F1_BOSS_NAME"));

        public void ShowPrompt(Pickup p)
        {
            string text;
            if (p.Price > 0) text = string.Format(Game.T("F1_PROMPT_BUY"), p.Price);
            else text = Game.T("F1_PROMPT_TAKE");
            _root.Overlay.ShowPrompt(text, (Vector3)p.Position + Vector3.up * 1.6f, mainCamera);
            _promptShown = true;
        }

        private void ShowPromptAt(string key, Vector2 world)
        {
            _root.Overlay.ShowPrompt(Game.T(key), (Vector3)world + Vector3.up * 1.2f, mainCamera);
            _promptShown = true;
        }

        // ------------------------------------------------------------------ rooms

        private IEnumerator Transition(DoorView door)
        {
            _transition = true;
            var from = Game.Current;
            var to = _rooms[door.Link.Neighbor];
            var entry = to.DoorFor(from.Node.Index);
            _elias.SetControlLocked(true);
            Game.Shots.Clear();
            to.SetActive(true);
            var camSpec = _db["camera"];
            Vector2 fromFocus = _camera.FocusFor(from.Frame.ImageRect, _elias.Position);
            Vector2 target = entry != null ? to.EntryPoint(entry) : to.Frame.Center;
            _elias.Teleport(target);
            _camera.SetRoom(from.Frame.ImageRect, null, false,
                to.Node.Kind == RoomKind.Boss ? camSpec.Num("bossOrtho", 6.2f) : camSpec.Num("ortho", 4.75f));
            _camera.SetBias(to.Node.Kind == RoomKind.Boss ? to.Frame.CellCenter(to.Frame.Cells.x / 2, 0) : (Vector2?)null,
                camSpec.Num("bossBias", 0.45f));
            Vector2 toFocus = _camera.FocusFor(to.Frame.ImageRect, target);
            float dur = _db["camera"].Num("slide", 0.32f);
            Game.Audio.Play("page_turn", 0.35f);
            StartCoroutine(_hud.PageTurn(dur * 1.3f, door.Side == Direction.Right || door.Side == Direction.Up));
            yield return _camera.Slide(fromFocus, toFocus, dur);
            from.Leave();
            from.SetActive(false);
            EnterRoom(to, entry, false);
            _elias.SetControlLocked(false);
            _transition = false;
        }

        private void EnterRoom(RoomView room, DoorView via, bool snap)
        {
            var prev = Game.Current;
            Game.Current = room;
            room.SetActive(true);
            Game.Run.Visit(room.Node.Index);
            _elias.OnRoomEntered();
            var camSpec = _db["camera"];
            _camera.SetRoom(room.Frame.ImageRect, _elias.transform, snap,
                room.Node.Kind == RoomKind.Boss ? camSpec.Num("bossOrtho", 6.2f) : camSpec.Num("ortho", 4.75f));

            // Light: dark rooms show a circle around Elias and the lamps.
            _lightTarget = room.IsDark ? 0.05f : 1f;
            if (snap && globalLight != null) globalLight.intensity = _lightTarget;
            _eliasLight.intensity = room.IsDark ? 1.25f : 0f;

            if (room.Node.HasEnemies && !room.Spawned && !room.Cleared) SpawnRoom(room);
            room.Enter();
            _camera.SetBias(room.Node.Kind == RoomKind.Boss ? room.Frame.CellCenter(room.Frame.Cells.x / 2, 0) : (Vector2?)null,
                camSpec.Num("bossBias", 0.45f));
            if (snap) _camera.SetRoom(room.Frame.ImageRect, _elias.transform, true);

            // Music by room.
            if (room.Node.Kind == RoomKind.Memory)
            {
                Game.Audio.Music("lullaby", 1.5f);
                if (!Game.Run.MemoryHealed)
                {
                    Game.Run.MemoryHealed = true;
                    _elias.Health.Heal(_db["memory"].Int("healHalves", 2));
                    Game.Audio.Play("pickup_ink");
                    Game.Fx.Burst(_elias.Position + Vector2.up, new Color(0.95f, 0.85f, 0.6f), 16, 2f, 0.12f, 0.8f);
                }
            }
            else if (room.Node.Kind == RoomKind.Boss && !_bossCleared) Game.Audio.Music("boss", 1f);
            else if (prev != null && (prev.Node.Kind == RoomKind.Memory || prev.Node.Kind == RoomKind.Boss)) Game.Audio.Music("floor", 2f);

            if (room.Node.Index == _ghostRoom && !_ghostDone) StartCoroutine(GhostCar(room));
            RefreshMap();
            RoomMarker(room);
        }

        private void RoomMarker(RoomView room)
        {
            switch (room.Node.Kind)
            {
                case RoomKind.Memory: RaiseMarker("F1_memory", 1.2f); break;
                case RoomKind.Corrector: RaiseMarker("F1_corrector", 0.6f); break;
                case RoomKind.Shop: RaiseMarker("F1_shop", 0.6f); break;
                case RoomKind.Secret: RaiseMarker("F1_secret", 0.6f); break;
                case RoomKind.Boss: RaiseMarker("F1_boss_p1", 2.5f); break;
                case RoomKind.Big: RaiseMarker("F1_big", 1.5f); break;
            }
            if (room.IsDark) RaiseMarker("F1_dark", 2.2f);
            foreach (var e in room.Enemies) if (e != null) RaiseMarker("F1_enemy_" + e.TypeId, 1.4f);
        }

        public void RaiseMarker(string id, float delay = 0f)
        {
            if (Marker == null || !_markersSent.Add(id)) return;
            if (delay <= 0f) Marker.Invoke(id);
            else StartCoroutine(DelayedMarker(id, delay));
        }

        private IEnumerator DelayedMarker(string id, float delay)
        {
            yield return new WaitForSeconds(delay);
            Marker?.Invoke(id);
        }

        private void SpawnRoom(RoomView room)
        {
            room.Spawned = true;
            var node = room.Node;
            if (node.Kind == RoomKind.Boss)
            {
                var go = new GameObject("Tirazh");
                go.transform.SetParent(room.ActorRoot, false);
                _boss = go.AddComponent<TirazhBoss>();
                _boss.Setup("boss", _db.Data["boss"], room, room.Frame.Center, false, _db.Data["boss"].Num("hp", 400f), 6f);
                return;
            }
            var rng = new SeededRandom(node.ContentSeed);
            int budget = _db.Budget(node.Distance);
            var points = room.Template.SpawnPoints;
            if (node.Kind == RoomKind.Big)
            {
                // Wave one uses the left half of the points, wave two the right.
                var half = new List<Vector2Int>();
                foreach (var p in points) if (p.x < room.Frame.Cells.x / 2) half.Add(p);
                SpawnPlanned(room, SpawnPlanner.Plan(_db, node, half, rng, budget));
                _waves[node.Index] = _db["difficulty"].Int("bigWaves", 2) - 1;
            }
            else SpawnPlanned(room, SpawnPlanner.Plan(_db, node, points, rng, budget));

            if (node.Index == _witnessRoom && !string.IsNullOrEmpty(_witnessType))
            {
                _witnessRoom = -1;
                var p = room.RandomFreePoint(_elias.Position, 4f);
                if (_witnessType == "letters") EnemyFactory.CreateLetters(room, p, 3, true);
                else if (Array.IndexOf(FloorDB.EnemyIds, _witnessType) >= 0) EnemyFactory.Create(_witnessType, room, p, true);
            }
        }

        private readonly Dictionary<int, int> _waves = new Dictionary<int, int>();

        private bool NextWave(RoomView room)
        {
            if (!_waves.TryGetValue(room.Node.Index, out int left) || left <= 0) return false;
            _waves[room.Node.Index] = left - 1;
            StartCoroutine(WaveRoutine(room));
            return true;
        }

        private IEnumerator WaveRoutine(RoomView room)
        {
            // Keep the room "occupied" while the next wave is on its way.
            yield return new WaitForSeconds(_db["difficulty"].Num("waveDelay", 1.2f));
            var rng = new SeededRandom(SeededRandom.Mix(room.Node.ContentSeed, 2));
            var half = new List<Vector2Int>();
            foreach (var p in room.Template.SpawnPoints) if (p.x >= room.Frame.Cells.x / 2) half.Add(p);
            SpawnPlanned(room, SpawnPlanner.Plan(_db, room.Node, half, rng, _db.Budget(room.Node.Distance)));
            if (room.Enemies.Count == 0) room.MarkCleared(true);
        }

        private void SpawnPlanned(RoomView room, List<SpawnEntry> plan)
        {
            foreach (var e in plan)
            {
                var pos = room.Frame.CellCenter(e.Cell);
                if (e.Type == "letters") EnemyFactory.CreateLetters(room, pos, e.Group, e.Elite);
                else EnemyFactory.Create(e.Type, room, pos, e.Elite);
            }
        }

        public void OnEnemyKilled(EnemyBase e) { }

        private void OnRoomCleared(RoomView room)
        {
            Game.Audio.Play("page_turn");
            var run = Game.Run;
            if (run.HasPencil)
            {
                int full = _db["redPencil"].Int("charge", 4);
                run.PencilCharge = Mathf.Min(full, run.PencilCharge + 1);
            }
            if (room.Node.Kind == RoomKind.Combat || room.Node.Kind == RoomKind.Big)
            {
                var rw = _db["rewards"];
                if (_rng.Chance(rw.Num("chance", 0.35f)))
                {
                    var kinds = new List<string>();
                    var weights = new List<float>();
                    foreach (var t in rw["table"].Items())
                    {
                        kinds.Add(t.Str("pickup"));
                        weights.Add(t.Num("weight", 1f));
                    }
                    int i = _rng.Weighted(weights);
                    if (i >= 0) SpawnPickup(room, kinds[i], room.Frame.Center);
                }
            }
            RefreshHud();
            RefreshMap();
        }

        // ------------------------------------------------------------------ pickups & items

        public Pickup SpawnPickup(RoomView room, string kind, Vector2 pos)
        {
            var k = kind == "drop" ? PickupKind.Drop : kind == "inkwell" ? PickupKind.Inkwell : kind == "page" ? PickupKind.Page : PickupKind.Sheet;
            var go = new GameObject("Pickup " + kind);
            var p = go.AddComponent<Pickup>();
            p.Init(room, k, room.Frame.ClampToFloor(pos, 0.4f));
            return p;
        }

        public Pickup SpawnItem(RoomView room, string itemId, Vector2 pos, bool pedestal, int price = 0, int group = 0, float height = -1f)
        {
            var go = new GameObject("Item " + itemId);
            var p = go.AddComponent<Pickup>();
            p.Init(room, itemId == "pencil" ? PickupKind.Pencil : PickupKind.Item, pos, itemId == "pencil" ? null : itemId, price, pedestal, group, 0, height);
            return p;
        }

        public void TryTake(Pickup p)
        {
            if (p == null || p.Taken || _elias == null) return;
            var run = Game.Run;
            switch (p.Kind)
            {
                case PickupKind.Drop:
                case PickupKind.Inkwell:
                    if (_elias.Health.IsFull) return;
                    _elias.Health.Heal(p.Kind == PickupKind.Drop ? _db["pickups"]["drop"].Int("heal", 1) : _db["pickups"]["inkwell"].Int("heal", 2));
                    Game.Audio.Play("pickup_ink");
                    break;
                case PickupKind.Sheet:
                    run.Sheets++;
                    Game.Audio.Play("pickup_ink", 0.8f);
                    break;
                case PickupKind.Page:
                    Game.Meta.AddPage(p.PageIndex);
                    run.PagesThisRun.Add(p.PageIndex);
                    Game.Audio.Play("pickup_page");
                    _hud.Banner(string.Format(Game.T("F1_PAGE_TITLE"), p.PageIndex, _db["pages"].Int("total", 7)), Game.T("F1_PAGE_" + p.PageIndex), 6f);
                    break;
                case PickupKind.Item:
                case PickupKind.Pencil:
                    if (p.Price > 0)
                    {
                        if (run.Sheets < p.Price)
                        {
                            _root.Overlay.ShowPrompt(Game.T("F1_PROMPT_POOR"), (Vector3)p.Position + Vector3.up * 1.6f, mainCamera);
                            _promptShown = true;
                            return;
                        }
                        run.Sheets -= p.Price;
                    }
                    GiveItem(p.Kind == PickupKind.Pencil ? "pencil" : p.ItemId, FloorSprites.Get(Pickup.SpriteFor(p.Kind, p.ItemId)));
                    if (p.ChoiceGroup != 0)
                    {
                        foreach (var other in new List<Pickup>(p.Room.Pickups))
                        {
                            if (other == p || other.ChoiceGroup != p.ChoiceGroup) continue;
                            if (other.Kind == PickupKind.Pencil) run.LeftPencil = true;
                            other.Remove(true);
                        }
                    }
                    break;
            }
            p.Remove(false);
            RefreshHud();
        }

        public void GiveItem(string id, Sprite icon)
        {
            var run = Game.Run;
            if (id == "pencil")
            {
                run.HasPencil = true;
                run.TookPencil = true;
                run.PencilCharge = _db["redPencil"].Int("charge", 4);
                _hud.Banner(Game.T("F1_ITEM_pencil"), Game.T("F1_ITEM_pencil_DESC"), 4.5f);
            }
            else
            {
                if (!run.Items.Contains(id)) run.Items.Add(id);
                _elias.RecomputeStats();
                _hud.Banner(Game.T("F1_ITEM_" + id), Game.T("F1_ITEM_" + id + "_DESC"), 4f);
            }
            _elias.ShowPickup(icon);
            Game.Audio.Play("pickup_item");
            RefreshHud();
        }

        // ------------------------------------------------------------------ red pencil

        private bool PencilReady => Game.Run.HasPencil && Game.Run.PencilCharge >= _db["redPencil"].Int("charge", 4);

        public void UsePencil()
        {
            if (!PencilReady || _transition || _dead || Game.Current == null) return;
            var room = Game.Current;
            bool any = false;
            foreach (var e in room.Enemies) if (e != null && e.Alive) { any = true; break; }
            if (!any) return;
            ConsumePencil();
            foreach (var e in new List<EnemyBase>(room.Enemies))
            {
                if (e == null || !e.Alive) continue;
                if (e is TirazhBoss boss)
                    boss.TakeHit(new HitInfo { Damage = boss.MaxHp * _db["redPencil"].Num("bossDamage", 0.2f), Red = true, Point = boss.Feet + Vector2.up });
                else
                {
                    RedMark(room, e.Feet);
                    e.Kill(true);
                }
            }
            Game.Shots.ClearFaction(Faction.Enemy);
            ScreenFlash(new Color(0.75f, 0.05f, 0.06f, 0.25f), 0.35f);
        }

        /// <summary>The pencil strikes out Elias's own death. True if it did.</summary>
        public bool TryPencilSave()
        {
            if (!PencilReady || _dead) return false;
            ConsumePencil();
            Game.Fx.Strike(null, _elias.Position + Vector2.up * 1f, 2.2f, FloorFx.RedInk, 0.12f, 0.8f);
            RedMark(Game.Current, _elias.Position);
            ScreenFlash(new Color(0.75f, 0.05f, 0.06f, 0.35f), 0.5f);
            HitStop(0.12f);
            return true;
        }

        private void ConsumePencil()
        {
            Game.Run.PencilCharge = 0;
            Game.Meta.RegisterEdit();
            _hud.SetEdits(Game.Meta.edits);
            Game.Audio.Play("redpencil");
            RefreshHud();
        }

        /// <summary>Red traces of an edit stay on the floor of the room.</summary>
        private void RedMark(RoomView room, Vector2 at)
        {
            if (room == null) return;
            var sr = FloorSprites.Quad(room.DecalRoot, "RedEdit", FloorFx.RedInk, new Vector2(1.2f, 0.06f), FloorSprites.FloorDecalOrder + 900, FloorSprites.Solid);
            sr.transform.position = at;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-25f, 25f));
            sr.color = new Color(FloorFx.RedInk.r, FloorFx.RedInk.g, FloorFx.RedInk.b, 0.75f);
        }

        // ------------------------------------------------------------------ secret

        public void OpenSecret(DoorView door)
        {
            if (door.IsOpen) return;
            door.SetOpen(true, false);
            var other = _rooms[door.Link.Neighbor];
            foreach (var d in other.Doors)
                if (d.Link.Neighbor == door.Room.Node.Index && d.Kind == DoorKind.Secret) d.SetOpen(true, false);
            Game.Audio.Play("secret_open");
            Game.Fx.Burst(door.Edge, new Color(0.35f, 0.3f, 0.26f), 30, 4f, 0.14f, 0.7f);
            Game.Camera?.Shake(0.15f, 0.2f);
            RefreshMap();
        }

        // ------------------------------------------------------------------ HUD

        private void RefreshHud()
        {
            if (_hud == null || _elias == null) return;
            _hud.SetHealth(_elias.Health.Current, _elias.Health.MaxHalves);
            _hud.SetSheets(Game.Run.Sheets);
            _hud.SetPencil(Game.Run.HasPencil, Game.Run.PencilCharge, _db["redPencil"].Int("charge", 4));
        }

        private void RefreshMap() => _map?.Refresh(Game.Current != null ? Game.Current.Node : null, _rooms, _revealMap, _bossCleared);
    }
}
