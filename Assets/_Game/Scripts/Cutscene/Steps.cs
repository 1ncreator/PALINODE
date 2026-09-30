using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.Effects;
using UnityEngine;

namespace Palinode.Cutscene
{
    /// <summary>
    /// Interpreter for prologue.json steps. Every step is {"type": "...", ...}. Common options:
    ///   "async": true  — run in background and continue immediately;
    ///   "delay": s     — wait before executing.
    /// Unknown types are forwarded to the interactive level (actor steps) if one is present.
    /// </summary>
    public static class Steps
    {
        private delegate IEnumerator Handler(JNode s, CutsceneContext c);

        private static readonly Dictionary<string, Handler> Map = new Dictionary<string, Handler>
        {
            { "wait", (s, c) => CutsceneContext.Wait(s.Num("time", 1f)) },
            { "marker", Marker },
            { "shot", Shot },
            { "show", Show },
            { "hide", Hide },
            { "clear", Clear },
            { "camera", CameraMove },
            { "shake", Shake },
            { "vibrate", Vibrate },
            { "fade", Fade },
            { "sfx", Sfx },
            { "stopSfx", StopSfx },
            { "loop", Loop },
            { "stopLoop", (s, c) => Do(() => c.Audio.StopLoop(s.Str("id"), s.Num("fade", 1f))) },
            { "loopVolume", (s, c) => Do(() => c.Audio.SetLoopVolume(s.Str("id"), Vol(s, c, s.Str("clip", s.Str("id")), 1f), s.Num("duration", 1f))) },
            { "handheld", (s, c) => Do(() => { c.Stage.HandheldPx = s.Num("px", 6f); c.Stage.HandheldRot = s.Num("rot", 0.25f); c.Stage.HandheldSpeed = s.Num("speed", 0.35f); }) },
            { "musicLevel", (s, c) => Do(() => c.Audio.SetMusicVolume(Vol(s, c, c.Audio.MusicClipName, 0.3f), s.Num("duration", 2f))) },
            { "mix", (s, c) => Do(() => Debug.Log($"[PALINODE] mix {s.Str("id", "")} {c.Audio.MixReport()}")) },
            { "music", Music },
            { "stopMusic", (s, c) => Do(() => c.Audio.StopMusic(s.Num("fade", 2f))) },
            { "duck", (s, c) => DoWait(() => c.Audio.Duck(s.Num("level", 0.3f), s.Num("duration", 1f)), s.Bool("wait") ? s.Num("duration", 1f) : 0f) },
            { "silence", (s, c) => Do(() => c.Audio.Silence(s.Num("duration", 0f))) },
            { "unsilence", (s, c) => Do(() => c.Audio.Unsilence(s.Num("duration", 0.5f))) },
            { "vo", Voice },
            { "subtitle", Subtitle },
            { "handTo", HandTo },
            { "write", Write },
            { "strike", Strike },
            { "insert", Insert },
            { "dot", Dot },
            { "preset", Preset },
            { "phoneHeader", PhoneHeader },
            { "phoneCall", PhoneCall },
            { "phoneMessage", PhoneMessage },
            { "clock", Clock },
            { "move", Move },
            { "walk", WalkFrames },
            { "alpha", AlphaTo },
            { "tint", TintTo },
            { "sprite", SwapSprite },
            { "flicker", Flicker },
            { "globalLight", GlobalLight },
            { "lightTo", LightTo },
            { "imprint", (s, c) => ImprintController.Animate(s.Num("value", 0f), s.Num("edge", 0f), s.Num("duration", 0f)) },
            { "glitch", (s, c) => ImprintController.GlitchBurst(s.Num("duration", 0.4f), s.Num("strength", 1f)) },
            { "title", Title },
            { "titleSub", TitleSub },
            { "clearTitle", (s, c) => c.Overlay.FadeTitle(0f, s.Num("fade", 1f)) },
            { "stamp", Stamp },
            { "emerge", Emerge },
            { "fx", Fx },
            { "parallel", Parallel },
            { "sequence", (s, c) => RunList(s["steps"], c) },
            { "player", (s, c) => Do(() => c.Level?.SetPlayerControl(s.Bool("enabled", true))) },
            { "playerFace", (s, c) => Do(() => c.Level?.FacePlayer(s.Str("dir", "down"))) },
            { "waitTrigger", (s, c) => c.Level != null ? c.Level.WaitTrigger(s.Str("id")) : null },
            { "waitInteract", (s, c) => c.Level != null ? c.Level.WaitInteract(s.Str("id"), s.Str("prompt", "PROMPT_OPEN")) : null },
        };

