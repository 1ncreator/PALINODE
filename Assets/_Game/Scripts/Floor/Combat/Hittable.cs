using UnityEngine;

namespace Palinode.Floor
{
    public enum Faction { Player, Enemy }

    /// <summary>What a shot carries into its target.</summary>
    public struct HitInfo
    {
        public float Damage;
        public Vector2 Direction;
        public float Stun;
        public bool Red;             // red pencil
        public Vector2 Point;
    }

    /// <summary>Anything Elias's shots can hit: enemies, boss parts, paper stacks, drying sheets, secret walls.</summary>
    public interface IHittable
    {
        /// <summary>Returns true if the shot is used up (it stops here).</summary>
        bool TakeHit(HitInfo hit);
        /// <summary>False for things a piercing shot should fly through after hitting (enemies).</summary>
        bool StopsPiercing { get; }
    }
}
