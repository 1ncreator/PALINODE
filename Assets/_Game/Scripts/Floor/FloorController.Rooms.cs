using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using TMPro;
using UnityEngine;

namespace Palinode.Floor
{
    public sealed partial class FloorController
    {
        // ------------------------------------------------------------------ special rooms

        private void PopulateSpecial(RoomView room)
        {
            var rng = new SeededRandom(SeededRandom.Mix(room.Node.ContentSeed, 99));
            switch (room.Node.Kind)
            {
                case RoomKind.Start: PopulateStart(room); break;
                case RoomKind.Corrector:
                {
                    var pool = _db.Pool("corrector");
                    var at = room.Frame.PxToWorld(_db["corrector"].Vec2("item", new Vector2(845, 400)));
                    SpawnItem(room, pool[rng.Range(0, pool.Count)], at, false, 0, 0, 0.1f);
                    break;
                }
                case RoomKind.Memory: PopulateMemory(room); break;
                case RoomKind.Shop: PopulateShop(room, rng); break;
                case RoomKind.Secret: PopulateSecret(room, rng); break;
            }
        }

        private void PopulateStart(RoomView room)
        {
            var s = _db["start"];
            var deskSpec = _db["objects"]["desk"];
            var deskCell = new Vector2Int((int)s.Vec2("desk", new Vector2(2, 0)).x, (int)s.Vec2("desk", new Vector2(2, 0)).y);
            var desk = FloorSprites.Make(room.PropRoot, "Desk", FloorSprites.Get(deskSpec.Str("sprite")), deskSpec.Num("width", 2.4f), 0);
            desk.transform.position = room.FloorBase(deskCell, 2);
            desk.sortingOrder = FloorSprites.Order(desk.transform.position.y);
            var col = new GameObject("DeskCollider") { layer = Layers.LowObstacle };
            col.transform.SetParent(room.PropRoot, false);
            col.transform.position = room.Frame.CellCenter(deskCell) + new Vector2(0.5f, 0f);
            col.AddComponent<BoxCollider2D>().size = new Vector2(2f, room.Frame.CellSize.y * 0.8f);
            room.Pathfinder.SetBlocked(deskCell.x, deskCell.y, true);
            room.Pathfinder.SetBlocked(deskCell.x + 1, deskCell.y, true);

            AddWallClock(room, s.Vec2("clock", new Vector2(1455, 140)), s.Num("clockWidth", 1.1f));

            // Chalk hints on the floor, away from where Elias stands.
            var chalk = _root.Config.Font("hand_caveat");
            var at = s.Vec2("hintsAt", new Vector2(9, 3));
            Vector2 first = room.Frame.CellCenter((int)at.x, (int)at.y);
            int line = 0;
            foreach (var k in s["hints"].Items())
            {
                var t = FloorSprites.Text(room.DecalRoot, "Chalk", Game.T(k.AsString()), chalk, 2.9f, new Color(0.88f, 0.87f, 0.82f, 0.6f),
                    FloorSprites.FloorDecalOrder + 950);
                t.transform.position = first + new Vector2(0f, -line * room.Frame.CellSize.y * 0.58f);
                t.transform.rotation = Quaternion.Euler(0f, 0f, ((line * 37) % 5 - 2) * 0.8f);
                line++;
            }
            // A line in another, neat girl's hand from revision 5 on.
            foreach (var g in s["girl"].Items())
            {
                if (Game.Meta.revision < g.Int("revision", 99)) continue;
                var t = FloorSprites.Text(room.DecalRoot, "GirlLine", Game.T(g.Str("key")), _root.Config.Font("hand_marck"), 3.3f,
                    new Color(0.93f, 0.92f, 0.9f, 0.72f), FloorSprites.FloorDecalOrder + 951);
                t.transform.position = room.Frame.CellCenter(room.Frame.Cells.x / 2 + 3, room.Frame.Cells.y - 1) + new Vector2(0f, 0.05f);
                t.transform.rotation = Quaternion.Euler(0f, 0f, -3f);
                break;
            }
        }

