using System.Collections.Generic;
using Palinode.Cutscene;
using Palinode.Gameplay;
using Palinode.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Stage = Palinode.Cutscene.Stage;

namespace Palinode.Editor
{
    /// <summary>Generates the game's Unity scenes (thin shells: camera, light, volume, bootstrap component).</summary>
    public static class SceneBuilder
    {
        public const string ScenesDir = "Assets/_Game/Prologue/Scenes";

        public static readonly string[] SceneOrder =
        {
            "Menu", "Prologue_Cinematic", "Prologue_Street", "Prologue_PrintHouse", "Floor1"
        };

        public const string Floor1Dir = "Assets/_Game/Floor1/Scenes";

        public static string PathOf(string scene) => scene.StartsWith("Floor") ? $"{Floor1Dir}/{scene}.unity" : $"{ScenesDir}/{scene}.unity";

        public static void BuildAll(VolumeProfile profile)
        {
            RenderSetup.EnsureFolder(ScenesDir);
            BuildMenu(profile);
            BuildCinematic(profile);
            BuildLevelScene("Prologue_Street", "street", profile);
            BuildLevelScene("Prologue_PrintHouse", "printhouse", profile);
            BuildFloor1(profile);

            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in SceneOrder) list.Add(new EditorBuildSettingsScene(PathOf(s), true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static Scene NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        private static Camera MakeCamera(Color background)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.cullingMask = ~(1 << 5);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.SetRenderer(0);
            return cam;
        }

        private static Light2D MakeGlobalLight(float intensity)
        {
            var go = new GameObject("Global Light 2D");
            var l = go.AddComponent<Light2D>();
            l.lightType = Light2D.LightType.Global;
            l.intensity = intensity;
            l.color = Color.white;
            var ids = new int[SortingLayer.layers.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = SortingLayer.layers[i].id;
            l.targetSortingLayers = ids;
            return l;
        }

        private static void MakeVolume(VolumeProfile profile)
        {
            var go = new GameObject("Global Volume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 0;
            v.sharedProfile = profile;
        }

        private static void Save(Scene scene, string name)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, PathOf(name));
        }

        private static void BuildMenu(VolumeProfile profile)
        {
            var scene = NewScene();
            var cam = MakeCamera(Color.black);
            MakeGlobalLight(1f);
            MakeVolume(profile);
            var menu = new GameObject("Menu").AddComponent<MenuController>();
            menu.Configure(cam);
            Save(scene, "Menu");
        }

        private static void BuildCinematic(VolumeProfile profile)
        {
            var scene = NewScene();
            var cam = MakeCamera(Color.black);
            var light = MakeGlobalLight(1f);
            MakeVolume(profile);
            var stage = new GameObject("Stage").AddComponent<Stage>();
            var host = new GameObject("ChapterHost").AddComponent<ChapterHost>();
            host.Configure(cam, stage, light, null);
            Save(scene, "Prologue_Cinematic");
        }

        private static void BuildLevelScene(string sceneName, string levelId, VolumeProfile profile)
        {
            var scene = NewScene();
            var cam = MakeCamera(Color.black);
            var light = MakeGlobalLight(0.6f);
            MakeVolume(profile);
            var level = new GameObject("Level").AddComponent<LevelController>();
            level.Configure(levelId, cam, light);
            var stage = new GameObject("Stage").AddComponent<Stage>();
            var host = new GameObject("ChapterHost").AddComponent<ChapterHost>();
            host.Configure(cam, stage, light, level);
            Save(scene, sceneName);
        }

        private static void BuildFloor1(VolumeProfile profile)
        {
            RenderSetup.EnsureFolder(Floor1Dir);
            var scene = NewScene();
            var cam = MakeCamera(new Color(0.02f, 0.015f, 0.015f));
            var light = MakeGlobalLight(1f);
            MakeVolume(profile);
            var floor = new GameObject("Floor").AddComponent<Palinode.Floor.FloorController>();
            floor.Configure(cam, light);
            Save(scene, "Floor1");
            // The old "Floor I — coming soon" placeholder is gone.
            if (System.IO.File.Exists($"{ScenesDir}/Placeholder_Floor1.unity")) AssetDatabase.DeleteAsset($"{ScenesDir}/Placeholder_Floor1.unity");
        }
    }
}
