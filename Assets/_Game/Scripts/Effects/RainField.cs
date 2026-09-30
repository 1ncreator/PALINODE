using Palinode.Core;
using Palinode.UI;
using UnityEngine;

namespace Palinode.Effects
{
    /// <summary>
    /// Full-screen rain for top-down levels. Three depth layers (far: fine, slow, faint → near: long, fast, bright),
    /// world-space simulation (drops do not slide with the camera) and emitters resized every frame to cover the view
    /// at any aspect ratio, plus puddle rings and tiny splashes on the ground inside the view.
    /// </summary>
    public sealed class RainField : MonoBehaviour
    {
        private sealed class Layer
        {
            public ParticleSystem Ps;
            public float Speed, Rate;
        }

        private Camera _cam;
        private Layer[] _layers;
        private ParticleSystem _rings, _splashes;
        private float _angle;
        private float _density = 1f;

        public void Build(Camera cam, JNode spec, GameConfig config, int order)
        {
            _cam = cam;
            _angle = spec.Num("angle", 9f) * Mathf.Deg2Rad;
            _density = spec.Num("density", 1f);
            Color c = spec.Color("color", new Color(0.78f, 0.82f, 0.9f, 0.6f));
            var drop = ProceduralSprites.SoftDot(32, 0.2f).texture;
            _layers = new[]
            {
                MakeLayer("RainFar", config, drop, order - 2, c * new Color(1, 1, 1, 0.45f), speed: 7f, rate: 900f, len: 0.16f, width: 0.008f),
                MakeLayer("RainMid", config, drop, order - 1, c * new Color(1, 1, 1, 0.7f), speed: 10f, rate: 520f, len: 0.3f, width: 0.013f),
                MakeLayer("RainNear", config, drop, order, c, speed: 15f, rate: 110f, len: 0.7f, width: 0.024f),
            };
            if (spec.Bool("ground", true))
            {
                _rings = MakeGround("PuddleRings", config, ProceduralSprites.Ring(64, 0.08f).texture, -29500, spec.Num("rings", 70f), true);
                _splashes = MakeGround("Splashes", config, drop, -29400, spec.Num("splashes", 160f), false);
            }
            Resize();
            foreach (var l in _layers) l.Ps.Play();
            _rings?.Play();
            _splashes?.Play();
        }

        private ParticleSystem Create(string name, GameConfig config, Texture tex, int order, string mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            var src = config.Material(mat);
            if (src != null)
            {
                var m = new Material(src);
                m.mainTexture = tex;
                r.sharedMaterial = m;
            }
            r.sortingOrder = order;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.loop = true;
            main.prewarm = true;
            return ps;
        }

        private Layer MakeLayer(string name, GameConfig config, Texture tex, int order, Color color, float speed, float rate, float len, float width)
        {
            var ps = Create(name, config, tex, order, "ParticleAlpha");
            var main = ps.main;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(width * 0.7f, width * 1.3f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(len * 0.7f, len);
            main.startSizeZ = 1f;
            main.startColor = color;
            main.startRotation = -_angle;
            main.maxParticles = 6000;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-Mathf.Sin(_angle) * speed * 0.95f, -Mathf.Sin(_angle) * speed * 1.05f);
            vel.y = new ParticleSystem.MinMaxCurve(-speed * 1.05f, -speed * 0.95f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return new Layer { Ps = ps, Speed = speed, Rate = rate };
        }

        private ParticleSystem MakeGround(string name, GameConfig config, Texture tex, int order, float rate, bool rings)
        {
            var ps = Create(name, config, tex, order, "ParticleAlpha");
            var main = ps.main;
            main.startSpeed = 0f;
            main.startLifetime = rings ? new ParticleSystem.MinMaxCurve(0.35f, 0.55f) : new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startSize = rings ? new ParticleSystem.MinMaxCurve(0.03f, 0.05f) : new ParticleSystem.MinMaxCurve(0.012f, 0.025f);
            main.startColor = new Color(0.8f, 0.85f, 0.95f, rings ? 0.35f : 0.55f);
            main.maxParticles = 800;
            var em = ps.emission;
            em.rateOverTime = rate * _density;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = rings ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 6f)) : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!rings)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                main.gravityModifier = 0.6f;
            }
            else
            {
                // Rings lie flat on the ground in a 3/4 view: squash them vertically.
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
                main.startSizeZ = 1f;
                size.separateAxes = true;
                size.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 6f));
                size.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 6f));
                size.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            }
            return ps;
        }

        private void Resize()
        {
            if (_cam == null) return;
            float h = _cam.orthographicSize * 2f, w = h * _cam.aspect;
            Vector3 camPos = _cam.transform.position;
            foreach (var l in _layers)
            {
                float drift = Mathf.Tan(_angle) * (h + 2f);
                var shape = l.Ps.shape;
                shape.scale = new Vector3(w + drift + 2f, 0.2f, 0f);
                shape.position = Vector3.zero;
                l.Ps.transform.position = new Vector3(camPos.x + drift * 0.5f, camPos.y + h * 0.5f + 0.5f, 0f);
                var main = l.Ps.main;
                main.startLifetime = (h + 1.5f) / l.Speed;
                var em = l.Ps.emission;
                em.rateOverTime = l.Rate * _density * (w * h) / (12.4f * 7f); // constant density on screen
            }
            foreach (var g in new[] { _rings, _splashes })
            {
                if (g == null) continue;
                var shape = g.shape;
                shape.scale = new Vector3(w * 1.1f, h * 1.1f, 0f);
                g.transform.position = new Vector3(camPos.x, camPos.y, 0f);
            }
        }

        private void LateUpdate() => Resize();
    }
}