        public static IEnumerable<string> KnownTypes => Map.Keys;

        public static IEnumerator Run(JNode step, CutsceneContext ctx)
        {
            string type = step.Str("type");
            IEnumerator routine;
            if (type != null && Map.TryGetValue(type, out var h)) routine = h(step, ctx);
            else if (ctx.Level != null) routine = ctx.Level.ActorStep(type, step, ctx);
            else
            {
                Debug.LogWarning($"[PALINODE] Unknown step type '{type}' in chapter {ctx.ChapterId}.");
                yield break;
            }
            float delay = step.Num("delay", 0f);
            if (delay > 0f) routine = Delayed(delay, routine);
            if (routine == null) yield break;
            if (step.Bool("async")) ctx.StartAsync(routine);
            else yield return routine;
        }

        public static IEnumerator RunList(JNode list, CutsceneContext ctx)
        {
            foreach (var s in list.Items()) yield return Run(s, ctx);
        }

        private static IEnumerator Delayed(float delay, IEnumerator r)
        {
            yield return CutsceneContext.Wait(delay);
            if (r != null) yield return r;
        }

        /// <summary>Volume from "lufs" (target loudness, measured table) or "volume".</summary>
        private static float Vol(JNode s, CutsceneContext c, string clip, float fallback)
            => s.Has("lufs") ? c.Audio.VolumeForLufs(clip, s.Num("lufs"), fallback) : s.Num("volume", fallback);

        private static float SfxVol(JNode s, CutsceneContext c, float fallback = 1f) => Vol(s, c, s.Str("sfx"), fallback);

        private static IEnumerator Do(System.Action a)
        {
            a();
            yield break;
        }

        private static IEnumerator DoWait(System.Action a, float wait)
        {
            a();
            if (wait > 0f) yield return CutsceneContext.Wait(wait);
        }

        private static IEnumerator Tween(float duration, System.Action<float> apply, string ease = "inOutSine")
        {
            if (duration <= 0f) { apply(1f); yield break; }
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                apply(Ease.Apply(ease, Mathf.Clamp01(t / duration)));
                yield return null;
            }
            apply(1f);
        }

        private static StageLayer Layer(CutsceneContext c, string id)
        {
            var l = c.Stage.Get(id);
            if (l == null && id != null) Debug.LogWarning($"[PALINODE] Layer '{id}' not found (chapter {c.ChapterId}).");
            return l;
        }

        private static T Comp<T>(CutsceneContext c, string id) where T : Component
        {
            var l = Layer(c, id);
            return l != null ? l.GetComponent<T>() : null;
        }

        private static HandFollower Hand(CutsceneContext c, JNode s)
        {
            string id = s.Str("hand");
            return id == null ? null : Comp<HandFollower>(c, id);
        }

        // ---------------------------------------------------------------- markers & shots

        private static IEnumerator Marker(JNode s, CutsceneContext c)
        {
            c.RaiseMarker(s.Str("id"));
            yield break;
        }

        private static IEnumerator Shot(JNode s, CutsceneContext c)
        {
            float fade = s.Num("fade", 0f);
            string through = s.Str("through");
            int oldGroup = c.Stage.CurrentGroup;
            Color throughColor = through == "white" ? Color.white : Color.black;

            if (through != null && fade > 0f) yield return c.Overlay.FadeTo(throughColor, 1f, fade * 0.5f);

            int g = c.Stage.NextGroup();
            var created = new List<StageLayer>();
            foreach (var spec in s["layers"].Items()) created.Add(c.Stage.CreateLayer(spec, g));
            ApplyCamera(c, s["camera"], true);

            bool crossfade = through == null && fade > 0f;
            if (crossfade)
            {
                foreach (var l in created) if (l != null && l.Parent == null) l.GroupAlpha = 0f;
                yield return Tween(fade, k =>
                {
                    foreach (var l in created) if (l != null && l.Parent == null) l.GroupAlpha = k;
                }, "inOutSine");
            }
            c.Stage.RemoveGroup(oldGroup);
            if (through != null && fade > 0f) yield return c.Overlay.FadeTo(throughColor, 0f, fade * 0.5f);
        }

        private static void ApplyCamera(CutsceneContext c, JNode cam, bool resetWhenMissing)
        {
            if (cam.IsNull)
            {
                if (!resetWhenMissing) return;
                c.Stage.CamPos = Vector2.zero;
                c.Stage.CamZoom = 1f;
                c.Stage.CamRot = 0f;
                return;
            }
            c.Stage.CamPos = new Vector2(cam.Num("x", 0f), cam.Num("y", 0f)) * 0.01f;
            c.Stage.CamZoom = cam.Num("zoom", 1f);
            c.Stage.CamRot = cam.Num("rot", 0f);
        }

