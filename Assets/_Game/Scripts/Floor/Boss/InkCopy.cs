using Palinode.Gameplay;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// An ink impression of Elias printed by the boss: it walks where Elias walked and shoots where he shot, three
    /// seconds late. Dark-tinted Elias drawings; HP 8.
    /// </summary>
    public sealed class InkCopy : EnemyBase
    {
        private float _delay = 3f;
        private float _born;
        private int _cursor;
        private float _shotCool;
        private static readonly Color Tint = new Color(0.16f, 0.13f, 0.2f, 0.92f);

        public void Configure(float delay)
        {
            _delay = delay;
            _born = Time.time;
        }

        protected override void OnSpawned()
        {
            ContactDamage = 1;
            if (Game.Player != null) SetSprite(Game.Player.IdleSprite(PlayerController.Facing.Down));
            Body.color = Tint;
            gameObject.layer = Layers.Ghost;   // walls and obstacles only: copies pass through each other and the boss
        }

        protected override void Tick(float dt)
        {
            var hist = Game.Player.History;
            float want = Time.time - _delay;
            Desired = Vector2.zero;
            if (hist.Count == 0 || hist[0].Time > want) return;
            // Find the recorded frame for "three seconds ago".
            if (_cursor >= hist.Count) _cursor = hist.Count - 1;
            while (_cursor > 0 && hist[_cursor].Time > want) _cursor--;
            while (_cursor < hist.Count - 1 && hist[_cursor + 1].Time <= want) _cursor++;
            var f = hist[_cursor];
            Vector2 to = f.Pos - Feet;
            Desired = to.magnitude > 0.05f ? Vector2.ClampMagnitude(to / Mathf.Max(0.02f, dt), 7f) : Vector2.zero;
            var sprite = Game.Player.IdleSprite(f.Facing == PlayerController.Facing.Left ? PlayerController.Facing.Right : f.Facing);
            SetSprite(sprite, f.Facing == PlayerController.Facing.Left);
            Body.color = Tint;
            _shotCool -= dt;
            if (f.Shot && _shotCool <= 0f)
            {
                _shotCool = 0.25f;
                var s = Game.Shots.Spawn(Faction.Enemy, ShotKind.InkDrop, Feet + f.ShotDir.ToVector() * 0.3f, f.ShotDir.ToVector() * 6.5f, 6f, 1f, 0.95f);
                s.Killer = "boss";
                s.Sprite.color = new Color(0.3f, 0.25f, 0.4f);
            }
        }
    }
}
