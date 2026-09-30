using System.Collections.Generic;
using Palinode.Core;
using Palinode.Gameplay;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Elias on Floor I: inertial 8-way walking (walk cycles driven by distance, as in the prologue), 4-way ink shots
    /// with a shooting pose, ink-drop health, the blotter, the red pencil's automatic save. Records his movement and
    /// shots for the boss's ink copies.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Elias : MonoBehaviour
    {
        public struct Frame
        {
            public float Time;
            public Vector2 Pos;
            public PlayerController.Facing Facing;
            public bool Moving;
            public bool Shot;
            public Direction ShotDir;
        }

        private sealed class WalkSet
        {
            public Sprite[] Frames;
            public int Idle;
            public int[] Plants;
        }

        private const float BlinkRate = 14f;

        private Rigidbody2D _rb;
        private Transform _visual;
        private SpriteRenderer _body, _blend, _flash, _shadow;
        private FootstepPlayer _steps;
        private readonly Dictionary<PlayerController.Facing, WalkSet> _walk = new Dictionary<PlayerController.Facing, WalkSet>();
        private readonly Dictionary<Direction, Sprite> _shootPose = new Dictionary<Direction, Sprite>();
        private Sprite _pickupPose, _fallen;
        private float _walkScale, _poseScale, _pickupScale, _fallenScale;
        private float _cycleUnits, _blendAmount;
        private float _cycle, _moveBlend;
        private Vector2 _vel, _knock;
        private float _nextShot, _poseUntil, _flashUntil;
        private Direction _poseDir;
        private int _shotCount;
        private bool _altSide;
        private float _pickupUntil;
        private SpriteRenderer _pickupIcon;
        private int _absorbLeft;
        private float _microTimer;
        private JNode _p;

        public readonly FloorInput Input = new FloorInput();
        public InkHealth Health { get; private set; }
        public PlayerStats Stats { get; private set; }
        public PlayerController.Facing Facing { get; private set; } = PlayerController.Facing.Down;
        public bool ControlLocked { get; private set; }
        public bool IsAlive => Health != null && !Health.IsDead;
        public Vector2 Position => _rb != null ? _rb.position : (Vector2)transform.position;
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;
        public float Radius { get; private set; } = 0.3f;
        public float WalkScale => _walkScale;
        public Sprite IdleSprite(PlayerController.Facing f) => _walk[f].Frames[_walk[f].Idle];
        public Sprite ShootSprite(Direction d) => _shootPose.TryGetValue(d, out var s) ? s : null;
        public float PoseScale => _poseScale;

        /// <summary>Last ~4 s of movement and shots (the boss's ink copies replay it with a delay).</summary>
        public readonly List<Frame> History = new List<Frame>();

        public void Init(JNode player)
        {
            _p = player;
            gameObject.layer = Layers.Player;
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Radius = player.Num("radius", 0.3f);
            var col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = Radius;

            var walk = player["walk"];
            _blendAmount = walk.Num("blend", 0.5f);
            _walk[PlayerController.Facing.Down] = Set(walk["down"]);
            _walk[PlayerController.Facing.Up] = Set(walk["up"]);
            _walk[PlayerController.Facing.Right] = Set(walk["right"]);
            _walk[PlayerController.Facing.Left] = _walk[PlayerController.Facing.Right];
            _cycleUnits = player.Num("cycle", 1.35f);

            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            var idle = IdleSprite(PlayerController.Facing.Down);
            _walkScale = FloorSprites.ScaleFor(idle, player.Num("width", 1.05f));
            float bodyH = idle != null ? idle.bounds.size.y * _walkScale : 2f;
            _body = FloorSprites.Make(_visual, "Body", null, 0f, 0);
            _blend = FloorSprites.Make(_visual, "Blend", null, 0f, 0);
            _flash = FloorSprites.Make(_visual, "Flash", null, 0f, 0, FloorSprites.Solid);
            _flash.enabled = false;
            _shadow = FloorSprites.Shadow(transform, 0.9f, 0.4f);

            var shoot = player["shoot"];
            _shootPose[Direction.Down] = FloorSprites.Get(shoot.Str("down"));
            _shootPose[Direction.Up] = FloorSprites.Get(shoot.Str("up"));
            _shootPose[Direction.Left] = FloorSprites.Get(shoot.Str("left"));
            _shootPose[Direction.Right] = FloorSprites.Get(shoot.Str("right"));
            // Poses are separate drawings with their own margins: size them by the figure's height on screen.
            _poseScale = FloorSprites.HeightScale(_shootPose[Direction.Down], player.Num("poseHeight", 2.15f));
            _pickupPose = FloorSprites.Get("F1_elias_pickup");
            _pickupScale = FloorSprites.HeightScale(_pickupPose, player.Num("pickupHeight", 2.45f));
            _fallen = FloorSprites.Get("F1_elias_fallen");
            _fallenScale = FloorSprites.ScaleFor(_fallen, player.Num("fallenWidth", 2.3f));

            _steps = gameObject.AddComponent<FootstepPlayer>();
            if (FloorSprites.Config != null)
                _steps.Init(FloorSprites.Config.Clip(player.Str("footsteps", "SFX_footsteps_wet")), player.Num("footstepVolume", 0.2f));

            Health = new InkHealth(player.Int("halves", 6), player.Int("maxDrops", 12), player.Num("iframes", 1f));
            Health.GodMode = Game.GodMode;
            RecomputeStats();
            _cycle = _walk[Facing].Idle;
            ApplyFrame(0f);
        }

        private static WalkSet Set(JNode w)
        {
            string prefix = w.Str("prefix", "G_walk_down_");
            int count = w.Int("count", 8);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++) frames[i] = FloorSprites.Get(prefix + (i + 1).ToString("00"));
            var plants = new List<int>();
            foreach (var p in w["plants"].Items()) plants.Add(p.AsInt());
            return new WalkSet { Frames = frames, Idle = w.Int("idle", 0), Plants = plants.Count > 0 ? plants.ToArray() : new[] { 0, count / 2 } };
        }

        public void RecomputeStats()
        {
            Stats = PlayerStats.Compute(_p, Game.DB.Data["items"], Game.Run.Items);
            _microTimer = Stats.MicroSleepEvery;
        }

        public void SetControlLocked(bool locked)
        {
            ControlLocked = locked;
            if (!locked) return;
            Input.Clear();
            _vel = Vector2.zero;
            _knock = Vector2.zero;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
        }

        public void Teleport(Vector2 p)
        {
            _rb.position = p;
            transform.position = p;
            _rb.linearVelocity = Vector2.zero;
            _vel = _knock = Vector2.zero;
            History.Clear();
        }

        public void FaceTo(PlayerController.Facing f)
        {
            Facing = f;
            _cycle = _walk[f].Idle;
            ApplyFrame(0f);
        }

        /// <summary>A new room: the blotter recharges.</summary>
        public void OnRoomEntered() => _absorbLeft = Stats.AbsorbPerRoom;

        // ------------------------------------------------------------------ damage

        /// <summary>Hit by an enemy / shot. Returns true if the hit landed (or was absorbed).</summary>
        public bool Hurt(int halves, Vector2 dir, string killer)
        {
            if (!IsAlive || ControlLocked && !Game.Ctrl.AllowHurtWhileLocked) return false;
            float now = Time.time;
            if (Health.IsInvulnerable(now)) return false;

            if (_absorbLeft > 0)
            {
                _absorbLeft--;
                Health.GrantInvulnerability(now, Health.Invulnerability);
                Game.Fx?.Burst(Position + Vector2.up * 0.8f, new Color(0.85f, 0.55f, 0.6f), 12, 2.5f, 0.12f, 0.4f);
                Game.Audio?.Play("ink_splat");
                Flash(new Color(0.9f, 0.6f, 0.65f), 0.15f);
                return true;
            }

            if (Health.WouldKill(halves, now) && Game.Ctrl.TryPencilSave())
            {
                Health.Set(Mathf.Max(1, Game.DB.Data["redPencil"].Int("reviveHalves", 2)));
                Health.GrantInvulnerability(now, 1.6f);
                return true;
            }

            if (!Health.TryDamage(halves, now)) return false;
            _knock = (dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.zero) * _p.Num("knockback", 5f);
            Flash(new Color(0.8f, 0.1f, 0.1f), 0.12f);
            Game.Audio?.Play("player_hurt");
            Game.Fx?.Burst(Position + Vector2.up * 0.9f, FloorFx.Ink, 14, 4f, 0.12f, 0.45f);
            Game.Camera?.Shake(0.22f, 0.25f);
            Game.Ctrl.HitStop(0.05f);
            if (Health.IsDead) Game.Ctrl.OnEliasDied(killer);
            return true;
        }

        public void Flash(Color c, float duration)
        {
            _flash.color = c;
            _flashUntil = Time.time + duration;
        }

        // ------------------------------------------------------------------ poses

        public void ShowPickup(Sprite icon, float duration = 1.6f)
        {
            _pickupUntil = Time.time + duration;
            if (_pickupIcon == null)
            {
                _pickupIcon = FloorSprites.Make(transform, "PickupIcon", null, 0f, 0);
            }
            _pickupIcon.sprite = icon;
            _pickupIcon.transform.localScale = Vector3.one * FloorSprites.FitScale(icon, 0.75f);
            _pickupIcon.transform.localPosition = new Vector3(0f, _p.Num("pickupHeight", 2.45f) + 0.35f, 0f);
            _pickupIcon.enabled = icon != null;
        }

        public void ShowFallen(bool on)
        {
            _fallenOn = on;
            ApplyFrame(0f);
        }

        private bool _fallenOn;

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (Health == null) return;
            if (!IsAlive || _fallenOn)
            {
                ApplyFrame(0f);
                return;
            }
            float now = Time.time;
            bool inv = Health.IsInvulnerable(now) && !Health.GodMode;
            bool blinkOff = inv && Mathf.FloorToInt(now * BlinkRate) % 2 == 1;
            _body.enabled = !blinkOff;
            _blend.enabled = _blend.enabled && !blinkOff;

            if (!ControlLocked && Time.timeScale > 0f)
            {
                Input.Update();
                if (Input.IsShooting && now >= _nextShot) Shoot(Input.ShootDirection);
                if (Input.ActivePressed) Game.Ctrl.UsePencil();
                if (Stats.MicroSleepEvery > 0f)
                {
                    _microTimer -= Time.deltaTime;
                    if (_microTimer <= 0f)
                    {
                        _microTimer = Stats.MicroSleepEvery * Random.Range(0.85f, 1.15f);
                        Game.Ctrl.MicroSleep(Stats.MicroSleep);
                    }
                }
            }
            Animate();

            if (_pickupIcon != null) _pickupIcon.enabled = now < _pickupUntil && _pickupIcon.sprite != null;

            // History for ink copies.
            History.Add(new Frame { Time = now, Pos = Position, Facing = Facing, Moving = _moveBlend > 0.3f });
            while (History.Count > 0 && now - History[0].Time > 4.5f) History.RemoveAt(0);
        }

        private void FixedUpdate()
        {
            if (Health == null || _rb == null) return;
            if (!IsAlive || _fallenOn) { _rb.linearVelocity = Vector2.zero; return; }
            float dt = Time.fixedDeltaTime;
            var room = Game.Current;
            float slow = room != null ? room.SlowAt(Position) : 1f;
            Vector2 target = ControlLocked ? Vector2.zero : Input.Move * Stats.Speed * slow;
            float rate = target.sqrMagnitude > 1e-4f ? _p.Num("accel", 38f) : _p.Num("decel", 30f);
            _vel = Vector2.MoveTowards(_vel, target, rate * dt);
            _knock = Vector2.MoveTowards(_knock, Vector2.zero, 25f * dt);
            Vector2 push = room != null && !ControlLocked ? room.PushAt(Position) : Vector2.zero;
            _rb.linearVelocity = _vel + _knock + push;
            if (!ControlLocked) CheckContact();
        }

        private void CheckContact()
        {
            var room = Game.Current;
            if (room == null || Health.IsInvulnerable(Time.time)) return;
            foreach (var e in room.Enemies)
            {
                if (e == null || !e.CanContact) continue;
                float r = Radius + e.ContactRadius;
                if ((e.Feet - Position).sqrMagnitude > r * r) continue;
                if (Hurt(e.ContactDamage, Position - e.Feet, e.TypeId)) return;
            }
        }

        private void Shoot(Direction dir)
        {
            var s = Stats;
            _nextShot = Time.time + 1f / Mathf.Max(0.1f, s.FireRate);
            _poseUntil = Time.time + _p.Num("shootPose", 0.2f);
            _poseDir = Facing2Dir(dir);
            Facing = DirToFacing(dir);
            _shotCount++;

            Vector2 fwd = dir.ToVector();
            Vector2 side = new Vector2(-fwd.y, fwd.x);
            Vector2 origin = Position + fwd * 0.3f;
            Vector2 vel = fwd * s.ShotSpeed + _rb.linearVelocity * _p.Num("inherit", 0.3f);
            float height = _p.Num("shotHeight", 0.95f);
            bool stamp = s.StampEvery > 0 && _shotCount % s.StampEvery == 0;
            float dmg = s.Damage * (stamp ? s.StampMul : 1f);
            var kind = stamp ? ShotKind.Stamp : s.LetterShots ? ShotKind.Letter : ShotKind.InkDrop;
            const string letters = "PALINODEMARA";
            char ch = letters[Random.Range(0, letters.Length)];

            if (s.DoubleShot)
            {
                Fire(kind, origin + side * 0.2f, vel, s, dmg * s.CopyDamage, height, ch, stamp);
                Fire(kind, origin - side * 0.2f, vel, s, dmg * s.CopyDamage, height, letters[Random.Range(0, letters.Length)], stamp);
            }
            else
            {
                _altSide = !_altSide;
                Fire(kind, origin + side * (_altSide ? 0.06f : -0.06f), vel, s, dmg, height, ch, stamp);
            }
            Game.Audio?.Play("ink_shot", stamp ? 1.2f : 1f);
            if (History.Count > 0)
            {
                var f = History[History.Count - 1];
                f.Shot = true;
                f.ShotDir = dir;
                History[History.Count - 1] = f;
            }
        }

        private void Fire(ShotKind kind, Vector2 origin, Vector2 vel, PlayerStats s, float dmg, float height, char ch, bool stamp)
        {
            var shot = Game.Shots.Spawn(Faction.Player, kind, origin, vel, s.Range, dmg, height, ch, stamp ? 1.35f : 1f);
            shot.Pierce = s.Pierce;
            shot.Stun = stamp ? s.StampStun : 0f;
        }

        private static PlayerController.Facing DirToFacing(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return PlayerController.Facing.Up;
                case Direction.Down: return PlayerController.Facing.Down;
                case Direction.Left: return PlayerController.Facing.Left;
                default: return PlayerController.Facing.Right;
            }
        }

        private static Direction Facing2Dir(Direction d) => d;

        private void Animate()
        {
            Vector2 move = Input.Move;
            bool walking = !ControlLocked && move.sqrMagnitude > 0.01f;
            _moveBlend = Mathf.MoveTowards(_moveBlend, walking ? 1f : 0f, Time.deltaTime * 6f);
            if (walking && Time.time >= _poseUntil)
            {
                var want = Mathf.Abs(move.x) > Mathf.Abs(move.y) * 0.9f
                    ? (move.x > 0 ? PlayerController.Facing.Right : PlayerController.Facing.Left)
                    : (move.y > 0 ? PlayerController.Facing.Up : PlayerController.Facing.Down);
                if (want != Facing)
                {
                    float phase = _cycle / Mathf.Max(1, _walk[Facing].Frames.Length);
                    Facing = want;
                    _cycle = phase * _walk[Facing].Frames.Length;
                }
            }
            var set = _walk[Facing];
            int n = set.Frames.Length;
            float blendK = 0f;
            if (walking)
            {
                float moved = _rb.linearVelocity.magnitude * Time.deltaTime;
                if (moved < 1e-5f) moved = Stats.Speed * Time.deltaTime * 0.25f;
                float prev = _cycle;
                _cycle = Mathf.Repeat(_cycle + moved / _cycleUnits * n, n);
                foreach (int p in set.Plants)
                    if (Crossed(prev, _cycle, p + 0.5f, n)) _steps?.PlayStep(Game.Root != null ? Game.Root.Audio.MasterVolume : 1f);
                blendK = Mathf.SmoothStep(0f, 1f, (_cycle - Mathf.Floor(_cycle) - 0.55f) / 0.45f) * _blendAmount;
            }
            else _cycle = set.Idle;
            ApplyFrame(blendK);

            float idle = 1f - _moveBlend;
            float breath = Mathf.Sin(Time.time * 2f * Mathf.PI / 3.6f);
            float bob = walking ? Mathf.Abs(Mathf.Sin(_cycle / n * Mathf.PI * 2f)) * 0.03f : 0f;
            _visual.localScale = new Vector3(1f, 1f + breath * 0.008f * idle, 1f);
            _visual.localPosition = new Vector3(0f, bob, 0f);
        }

        private static bool Crossed(float prev, float cur, float mark, int n)
        {
            if (cur >= prev) return prev < mark && cur >= mark;
            return prev < mark || cur >= mark;
        }

        private void ApplyFrame(float blendK)
        {
            if (_body == null) return;
            int order = FloorSprites.Order(transform.position.y);
            Sprite sprite;
            float scale;
            bool flip = false;
            if (_fallenOn || !IsAlive)
            {
                sprite = _fallen;
                scale = _fallenScale;
                blendK = 0f;
            }
            else if (Time.time < _pickupUntil && _pickupPose != null)
            {
                sprite = _pickupPose;
                scale = _pickupScale;
                blendK = 0f;
            }
            else if (Time.time < _poseUntil && _shootPose.TryGetValue(_poseDir, out var pose) && pose != null)
            {
                sprite = pose;
                scale = _poseScale;
                blendK = 0f;
            }
            else
            {
                var set = _walk[Facing];
                int n = set.Frames.Length;
                int i = Mathf.FloorToInt(_cycle) % n;
                sprite = set.Frames[i];
                scale = _walkScale;
                flip = Facing == PlayerController.Facing.Left;
                _blend.sprite = set.Frames[(i + 1) % n];
            }
            _body.sprite = sprite;
            _body.flipX = flip;
            _body.transform.localScale = Vector3.one * scale;
            _body.sortingOrder = order;
            _blend.transform.localScale = Vector3.one * scale;
            _blend.flipX = flip;
            _blend.enabled = blendK > 0.02f;
            _blend.color = new Color(1f, 1f, 1f, blendK);
            _blend.sortingOrder = order + 1;
            _flash.sprite = sprite;
            _flash.flipX = flip;
            _flash.transform.localScale = Vector3.one * scale;
            _flash.sortingOrder = order + 2;
            _flash.enabled = Time.time < _flashUntil;
            _shadow.enabled = !_fallenOn && IsAlive;
            if (_pickupIcon != null) _pickupIcon.sortingOrder = order + 3;
        }
    }
}
