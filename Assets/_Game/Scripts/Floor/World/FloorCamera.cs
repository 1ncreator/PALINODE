using System.Collections;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Room camera (from RogueDungeon's CameraController): a fixed orthographic size (an ordinary room fills the
    /// screen); in larger rooms it follows Elias, clamped to the painted background; slides between rooms; shakes.
    /// </summary>
    public sealed class FloorCamera : MonoBehaviour
    {
        private Camera _cam;
        private Rect _bounds;
        private Transform _target;
        private Vector2 _pos;
        private float _follow = 7f;
        private float _shakeAmp, _shakeDur, _shakeLeft;
        private Coroutine _slide;

        public Camera Cam => _cam;
        public bool Sliding => _slide != null;

        public void Init(Camera cam, float ortho, float follow, Color background)
        {
            _cam = cam;
            _cam.orthographic = true;
            _cam.orthographicSize = ortho;
            _ortho = ortho;
            _cam.backgroundColor = background;
            _follow = follow;
        }

        private float _ortho;

        public void SetRoom(Rect imageBounds, Transform target, bool snap, float ortho = -1f)
        {
            _bounds = imageBounds;
            _target = target;
            if (ortho > 0f) _ortho = ortho;
            if (snap && _ortho > 0f) _cam.orthographicSize = _ortho;
            if (snap) _pos = Clamp(_target != null ? (Vector2)_target.position : _bounds.center);
            Apply();
        }

        /// <summary>The camera always frames for 16:9 (batch mode renders with another window size).</summary>
        private float Aspect => Application.isBatchMode ? 16f / 9f : _cam.aspect;

        /// <summary>Where the camera would look in the given bounds (for slides).</summary>
        public Vector2 FocusFor(Rect bounds, Vector2 target)
        {
            var old = _bounds;
            _bounds = bounds;
            var p = Clamp(target);
            _bounds = old;
            return p;
        }

        private Vector2? _bias;
        private float _biasWeight;

        /// <summary>Pull the view toward a point (the boss) so both it and Elias stay on screen; null to clear.</summary>
        public void SetBias(Vector2? point, float weight)
        {
            _bias = point;
            _biasWeight = weight;
        }

        private Vector2 Clamp(Vector2 p)
        {
            if (_bias.HasValue) p = Vector2.Lerp(p, _bias.Value, _biasWeight);
            float h = _ortho > 0f ? _ortho : _cam.orthographicSize, w = h * Aspect;
            float x = _bounds.width <= w * 2f ? _bounds.center.x : Mathf.Clamp(p.x, _bounds.xMin + w, _bounds.xMax - w);
            float y = _bounds.height <= h * 2f ? _bounds.center.y : Mathf.Clamp(p.y + 0.6f, _bounds.yMin + h, _bounds.yMax - h);
            return new Vector2(x, y);
        }

        public IEnumerator Slide(Vector2 from, Vector2 to, float duration)
        {
            if (_slide != null) StopCoroutine(_slide);
            _slide = StartCoroutine(SlideRoutine(from, to, duration));
            yield return _slide;
        }

        private IEnumerator SlideRoutine(Vector2 from, Vector2 to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                _pos = Vector2.LerpUnclamped(from, to, k);
                Apply();
                yield return null;
            }
            _pos = to;
            _slide = null;
        }

        public void Shake(float amplitude, float duration)
        {
            if (amplitude >= _shakeAmp * (_shakeLeft / Mathf.Max(0.0001f, _shakeDur)))
            {
                _shakeAmp = amplitude;
                _shakeDur = duration;
                _shakeLeft = duration;
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            if (Application.isBatchMode) _cam.aspect = 16f / 9f;
            if (_ortho > 0f && !Mathf.Approximately(_cam.orthographicSize, _ortho))
                _cam.orthographicSize = Mathf.MoveTowards(_cam.orthographicSize, _ortho, Time.unscaledDeltaTime * 6f);
            if (_slide == null && _target != null)
            {
                var want = Clamp(_target.position);
                _pos = Vector2.Lerp(_pos, want, 1f - Mathf.Exp(-_follow * Time.unscaledDeltaTime));
            }
            if (_shakeLeft > 0f) _shakeLeft -= Time.unscaledDeltaTime;
            Apply();
        }

        private void Apply()
        {
            if (_cam == null) return;
            Vector2 off = Vector2.zero;
            if (_shakeLeft > 0f && _shakeDur > 0f) off = Random.insideUnitCircle * _shakeAmp * (_shakeLeft / _shakeDur);
            _cam.transform.position = new Vector3(_pos.x + off.x, _pos.y + off.y, -10f);
        }
    }
}
