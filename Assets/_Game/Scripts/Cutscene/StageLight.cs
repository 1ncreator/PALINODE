using System.Collections;
using Palinode.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Cutscene
{
    /// <summary>2D point/spot light attached to a stage layer with optional idle flicker and scripted flicker bursts.</summary>
    public sealed class StageLight : MonoBehaviour, IStageAlpha
    {
        private Light2D _light;
        private float _baseIntensity;
        private float _idleFlicker;
        private float _alpha = 1f;
        private float _burst = 1f;
        private float _seed;

        public Light2D Light => _light;

        public float BaseIntensity
        {
            get => _baseIntensity;
            set => _baseIntensity = value;
        }

        public static Light2D CreateLight(GameObject host, Light2D.LightType type)
        {
            bool wasActive = host.activeSelf;
            host.SetActive(false);
            var l = host.AddComponent<Light2D>();
            l.lightType = type;
            var ids = new int[SortingLayer.layers.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = SortingLayer.layers[i].id;
            l.targetSortingLayers = ids;
            host.SetActive(wasActive);
            return l;
        }

        public void Build(StageLayer layer, JNode spec)
        {
            var child = new GameObject("Light2D");
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            _light = CreateLight(child, Light2D.LightType.Point);
            Configure(spec, layer.transform.lossyScale.x);
            layer.RegisterAlphaTarget(this);
        }

        public void Configure(JNode spec, float scale)
        {
            _light.color = spec.Color("color", new Color(1f, 0.82f, 0.55f));
            _baseIntensity = spec.Num("intensity", 0.8f);
            float r = spec.Num("radius", 300f) * 0.01f * Mathf.Max(0.0001f, scale);
            _light.pointLightOuterRadius = r;
            _light.pointLightInnerRadius = r * spec.Num("inner", 0.15f);
            _light.falloffIntensity = spec.Num("falloff", 0.6f);
            if (spec.Has("angle"))
            {
                float a = spec.Num("angle", 360f);
                _light.pointLightOuterAngle = a;
                _light.pointLightInnerAngle = a * spec.Num("innerAngle", 0.6f);
                _light.transform.localRotation = Quaternion.Euler(0f, 0f, spec.Num("dir", 0f) - 90f);
            }
            _idleFlicker = spec.Num("flicker", 0f);
            _seed = Random.value * 100f;
            _light.intensity = _baseIntensity;
        }

        /// <summary>Scripted flicker: rapid irregular dips for a duration.</summary>
        public IEnumerator Flicker(float duration, float strength)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float n = Mathf.PerlinNoise(_seed, Time.time * 22f);
                bool drop = n < 0.45f || Random.value < 0.08f;
                _burst = drop ? 1f - strength * Random.Range(0.6f, 1f) : 1f + strength * 0.15f;
                yield return null;
            }
            _burst = 1f;
        }

        public void SetStageAlpha(float alpha) => _alpha = alpha;

        private void Update()
        {
            if (_light == null) return;
            float idle = 1f;
            if (_idleFlicker > 0f)
            {
                float n = Mathf.PerlinNoise(_seed, Time.time * 7f);
                idle = 1f - _idleFlicker * Mathf.Clamp01((0.55f - n) * 3f);
            }
            _light.intensity = _baseIntensity * idle * _burst * _alpha;
            _light.enabled = _alpha > 0.001f;
        }
    }
}
