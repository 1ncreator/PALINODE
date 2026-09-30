using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Geometry of one placed room: the painted background is scaled uniformly so its calibrated floor rectangle is
    /// exactly <see cref="Cells"/> cells wide (1 cell = 1 world unit across); a cell's height on screen follows the
    /// painting's perspective (≈ 0.75). Rows count from the top (back) wall, columns from the left.
    /// </summary>
    public sealed class RoomFrame
    {
        public readonly BackgroundSpec Spec;
        public readonly Vector2 Center;          // world centre of the floor rectangle
        public readonly Vector2Int Cells;
        public readonly Vector2 CellSize;        // world size of a cell (1, ~0.75)
        public readonly float PxToUnit;          // world units per image pixel
        public readonly Vector2 ImageCenter;     // world position of the background sprite's centre
        public readonly Vector2 ImageSize;       // world size of the background

        public RoomFrame(BackgroundSpec spec, Vector2 center, Vector2 imagePx)
        {
            Spec = spec;
            Center = center;
            Cells = spec.Cells;
            PxToUnit = Cells.x / (float)spec.FloorPx.width;
            CellSize = new Vector2(1f, spec.FloorPx.height * PxToUnit / Cells.y);
            ImageSize = imagePx * PxToUnit;
            Vector2 floorCenterPx = new Vector2(spec.FloorPx.x + spec.FloorPx.width * 0.5f, spec.FloorPx.y + spec.FloorPx.height * 0.5f);
            ImageCenter = center + new Vector2(imagePx.x * 0.5f - floorCenterPx.x, -(imagePx.y * 0.5f - floorCenterPx.y)) * PxToUnit;
        }

        public Vector2 FloorSize => new Vector2(Cells.x * CellSize.x, Cells.y * CellSize.y);
        public Rect FloorRect => new Rect(Center - FloorSize * 0.5f, FloorSize);
        public Rect ImageRect => new Rect(ImageCenter - ImageSize * 0.5f, ImageSize);

        /// <summary>World centre of a cell (column, row-from-top).</summary>
        public Vector2 CellCenter(int col, int row) =>
            new Vector2(FloorRect.xMin + (col + 0.5f) * CellSize.x, FloorRect.yMax - (row + 0.5f) * CellSize.y);

        public Vector2 CellCenter(Vector2Int c) => CellCenter(c.x, c.y);

        /// <summary>Bottom middle of a cell (where things standing in it put their feet).</summary>
        public Vector2 CellBase(int col, int row) => CellCenter(col, row) - new Vector2(0f, CellSize.y * 0.35f);

        public Vector2Int CellAt(Vector2 world)
        {
            var r = FloorRect;
            int col = Mathf.Clamp(Mathf.FloorToInt((world.x - r.xMin) / CellSize.x), 0, Cells.x - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((r.yMax - world.y) / CellSize.y), 0, Cells.y - 1);
            return new Vector2Int(col, row);
        }

        public bool InsideFloor(Vector2 world, float margin = 0f)
        {
            var r = FloorRect;
            return world.x >= r.xMin + margin && world.x <= r.xMax - margin && world.y >= r.yMin + margin && world.y <= r.yMax - margin;
        }

        public Vector2 ClampToFloor(Vector2 world, float margin)
        {
            var r = FloorRect;
            return new Vector2(Mathf.Clamp(world.x, r.xMin + margin, r.xMax - margin), Mathf.Clamp(world.y, r.yMin + margin, r.yMax - margin));
        }

        /// <summary>Image pixel (top-left origin) → world.</summary>
        public Vector2 PxToWorld(Vector2 px)
        {
            var img = ImageRect;
            return new Vector2(img.xMin + px.x * PxToUnit, img.yMax - px.y * PxToUnit);
        }

        public Rect PxRectToWorld(RectInt px)
        {
            Vector2 a = PxToWorld(new Vector2(px.x, px.y + px.height));
            return new Rect(a, new Vector2(px.width, px.height) * PxToUnit);
        }

        /// <summary>Interior cell next to a door on the given wall of grid cell <paramref name="gridCell"/> (relative to the room's origin cell).</summary>
        public Vector2Int DoorCell(Vector2Int gridCell, Vector2Int roomGridSize, Direction side)
        {
            int cw = Cells.x / Mathf.Max(1, roomGridSize.x), ch = Cells.y / Mathf.Max(1, roomGridSize.y);
            // grid y goes up, rows go down
            int gxCol = gridCell.x * cw, gyRow = (roomGridSize.y - 1 - gridCell.y) * ch;
            switch (side)
            {
                case Direction.Up: return new Vector2Int(gxCol + cw / 2, gyRow);
                case Direction.Down: return new Vector2Int(gxCol + cw / 2, gyRow + ch - 1);
                case Direction.Left: return new Vector2Int(gxCol, gyRow + ch / 2);
                default: return new Vector2Int(gxCol + cw - 1, gyRow + ch / 2);
            }
        }

        /// <summary>Point on the floor edge in the middle of a door gap.</summary>
        public Vector2 DoorEdge(Vector2Int doorCell, Direction side)
        {
            var c = CellCenter(doorCell);
            var r = FloorRect;
            switch (side)
            {
                case Direction.Up: return new Vector2(c.x, r.yMax);
                case Direction.Down: return new Vector2(c.x, r.yMin);
                case Direction.Left: return new Vector2(r.xMin, c.y);
                default: return new Vector2(r.xMax, c.y);
            }
        }
    }
}
