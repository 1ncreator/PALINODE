using System.Collections;
using System.IO;
using NUnit.Framework;
using Palinode.Core;
using Palinode.Cutscene;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Palinode.Tests
{
    /// <summary>
    /// Renders the manuscript page (all lines, strikes, red insertion) with each candidate handwriting font in EN and
    /// RU to Screenshots/Fonts_&lt;font&gt;_&lt;lang&gt;.png, so fonts can be compared side by side.
    /// </summary>
    public sealed class HandwritingSamplesTest
    {
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator RenderFontSamples()
        {
            var root = GameRoot.Ensure();
            var scene = SceneManager.CreateScene("FontSamples");
            SceneManager.SetActiveScene(scene);
            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.aspect = 16f / 9f;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            var lightGo = new GameObject("Global");
            var gl = StageLight.CreateLight(lightGo, Light2D.LightType.Global);
            gl.intensity = 1f;
            var stage = new GameObject("Stage").AddComponent<Stage>();
            stage.Init(root.Config, 0, true);

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(dir);
            var prevText = root.Settings.TextLanguage;
            try
            {
                foreach (var font in new[] { "hand_marck", "hand_bad" })
                foreach (var lang in new[] { Language.EN, Language.RU })
                {
                    root.Settings.SetTextLanguage(lang);
                    stage.ClearAll();
                    int g = stage.NextGroup();
                    stage.CreateLayer(JNode.Parse("{\"id\":\"desk\",\"sprite\":\"P1-03_desk\"}"), g);
                    stage.CreateLayer(JNode.Parse("{\"id\":\"page\",\"type\":\"page\",\"parent\":\"desk\",\"px\":[733,512],\"rot\":2.0," +
                                                  "\"size\":[480,580],\"top\":62,\"left\":30,\"lineHeight\":66,\"fontPx\":40,\"order\":2,\"font\":\"" + font + "\"}"), g);
                    stage.CamZoom = 1.5f;
                    stage.CamPos = new Vector2(-0.45f, 1.1f);
                    var page = stage.Get("page").GetComponent<HandwritingPage>();
                    page.Preset(0, root.T("PAGE_TITLE"), "center", 1.3f, false);
                    page.Preset(1, root.T("PAGE_THEN_SHE"), "left", 1f, true);
                    page.Preset(2, root.T("PAGE_L1"), "left", 1f, true);
                    page.Preset(3, root.T("PAGE_L2"), "left", 1f, true);
                    page.Preset(4, root.T("PAGE_L3"), "left", 1f, false);
                    var ins = page.Insert(4, root.T("W_DRUNK"), root.T("W_INSERT_ANCHOR"), new Color(0.66f, 0.07f, 0.08f), 60f, null);
                    while (ins.MoveNext()) yield return ins.Current;
                    var dot = page.Dot(4, "lineEnd", new Color(0.66f, 0.07f, 0.08f), 12f, null);
                    while (dot.MoveNext()) yield return dot.Current;
                    for (int i = 0; i < 3; i++) yield return null;

                    var rt = RenderTexture.GetTemporary(1920, 1080, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                    File.WriteAllBytes(Path.Combine(dir, $"Fonts_{font}_{lang}.png"), tex.EncodeToPNG());
                    Object.Destroy(tex);
                }
            }
            finally
            {
                root.Settings.SetTextLanguage(prevText);
            }
            Assert.Pass();
        }
    }
}
