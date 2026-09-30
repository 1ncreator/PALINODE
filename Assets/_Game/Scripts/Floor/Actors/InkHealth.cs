using System;

namespace Palinode.Floor
{
    /// <summary>
    /// Isaac-style health counted in half ink drops (ported from RogueDungeon's HeartHealth), with drop containers
    /// and invulnerability after a hit. Time is passed in explicitly so it is testable without Unity's clock.
    /// </summary>
    public sealed class InkHealth
    {
        private float _invulnerableUntil = float.NegativeInfinity;

        public InkHealth(int halves, int maxContainers, float invulnerability)
        {
            if (halves <= 0) throw new ArgumentOutOfRangeException(nameof(halves));
            Containers = (halves + 1) / 2;
            MaxContainers = Math.Max(Containers, maxContainers);
            Current = halves;
            Invulnerability = invulnerability;
        }

        public event Action<int, int> Changed;   // (current halves, max halves)
        public event Action Died;

        public int Containers { get; private set; }
        public int MaxContainers { get; }
        public int MaxHalves => Containers * 2;
        public int Current { get; private set; }
        public float Invulnerability { get; }
        public bool GodMode { get; set; }
        public bool IsDead => Current <= 0;

        public bool IsInvulnerable(float time) => GodMode || time < _invulnerableUntil;

        public void GrantInvulnerability(float time, float duration) => _invulnerableUntil = Math.Max(_invulnerableUntil, time + duration);

        /// <summary>Applies damage unless dead or invulnerable. True when damage was taken.</summary>
        public bool TryDamage(int halves, float time)
        {
            if (halves <= 0 || IsDead || IsInvulnerable(time)) return false;
            Current = Math.Max(0, Current - halves);
            _invulnerableUntil = time + Invulnerability;
            Changed?.Invoke(Current, MaxHalves);
            if (IsDead) Died?.Invoke();
            return true;
        }

        /// <summary>Would this hit be fatal (used by the red pencil's automatic save)?</summary>
        public bool WouldKill(int halves, float time) => halves > 0 && !IsDead && !IsInvulnerable(time) && halves >= Current;

        public void Heal(int halves)
        {
            if (halves <= 0 || IsDead) return;
            Current = Math.Min(MaxHalves, Current + halves);
            Changed?.Invoke(Current, MaxHalves);
        }

        public void Set(int halves)
        {
            Current = Math.Max(0, Math.Min(MaxHalves, halves));
            Changed?.Invoke(Current, MaxHalves);
        }

        public void AddContainer(int n = 1)
        {
            Containers = Math.Min(MaxContainers, Containers + n);
            Changed?.Invoke(Current, MaxHalves);
        }

        public bool IsFull => Current >= MaxHalves;
    }
}
