using UnityEngine;

namespace Palinode.Floor
{
    public enum Direction { Up = 0, Right = 1, Down = 2, Left = 3 }

    public static class DirectionUtil
    {
        public static readonly Direction[] All = { Direction.Up, Direction.Right, Direction.Down, Direction.Left };

        /// <summary>Offset on the floor grid (y up).</summary>
        public static Vector2Int ToOffset(this Direction d)
        {
            switch (d)
            {
                case Direction.Up: return new Vector2Int(0, 1);
                case Direction.Right: return new Vector2Int(1, 0);
                case Direction.Down: return new Vector2Int(0, -1);
                default: return new Vector2Int(-1, 0);
            }
        }

        public static Vector2 ToVector(this Direction d)
        {
            var o = d.ToOffset();
            return new Vector2(o.x, o.y);
        }

        public static Direction Opposite(this Direction d) => (Direction)(((int)d + 2) % 4);

        public static Direction FromVector(Vector2 v) =>
            Mathf.Abs(v.x) > Mathf.Abs(v.y) ? (v.x > 0f ? Direction.Right : Direction.Left) : (v.y > 0f ? Direction.Up : Direction.Down);
    }
}
