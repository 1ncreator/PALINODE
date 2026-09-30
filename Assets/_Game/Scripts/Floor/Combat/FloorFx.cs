using System.Collections;
using System.Collections.Generic;
using Palinode.UI;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Visual effects of the floor: particle bursts (ink, paper), floor decals (splats, red edits), the strike-through
    /// line that crosses out a dead enemy, falling letters. One world-space particle system for all bursts (from
    /// RogueDungeon's FxService).
    /// </summary>
    public sealed class FloorFx : MonoBehaviour
    {
        private ParticleSystem _particles;
        private ParticleSystem.EmitParams _emit;
        private Material _material;
        private static readonly string[] Splats = { "F1_splat_1", "F1_splat_2", "F1_splat_3", "F1_splat_4" };

        public static readonly Color Ink = new Color(0.06f, 0.05f, 0.06f, 1f);
        public static readonly Color RedInk = new Color(0.72f, 0.07f, 0.08f, 1f);
        public static readonly Color Paper = new Color(0.86f, 0.8f, 0.68f, 1f);

        public void Init()
        {
            _particles = gameObject.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2000;
            main.startLifetime = 0.4f;
            main.startSpeed = 0f;
            main.startSize = 0.15f;
            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;
            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var drag = _particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0f;
            drag.dampen = 0.1f;
            var r = GetComponent<ParticleSystemRenderer>();
            var src = FloorSprites.Config != null ? FloorSprites.Config.Material("ParticleAlpha") : null;
            _material = src != null ? new Material(src) : new Material(Shader.Find("Sprites/Default"));
            _material.mainTexture = ProceduralSprites.SoftDot(32, 0.45f).texture;
            r.sharedMaterial = _material;
            r.sortingOrder = FloorSprites.OverlayOrder - 10;
            _emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            _particles.Play();
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        public void Burst(Vector2 pos, Color color, int count, float speed, float size = 0.12f, float life = 0.4f)
        {
            if (_particles == null) return;
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                dir.y *= 0.75f;
                _emit.position = pos;
                _emit.velocity = dir * (speed * Random.Range(0.3f, 1f));
                _emit.startColor = color;
                _emit.startSize = size * Random.Range(0.6f, 1.4f);
                _emit.startLifetime = life * Random.Range(0.7f, 1.25f);
                _particles.Emit(_emit, 1);
            }
        }

        public void Clear() => _particles?.Clear();

        /// <summary>Ink splat decal on the floor of <paramref name="room"/> (stays until Elias leaves the room).</summary>
        public SpriteRenderer Splat(RoomView room, Vector2 pos, float width, Color tint)
        {
            if (room == null) return null;
            var sr = FloorSprites.Make(room.DecalRoot, "Splat", FloorSprites.Get(Splats[Random.Range(0, Splats.Length)]), width,
                FloorSprites.FloorDecalOrder + Random.Range(0, 200));
            sr.transform.position = pos;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            var s = sr.transform.localScale;
            sr.transform.localScale = new Vector3(s.x, s.y * 0.75f, 1f);
            sr.color = tint;
            room.RegisterSplat(sr);
            return sr;
        }

        /// <summary>A sharp ink stroke that crosses out whatever stands at <paramref name="center"/>.</summary>
        public void Strike(Transform parent, Vector2 center, float length, Color color, float duration = 0.09f, float hold = 0.25f,
            float angle = float.NaN)
        {
            StartCoroutine(StrikeRoutine(parent, center, length, color, duration, hold, angle));
        }

        private IEnumerator StrikeRoutine(Transform parent, Vector2 center, float length, Color color, float duration, float hold, float angle)
        {
            if (float.IsNaN(angle)) angle = Random.Range(-24f, -8f) * (Random.value < 0.5f ? 1f : -1f);
            var line = FloorSprites.Quad(parent != null ? parent : transform, "Strike", color, new Vector2(0.01f, 0.07f), FloorSprites.OverlayOrder, FloorSprites.Solid);
            line.transform.position = center;
            line.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var start = (Vector2)center - (Vector2)(line.transform.right * length * 0.5f);
            float baseY = line.transform.localScale.y;
            float unit = line.sprite.bounds.size.x;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float len = Mathf.Max(0.01f, length * k);
                line.transform.localScale = new Vector3(len / unit, baseY, 1f);
                line.transform.position = start + (Vector2)(line.transform.right * len * 0.5f);
                yield return null;
            }
            t = 0f;
            while (t < hold)
            {
                t += Time.deltaTime;
                var c = line.color;
                c.a = 1f - Mathf.Clamp01(t / hold) * 0.6f;
                line.color = c;
                yield return null;
            }
            if (line != null) Destroy(line.gameObject);
        }

        /// <summary>A sprite flies up a little, spins and drops out of sight (a letter falling off its block).</summary>
        public void Fall(Sprite sprite, Vector2 pos, float width, bool flip = false)
        {
            if (sprite == null) return;
            var sr = FloorSprites.Make(transform, "Fall", sprite, width, FloorSprites.Order(pos.y) + 5);
            sr.flipX = flip;
            sr.transform.position = pos;
            StartCoroutine(FallRoutine(sr));
        }

        private IEnumerator FallRoutine(SpriteRenderer sr)
        {
            Vector2 p0 = sr.transform.position;
            float vx = Random.Range(-1.2f, 1.2f), spin = Random.Range(-420f, 420f);
            float t = 0f, dur = 0.55f;
            while (t < dur && sr != null)
            {
                t += Time.deltaTime;
                float k = t / dur;
                sr.transform.position = p0 + new Vector2(vx * t, 1.6f * t - 5.5f * t * t);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, spin * t);
                var c = sr.color;
                c.a = 1f - k * k;
                sr.color = c;
                yield return null;
            }
            if (sr != null) Destroy(sr.gameObject);
        }

        /// <summary>A sprite fades away over time (short-lived marks).</summary>
        public void FadeOut(SpriteRenderer sr, float delay, float duration)
        {
            if (sr != null) StartCoroutine(FadeRoutine(sr, delay, duration));
        }

        private IEnumerator FadeRoutine(SpriteRenderer sr, float delay, float duration)
        {
            yield return new WaitForSeconds(delay);
            float t = 0f;
            float a0 = sr != null ? sr.color.a : 1f;
            while (t < duration && sr != null)
            {
                t += Time.deltaTime;
                var c = sr.color;
                c.a = a0 * (1f - t / duration);
                sr.color = c;
                yield return null;
            }
            if (sr != null) Destroy(sr.gameObject);
        }
    }
}
