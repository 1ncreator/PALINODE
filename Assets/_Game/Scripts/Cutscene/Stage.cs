using System.Collections.Generic;
using Palinode.Core;
using Palinode.Effects;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// Layered 2D "theatre" for cinematic shots. Stage space: 1 unit = 100 reference pixels of a 1920×1080 frame,
    /// origin at the screen centre. A virtual camera (pan/zoom/roll/shake) is applied per root layer with parallax depth.
    /// </summary>
    public sealed class Stage : MonoBehaviour
    {
        public const float ViewWidth = 19.2f;
        public const float ViewHeight = 10.8f;

        [SerializeField] private int sortingBase;

        private readonly Dictionary<string, StageLayer> _layers = new Dictionary<string, StageLayer>();
        private readonly List<StageLayer> _all = new List<StageLayer>();
        private GameConfig _config;
        private int _group;
        private int _autoId;

        public Vector2 CamPos { get; set; }
        public float CamZoom { get; set; } = 1f;
        public float CamRot { get; set; }
        public Vector2 Shake { get; set; }

        /// <summary>Hand-held camera drift: amplitude in reference px, roll in degrees, speed (~Hz).</summary>
        public float HandheldPx { get; set; }
        public float HandheldRot { get; set; }
        public float HandheldSpeed { get; set; } = 0.35f;
        private Vector2 _drift;
        private float _driftRot;

        public int CurrentGroup => _group;
        public GameConfig Config => _config;

        /// <summary>Default for sprite layers without "lit". Inserts shown over a gameplay level are unlit so level lights do not leak in.</summary>
        public bool DefaultLit { get; private set; } = true;

        public void Init(GameConfig config, int orderBase, bool defaultLit = true)
        {
            _config = config;
            sortingBase = orderBase;
            DefaultLit = defaultLit;
        }

        public int NextGroup() => ++_group;

        public int GroupSortBase(int group) => sortingBase + (group % 16) * 1000;

        public StageLayer Get(string id)
        {
            if (id == null) return null;
            _layers.TryGetValue(id, out var l);
            if (l == null) _layers.Remove(id);
            return l;
        }

        public bool TryGet(string id, out StageLayer layer)
        {
            layer = Get(id);
            return layer != null;
        }

        public IEnumerable<StageLayer> RootLayersInGroup(int group)
        {
            foreach (var l in _all)
                if (l != null && l.Parent == null && l.Group == group) yield return l;
        }

        public List<StageLayer> AllLayers()
        {
            _all.RemoveAll(l => l == null);
            return new List<StageLayer>(_all);
        }

        public StageLayer CreateLayer(JNode spec, int group)
        {
            string id = spec.Str("id") ?? ("auto" + (++_autoId));
            string type = spec.Str("type", "sprite");
            StageLayer parent = spec.Has("parent") ? Get(spec.Str("parent")) : null;
            if (spec.Has("parent") && parent == null)
                Debug.LogWarning($"[PALINODE] Layer '{id}': parent '{spec.Str("parent")}' not found.");

            if (_layers.TryGetValue(id, out var old) && old != null) Remove(id);

            var go = new GameObject("L_" + id);
            go.layer = gameObject.layer;
            go.transform.SetParent(parent != null ? parent.transform : transform, false);
            var layer = go.AddComponent<StageLayer>();
            int baseOrder = GroupSortBase(group);
            int order = spec.Has("order")
                ? baseOrder + spec.Int("order")
                : (parent != null ? parent.Order + 1 : baseOrder);
            layer.Init(id, group, parent, order);
            layer.Depth = spec.Num("depth", 1f);
            layer.Alpha = spec.Num("alpha", 1f);
            layer.Tint = spec.Color("tint", Color.white);

            Vector2 natural = Vector2.zero; // natural size in units for fitting
            switch (type)
            {
                case "sprite":
                {
                    var sr = layer.EnsureRenderer();
                    sr.sprite = _config.Sprite(spec.Str("sprite"));
                    sr.sortingOrder = order;
                    sr.flipX = spec.Bool("flipX");
                    var mat = _config.Material(spec.Bool("lit", DefaultLit) ? "SpriteLit" : "SpriteUnlit");
                    if (mat != null) sr.sharedMaterial = mat;
                    if (sr.sprite != null) natural = sr.sprite.bounds.size;
                    break;
                }
                case "rect":
                {
                    var sr = layer.EnsureRenderer();
                    sr.sprite = Palinode.UI.ProceduralSprites.White();
                    sr.sortingOrder = order;
                    var mat = _config.Material(spec.Bool("lit", false) ? "SpriteLit" : "SpriteUnlit");
                    if (mat != null) sr.sharedMaterial = mat;
                    Vector2 size = spec.Vec2("size", new Vector2(ViewWidth * 100f * 1.6f, ViewHeight * 100f * 1.6f)) * 0.01f;
                    sr.drawMode = SpriteDrawMode.Sliced;
                    sr.size = size;
                    layer.Tint = spec.Color("color", Color.black);
                    break;
                }
                case "page": go.AddComponent<HandwritingPage>().Build(layer, spec, _config); break;
                case "phone": go.AddComponent<PhoneScreen>().Build(layer, spec, _config); break;
                case "clock": go.AddComponent<ClockFace>().Build(layer, spec, _config); break;
                case "text": go.AddComponent<StageText>().Build(layer, spec, _config); break;
                case "light": go.AddComponent<StageLight>().Build(layer, spec); break;
                case "rain": go.AddComponent<ParticleFX>().BuildRain(layer, spec, _config); break;
                case "dust": go.AddComponent<ParticleFX>().BuildDust(layer, spec, _config); break;
                case "shaderQuad": go.AddComponent<ShaderQuad>().Build(layer, spec, _config); break;
                case "glow": go.AddComponent<ShaderQuad>().BuildGlow(layer, spec, _config); break;
                case "empty": break;
                default:
                    Debug.LogWarning($"[PALINODE] Unknown layer type '{type}' for '{id}'.");
                    break;
            }

            // Transform
            float scale = spec.Num("scale", 1f);
            Vector2 scale2 = spec.Vec2("scale2", new Vector2(scale, scale));
            if (parent == null)
            {
                string fit = spec.Str("fit", type == "sprite" ? "cover" : "none");
                float f = 1f;
                if (natural.x > 0f && natural.y > 0f)
                {
                    switch (fit)
                    {
                        case "cover": f = Mathf.Max(ViewWidth / natural.x, ViewHeight / natural.y); break;
                        case "contain": f = Mathf.Min(ViewWidth / natural.x, ViewHeight / natural.y); break;
                        case "width": f = ViewWidth / natural.x; break;
                        case "height": f = ViewHeight / natural.y; break;
                    }
                }
                layer.BaseScale = scale2 * f;
                layer.BasePos = spec.Vec2("pos", Vector2.zero) * 0.01f;
            }
            else
            {
                layer.BaseScale = scale2;
                layer.BasePos = spec.Has("px")
                    ? parent.PixelToLocal(spec.Vec2("px", Vector2.zero))
                    : spec.Vec2("pos", Vector2.zero) * 0.01f;
            }
            layer.BaseRot = spec.Num("rot", 0f);

            if (spec.Has("tip") || spec.Bool("rig"))
            {
                // Tip: explicit pixel, or the sprite pivot (art_processing "pivot": "tip" puts it on the nib).
                Vector2 tip;
                var sp = layer.Sprite;
                if (spec["tip"].IsArray) tip = spec.Vec2("tip", Vector2.zero);
                else tip = sp != null ? new Vector2(sp.pivot.x, sp.rect.height - sp.pivot.y) : Vector2.zero;
                Vector2? elbow = spec.Has("elbow") ? spec.Vec2("elbow", Vector2.zero) : _config.ArtPoint(spec.Str("sprite"), "elbow");
                var hf = go.AddComponent<HandFollower>();
                hf.Init(layer, tip, elbow);
                hf.BuildShadows(_config.Material("SpriteUnlit"), spec.Num("shadowAlpha", 0.3f));
                if (spec.Has("liftPx")) hf.LiftPx = spec.Num("liftPx");
                if (spec.Has("elbowFollow")) { var ef = spec.Vec2("elbowFollow", new Vector2(0.3f, 0.15f)); hf.ElbowFollowX = ef.x; hf.ElbowFollowY = ef.y; }
                if (spec.Has("shadowDir")) hf.ShadowDir = spec.Vec2("shadowDir", hf.ShadowDir);
                if (spec.Has("enter")) hf.EnterFrom(spec.Vec2("enter", Vector2.zero) * new Vector2(0.01f, -0.01f), spec.Num("enterTime", 1.2f));
            }

            _layers[id] = layer;
            _all.Add(layer);
            ApplyLayer(layer);
            layer.ApplyAlpha();
            return layer;
        }

        public void Remove(string id)
        {
            var l = Get(id);
            _layers.Remove(id);
            if (l == null) return;
            foreach (var d in l.GetComponentsInChildren<StageLayer>(true))
            {
                if (d.Id != null && _layers.TryGetValue(d.Id, out var reg) && reg == d) _layers.Remove(d.Id);
                _all.Remove(d);
            }
            Destroy(l.gameObject);
        }

        public void RemoveGroup(int group)
        {
            foreach (var l in new List<StageLayer>(_all))
                if (l != null && l.Parent == null && l.Group == group) Remove(l.Id);
        }

        public void ClearAll()
        {
            foreach (var l in new List<StageLayer>(_all))
                if (l != null && l.Parent == null) Remove(l.Id);
            _all.Clear();
            _layers.Clear();
            CamPos = Vector2.zero;
            CamZoom = 1f;
            CamRot = 0f;
            Shake = Vector2.zero;
        }

        private void ApplyLayer(StageLayer l)
        {
            if (l.Parent != null)
            {
                l.ApplyChildTransform();
                return;
            }
            float z = Mathf.Pow(Mathf.Max(0.01f, CamZoom), l.Depth);
            Vector2 cam = (CamPos + Shake + _drift) * l.Depth;
            Vector2 p = (l.BasePos + l.Offset - cam) * z;
            float rot = CamRot + _driftRot;
            if (Mathf.Abs(rot) > 0.001f) p = Quaternion.Euler(0f, 0f, -rot) * p;
            l.transform.localPosition = new Vector3(p.x, p.y, 0f);
            l.transform.localRotation = Quaternion.Euler(0f, 0f, l.BaseRot + l.RotOffset - rot);
            l.transform.localScale = new Vector3(l.BaseScale.x * z, l.BaseScale.y * z, 1f);
        }

        private void LateUpdate()
        {
            float ht = Time.time * HandheldSpeed;
            _drift = new Vector2(Mathf.PerlinNoise(ht, 3.7f) - 0.5f, Mathf.PerlinNoise(8.1f, ht * 0.9f) - 0.5f) * 2f * HandheldPx * 0.01f;
            _driftRot = (Mathf.PerlinNoise(ht * 0.7f, 12.3f) - 0.5f) * 2f * HandheldRot;
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var l = _all[i];
                if (l == null) { _all.RemoveAt(i); continue; }
                ApplyLayer(l);
            }
            for (int i = 0; i < _all.Count; i++) _all[i].ApplyAlpha();
        }
    }
}
