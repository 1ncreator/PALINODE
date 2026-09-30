using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Shared enemy logic (adapted from RogueDungeon's EnemyBase): health, white hit flash, knockback, stun, a
    /// harmless grace period after appearing, obstacle-aware steering on the room grid, the elite "Witness" outline,
    /// and death by an ink stroke that crosses the enemy out. Subclasses decide how to move and attack in Tick.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class EnemyBase : MonoBehaviour, IHittable
    {
        private const float SpawnAnimTime = 0.35f;

        protected JNode Spec;
        protected Rigidbody2D Rb;
        protected CircleCollider2D Col;
        protected Transform Visual;
        protected SpriteRenderer Body;
        protected SpriteRenderer FlashSr;
        protected SpriteRenderer OutlineSr;
        private readonly List<SpriteRenderer> _outline = new List<SpriteRenderer>();
        protected SpriteRenderer ShadowSr;
        protected float Width = 1f;
        protected Vector2 Desired;
        protected float VisualHeight;       // lift of the drawing above the feet (jumps)

        private float _activeAt, _flashUntil, _stunUntil, _spawnAnim;
        private Vector2 _knock;
        private bool _dying;
        private readonly List<Vector2Int> _path = new List<Vector2Int>();
        private float _repath;
        private int _pathIndex;
        private TMPro.TextMeshPro _label;
        private float _labelUntil;

        public string TypeId { get; private set; }
        public RoomView Room { get; private set; }
        public float Hp { get; protected set; }
        public float MaxHp { get; protected set; }
        public int ContactDamage { get; protected set; } = 1;
        public float ContactRadius { get; protected set; } = 0.35f;
        public bool Elite { get; private set; }
        public bool Alive => !_dying && Room != null;
        public bool Active => Alive && Time.time >= _activeAt;
        public bool Stunned => Time.time < _stunUntil;
        public virtual bool Grounded => true;
        public bool CanContact => Active && Grounded && ContactDamage > 0;
        public Vector2 Feet => Rb != null ? Rb.position : (Vector2)transform.position;
        public virtual bool StopsPiercing => false;
        protected Elias Player => Game.Player;
        protected Vector2 PlayerPos => Game.Player != null ? Game.Player.Position : Feet;
        protected bool PlayerAvailable => Game.Player != null && Game.Player.IsAlive && !Game.Player.ControlLocked;

        public void Setup(string type, JNode spec, RoomView room, Vector2 pos, bool elite, float hpOverride = -1f, float widthOverride = -1f)
        {
            TypeId = type;
            Spec = spec;
            Room = room;
            Elite = elite;
            gameObject.layer = Layers.Enemy;
            transform.position = pos;
            Rb = GetComponent<Rigidbody2D>();
            Rb.gravityScale = 0f;
            Rb.freezeRotation = true;
            Rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            Rb.linearDamping = 0f;
            Col = gameObject.AddComponent<CircleCollider2D>();
            Width = widthOverride > 0f ? widthOverride : spec.Num("width", 1f);
            Col.radius = Mathf.Clamp(Width * 0.32f, 0.2f, 0.55f);
            ContactRadius = Col.radius;
            MaxHp = (hpOverride > 0f ? hpOverride : spec.Num("hp", 5f)) * (elite ? Game.DB.Data["enemies"].Num("eliteHp", 1.5f) : 1f);
            Hp = MaxHp;
            ContactDamage = spec.Int("damage", 1);

            Visual = new GameObject("Visual").transform;
            Visual.SetParent(transform, false);
            ShadowSr = FloorSprites.Shadow(transform, Width * 0.8f, 0.4f);
            if (elite)
            {
                // White outline: four solid copies nudged around the drawing, behind it.
                foreach (var off in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
                                            new Vector2(0.7f, 0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, -0.7f) })
                {
                    var o = FloorSprites.Make(Visual, "Outline", null, 0f, 0, FloorSprites.Solid);
                    o.color = new Color(1f, 1f, 1f, 0.95f);
                    o.transform.localPosition = off * 0.035f;
                    _outline.Add(o);
                }
                OutlineSr = _outline[0];
                ShowLabel(string.Format(Game.T("F1_WITNESS"), Game.Meta != null ? Game.Meta.RevisionLabel : "01"), 4f);
            }
            Body = FloorSprites.Make(Visual, "Body", null, 0f, 0);
            FlashSr = FloorSprites.Make(Visual, "Flash", null, 0f, 0, FloorSprites.Solid);
            FlashSr.enabled = false;

            _activeAt = Time.time + Game.DB.Data["enemies"].Num("spawnGrace", 0.5f);
            _spawnAnim = 0f;
            Visual.localScale = Vector3.one * 0.1f;
            room.AddEnemy(this);
            OnSpawned();
            Game.Fx?.Burst(pos + Vector2.up * 0.4f, new Color(0.1f, 0.09f, 0.1f, 0.8f), 10, 2.5f, 0.14f, 0.35f);
        }

        protected virtual void OnSpawned() { }
        protected abstract void Tick(float dt);
        protected virtual void OnDamaged(HitInfo hit) { }
        protected virtual void OnDeath(bool red) { }
        protected virtual float DamageMultiplier(HitInfo hit) => 1f;
        /// <summary>False for things that stay after death (the jammed press).</summary>
        protected virtual bool DestroyOnDeath => true;

        /// <summary>Sets the drawing (and its flash / outline copies) sized to the enemy's width.</summary>
        protected void SetSprite(Sprite s, bool flip = false, float widthMul = 1f)
        {
            if (Body == null) return;
            float sc = FloorSprites.ScaleFor(s, Width * widthMul);
            foreach (var r in new[] { Body, FlashSr })
            {
                r.sprite = s;
                r.flipX = flip;
                r.transform.localScale = Vector3.one * sc;
            }
            foreach (var r in _outline)
            {
                r.sprite = s;
                r.flipX = flip;
                r.transform.localScale = Vector3.one * sc;
            }
        }

        public void ShowLabel(string text, float seconds)
        {
            if (_label == null)
            {
                var font = FloorSprites.Config != null ? FloorSprites.Config.Font("hand_marck") : null;
                _label = FloorSprites.Text(transform, "Label", text, font, 3.2f, new Color(0.95f, 0.93f, 0.88f), FloorSprites.OverlayOrder);
                _label.transform.localPosition = new Vector3(0f, Width * 1.6f + 0.4f, 0f);
            }
            _label.text = text;
            _labelUntil = Time.time + seconds;
        }

        protected virtual void Update()
        {
            if (Room == null) return;
            if (_spawnAnim < SpawnAnimTime)
            {
                _spawnAnim += Time.deltaTime;
                float t = Mathf.Clamp01(_spawnAnim / SpawnAnimTime);
                float c1 = 1.70158f, c3 = c1 + 1f, p = t - 1f;
                Visual.localScale = Vector3.one * (1f + c3 * p * p * p + c1 * p * p);
            }
            int order = FloorSprites.Order(Feet.y);
            foreach (var o in _outline) o.sortingOrder = order - 1;
            Body.sortingOrder = order;
            FlashSr.sortingOrder = order + 1;
            FlashSr.enabled = Time.time < _flashUntil;
            Visual.localPosition = new Vector3(0f, VisualHeight, 0f);
            if (_label != null)
            {
                float a = Mathf.Clamp01(_labelUntil - Time.time);
                _label.alpha = a;
                _label.gameObject.SetActive(a > 0f);
            }
            if (_dying) return;
            if (!Active || !PlayerAvailable || Stunned)
            {
                Desired = Vector2.zero;
                return;
            }
            if (Game.AutoKill && Time.time > _activeAt + 0.3f) { Kill(false); return; }
            Tick(Time.deltaTime);
        }

        protected virtual void FixedUpdate()
        {
            if (Rb == null || Room == null) return;
            if (_dying) { Rb.linearVelocity = Vector2.zero; return; }
            _knock = Vector2.MoveTowards(_knock, Vector2.zero, 22f * Time.fixedDeltaTime);
            float slow = Grounded ? Room.SlowAt(Feet) : 1f;
            Vector2 push = Grounded ? Room.PushAt(Feet) : Vector2.zero;
            Rb.linearVelocity = Desired * slow + _knock + push;
        }

        public bool TakeHit(HitInfo hit)
        {
            if (!Alive) return false;
            float dmg = hit.Damage * DamageMultiplier(hit);
            Hp -= dmg;
            _flashUntil = Time.time + 0.08f;
            FlashSr.color = hit.Red ? FloorFx.RedInk : Color.white;
            if (hit.Direction.sqrMagnitude > 1e-4f) _knock += hit.Direction.normalized * Spec.Num("knockback", 3f);
            if (hit.Stun > 0f) _stunUntil = Time.time + hit.Stun;
            // An ink stroke where the drop hit.
            Game.Fx?.Burst(hit.Point + Vector2.up * 0.5f, FloorFx.Ink, 6, 2.2f, 0.1f, 0.3f);
            if (Hp <= 0f) Die(hit.Red);
            else OnDamaged(hit);
            return true;
        }

        public void Kill(bool red)
        {
            if (!Alive) return;
            Hp = 0f;
            Die(red);
        }

        /// <summary>Removes the enemy without the death sequence (boss death clears minions this way too, but struck out).</summary>
        protected void Die(bool red)
        {
            if (_dying) return;
            _dying = true;
            Desired = Vector2.zero;
            if (Rb != null) Rb.linearVelocity = Vector2.zero;
            if (Col != null) Col.enabled = false;
            Vector2 c = Feet + Vector2.up * (VisualHeight + Width * 0.45f);
            Game.Fx?.Strike(null, c, Width * 1.5f + 0.3f, red ? FloorFx.RedInk : FloorFx.Ink);
            Game.Fx?.Burst(c, red ? FloorFx.RedInk : FloorFx.Ink, 16, 4.5f, 0.12f, 0.5f);
            Game.Audio?.Play("enemy_struck");
            Game.Ctrl?.HitStop(0.04f);
            Game.Camera?.Shake(0.08f, 0.12f);
            OnDeath(red);
            var room = Room;
            room.RemoveEnemy(this);
            Game.Ctrl?.OnEnemyKilled(this);
            if (!DestroyOnDeath) return;
            if (gameObject.activeInHierarchy) StartCoroutine(DeathRoutine());
            else Destroy(gameObject);
        }

        private IEnumerator DeathRoutine()
        {
            // Crossed out, the drawing breaks apart: squash and fade.
            float t = 0f;
            var s0 = Visual.localScale;
            yield return new WaitForSeconds(0.06f);
            while (t < 0.22f)
            {
                t += Time.deltaTime;
                float k = t / 0.22f;
                Visual.localScale = new Vector3(s0.x * (1f + 0.35f * k), s0.y * (1f - k), 1f);
                var col = Body.color;
                col.a = 1f - k;
                Body.color = col;
                yield return null;
            }
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ helpers

        protected Vector2 DirToPlayer()
        {
            Vector2 d = PlayerPos - Feet;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
        }

        protected float DistToPlayer() => Vector2.Distance(PlayerPos, Feet);

        protected bool ClearLine(Vector2 from, Vector2 to, float radius)
        {
            Vector2 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Layers.SolidMask);
            var hits = new RaycastHit2D[1];
            return Physics2D.CircleCast(from, radius, d / dist, filter, hits, dist) == 0;
        }

        /// <summary>Direction to walk toward <paramref name="target"/>, around obstacles (grid path when blocked).</summary>
        protected Vector2 Steer(Vector2 target)
        {
            if (ClearLine(Feet, target, Col.radius * 0.9f))
            {
                _path.Clear();
                Vector2 d = target - Feet;
                return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.zero;
            }
            _repath -= Time.deltaTime;
            if (_repath <= 0f || _path.Count == 0)
            {
                _repath = 0.4f;
                var from = Room.Frame.CellAt(Feet);
                var to = Room.Frame.CellAt(target);
                if (!Room.Pathfinder.FindPath(from, NearestWalkable(to), _path)) _path.Clear();
                _pathIndex = _path.Count > 1 ? 1 : 0;
            }
            while (_pathIndex < _path.Count)
            {
                Vector2 wp = Room.Frame.CellCenter(_path[_pathIndex]);
                if ((wp - Feet).sqrMagnitude < 0.12f) { _pathIndex++; continue; }
                return (wp - Feet).normalized;
            }
            Vector2 dd = target - Feet;
            return dd.sqrMagnitude > 1e-4f ? dd.normalized : Vector2.zero;
        }

        protected Vector2Int NearestWalkable(Vector2Int c)
        {
            if (Room.IsWalkableCell(c)) return c;
            for (int r = 1; r < 4; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                var n = new Vector2Int(c.x + dx, c.y + dy);
                if (Room.Pathfinder.InBounds(n.x, n.y) && Room.IsWalkableCell(n)) return n;
            }
            return c;
        }

        protected static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        protected void FireShot(ShotKind kind, Vector2 dir, float speed, float range, char letter = 'A', float height = 0.8f)
        {
            var s = Game.Shots.Spawn(Faction.Enemy, kind, Feet + dir * (Col.radius + 0.1f), dir * speed, range, 1f, height, letter);
            s.Killer = TypeId;
        }

        protected SpriteRenderer MakeGlow(Transform parent, float size, Color color)
        {
            var sr = FloorSprites.Make(parent, "Glow", UI.ProceduralSprites.SoftDot(64, 0.15f), 0f, FloorSprites.OverlayOrder - 50, FloorSprites.Glow);
            var b = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(size / b.x, size / b.y, 1f);
            sr.color = color;
            return sr;
        }
    }
}