        /// <summary>The wall clock stopped at 03:12 (no ticking).</summary>
        private void AddWallClock(RoomView room, Vector2 px, float width)
        {
            var at = room.Frame.PxToWorld(px);
            var face = FloorSprites.Make(room.PropRoot, "WallClock", FloorSprites.Get("PR_wallclock_face"), width, FloorSprites.DoorOrder + 10);
            face.transform.position = at;
            float scale = face.transform.localScale.x;
            var hour = FloorSprites.Make(face.transform, "Hour", FloorSprites.Get("PR_wallclock_hour"), 0f, FloorSprites.DoorOrder + 11);
            hour.transform.localRotation = Quaternion.Euler(0f, 0f, -(3f + 12f / 60f) * 30f);
            var minute = FloorSprites.Make(face.transform, "Minute", FloorSprites.Get("PR_wallclock_minute"), 0f, FloorSprites.DoorOrder + 12);
            minute.transform.localRotation = Quaternion.Euler(0f, 0f, -12f * 6f);
        }

        private void PopulateMemory(RoomView room)
        {
            var m = _db["memory"];
            string sprite = "F1_drawing_1";
            foreach (var d in m["drawings"].Items())
                if (Game.Meta.revision >= d.Int("revision", 1)) { sprite = d.Str("sprite"); break; }
            var dr = m["drawing"];
            bool topDoor = false;
            foreach (var d in room.Doors) if (d.Side == Direction.Up) topDoor = true;
            var pic = FloorSprites.Make(room.PropRoot, "Drawing", FloorSprites.Get(sprite), dr.Num("width", 2.1f), FloorSprites.DoorOrder + 10);
            pic.transform.position = room.Frame.PxToWorld(topDoor ? dr.Vec2("altAt", new Vector2(1500, 185)) : dr.Vec2("at", new Vector2(862, 175)));
            pic.transform.rotation = Quaternion.Euler(0f, 0f, 2f);
            var cap = FloorSprites.Text(room.PropRoot, "Caption", Game.T(m.Str("caption", "F1_DRAWING_CAPTION")), _root.Config.Font("hand_caveat"), 2.4f,
                new Color(0.25f, 0.2f, 0.45f, 0.95f), FloorSprites.DoorOrder + 11);
            cap.transform.position = (Vector2)pic.transform.position + new Vector2(0f, -pic.bounds.size.y * 0.5f - 0.18f);
            cap.transform.rotation = Quaternion.Euler(0f, 0f, 1.5f);
        }

        private void PopulateShop(RoomView room, SeededRandom rng)
        {
            var s = _db["shop"];
            var pool = _db.Pool("shop");
            rng.Shuffle(pool);
            int i = 0;
            foreach (var t in s["tables"].Items())
            {
                if (i >= pool.Count || i >= s.Int("count", 3)) break;
                var at = room.Frame.PxToWorld(new Vector2(t[0].AsFloat(), t[1].AsFloat()));
                int price = rng.Range(s.Int("priceMin", 3), s.Int("priceMax", 5) + 1);
                SpawnItem(room, pool[i], at, false, price, 0, 0.1f);
                i++;
            }
            // The Binder sits on her stool and hardly says a word. Her face is never seen.
            var binder = FloorSprites.Make(room.PropRoot, "Binder", FloorSprites.Get("F1_binder_a"), s.Num("binderWidth", 1.3f), 0);
            binder.transform.position = room.Frame.PxToWorld(s.Vec2("binder", new Vector2(1165, 415)));
            binder.sortingOrder = FloorSprites.Order(binder.transform.position.y);
            StartCoroutine(BinderIdle(binder));
        }

        private IEnumerator BinderIdle(SpriteRenderer sr)
        {
            var a = FloorSprites.Get("F1_binder_a");
            var b = FloorSprites.Get("F1_binder_b");
            bool alt = false;
            while (sr != null)
            {
                yield return new WaitForSeconds(Random.Range(3.5f, 7f));
                if (sr == null) yield break;
                alt = !alt;
                float w = sr.bounds.size.x;
                sr.sprite = alt ? b : a;
                sr.transform.localScale = Vector3.one * FloorSprites.ScaleFor(sr.sprite, _db["shop"].Num("binderWidth", 1.3f));
            }
        }

