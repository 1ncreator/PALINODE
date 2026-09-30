using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using Palinode.Core;
using Palinode.Cutscene;
using Palinode.Gameplay;
using Palinode.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Palinode.Tests
{
    /// <summary>
    /// Plays the prologue with the interactive scenes auto-piloted, saving a PNG for every "marker" step into
    /// &lt;project&gt;/Screenshots. Passes when Floor I starts (or PALINODE_TEST_TO chapter is reached).
    ///
    /// Markers named "seq_&lt;name&gt;|crop=x,y,w,h|n=12|dt=0.08" record an animation contact sheet: the game drops to
    /// ×1 speed, captures n frames dt seconds of game time apart (Time.captureDeltaTime; crop in 1920×1080 screen pixels)
    /// and restores the speed.
    ///
    /// Environment: PALINODE_TEST_SPEED (default 10), PALINODE_TEST_LANG (RU), PALINODE_TEST_FROM / PALINODE_TEST_TO
    /// (chapter ids), PALINODE_TEST_MENU_ONLY=1.
    /// </summary>
    public sealed class PrologueSmokeTest
    {
        private readonly Queue<string> _pendingShots = new Queue<string>();
        private readonly List<string> _markers = new List<string>();
        private string _shotDir;
        private string _prefix = string.Empty;

        private void OnMarker(string id)
        {
            _markers.Add(id);
            _pendingShots.Enqueue(id);
        }

        private static string Env(string k) => System.Environment.GetEnvironmentVariable(k);

        [UnityTest]
        [Timeout(3600000)]
        public IEnumerator FullPrologue_ReachesFloorOne()
        {
            _shotDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(_shotDir);
            float speed = 10f;
            if (float.TryParse(Env("PALINODE_TEST_SPEED"), NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) speed = s;
            string from = Env("PALINODE_TEST_FROM"), to = Env("PALINODE_TEST_TO");

            string lang = Env("PALINODE_TEST_LANG");
            string prefix = string.IsNullOrEmpty(lang) ? string.Empty : lang.ToUpperInvariant() + "_";
            GameSettings settings = null;
            Language prevText = Language.EN, prevVoice = Language.EN;

            CutsceneContext.MarkerReached += OnMarker;
            LevelController.AutoPilotEnabled = true;
            try
            {
                SceneManager.LoadScene("Menu");
                yield return null;
                yield return null;
                var root = GameRoot.Instance;
                Assert.IsNotNull(root, "GameRoot was not created by the menu scene");
                settings = root.Settings;
                prevText = settings.TextLanguage;
                prevVoice = settings.VoiceLanguage;
                if (prefix == "RU_")
                {
                    settings.SetTextLanguage(Language.RU);
                    settings.SetVoiceLanguage(Language.RU);
                }
                _prefix = prefix;
                yield return new WaitForSecondsRealtime(2.5f);
                Capture(root, "A_menu", null);
                if (Env("PALINODE_TEST_MENU_ONLY") == "1") yield break;
                root.SpeedMultiplier = speed;

                int start = 0;
                if (!string.IsNullOrEmpty(from))
                {
                    start = new List<string>(root.Flow.Order).IndexOf(from);
                    Assert.GreaterOrEqual(start, 0, "Unknown PALINODE_TEST_FROM chapter " + from);
                }
                root.Flow.GoTo(start);

                int runs0 = Palinode.Floor.FloorController.RunsStarted;
                bool Reached() => Palinode.Floor.FloorController.RunsStarted > runs0;
                float timeout = Time.realtimeSinceStartup + 3000f;
                bool reachedTo = false;
                while (!Reached() && Time.realtimeSinceStartup < timeout)
                {
                    if (!string.IsNullOrEmpty(to) && root.Flow.CurrentChapterId == to) { reachedTo = true; break; }
                    if (root.MainCamera != null) root.MainCamera.aspect = 16f / 9f;
                    while (_pendingShots.Count > 0)
                    {
                        string id = _pendingShots.Dequeue();
                        if (id.StartsWith("seq_")) yield return CaptureSequence(root, id, speed);
                        else Capture(root, id, null);
                    }
                    yield return null;
                }
                Assert.IsTrue(Reached() || reachedTo,
                    "Prologue did not reach the end. Markers: " + string.Join(", ", _markers));
                if (Reached())
                {
                    yield return new WaitForSecondsRealtime(2.5f);
                    Capture(root, "Z_floor1", null);
                }
                root.SpeedMultiplier = 1f;
            }
            finally
            {
                CutsceneContext.MarkerReached -= OnMarker;
                LevelController.AutoPilotEnabled = false;
                if (settings != null)
                {
                    settings.SetTextLanguage(prevText);
                    settings.SetVoiceLanguage(prevVoice);
                }
            }
        }

        private IEnumerator CaptureSequence(GameRoot root, string spec, float restoreSpeed)
        {
            var parts = spec.Split('|');
            string name = parts[0];
            RectInt crop = new RectInt(0, 0, 1920, 1080);
            int n = 12;
            float dt = 0.08f;
            foreach (var p in parts)
            {
                if (p.StartsWith("crop="))
                {
                    var v = p.Substring(5).Split(',');
                    crop = new RectInt(int.Parse(v[0]), int.Parse(v[1]), int.Parse(v[2]), int.Parse(v[3]));
                }
                else if (p.StartsWith("n=")) n = int.Parse(p.Substring(2));
                else if (p.StartsWith("dt=")) dt = float.Parse(p.Substring(3), CultureInfo.InvariantCulture);
            }
            // Fixed game-time step per rendered frame: the frames are exactly dt apart in game time, however slow
            // batchmode rendering is.
            root.SpeedMultiplier = 1f;
            Time.captureDeltaTime = dt;
            yield return null;
            var frames = new List<Texture2D>();
            try
            {
                for (int i = 0; i < n; i++)
                {
                    frames.Add(Grab(root, crop));
                    yield return null;
                }
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                root.SpeedMultiplier = restoreSpeed;
            }

            // Contact sheet: 4 columns, frames scaled to ≤ 480 px wide.
            int cols = 4, rows = (n + cols - 1) / cols;
            float sc = Mathf.Min(1f, 480f / crop.width);
            int fw = Mathf.RoundToInt(crop.width * sc), fh = Mathf.RoundToInt(crop.height * sc);
            var sheet = new Texture2D(cols * fw + (cols + 1) * 4, rows * fh + (rows + 1) * 4, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(24, 24, 24, 255);
            sheet.SetPixels32(fill);
            for (int i = 0; i < frames.Count; i++)
            {
                int cx = i % cols, cy = rows - 1 - i / cols;
                for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                    sheet.SetPixel(4 + cx * (fw + 4) + x, 4 + cy * (fh + 4) + y, frames[i].GetPixelBilinear((x + 0.5f) / fw, (y + 0.5f) / fh));
                Object.Destroy(frames[i]);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(_shotDir, _prefix + name + ".png"), sheet.EncodeToPNG());
            Object.Destroy(sheet);
            Debug.Log($"[PALINODE] sequence {name}: {n} frames × {dt}s");
        }

        private Texture2D Grab(GameRoot root, RectInt crop)
        {
            var cam = root.MainCamera;
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var switched = SwitchCanvases(cam);
            int prevMask = cam.cullingMask;
            cam.cullingMask |= 1 << GameRoot.UILayer;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.cullingMask = prevMask;
            RestoreCanvases(switched);
            cam.targetTexture = prev;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(crop.width, crop.height, TextureFormat.RGB24, false);
            // ReadPixels origin is bottom-left; crop is given top-left.
            tex.ReadPixels(new Rect(crop.x, h - crop.y - crop.height, crop.width, crop.height), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static List<Canvas> SwitchCanvases(Camera cam)
        {
            // UI canvases are Screen Space - Overlay; for the capture draw them with the camera (they then also get
            // the camera's post-processing, which the real game does not apply to UI).
            var switched = new List<Canvas>();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.05f + 0.001f * (200 - c.sortingOrder);
                c.sortingOrder += 30000;
                switched.Add(c);
            }
            return switched;
        }

        private static void RestoreCanvases(List<Canvas> switched)
        {
            foreach (var c in switched)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.worldCamera = null;
                c.sortingOrder -= 30000;
            }
        }

        private void Capture(GameRoot root, string id, RectInt? crop)
        {
            if (root.MainCamera == null) return;
            var tex = Grab(root, crop ?? new RectInt(0, 0, 1920, 1080));
            File.WriteAllBytes(Path.Combine(_shotDir, _prefix + id + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log($"[PALINODE] screenshot {id} (subtitle: '{root.Overlay.CurrentSubtitle}', fade: {root.Overlay.FadeAlpha:0.00})");
        }
    }
}
