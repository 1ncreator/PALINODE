using System.Collections;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>Paper stack: solid, stops shots, three stages of damage; the last hit scatters it (10%: a blank sheet).</summary>
    public sealed class PaperStack : MonoBehaviour, IHittable
    {
        private SpriteRenderer _sr;
        private Sprite[] _stages;
        private Collider2D _col;
        private int _hp, _maxHp;
        private float _width;
        private float _sheetChance;
        private RoomView _room;

        public bool StopsPiercing => true;
        public bool Destroyed => _hp <= 0;

        public void Init(RoomView room, SpriteRenderer sr, Sprite[] stages, Collider2D col, int hits, float width, float sheetChance)
        {
            _room = room;
            _sr = sr;
            _stages = stages;
            _col = col;
            _hp = _maxHp = Mathf.Max(1, hits);
            _width = width;
            _sheetChance = sheetChance;
        }

        public bool TakeHit(HitInfo hit)
        {
            if (_hp <= 0) return false;
            _hp--;
            Game.Audio?.Play("stack_hit");
            Game.Fx?.Burst(hit.Point + Vector2.up * 0.4f, FloorFx.Paper, 6, 2.5f, 0.09f, 0.35f);
            int stage = Mathf.Clamp(_maxHp - _hp, 0, _stages.Length - 1);
            if (_hp <= 0)
            {
                _col.enabled = false;
                _sr.sprite = _stages[_stages.Length - 1];
                _sr.sortingOrder = FloorSprites.FloorDecalOrder + 300;
                _room.OnObstacleRemoved(transform.position);
                if (Random.value < _sheetChance) Game.Ctrl.SpawnPickup(_room, "sheet", transform.position + Vector3.down * 0.1f);
            }
            else _sr.sprite = _stages[Mathf.Min(stage, _stages.Length - 2)];
            if (_sr.sprite != null) _sr.transform.localScale = Vector3.one * FloorSprites.ScaleFor(_sr.sprite, _width);
            StartCoroutine(Shake());
            return true;
        }

        private IEnumerator Shake()
        {
            var t0 = _sr.transform.localPosition;
            for (int i = 0; i < 5; i++)
            {
                _sr.transform.localPosition = t0 + (Vector3)(Random.insideUnitCircle * 0.04f);
                yield return null;
            }
            _sr.transform.localPosition = t0;
        }
    }

    /// <summary>Drying sheets on a line: walkable, stop shots, torn by one hit.</summary>
    public sealed class DryingSheets : MonoBehaviour, IHittable
    {
        private SpriteRenderer _sr;
        private Collider2D _col;
        private int _hp = 1;

        public bool StopsPiercing => true;

        public void Init(SpriteRenderer sr, Collider2D col, int hits)
        {
            _sr = sr;
            _col = col;
            _hp = Mathf.Max(1, hits);
        }

        public bool TakeHit(HitInfo hit)
        {
            if (_hp <= 0) return false;
            if (--_hp > 0) return true;
            _col.enabled = false;
            Game.Audio?.Play("stack_hit", 1.2f);
            Game.Fx?.Burst(hit.Point + Vector2.up * 0.5f, FloorFx.Paper, 14, 3.2f, 0.11f, 0.5f);
            StartCoroutine(Tear());
            return true;
        }

        private IEnumerator Tear()
        {
            float t = 0f;
            var s0 = _sr.transform.localScale;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float k = t / 0.35f;
                _sr.transform.localScale = new Vector3(s0.x, s0.y * (1f - 0.55f * k), 1f);
                _sr.color = new Color(0.75f, 0.72f, 0.66f, 1f - 0.45f * k);
                yield return null;
            }
        }
    }

    /// <summary>Anything that simply forwards shots to an owner (boss parts, etc.).</summary>
    public sealed class HitForwarder : MonoBehaviour, IHittable
    {
        public System.Func<HitInfo, bool> OnHit;
        public bool Stops = true;
        public bool StopsPiercing => Stops;
        public bool TakeHit(HitInfo hit) => OnHit != null && OnHit(hit);
    }

    /// <summary>A scrolling feed belt: the painted segment stretched over the run + moving chevrons that show the direction.</summary>
    public sealed class BeltVisual : MonoBehaviour
    {
        private Transform[] _marks;
        private Vector2 _from, _to;
        private float _speed, _phase;

        public void Init(Vector2 from, Vector2 to, float speed, int marks)
        {
            _from = from;
            _to = to;
            _speed = speed;
            _marks = new Transform[marks];
            float len = Vector2.Distance(from, to);
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            for (int i = 0; i < marks; i++)
            {
                var m = FloorSprites.Quad(transform, "Chevron", new Color(0.08f, 0.07f, 0.06f, 0.55f), new Vector2(0.12f, 0.36f),
                    FloorSprites.FloorDecalOrder + 420, FloorSprites.Unlit);
                m.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                _marks[i] = m.transform;
            }
            _phase = 0f;
            if (len < 0.01f) enabled = false;
        }

        private void Update()
        {
            float len = Vector2.Distance(_from, _to);
            if (len < 0.01f) return;
            _phase = Mathf.Repeat(_phase + _speed * Time.deltaTime / len, 1f);
            for (int i = 0; i < _marks.Length; i++)
            {
                float k = Mathf.Repeat(_phase + i / (float)_marks.Length, 1f);
                _marks[i].position = Vector2.Lerp(_from, _to, k);
            }
        }
    }
}
