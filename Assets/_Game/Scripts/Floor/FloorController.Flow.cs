using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using Palinode.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Palinode.Floor
{
    public sealed partial class FloorController
    {
        // ------------------------------------------------------------------ boss defeated

        public void OnBossDefeated(TirazhBoss boss)
        {
            StartCoroutine(BossDeath(boss));
        }

        private IEnumerator BossDeath(TirazhBoss boss)
        {
            var room = boss.Room;
            Game.Audio.StopMusic(1.5f);
            Game.Audio.Play("boss_jam");
            Game.Shots.ClearFaction(Faction.Enemy);
            Game.Camera?.Shake(0.35f, 1.4f);
            // Every copy and Letter is crossed out with the machine.
            foreach (var e in new List<EnemyBase>(room.Enemies))
                if (e != null && e.Alive) e.Kill(false);
            yield return new WaitForSeconds(1.4f);
            _elias.SetControlLocked(true);
            yield return Newspaper();
            _elias.SetControlLocked(false);
            _bossCleared = true;
            room.MarkCleared(false);
            room.SetDoorsOpen(true, true);

            // Reward: the red pencil (75%) and a random item — only one can be taken; a full drop; the trapdoor down.
            var rp = _db["redPencil"];
            bool pencil = _rng.Chance(rp.Num("chance", 0.75f));
            var pool = _db.Pool("boss");
            string item = pool[_rng.Range(0, pool.Count)];
            var f = room.Frame;
            Vector2 mid = f.CellCenter(f.Cells.x / 2, f.Cells.y / 2 + 1);
            if (pencil)
            {
                SpawnItem(room, "pencil", mid + new Vector2(-1.6f, 0f), true, 0, 1);
                SpawnItem(room, item, mid + new Vector2(1.6f, 0f), true, 0, 1);
            }
            else SpawnItem(room, item, mid, true, 0, 1);
            SpawnPickup(room, "inkwell", mid + new Vector2(0f, -1.2f));
            BuildTrapdoor(room, mid + new Vector2(0f, -2.9f));
            RefreshMap();
            RaiseMarker("F1_boss_reward", 0.8f);
        }

        /// <summary>The newspaper slowly crawls out: "BRIDGE CRASH, 3:12 A.M. ONE DEAD" and a name smeared with ink.</summary>
        private IEnumerator Newspaper()
        {
            var canvas = UIFactory.Canvas("Newspaper", transform, null, 40);
            var root = (RectTransform)canvas.transform;
            var dim = UIFactory.Image("Dim", root, new Color(0f, 0f, 0f, 0f), ProceduralSprites.White());
            UIFactory.Stretch(dim.rectTransform);
            var paper = UIFactory.Image("Paper", root, Color.white, _root.Config.Sprite("F1_boss_newspaper"));
            paper.preserveAspect = true;
            var prt = paper.rectTransform;
            UIFactory.Anchor(prt, new Vector2(0.5f, 0.5f), new Vector2(0f, -1200f), new Vector2(760f, 912f));
            var head = UIFactory.Text("Headline", prt, _root.Config.TitleFont, 44f, new Color(0.08f, 0.07f, 0.07f));
            UIFactory.Anchor(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(640f, 120f));
            head.textWrappingMode = TextWrappingModes.Normal;
            head.fontStyle = FontStyles.Bold;
            head.text = Game.T("F1_NEWS_HEADLINE");
            var name = UIFactory.Text("Name", prt, _root.Config.TitleFont, 36f, new Color(0.1f, 0.09f, 0.09f));
            UIFactory.Anchor(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(560f, 60f));
            name.text = Game.T("F1_NEWS_NAME");
            // Ink smeared over the name: unreadable.
            for (int i = 0; i < 3; i++)
            {
                var smear = UIFactory.Image("Smear" + i, prt, new Color(0.03f, 0.03f, 0.04f, 0.93f), _root.Config.Sprite("F1_splat_" + (i + 2)));
                smear.preserveAspect = false;
                UIFactory.Anchor(smear.rectTransform, new Vector2(0.5f, 0f), new Vector2(-150f + i * 150f, 192f), new Vector2(230f, 110f));
                smear.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 23f - 12f);
            }
            RaiseMarker("F1_newspaper", 3.6f);
            float t = 0f, dur = 3.2f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                prt.anchoredPosition = new Vector2(0f, Mathf.Lerp(-1200f, 0f, k));
                prt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-6f, 1.5f, k));
                dim.color = new Color(0f, 0f, 0f, 0.55f * k);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(BotEnabled ? 1.2f : 3.5f);
            t = 0f;
            while (t < 0.8f)
            {
                t += Time.unscaledDeltaTime;
                float k = t / 0.8f;
                prt.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 1300f, k * k));
                dim.color = new Color(0f, 0f, 0f, 0.55f * (1f - k));
                yield return null;
            }
            Destroy(canvas.gameObject);
        }

        private void BuildTrapdoor(RoomView room, Vector2 at)
        {
            var spec = _db["objects"]["trapdoor"];
            _trapdoor = new GameObject("Trapdoor");
            _trapdoor.transform.SetParent(room.DecalRoot, false);
            _trapdoor.transform.position = at;
            var sr = FloorSprites.Make(_trapdoor.transform, "Sprite", FloorSprites.Get(spec.Str("open")), spec.Num("width", 1.8f), FloorSprites.FloorDecalOrder + 990);
            sr.transform.localPosition = Vector3.zero;
            Game.Fx.Burst(at, FloorFx.Ink, 20, 3f, 0.12f, 0.6f);
        }

        private void UpdateTrapdoor()
        {
            if (_trapdoor == null || Game.Current == null || Game.Current.Node.Kind != RoomKind.Boss) return;
            if (Vector2.Distance(_elias.Position, _trapdoor.transform.position) > 1.0f) return;
            ShowPromptAt("F1_PROMPT_DESCEND", _trapdoor.transform.position);
            if (_elias.Input.InteractPressed) StartCoroutine(Descend());
        }

        // ------------------------------------------------------------------ corridor & ending

        private RoomView _corridor;
        private SpriteRenderer _archivist;
        private bool _archivistSpoke, _leaving;

        private IEnumerator Descend()
        {
            if (_ending) yield break;
            _ending = true;
            _elias.SetControlLocked(true);
            Game.Audio.Play("book_close");
            Game.Audio.StopMusic(1.2f);
            yield return _root.Overlay.FadeTo(Color.black, 1f, 1.1f);
            _trapdoor = null;
            _map.SetVisible(false);
            _hud.BossBar(false, 0f, null);
            BuildCorridor();
            yield return new WaitForSecondsRealtime(0.3f);
            _elias.SetControlLocked(false);
            _ending = false;
            yield return _root.Overlay.FadeTo(Color.black, 0f, 1.2f);
            RaiseMarker("F1_corridor", 1.5f);
        }

        private void BuildCorridor()
        {
            var c = _db["corridor"];
            var node = new RoomNode(-1, new Vector2Int(-10, -10), Vector2Int.one) { Kind = RoomKind.Start };
            var go = new GameObject("Corridor");
            _corridor = go.AddComponent<RoomView>();
            var emptyLayout = new FloorLayout(0, 1, 1);
            _corridor.Build(node, emptyLayout, new Vector2(-400f, -400f), _db["rooms"].Str("corridor", "F1_corridor"), null, _db);
            if (Game.Current != null) { Game.Current.Leave(); Game.Current.SetActive(false); }
            Game.Current = _corridor;
            InCorridor = true;
            _lightTarget = 1f;
            _eliasLight.intensity = 0.4f;
            var spawn = c.Vec2("spawn", new Vector2(0, 2));
            _elias.Teleport(_corridor.Frame.CellCenter((int)spawn.x, (int)spawn.y));
            _elias.FaceTo(Gameplay.PlayerController.Facing.Right);
            _camera.SetBias(null, 0f);
            _camera.SetRoom(_corridor.Frame.ImageRect, _elias.transform, true, _db["camera"].Num("ortho", 4.75f));
            _archivist = FloorSprites.Make(_corridor.PropRoot, "Archivist", FloorSprites.Get("F1_archivist_b"), c.Num("archivistWidth", 2.4f), 0);
            _archivist.transform.position = _corridor.Frame.PxToWorld(c.Vec2("archivist", new Vector2(1600, 330)));
            _archivist.sortingOrder = FloorSprites.Order(_archivist.transform.position.y);
            Game.Audio.Ambience("rain", _rng, 1f);
        }

        private string ArchivistLine()
        {
            var run = Game.Run;
            int rev = Game.Meta.revision;
            if (run.TookPencil) return "F1_ARCH_TOOK";
            if (run.LeftPencil) return "F1_ARCH_LEFT";
            if (rev >= 10) return "F1_ARCH_10";
            if (rev >= 5) return "F1_ARCH_5";
            return "F1_ARCH_FIRST";
        }

        private void UpdateCorridor()
        {
            var c = _db["corridor"];
            if (!_archivistSpoke && Vector2.Distance(_elias.Position, _archivist.transform.position) < c.Num("talkDistance", 3.2f))
            {
                _archivistSpoke = true;
                StartCoroutine(Speak(Game.T(ArchivistLine())));
            }
            var cell = _corridor.Frame.CellAt(_elias.Position);
            if (!_leaving && _archivistSpoke && cell.x >= c.Int("exitCol", 14)) StartCoroutine(Ending());
        }

        private IEnumerator Speak(string line)
        {
            _root.Overlay.ShowSubtitle(Game.T("F1_SPEAKER_ARCHIVIST") + ": " + line, true);
            RaiseMarker("F1_archivist", 0.4f);
            yield return new WaitForSecondsRealtime(BotEnabled ? 1.2f : 4.5f);
            _root.Overlay.HideSubtitle();
        }

        private IEnumerator Ending()
        {
            _leaving = true;
            _elias.SetControlLocked(true);
            Game.Audio.Play("book_close", 0.6f);
            yield return _root.Overlay.FadeTo(Color.black, 1f, 1.4f);
            Game.Audio.StopAmbience("printhouse", 1f);
            Game.Audio.StopAmbience("rain", 1f);
            // The floor is done: no traces of this run next time.
            Game.Meta.lastDeath = new DeathRecord();
            Game.Meta.Save();
            _hud.gameObject.SetActive(false);
            var next = _db["next"];
            yield return _root.Overlay.TitleCard(Game.T(next.Str("title", "F1_NEXT_CHAPTER")), Game.T("F1_NEXT_CHAPTER_SUB"), 1.5f, BotEnabled ? 1f : 3f, 1.2f, 110f);
            RaiseMarker("F1_to_be_continued", 1.4f);
            yield return _root.Overlay.TitleCard(Game.T(next.Str("continued", "F1_TO_BE_CONTINUED")), "", 1.2f, BotEnabled ? 1.2f : 3f, 1.2f, 80f);
            Reached = true;
            string scene = next.Str("scene", "");
            if (!string.IsNullOrEmpty(scene)) SceneManager.LoadScene(scene);
            else _root.Flow.ReturnToMenu();
        }

        // ------------------------------------------------------------------ death = revision

        public void OnEliasDied(string killer)
        {
            if (_dead) return;
            _dead = true;
            StartCoroutine(Revision(killer));
        }

        private IEnumerator Revision(string killer)
        {
            _elias.SetControlLocked(true);
            _elias.ShowFallen(true);
            Game.Fx.Strike(null, _elias.Position + Vector2.up * 0.4f, 2.6f, FloorFx.Ink, 0.12f, 1.4f, -10f);
            Game.Audio.Play("revision");
            Game.Audio.StopMusic(1f);
            Game.Shots.Clear();
            yield return new WaitForSecondsRealtime(1.3f);

            int page = Game.Run.PageOf(Game.Current != null ? Game.Current.Node.Index : 0);
            string enemyName = Game.T("F1_ENEMY_" + (string.IsNullOrEmpty(killer) ? "unknown" : killer));
            Game.Meta.RegisterDeath(_db.Data.Int("chapter", 1), killer, page, Game.Run.Items);

            // The screen becomes a page.
            var canvas = UIFactory.Canvas("Revision", transform, null, 60);
            var root = (RectTransform)canvas.transform;
            var back = UIFactory.Image("Black", root, Color.black, ProceduralSprites.White());
            UIFactory.Stretch(back.rectTransform);
            var sheet = UIFactory.Image("Page", root, new Color(1f, 1f, 1f, 0f), _root.Config.Sprite("PR_sheet"));
            sheet.preserveAspect = true;
            UIFactory.Anchor(sheet.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 1060f));
            var line = UIFactory.Text("Line", sheet.rectTransform, _root.Config.Font("hand_bad"), 50f, new Color(0.1f, 0.08f, 0.08f, 0f));
            UIFactory.Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(1100f, 200f));
            line.textWrappingMode = TextWrappingModes.Normal;
            line.text = string.Format(Game.T("F1_DEATH_LINE"), enemyName, page);
            var strike = UIFactory.Image("Strike", sheet.rectTransform, new Color(0.08f, 0.06f, 0.06f, 0.95f), ProceduralSprites.White());
            strike.rectTransform.anchorMin = strike.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            strike.rectTransform.pivot = new Vector2(0f, 0.5f);
            strike.rectTransform.sizeDelta = new Vector2(0f, 6f);
            strike.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2.5f);
            var rev = UIFactory.Text("Revision", root, _root.Config.TitleFont, 150f, new Color(0.92f, 0.88f, 0.8f, 0f));
            UIFactory.Anchor(rev.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1800f, 240f));
            rev.characterSpacing = 20f;
            rev.text = string.Format(Game.T("F1_REVISION"), Game.Meta.RevisionLabel);

            yield return Fade(t => { sheet.color = new Color(1f, 1f, 1f, t); line.color = new Color(0.1f, 0.08f, 0.08f, t); }, 0.9f);
            yield return new WaitForSecondsRealtime(BotEnabled ? 0.8f : 2.2f);
            // An invisible hand strikes the line out.
            Vector2 size = line.GetPreferredValues(line.text, 1100f, 0f);
            float width = Mathf.Min(1100f, size.x) + 30f;
            strike.rectTransform.anchoredPosition = new Vector2(-width * 0.5f, 90f - 4f);
            Game.Audio.Play("enemy_struck", 1.2f);
            yield return Fade(t => strike.rectTransform.sizeDelta = new Vector2(width * t, 6f), 0.35f);
            yield return new WaitForSecondsRealtime(BotEnabled ? 0.6f : 1.4f);
            yield return Fade(t => { sheet.color = new Color(1f, 1f, 1f, 1f - t); line.alpha = 1f - t; strike.color = new Color(0.08f, 0.06f, 0.06f, 0.95f * (1f - t)); }, 0.8f);
            RaiseMarker("F1_revision_screen");
            yield return Fade(t => rev.color = new Color(0.92f, 0.88f, 0.8f, t), 1.2f);
            yield return new WaitForSecondsRealtime(BotEnabled ? 1.2f : 2.6f);
            yield return Fade(t => rev.color = new Color(0.92f, 0.88f, 0.8f, 1f - t), 1f);
            // A new run, straight into the first room of Floor I.
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static IEnumerator Fade(System.Action<float> apply, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                apply(Mathf.Clamp01(t / duration));
                yield return null;
            }
            apply(1f);
        }

        // ------------------------------------------------------------------ pause

        private CanvasGroup _pauseGroup;
        private TextMeshProUGUI _pauseRevision, _pausePages;
        private Button _resume;

        private void BuildPause()
        {
            var canvas = UIFactory.Canvas("Pause", transform, null, 50);
            var root = (RectTransform)canvas.transform;
            _pauseGroup = root.gameObject.AddComponent<CanvasGroup>();
            var dim = UIFactory.Image("Dim", root, new Color(0f, 0f, 0f, 0.72f), ProceduralSprites.White());
            UIFactory.Stretch(dim.rectTransform);
            var title = UIFactory.Text("Title", root, _root.Config.TitleFont, 90f, new Color(0.93f, 0.89f, 0.8f));
            UIFactory.Anchor(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(1200f, 120f));
            title.characterSpacing = 16f;
            title.text = Game.T("F1_PAUSE");
            _pauseRevision = UIFactory.Text("Revision", root, _root.Config.TitleFont, 52f, new Color(0.85f, 0.8f, 0.72f));
            UIFactory.Anchor(_pauseRevision.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1200f, 70f));
            _pauseRevision.characterSpacing = 10f;
            _pausePages = UIFactory.Text("Pages", root, _root.Config.Font("hand_bad"), 42f, new Color(0.82f, 0.77f, 0.68f));
            UIFactory.Anchor(_pausePages.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(1200f, 60f));
            _resume = UIFactory.Button("Resume", root, _root.Config.TitleFont, 48f, new Vector2(520f, 80f), () => SetPaused(false), out var rl);
            UIFactory.Anchor((RectTransform)_resume.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(520f, 80f));
            rl.text = Game.T("F1_RESUME");
            var quit = UIFactory.Button("Quit", root, _root.Config.TitleFont, 48f, new Vector2(520f, 80f), QuitToMenu, out var ql);
            UIFactory.Anchor((RectTransform)quit.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(520f, 80f));
            ql.text = Game.T("F1_QUIT_MENU");
            UIFactory.Navigation(new Selectable[] { _resume, quit });
            root.gameObject.SetActive(false);
        }

        private void SetPaused(bool on)
        {
            _paused = on;
            _pauseGroup.gameObject.SetActive(on);
            if (on)
            {
                _pauseRevision.text = string.Format(Game.T("F1_REVISION"), Game.Meta.RevisionLabel);
                _pausePages.text = string.Format(Game.T("F1_PAGES"), Game.Meta.pages.Count, _db["pages"].Int("total", 7));
                UIFactory.Select(_resume);
                RaiseMarker("F1_pause");
            }
            ApplyTimeScale();
        }

        private void UpdatePause()
        {
            if (PausePressed()) SetPaused(false);
        }

        private void QuitToMenu()
        {
            _paused = false;
            ApplyTimeScale();
            Game.Audio.StopMusic(0.5f);
            _root.Flow.ReturnToMenu();
        }

        // ------------------------------------------------------------------ debug (editor / development builds)

        private bool _debugPanel;
        private int _debugItem;

        private void UpdateDebug()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f4Key.wasPressedThisFrame)
            {
                _debugPanel = !_debugPanel;
                ShowDebug();
            }
            if (!_debugPanel) return;
            for (int i = 1; i <= 9; i++)
            {
                var key = kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + i - 1)] as UnityEngine.InputSystem.Controls.KeyControl;
                if (key == null || !key.wasPressedThisFrame) continue;
                DebugAction(i);
                ShowDebug();
            }
