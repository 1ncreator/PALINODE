using System.Collections;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// Clock hands (optionally with a dial sprite) laid over an illustration. Hand sprites are upright with their
    /// pivot at the ring centre, so rotation is just −angle. "squash" fakes perspective of an elliptical dial.
    /// </summary>
    public sealed class ClockFace : MonoBehaviour, IStageAlpha
    {
        private Transform _dial;
        private SpriteRenderer _face;
        private SpriteRenderer _hour;
        private SpriteRenderer _minute;
        private float _minutes; // minutes since 00:00 (continuous)
        private Color _tint = Color.white;

        public float TotalMinutes => _minutes;

        public void Build(StageLayer layer, JNode spec, GameConfig config)
        {
            _tint = spec.Color("handTint", Color.white);
            _dial = new GameObject("Dial").transform;
            _dial.gameObject.layer = gameObject.layer;
            _dial.SetParent(transform, false);
            Vector2 squash = spec.Vec2("squash", Vector2.one);
            _dial.localScale = new Vector3(squash.x, squash.y, 1f);
            _dial.localRotation = Quaternion.Euler(0f, 0f, spec.Num("tilt", 0f));
            var stage = layer.GetComponentInParent<Stage>();
            var mat = config.Material(spec.Bool("lit", stage == null || stage.DefaultLit) ? "SpriteLit" : "SpriteUnlit");

            if (spec.Has("face"))
            {
                _face = MakeRenderer("Face", config.Sprite(spec.Str("face")), layer.Order, mat);
                _face.transform.SetParent(transform, false); // the face sprite already contains its own perspective
                _face.transform.localScale = Vector3.one * spec.Num("faceScale", 1f);
            }
            _hour = MakeHand("Hour", config.Sprite(spec.Str("hour")), spec.Num("hourLen", 40f), layer.Order + 1, mat, spec.Num("handWidth", 1f));
            _minute = MakeHand("Minute", config.Sprite(spec.Str("minute")), spec.Num("minuteLen", 60f), layer.Order + 2, mat, spec.Num("handWidth", 1f));
            SetTimeImmediate(spec.Str("time", "00:00"));
            layer.RegisterAlphaTarget(this);
        }

        private SpriteRenderer MakeRenderer(string name, Sprite sprite, int order, Material mat)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(_dial, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (mat != null) sr.sharedMaterial = mat;
            return sr;
        }

        private SpriteRenderer MakeHand(string name, Sprite sprite, float lengthPx, int order, Material mat, float widthMul)
        {
            var sr = MakeRenderer(name, sprite, order, mat);
            sr.color = _tint;
            if (sprite != null)
            {
                float spriteLen = Mathf.Max(1f, sprite.rect.height - sprite.pivot.y);
                float s = lengthPx / spriteLen;
                sr.transform.localScale = new Vector3(s * widthMul, s, 1f);
            }
            return sr;
        }

        public static float ParseMinutes(string hhmm)
        {
            if (string.IsNullOrEmpty(hhmm)) return 0f;
            var parts = hhmm.Split(':');
            int h = parts.Length > 0 && int.TryParse(parts[0], out int hh) ? hh : 0;
            int m = parts.Length > 1 && int.TryParse(parts[1], out int mm) ? mm : 0;
            return h * 60f + m;
        }

        public void SetTimeImmediate(string hhmm)
        {
            _minutes = ParseMinutes(hhmm);
            Apply();
        }

        /// <summary>Advance (always forward) to the given time.</summary>
        public IEnumerator SetTime(string hhmm, float duration, string ease)
        {
            float target = ParseMinutes(hhmm);
            float from = _minutes;
            float delta = Mathf.Repeat(target - Mathf.Repeat(from, 1440f), 1440f);
            if (duration <= 0f)
            {
                _minutes = from + delta;
                Apply();
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                _minutes = from + delta * Ease.Apply(ease ?? "outBack", t / duration);
                Apply();
                yield return null;
            }
            _minutes = from + delta;
            Apply();
        }

        private void Apply()
        {
            float m = Mathf.Repeat(_minutes, 1440f);
            float minuteAngle = (m % 60f) * 6f;
            float hourAngle = ((m / 60f) % 12f) * 30f;
            if (_minute != null) _minute.transform.localRotation = Quaternion.Euler(0f, 0f, -minuteAngle);
            if (_hour != null) _hour.transform.localRotation = Quaternion.Euler(0f, 0f, -hourAngle);
        }

        public void SetStageAlpha(float alpha)
        {
            if (_face != null) _face.color = new Color(1f, 1f, 1f, alpha);
            if (_hour != null) _hour.color = new Color(_tint.r, _tint.g, _tint.b, _tint.a * alpha);
            if (_minute != null) _minute.color = new Color(_tint.r, _tint.g, _tint.b, _tint.a * alpha);
        }
    }
}
