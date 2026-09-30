using System.Collections.Generic;
using Palinode.Core;
using Palinode.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Palinode.Floor
{
    /// <summary>
    /// The map as a table of contents (from RogueDungeon's MinimapView): on the paper of F1_ui_map, visited rooms are
    /// washed with ink, unvisited neighbours are pencil outlines, special rooms carry their icons. Small in the corner;
    /// Tab shows it as a whole page "Contents. Chapter I. The Press".
    /// </summary>
    public sealed class FloorMap : MonoBehaviour
    {
        private sealed class View
        {
            public RectTransform Root, Content;
            public Image[] Fill, Outline, Icon;
            public Image Marker;
            public float Cell;
        }

        private FloorLayout _layout;
        private View _mini, _full;
        private CanvasGroup _fullGroup;
        private GameConfig _config;
        private static Sprite _outlineSprite;
        private Vector2Int _min, _max;

        public bool FullShown { get; private set; }

        public void Build(Transform canvas, GameConfig config, FloorLayout layout)
        {
            _config = config;
            _layout = layout;
            _min = new Vector2Int(int.MaxValue, int.MaxValue);
            _max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var r in layout.Rooms)
                foreach (var c in r.Cells)
                {
                    _min = Vector2Int.Min(_min, c);
                    _max = Vector2Int.Max(_max, c);
                }

            // Mini map, top right.
            var miniRoot = UIFactory.Rect("MiniMap", canvas);
            UIFactory.Anchor(miniRoot, new Vector2(1f, 1f), new Vector2(-30f, -24f), new Vector2(250f, 205f));
            miniRoot.pivot = new Vector2(1f, 1f);
            _mini = MakeView(miniRoot, 30f, false);

            // Full page.
            var fullRoot = UIFactory.Rect("FullMap", canvas);
            UIFactory.Stretch(fullRoot);
            _fullGroup = fullRoot.gameObject.AddComponent<CanvasGroup>();
            _fullGroup.alpha = 0f;
            _fullGroup.blocksRaycasts = false;
            var dim = UIFactory.Image("Dim", fullRoot, new Color(0f, 0f, 0f, 0.6f), ProceduralSprites.White());
            UIFactory.Stretch(dim.rectTransform);
            var page = UIFactory.Rect("Page", fullRoot);
            UIFactory.Anchor(page, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180f, 940f));
            _full = MakeView(page, 88f, true);
            var head = UIFactory.Text("Heading", page, config.Font("hand_bad"), 56f, new Color(0.18f, 0.14f, 0.12f));
            UIFactory.Anchor(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(1000f, 80f));
            head.text = Game.T("F1_MAP_TITLE");
            var sub = UIFactory.Text("Chapter", page, config.Font("hand_bad"), 40f, new Color(0.25f, 0.2f, 0.17f));
            UIFactory.Anchor(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(1000f, 60f));
            sub.text = Game.T("F1_MAP_CHAPTER");
        }

        private View MakeView(RectTransform root, float cell, bool full)
        {
            var v = new View { Root = root, Cell = cell };
            var paper = UIFactory.Image("Paper", root, Color.white, _config.Sprite("F1_map_paper"));
            UIFactory.Stretch(paper.rectTransform);
            v.Content = UIFactory.Rect("Rooms", root);
            int gw = _max.x - _min.x + 1, gh = _max.y - _min.y + 1;
            float w = gw * cell, h = gh * cell * 0.72f;
            UIFactory.Anchor(v.Content, new Vector2(0.5f, 0.5f), new Vector2(0f, full ? -40f : 0f), new Vector2(w, h));
            // Fit the mini map into its paper.
            if (!full)
            {
                float sx = (root.sizeDelta.x - 60f) / Mathf.Max(1f, w), sy = (root.sizeDelta.y - 60f) / Mathf.Max(1f, h);
                v.Content.localScale = Vector3.one * Mathf.Min(1f, Mathf.Min(sx, sy));
            }
            else
            {
                float sx = 1000f / Mathf.Max(1f, w), sy = 640f / Mathf.Max(1f, h);
                v.Content.localScale = Vector3.one * Mathf.Min(1.2f, Mathf.Min(sx, sy));
            }
            int n = _layout.Rooms.Count;
            v.Fill = new Image[n];
            v.Outline = new Image[n];
            v.Icon = new Image[n];
            foreach (var r in _layout.Rooms)
            {
                var pos = new Vector2((r.Origin.x - _min.x) * cell, (r.Origin.y - _min.y) * cell * 0.72f);
                var size = new Vector2(r.Size.x * cell, r.Size.y * cell * 0.72f);
                var fill = UIFactory.Image("Room" + r.Index, v.Content, new Color(0.12f, 0.1f, 0.1f, 0.78f), ProceduralSprites.RoundedRect(6));
                Place(fill.rectTransform, pos, size, cell * 0.1f);
                var outline = UIFactory.Image("Outline" + r.Index, v.Content, new Color(0.3f, 0.27f, 0.25f, 0.9f), OutlineSprite());
                outline.type = Image.Type.Sliced;
                Place(outline.rectTransform, pos, size, cell * 0.1f);
                v.Fill[r.Index] = fill;
                v.Outline[r.Index] = outline;
                string icon = IconFor(r.Kind);
                if (icon != null)
                {
                    var im = UIFactory.Image("Icon" + r.Index, v.Content, Color.white, _config.Sprite(icon));
                    im.preserveAspect = true;
                    float s = Mathf.Min(size.x, size.y) * 0.8f;
                    im.rectTransform.anchorMin = im.rectTransform.anchorMax = Vector2.zero;
                    im.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    im.rectTransform.anchoredPosition = pos + size * 0.5f;
                    im.rectTransform.sizeDelta = new Vector2(s, s);
                    v.Icon[r.Index] = im;
                }
            }
            v.Marker = UIFactory.Image("Elias", v.Content, new Color(0.72f, 0.08f, 0.08f, 1f), ProceduralSprites.SoftDot(32, 0.7f));
            v.Marker.rectTransform.anchorMin = v.Marker.rectTransform.anchorMax = Vector2.zero;
            v.Marker.rectTransform.sizeDelta = new Vector2(cell * 0.35f, cell * 0.35f);
            return v;
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size, float pad)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos + Vector2.one * pad;
            rt.sizeDelta = size - Vector2.one * pad * 2f;
        }

        private static string IconFor(RoomKind k)
        {
            switch (k)
            {
                case RoomKind.Corrector: return "F1_icon_lamp";
                case RoomKind.Memory: return "F1_icon_musicbox";
                case RoomKind.Boss: return "F1_icon_gear";
                case RoomKind.Secret: return "F1_icon_star";
                case RoomKind.Shop: return "F1_icon_needle";
                default: return null;
            }
        }

        /// <summary>Visited rooms: ink wash. Unvisited rooms next to a visited one (or revealed): pencil outline.</summary>
        public void Refresh(RoomNode current, IList<RoomView> rooms, bool revealAll, bool bossCleared)
        {
            foreach (var v in new[] { _mini, _full })
            {
                if (v == null) continue;
                foreach (var r in _layout.Rooms)
                {
                    var view = rooms[r.Index];
                    bool visited = view != null && view.Visited;
                    bool secretFound = r.Kind != RoomKind.Secret || visited || SecretFound(view);
                    bool seen = revealAll || visited || (secretFound && HasVisitedNeighbor(r, rooms));
                    if (r.Kind == RoomKind.Secret && !secretFound && !revealAll) seen = false;
                    v.Fill[r.Index].gameObject.SetActive(visited);
                    v.Fill[r.Index].color = r == current ? new Color(0.2f, 0.16f, 0.15f, 0.92f) : new Color(0.1f, 0.08f, 0.08f, 0.62f);
                    v.Outline[r.Index].gameObject.SetActive(seen && !visited);
                    if (v.Icon[r.Index] != null)
                    {
                        v.Icon[r.Index].gameObject.SetActive(seen);
                        if (r.Kind == RoomKind.Boss && bossCleared) v.Icon[r.Index].sprite = _config.Sprite("F1_icon_trapdoor");
                    }
                }
                if (current != null)
                {
                    var pos = new Vector2((current.Origin.x - _min.x + current.Size.x * 0.5f) * v.Cell,
                        (current.Origin.y - _min.y + current.Size.y * 0.5f) * v.Cell * 0.72f);
                    v.Marker.rectTransform.anchoredPosition = pos;
                }
            }
        }

        private static bool SecretFound(RoomView secret)
        {
            if (secret == null) return false;
            foreach (var d in secret.Doors) if (d.Discovered) return true;
            return false;
        }

        private bool HasVisitedNeighbor(RoomNode r, IList<RoomView> rooms)
        {
            foreach (var d in r.Doors)
            {
                var n = rooms[d.Neighbor];
                if (n != null && n.Visited && (!d.Secret || SecretOpen(rooms[r.Index], d))) return true;
            }
            return false;
        }

        private static bool SecretOpen(RoomView room, DoorLink link)
        {
            if (room == null) return false;
            foreach (var d in room.Doors) if (d.Link.Neighbor == link.Neighbor && d.Link.Cell == link.Cell) return d.Discovered;
            return false;
        }

        public void ShowFull(bool on)
        {
            FullShown = on;
            if (_fullGroup != null) _fullGroup.alpha = on ? 1f : 0f;
            if (_mini != null) _mini.Root.gameObject.SetActive(!on && _visible);
        }

        private bool _visible = true;

        public void SetVisible(bool on)
        {
            _visible = on;
            if (!on) ShowFull(false);
            if (_mini != null) _mini.Root.gameObject.SetActive(on && !FullShown);
        }

        /// <summary>Pencil outline for the map cells (9-sliced).</summary>
        private static Sprite OutlineSprite()
        {
            if (_outlineSprite != null) return _outlineSprite;
            const int s = 32, b = 3;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool edge = x < b || y < b || x >= s - b || y >= s - b;
                px[x + y * s] = edge ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            _outlineSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b + 1, b + 1, b + 1, b + 1));
            return _outlineSprite;
        }
    }
}