#endif
        }

        public void DebugAction(int i)
        {
            switch (i)
            {
                case 1:
                    Game.GodMode = !Game.GodMode;
                    if (_elias != null) _elias.Health.GodMode = Game.GodMode;
                    break;
                case 2:
                    if (Game.Current != null) foreach (var e in new List<EnemyBase>(Game.Current.Enemies)) if (e != null && e.Alive) e.Kill(false);
                    break;
                case 3: StartCoroutine(DebugTeleport(RoomKind.Boss)); break;
                case 4:
                    _revealMap = !_revealMap;
                    RefreshMap();
                    break;
                case 5:
                {
                    var ids = new List<string>(FloorDB.ItemIds) { "pencil" };
                    string id = ids[_debugItem++ % ids.Count];
                    GiveItem(id, FloorSprites.Get(id == "pencil" ? "PR_redpencil" : _db.Item(id).Str("icon")));
                    break;
                }
                case 6: Game.Meta.revision++; Game.Meta.Save(); break;
                case 7: Game.Meta.revision = Mathf.Max(1, Game.Meta.revision - 1); Game.Meta.Save(); break;
                case 8: Game.Meta.revision = 1; Game.Meta.edits = 0; Game.Meta.lastDeath = new DeathRecord(); Game.Meta.pages.Clear(); Game.Meta.Save(); break;
                case 9:
                    Game.AutoKill = !Game.AutoKill;
                    break;
            }
        }

        private void ShowDebug()
        {
            if (!_debugPanel) { _root.Overlay.SetDebug(string.Empty); return; }
            _root.Overlay.SetDebug(
                "FLOOR I DEBUG (F4)\n" +
                $"  1. Invulnerable: {(Game.GodMode ? "ON" : "off")}\n" +
                "  2. Kill everything in the room\n" +
                "  3. Teleport to the boss\n" +
                $"  4. Show whole map: {(_revealMap ? "ON" : "off")}\n" +
                "  5. Give next item\n" +
                $"  6/7. Revision +1 / −1 (now {Game.Meta.RevisionLabel})\n" +
                "  8. Reset meta save (revision 01)\n" +
                $"  9. Auto-kill enemies: {(Game.AutoKill ? "ON" : "off")}\n" +
                $"  seed {Game.Run?.Seed}   edits {Game.Meta.edits}");
        }

        /// <summary>Moves Elias into the first room of a kind (debug / screenshots).</summary>
        public IEnumerator DebugTeleport(RoomKind kind)
        {
            RoomView target = null;
            foreach (var r in _rooms) if (r.Node.Kind == kind) { target = r; break; }
            if (target != null) yield return DebugTeleportTo(target);
        }

        public void ShowFullMap(bool on) => _map?.ShowFull(on);

        public IEnumerator DebugDescend() => Descend();

        public IEnumerator DebugTeleportTo(RoomView target)
        {
            if (_transition || target == null) yield break;
            _transition = true;
            var from = Game.Current;
            if (from != null && from != target) { from.Leave(); from.SetActive(false); }
            Game.Shots.Clear();
            DoorView entry = target.Doors.Count > 0 ? target.Doors[0] : null;
            _elias.Teleport(entry != null ? target.EntryPoint(entry) : target.Frame.Center);
            EnterRoom(target, entry, true);
            _transition = false;
            yield return null;
        }

        public bool HasRoom(RoomKind kind)
        {
            foreach (var r in _rooms) if (r.Node.Kind == kind) return true;
            return false;
        }

        // ------------------------------------------------------------------ autopilot (tests)

        private readonly List<Vector2Int> _botPath = new List<Vector2Int>();
        private float _botRepath;
        private Vector2 _botLastPos;
        private float _botStuck;

        /// <summary>
        /// Walks the floor toward the boss (enemies die by the auto-kill cheat), takes the boss reward, goes down the
        /// trapdoor and through the corridor. Also takes items it passes.
        /// </summary>
        private void UpdateBot()
        {
            if (_elias == null || !_elias.IsAlive || _elias.ControlLocked || _transition || _dead)
            {
                if (_elias != null) { _elias.Input.BotMove = null; _elias.Input.BotInteract = false; }
                return;
            }
            var room = Game.Current;
            Vector2 goal;
            _elias.Input.BotInteract = false;
            _elias.Input.BotShoot = null;
            if (InCorridor)
            {
                goal = _corridor.Frame.CellCenter(_corridor.Frame.Cells.x - 1, 2);
            }
            else if (room.Enemies.Count > 0)
            {
                goal = room.Frame.Center;
                _elias.Input.BotShoot = Direction.Up;
            }
            else if (room.Node.Kind == RoomKind.Boss && _bossCleared)
            {
                Pickup item = null;
                foreach (var p in room.Pickups) if (p.NeedsInteract) { item = p; break; }
                if (item != null)
                {
                    goal = item.Position + Vector2.down * 0.6f;
                    if (Vector2.Distance(_elias.Position, item.Position) < 0.95f) _elias.Input.BotInteract = true;
                }
                else if (_trapdoor != null)
                {
                    goal = _trapdoor.transform.position;
                    if (Vector2.Distance(_elias.Position, goal) < 0.9f) _elias.Input.BotInteract = true;
                }
                else goal = room.Frame.Center;
            }
            else if (room.Node.Kind == RoomKind.Boss && !_bossCleared) goal = room.Frame.Center;
            else
            {
                var door = NextDoorToBoss(room);
                goal = door != null ? door.Edge + door.Side.ToVector() * 0.6f : room.Frame.Center;
            }
            _elias.Input.BotMove = BotSteer(room, goal);
        }

        private DoorView NextDoorToBoss(RoomView room)
        {
            // BFS over rooms through ordinary doors.
            var prev = new Dictionary<int, int>();
            var q = new Queue<int>();
            q.Enqueue(room.Node.Index);
            prev[room.Node.Index] = -1;
            int boss = _layout.Boss.Index;
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                if (cur == boss) break;
                foreach (var d in _layout.Rooms[cur].Doors)
                {
                    if (d.Secret || prev.ContainsKey(d.Neighbor)) continue;
                    prev[d.Neighbor] = cur;
                    q.Enqueue(d.Neighbor);
                }
            }
            if (!prev.ContainsKey(boss)) return null;
            int step = boss;
            while (prev[step] != room.Node.Index && prev[step] != -1) step = prev[step];
            return room.DoorFor(step);
        }

        private Vector2 BotSteer(RoomView room, Vector2 goal)
        {
            Vector2 pos = _elias.Position;
            if ((pos - goal).sqrMagnitude < 0.04f) return Vector2.zero;
            _botRepath -= Time.deltaTime;
            if (_botRepath <= 0f)
            {
                _botRepath = 0.35f;
                var from = room.Frame.CellAt(pos);
                var to = room.Frame.CellAt(room.Frame.ClampToFloor(goal, 0.05f));
                if (!room.Pathfinder.FindPath(from, to, _botPath)) _botPath.Clear();
                if ((pos - _botLastPos).sqrMagnitude < 0.01f) _botStuck += 0.35f; else _botStuck = 0f;
                _botLastPos = pos;
            }
            Vector2 wp = goal;
            for (int i = 1; i < _botPath.Count; i++)
            {
                var c = room.Frame.CellCenter(_botPath[i]);
                if ((c - pos).sqrMagnitude > 0.09f) { wp = i == _botPath.Count - 1 ? goal : c; break; }
            }
            Vector2 d = wp - pos;
            if (_botStuck > 1.5f) d += Random.insideUnitCircle * 2f;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.zero;
        }
    }
}
