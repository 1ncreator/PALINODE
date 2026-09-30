using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Floor I physics layers (named in the TagManager by the builder) and their collision matrix, which is
    /// re-applied when a floor starts so behaviour never depends on stale project settings. Movement uses the
    /// physics solver; shots use explicit circle casts against the masks below.
    /// </summary>
    public static class Layers
    {
        public const int Player = 8;
        public const int Enemy = 9;
        public const int Wall = 10;
        public const int Obstacle = 11;      // blocks walking and shots (press, paper stack)
        public const int LowObstacle = 12;   // blocks walking only (type case)
        public const int ShotScreen = 13;    // blocks shots only (drying sheets)
        public const int Airborne = 14;      // jumping blot, dashing car: no body collisions with actors
        public const int Ghost = 15;         // ink copies (collide with walls only)

        public const int First = Player;
        public static readonly string[] Names = { "Player", "Enemy", "Wall", "Obstacle", "LowObstacle", "ShotScreen", "Airborne", "Ghost" };

        /// <summary>What a player shot can hit.</summary>
        public const int PlayerShotMask = (1 << Enemy) | (1 << Wall) | (1 << Obstacle) | (1 << ShotScreen) | (1 << Airborne) | (1 << Ghost);
        /// <summary>What an enemy shot can hit.</summary>
        public const int EnemyShotMask = (1 << Player) | (1 << Wall) | (1 << Obstacle) | (1 << ShotScreen);
        public const int SolidMask = (1 << Wall) | (1 << Obstacle) | (1 << LowObstacle);

        private static bool Collides(int a, int b)
        {
            int Mask(int l)
            {
                switch (l)
                {
                    case Player: return (1 << Wall) | (1 << Obstacle) | (1 << LowObstacle);
                    case Enemy: return (1 << Wall) | (1 << Obstacle) | (1 << LowObstacle) | (1 << Enemy);
                    case Airborne: return 1 << Wall;
                    case Ghost: return (1 << Wall) | (1 << Obstacle) | (1 << LowObstacle);
                    case Wall: return (1 << Player) | (1 << Enemy) | (1 << Airborne) | (1 << Ghost);
                    case Obstacle:
                    case LowObstacle: return (1 << Player) | (1 << Enemy) | (1 << Ghost);
                    default: return 0;
                }
            }
            return (Mask(a) & (1 << b)) != 0 || (Mask(b) & (1 << a)) != 0;
        }

        public static void ApplyCollisionMatrix()
        {
            for (int a = First; a < First + Names.Length; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    bool mine = b >= First && b < First + Names.Length;
                    Physics2D.IgnoreLayerCollision(a, b, mine ? !Collides(a, b) : true);
                }
            }
        }
    }
}
