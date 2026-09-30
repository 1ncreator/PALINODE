using Palinode.Core;

namespace Palinode.Floor
{
    /// <summary>Services of the running floor (set by <see cref="FloorController"/>), so actors need no wiring.</summary>
    public static class Game
    {
        public static FloorController Ctrl;
        public static FloorDB DB;
        public static FloorAudio Audio;
        public static FloorFx Fx;
        public static ShotPool Shots;
        public static MetaSave Meta;
        public static RunState Run;
        public static Elias Player;
        public static RoomView Current;
        public static FloorCamera Camera;

        /// <summary>Debug / test cheats.</summary>
        public static bool GodMode;
        public static bool AutoKill;

        public static GameRoot Root => GameRoot.Instance;
        public static string T(string key) => Root != null ? Root.T(key) : key;

        public static void Clear()
        {
            Ctrl = null; DB = null; Audio = null; Fx = null; Shots = null; Player = null; Current = null; Camera = null; Run = null;
        }
    }
}
