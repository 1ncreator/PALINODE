using Palinode.Core;
using Palinode.Cutscene;
using Palinode.UI;
using UnityEngine;

namespace Palinode.Effects
{
    /// <summary>Procedurally configured particle systems: falling rain streaks and dust motes in a lamp beam.</summary>
    public sealed class ParticleFX : MonoBehaviour, IStageAlpha
    {
        private ParticleSystem _ps;
        private ParticleSystemRenderer _renderer;
        private Material _material;
        private Color _baseColor;
        private float _alpha = 1f;

        public ParticleSystem System => _ps;

        private void Create(StageLayer layer, GameConfig config, string materialName, int order)
        {
            var go = new GameObject("Particles");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _renderer = go.GetComponent<ParticleSystemRenderer>();
            var src = config.Material(materialName);
            _material = src != null ? new Material(src) : null;
            _renderer.sharedMaterial = _material;
            _renderer.sortingOrder = order;
            layer?.RegisterAlphaTarget(this);
        }

        /// <summary>Rain in a rectangle given in parent pixels ("size"), falling at "angle" degrees.</summary>
        public void BuildRain(StageLayer layer, JNode spec, GameConfig config)
        {
            Create(layer, config, spec.Str("material", "ParticleAlpha"), layer.Order);
            ConfigureRain(spec, layer.Parent != null ? 1f : 1f);
            _ps.Play();
        }

        public void ConfigureRain(JNode spec, float unitScale)
        {
            Vector2 size = spec.Vec2("size", new Vector2(1920f, 1080f)) * 0.01f * unitScale;
            float angle = spec.Num("angle", 8f);
            float speed = spec.Num("speed", 14f) * unitScale;
            float rate = spec.Num("rate", 400f);
            float length = spec.Num("length", 0.35f) * unitScale;
            _baseColor = spec.Color("color", new Color(0.75f, 0.8f, 0.9f, 0.28f));
            if (_material != null) _material.mainTexture = ProceduralSprites.SoftDot(32, 0.2f).texture;

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startSpeed = speed;
            main.startLifetime = (size.y + 1f) / Mathf.Max(0.1f, speed) * 1.1f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.012f * unitScale * spec.Num("width", 1f), 0.022f * unitScale * spec.Num("width", 1f));
            main.startSizeY = new ParticleSystem.MinMaxCurve(length * 0.6f, length);
            main.startSizeZ = 1f;
            main.startColor = _baseColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 4000;
            main.prewarm = true;
            main.startRotation = 0f;

            var emission = _ps.emission;
            emission.rateOverTime = rate;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(size.x * 1.15f, 0.1f, 0f);
            shape.position = new Vector3(0f, size.y * 0.5f + 0.2f, 0f);
            shape.rotation = new Vector3(90f, 0f, 0f);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            float rad = angle * Mathf.Deg2Rad;
            vel.x = new ParticleSystem.MinMaxCurve(-Mathf.Sin(rad) * speed * 0.9f, -Mathf.Sin(rad) * speed * 1.1f);
            vel.y = new ParticleSystem.MinMaxCurve(-speed * 0.1f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Rotate the quads to follow the fall direction.
            main.startRotation = new ParticleSystem.MinMaxCurve(-rad, -rad);

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _renderer.alignment = ParticleSystemRenderSpace.View;
        }

        /// <summary>Slowly drifting dust motes inside a rect ("size" in parent pixels).</summary>
        public void BuildDust(StageLayer layer, JNode spec, GameConfig config)
        {
            Create(layer, config, spec.Str("material", "ParticleAdd"), layer.Order);
            Vector2 size = spec.Vec2("size", new Vector2(400f, 400f)) * 0.01f;
            _baseColor = spec.Color("color", new Color(1f, 0.85f, 0.6f, 0.35f));
            if (_material != null) _material.mainTexture = ProceduralSprites.SoftDot(32, 0.1f).texture;

            var main = _ps.main;
            main.loop = true;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f * spec.Num("sizeMul", 1f), 0.055f * spec.Num("sizeMul", 1f));
            main.startColor = _baseColor;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 300;
            main.prewarm = true;

            var emission = _ps.emission;
            emission.rateOverTime = spec.Num("rate", 14f);

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(size.x, size.y, 0f);

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.15f;

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.03f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.01f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            _renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _ps.Play();
        }

        public void SetStageAlpha(float alpha)
        {
            if (Mathf.Approximately(alpha, _alpha)) return;
            _alpha = alpha;
            if (_material != null) _material.SetFloat("_Alpha", alpha);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