        private static IEnumerator Show(JNode s, CutsceneContext c)
        {
            var created = new List<StageLayer>();
            if (s.Has("layer")) created.Add(c.Stage.CreateLayer(s["layer"], c.Stage.CurrentGroup));
            foreach (var spec in s["layers"].Items()) created.Add(c.Stage.CreateLayer(spec, c.Stage.CurrentGroup));
            float fade = s.Num("fade", 0f);
            if (fade <= 0f) yield break;
            var targets = new List<float>();
            foreach (var l in created) { targets.Add(l.Alpha); l.Alpha = 0f; }
            yield return Tween(fade, k =>
            {
                for (int i = 0; i < created.Count; i++) if (created[i] != null) created[i].Alpha = targets[i] * k;
            }, "inOutSine");
        }

        private static IEnumerator Hide(JNode s, CutsceneContext c)
        {
            var ids = new List<string>();
            if (s.Has("id")) ids.Add(s.Str("id"));
            foreach (var n in s["ids"].Items()) ids.Add(n.AsString());
            float fade = s.Num("fade", 0f);
            var layers = new List<StageLayer>();
            foreach (var id in ids) { var l = Layer(c, id); if (l != null) layers.Add(l); }
            var from = new List<float>();
            foreach (var l in layers) from.Add(l.Alpha);
            yield return Tween(fade, k =>
            {
                for (int i = 0; i < layers.Count; i++) if (layers[i] != null) layers[i].Alpha = from[i] * (1f - k);
            }, "inOutSine");
            if (!s.Bool("keep")) foreach (var id in ids) c.Stage.Remove(id);
        }

        private static IEnumerator Clear(JNode s, CutsceneContext c)
        {
            int g = c.Stage.CurrentGroup;
            float fade = s.Num("fade", 0f);
            var layers = new List<StageLayer>(c.Stage.RootLayersInGroup(g));
            yield return Tween(fade, k =>
            {
                foreach (var l in layers) if (l != null) l.GroupAlpha = 1f - k;
            });
            c.Stage.RemoveGroup(g);
        }

        private static IEnumerator CameraMove(JNode s, CutsceneContext c)
        {
            var st = c.Stage;
            Vector2 p0 = st.CamPos;
            float z0 = st.CamZoom, r0 = st.CamRot;
            Vector2 p1 = new Vector2(s.Has("x") ? s.Num("x") * 0.01f : p0.x, s.Has("y") ? s.Num("y") * 0.01f : p0.y);
            float z1 = s.Has("zoom") ? s.Num("zoom") : z0;
            float r1 = s.Has("rot") ? s.Num("rot") : r0;
            string ease = s.Str("ease", "inOutSine");
            yield return Tween(s.Num("duration", 0f), k =>
            {
                st.CamPos = Vector2.LerpUnclamped(p0, p1, k);
                st.CamZoom = Mathf.LerpUnclamped(z0, z1, k);
                st.CamRot = Mathf.LerpUnclamped(r0, r1, k);
            }, ease);
        }

