using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Palinode.Editor
{
    /// <summary>URP pipeline asset with two 2D renderers (world + imprint feature, UI), post-processing profile, materials.</summary>
    public static class RenderSetup
    {
        public const string Dir = "Assets/_Game/Settings";
        public const string PipelinePath = Dir + "/PALINODE_URP.asset";
        public const string MainRendererPath = Dir + "/Renderer2D_World.asset";
        public const string UIRendererPath = Dir + "/Renderer2D_UI.asset";
        public const string ProfilePath = Dir + "/PostProcess_Profile.asset";
        public const string MaterialsDir = Dir + "/Materials";

        public static VolumeProfile Profile { get; private set; }

        public static Dictionary<string, Material> Run()
        {
            EnsureFolder(Dir);
            EnsureFolder(MaterialsDir);

            var main = AssetDatabase.LoadAssetAtPath<Renderer2DData>(MainRendererPath) ?? CreateRenderer(MainRendererPath);
            var ui = AssetDatabase.LoadAssetAtPath<Renderer2DData>(UIRendererPath) ?? CreateRenderer(UIRendererPath);

            var mats = CreateMaterials();
            EnsureImprintFeature(main, mats["Imprint"]);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(main);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var so = new SerializedObject(pipeline);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = main;
            list.GetArrayElementAtIndex(1).objectReferenceValue = ui;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            SetBool(so, "m_SupportsHDR", true);
            SetInt(so, "m_MSAA", 1);
            SetFloat(so, "m_RenderScale", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            Profile = CreateProfile();
            AssetDatabase.SaveAssets();
            return mats;
        }

        private static Renderer2DData CreateRenderer(string path)
        {
            // Use URP's own factory (internal) so default resources/post-process data are filled in exactly like the menu does.
            var menus = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEditor.Rendering.Universal.Renderer2DMenus")
                        ?? FindType("UnityEditor.Rendering.Universal.Renderer2DMenus");
            var rendererType = FindType("UnityEngine.Rendering.Universal.RendererType");
            if (menus != null && rendererType != null)
            {
                var m = menus.GetMethod("CreateRendererAsset", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (m != null)
                {
                    object type2D = Enum.Parse(rendererType, "_2DRenderer");
                    var data = m.Invoke(null, new object[] { path, type2D, false, "Renderer" }) as Renderer2DData;
                    if (data != null) return data;
                }
            }
            Debug.LogWarning("[PALINODE] Falling back to plain Renderer2DData creation.");
            var inst = ScriptableObject.CreateInstance<Renderer2DData>();
            AssetDatabase.CreateAsset(inst, path);
            return inst;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static void EnsureImprintFeature(Renderer2DData data, Material material)
        {
            FullScreenPassRendererFeature feature = null;
            foreach (var f in data.rendererFeatures)
                if (f is FullScreenPassRendererFeature fs && f.name == "Imprint") feature = fs;
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = "Imprint";
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
            }
            feature.passMaterial = material;
            feature.passIndex = 0;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.Color;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);

            // Keep the feature map (local file ids) in sync, as URP's editor does.
            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize = features.arraySize;
            for (int i = 0; i < features.arraySize; i++)
            {
                var obj = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (obj != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string _, out long localId))
                    map.GetArrayElementAtIndex(i).longValue = localId;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var result = new Dictionary<string, Material>
            {
                ["SpriteLit"] = Mat("SpriteLit", "Universal Render Pipeline/2D/Sprite-Lit-Default"),
                ["SpriteUnlit"] = Mat("SpriteUnlit", "Universal Render Pipeline/2D/Sprite-Unlit-Default"),
                ["ParticleAlpha"] = Mat("ParticleAlpha", "Palinode/ParticleAlpha"),
                ["ParticleAdd"] = Mat("ParticleAdd", "Palinode/ParticleAdd"),
                ["GlowAdd"] = Mat("GlowAdd", "Palinode/SpriteAdd"),
                ["RainGlass"] = Mat("RainGlass", "Palinode/RainGlass"),
                ["Fog"] = Mat("Fog", "Palinode/Fog"),
                ["Imprint"] = Mat("Imprint", "Palinode/Imprint"),
                ["InkSDF"] = Mat("InkSDF", "Palinode/InkSDF"),
                ["Ghost"] = Mat("Ghost", "Palinode/SpriteGhost"),
                ["Occluder"] = Mat("Occluder", "Universal Render Pipeline/2D/Sprite-Lit-Default"),
                ["Solid"] = Mat("Solid", "Palinode/SpriteSolid"),
            };
            result["Ghost"].SetFloat("_Base", 0.25f);
            result["Ghost"].SetFloat("_Detail", 2.2f);
            return result;
        }

        private static Material Mat(string name, string shaderName)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception($"Shader '{shaderName}' not found (material {name}).");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile CreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(1f, 0.9f, 0.78f));

            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.42f);
            vignette.color.Override(Color.black);

            var grain = Get<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.28f);
            grain.response.Override(0.75f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing)) return existing;
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        private static void SetBool(SerializedObject so, string prop, bool v) { var p = so.FindProperty(prop); if (p != null) p.boolValue = v; }
        private static void SetInt(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.intValue = v; }
        private static void SetFloat(SerializedObject so, string prop, float v) { var p = so.FindProperty(prop); if (p != null) p.floatValue = v; }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
