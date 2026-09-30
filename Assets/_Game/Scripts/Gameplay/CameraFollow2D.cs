using UnityEngine;

namespace Palinode.Gameplay
{
    /// <summary>Smoothly follows a target and keeps the orthographic view inside the map bounds.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        private Camera _cam;
        private Transform _target;
        private Rect _bounds;
        private Vector3 _velocity;
        private Vector2 _lookAhead;

        public Vector2 Offset { get; set; }
        public float SmoothTime { get; set; } = 0.35f;
        public Vector2 ShakeOffset { get; set; }

        public void Init(Transform target, Rect bounds, float orthoSize)
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = orthoSize;
            _target = target;
            _bounds = bounds;
            Snap();
        }

        public void SetTarget(Transform t) => _target = t;

        public void SetSize(float size) => _cam.orthographicSize = size;

        public void Snap()
        {
            if (_target == null) return;
            Vector3 p = Clamp((Vector2)_target.position + Offset);
            transform.position = new Vector3(p.x, p.y, transform.position.z);
            _velocity = Vector3.zero;
        }

        private Vector2 Clamp(Vector2 p)
        {
            float h = _cam.orthographicSize;
            float w = h * _cam.aspect;
            float minX = _bounds.xMin + w, maxX = _bounds.xMax - w;
            float minY = _bounds.yMin + h, maxY = _bounds.yMax - h;
            p.x = minX > maxX ? _bounds.center.x : Mathf.Clamp(p.x, minX, maxX);
            p.y = minY > maxY ? _bounds.center.y : Mathf.Clamp(p.y, minY, maxY);
            return p;
        }

        private void LateUpdate()
        {
            if (_target == null || _cam == null) return;
            var body = _target.GetComponent<Rigidbody2D>();
            Vector2 vel = body != null ? body.linearVelocity : Vector2.zero;
            _lookAhead = Vector2.Lerp(_lookAhead, vel * 0.35f, Time.deltaTime * 2f);
            Vector2 goal = Clamp((Vector2)_target.position + Offset + _lookAhead);
            Vector3 cur = transform.position - (Vector3)ShakeOffset;
            Vector3 next = Vector3.SmoothDamp(cur, new Vector3(goal.x, goal.y, cur.z), ref _velocity, SmoothTime);
            transform.position = next + (Vector3)ShakeOffset;
        }
    }
}
