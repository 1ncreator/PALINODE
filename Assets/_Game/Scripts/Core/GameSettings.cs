using System;
using UnityEngine;

namespace Palinode.Core
{
    public enum Language { EN = 0, RU = 1 }

    /// <summary>Player-facing settings persisted in PlayerPrefs. Text and voice languages are independent.</summary>
    public sealed class GameSettings
    {
        private const string KeyText = "palinode.textLang";
        private const string KeyVoice = "palinode.voiceLang";
        private const string KeySubs = "palinode.subtitles";
        private const string KeyVolume = "palinode.masterVolume";

        public event Action Changed;

        public Language TextLanguage { get; private set; }
        public Language VoiceLanguage { get; private set; }
        public bool Subtitles { get; private set; }
        public float MasterVolume { get; private set; }

        public GameSettings()
        {
            TextLanguage = (Language)Mathf.Clamp(PlayerPrefs.GetInt(KeyText, 0), 0, 1);
            VoiceLanguage = (Language)Mathf.Clamp(PlayerPrefs.GetInt(KeyVoice, 0), 0, 1);
            Subtitles = PlayerPrefs.GetInt(KeySubs, 1) == 1;
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 0.9f));
        }

        public void SetTextLanguage(Language l) { TextLanguage = l; PlayerPrefs.SetInt(KeyText, (int)l); Save(); }
        public void SetVoiceLanguage(Language l) { VoiceLanguage = l; PlayerPrefs.SetInt(KeyVoice, (int)l); Save(); }
        public void SetSubtitles(bool on) { Subtitles = on; PlayerPrefs.SetInt(KeySubs, on ? 1 : 0); Save(); }
        public void SetMasterVolume(float v) { MasterVolume = Mathf.Clamp01(v); PlayerPrefs.SetFloat(KeyVolume, MasterVolume); Save(); }

        public string VoiceFolder => VoiceLanguage == Language.RU ? "ru" : "en";

        private void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
