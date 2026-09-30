using TMPro;
using UnityEngine;

namespace Palinode.Floor
{
    public enum PickupKind { Drop, Inkwell, Sheet, Page, Item, Pencil }

    /// <summary>
    /// Something lying in a room: ink drop (+½), inkwell (+1), blank sheet (money), a page of the original, or an
    /// item on a pedestal / shop table. Small pickups are taken by touch; items need E / A. Pedestals of one
    /// "choice group" offer one item only — taking it removes the others.
    /// </summary>
    public sealed class Pickup : MonoBehaviour
    {
        private SpriteRenderer _icon, _shadow, _pedestal;
        private TextMeshPro _price;
        private float _bobPhase;
        private float _iconBaseY;
        private bool _taken;
        private float _readyAt;

        public PickupKind Kind { get; private set; }
        public string ItemId { get; private set; }
        public int Price { get; private set; }
        public int ChoiceGroup { get; private set; }
        public int PageIndex { get; private set; }
        public RoomView Room { get; private set; }
        public bool NeedsInteract => Kind == PickupKind.Item || Kind == PickupKind.Pencil;
        public Vector2 Position => transform.position;
        public bool Taken => _taken;

        public static string SpriteFor(PickupKind k, string itemId)
        {
            var db = Game.DB;
            switch (k)
            {
                case PickupKind.Drop: return db.Data["pickups"]["drop"].Str("sprite");
                case PickupKind.Inkwell: return db.Data["pickups"]["inkwell"].Str("sprite");
                case PickupKind.Sheet: return db.Data["pickups"]["sheet"].Str("sprite");
                case PickupKind.Page: return db.Data["pickups"]["page"].Str("sprite");
                case PickupKind.Pencil: return db.Data["redPencil"].Str("icon", "PR_redpencil");
                default: return db.Item(itemId).Str("icon");
            }
        }

        public void Init(RoomView room, PickupKind kind, Vector2 pos, string itemId = null, int price = 0, bool pedestal = false,
            int choiceGroup = 0, int pageIndex = 0, float heightOverride = -1f)
        {
            Room = room;
            Kind = kind;
            ItemId = itemId;
            Price = price;
            ChoiceGroup = choiceGroup;
            PageIndex = pageIndex;
            transform.SetParent(room.ActorRoot, false);
            transform.position = pos;
            var db = Game.DB;
            float width = kind == PickupKind.Item || kind == PickupKind.Pencil ? 0.72f
                : db.Data["pickups"][kind == PickupKind.Drop ? "drop" : kind == PickupKind.Inkwell ? "inkwell" : kind == PickupKind.Sheet ? "sheet" : "page"].Num("width", 0.5f);
            var sprite = FloorSprites.Get(SpriteFor(kind, itemId));
            float lift = 0.12f;
            if (pedestal)
            {
                var ps = db.Data["objects"]["pedestal"];
                _pedestal = FloorSprites.Make(transform, "Pedestal", FloorSprites.Get(ps.Str("sprite")), ps.Num("width", 0.95f), FloorSprites.Order(pos.y));
                lift = _pedestal.sprite != null ? _pedestal.bounds.size.y * 0.92f : 0.9f;
                var col = gameObject.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.7f, 0.35f);
                gameObject.layer = Layers.LowObstacle;
            }
            if (heightOverride >= 0f) lift = heightOverride;
            _shadow = pedestal ? null : FloorSprites.Shadow(transform, width * 0.8f, 0.35f);
            _icon = FloorSprites.Make(transform, "Icon", sprite, 0f, FloorSprites.Order(pos.y) + 2);
            _icon.transform.localScale = Vector3.one * FloorSprites.FitScale(sprite, width);
            _iconBaseY = lift;
            _icon.transform.localPosition = new Vector3(0f, lift, 0f);
            if (kind == PickupKind.Pencil && sprite != null)
            {
                _icon.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
                _icon.transform.localScale = Vector3.one * FloorSprites.FitScale(sprite, 1.0f);
            }
            if (price > 0)
            {
                var font = FloorSprites.Config != null ? FloorSprites.Config.Font("hand_bad") : null;
                _price = FloorSprites.Text(transform, "Price", price.ToString(), font, 2.6f, new Color(0.93f, 0.88f, 0.76f), FloorSprites.Order(pos.y) + 3);
                _price.transform.localPosition = new Vector3(-0.1f, -0.55f, 0f);
                var sheet = FloorSprites.Make(_price.transform, "SheetIcon", FloorSprites.Get(db.Data["pickups"]["sheet"].Str("sprite")), 0.28f, FloorSprites.Order(pos.y) + 3);
                sheet.transform.localPosition = new Vector3(0.38f, 0.02f, 0f);
            }
            _bobPhase = Random.Range(0f, 6f);
            _readyAt = Time.time + (kind == PickupKind.Item || kind == PickupKind.Pencil ? 0.3f : 0.45f);
            room.Pickups.Add(this);
        }

        private void Update()
        {
            if (_taken || Room == null) return;
            float bob = (Kind == PickupKind.Item || Kind == PickupKind.Pencil || Kind == PickupKind.Page) ? Mathf.Sin(Time.time * 2.2f + _bobPhase) * 0.07f : 0f;
            _icon.transform.localPosition = new Vector3(0f, _iconBaseY + bob, 0f);
            var p = Game.Player;
            if (p == null || !p.IsAlive || p.ControlLocked || Game.Current != Room || Time.time < _readyAt) return;
            float d = Vector2.Distance(p.Position, Position);
            if (NeedsInteract)
            {
                if (d > 1.0f) return;
                Game.Ctrl.ShowPrompt(this);
                if (p.Input.InteractPressed) Game.Ctrl.TryTake(this);
            }
            else if (d < 0.55f) Game.Ctrl.TryTake(this);
        }

        public void Remove(bool vanishEffect)
        {
            if (_taken) return;
            _taken = true;
            Room?.Pickups.Remove(this);
            if (vanishEffect)
            {
                Game.Fx?.Burst(Position + Vector2.up * _iconBaseY, FloorFx.Ink, 14, 2.5f, 0.12f, 0.5f);
                Game.Fx?.Strike(null, Position + Vector2.up * _iconBaseY, 1.1f, FloorFx.Ink);
            }
            if (_pedestal != null)
            {
                // The pedestal stays, empty.
                _icon.enabled = false;
                if (_price != null) _price.gameObject.SetActive(false);
                enabled = false;
                return;
            }
            Destroy(gameObject);
        }
    }
}
