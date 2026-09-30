using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Palinode.Core
{
    [Serializable]
    public struct NamedSprite
    {
        public string name;
        public Sprite sprite;
    }

    [Serializable]
    public struct NamedClip
    {
        public string name;
        public AudioClip clip;
    }

    [Serializable]
    public struct NamedMaterial
    {
        public string name;
        public Material material;
    }

    [Serializable]
    public struct NamedFont
    {
        public string name;
        public TMP_FontAsset font;
    }

    [Serializable]
    public struct NamedText
    {
        public string name;
        public TextAsset text;
    }

    /// <summary>
    /// Single entry point for runtime data: the prologue script, localization table and a name → asset registry.
    /// Built and kept up to date by Palinode.Editor.ProjectBuilder; loaded from Resources so every scene can boot standalone.
    /// </summary>
    [CreateAssetMenu(menuName = "PALINODE/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "PalinodeConfig";

        [SerializeField] private TextAsset prologueJson;
        [SerializeField] private TextAsset localizationCsv;
        [SerializeField] private List<NamedSprite> sprites = new List<NamedSprite>();
        [SerializeField] private List<NamedClip> clips = new List<NamedClip>();
        [SerializeField] private List<NamedMaterial> materials = new List<NamedMaterial>();
        [SerializeField] private TMP_FontAsset handwritingFont;
        [SerializeField] private TMP_FontAsset monoFont;
        [SerializeField] private TMP_FontAsset titleFont;
        [SerializeField] private List<NamedFont> fonts = new List<NamedFont>();
        [SerializeField] private TextAsset audioLevels;
        [SerializeField] private TextAsset artMeta;
        [SerializeField] private List<NamedText> texts = new List<NamedText>();

        /// <summary>Named data file (e.g. "floor1", "floor1_audio"); null if not registered.</summary>
        public TextAsset Text(string name)
        {
            foreach (var t in texts) if (t.name == name) return t.text;
            return null;
        }

        private Dictionary<string, Sprite> _spriteMap;
        private Dictionary<string, float> _lufs;

        public TextAsset AudioLevels => audioLevels;

        private JNode _artMeta;

        /// <summary>Named point of a processed sprite from processed_meta.json ("tip", "elbow"), image px, top-left origin.</summary>
        public Vector2? ArtPoint(string sprite, string key)
        {
            if (artMeta == null) return null;
            if (_artMeta.IsNull) _artMeta = JNode.Parse(artMeta.text);
            var n = _artMeta[sprite][key];
            if (!n.IsArray) return null;
            return new Vector2(n[0].AsFloat(), n[1].AsFloat());
        }

        /// <summary>Measured integrated loudness of a clip (LUFS) from Data/audio_levels.json; NaN if unknown.</summary>
        public float ClipLufs(string clipName)
        {
            if (_lufs == null)
            {
                _lufs = new Dictionary<string, float>(StringComparer.Ordinal);
                if (audioLevels != null)
                    foreach (var kv in JNode.Parse(audioLevels.text)["clips"].Pairs()) _lufs[kv.Key] = kv.Value.Num("lufs", float.NaN);
            }
            return clipName != null && _lufs.TryGetValue(clipName, out var l) ? l : float.NaN;
        }

        public JNode ClipLevels(string clipName)
        {
            if (audioLevels == null || clipName == null) return default;
            return JNode.Parse(audioLevels.text)["clips"][clipName];
        }
        private Dictionary<string, AudioClip> _clipMap;
        private Dictionary<string, Material> _materialMap;

        public TextAsset PrologueJson => prologueJson;
        public TextAsset LocalizationCsv => localizationCsv;
        public TMP_FontAsset HandwritingFont => handwritingFont;
        public TMP_FontAsset MonoFont => monoFont;
        public TMP_FontAsset TitleFont => titleFont;
        public IReadOnlyList<NamedSprite> Sprites => sprites;
        public IReadOnlyList<NamedClip> Clips => clips;

        public static GameConfig Load() => Resources.Load<GameConfig>(ResourcePath);

        public TMP_FontAsset Font(string id)
        {
            foreach (var f in fonts)
                if (f.name == id && f.font != null) return f.font;
            switch (id)
            {
                case "hand": return handwritingFont;
                case "mono": return monoFont;
                default: return titleFont;
            }
        }

        public Sprite Sprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_spriteMap == null)
            {
                _spriteMap = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                foreach (var s in sprites) if (s.sprite != null) _spriteMap[s.name] = s.sprite;
            }
            _spriteMap.TryGetValue(name, out var sprite);
            if (sprite == null) Debug.LogWarning($"[PALINODE] Sprite '{name}' is not registered in GameConfig.");
            return sprite;
        }

        public bool HasSprite(string name)
        {
            foreach (var s in sprites) if (s.name == name && s.sprite != null) return true;
            return false;
        }

        /// <summary>Clip lookup. Voice clips are registered as "en/VO_x" and "ru/VO_x".</summary>
        public AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_clipMap == null)
            {
                _clipMap = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
                foreach (var c in clips) if (c.clip != null) _clipMap[c.name] = c.clip;
            }
            _clipMap.TryGetValue(name, out var clip);
            if (clip == null) Debug.LogWarning($"[PALINODE] Audio clip '{name}' is not registered in GameConfig.");
            return clip;
        }

        public bool HasClip(string name)
        {
            foreach (var c in clips) if (c.name == name && c.clip != null) return true;
            return false;
        }

        public Material Material(string name)
        {
            if (_materialMap == null)
            {
                _materialMap = new Dictionary<string, Material>(StringComparer.Ordinal);
                foreach (var m in materials) if (m.material != null) _materialMap[m.name] = m.material;
            }
            _materialMap.TryGetValue(name, out var mat);
            return mat;
        }

#if UNITY_EDITOR
        public void EditorAssign(TextAsset prologue, TextAsset loc, List<NamedSprite> spriteList, List<NamedClip> clipList,
            List<NamedMaterial> materialList, TMP_FontAsset hand, TMP_FontAsset mono, TMP_FontAsset title)
        {
            prologueJson = prologue;
            localizationCsv = loc;
            sprites = spriteList;
            clips = clipList;
            materials = materialList;
            handwritingFont = hand;
            monoFont = mono;
            titleFont = title;
            _spriteMap = null;
            _clipMap = null;
            _materialMap = null;
        }

        public void EditorAssignTexts(List<NamedText> list) => texts = list ?? new List<NamedText>();

        public void EditorAssignExtra(TextAsset levels, List<NamedFont> fontList, TextAsset meta = null)
        {
            audioLevels = levels;
            artMeta = meta;
            _artMeta = default;
            fonts = fontList ?? new List<NamedFont>();
            _lufs = null;
        }
#endif
    }
}
