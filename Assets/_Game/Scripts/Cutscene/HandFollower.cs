using Palinode.UI;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// Arm rig for a hand-with-tool layer whose sprite pivot is the tool tip (see art_processing.json "pivot": "tip").
    /// The tip is placed exactly on the target; the hand rotates around an elbow outside the frame (the sprite's
    /// sleeve is extended past the picture edge, so it always leaves the camera frame). The elbow slides after the
    /// hand only partially, which gives the forearm swing of real writing.
    /// Also: lift between words (hand grows toward the camera, shadow separates), a contact shadow that tightens
    /// on touch, a small press "dip" when the tip lands, and a slight roll toward the stroke direction.
    /// Runs before Stage.LateUpdate so the stage applies the pose the same frame.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class HandFollower : MonoBehaviour
    {
        private StageLayer _layer;
        private Vector2 _tipPx;
        private Vector2 _elbowPx;
        private bool _hasElbow;

        private Vector3 _target, _current, _velocity;
        private bool _hasTarget;
        private bool _restCaptured;
        private Vector2 _restTip;       // parent-local
        private Vector2 _restElbow;     // parent-local
        private float _restRot;
        private Vector2 _elbow;         // parent-local, sliding
        private float _baseScale = 1f;

        private float _lift = 1f, _liftTarget = 1f;
        private float _press;
        private float _roll;
        private Vector2 _enterOffset;
        private float _enterT = 1f, _enterDuration;

        private SpriteRenderer _shadow;
        private SpriteRenderer _contact;

        public bool Writing { get; set; }
        public float Smooth { get; set; } = 0.02f;
        public float TremorPx { get; set; } = 0.6f;
        public float ElbowFollowX { get; set; } = 0.3f;
        public float ElbowFollowY { get; set; } = 0.15f;
        public float MaxSwingDeg { get; set; } = 22f;
        public float LiftPx { get; set; } = 10f;
        public float LiftScale { get; set; } = 0.035f;
        public Vector2 ShadowDir { get; set; } = new Vector2(0.6f, -0.8f);
        public Vector2 TipPx => _tipPx;

        /// <summary>0 = nib on paper, 1 = lifted.</summary>
        public float Lift => _lift;

        public void Init(StageLayer layer, Vector2 tipPx, Vector2? elbowPx = null)
        {
            _layer = layer;
            _tipPx = tipPx;
            if (elbowPx.HasValue) { _elbowPx = elbowPx.Value; _hasElbow = true; }
        }

        /// <summary>Configure shadows (call after the layer's renderer exists).</summary>
        public void BuildShadows(Material unlit, float shadowAlpha)
        {
            var r = _layer != null ? _layer.Renderer : null;
            if (r == null) return;
            var go = new GameObject("HandShadow");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _shadow = go.AddComponent<SpriteRenderer>();
            _shadow.sprite = r.sprite;
            _shadow.sharedMaterial = unlit;
            _shadow.color = new Color(0f, 0f, 0f, shadowAlpha);
            _shadow.sortingOrder = r.sortingOrder - 1;

            var c = new GameObject("ContactShadow");
            c.layer = gameObject.layer;
            c.transform.SetParent(transform, false);
            _contact = c.AddComponent<SpriteRenderer>();
            _contact.sprite = ProceduralSprites.SoftDot(64, 0.15f);
            _contact.sharedMaterial = unlit;
            _contact.color = new Color(0f, 0f, 0f, 0.4f);
            _contact.sortingOrder = r.sortingOrder - 1;
        }

        /// <summary>Start the shot with the hand outside the frame, sliding in along the forearm.</summary>
        public void EnterFrom(Vector2 offsetParentLocal, float duration)
        {
            _enterOffset = offsetParentLocal;
            _enterDuration = Mathf.Max(0.01f, duration);
            _enterT = 0f;
        }

        public void SetContact(bool touching)
        {
            float next = touching ? 0f : 1f;
            if (touching && _liftTarget > 0.5f) _press = 1f; // lands → small dip
            _liftTarget = next;
        }

        public void SetTarget(Vector3 worldTarget, bool snap = false)
        {
            _target = worldTarget;
            if (snap || !_hasTarget)
            {
                _current = worldTarget;
                _velocity = Vector3.zero;
            }
            _hasTarget = true;
        }

        public Vector3 CurrentTip => _hasTarget ? _current : transform.position;

        private void CaptureRest()
        {
            _restCaptured = true;
            _restTip = _layer.BasePos;
            _restRot = _layer.BaseRot;
            _baseScale = _layer.BaseScale.x;
            // Elbow in parent-local space: sprite px → layer local → parent local (layer local == px relative to pivot).
            Vector2 elbowLocal = _hasElbow ? _layer.PixelToLocal(_elbowPx) : new Vector2(8f, -2f);
            Vector2 scaled = Vector2.Scale(elbowLocal, _layer.BaseScale);
            _restElbow = _restTip + (Vector2)(Quaternion.Euler(0f, 0f, _restRot) * scaled);
            _elbow = _restElbow;
        }

        private void LateUpdate()
        {
            if (_layer == null || _layer.Parent == null) return;
            if (!_restCaptured) CaptureRest();
            float dt = Time.deltaTime;
            var parent = transform.parent;

            _lift = Mathf.MoveTowards(_lift, _liftTarget, dt * (_liftTarget > _lift ? 7f : 10f));
            _press = Mathf.MoveTowards(_press, 0f, dt / 0.14f);

            Vector2 tip;
            if (_hasTarget)
            {
                _current = Vector3.SmoothDamp(_current, _target, ref _velocity, Smooth, Mathf.Infinity, dt);
                tip = parent.InverseTransformPoint(_current);
            }
            else tip = _restTip;

            float pxUnit = 0.01f; // parent (image) px → parent-local units
            if (Writing)
            {
                float t = Time.time;
                tip += new Vector2(Mathf.Sin(t * 19f) * 0.6f, Mathf.Sin(t * 13f + 1.3f)) * TremorPx * pxUnit;
            }

            // Enter from outside the frame.
            if (_enterT < 1f)
            {
                _enterT = Mathf.Min(1f, _enterT + dt / _enterDuration);
                float k = 1f - Palinode.Core.Ease.Apply("outCubic", _enterT);
                tip += _enterOffset * k;
            }

            // Lift: the hand rises toward the camera → moves slightly up on the page and grows.
            float liftAmount = _lift * (1f - _press * 0.5f);
            Vector2 lifted = tip + new Vector2(0f, LiftPx * pxUnit * liftAmount);
            lifted.y -= _press * 2f * pxUnit;

            // Elbow slides partially with the hand; forearm direction = elbow − tip.
            Vector2 delta = tip - _restTip;
            Vector2 elbowGoal = _restElbow + new Vector2(delta.x * ElbowFollowX, delta.y * ElbowFollowY);
            _elbow = Vector2.Lerp(_elbow, elbowGoal, 1f - Mathf.Exp(-dt * 3f));
            Vector2 restDir = _restElbow - _restTip;
            Vector2 dir = _elbow - lifted;
            float swing = Vector2.SignedAngle(restDir, dir);
            swing = Mathf.Clamp(swing, -MaxSwingDeg, MaxSwingDeg);

            // Roll toward the stroke direction.
            float vx = _velocity.x / Mathf.Max(0.0001f, parent.lossyScale.x);
            _roll = Mathf.Lerp(_roll, Mathf.Clamp(-vx * 1.5f, -4f, 4f), 1f - Mathf.Exp(-dt * 8f));

            _layer.BasePos = lifted - _layer.Offset;
            _layer.BaseRot = _restRot + swing + _roll;
            float s = _baseScale * (1f + LiftScale * liftAmount - 0.012f * _press);
            _layer.BaseScale = new Vector2(s, s);

            // Shadows (in the layer's own space; the layer pivot is the tip).
            if (_shadow != null)
            {
                Vector2 off = ShadowDir.normalized * (4f + 22f * liftAmount) * pxUnit / Mathf.Max(0.0001f, s);
                off = Quaternion.Euler(0f, 0f, -_layer.BaseRot) * off;
                _shadow.transform.localPosition = new Vector3(off.x, off.y, 0f);
                _shadow.transform.localScale = Vector3.one * (1f + 0.02f * liftAmount);
                var c = _shadow.color;
                c.a = Mathf.Lerp(0.34f, 0.18f, liftAmount) * (_layer.Renderer != null ? _layer.Renderer.color.a : 1f);
                _shadow.color = c;
                _shadow.sprite = _layer.Renderer != null ? _layer.Renderer.sprite : _shadow.sprite;
            }
            if (_contact != null)
            {
                Vector2 off = ShadowDir.normalized * (1.5f + 16f * liftAmount) * pxUnit / Mathf.Max(0.0001f, s);
                off = Quaternion.Euler(0f, 0f, -_layer.BaseRot) * off;
                _contact.transform.localPosition = new Vector3(off.x, off.y - LiftPx * pxUnit * liftAmount / s, 0f);
                float size = Mathf.Lerp(7f, 22f, liftAmount) * pxUnit / Mathf.Max(0.0001f, s);
                _contact.transform.localScale = new Vector3(size * 1.6f, size, 1f);
                var c = _contact.color;
                c.a = Mathf.Lerp(0.55f, 0.15f, liftAmount) * (_layer.Renderer != null ? _layer.Renderer.color.a : 1f);
                _contact.color = c;
            }
        }
    }
}
