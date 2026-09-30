using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Floor
{
    /// <summary>
    /// Headlights: a car-thing that lives in dark rooms, where only its two lamps show. It circles Elias, then its
    /// lamps flare (0.8 s, engine revving, a strip of light on the floor) and it rams straight across the room with
    /// screeching tyres; a near miss whitens the screen. After a dash it is exposed for 1.2 s (double damage).
    /// </summary>
    public sealed class HeadlightsEnemy : EnemyBase
    {
        private enum State { Circle, Telegraph, Dash, Exposed }

        private State _state;
        private float _timer;
        private Vector2 _dashDir;
        private float _dashLeft;
        private bool _nearMissed;
        private Sprite _full, _ghost;
        private SpriteRenderer _glowA, _glowB;
        private Light2D _lamp;
        private int _orbitSign = 1;
        private AudioSource _rev;

        protected override float DamageMultiplier(HitInfo hit) => _state == State.Exposed ? Spec.Num("vulnerableMul", 2f) : 1f;

        protected override void OnSpawned()
        {
            _full = FloorSprites.Get("F1_headlights_full");
            _ghost = FloorSprites.Get("F1_headlights_ghost");
            ContactDamage = Spec.Int("damage", 2);
            _orbitSign = Random.value < 0.5f ? 1 : -1;
            _glowA = MakeGlow(Visual, 0.34f, new Color(1f, 0.93f, 0.78f, 0.9f));
            _glowB = MakeGlow(Visual, 0.34f, new Color(1f, 0.93f, 0.78f, 0.9f));
            var lg = new GameObject("Lamp");
            lg.transform.SetParent(Visual, false);
            _lamp = Cutscene.StageLight.CreateLight(lg, Light2D.LightType.Point);
            _lamp.color = new Color(1f, 0.92f, 0.78f);
            _lamp.intensity = 0.9f;
            _lamp.pointLightOuterRadius = 2.2f;
            _lamp.pointLightInnerRadius = 0.2f;
            _lamp.falloffIntensity = 0.6f;
            _state = State.Circle;
            _timer = Random.Range(1.5f, 2.5f);
            UpdateLook(Vector2.right);
        }

        private void UpdateLook(Vector2 dir)
        {
            bool dark = Room != null && Room.IsDark && _state != State.Exposed;
            bool left = dir.x < 0f;
            SetSprite(dark ? _ghost : _full, left);
            // The lamps are painted at the front of the car, low on the right of the drawing (mirrored when facing
            // left): measured on the sheet as fractions of the sprite width from its centre.
            float fx = left ? -1f : 1f;
            _glowA.transform.localPosition = new Vector3(fx * Width * 0.115f, -Width * 0.21f, 0f);
            _glowB.transform.localPosition = new Vector3(fx * Width * 0.29f, -Width * 0.175f, 0f);
            _lamp.transform.localPosition = new Vector3(fx * Width * 0.45f, -Width * 0.2f, 0f);
            Body.color = dark ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
        }

        protected override void Tick(float dt)
        {
            Vector2 to = PlayerPos - Feet;
            switch (_state)
            {
                case State.Circle:
                {
                    float r = Spec.Num("orbit", 4.5f);
                    Vector2 n = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.right;
                    Vector2 tangent = new Vector2(-n.y, n.x) * _orbitSign;
                    float radial = (to.magnitude - r) * 0.8f;
                    Desired = (tangent + n * radial).normalized * Spec.Num("speed", 2.2f);
                    UpdateLook(Desired);
                    SetLamps(0.75f);
                    _timer -= dt;
                    if (_timer <= 0f) StartCoroutine(Charge());
                    break;
                }
                case State.Dash:
                    Desired = _dashDir * Spec.Num("dash", 14f);
                    _dashLeft -= Desired.magnitude * dt;
                    if (!_nearMissed && Game.Player != null && Vector2.Distance(PlayerPos, Feet) < Spec.Num("nearMiss", 1.2f))
                    {
                        _nearMissed = true;
                        Game.Ctrl.ScreenFlash(new Color(1f, 1f, 1f, 0.55f), 0.18f);
                    }
                    if (_dashLeft <= 0f || !Room.Frame.InsideFloor(Feet + _dashDir * 0.6f, 0.2f) || Rb.linearVelocity.magnitude < 1.5f && _timer < 0f)
                        EndDash();
                    _timer -= dt;
                    break;
                case State.Exposed:
                    Desired = Vector2.zero;
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _state = State.Circle;
                        _timer = Random.Range(Spec["cooldown"][0].AsFloat(1.2f), Spec["cooldown"][1].AsFloat(2.2f));
                        UpdateLook(to);
                    }
                    break;
                default:
                    Desired = Vector2.zero;
                    break;
            }
        }

        private void SetLamps(float k)
        {
            _glowA.color = new Color(1f, 0.9f, 0.7f, Mathf.Clamp01(0.35f * k + 0.2f));
            _glowB.color = _glowA.color;
            _lamp.intensity = 0.5f + 1.6f * k;
        }

        private IEnumerator Charge()
        {
            _state = State.Telegraph;
            Desired = Vector2.zero;
            Vector2 dir = DirToPlayer();
            UpdateLook(dir);
            _rev = Game.Audio?.Play("headlights_rev");
            // A strip of light on the floor where it is going to run.
            var strip = FloorSprites.Quad(Room.DecalRoot, "Beam", new Color(1f, 0.92f, 0.75f, 0f), new Vector2(9f, 0.9f),
                FloorSprites.FloorDecalOrder + 700, FloorSprites.Glow);
            strip.transform.position = Feet + dir * 4.8f;
            strip.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            float tel = Spec.Num("telegraph", 0.8f), t = 0f;
            while (t < tel)
            {
                if (!Alive) { Destroy(strip.gameObject); yield break; }
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / tel);
                SetLamps(1f + k);
                strip.color = new Color(1f, 0.92f, 0.75f, 0.35f * k);
                Visual.localPosition = (Vector3)(Random.insideUnitCircle * 0.03f * k);
                yield return null;
            }
            Destroy(strip.gameObject);
            _dashDir = dir;
            _dashLeft = 22f;
            _nearMissed = false;
            _timer = 0.25f;
            _state = State.Dash;
            gameObject.layer = Layers.Enemy;
            Game.Audio?.Play("headlights_screech");
        }

        private void EndDash()
        {
            _state = State.Exposed;
            _timer = Spec.Num("vulnerable", 1.2f);
            Desired = Vector2.zero;
            SetLamps(0.2f);
            UpdateLook(_dashDir);
            Game.Camera?.Shake(0.12f, 0.15f);
        }

        protected override void OnDeath(bool red)
        {
            if (_rev != null) Game.Audio?.Stop(_rev, 0.1f);
            _lamp.enabled = false;
            _glowA.enabled = _glowB.enabled = false;
        }
    }
}
