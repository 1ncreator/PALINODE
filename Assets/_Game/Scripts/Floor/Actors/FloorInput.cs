using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Palinode.Floor
{
    /// <summary>
    /// Polls keyboard and gamepad without allocations (ported from RogueDungeon's PlayerInputReader).
    /// Move: WASD / left stick. Shoot: arrows (last pressed wins) / right stick, 4 directions. Space / RB: active item.
    /// E / A: interact. Tab / Select: map. Esc / Start: pause.
    /// </summary>
    public sealed class FloorInput
    {
        private const float StickDeadZone = 0.2f;
        private const float AimDeadZone = 0.5f;

        private readonly Direction[] _held = new Direction[4];
        private int _heldCount;

        public Vector2 Move { get; private set; }
        public bool IsShooting { get; private set; }
        public Direction ShootDirection { get; private set; } = Direction.Down;
        public bool ActivePressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool MapHeld { get; private set; }
        public bool MapPressed { get; private set; }
        public bool PausePressed { get; private set; }

        /// <summary>Autopilot (tests): overrides movement and shooting when set.</summary>
        public Vector2? BotMove;
        public Direction? BotShoot;
        public bool BotInteract;

        public void Update()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            Vector2 move = Vector2.zero;
            ActivePressed = InteractPressed = MapPressed = PausePressed = false;
            MapHeld = false;

            if (kb != null)
            {
                if (kb.wKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed) move.x -= 1f;
                move = move.normalized;
                Track(Direction.Up, kb.upArrowKey);
                Track(Direction.Down, kb.downArrowKey);
                Track(Direction.Left, kb.leftArrowKey);
                Track(Direction.Right, kb.rightArrowKey);
                ActivePressed |= kb.spaceKey.wasPressedThisFrame;
                InteractPressed |= kb.eKey.wasPressedThisFrame;
                MapPressed |= kb.tabKey.wasPressedThisFrame;
                MapHeld |= kb.tabKey.isPressed;
                PausePressed |= kb.escapeKey.wasPressedThisFrame;
            }
            else _heldCount = 0;

            IsShooting = _heldCount > 0;
            if (IsShooting) ShootDirection = _held[_heldCount - 1];

            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > StickDeadZone * StickDeadZone) move += stick;
                if (!IsShooting)
                {
                    Vector2 aim = gp.rightStick.ReadValue();
                    if (aim.sqrMagnitude > AimDeadZone * AimDeadZone)
                    {
                        IsShooting = true;
                        ShootDirection = DirectionUtil.FromVector(aim);
                    }
                }
                ActivePressed |= gp.rightShoulder.wasPressedThisFrame;
                InteractPressed |= gp.buttonSouth.wasPressedThisFrame;
                MapPressed |= gp.selectButton.wasPressedThisFrame;
                MapHeld |= gp.selectButton.isPressed;
                PausePressed |= gp.startButton.wasPressedThisFrame;
            }

            Move = Vector2.ClampMagnitude(move, 1f);
            if (BotMove.HasValue) Move = Vector2.ClampMagnitude(BotMove.Value, 1f);
            if (BotShoot.HasValue) { IsShooting = true; ShootDirection = BotShoot.Value; }
            if (BotInteract) InteractPressed = true;
        }

        public void Clear()
        {
            _heldCount = 0;
            Move = Vector2.zero;
            IsShooting = false;
        }

        private void Track(Direction d, KeyControl key)
        {
            if (key.wasPressedThisFrame)
            {
                Remove(d);
                _held[_heldCount++] = d;
            }
            else if (!key.isPressed) Remove(d);
            else if (IndexOf(d) < 0)
            {
                // Held since before tracking started (e.g. after a pause): treat as oldest.
                for (int i = _heldCount; i > 0; i--) _held[i] = _held[i - 1];
                _held[0] = d;
                _heldCount++;
            }
        }

        private int IndexOf(Direction d)
        {
            for (int i = 0; i < _heldCount; i++) if (_held[i] == d) return i;
            return -1;
        }

        private void Remove(Direction d)
        {
            int i = IndexOf(d);
            if (i < 0) return;
            for (int k = i; k < _heldCount - 1; k++) _held[k] = _held[k + 1];
            _heldCount--;
        }
    }
}
