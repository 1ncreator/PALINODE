using System.Collections.Generic;
using Palinode.Audio;
using Palinode.Core;
using Palinode.Cutscene;
using Palinode.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Gameplay
{
    /// <summary>
    /// A vehicle driving along a straight lane (prologue.json levels.street.traffic): a lit body sprite depth-sorted by
    /// its ground point, headlight cones and glows, tail lights, a mirrored reflection on the wet asphalt, road spray, and
    /// a pass-by sound whose pan, level and Doppler pitch follow the camera. A ghost vehicle uses the SpriteGhost material
    /// (translucent, cold, flickering, dissolving tail), fades in with its lights and can be made to vanish at once.
    /// </summary>
    public sealed class Vehicle : MonoBehaviour
    {
        private sealed class Lamp
        {
            public Light2D Light;
            public SpriteRenderer Glow, Streak;
            public float Intensity, GlowAlpha, StreakAlpha;
        }

        private Camera _cam;
        private AudioDirector _audio;
        private Vector2 _from, _dir;
        private float _speed, _length, _dist;
        private float _age, _fadeIn, _vanishT = -1f, _vanishDur;
        private SpriteRenderer _body, _reflection;
        private Color _bodyColor, _reflectionColor;
        private readonly List<Lamp> _lamps = new List<Lamp>();
        private ParticleSystem _spray;
        private AudioSource _sound;
        private float _soundVolume, _doppler, _panRange, _hearRange, _basePitch = 1f;
        private Material _ghostMat;
        private float _ghostDissolve, _flicker, _flickerChance;
        private float _flickerHold;

        public bool Ghost { get; private set; }
        public bool Finished { get; private set; }
        public Vector2 Position => transform.position;
        public string Id { get; private set; }

        /// <param name="car">per-car spec: anchor, head[], headGlow, tail[], mirror (sprite px)</param>
        /// <param name="look">shared look: scale, rotation, headlight, taillight, reflection, spray, sound, ghost</param>
        /// <param name="ppu">map pixels per world unit</param>
        public void Init(string id, GameConfig config, Camera cam, AudioDirector audio, Sprite sprite, JNode car, JNode look,
            Vector2 from, Vector2 to, float speed, bool ghost, float ppu)
        {
            Id = id;
            _cam = cam;
            _audio = audio;
            _from = from;
            _dir = (to - from).normalized;
            _length = Vector2.Distance(from, to);
            _speed = speed;
            Ghost = ghost;
            transform.position = from;

            float s = look.Num("scale", 0.28f);
            float rot = look.Num("rotation", 7f);
            Quaternion r = Quaternion.Euler(0f, 0f, rot);
            float spu = sprite.pixelsPerUnit;
            Vector2 anchor = car.Vec2("anchor", sprite.rect.size * 0.5f);
            // Sprite pixel (top-left origin) → position relative to the ground anchor, in this (unrotated) root's space.
            Vector3 Local(Vector2 px) => r * (Vector3)(new Vector2(px.x - anchor.x, anchor.y - px.y) * (s / spu));
            Vector3 bodyCentre = Local(new Vector2(sprite.pivot.x, sprite.rect.height - sprite.pivot.y));
            float mirrorDrop = (car.Num("mirror", anchor.y + 90f) - anchor.y) * s / spu;   // reflection axis below the anchor

            // Body.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, false);
            bodyGo.transform.localPosition = bodyCentre;
            bodyGo.transform.localRotation = r;
            bodyGo.transform.localScale = Vector3.one * s;
            _body = bodyGo.AddComponent<SpriteRenderer>();
            _body.sprite = sprite;
            if (ghost)
            {
                var g = look["ghost"];
                _ghostMat = new Material(config.Material("Ghost"));
                _ghostMat.SetFloat("_Base", g.Num("base", 0.3f));
                _ghostMat.SetFloat("_Detail", g.Num("detail", 1.8f));
                _ghostMat.SetFloat("_Desat", g.Num("keepColour", 0.2f));
                _ghostDissolve = g.Num("dissolve", 0.45f);
                _ghostMat.SetFloat("_Dissolve", _ghostDissolve);
                Vector2 dd = g.Vec2("dissolveDir", new Vector2(1f, -0.35f));
                _ghostMat.SetVector("_DissolveDir", new Vector4(dd.x, dd.y, 0f, 0f));
                _ghostMat.SetFloat("_Scan", g.Num("scan", 0.18f));
                _body.sharedMaterial = _ghostMat;
                _bodyColor = g.Color("tint", new Color(0.74f, 0.84f, 1f, 0.55f));
                _fadeIn = g.Num("fadeIn", 0.35f);
                _flicker = g.Num("flicker", 0.45f);
                _flickerChance = g.Num("flickerChance", 0.12f);
            }
            else
            {
                _body.sharedMaterial = config.Material("SpriteLit");
                _bodyColor = look.Color("tint", Color.white);
            }
            _body.color = _bodyColor;

            // Reflection on the wet road: the body mirrored about its near-side ground line, faint and cold.
            var refl = look["reflection"];
            if (!ghost && !refl.IsNull)
            {
                var rgo = new GameObject("Reflection");
                rgo.transform.SetParent(transform, false);
                rgo.transform.localPosition = new Vector3(bodyCentre.x, -2f * mirrorDrop - bodyCentre.y, 0f);
                rgo.transform.localRotation = Quaternion.Euler(0f, 0f, -rot);
                rgo.transform.localScale = new Vector3(s, s * refl.Num("squash", 0.85f), 1f);
                _reflection = rgo.AddComponent<SpriteRenderer>();
                _reflection.sprite = sprite;
                _reflection.flipY = true;
                _reflection.sharedMaterial = config.Material("SpriteLit");
                _reflectionColor = refl.Color("color", new Color(0.55f, 0.6f, 0.72f, 0.16f));
                _reflection.color = _reflectionColor;
                _reflection.sortingOrder = refl.Int("order", -29500);
            }

            // Lamps.
            float angle = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
            var glowMat = config.Material("GlowAdd");
            var head = look["headlight"];
            bool headGlow = car.Bool("headGlow", true);
            foreach (var p in car["head"].Items())
                _lamps.Add(MakeLamp(Local(new Vector2(p[0].AsFloat(), p[1].AsFloat())), head, true, headGlow, angle, mirrorDrop, glowMat, ppu));
            var tail = look["taillight"];
            foreach (var p in car["tail"].Items())
                _lamps.Add(MakeLamp(Local(new Vector2(p[0].AsFloat(), p[1].AsFloat())), tail, false, true, angle, mirrorDrop, glowMat, ppu));

            // Spray thrown up by the tyres.
            var sp = look["spray"];
            if (!sp.IsNull && sp.Num("rate", 0f) > 0f)
                _spray = MakeSpray(config, sp, s * sprite.rect.width / spu * 0.55f, angle);

            // Pass-by sound.
            var snd = look["sound"];
            var clip = config.Clip(snd.Str("clip"));
            if (clip != null && audio != null)
            {
                _soundVolume = snd.Has("lufs") ? audio.VolumeForLufs(clip.name, snd.Num("lufs"), 0.5f) : snd.Num("volume", 0.5f);
                _doppler = snd.Num("doppler", 0.07f);
                _panRange = snd.Num("panRange", 900f) / ppu;
                _hearRange = snd.Num("range", 700f) / ppu;
                _basePitch = snd.Num("pitch", 1f) * Random.Range(1f - snd.Num("pitchJitter", 0.04f), 1f + snd.Num("pitchJitter", 0.04f));
                _sound = audio.PlayOneShot(clip, 0f, _basePitch, true);
                if (_sound != null && snd.Bool("randomStart", true) && clip.length > 8f)
                    _sound.time = Random.Range(0f, clip.length - 8f);
            }
            UpdateFrame(0f);
        }

        private Lamp MakeLamp(Vector3 local, JNode spec, bool isHead, bool glow, float angle, float mirrorDrop, Material glowMat, float ppu)
        {
            var lamp = new Lamp();
            Color c = spec.Color("color", isHead ? new Color(1f, 0.94f, 0.83f) : new Color(1f, 0.16f, 0.1f));
            var go = new GameObject(isHead ? "Headlight" : "Taillight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            lamp.Light = StageLight.CreateLight(go, Light2D.LightType.Point);
            lamp.Light.color = c;
            lamp.Light.pointLightOuterRadius = spec.Num("radius", isHead ? 520f : 90f) / ppu;
            lamp.Light.pointLightInnerRadius = spec.Num("innerRadius", isHead ? 20f : 0f) / ppu;
            lamp.Light.pointLightOuterAngle = spec.Num("angle", isHead ? 52f : 360f);
            lamp.Light.pointLightInnerAngle = spec.Num("innerAngle", isHead ? 22f : 360f);
            lamp.Light.falloffIntensity = spec.Num("falloff", 0.55f);
            lamp.Intensity = spec.Num("intensity", isHead ? 1.6f : 0.8f);
            // Cones shine along the direction of travel (tail lights are omni).
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);

            float glowPx = spec.Num("glow", isHead ? 60f : 34f);
            if (glow && glowPx > 0f)
            {
                var g = new GameObject("Glow");
                g.transform.SetParent(transform, false);
                g.transform.localPosition = local;
                g.transform.localScale = Vector3.one * glowPx / ppu / 1.28f;     // SoftDot(128) = 1.28 units
                lamp.Glow = g.AddComponent<SpriteRenderer>();
                lamp.Glow.sprite = ProceduralSprites.SoftDot(128, 0f);
                lamp.Glow.sharedMaterial = glowMat;
                lamp.GlowAlpha = spec.Num("glowAlpha", isHead ? 0.6f : 0.55f);
                lamp.Glow.color = new Color(c.r, c.g, c.b, 0f);
            }

            // Streak on the wet asphalt: the lamp mirrored about the ground line, stretched towards the viewer.
            Vector2 streak = spec.Vec2("streak", isHead ? new Vector2(40f, 150f) : new Vector2(22f, 90f));
            if (streak.y > 0f)
            {
                var sgo = new GameObject("Streak");
                sgo.transform.SetParent(transform, false);
                float groundY = -mirrorDrop;
                sgo.transform.localPosition = new Vector3(local.x, 2f * groundY - local.y - streak.y * 0.35f / ppu, 0f);
                sgo.transform.localScale = new Vector3(streak.x / ppu / 1.28f, streak.y / ppu / 1.28f, 1f);
                lamp.Streak = sgo.AddComponent<SpriteRenderer>();
                lamp.Streak.sprite = ProceduralSprites.SoftDot(128, 0f);
                lamp.Streak.sharedMaterial = glowMat;
                lamp.Streak.sortingOrder = -29400;
                lamp.StreakAlpha = spec.Num("streakAlpha", isHead ? 0.22f : 0.2f);
                lamp.Streak.color = new Color(c.r, c.g, c.b, 0f);
            }
            return lamp;
        }

        private ParticleSystem MakeSpray(GameConfig config, JNode sp, float length, float angle)
        {
            var go = new GameObject("Spray");
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, sp.Num("life", 0.8f));
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sp.Num("size", 0.3f) * 0.5f, sp.Num("size", 0.3f));
            main.startColor = sp.Color("color", new Color(0.78f, 0.82f, 0.9f, 0.13f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.rateOverDistance = sp.Num("rate", 14f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(length, sp.Num("width", 30f) / 100f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            Vector2 back = -_dir * _speed * sp.Num("drag", 0.08f);
            vel.x = new ParticleSystem.MinMaxCurve(back.x - 0.25f, back.x + 0.25f);
            vel.y = new ParticleSystem.MinMaxCurve(back.y + 0.05f, back.y + sp.Num("rise", 0.35f));
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 2.4f));
            var rend = go.GetComponent<ParticleSystemRenderer>();
            var src = config.Material("ParticleAlpha");
            if (src != null) { var m = new Material(src); m.mainTexture = ProceduralSprites.SoftDot(64, 0f).texture; rend.sharedMaterial = m; }
            ps.Play();
            return ps;
        }

        /// <summary>Gone in <paramref name="duration"/> s: the ghost dissolves, the lights cut, the sound is cut short.</summary>
        public void Vanish(float duration, float soundFade = 0.4f)
        {
            if (_vanishT >= 0f) return;
            _vanishT = 0f;
            _vanishDur = Mathf.Max(0.01f, duration);
            if (_sound != null) { _audio.StopOneShot(_sound, soundFade); _sound = null; }
            if (_spray != null) _spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Update()
        {
            if (Finished) return;
            _dist += _speed * Time.deltaTime;
            _age += Time.deltaTime;
            transform.position = _from + _dir * Mathf.Min(_dist, _length);
            UpdateFrame(Time.deltaTime);
            if (_dist >= _length || (_vanishT >= 0f && _vanishT >= _vanishDur)) Finish();
        }

        private void UpdateFrame(float dt)
        {
            float env = _fadeIn > 0f ? Mathf.SmoothStep(0f, 1f, _age / _fadeIn) : 1f;
            float gone = 0f;
            if (_vanishT >= 0f)
            {
                _vanishT += dt;
                gone = Mathf.Clamp01(_vanishT / _vanishDur);
            }
            float flick = 1f;
            if (Ghost)
            {
                if (_flickerHold > 0f) { _flickerHold -= dt; flick = 1f - _flicker; }
                else if (dt > 0f && Random.value < _flickerChance) _flickerHold = Random.Range(0.03f, 0.09f);
                flick *= 0.9f + 0.1f * Mathf.Sin(_age * 23f);
                _ghostMat.SetFloat("_Dissolve", Mathf.Lerp(_ghostDissolve, 1f, gone));
            }
            float k = env * flick * (1f - gone);

            int order = PlayerController.SortingOrderFor(transform.position.y);
            _body.sortingOrder = order;
            var bc = _bodyColor;
            bc.a *= Ghost ? k : 1f - gone;
            _body.color = bc;
            if (_reflection != null)
            {
                var rc = _reflectionColor;
                rc.a *= 1f - gone;
                _reflection.color = rc;
            }
            foreach (var l in _lamps)
            {
                l.Light.intensity = l.Intensity * k;
                if (l.Glow != null)
                {
                    var c = l.Glow.color; c.a = l.GlowAlpha * k; l.Glow.color = c;
                    l.Glow.sortingOrder = order + 1;
                }
                if (l.Streak != null) { var c = l.Streak.color; c.a = l.StreakAlpha * k; l.Streak.color = c; }
            }
            if (_spray != null) _spray.GetComponent<ParticleSystemRenderer>().sortingOrder = order + 2;

            if (_sound != null && _cam != null)
            {
                Vector2 rel = (Vector2)transform.position - (Vector2)_cam.transform.position;
                float d = rel.magnitude;
                float vol = _soundVolume * env / (1f + (d / _hearRange) * (d / _hearRange));
                _audio.SetOneShotVolume(_sound, vol);
                _audio.SetPan(_sound, rel.x / _panRange);
                // Doppler: radial speed towards the listener (camera), normalised by the vehicle speed.
                float approach = d > 1e-3f ? -Vector2.Dot(_dir, rel / d) : 0f;
                _sound.pitch = _basePitch * (1f + _doppler * approach);
            }
        }

        private void Finish()
        {
            Finished = true;
            if (_sound != null) { _audio.StopOneShot(_sound, 0.3f); _sound = null; }
            if (_spray != null)
            {
                _spray.transform.SetParent(transform.parent, true);
                _spray.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(_spray.gameObject, 2f);
            }
            if (_ghostMat != null) Destroy(_ghostMat, 0.1f);
            Destroy(gameObject);
        }
    }
}
