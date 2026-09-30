using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Blot: every 1.6 s it squeezes (0.4 s telegraph, trembling) and leaps up to 3 cells at Elias along an arc (its
    /// shadow shrinks), leaving a 2×2 ink puddle for 5 s where it lands. Dying, it splits into two small blots.
    /// </summary>
    public sealed class BlotEnemy : EnemyBase
    {
        private enum State { Idle, Telegraph, Air }

        private State _state;
        private float _timer, _airT, _airTime;
        private Vector2 _from, _to;
        private bool _small;
        private Sprite _idle, _squash, _jump;
        private float _shadowBase;

        public override bool Grounded => _state != State.Air;

        public void MakeSmall() => _small = true;

        protected override void OnSpawned()
        {
            _idle = FloorSprites.Get(_small ? "F1_blot_small" : "F1_blot_idle");
            _squash = FloorSprites.Get(_small ? "F1_blot_small" : "F1_blot_squash");
            _jump = FloorSprites.Get(_small ? "F1_blot_small" : "F1_blot_jump");
            SetSprite(_idle);
            _timer = Spec.Num("interval", 1.6f) * Random.Range(0.5f, 1f);
            _state = State.Idle;
            _shadowBase = ShadowSr.transform.localScale.x;
        }

        protected override void Tick(float dt)
        {
            Desired = Vector2.zero;
            switch (_state)
            {
                case State.Idle:
                    _timer -= dt;
                    SetSprite(_idle, PlayerPos.x < Feet.x);
                    if (_timer <= 0f)
                    {
                        _state = State.Telegraph;
                        _timer = Spec.Num("telegraph", 0.4f);
                    }
                    break;
                case State.Telegraph:
                    _timer -= dt;
                    SetSprite(_squash, PlayerPos.x < Feet.x);
                    Body.transform.localPosition = (Vector3)(Random.insideUnitCircle * 0.04f);
                    if (_timer <= 0f) Jump();
                    break;
                case State.Air:
                    _airT += dt;
                    float k = Mathf.Clamp01(_airT / _airTime);
                    Vector2 p = Vector2.Lerp(_from, _to, k);
                    Rb.MovePosition(p);
                    VisualHeight = Mathf.Sin(k * Mathf.PI) * (_small ? 0.9f : 1.4f);
                    float sh = Mathf.Lerp(1f, 0.55f, Mathf.Sin(k * Mathf.PI));
                    ShadowSr.transform.localScale = new Vector3(_shadowBase * sh, ShadowSr.transform.localScale.y, 1f);
                    if (k >= 1f) Land();
                    break;
            }
        }

        protected override void FixedUpdate()
        {
            if (_state == State.Air) return;   // moved kinematically in the air
            base.FixedUpdate();
        }

        private void Jump()
        {
            Body.transform.localPosition = Vector3.zero;
            float max = _small ? Spec["small"].Num("jump", 2f) : Spec.Num("jump", 3f);
            Vector2 d = PlayerPos - Feet;
            if (d.magnitude > max) d = d.normalized * max;
            var target = Room.Frame.ClampToFloor(Feet + d, 0.3f);
            var cell = NearestWalkable(Room.Frame.CellAt(target));
            if (!Room.IsWalkableCell(Room.Frame.CellAt(target))) target = Room.Frame.CellCenter(cell);
            _from = Feet;
            _to = target;
            _airT = 0f;
            _airTime = Spec.Num("jumpTime", 0.55f) * (_small ? 0.8f : 1f);
            _state = State.Air;
            gameObject.layer = Layers.Airborne;
            SetSprite(_jump, d.x < 0f);
            Game.Audio?.Play("blot_jump", _small ? 0.7f : 1f);
        }

        private void Land()
        {
            _state = State.Idle;
            _timer = Spec.Num("interval", 1.6f);
            VisualHeight = 0f;
            ShadowSr.transform.localScale = new Vector3(_shadowBase, ShadowSr.transform.localScale.y, 1f);
            gameObject.layer = Layers.Enemy;
            SetSprite(_idle);
            Game.Audio?.Play("blot_land", _small ? 0.7f : 1f);
            Game.Fx?.Burst(Feet, FloorFx.Ink, _small ? 8 : 16, 3f, 0.12f, 0.4f);
            if (!_small)
            {
                var pz = Spec["puddle"];
                Room.AddPuddle(Feet, pz.Num("size", 2f), pz.Num("time", 5f));
                Game.Camera?.Shake(0.06f, 0.1f);
            }
        }

        protected override void OnDeath(bool red)
        {
            if (_small || red) return;
            var sm = Spec["small"];
            for (int i = 0; i < 2; i++)
            {
                Vector2 off = new Vector2(i == 0 ? -0.5f : 0.5f, 0f);
                var go = new GameObject("Blot (small)");
                var b = go.AddComponent<BlotEnemy>();
                b.MakeSmall();
                b.Setup("blot", Spec, Room, Room.Frame.ClampToFloor(Feet + off, 0.3f), false, sm.Num("hp", 3f), sm.Num("width", 0.6f));
            }
        }
    }
}
