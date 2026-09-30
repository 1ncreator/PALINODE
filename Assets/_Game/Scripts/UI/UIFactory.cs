using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Palinode.UI
{
    /// <summary>Helpers to assemble uGUI hierarchies from code (menus and overlays are built at runtime).</summary>
    public static class UIFactory
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static Button Button(string name, Transform parent, TMP_FontAsset font, float fontSize, Vector2 size,
            UnityAction onClick, out TextMeshProUGUI label)
        {
            // The graphic stays opaque white: ColorTint multiplies it, so the ColorBlock alone decides what is visible.
            var img = Image(name, parent, Color.white, ProceduralSprites.RoundedRect(12));
            img.raycastTarget = true;
            img.rectTransform.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(0.85f, 0.72f, 0.48f, 0.22f);
            colors.selectedColor = new Color(0.85f, 0.72f, 0.48f, 0.22f);
            colors.pressedColor = new Color(0.95f, 0.8f, 0.52f, 0.4f);
            colors.fadeDuration = 0.1f;
            btn.colors = colors;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);
            label = Text("Label", img.transform, font, fontSize, new Color(0.9f, 0.86f, 0.78f), TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            btn.gameObject.AddComponent<HoverFeedback>().Label = label;
            return btn;
        }

        public static void Navigation(Selectable[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var nav = new Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
                nav.selectOnUp = items[(i - 1 + items.Length) % items.Length];
                nav.selectOnDown = items[(i + 1) % items.Length];
                items[i].navigation = nav;
            }
        }

        public static void Select(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        public static Canvas Canvas(string name, Transform parent, Camera uiCamera, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            if (uiCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = 1f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static string Format(Func<string> f) => f != null ? f() : string.Empty;
    }
}
