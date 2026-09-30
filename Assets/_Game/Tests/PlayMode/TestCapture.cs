using System.Collections.Generic;
using System.IO;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Tests
{
    /// <summary>Screenshots from play-mode tests (camera + overlay canvases rendered into a 1920×1080 texture).</summary>
    public static class TestCapture
    {
        public static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));

        public static void Shot(string name)
        {
            var root = GameRoot.Instance;
            if (root == null || root.MainCamera == null) return;
            Directory.CreateDirectory(Dir);
            var tex = Grab(root.MainCamera);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log($"[PALINODE] screenshot {name}");
        }

        public static Texture2D Grab(Camera cam)
        {
            const int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            float aspect = cam.aspect;
            cam.targetTexture = rt;
            cam.aspect = 16f / 9f;
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
            int mask = cam.cullingMask;
            cam.cullingMask |= 1 << GameRoot.UILayer;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            cam.cullingMask = mask;
            foreach (var c in switched)
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.worldCamera = null;
                c.sortingOrder -= 30000;
            }
            cam.targetTexture = prev;
            cam.aspect = aspect;
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