        private void PopulateSecret(RoomView room, SeededRandom rng)
        {
            var s = _db["secret"];
            var pool = _db.Pool("secret");
            SpawnItem(room, pool[rng.Range(0, pool.Count)], room.Frame.PxToWorld(s.Vec2("item", new Vector2(725, 545))), false, 0, 0, 0.35f);
            var pageCell = s.Vec2("page", new Vector2(2, 3));
            var pageGo = new GameObject("Page");
            var page = pageGo.AddComponent<Pickup>();
            page.Init(room, PickupKind.Page, room.Frame.CellCenter((int)pageCell.x, (int)pageCell.y), null, 0, false, 0, s.Int("pageIndex", 1));
            if (Game.Meta.pages.Contains(s.Int("pageIndex", 1))) page.Remove(false);
            // The note on the floor: "What is written in red becomes true."
            var noteCell = s.Vec2("note", new Vector2(4, 1));
            var note = FloorSprites.Make(room.DecalRoot, "Note", FloorSprites.Get("PR_sheet"), 1.5f, FloorSprites.FloorDecalOrder + 960);
            note.transform.position = room.Frame.CellCenter((int)noteCell.x, (int)noteCell.y);
            note.transform.rotation = Quaternion.Euler(0f, 0f, -8f);
            var text = FloorSprites.Text(room.DecalRoot, "NoteText", Game.T(s.Str("noteKey", "F1_SECRET_NOTE")), _root.Config.Font("hand_bad"), 1.25f,
                new Color(0.62f, 0.05f, 0.06f), FloorSprites.FloorDecalOrder + 961);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.rectTransform.sizeDelta = new Vector2(1.2f, 1.2f);
            text.transform.position = note.transform.position;
            text.transform.rotation = note.transform.rotation;
            room.gameObject.AddComponent<NoteReader>().Init(note.transform, s.Str("noteKey", "F1_SECRET_NOTE"));
        }

        /// <summary>Shows the note's text as a banner when Elias comes close (once).</summary>
        private sealed class NoteReader : MonoBehaviour
        {
            private Transform _note;
            private string _key;
            private bool _read;

            public void Init(Transform note, string key) { _note = note; _key = key; }

            private void Update()
            {
                if (_read || Game.Player == null || _note == null) return;
                if (Vector2.Distance(Game.Player.Position, _note.position) > 1.1f) return;
                _read = true;
                Game.Ctrl.Hud.Banner(Game.T("F1_NOTE_TITLE"), Game.T(_key), 5f);
                Game.Audio?.Play("pickup_page", 0.6f);
            }
        }

        // ------------------------------------------------------------------ traces of past revisions

        private void PlaceTraces()
        {
            var last = Game.Meta.lastDeath;
            if (last == null || last.floor != _db.Data.Int("chapter", 1)) return;
            var combat = new List<RoomView>();
            foreach (var r in _rooms) if (r.Node.Kind == RoomKind.Combat) combat.Add(r);
            if (combat.Count == 0) return;
            _rng.Shuffle(combat);
            // The crossed-out body of the last Elias, with one of his items.
            var bodyRoom = combat[0];
            var pos = bodyRoom.Frame.CellCenter(bodyRoom.Frame.Cells.x / 2 + _rng.Range(-3, 4), _rng.Range(2, bodyRoom.Frame.Cells.y - 2));
            var body = FloorSprites.Make(bodyRoom.PropRoot, "PastElias", FloorSprites.Get("F1_elias_fallen"), 2.2f, FloorSprites.FloorDecalOrder + 980);
            body.transform.position = pos;
            body.color = new Color(0.38f, 0.36f, 0.4f, 0.9f);
            var strike = FloorSprites.Quad(body.transform, "Struck", FloorFx.Ink, new Vector2(2.4f, 0.07f), FloorSprites.FloorDecalOrder + 981, FloorSprites.Solid);
            strike.transform.position = pos + new Vector2(0f, 0.2f);
            strike.transform.rotation = Quaternion.Euler(0f, 0f, -12f);
            string item = last.items != null && last.items.Count > 0 ? last.items[_rng.Range(0, last.items.Count)] : null;
            if (!string.IsNullOrEmpty(item) && _db.Item(item).Has("icon")) SpawnItem(bodyRoom, item, pos + new Vector2(1.3f, -0.2f), false);
            else SpawnPickup(bodyRoom, "sheet", pos + new Vector2(1.3f, -0.2f));

            // The killer's kind comes back once as the Witness of this revision.
            if (!string.IsNullOrEmpty(last.killer) && System.Array.IndexOf(FloorDB.EnemyIds, last.killer) >= 0 && combat.Count > 1)
            {
                _witnessType = last.killer;
                // Headlights only live in dark rooms: prefer one; otherwise any far room.
                RoomView pick = null;
                foreach (var r in combat)
                    if (r != bodyRoom && (last.killer != "headlights" || r.IsDark)) { pick = r; break; }
                if (pick == null) foreach (var r in combat) if (r != bodyRoom) { pick = r; break; }
                _witnessRoom = pick != null ? pick.Node.Index : -1;
            }
        }

