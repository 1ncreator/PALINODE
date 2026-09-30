using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.Cutscene;
using Palinode.Effects;
using Palinode.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Gameplay
{
    /// <summary>
    /// Builds an interactive top-down level from prologue.json ("levels.&lt;id&gt;"): painted background,
    /// invisible colliders traced over the art, props, lights, weather, triggers and interactables.
    /// All coordinates in the data are background-image pixels (top-left origin).
    /// </summary>
    public sealed class LevelController : MonoBehaviour, ILevel
    {
        /// <summary>Set by play-mode tests: the player walks chapter "autopilot" paths and auto-interacts.</summary>
        public static bool AutoPilotEnabled { get; set; }

        [SerializeField] private string levelId;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Light2D globalLight;

        private GameConfig _config;
        private JNode _spec;
        private Vector2 _size;         // background size in pixels
        private float _ppu = 100f;
        private PlayerController _player;
        private CameraFollow2D _follow;
        private readonly Dictionary<string, GameObject> _actors = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, Light2D> _lights = new Dictionary<string, Light2D>();
        private readonly Dictionary<string, float> _lightBase = new Dictionary<string, float>();
        private readonly Dictionary<string, Rect> _triggers = new Dictionary<string, Rect>();
        private readonly Dictionary<string, JNode> _interactables = new Dictionary<string, JNode>();
        private readonly List<Vector2[]> _debugPaths = new List<Vector2[]>();
        private GameObject _debugRoot;
        private Coroutine _traffic;
        private bool _trafficOn = true;
        private readonly Dictionary<string, Vehicle> _vehicles = new Dictionary<string, Vehicle>();

        public PlayerController Player => _player;
        public Transform StageAnchor => worldCamera != null ? worldCamera.transform : transform;
        public Camera Camera => worldCamera;
        public string LevelId => levelId;

        private void Awake()
        {
            var root = GameRoot.Ensure();
            Build(root.Config, root.Data["levels"]);
        }

        public void Configure(string id, Camera cam, Light2D global)
        {
            levelId = id;
            worldCamera = cam;
            globalLight = global;
        }

        public Vector2 PxToWorld(Vector2 px) => new Vector2((px.x - _size.x * 0.5f) / _ppu, (_size.y * 0.5f - px.y) / _ppu);

        public Vector2 PxToWorld(JNode n) => PxToWorld(new Vector2(n[0].AsFloat(), n[1].AsFloat()));

        public void Build(GameConfig config, JNode levels)
        {
            _config = config;
            _spec = levels[levelId];
            if (_spec.IsNull)
            {
                Debug.LogError($"[PALINODE] Level '{levelId}' missing in prologue.json.");
                return;
            }

            // Background
            var bgSprite = config.Sprite(_spec.Str("background"));
            var bg = new GameObject("Background");
            bg.transform.SetParent(transform, false);
            var bgr = bg.AddComponent<SpriteRenderer>();
            bgr.sprite = bgSprite;
            bgr.sortingOrder = -30000;
            bgr.sharedMaterial = config.Material("SpriteLit");
            _size = bgSprite != null ? bgSprite.rect.size : new Vector2(1920f, 1080f);
            _ppu = bgSprite != null ? bgSprite.pixelsPerUnit : 100f;

            if (globalLight != null)
            {
                var gl = _spec["globalLight"];
                globalLight.intensity = gl.Num("intensity", 1f);
                globalLight.color = gl.Color("color", Color.white);
            }

            BuildColliders();
            BuildProps();
            BuildLights();
            BuildPlayer();
            BuildFx();

            foreach (var t in _spec["triggers"].Items())
            {
                Vector4 r = t.Vec4("rect", Vector4.zero);
                Vector2 a = PxToWorld(new Vector2(r.x, r.y));
                Vector2 b = PxToWorld(new Vector2(r.x + r.z, r.y + r.w));
                _triggers[t.Str("id")] = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                _debugPaths.Add(new[] { a, new Vector2(b.x, a.y), b, new Vector2(a.x, b.y), a });
            }
            foreach (var it in _spec["interactables"].Items())
            {
                _interactables[it.Str("id")] = it;
                Vector2 c = PxToWorld(it["pos"]);
                float rad = it.Num("radius", 80f) / _ppu;
                var circle = new Vector2[17];
                for (int i = 0; i <= 16; i++) circle[i] = c + new Vector2(Mathf.Cos(i / 16f * Mathf.PI * 2f), Mathf.Sin(i / 16f * Mathf.PI * 2f)) * rad;
                _debugPaths.Add(circle);
            }

            if (_spec.Has("traffic")) _traffic = StartCoroutine(TrafficLoop(_spec["traffic"]));
            GameRoot.DebugCollidersChanged += OnDebugColliders;
            OnDebugColliders(GameRoot.DebugCollidersVisible);
        }

        private void OnDestroy()
        {
            GameRoot.DebugCollidersChanged -= OnDebugColliders;
        }

        // ------------------------------------------------------------------ construction

        private void BuildColliders()
        {
            var root = new GameObject("Colliders");
            root.transform.SetParent(transform, false);
            if (_spec.Has("bounds"))
            {
                var pts = ToWorld(_spec["bounds"]);
                var list = new List<Vector2>(pts) { pts[0] };
                var edge = root.AddComponent<EdgeCollider2D>();
                edge.points = list.ToArray();
                edge.edgeRadius = 0.02f;
                _debugPaths.Add(list.ToArray());
            }
            foreach (var wall in _spec["walls"].Items())
            {
                var pts = ToWorld(wall);
                var go = new GameObject("Wall");
                go.transform.SetParent(root.transform, false);
                var poly = go.AddComponent<PolygonCollider2D>();
                poly.points = pts;
                var closed = new List<Vector2>(pts) { pts[0] };
                _debugPaths.Add(closed.ToArray());
            }
        }

        private Vector2[] ToWorld(JNode poly)
        {
            var list = new List<Vector2>();
            foreach (var p in poly.Items()) list.Add(PxToWorld(p));
            return list.ToArray();
        }

        private void BuildProps()
        {
            foreach (var p in _spec["props"].Items()) BuildProp(p, transform, 0);
            BuildOccluders();
        }

        /// <summary>
        /// Objects cut out of the map (art_processing.json "cutouts") drawn over their painted originals and depth-sorted
        /// by their base Y against the player, plus a collider around their footprint.
        /// </summary>
        private void BuildOccluders()
        {
            var root = new GameObject("Occluders");
            root.transform.SetParent(transform, false);
            foreach (var o in _spec["occluders"].Items())
            {
                string sprite = o.Str("sprite");
                var sp = _config.Sprite(sprite);
                Vector2? basePx = _config.ArtPoint(sprite, "base");
                if (sp == null || !basePx.HasValue)
                {
                    Debug.LogWarning($"[PALINODE] Occluder '{sprite}' has no sprite/base (run Build Everything).");
                    continue;
                }
                var go = new GameObject("Occ_" + sprite);
                go.transform.SetParent(root.transform, false);
                Vector2 basePos = PxToWorld(basePx.Value);
                go.transform.position = basePos;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sharedMaterial = _config.Material("SpriteLit");
                sr.sortingOrder = PlayerController.SortingOrderFor(basePos.y + o.Num("sortBias", 0f) / _ppu);
                _actors[o.Str("id", sprite)] = go;

                // Footprint collider: explicit polygon (map px) or a box [w, h] centred on the base.
                if (o["footprint"].IsArray && o["footprint"].Count > 0 && o["footprint"][0].IsArray)
                {
                    var col = go.AddComponent<PolygonCollider2D>();
                    var pts = new List<Vector2>();
                    foreach (var p in o["footprint"].Items()) pts.Add(PxToWorld(p) - basePos);
                    col.points = pts.ToArray();
                    var closed = new List<Vector2>();
                    foreach (var p in pts) closed.Add(p + basePos);
                    closed.Add(closed[0]);
                    _debugPaths.Add(closed.ToArray());
                }
                else if (o.Has("footprint"))
                {
                    Vector2 fp = o.Vec2("footprint", new Vector2(20f, 10f)) / _ppu;
                    var bc = go.AddComponent<BoxCollider2D>();
                    bc.size = fp;
                    bc.offset = new Vector2(0f, fp.y * 0.25f);
                    Vector2 c = basePos + bc.offset, hsz = fp * 0.5f;
                    _debugPaths.Add(new[] { c - hsz, c + new Vector2(hsz.x, -hsz.y), c + hsz, c + new Vector2(-hsz.x, hsz.y), c - hsz });
                }
            }
        }

        private GameObject BuildProp(JNode p, Transform parent, int parentOrder)
        {
            string id = p.Str("id", "prop");
            var go = new GameObject("Prop_" + id);
            go.transform.SetParent(parent, false);
            bool child = parent != transform;
            Vector2 pos;
            if (child && p.Has("px"))
            {
                // Pixel of the parent's sprite (top-left origin) → parent local space.
                var psr = parent.GetComponent<SpriteRenderer>();
                var ps = psr != null ? psr.sprite : null;
                Vector2 px = p.Vec2("px", Vector2.zero);
                pos = ps != null
                    ? new Vector2((px.x - ps.pivot.x) / ps.pixelsPerUnit, (ps.rect.height - px.y - ps.pivot.y) / ps.pixelsPerUnit)
                    : px / _ppu;
            }
            else pos = child ? p.Vec2("local", Vector2.zero) / _ppu : PxToWorld(p["pos"]);
            go.transform.localPosition = pos;
            float scale = p.Num("scale", 1f);
            Vector2 scale2 = p.Vec2("scale2", new Vector2(scale, scale));
            go.transform.localScale = new Vector3(scale2.x * (p.Bool("flipX") ? -1f : 1f), scale2.y, 1f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, p.Num("rot", 0f));
            string type = p.Str("type", "sprite");
            // Children draw just above their parent; root props are depth-sorted by their base (pivot) Y like the player.
            int order = p.Has("order") ? p.Int("order")
                : child ? parentOrder + 1 + parent.childCount
                : PlayerController.SortingOrderFor(go.transform.position.y + p.Num("sortBias", 0f) / _ppu);

            if (type == "sprite")
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _config.Sprite(p.Str("sprite"));
                sr.sortingOrder = order;
                sr.sharedMaterial = _config.Material(p.Bool("lit", true) ? "SpriteLit" : "SpriteUnlit");
                sr.color = p.Color("tint", Color.white);
            }
            else if (type == "text")
            {
                var st = go.AddComponent<StageText>();
                st.Build(null, p, _config, order);
                st.SetReveal(p.Num("reveal", 1f));
            }
            else if (type == "glow")
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ProceduralSprites.SoftDot(128, 0f);
                sr.sharedMaterial = _config.Material("GlowAdd");
                sr.color = p.Color("color", new Color(1f, 1f, 1f, 0.3f));
                sr.sortingOrder = order;
                Vector2 size = p.Vec2("size", new Vector2(200f, 200f)) / _ppu;
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
            }

            if (p.Has("footprint"))
            {
                Vector4 f = p.Vec4("footprint", Vector4.zero); // x,y offset from pivot in px (scaled), w,h
                var box = new GameObject("Footprint");
                box.transform.SetParent(transform, false);
                box.transform.position = (Vector2)go.transform.position + new Vector2(f.x, -f.y) / _ppu;
                var bc = box.AddComponent<BoxCollider2D>();
                bc.size = new Vector2(f.z, f.w) / _ppu;
                Vector2 c = box.transform.position;
                Vector2 h = bc.size * 0.5f;
                _debugPaths.Add(new[] { c - h, c + new Vector2(h.x, -h.y), c + h, c + new Vector2(-h.x, h.y), c - h });
            }

            if (p.Bool("hidden")) go.SetActive(false);
            _actors[id] = go;
            foreach (var c in p["children"].Items()) BuildProp(c, go.transform, order);
            return go;
        }

        private void BuildLights()
        {
            foreach (var l in _spec["lights"].Items()) BuildLight(l, transform);
        }

        private Light2D BuildLight(JNode l, Transform parent)
        {
            var go = new GameObject("Light_" + l.Str("id", "l"));
            go.transform.SetParent(parent, false);
            go.transform.position = PxToWorld(l["pos"]);
            var light = StageLight.CreateLight(go, Light2D.LightType.Point);
            light.color = l.Color("color", new Color(1f, 0.8f, 0.55f));
            float intensity = l.Num("intensity", 1f);
            light.intensity = intensity;
            float r = l.Num("radius", 200f) / _ppu;
            light.pointLightOuterRadius = r;
            light.pointLightInnerRadius = r * l.Num("inner", 0.1f);
            light.falloffIntensity = l.Num("falloff", 0.6f);
            if (l.Has("angle"))
            {
                light.pointLightOuterAngle = l.Num("angle", 360f);
                light.pointLightInnerAngle = l.Num("angle", 360f) * 0.55f;
                go.transform.rotation = Quaternion.Euler(0f, 0f, l.Num("dir", -90f) - 90f);
            }
            string id = l.Str("id");
            if (id != null)
            {
                _lights[id] = light;
                _lightBase[id] = intensity;
            }
            if (l.Num("flicker", 0f) > 0f) go.AddComponent<IdleFlicker>().Init(light, l.Num("flicker", 0f));
            if (l.Has("glow"))
            {
                var g = l["glow"];
                var glow = new GameObject("Glow");
                glow.transform.SetParent(go.transform, false);
                glow.transform.localPosition = g.Vec2("offset", Vector2.zero) / _ppu;
                Vector2 size = g.Vec2("size", new Vector2(120f, 120f)) / _ppu;
                glow.transform.localScale = new Vector3(size.x, size.y, 1f);
                var sr = glow.AddComponent<SpriteRenderer>();
                sr.sprite = ProceduralSprites.SoftDot(128, 0f);
                sr.sharedMaterial = _config.Material("GlowAdd");
                sr.color = g.Color("color", new Color(1f, 0.8f, 0.5f, 0.35f));
                sr.sortingOrder = 12000;
            }
            return light;
        }

        private void BuildPlayer()
        {
            var ps = _spec["player"];
            var go = new GameObject("Elias");
            go.transform.SetParent(transform, false);
            go.transform.position = PxToWorld(ps["spawn"]);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            var col = go.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Horizontal;
            col.size = ps.Vec2("feet", new Vector2(34f, 14f)) / _ppu;
            col.offset = Vector2.zero;

            var steps = go.AddComponent<FootstepPlayer>();
            steps.Init(_config.Clip(ps.Str("footsteps", "SFX_footsteps_wet")), ps.Num("footstepVolume", 0.55f));

            _player = go.AddComponent<PlayerController>();
            var walk = ps.Has("walk") ? ps["walk"] : GameRoot.Ensure().Data["playerWalk"];
            _player.Init(WalkSetFrom(walk["down"]), WalkSetFrom(walk["up"]), WalkSetFrom(walk["right"]),
                ps.Num("scale", 0.14f), ps.Num("speed", 110f) / _ppu, ps.Num("cyclePx", 120f) / _ppu, walk.Num("blend", 0.55f),
                steps, ps.Bool("reflection"), 5000);
            _player.SetMaterial(_config.Material("SpriteLit"));
            _player.Face(ParseFacing(ps.Str("facing", "right")));

            if (ps.Has("flashlight"))
            {
                var fl = ps["flashlight"];
                var spotGo = new GameObject("Flashlight");
                spotGo.transform.SetParent(go.transform, false);
                var spot = StageLight.CreateLight(spotGo, Light2D.LightType.Point);
                spot.color = fl.Color("color", new Color(0.9f, 0.95f, 1f));
                spot.intensity = fl.Num("intensity", 1.4f);
                spot.pointLightOuterRadius = fl.Num("radius", 420f) / _ppu;
                spot.pointLightInnerRadius = 0.1f;
                spot.pointLightOuterAngle = fl.Num("angle", 56f);
                spot.pointLightInnerAngle = fl.Num("angle", 56f) * 0.45f;
                spot.falloffIntensity = 0.45f;
                var auraGo = new GameObject("Aura");
                auraGo.transform.SetParent(go.transform, false);
                auraGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                var aura = StageLight.CreateLight(auraGo, Light2D.LightType.Point);
                aura.color = spot.color;
                aura.intensity = fl.Num("aura", 0.35f);
                aura.pointLightOuterRadius = 1.1f;
                aura.pointLightInnerRadius = 0.05f;
                aura.falloffIntensity = 0.8f;
                _player.AddFlashlight(spot, aura);
            }

            if (worldCamera != null)
            {
                _follow = worldCamera.GetComponent<CameraFollow2D>() ?? worldCamera.gameObject.AddComponent<CameraFollow2D>();
                Vector2 half = _size / _ppu * 0.5f;
                _follow.Init(go.transform, new Rect(-half.x, -half.y, half.x * 2f, half.y * 2f), _spec.Num("cameraSize", 3.4f));
                _follow.Offset = new Vector2(0f, _spec.Num("cameraOffsetY", 60f) / _ppu);
                _follow.Snap();
            }
        }

        private void BuildFx()
        {
            foreach (var fx in _spec["fx"].Items())
            {
                string type = fx.Str("type");
                var go = new GameObject("FX_" + type);
                bool cameraSpace = fx.Bool("camera", true) && worldCamera != null;
                go.transform.SetParent(cameraSpace ? worldCamera.transform : transform, false);
                go.transform.localPosition = cameraSpace ? new Vector3(0f, 0f, 10f) : (Vector3)PxToWorld(fx["pos"]);
                switch (type)
                {
                    case "rainField":
                    {
                        go.transform.SetParent(transform, false);
                        go.AddComponent<RainField>().Build(worldCamera, fx, _config, fx.Int("order", 14000));
                        break;
                    }
                    case "rain":
                    {
                        var pfx = go.AddComponent<ParticleFX>();
                        var layer = go.AddComponent<StageLayer>();
                        layer.Init("rain", 0, null, fx.Int("order", 14000));
                        pfx.BuildRain(layer, fx, _config);
                        break;
                    }
                    case "fog":
                    {
                        var layer = go.AddComponent<StageLayer>();
                        layer.Init("fog", 0, null, fx.Int("order", 13000));
                        go.AddComponent<ShaderQuad>().Build(layer, fx, _config);
                        break;
                    }
                }
            }
        }

        /// <summary>prologue.json "playerWalk.&lt;dir&gt;": { "prefix", "count", "idle", "plants" }.</summary>
        private PlayerController.WalkSet WalkSetFrom(JNode w)
        {
            string prefix = w.Str("prefix", "G_walk_right_");
            int count = w.Int("count", 8);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++) frames[i] = _config.Sprite(prefix + (i + 1).ToString("00"));
            var plants = new List<int>();
            foreach (var p in w["plants"].Items()) plants.Add(p.AsInt());
            return new PlayerController.WalkSet
            {
                Frames = frames,
                Idle = w.Int("idle", 0),
                Plants = plants.Count > 0 ? plants.ToArray() : new[] { 0, count / 2 }
            };
        }

        private static PlayerController.Facing ParseFacing(string s)
        {
            switch (s)
            {
                case "up": return PlayerController.Facing.Up;
                case "down": return PlayerController.Facing.Down;
                case "left": return PlayerController.Facing.Left;
                default: return PlayerController.Facing.Right;
            }
        }

        // ------------------------------------------------------------------ traffic

        /// <summary>Ambient cars ("traffic"): a random car on a random lane every "interval" seconds.</summary>
        private IEnumerator TrafficLoop(JNode tr)
        {
            yield return CutsceneContext.Wait(tr.Num("firstDelay", 4f));
            var lanes = new List<string>();
            foreach (var kv in tr["lanes"].Pairs()) lanes.Add(kv.Key);
            if (lanes.Count == 0) yield break;
            string last = null;
            while (true)
            {
                if (_trafficOn)
                {
                    string lane = lanes[Random.Range(0, lanes.Count)];
                    if (lane == last && lanes.Count > 1 && Random.value < 0.6f) lane = lanes[(lanes.IndexOf(lane) + 1) % lanes.Count];
                    if (SpawnVehicle(tr, lane, null, null, false, default) != null) last = lane;
                }
                Vector2 iv = tr.Vec2("interval", new Vector2(8f, 15f));
                yield return CutsceneContext.Wait(Random.Range(iv.x, iv.y));
            }
        }

        /// <param name="step">optional: "speed", "reach" [x, _] + "reachTime" (be at map x at that time), "look" overrides</param>
        private Vehicle SpawnVehicle(JNode tr, string laneId, string carName, string id, bool ghost, JNode step)
        {
            var lane = tr["lanes"][laneId];
            if (lane.IsNull) { Debug.LogWarning($"[PALINODE] Traffic lane '{laneId}' not found."); return null; }
            if (string.IsNullOrEmpty(carName))
            {
                var cars = new List<JNode>(lane["cars"].Items());
                if (cars.Count == 0) return null;
                carName = cars[Random.Range(0, cars.Count)].AsString();
            }
            var sprite = _config.Sprite(carName);
            if (sprite == null) { Debug.LogWarning($"[PALINODE] Vehicle sprite '{carName}' missing."); return null; }
            Vector2 a = PxToWorld(lane["path"][0]), b = PxToWorld(lane["path"][1]);
            Vector2 dir = (b - a).normalized;
            Vector2 sr = tr.Vec2("speed", new Vector2(600f, 720f));
            float speed = (step.Has("speed") ? step.Num("speed") : Random.Range(sr.x, sr.y)) / _ppu;

            // Ambient cars never drive through Elias: skip the spawn while he stands in the lane.
            if (!ghost && _player != null)
            {
                Vector2 f = _player.FeetPosition;
                float along = Vector2.Dot(f - a, dir);
                float off = Mathf.Abs(dir.x * (f - a).y - dir.y * (f - a).x);
                if (along > 0f && off < tr.Num("avoidPlayer", 110f) / _ppu) return null;
            }

            Vector2 from = a;
            if (step.Has("reach"))
            {
                // Point of the lane at the requested map x, reached "reachTime" s after the spawn.
                Vector2 rx = PxToWorld(new Vector2(step["reach"][0].AsFloat(), 0f));
                float tAlong = Mathf.Abs(dir.x) > 1e-4f ? (rx.x - a.x) / dir.x : 0f;
                from = a + dir * tAlong - dir * speed * step.Num("reachTime", 1f);
            }

            JNode look = tr;
            if (step["look"].IsObject)
            {
                var merged = new Dictionary<string, object>(tr.Object);
                foreach (var kv in step["look"].Pairs()) merged[kv.Key] = kv.Value.Raw;
                look = new JNode(merged);
            }
            var go = new GameObject("Vehicle_" + carName);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Vehicle>();
            v.Init(id ?? carName, _config, worldCamera, GameRoot.Instance != null ? GameRoot.Instance.Audio : null, sprite,
                tr["cars"][carName], look, from, b, speed, ghost, _ppu);
            if (!string.IsNullOrEmpty(id)) _vehicles[id] = v;
            Debug.Log($"[PALINODE] vehicle {carName} lane {laneId}{(ghost ? " (ghost)" : "")}: from {from} to {b}, {speed * _ppu:0} px/s");
            return v;
        }
        // ------------------------------------------------------------------ ILevel

        public void OnChapterStart(JNode chapter)
        {
            _crossed.Clear();
            if (_player == null) return;
            _lastFeet = _player.FeetPosition;
            _player.AutoPilot = AutoPilotEnabled;
            _player.AutoPath.Clear();
            if (AutoPilotEnabled)
                foreach (var p in chapter["autopilot"].Items()) _player.AutoPath.Add(PxToWorld(p));
        }

        private readonly HashSet<string> _crossed = new HashSet<string>();
        private Vector2 _lastFeet;

        /// <summary>Every frame: which triggers did the feet enter or cross (swept, so thin triggers work at any speed).</summary>
        private void Update()
        {
            if (_player == null) return;
            Vector2 cur = _player.FeetPosition;
            foreach (var kv in _triggers)
                if (!_crossed.Contains(kv.Key) && (kv.Value.Contains(cur) || SegmentHitsRect(_lastFeet, cur, kv.Value)))
                    _crossed.Add(kv.Key);
            _lastFeet = cur;
        }

        public IEnumerator WaitTrigger(string id)
        {
            if (!_triggers.ContainsKey(id))
            {
                Debug.LogWarning($"[PALINODE] Trigger '{id}' not found.");
                yield break;
            }
            float report = 0f;
            while (_player != null && !_crossed.Contains(id))
            {
                if (AutoPilotEnabled && (report += Time.deltaTime) > 20f)
                {
                    report = 0f;
                    Debug.LogWarning($"[PALINODE] Waiting for trigger '{id}' {_triggers[id]}; player at {_player.FeetPosition}, path left {_player.AutoPath.Count}");
                }
                yield return null;
            }
        }
        private static bool SegmentHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            // Liang–Barsky clipping.
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { a.x - r.xMin, r.xMax - a.x, a.y - r.yMin, r.yMax - a.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-7f) { if (q[i] < 0f) return false; continue; }
                float t = q[i] / p[i];
                if (p[i] < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return true;
        }

        public IEnumerator WaitInteract(string id, string promptKey)
        {
            if (!_interactables.TryGetValue(id, out var spec))
            {
                Debug.LogWarning($"[PALINODE] Interactable '{id}' not found.");
                yield break;
            }
            var root = GameRoot.Instance;
            Vector2 center = PxToWorld(spec["pos"]);
            float radius = spec.Num("radius", 80f) / _ppu;
            Vector2 promptPos = center + spec.Vec2("promptOffset", new Vector2(0f, -120f)) * new Vector2(1f, -1f) / _ppu;
            bool announced = false;
            float inRangeTime = 0f;
            while (true)
            {
                bool inRange = _player != null && Vector2.Distance(_player.FeetPosition, center) <= radius && _player.ControlEnabled;
                if (inRange)
                {
                    root.Overlay.ShowPrompt(root.T(promptKey), promptPos, worldCamera);
                    inRangeTime += Time.unscaledDeltaTime;
                    if (!announced && root.Overlay.PromptVisible)
                    {
                        announced = true;
                        CutsceneContext.Raise("prompt_" + id);
                    }
                    bool pressed = _player.InteractPressed || (AutoPilotEnabled && _player.AutoPath.Count == 0 && inRangeTime > 0.6f);
                    if (pressed)
                    {
                        root.Overlay.HidePrompt();
                        yield break;
                    }
                }
                else
                {
                    root.Overlay.HidePrompt();
                    inRangeTime = 0f;
                }
                yield return null;
            }
        }

        public void SetPlayerControl(bool enabled)
        {
            if (_player != null) _player.ControlEnabled = enabled;
            if (!enabled) GameRoot.Instance?.Overlay.HidePrompt();
        }

        public void FacePlayer(string direction) => _player?.Face(ParseFacing(direction));

        public void TurnPlayer(string direction, float duration) => _player?.TurnTo(ParseFacing(direction), duration);

        public GameObject Actor(string id)
        {
            _actors.TryGetValue(id ?? string.Empty, out var go);
            if (go == null) Debug.LogWarning($"[PALINODE] Actor '{id}' not found in level {levelId}.");
            return go;
        }

        public IEnumerator ActorStep(string type, JNode s, CutsceneContext ctx)
        {
            switch (type)
            {
                case "actorSprite":
                {
                    var go = Actor(s.Str("id"));
                    var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                    if (sr != null) sr.sprite = _config.Sprite(s.Str("sprite"));
                    return null;
                }
                case "actorActive":
                {
                    var go = Actor(s.Str("id"));
                    if (go != null) go.SetActive(s.Bool("active", true));
                    return null;
                }
                case "actorAlpha": return ActorAlpha(s);
                case "actorLight": return LightIntensity(s);
                case "actorText":
                {
                    var go = Actor(s.Str("id"));
                    var st = go != null ? go.GetComponent<StageText>() : null;
                    if (st == null) return null;
                    if (s.Has("key")) st.SetText(ctx.T(s.Str("key")));
                    return st.Emerge(s.Num("duration", 2f));
                }
                case "actorPress": return PressClack(s, ctx);
                case "lightSweep":
                    return LightSweep.Run(transform, PxToWorld(s["from"]), PxToWorld(s["to"]), s.Num("duration", 1.5f),
                        s.Num("intensity", 3f), s.Color("color", new Color(1f, 0.95f, 0.85f)), _config.Material("GlowAdd"));
                case "cameraSize": return CameraSize(s);
                case "ghostVehicle": return GhostVehicle(s);
                // Instant actions run inside a routine so that the step's "delay" applies to them.
                case "vehicle":
                    // One scripted vehicle on a traffic lane (e.g. the ghost truck); "id" lets vehicleVanish find it.
                    return Act(() => SpawnVehicle(_spec["traffic"], s.Str("lane", "A"), s.Str("car"), s.Str("id"), s.Bool("ghost"), s));
                case "vehicleVanish":
                    return Act(() =>
                    {
                        if (_vehicles.TryGetValue(s.Str("id") ?? string.Empty, out var v) && v != null)
                            v.Vanish(s.Num("duration", 0.12f), s.Num("soundFade", 0.35f));
                    });
                case "traffic": return Act(() => _trafficOn = s.Bool("enabled", true));
                case "playerTurn": TurnPlayer(s.Str("dir", "left"), s.Num("duration", 0.35f)); return null;
                case "playerFreeze":
                    // Stop dead but keep control disabled (used for the brakes moment).
                    SetPlayerControl(false);
                    return null;
                case "cameraShake": return CameraShake(s);
                case "levelLight": return GlobalIntensity(s);
                case "hint": return Hint(s, ctx);
                case "hideHint": return Act(() => ctx.Overlay.HideHint());
                default:
                    Debug.LogWarning($"[PALINODE] Unknown step type '{type}'.");
                    return null;
            }
        }

        private static IEnumerator Act(System.Action a)
        {
            a();
            yield break;
        }

        /// <summary>
        /// On-screen hint: "keys" (lines) are shown until the player has walked "untilMoved" world units
        /// (0 = don't wait for movement) or "maxTime" passes, then stay for "linger" seconds. Run it with "async": true.
        /// </summary>
        private IEnumerator Hint(JNode s, CutsceneContext ctx)
        {
            var lines = new List<string>();
            if (s.Has("keys")) foreach (var k in s["keys"].Items()) lines.Add(ctx.T(k.AsString()));
            else lines.Add(ctx.T(s.Str("key")));
            ctx.Overlay.ShowHint(string.Join("\n", lines));

            float untilMoved = s.Num("untilMoved", 0f), maxTime = s.Num("maxTime", 12f), t = 0f;
            Vector2 start = _player != null ? _player.FeetPosition : Vector2.zero;
            while (t < maxTime)
            {
                if (untilMoved > 0f && _player != null && Vector2.Distance(_player.FeetPosition, start) >= untilMoved) break;
                t += Time.deltaTime;
                yield return null;
            }
            float linger = s.Num("linger", 2.5f);
            while (linger > 0f) { linger -= Time.deltaTime; yield return null; }
            ctx.Overlay.HideHint();
        }

        private IEnumerator ActorAlpha(JNode s)
        {
            var go = Actor(s.Str("id"));
            if (go == null) yield break;
            var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            var from = new float[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) from[i] = renderers[i].color.a;
            float to = s.Num("to", 1f), dur = s.Num("duration", 1f), t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var c = renderers[i].color;
                    c.a = Mathf.Lerp(from[i], to, k);
                    renderers[i].color = c;
                }
                yield return null;
            }
        }

        private IEnumerator LightIntensity(JNode s)
        {
            if (!_lights.TryGetValue(s.Str("id") ?? string.Empty, out var l)) yield break;
            float from = l.intensity, to = s.Num("intensity", 1f), dur = s.Num("duration", 1f), t = 0f;
            var flick = l.GetComponent<IdleFlicker>();
            while (t < dur)
            {
                t += Time.deltaTime;
                float v = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
                if (flick != null) flick.Base = v; else l.intensity = v;
                yield return null;
            }
            if (flick != null) flick.Base = to; else l.intensity = to;
        }

        private IEnumerator GlobalIntensity(JNode s)
        {
            if (globalLight == null) yield break;
            float from = globalLight.intensity, to = s.Num("intensity", 1f), dur = s.Num("duration", 1f), t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                globalLight.intensity = Mathf.Lerp(from, to, t / dur);
                yield return null;
            }
            globalLight.intensity = to;
        }

        private IEnumerator CameraSize(JNode s)
        {
            if (worldCamera == null) yield break;
            float from = worldCamera.orthographicSize, to = s.Num("size", from), dur = s.Num("duration", 1f), t = 0f;
            Vector2 off0 = _follow != null ? _follow.Offset : Vector2.zero;
            Vector2 off1 = s.Has("offsetY") ? new Vector2(off0.x, s.Num("offsetY") / _ppu) : off0;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Ease.Apply("inOutSine", t / dur);
                worldCamera.orthographicSize = Mathf.Lerp(from, to, k);
                if (_follow != null) _follow.Offset = Vector2.Lerp(off0, off1, k);
                yield return null;
            }
            worldCamera.orthographicSize = to;
            if (_follow != null) _follow.Offset = off1;
        }

        private IEnumerator CameraShake(JNode s)
        {
            if (_follow == null) yield break;
            float dur = s.Num("duration", 0.3f), amp = s.Num("amplitude", 6f) / _ppu, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float f = 1f - t / dur;
                _follow.ShakeOffset = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * amp * f;
                yield return null;
            }
            _follow.ShakeOffset = Vector2.zero;
        }

        /// <summary>Printing press: the moving plate is lifted and slammed down N times.</summary>
        /// <summary>
        /// Printing press strike, performed by the whole machine: it gathers (slight rise), slams (compression of the
        /// frame), recoils; dust puffs from the base, the lamps dip, the camera shakes. N strikes, SFX on impact.
        /// </summary>
        private IEnumerator PressClack(JNode s, CutsceneContext ctx)
        {
            var press = Actor(s.Str("id"));
            if (press == null) yield break;
            int count = s.Int("count", 3);
            float interval = s.Num("interval", 0.6f);
            var clip = _config.Clip(s.Str("clip", "SFX_press_clack"));
            Vector3 s0 = press.transform.localScale;
            Vector3 p0 = press.transform.localPosition;
            var dust = MakeDust(press.transform.position, s.Num("dustWidth", 150f) / _ppu);
            for (int i = 0; i < count; i++)
            {
                // Wind-up: the frame stretches up a little.
                float t = 0f, up = interval * 0.55f;
                while (t < up)
                {
                    t += Time.deltaTime;
                    float k = Ease.Apply("inOutSine", t / up);
                    press.transform.localScale = new Vector3(s0.x * (1f - 0.012f * k), s0.y * (1f + 0.03f * k), s0.z);
                    yield return null;
                }
                // Slam.
                t = 0f;
                float down = interval * 0.08f;
                while (t < down)
                {
                    t += Time.deltaTime;
                    float k = Ease.Apply("inCubic", t / down);
                    press.transform.localScale = new Vector3(s0.x * (1f + 0.025f * k), s0.y * (1f + 0.03f - 0.09f * k), s0.z);
                    yield return null;
                }
                ctx.Audio.PlayOneShot(clip, s.Num("volume", 1f), Random.Range(0.96f, 1.04f));
                dust.Emit(40);
                StartCoroutine(CameraShake(new JNode(new Dictionary<string, object> { { "duration", 0.3 }, { "amplitude", 6.0 } })));
                StartCoroutine(LampDip(0.25f, 0.55f));
                // Recoil: damped oscillation back to rest, with a tiny sideways jolt.
                t = 0f;
                float rec = interval * 0.37f;
                while (t < rec)
                {
                    t += Time.deltaTime;
                    float k = t / rec;
                    float osc = Mathf.Cos(k * Mathf.PI * 3f) * Mathf.Exp(-k * 4f);
                    press.transform.localScale = new Vector3(s0.x * (1f + 0.025f * osc), s0.y * (1f - 0.06f * osc), s0.z);
                    press.transform.localPosition = p0 + new Vector3(Mathf.Sin(k * 40f) * 0.012f * (1f - k), 0f, 0f);
                    yield return null;
                }
                press.transform.localScale = s0;
                press.transform.localPosition = p0;
            }
            Destroy(dust.gameObject, 3f);
        }

        private ParticleSystem MakeDust(Vector3 at, float width)
        {
            var go = new GameObject("PressDust");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new Color(0.72f, 0.7f, 0.66f, 0.16f);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.25f, 0f);
            shape.position = new Vector3(0f, 0.15f, 0f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 1.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var src = _config.Material("ParticleAlpha");
            if (src != null) { var m = new Material(src); m.mainTexture = ProceduralSprites.SoftDot(64, 0f).texture; r.sharedMaterial = m; }
            r.sortingOrder = PlayerController.SortingOrderFor(at.y) + 5;
            return ps;
        }

        /// <summary>
        /// "The truck that isn't there": a vehicle sprite crosses as a translucent, cold, horizontally smeared ghost,
        /// with a few trailing echoes and a flicker; it leaves no reflection and casts no light of its own.
        /// </summary>
        private IEnumerator GhostVehicle(JNode s)
        {
            var sp = _config.Sprite(s.Str("sprite"));
            if (sp == null) yield break;
            Vector2 a = PxToWorld(s["from"]), b = PxToWorld(s["to"]);
            float dur = s.Num("duration", 1.6f), scale = s.Num("scale", 1.2f), alphaMax = s.Num("alpha", 0.35f);
            Color tint = s.Color("tint", new Color(0.72f, 0.82f, 1f));
            int echoes = s.Int("echoes", 4);
            var root = new GameObject("GhostVehicle").transform;
            root.SetParent(transform, false);
            var parts = new List<SpriteRenderer>();
            for (int i = 0; i <= echoes; i++)
            {
                var go = new GameObject(i == 0 ? "Body" : "Echo" + i);
                go.transform.SetParent(root, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.sharedMaterial = _config.Material("Ghost");
                sr.flipX = s.Bool("flipX");
                sr.sortingOrder = 13000 - i;
                parts.Add(sr);
            }
            float t = 0f;
            Vector2 dir = (b - a).normalized;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                Vector2 p = Vector2.Lerp(a, b, Ease.Apply("inOutSine", k));
                float env = Mathf.Sin(k * Mathf.PI);
                float flick = Random.value < 0.15f ? 0.4f : 1f;
                for (int i = 0; i < parts.Count; i++)
                {
                    var tr = parts[i].transform;
                    tr.position = p - dir * i * s.Num("echoSpacing", 0.45f);
                    tr.localScale = new Vector3(scale * (1f + 0.25f * env), scale * (1f - 0.08f * env), 1f);
                    float a2 = alphaMax * env * flick * (i == 0 ? 1f : 0.5f / i);
                    parts[i].color = new Color(tint.r, tint.g, tint.b, a2);
                }
                yield return null;
            }
            Destroy(root.gameObject);
        }

        /// <summary>All lamps of the level dip for a moment (a heavy impact shakes the wiring).</summary>
        private IEnumerator LampDip(float duration, float depth)
        {
            var baseI = new Dictionary<Light2D, float>();
            foreach (var l in _lights.Values) if (l != null && l.GetComponent<IdleFlicker>() == null) baseI[l] = l.intensity;
            float gl0 = globalLight != null ? globalLight.intensity : 0f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = 1f - depth * Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI) * (Random.value < 0.3f ? 1f : 0.6f);
                foreach (var kv in baseI) kv.Key.intensity = kv.Value * k;
                foreach (var l in _lights.Values) { var f = l != null ? l.GetComponent<IdleFlicker>() : null; if (f != null) f.Dip = k; }
                if (globalLight != null) globalLight.intensity = gl0 * k;
                yield return null;
            }
            foreach (var kv in baseI) kv.Key.intensity = kv.Value;
            foreach (var l in _lights.Values) { var f = l != null ? l.GetComponent<IdleFlicker>() : null; if (f != null) f.Dip = 1f; }
            if (globalLight != null) globalLight.intensity = gl0;
        }
        // ------------------------------------------------------------------ debug

        private void OnDebugColliders(bool visible)
        {
            if (!visible)
            {
                if (_debugRoot != null) _debugRoot.SetActive(false);
                return;
            }
            if (_debugRoot == null)
            {
                _debugRoot = new GameObject("DebugColliders");
                _debugRoot.transform.SetParent(transform, false);
                foreach (var path in _debugPaths)
                {
                    var go = new GameObject("Path");
                    go.transform.SetParent(_debugRoot.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true;
                    lr.positionCount = path.Length;
                    for (int i = 0; i < path.Length; i++) lr.SetPosition(i, path[i]);
                    lr.widthMultiplier = 0.025f;
                    lr.sharedMaterial = _config.Material("SpriteUnlit");
                    lr.startColor = lr.endColor = new Color(0.2f, 1f, 0.4f, 0.9f);
                    lr.sortingOrder = 20000;
                }
            }
            _debugRoot.SetActive(true);
        }
    }

    /// <summary>Gentle random intensity flicker for lamps (lanterns, street lamps).</summary>
    public sealed class IdleFlicker : MonoBehaviour
    {
        private Light2D _light;
        private float _amount;
        private float _seed;

        public float Base { get; set; }
        public float Dip { get; set; } = 1f;

        public void Init(Light2D light, float amount)
        {
            _light = light;
            _amount = amount;
            Base = light.intensity;
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            if (_light == null) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * 6f);
            _light.intensity = Base * Dip * (1f - _amount * Mathf.Clamp01((0.6f - n) * 2.5f));
        }
    }
}
