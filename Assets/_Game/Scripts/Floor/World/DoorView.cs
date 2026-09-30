using System.Collections;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    public enum DoorKind { Normal, Boss, Secret }

    /// <summary>
    /// A door in a room wall. Drawn for the top wall and rotated for the others (like in Isaac). Closed: a solid
    /// blocker in the wall gap. A secret "*" wall opens after a few hits of ink.
    /// </summary>
    public sealed class DoorView : MonoBehaviour, IHittable
    {
        public const float Thickness = 3f;

        private SpriteRenderer _sr;
        private BoxCollider2D _blocker;
        private Sprite _open, _closed;
        private float _width;
        private int _hitsLeft;

        public RoomView Room { get; private set; }
        public DoorLink Link { get; private set; }
        public DoorKind Kind { get; private set; }
        public Direction Side => Link.Side;
        public Vector2Int Cell { get; private set; }     // interior cell next to the door
        public Vector2 Edge { get; private set; }        // floor edge in the middle of the gap
        public float Gap { get; private set; }           // gap size along the wall
        public bool IsOpen { get; private set; }
        public bool Discovered { get; private set; }     // secret wall found (broken)
        public bool StopsPiercing => true;

        public void Init(RoomView room, DoorLink link, DoorKind kind, Vector2Int cell, Vector2 edge, float gap, JNode spec, float sideScale)
        {
            Room = room;
            Link = link;
            Kind = kind;
            Cell = cell;
            Edge = edge;
            Gap = gap;
            _open = FloorSprites.Get(spec.Str("open"));
            _closed = FloorSprites.Get(spec.Str("closed"));
            _width = spec.Num("width", 2.1f) * (link.Side == Direction.Up ? 1f : sideScale);
            _hitsLeft = spec.Int("hits", 3);

            transform.position = edge;
            float rot = link.Side == Direction.Up ? 0f : link.Side == Direction.Down ? 180f : link.Side == Direction.Left ? 90f : -90f;
            _sr = FloorSprites.Make(transform, "Door", _closed, _width, FloorSprites.DoorOrder);
            _sr.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
            // Doors on the bottom wall sit in front of the painted front wall.
            if (link.Side == Direction.Down) _sr.sortingOrder = FloorSprites.OverlayOrder - 200;

            gameObject.layer = kind == DoorKind.Secret ? Layers.Obstacle : Layers.Wall;
            _blocker = gameObject.AddComponent<BoxCollider2D>();
            Vector2 n = link.Side.ToVector();
            bool vertical = link.Side == Direction.Up || link.Side == Direction.Down;
            _blocker.size = vertical ? new Vector2(gap, Thickness) : new Vector2(Thickness, gap);
            _blocker.offset = n * (Thickness * 0.5f);
            SetOpen(kind != DoorKind.Secret, false);
        }

        public void SetOpen(bool open, bool sound)
        {
            if (Kind == DoorKind.Secret && open) Discovered = true;
            bool was = IsOpen;
            IsOpen = open;
            _blocker.enabled = !open;
            if (_sr != null)
            {
                _sr.sprite = open ? _open : _closed;
                if (_sr.sprite != null) _sr.transform.localScale = Vector3.one * FloorSprites.ScaleFor(_sr.sprite, _width);
            }
            if (sound && was != open && Kind != DoorKind.Secret) Game.Audio?.Play(open ? "door_open" : "door_close");
        }

        public bool TakeHit(HitInfo hit)
        {
            if (IsOpen) return false;
            if (Kind != DoorKind.Secret) return true;   // a closed door just swallows the ink
            _hitsLeft--;
            Game.Fx?.Burst(hit.Point, new Color(0.35f, 0.3f, 0.26f), 6, 2.5f, 0.1f, 0.4f);
            StartCoroutine(Shake());
            if (_hitsLeft <= 0) Game.Ctrl.OpenSecret(this);
            return true;
        }

        private IEnumerator Shake()
        {
            var p0 = _sr.transform.localPosition;
            for (int i = 0; i < 6; i++)
            {
                _sr.transform.localPosition = p0 + (Vector3)(Random.insideUnitCircle * 0.05f);
                yield return null;
            }
            _sr.transform.localPosition = p0;
        }

        /// <summary>Has a point walked out of the room through this (open) door?</summary>
        public bool Passed(Vector2 p, float margin)
        {
            if (!IsOpen) return false;
            Vector2 d = p - Edge;
            switch (Side)
            {
                case Direction.Up: return d.y > margin && Mathf.Abs(d.x) < Gap;
                case Direction.Down: return d.y < -margin && Mathf.Abs(d.x) < Gap;
                case Direction.Left: return d.x < -margin && Mathf.Abs(d.y) < Gap;
                default: return d.x > margin && Mathf.Abs(d.y) < Gap;
            }
        }
    }
}
