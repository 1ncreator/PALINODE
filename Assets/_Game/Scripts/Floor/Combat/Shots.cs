using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Palinode.Floor
{
    public enum ShotKind { InkDrop, Letter, Sheet, Stamp }

    /// <summary>One flying shot. The position is its shadow on the floor; the drawing floats <see cref="Height"/> above.</summary>
    public sealed class Shot
    {
        public GameObject Go;
        public Transform Visual;
        public SpriteRenderer Sprite;
        public SpriteRenderer ShadowSr;
        public TextMeshPro Text;
        public Faction Faction;
        public ShotKind Kind;
        public Vector2 Pos, Vel;
        public float RangeLeft, RangeTotal, Damage, Radius, Height, Stun, Spin;
        public int Hp;                    // > 0: another shot can destroy it (boss sheets)
        public bool Pierce, Active;
        public string Killer;             // enemy type that fired it (death screen)
        public readonly HashSet<IHittable> Pierced = new HashSet<IHittable>();
    }

    /// <summary>
    /// All shots of the floor, updated in one place. Shots move themselves and circle-cast along the path each frame
    /// against the layers they can hit (never tunnel), like RogueDungeon's BulletPool.
    /// </summary>
    public sealed class ShotPool : MonoBehaviour
    {
        private readonly List<Shot> _active = new List<Shot>();
        private readonly Stack<Shot> _free = new Stack<Shot>();
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];
        private TMP_FontAsset _mono;
        private Sprite _drop, _sheet;

        public IReadOnlyList<Shot> Active => _active;
        public System.Action<Shot, Vector2, IHittable> PlayerShotHit;   // for the bug that dodges / stats
        public System.Action<Shot, Vector2> PlayerShotEnded;

        public void Init()
        {
            _mono = FloorSprites.Config != null ? FloorSprites.Config.MonoFont : null;
            _drop = FloorSprites.Get("F1_ink_drop");
            _sheet = FloorSprites.Get("F1_sheet_proj");
        }

        private Shot Create()
        {
            var s = new Shot();
            s.Go = new GameObject("Shot");
            s.Go.transform.SetParent(transform, false);
            s.Visual = new GameObject("Visual").transform;
            s.Visual.SetParent(s.Go.transform, false);
            s.Sprite = s.Visual.gameObject.AddComponent<SpriteRenderer>();
            s.Sprite.sharedMaterial = FloorSprites.Lit;
            s.ShadowSr = FloorSprites.Shadow(s.Go.transform, 0.3f, 0.35f);
            s.Text = FloorSprites.Text(s.Visual, "Letter", "A", _mono, 5f, new Color(0.05f, 0.04f, 0.05f), 0);
            return s;
        }

        public Shot Spawn(Faction f, ShotKind kind, Vector2 pos, Vector2 vel, float range, float damage, float height = 0.9f,
            char letter = 'A', float scale = 1f)
        {
            var s = _free.Count > 0 ? _free.Pop() : Create();
            s.Faction = f;
            s.Kind = kind;
            s.Pos = pos;
            s.Vel = vel;
            s.RangeLeft = s.RangeTotal = range;
            s.Damage = damage;
            s.Height = height;
            s.Radius = (kind == ShotKind.Sheet ? 0.28f : kind == ShotKind.Stamp ? 0.24f : 0.17f) * scale;
            s.Hp = kind == ShotKind.Sheet ? 1 : 0;
            s.Pierce = false;
            s.Stun = 0f;
            s.Killer = null;
            s.Spin = kind == ShotKind.Sheet ? Random.Range(-360f, 360f) : 0f;
            s.Pierced.Clear();
            s.Active = true;
            s.Go.SetActive(true);

            bool text = kind == ShotKind.Letter;
            s.Text.gameObject.SetActive(text);
            s.Sprite.enabled = !text;
            s.Visual.localRotation = Quaternion.identity;
            if (text)
            {
                s.Text.text = letter.ToString();
                s.Text.fontSize = 5.5f * scale;
                s.Text.color = f == Faction.Player ? new Color(0.07f, 0.06f, 0.07f) : new Color(0.1f, 0.08f, 0.08f);
                s.Text.fontStyle = FontStyles.Bold;
                s.Visual.localScale = Vector3.one;
            }
            else
            {
                s.Sprite.sprite = kind == ShotKind.Sheet ? _sheet : _drop;
                s.Sprite.color = Color.white;
                float w = kind == ShotKind.Sheet ? 0.75f : kind == ShotKind.Stamp ? 0.62f : 0.42f;
                s.Visual.localScale = Vector3.one * FloorSprites.ScaleFor(s.Sprite.sprite, w * scale);
            }
            var sb = s.ShadowSr.sprite.bounds.size;
            float sw = s.Radius * 2.4f;
            s.ShadowSr.transform.localScale = new Vector3(sw / sb.x, sw * 0.4f / sb.y, 1f);
            Place(s);
            _active.Add(s);
            return s;
        }

        private void Place(Shot s)
        {
            s.Go.transform.position = s.Pos;
            float k = s.RangeTotal > 0f ? s.RangeLeft / s.RangeTotal : 1f;
            float h = s.Height * (k < 0.3f ? Mathf.Lerp(0.2f, 1f, k / 0.3f) : 1f);   // drops at the end of range
            s.Visual.localPosition = new Vector3(0f, h, 0f);
            if (s.Kind == ShotKind.InkDrop || s.Kind == ShotKind.Stamp)
            {
                float a = Mathf.Atan2(s.Vel.y, s.Vel.x) * Mathf.Rad2Deg;
                s.Visual.localRotation = Quaternion.Euler(0f, 0f, a);   // the drawn drop points right, tail to the left
            }
            else if (s.Kind == ShotKind.Sheet) s.Visual.localRotation *= Quaternion.Euler(0f, 0f, s.Spin * Time.deltaTime);
            int order = FloorSprites.Order(s.Pos.y) + 50;
            s.Sprite.sortingOrder = order;
            s.Text.sortingOrder = order;
        }

        public void Despawn(Shot s)
        {
            if (!s.Active) return;
            s.Active = false;
            s.Go.SetActive(false);
            _active.Remove(s);
            _free.Push(s);
        }

        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--) Despawn(_active[i]);
        }

        public void ClearFaction(Faction f)
        {
            for (int i = _active.Count - 1; i >= 0; i--) if (_active[i].Faction == f) Despawn(_active[i]);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;
                var s = _active[i];
                if (!s.Active) continue;
                Step(s, dt);
            }
        }

        private void Step(Shot s, float dt)
        {
            Vector2 step = s.Vel * dt;
            float dist = step.magnitude;
            if (dist <= 1e-5f) return;
            Vector2 dir = step / dist;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(s.Faction == Faction.Player ? Layers.PlayerShotMask : Layers.EnemyShotMask);
            int n = Physics2D.CircleCast(s.Pos, s.Radius, dir, filter, _hits, dist);
            // closest first
            System.Array.Sort(_hits, 0, n, HitComparer.Instance);
            for (int h = 0; h < n; h++)
            {
                var hit = _hits[h];
                if (hit.collider == null) continue;
                if (s.Faction == Faction.Player)
                {
                    var target = hit.collider.GetComponentInParent<IHittable>();
                    if (target != null)
                    {
                        if (s.Pierced.Contains(target)) continue;
                        var info = new HitInfo { Damage = s.Damage, Direction = dir, Stun = s.Stun, Point = hit.point };
                        bool used = target.TakeHit(info);
                        PlayerShotHit?.Invoke(s, hit.point, target);
                        if (!used) continue;
                        if (s.Pierce && !target.StopsPiercing) { s.Pierced.Add(target); continue; }
                        End(s, hit.centroid, true);
                        return;
                    }
                    End(s, hit.centroid, false);
                    return;
                }
                // Enemy shot.
                if (hit.collider.gameObject.layer == Layers.Player)
                {
                    var elias = hit.collider.GetComponentInParent<Elias>();
                    if (elias != null && elias.Hurt(Mathf.Max(1, Mathf.RoundToInt(s.Damage)), dir, s.Killer)) { End(s, hit.centroid, true); return; }
                    continue;   // invulnerable: fly through
                }
                End(s, hit.centroid, false);
                return;
            }

            // Player shots can knock boss sheets out of the air.
            if (s.Faction == Faction.Player)
            {
                for (int j = _active.Count - 1; j >= 0; j--)
                {
                    var o = _active[j];
                    if (!o.Active || o.Faction != Faction.Enemy || o.Hp <= 0) continue;
                    if ((o.Pos - s.Pos).sqrMagnitude > (o.Radius + s.Radius + 0.1f) * (o.Radius + s.Radius + 0.1f)) continue;
                    o.Hp--;
                    if (o.Hp <= 0)
                    {
                        Game.Fx?.Burst(o.Pos + Vector2.up * o.Height, FloorFx.Paper, 10, 3f, 0.1f, 0.35f);
                        Despawn(o);
                    }
                    End(s, s.Pos, true);
                    return;
                }
            }

            s.Pos += step;
            s.RangeLeft -= dist;
            Place(s);
            if (s.RangeLeft <= 0f) End(s, s.Pos, false);
        }

        private void End(Shot s, Vector2 at, bool hitSomething)
        {
            if (!s.Active) return;
            if (s.Faction == Faction.Player)
            {
                Game.Fx?.Burst(at + Vector2.up * s.Height * 0.6f, FloorFx.Ink, hitSomething ? 7 : 5, 2.4f, 0.09f, 0.3f);
                if (!hitSomething && Game.Current != null && Game.Current.Frame.InsideFloor(at, -0.1f))
                {
                    Game.Fx?.Splat(Game.Current, at, s.Kind == ShotKind.Stamp ? 0.8f : 0.55f, Color.white);
                    Game.Audio?.Play("ink_splat", 0.8f);
                }
                else if (hitSomething) Game.Audio?.Play("ink_hit");
                PlayerShotEnded?.Invoke(s, at);
            }
            else
            {
                Color c = s.Kind == ShotKind.Sheet ? FloorFx.Paper : FloorFx.Ink;
                Game.Fx?.Burst(at + Vector2.up * s.Height * 0.6f, c, 5, 2f, 0.08f, 0.25f);
            }
            Despawn(s);
        }

        private sealed class HitComparer : IComparer<RaycastHit2D>
        {
            public static readonly HitComparer Instance = new HitComparer();
            public int Compare(RaycastHit2D a, RaycastHit2D b) => a.distance.CompareTo(b.distance);
        }
    }
}
