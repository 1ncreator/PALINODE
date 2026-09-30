using System.Collections;
using Palinode.Cutscene;
using Palinode.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Palinode.Gameplay
{
    /// <summary>A pair of headlight cones (and glows) travelling along a path — a car passing in the rain.</summary>
    public static class LightSweep
    {
        public static IEnumerator Run(Transform parent, Vector2 from, Vector2 to, float duration, float intensity, Color color,
            Material glowMaterial, AudioSource sound = null, float soundVolume = 0.5f, float doppler = 0.08f, float pan = 0.85f)
        {
            var root = new GameObject("Headlights").transform;
            root.SetParent(parent, false);
            Vector2 dir = (to - from).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            root.rotation = Quaternion.Euler(0f, 0f, angle);

            var cones = new Light2D[2];
            var glows = new SpriteRenderer[2];
            var reflections = new SpriteRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Cone" + i);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(0f, (i == 0 ? 1f : -1f) * 0.32f, 0f);
                var l = StageLight.CreateLight(go, Light2D.LightType.Point);
                l.color = color;
                l.pointLightOuterRadius = 4.5f;
                l.pointLightInnerRadius = 0.2f;
                l.pointLightOuterAngle = 55f;
                l.pointLightInnerAngle = 25f;
                l.falloffIntensity = 0.55f;
                l.intensity = 0f;
                go.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                cones[i] = l;

                var g = new GameObject("Glow" + i);
                g.transform.SetParent(go.transform.parent, false);
                g.transform.localPosition = go.transform.localPosition;
                g.transform.localScale = Vector3.one * 0.6f;
                var sr = g.AddComponent<SpriteRenderer>();
                sr.sprite = ProceduralSprites.SoftDot(128, 0f);
                sr.sharedMaterial = glowMaterial;
                sr.sortingOrder = 12000;
                glows[i] = sr;

                // Wet-asphalt reflection: a long streak below each lamp.
                var rgo = new GameObject("Reflection" + i);
                rgo.transform.SetParent(root.parent, false);
                var rr = rgo.AddComponent<SpriteRenderer>();
                rr.sprite = ProceduralSprites.SoftDot(128, 0f);
                rr.sharedMaterial = glowMaterial;
                rr.sortingOrder = -29000;
                rgo.transform.localScale = new Vector3(0.35f, 1.6f, 1f);
                reflections[i] = rr;
            }

            float basePitch = sound != null ? sound.pitch : 1f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                root.position = Vector2.Lerp(from, to, k);
                float env = Mathf.Sin(k * Mathf.PI);
                env = Mathf.Clamp01(env * 1.6f);
                for (int i = 0; i < 2; i++)
                {
                    cones[i].intensity = intensity * env;
                    glows[i].color = new Color(color.r, color.g, color.b, 0.55f * env);
                    Vector3 lampPos = glows[i].transform.position;
                    reflections[i].transform.position = lampPos + new Vector3(0f, -0.95f, 0f);
                    reflections[i].color = new Color(color.r, color.g, color.b, 0.18f * env);
                }
                if (sound != null)
                {
                    sound.volume = soundVolume * env * env;
                    sound.panStereo = Mathf.Lerp(-pan, pan, k);                     // passes across the stereo field
                    sound.pitch = basePitch * (1f + doppler * (k < 0.5f ? 1f : -1f) * Mathf.SmoothStep(0f, 1f, Mathf.Abs(k - 0.5f) * 2f)); // Doppler: higher approaching, lower leaving
                }
                yield return null;
            }
            if (sound != null) sound.Stop();
            foreach (var r in reflections) if (r != null) Object.Destroy(r.gameObject);
            Object.Destroy(root.gameObject);
        }
    }
}
