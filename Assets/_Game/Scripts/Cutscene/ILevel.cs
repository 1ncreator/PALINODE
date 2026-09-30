using System.Collections;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>Hooks an interactive scene exposes to cutscene steps (implemented by Gameplay.LevelController).</summary>
    public interface ILevel
    {
        IEnumerator WaitTrigger(string id);
        IEnumerator WaitInteract(string id, string promptKey);
        void SetPlayerControl(bool enabled);
        void FacePlayer(string direction);
        IEnumerator ActorStep(string type, JNode step, CutsceneContext ctx);
        void OnChapterStart(JNode chapter);
        Transform StageAnchor { get; }
        Camera Camera { get; }
    }
}
