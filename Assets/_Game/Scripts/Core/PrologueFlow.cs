using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Palinode.Core
{
    /// <summary>
    /// Ordered list of prologue chapters ("flow" in prologue.json). Each chapter names the Unity scene that hosts it.
    /// Chapters in the same Unity scene run back to back; otherwise the scene is loaded and its ChapterHost picks
    /// the pending chapter up. After the last chapter the "exitScene" is loaded (Floor I).
    /// </summary>
    public sealed class PrologueFlow
    {
        public interface IHost
        {
            string SceneName { get; }
            void RunChapter(string chapterId);
            void StopChapter();
        }

        private readonly JNode _data;
        private readonly List<string> _order = new List<string>();
        private IHost _host;

        public int Index { get; private set; } = -1;
        public string PendingChapter { get; private set; }
        public bool Active => Index >= 0;
        public IReadOnlyList<string> Order => _order;
        public string ExitScene => _data["exitScene"].AsString("Floor1");
        public string MenuScene => _data["menuScene"].AsString("Menu");
        public JNode Data => _data;

        public event Action<string> ChapterStarted;

        public PrologueFlow(JNode data)
        {
            _data = data;
            foreach (var n in data["flow"].Items()) _order.Add(n.AsString());
        }

        public string CurrentChapterId => Index >= 0 && Index < _order.Count ? _order[Index] : null;

        public JNode Chapter(string id) => _data["chapters"][id];

        public string SceneOf(string chapterId) => Chapter(chapterId).Str("scene", "Prologue_Cinematic");

        public void RegisterHost(IHost host) => _host = host;

        public void UnregisterHost(IHost host)
        {
            if (_host == host) _host = null;
        }

        public void StartNewGame() => GoTo(0);

        public void GoTo(int index)
        {
            _host?.StopChapter();
            if (index >= _order.Count)
            {
                Index = -1;
                PendingChapter = null;
                SceneManager.LoadScene(ExitScene);
                return;
            }
            Index = Mathf.Max(0, index);
            string id = _order[Index];
            string scene = SceneOf(id);
            PendingChapter = id;
            if (_host != null && _host.SceneName == scene && SceneManager.GetActiveScene().name == scene)
            {
                StartPendingOn(_host);
            }
            else
            {
                SceneManager.LoadScene(scene);
            }
        }

        /// <summary>Called by a scene host when it becomes ready. Returns the chapter it should run (or null).</summary>
        public string ClaimPending(IHost host)
        {
            if (PendingChapter != null && SceneOf(PendingChapter) == host.SceneName) return PendingChapter;
            // Scene opened directly (e.g. Play in the editor): start at its first chapter.
            for (int i = 0; i < _order.Count; i++)
            {
                if (SceneOf(_order[i]) != host.SceneName) continue;
                Index = i;
                PendingChapter = _order[i];
                return PendingChapter;
            }
            return null;
        }

        public void StartPendingOn(IHost host)
        {
            string id = PendingChapter;
            PendingChapter = null;
            if (id == null) return;
            ChapterStarted?.Invoke(id);
            host.RunChapter(id);
        }

        public void MarkStarted(string id)
        {
            PendingChapter = null;
            ChapterStarted?.Invoke(id);
        }

        public void CompleteCurrent() => GoTo(Index + 1);

        public void Skip() => GoTo(Index + 1);

        public void ReturnToMenu()
        {
            _host?.StopChapter();
            Index = -1;
            PendingChapter = null;
            SceneManager.LoadScene(MenuScene);
        }
    }
}
