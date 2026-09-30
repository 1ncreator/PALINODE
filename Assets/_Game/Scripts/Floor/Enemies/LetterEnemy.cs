using System.Collections.Generic;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>A group of Letters that run as one swarm; once in a while four of them first line up as MARA.</summary>
    public sealed class LetterSwarm
    {
        public readonly List<LetterEnemy> Members = new List<LetterEnemy>();
        public bool Mara;
        public float MaraUntil;
        public Vector2 MaraAt;
        public float NextClatter;
    }

    /// <summary>
    /// Letters: a swarm of type blocks on little legs, trembling as they run; touch = half a drop. Mostly M A R E L I S.
    /// Death: the letter falls with a clatter.
    /// </summary>
    public sealed class LetterEnemy : EnemyBase
    {
        private LetterSwarm _swarm;
        private char _letter;
        private Vector2 _offset;
        private float _jitterT;
        private Vector2 _jitter;
        private float _bob;

        public char Letter => _letter;

        public void Configure(LetterSwarm swarm, char letter)
        {
            _swarm = swarm;
            swarm.Members.Add(this);
            SetLetter(letter);
        }

        public void SetLetter(char letter)
        {
            _letter = letter;
            SetSprite(FloorSprites.Get("F1_letter_" + letter));
        }

        protected override void OnSpawned()
        {
            _offset = Random.insideUnitCircle * 0.9f;
            _bob = Random.Range(0f, 10f);
        }

        protected override void Tick(float dt)
        {
            float speed = Spec.Num("speed", 3f);
            if (_swarm != null && _swarm.Mara && Time.time < _swarm.MaraUntil)
            {
                // Line up in the spelling order and hold still.
                int i = _swarm.Members.IndexOf(this);
                Vector2 slot = _swarm.MaraAt + new Vector2((i - 1.5f) * 0.85f, 0f);
                Vector2 d = slot - Feet;
                Desired = d.magnitude > 0.08f ? d.normalized * speed : Vector2.zero;
                return;
            }
            _jitterT -= dt;
            if (_jitterT <= 0f)
            {
                _jitterT = Random.Range(0.08f, 0.2f);
                _jitter = Random.insideUnitCircle * Spec.Num("jitter", 0.35f) * speed;
            }
            Vector2 target = PlayerPos + _offset * 0.6f;
            Desired = Steer(target) * speed + _jitter;
            if (_swarm != null && Time.time > _swarm.NextClatter)
            {
                _swarm.NextClatter = Time.time + Random.Range(1.4f, 2.4f);
                Game.Audio?.Play("letters_clatter");
            }
        }

        protected override void Update()
        {
            base.Update();
            if (Alive && Body != null)
            {
                // Trembling run.
                bool moving = Desired.sqrMagnitude > 0.1f;
                float wob = moving ? Mathf.Sin((Time.time + _bob) * 38f) * 4f : Mathf.Sin((Time.time + _bob) * 6f) * 1f;
                Body.transform.localRotation = Quaternion.Euler(0f, 0f, wob);
                FlashSr.transform.localRotation = Body.transform.localRotation;
            }
        }

        protected override void OnDeath(bool red)
        {
            _swarm?.Members.Remove(this);
            Game.Fx?.Fall(Body.sprite, Feet + Vector2.up * 0.3f, Width);
            Game.Audio?.Play("letter_fall");
        }
    }

    /// <summary>
    /// Proofreader's bug: zigzags at the player, side-steps shots flying at it, and eats Elias's ink splats on the
    /// floor (leaving a white spot). Two leg poses alternate while it runs.
    /// </summary>
    public sealed class BugEnemy : EnemyBase
    {
        private Sprite _a, _b;
        private float _legT, _zigT, _dodgeUntil, _dodgeCool;
        private int _zigSign = 1;
        private bool _legs;
        private Vector2 _dodgeDir;
        private float _scuttle;

        protected override void OnSpawned()
        {
            _a = FloorSprites.Get("F1_bug_a");
            _b = FloorSprites.Get("F1_bug_b");
            SetSprite(_a);
        }

        protected override void Tick(float dt)
        {
            float speed = Spec.Num("speed", 5f);
            // Dodge a shot heading at it.
            if (Time.time < _dodgeUntil)
            {
                Desired = _dodgeDir * Spec.Num("dodge", 3.5f) * 1.4f;
                Animate(dt, true);
                return;
            }
            _dodgeCool -= dt;
            if (_dodgeCool <= 0f && TryDodge()) return;

            Vector2 target = PlayerPos;
            // Eat splats on the way.
            if (Spec.Bool("eatInk", true))
            {
                SpriteRenderer best = null;
                float bestD = 2.5f * 2.5f;
                foreach (var s in Room.Splats)
                {
                    if (s == null) continue;
                    float d = ((Vector2)s.transform.position - Feet).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = s; }
                }
                if (best != null)
                {
                    target = best.transform.position;
                    if (bestD < 0.3f * 0.3f) Eat(best);
                }
            }
            _zigT -= dt;
            if (_zigT <= 0f)
            {
                _zigT = Random.Range(0.3f, 0.5f);
                _zigSign = -_zigSign;
            }
            Vector2 dir = Steer(target);
            Desired = Rotate(dir, _zigSign * Spec.Num("zigzag", 0.55f) * Mathf.Rad2Deg) * speed;
            Animate(dt, true);
        }

        private bool TryDodge()
        {
            foreach (var s in Game.Shots.Active)
            {
                if (!s.Active || s.Faction != Faction.Player) continue;
                Vector2 to = Feet - s.Pos;
                float along = Vector2.Dot(to, s.Vel.normalized);
                if (along < 0f || along > 2.6f) continue;
                Vector2 lateral = to - s.Vel.normalized * along;
                if (lateral.magnitude > 0.6f) continue;
                Vector2 perp = new Vector2(-s.Vel.y, s.Vel.x).normalized;
                _dodgeDir = Vector2.Dot(perp, lateral) >= 0f ? perp : -perp;
                _dodgeUntil = Time.time + 0.22f;
                _dodgeCool = 0.9f;
                return true;
            }
            return false;
        }

        private void Eat(SpriteRenderer splat)
        {
            var pos = splat.transform.position;
            Room.RemoveSplat(splat);
            // A pale spot where the ink was.
            var spot = FloorSprites.Make(Room.DecalRoot, "EatenSpot", UI.ProceduralSprites.SoftDot(64, 0.5f), 0f, FloorSprites.FloorDecalOrder + 50, FloorSprites.Unlit);
            var b = spot.sprite.bounds.size;
            spot.transform.localScale = new Vector3(0.55f / b.x, 0.4f / b.y, 1f);
            spot.transform.position = pos;
            spot.color = new Color(0.92f, 0.9f, 0.84f, 0.45f);
        }

        private void Animate(float dt, bool moving)
        {
            _legT -= dt;
            if (moving && _legT <= 0f)
            {
                _legT = 0.09f;
                _legs = !_legs;
            }
            SetSprite(_legs ? _b : _a, Desired.x < 0f);
            _scuttle -= dt;
            if (moving && _scuttle <= 0f)
            {
                _scuttle = Random.Range(1.2f, 2.2f);
                Game.Audio?.Play("bug_scuttle");
            }
        }
    }
}
