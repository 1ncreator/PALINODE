using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Palinode.Gameplay
{
    /// <summary>
    /// Top-down walker for Elias with frame-by-frame walk cycles (down / up / right; left = mirrored right).
    /// The cycle position is driven by the distance actually travelled (cyclePx per full cycle), so the feet do not
    /// slide at any speed; neighbouring frames are blended to smooth the drawn cycle. Footsteps fire on the frames where
    /// a foot is planted. Standing still: the "idle" frame of the facing with breathing and a slow weight sway.
    /// Supports an autopilot (tests / attract mode).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerController : MonoBehaviour
    {
        public enum Facing { Down, Up, Right, Left }

        /// <summary>One direction of the walk cycle.</summary>
        public sealed class WalkSet
        {
            public Sprite[] Frames;
            public int Idle;
            public int[] Plants = { 0, 4 };
        }

        private Rigidbody2D _rb;
        private SpriteRenderer _body;
        private SpriteRenderer _blend;
        private SpriteRenderer _reflection;
        private Transform _visual;
        private readonly Dictionary<Facing, WalkSet> _sets = new Dictionary<Facing, WalkSet>();
        private float _speed = 1.2f;
        private float _cycleUnits = 1.2f;      // world units travelled per full cycle (2 steps)
        private float _blendAmount = 0.55f;
        private FootstepPlayer _footsteps;
        private Light2D _flashlight;
        private Light2D _aura;

        private InputAction _move;
        private InputAction _interact;

        private Vector2 _velocity;
        private float _cycle;                  // 0..frameCount
        private float _moveBlend;
        private float _flashAngle = -90f;
        private float _baseScale = 1f;
        private float _autoBest = float.MaxValue;
        private float _autoStuck;

        private SpriteRenderer _turnFade;
        private float _turnT = 1f, _turnDuration = 0.3f, _turnDir;

        public bool ControlEnabled { get; set; } = true;
        public Facing CurrentFacing { get; private set; } = Facing.Right;
        public Vector2 FeetPosition => _rb != null ? _rb.position : (Vector2)transform.position;
        public bool IsMoving => _moveBlend > 0.2f;
        public float Speed => _speed;
        public int CurrentFrame { get; private set; }

        /// <summary>Autopilot waypoints (world). When set and control is enabled, input is ignored.</summary>
        public readonly List<Vector2> AutoPath = new List<Vector2>();
        public bool AutoPilot { get; set; }

        public bool InteractPressed => _interact != null && _interact.WasPressedThisFrame();

        public void Init(WalkSet down, WalkSet up, WalkSet right, float scale, float speed, float cycleUnits, float blend,
            FootstepPlayer footsteps, bool reflection, int sortingBase)
        {
            _sets[Facing.Down] = down;
            _sets[Facing.Up] = up;
            _sets[Facing.Right] = right;
            _sets[Facing.Left] = right;
            _speed = speed;
            _cycleUnits = Mathf.Max(0.05f, cycleUnits);
            _blendAmount = blend;
            _footsteps = footsteps;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            _visual.localScale = Vector3.one * scale;
            _baseScale = scale;
            _body = _visual.gameObject.AddComponent<SpriteRenderer>();
            _body.sortingOrder = sortingBase;
            var b = new GameObject("FrameBlend");
            b.transform.SetParent(_visual, false);
            _blend = b.AddComponent<SpriteRenderer>();

            if (reflection)
            {
                var r = new GameObject("Reflection").transform;
                r.SetParent(transform, false);
                r.localScale = new Vector3(scale, -scale, 1f);
                r.localPosition = new Vector3(0f, -0.02f, 0f);
                _reflection = r.gameObject.AddComponent<SpriteRenderer>();
                _reflection.color = new Color(0.55f, 0.6f, 0.75f, 0.16f);
                _reflection.sortingOrder = -30000 + 10;
            }

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");
            _move.AddBinding("<Gamepad>/dpad");
            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonSouth");
            _move.Enable();
            _interact.Enable();
            _cycle = _sets[CurrentFacing].Idle;
            ApplyFrame(0f);
        }

        public void AddFlashlight(Light2D spot, Light2D aura)
        {
            _flashlight = spot;
            _aura = aura;
        }

        public void SetMaterial(Material m)
        {
            if (m == null) return;
            _body.sharedMaterial = m;
            _blend.sharedMaterial = m;
            if (_reflection != null) _reflection.sharedMaterial = m;
        }

        public void Face(Facing f)
        {
            CurrentFacing = f;
            _cycle = _sets[f].Idle;
            ApplyFrame(0f);
        }

        /// <summary>Turn to a facing smoothly: the new view fades in over the old one with a small weight shift.</summary>
        public void TurnTo(Facing f, float duration)
        {
            if (f == CurrentFacing) return;
            if (_turnFade == null)
            {
                var go = new GameObject("TurnFade");
                go.transform.SetParent(_visual, false);
                _turnFade = go.AddComponent<SpriteRenderer>();
                _turnFade.sharedMaterial = _body.sharedMaterial;
            }
            _turnFade.sprite = _body.sprite;
            _turnFade.flipX = _body.flipX;
            _turnT = 0f;
            _turnDuration = Mathf.Max(0.05f, duration);
            _turnDir = f == Facing.Left || (CurrentFacing == Facing.Right && f != Facing.Right) ? 1f : -1f;
            CurrentFacing = f;
            _cycle = _sets[f].Idle;
            ApplyFrame(0f);
        }

        public void Teleport(Vector2 p)
        {
            _rb.position = p;
            transform.position = p;
        }

        private void OnDestroy()
        {
            _move?.Dispose();
            _interact?.Dispose();
        }

        private Vector2 ReadInput()
        {
            if (!ControlEnabled) return Vector2.zero;
            if (AutoPilot)
            {
                while (AutoPath.Count > 0 && Vector2.Distance(AutoPath[0], FeetPosition) < 0.12f)
                {
                    AutoPath.RemoveAt(0);
                    _autoBest = float.MaxValue;
                    _autoStuck = 0f;
                }
                if (AutoPath.Count == 0) return Vector2.zero;
                // Blocked by a collider for a while → give up on this waypoint.
                float d = Vector2.Distance(AutoPath[0], FeetPosition);
                if (d < _autoBest - 0.02f) { _autoBest = d; _autoStuck = 0f; }
                else if ((_autoStuck += Time.deltaTime) > 1.5f)
                {
                    Debug.LogWarning($"[PALINODE] Autopilot blocked at {FeetPosition}, skipping waypoint {AutoPath[0]}");
                    AutoPath.RemoveAt(0);
                    _autoBest = float.MaxValue;
                    _autoStuck = 0f;
                    if (AutoPath.Count == 0) return Vector2.zero;
                }
                return (AutoPath[0] - FeetPosition).normalized;
            }
            Vector2 v = _move.ReadValue<Vector2>();
            if (v.sqrMagnitude > 1f) v.Normalize();
            return v.sqrMagnitude < 0.02f ? Vector2.zero : v;
        }

        private void Update()
        {
            Vector2 input = ReadInput();
            _velocity = input * _speed;
            float target = input.sqrMagnitude > 0.01f ? 1f : 0f;
            _moveBlend = Mathf.MoveTowards(_moveBlend, target, Time.deltaTime * 6f);

            if (input.sqrMagnitude > 0.01f)
            {
                var want = Mathf.Abs(input.x) > Mathf.Abs(input.y) * 0.9f
                    ? (input.x > 0 ? Facing.Right : Facing.Left)
                    : (input.y > 0 ? Facing.Up : Facing.Down);
                if (want != CurrentFacing)
                {
                    // Keep the gait phase when changing direction mid-walk (both cycles start on the same foot).
                    float phase = _cycle / Mathf.Max(1, Set.Frames.Length);
                    CurrentFacing = want;
                    _cycle = phase * Set.Frames.Length;
                }
            }

            var set = Set;
            int n = set.Frames.Length;
            float blendK = 0f;
            if (target > 0f && _rb != null)
            {
                // Cycle advances with the distance actually covered (not the input), so collisions don't make feet slide.
                float moved = _rb.linearVelocity.magnitude * Time.deltaTime;
                if (moved < 1e-5f) moved = _velocity.magnitude * Time.deltaTime * 0.25f;
                float prev = _cycle;
                _cycle = Mathf.Repeat(_cycle + moved / _cycleUnits * n, n);
                foreach (int p in set.Plants)
                    if (Crossed(prev, _cycle, p + 0.5f, n)) _footsteps?.PlayStep();
                blendK = Mathf.SmoothStep(0f, 1f, (_cycle - Mathf.Floor(_cycle) - 0.55f) / 0.45f) * _blendAmount;   // only around the frame change
            }
            else
            {
                // Settle on the idle frame (legs together).
                _cycle = set.Idle;
            }
            ApplyFrame(blendK);

            // Idle: slow breathing and a slight sway of the weight when standing.
            float idle = 1f - _moveBlend;
            float breath = Mathf.Sin(Time.time * 2f * Mathf.PI / 3.6f);
            float sway = Mathf.Sin(Time.time * 2f * Mathf.PI / 5.3f + 1.1f);
            float squash = 1f + breath * 0.008f * idle;
            float tilt = sway * 0.6f * idle;

            if (_turnT < 1f)
            {
                _turnT = Mathf.Min(1f, _turnT + Time.deltaTime / _turnDuration);
                float k = Mathf.SmoothStep(0f, 1f, _turnT);
                float mid = Mathf.Sin(_turnT * Mathf.PI);
                squash *= 1f - 0.03f * mid;
                tilt += _turnDir * 4f * mid;
                _body.color = new Color(1f, 1f, 1f, k);
                _turnFade.color = new Color(1f, 1f, 1f, 1f - k);
                _turnFade.sortingOrder = _body.sortingOrder - 1;
                _turnFade.enabled = _turnT < 1f;
            }
            else if (_turnFade != null && _turnFade.enabled)
            {
                _turnFade.enabled = false;
                _body.color = Color.white;
            }

            _visual.localRotation = Quaternion.Euler(0f, 0f, tilt);
            _visual.localScale = new Vector3(_baseScale, _baseScale * squash, 1f);

            _body.sortingOrder = SortingOrderFor(transform.position.y);
            _blend.sortingOrder = _body.sortingOrder + 1;

            if (_flashlight != null)
            {
                float want = CurrentFacing switch { Facing.Up => 90f, Facing.Down => -90f, Facing.Left => 180f, _ => 0f };
                _flashAngle = Mathf.LerpAngle(_flashAngle, want, Time.deltaTime * 6f);
                _flashlight.transform.localRotation = Quaternion.Euler(0f, 0f, _flashAngle - 90f);
                _flashlight.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            }
        }

        private WalkSet Set => _sets[CurrentFacing];

        private static bool Crossed(float prev, float cur, float mark, int n)
        {
            if (cur >= prev) return prev < mark && cur >= mark;
            return prev < mark || cur >= mark; // wrapped
        }

        public static int SortingOrderFor(float worldY) => Mathf.Clamp(5000 - Mathf.RoundToInt(worldY * 100f), -20000, 20000);

        private void FixedUpdate()
        {
            if (_rb != null) _rb.linearVelocity = _velocity;
        }

        private void ApplyFrame(float blendK)
        {
            if (_body == null) return;
            var set = Set;
            int n = set.Frames.Length;
            int i = Mathf.FloorToInt(_cycle) % n;
            bool flip = CurrentFacing == Facing.Left;
            CurrentFrame = i;
            _body.sprite = set.Frames[i];
            _body.flipX = flip;
            _blend.sprite = set.Frames[(i + 1) % n];
            _blend.flipX = flip;
            _blend.enabled = blendK > 0.02f;
            _blend.color = new Color(1f, 1f, 1f, blendK * (_turnT < 1f ? 0f : 1f));
            if (_reflection != null)
            {
                _reflection.sprite = set.Frames[i];
                _reflection.flipX = flip;
            }
        }
    }
}