        private static IEnumerator Shake(JNode s, CutsceneContext c)
        {
            float dur = s.Num("duration", 0.4f);
            float amp = s.Num("amplitude", 8f) * 0.01f;
            float freq = s.Num("frequency", 25f);
            var layer = s.Has("id") ? Layer(c, s.Str("id")) : null;
            float t = 0f, seed = Random.value * 50f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float fall = 1f - t / dur;
                var o = new Vector2(Mathf.PerlinNoise(seed, t * freq) - 0.5f, Mathf.PerlinNoise(seed + 9f, t * freq) - 0.5f) * 2f * amp * fall;
                if (layer != null) layer.Offset = o; else c.Stage.Shake = o;
                yield return null;
            }
            if (layer != null) layer.Offset = Vector2.zero; else c.Stage.Shake = Vector2.zero;
        }

        /// <summary>Phone-style buzz: tight high-frequency jitter with a little rotation.</summary>
        private static IEnumerator Vibrate(JNode s, CutsceneContext c)
        {
            var layer = Layer(c, s.Str("id"));
            if (layer == null) yield break;
            float dur = s.Num("duration", 0.8f);
            float amp = s.Num("amplitude", 3f) * 0.01f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float pulse = Mathf.Sin(t / dur * Mathf.PI);
                layer.Offset = new Vector2(Mathf.Sin(t * 170f), Mathf.Sin(t * 143f + 1f)) * amp * pulse;
                layer.RotOffset = Mathf.Sin(t * 120f) * s.Num("rot", 0.8f) * pulse;
                yield return null;
            }
            layer.Offset = Vector2.zero;
            layer.RotOffset = 0f;
        }

        private static IEnumerator Fade(JNode s, CutsceneContext c)
        {
            string to = s.Str("to", "clear");
            float dur = s.Num("duration", 1f);
            if (to == "clear") return c.Overlay.FadeTo(c.Overlay.FadeColor, 0f, dur);
            Color col = s.Color("to", Color.black);
            return c.Overlay.FadeTo(col, s.Num("alpha", 1f), dur);
        }

        // ---------------------------------------------------------------- audio

        private static IEnumerator Sfx(JNode s, CutsceneContext c)
        {
            var clip = c.Config.Clip(s.Str("clip"));
            var src = c.Audio.PlayOneShot(clip, Vol(s, c, s.Str("clip"), 1f), s.Num("pitch", 1f), s.Bool("loop"), s.Num("pan", 0f),
                s.Bool("throughSilence"));
            c.RememberSfx(s.Str("id"), src);
            if (s.Bool("wait") && clip != null) yield return CutsceneContext.Wait(clip.length);
            // Continue exactly on the audible attack of the clip (measured offline, Data/audio_levels.json).
            if (s.Bool("waitAttack") && clip != null)
            {
                float attack = c.Config.ClipLevels(s.Str("clip")).Num("attack", 0f);
                Debug.Log($"[PALINODE] {s.Str("clip")}: waiting for attack at {attack:0.00}s");
                float t0 = Time.realtimeSinceStartup;
                while (src != null && src.isPlaying && src.time < attack) yield return null;
            }
        }

        private static IEnumerator StopSfx(JNode s, CutsceneContext c)
        {
            var src = c.NamedSfx(s.Str("id"));
            if (src != null) c.Audio.StopOneShot(src, s.Num("fade", 0.3f));
            yield break;
        }

        private static IEnumerator Loop(JNode s, CutsceneContext c)
        {
            c.Audio.PlayLoop(s.Str("id", s.Str("clip")), c.Config.Clip(s.Str("clip")), Vol(s, c, s.Str("clip"), 1f), s.Num("fade", 1f));
            yield break;
        }

        private static IEnumerator Music(JNode s, CutsceneContext c)
        {
            c.Audio.PlayMusic(c.Config.Clip(s.Str("clip")), Vol(s, c, s.Str("clip"), 0.3f), s.Num("fade", 3f), s.Bool("loop", true));
            yield break;
        }

        private static IEnumerator Voice(JNode s, CutsceneContext c)
        {
            var clip = c.Root.VoiceClip(s.Str("clip"));
            float voTarget = s.Has("lufs") ? s.Num("lufs") : c.Root.Data["mix"].Num("voiceLufs", float.NaN);
            float voVol = float.IsNaN(voTarget) ? s.Num("volume", 1f)
                : c.Audio.VolumeForLufs(c.Root.Settings.VoiceFolder + "/" + s.Str("clip"), voTarget, s.Num("volume", 1f));
            float len = c.Audio.PlayVoice(clip, voVol);
            string sub = s.Str("sub");
            Debug.Log($"[PALINODE] vo {s.Str("clip")} ({c.Root.Settings.VoiceFolder}) {len:0.00}s, subtitle={(sub != null && c.Root.Settings.Subtitles)}");
            if (sub != null && c.Root.Settings.Subtitles) c.Overlay.ShowSubtitle(c.T(sub), true);
            yield return CutsceneContext.Wait(Mathf.Max(len, s.Num("min", 0.5f)));
            if (sub != null && !s.Bool("keepSubtitle")) c.Overlay.HideSubtitle();
            yield return CutsceneContext.Wait(s.Num("pause", 0.8f));
        }

        private static IEnumerator Subtitle(JNode s, CutsceneContext c)
        {
            if (c.Root.Settings.Subtitles) c.Overlay.ShowSubtitle(c.T(s.Str("key")), true);
            yield return CutsceneContext.Wait(s.Num("time", 2f));
            c.Overlay.HideSubtitle();
        }

        // ---------------------------------------------------------------- writing

        private static IEnumerator HandTo(JNode s, CutsceneContext c)
        {
            var hand = Hand(c, s);
            if (hand == null) yield break;
            if (s.Has("page"))
            {
                var page = Comp<HandwritingPage>(c, s.Str("page"));
                int line = s.Int("line");
                Vector3 p = s.Str("at", "start") == "end" ? page.LineEndWorld(line) : page.LineStartWorld(line);
                hand.SetTarget(p + (Vector3)(s.Vec2("offset", Vector2.zero) * 0.01f * page.transform.lossyScale.x));
            }
            else if (s.Has("layer"))
            {
                var l = Layer(c, s.Str("layer"));
                if (l != null) hand.SetTarget(l.PixelToWorld(s.Vec2("px", Vector2.zero)));
            }
            hand.Smooth = s.Num("smooth", 0.35f);
            if (s.Has("contact")) hand.SetContact(s.Bool("contact"));
            yield return CutsceneContext.Wait(s.Num("duration", 0.6f));
            hand.Smooth = 0.06f;
        }

        private static IEnumerator Write(JNode s, CutsceneContext c)
        {
            var page = Comp<HandwritingPage>(c, s.Str("page"));
            if (page == null) yield break;
            Color? col = s.Has("color") ? s.Color("color", Color.black) : (Color?)null;
            string text = s.Has("text") ? s.Str("text") : c.T(s.Str("key"));
            yield return page.Write(s.Int("line"), text, s.Num("cps", 11f), s.Str("align", "left"), s.Num("fontScale", 1f), Hand(c, s), col);
        }

        private static IEnumerator Strike(JNode s, CutsceneContext c)
        {
            var page = Comp<HandwritingPage>(c, s.Str("page"));
            if (page == null) yield break;
            if (s.Has("sfx")) c.Audio.PlayOneShot(c.Config.Clip(s.Str("sfx")), SfxVol(s, c));
            yield return page.Strike(s.Int("line"), s.Num("duration", 0.7f), Hand(c, s));
        }

        private static IEnumerator Insert(JNode s, CutsceneContext c)
        {
            var page = Comp<HandwritingPage>(c, s.Str("page"));
            if (page == null) yield break;
            yield return page.Insert(s.Int("line"), c.T(s.Str("key")), c.T(s.Str("anchorKey")),
                s.Color("color", new Color(0.66f, 0.07f, 0.08f)), s.Num("cps", 7f), Hand(c, s));
        }

        private static IEnumerator Dot(JNode s, CutsceneContext c)
        {
            var page = Comp<HandwritingPage>(c, s.Str("page"));
            if (page == null) yield break;
            yield return page.Dot(s.Int("line"), s.Str("at", "lineEnd"), s.Color("color", new Color(0.66f, 0.07f, 0.08f)), s.Num("size", 11f), Hand(c, s));
            if (s.Has("sfx")) c.Audio.PlayOneShot(c.Config.Clip(s.Str("sfx")), SfxVol(s, c));
        }

        private static IEnumerator Preset(JNode s, CutsceneContext c)
        {
            var page = Comp<HandwritingPage>(c, s.Str("page"));
            page?.Preset(s.Int("line"), c.T(s.Str("key")), s.Str("align", "left"), s.Num("fontScale", 1f), s.Bool("struck"));
            yield break;
        }

        // ---------------------------------------------------------------- phone, clock

        private static IEnumerator PhoneHeader(JNode s, CutsceneContext c)
        {
            Comp<PhoneScreen>(c, s.Str("phone"))?.SetHeader(c.T(s.Str("key")));
            yield break;
        }

        private static IEnumerator PhoneCall(JNode s, CutsceneContext c)
        {
            var p = Comp<PhoneScreen>(c, s.Str("phone"));
            if (p != null) yield return p.ShowCall(c.T(s.Str("key")), c.T(s.Str("subKey", "CALLER_INCOMING")));
        }

        private static IEnumerator PhoneMessage(JNode s, CutsceneContext c)
        {
            var p = Comp<PhoneScreen>(c, s.Str("phone"));
            if (p == null) yield break;
            if (s.Has("sfx")) c.Audio.PlayOneShot(c.Config.Clip(s.Str("sfx")), SfxVol(s, c, 0.8f));
            yield return p.AddMessage(c.T(s.Str("key")), s.Bool("accent"));
        }

        private static IEnumerator Clock(JNode s, CutsceneContext c)
        {
            var clock = Comp<ClockFace>(c, s.Str("clock"));
            if (clock == null) yield break;
            yield return clock.SetTime(s.Str("time", "00:00"), s.Num("duration", 0f), s.Str("ease", "outBack"));
        }

        // ---------------------------------------------------------------- layer animation

        private static IEnumerator Move(JNode s, CutsceneContext c)
        {
            var l = Layer(c, s.Str("id"));
            if (l == null) yield break;
            Vector2 p0 = l.BasePos, s0 = l.BaseScale;
            float r0 = l.BaseRot;
            Vector2 p1 = p0;
            if (s.Has("px") && l.Parent != null) p1 = l.Parent.PixelToLocal(s.Vec2("px", Vector2.zero));
            else if (s.Has("pos")) p1 = s.Vec2("pos", Vector2.zero) * 0.01f;
            Vector2 s1 = s.Has("scale") ? s0 / Mathf.Max(0.0001f, Mathf.Abs(s0.y)) * s.Num("scale") * (l.Parent == null ? FitOf(l) : 1f) : s0;
            if (s.Has("scaleMul")) s1 = s0 * s.Num("scaleMul");
            float r1 = s.Has("rot") ? s.Num("rot") : r0;
            float bob = s.Num("bob", 0f) * 0.01f;
            float bobFreq = s.Num("bobFreq", 1.8f);
            float sway = s.Num("sway", 0f);
            string ease = s.Str("ease", "inOutSine");
            float dur = s.Num("duration", 1f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Ease.Apply(ease, Mathf.Clamp01(t / dur));
                l.BasePos = Vector2.LerpUnclamped(p0, p1, k);
                l.BaseScale = Vector2.LerpUnclamped(s0, s1, k);
                l.BaseRot = Mathf.LerpUnclamped(r0, r1, k);
                if (bob > 0f)
                {
                    float phase = t * bobFreq * Mathf.PI * 2f;
                    float scaleNow = Mathf.Abs(l.BaseScale.y) / Mathf.Max(0.0001f, Mathf.Abs(s0.y));
                    l.Offset = new Vector2(0f, Mathf.Abs(Mathf.Sin(phase)) * bob * scaleNow);
                    l.RotOffset = Mathf.Sin(phase) * sway;
                }
                yield return null;
            }
            l.BasePos = p1;
            l.BaseScale = s1;
            l.BaseRot = r1;
            l.Offset = Vector2.zero;
            l.RotOffset = 0f;
        }

        /// <summary>
        /// Frame-animated walk of a sprite layer (P2-04, P3-04): the layer moves to "px"/"pos" and to "scale" while its
        /// sprite cycles through "frames" {prefix, count, idle, plants}. One cycle (all frames = two steps) takes
        /// "cycleTime" s at full speed; the frame rate follows the eased speed, so the feet slow down with the body, and
        /// the cycle is stretched to end exactly on the "idle" frame (a near-standing pose). "blend" cross-fades the next
        /// frame in over the last ~45 % of each frame (softens the 8-frame cycle without double images). "perspective": true shrinks the figure as it walks into depth (1/scale is linear in
        /// time, as with a constant walking speed); with "vanish" [px] the path also converges to that vanishing point.
        /// "stepClip" (+ "stepLufs") plays on every planted foot. The layer's sprite should be a frame of the same set
        /// (pivot at the feet), so "px" is where the feet stand.
        /// </summary>
        private static IEnumerator WalkFrames(JNode s, CutsceneContext c)
        {
            var l = Layer(c, s.Str("id"));
            if (l == null || l.Renderer == null) yield break;
            var fr = s["frames"];
            int n = Mathf.Max(1, fr.Int("count", 8));
            var frames = new Sprite[n];
            for (int i = 0; i < n; i++) frames[i] = c.Config.Sprite(fr.Str("prefix") + (i + 1).ToString("00"));
            int idle = fr.Int("idle", 0);
            var plants = new List<int>();
            foreach (var p in fr["plants"].Items()) plants.Add(p.AsInt());

            Vector2 p0 = l.BasePos, s0 = l.BaseScale;
            Vector2 p1 = p0;
            if (s.Has("px") && l.Parent != null) p1 = l.Parent.PixelToLocal(s.Vec2("px", Vector2.zero));
            else if (s.Has("pos")) p1 = s.Vec2("pos", Vector2.zero) * 0.01f;
            Vector2 s1 = s.Has("scale") ? s0 / Mathf.Max(0.0001f, Mathf.Abs(s0.y)) * s.Num("scale") * (l.Parent == null ? FitOf(l) : 1f) : s0;
            float a0 = Mathf.Abs(s0.y), a1 = Mathf.Abs(s1.y);
            bool persp = s.Bool("perspective") && a0 > 1e-4f && a1 > 1e-4f;
            bool hasVp = persp && s.Has("vanish") && l.Parent != null;
            Vector2 vp = hasVp ? l.Parent.PixelToLocal(s.Vec2("vanish", Vector2.zero)) : Vector2.zero;
            string ease = s.Str("ease", "outSine");
            float dur = Mathf.Max(0.01f, s.Num("duration", 3f));
            float cycle = Mathf.Max(0.1f, s.Num("cycleTime", 1.1f));
            float blendAmt = s.Num("blend", 0.5f);
            int startFrame = s.Int("startFrame", 0);
            // Frames advanced at full speed over the move, rounded so the last frame is the idle pose.
            float span = dur / cycle * n;
            int toIdle = Mod(idle - startFrame, n);
            float endPhase = startFrame + Mathf.Max(0f, Mathf.Round((span - toIdle) / n)) * n + toIdle;

            var stepClip = s.Has("stepClip") ? c.Config.Clip(s.Str("stepClip")) : null;
            float stepVol = s.Has("stepLufs") ? c.Audio.VolumeForLufs(s.Str("stepClip"), s.Num("stepLufs"), 0.4f) : s.Num("stepVolume", 0.4f);

            var bgo = new GameObject("WalkBlend");
            bgo.layer = l.gameObject.layer;
            bgo.transform.SetParent(l.Renderer.transform, false);
            var blend = bgo.AddComponent<SpriteRenderer>();
            blend.sharedMaterial = l.Renderer.sharedMaterial;
            blend.sortingOrder = l.Renderer.sortingOrder;
            blend.sortingLayerID = l.Renderer.sortingLayerID;

            float t = 0f, lastPhase = startFrame;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Ease.Apply(ease, Mathf.Clamp01(t / dur));
                float sc = persp ? 1f / Mathf.Lerp(1f / a0, 1f / a1, k) : Mathf.Lerp(a0, a1, k);
                l.BaseScale = s0 / Mathf.Max(0.0001f, a0) * sc;
                if (hasVp) l.BasePos = vp + Vector2.Lerp((p0 - vp) / a0, (p1 - vp) / a1, k) * sc;
                else l.BasePos = Vector2.Lerp(p0, p1, persp ? Mathf.InverseLerp(1f / a0, 1f / a1, 1f / sc) : k);

                float phase = Mathf.Lerp(startFrame, endPhase, k);
                int i0 = Mod(Mathf.FloorToInt(phase), n);
                float frac = phase - Mathf.Floor(phase);
                l.Renderer.sprite = frames[i0];
                blend.sprite = frames[(i0 + 1) % n];
                var col = l.Renderer.color;
                col.a *= blendAmt * Mathf.SmoothStep(0f, 1f, (frac - 0.55f) / 0.45f);   // only around the frame change: no double image mid-frame
                blend.color = col;
                blend.flipX = l.Renderer.flipX;
                if (stepClip != null)
                    foreach (int pl in plants)
                        for (float x = Mathf.Floor((lastPhase - pl) / n) * n + pl; x <= phase; x += n)
                            if (x > lastPhase) c.Audio.PlayOneShot(stepClip, stepVol, Random.Range(0.94f, 1.06f));
                lastPhase = phase;
                yield return null;
            }
            l.BasePos = p1;
            l.BaseScale = s1;
            l.Renderer.sprite = frames[Mod(idle, n)];
            Object.Destroy(bgo);
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;

        // Root layers store fit * scale in BaseScale; recover the fit factor from the current sprite.
        private static float FitOf(StageLayer l)
        {
            var sp = l.Sprite;
            if (sp == null) return 1f;
            Vector2 n = sp.bounds.size;
            return Mathf.Max(Stage.ViewWidth / n.x, Stage.ViewHeight / n.y);
        }

        private static IEnumerator AlphaTo(JNode s, CutsceneContext c)
        {
            var l = Layer(c, s.Str("id"));
            if (l == null) yield break;
            float a0 = l.Alpha, a1 = s.Num("to", 1f);
            yield return Tween(s.Num("duration", 1f), k => { if (l != null) l.Alpha = Mathf.Lerp(a0, a1, k); }, s.Str("ease", "inOutSine"));
        }

        private static IEnumerator TintTo(JNode s, CutsceneContext c)
        {
            var l = Layer(c, s.Str("id"));
            if (l == null) yield break;
            Color c0 = l.Tint, c1 = s.Color("color", Color.white);
            yield return Tween(s.Num("duration", 1f), k => { if (l != null) l.Tint = Color.Lerp(c0, c1, k); });
        }

        private static IEnumerator SwapSprite(JNode s, CutsceneContext c)
        {
            var l = Layer(c, s.Str("id"));
            if (l == null || l.Renderer == null) yield break;
            var sprite = c.Config.Sprite(s.Str("sprite"));
            float fade = s.Num("fade", 0f);
            if (fade <= 0f)
            {
                l.Renderer.sprite = sprite;
                yield break;
            }
            // Crossfade: duplicate renderer on top, fade it in, then swap.
            var go = new GameObject("Crossfade");
            go.layer = l.gameObject.layer;
            go.transform.SetParent(l.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = l.Renderer.sharedMaterial;
            sr.sortingOrder = l.Renderer.sortingOrder + 1;
            sr.flipX = l.Renderer.flipX;
            yield return Tween(fade, k =>
            {
                var col = l.Renderer.color;
                col.a *= k;
                sr.color = col;
            });
            l.Renderer.sprite = sprite;
            Object.Destroy(go);
        }

        // ---------------------------------------------------------------- light

        private static IEnumerator Flicker(JNode s, CutsceneContext c)
        {
            float dur = s.Num("duration", 1.2f);
            float strength = s.Num("strength", 0.8f);
            if (s.Has("light"))
            {
                var light = Comp<StageLight>(c, s.Str("light"));
                if (light != null) yield return light.Flicker(dur, strength);
                yield break;
            }
            if (c.GlobalLight == null) yield break;
            float t = 0f;
            float baseI = c.GlobalLightBase;
            while (t < dur)
            {
                t += Time.deltaTime;
                bool drop = Mathf.PerlinNoise(3.1f, Time.time * 20f) < 0.42f || Random.value < 0.07f;
                c.GlobalLight.intensity = baseI * (drop ? 1f - strength * Random.Range(0.5f, 1f) : 1f);
                yield return null;
            }
            c.GlobalLight.intensity = baseI;
        }

        private static IEnumerator GlobalLight(JNode s, CutsceneContext c)
        {
            if (c.GlobalLight == null) yield break;
            float i0 = c.GlobalLight.intensity, i1 = s.Num("intensity", i0);
            Color c0 = c.GlobalLight.color, c1 = s.Color("color", c0);
            c.GlobalLightBase = i1;
            yield return Tween(s.Num("duration", 0f), k =>
            {
                c.GlobalLight.intensity = Mathf.Lerp(i0, i1, k);
                c.GlobalLight.color = Color.Lerp(c0, c1, k);
            });
        }

        private static IEnumerator LightTo(JNode s, CutsceneContext c)
        {
            var l = Comp<StageLight>(c, s.Str("id"));
            if (l == null) yield break;
            float i0 = l.BaseIntensity, i1 = s.Num("intensity", 1f);
            yield return Tween(s.Num("duration", 1f), k => l.BaseIntensity = Mathf.Lerp(i0, i1, k));
        }

        // ---------------------------------------------------------------- titles & texts

        private static IEnumerator Title(JNode s, CutsceneContext c)
        {
            string main = s.Has("text") ? s.Str("text") : c.T(s.Str("key"));
            string sub = s.Has("subKey") ? c.T(s.Str("subKey")) : string.Empty;
            yield return c.Overlay.TitleCard(main, sub, s.Num("fadeIn", 2f), s.Num("hold", 2f), s.Num("fadeOut", -1f), s.Num("size", 150f));
        }

        private static IEnumerator TitleSub(JNode s, CutsceneContext c) => c.Overlay.SetTitleSub(c.T(s.Str("key")), s.Num("fade", 1f));

        private static IEnumerator Stamp(JNode s, CutsceneContext c)
        {
            var t = Comp<StageText>(c, s.Str("id"));
            if (t == null) yield break;
            if (s.Has("key")) t.SetText(c.T(s.Str("key")));
            if (s.Has("text")) t.SetText(s.Str("text"));
            if (s.Has("sfx")) c.Audio.PlayOneShot(c.Config.Clip(s.Str("sfx")), SfxVol(s, c));
            yield return t.Stamp(s.Num("duration", 0.25f));
        }

        private static IEnumerator Emerge(JNode s, CutsceneContext c)
        {
            var t = Comp<StageText>(c, s.Str("id"));
            if (t == null) yield break;
            if (s.Has("key")) t.SetText(c.T(s.Str("key")));
            yield return t.Emerge(s.Num("duration", 2f));
        }

        private static IEnumerator Fx(JNode s, CutsceneContext c)
        {
            var fx = Comp<ParticleFX>(c, s.Str("id"));
            if (fx == null || fx.System == null) yield break;
            if (s.Bool("emit", true)) fx.System.Play(); else fx.System.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static IEnumerator Parallel(JNode s, CutsceneContext c)
        {
            int running = 0;
            foreach (var step in s["steps"].Items())
            {
                running++;
                c.Runner.StartCoroutine(Track(Run(step, c), () => running--));
            }
            while (running > 0) yield return null;
        }

        private static IEnumerator Track(IEnumerator r, System.Action done)
        {
            yield return r;
            done();
        }
    }
}