        // ------------------------------------------------------------------ ghost car

        private void ChooseGhostCar()
        {
            var gc = _db["ghostCar"];
            if (!_rng.Chance(gc.Num("chance", 1f))) return;
            var candidates = new List<int>();
            foreach (var r in _rooms)
                if (r.Node.Kind != RoomKind.Start && r.Node.Kind != RoomKind.Secret && r.Frame.Spec.Windows.Count > 0) candidates.Add(r.Node.Index);
            if (candidates.Count > 0) _ghostRoom = candidates[_rng.Range(0, candidates.Count)];
        }

        /// <summary>Headlights sweep across the windows and a truck rumbles far away. There is no car.</summary>
        private IEnumerator GhostCar(RoomView room)
        {
            _ghostDone = true;
            var gc = _db["ghostCar"];
            yield return new WaitForSeconds(Random.Range(gc["delay"][0].AsFloat(4f), gc["delay"][1].AsFloat(10f)));
            if (Game.Current != room || _dead) { _ghostDone = false; yield break; }
            float dur = gc.Num("duration", 2.4f);
            Game.Audio.Play("ghost_car");
            var glows = new List<(SpriteRenderer sr, Rect win)>();
            foreach (var wpx in room.Frame.Spec.Windows)
            {
                var win = room.Frame.PxRectToWorld(wpx);
                var maskGo = new GameObject("WindowMask");
                maskGo.transform.SetParent(room.PropRoot, false);
                maskGo.transform.position = win.center;
                var mask = maskGo.AddComponent<SpriteMask>();
                mask.sprite = UI.ProceduralSprites.White();
                var mb = mask.sprite.bounds.size;
                maskGo.transform.localScale = new Vector3(win.width / mb.x, win.height / mb.y, 1f);
                var glow = FloorSprites.Make(room.PropRoot, "Headlights", UI.ProceduralSprites.SoftDot(64, 0.1f), 0f, FloorSprites.DoorOrder + 20, FloorSprites.Glow);
                var gb = glow.sprite.bounds.size;
                glow.transform.localScale = new Vector3(win.height * 1.3f / gb.x, win.height * 0.9f / gb.y, 1f);
                glow.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                glow.color = new Color(1f, 0.93f, 0.78f, 0f);
                glows.Add((glow, win));
                Destroy(maskGo, dur + 0.5f);
            }
            float t = 0f;
            RaiseMarker("F1_ghostcar", 0.9f);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                foreach (var (sr, win) in glows)
                {
                    if (sr == null) continue;
                    sr.transform.position = new Vector2(Mathf.Lerp(win.xMin - win.height, win.xMax + win.height, k), win.center.y - win.height * 0.1f);
                    sr.color = new Color(1f, 0.93f, 0.78f, Mathf.Sin(k * Mathf.PI) * 0.8f);
                }
                yield return null;
            }
            foreach (var (sr, _) in glows) if (sr != null) Destroy(sr.gameObject);
        }
    }
}
