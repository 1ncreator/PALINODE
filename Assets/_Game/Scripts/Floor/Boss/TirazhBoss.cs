using System.Collections;
using System.Collections.Generic;
using Palinode.Core;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// "Tirazh" (Print Run), the press at the top of the 26×14 arena. Immobile body, a flywheel that spins faster each
    /// phase and the platen in its mouth, which opens for 2 s after every roller pass (double damage there).
    ///   Phase 1 "Proof run"  (100–65%): a roller crosses one of three floor bands; a fan of 5 sheets every 3 s.
    ///   Phase 2 "Rush print" (65–30%): rings of 12 sheets every 4 s (offset by half a step each time), side belts,
    ///                                  rollers on two bands at once, three Letters every 10 s.
    ///   Phase 3 "Impressions" (30–0%): ink copies of Elias (≤2) replaying him 3 s late; the "Headline" — a shadow grows
    ///                                  under Elias and a huge newspaper falls. Below 10% everything comes twice as often.
    /// </summary>
    public sealed class TirazhBoss : EnemyBase
    {
        private JNode _b;
        private int _phase = 1;
        private float _rollerT, _fanT, _ringT, _lettersT, _copiesT, _headlineT;
        private bool _ringOffset;
        private bool _platenOpen;
        private float _platenUntil;
        private SpriteRenderer _flywheel, _platen, _platenGlow;
        private AudioSource _whir;
        private Vector2 _mouth;
        private readonly List<InkCopy> _copies = new List<InkCopy>();
        private readonly List<GameObject> _sideBelts = new List<GameObject>();
        private int _bodyCol, _bodyRows;
        private bool _deathStarted;

        public int Phase => _phase;
        public float Fraction => Mathf.Clamp01(Hp / Mathf.Max(1f, MaxHp));
        public bool PlatenOpen => _platenOpen;
        public override bool StopsPiercing => true;

        protected override void OnSpawned()
        {
            _b = Game.DB.Data["boss"];
            ContactDamage = 0;
            Rb.bodyType = RigidbodyType2D.Kinematic;
            Col.enabled = false;
            var body = _b["body"];
            _bodyCol = body.Int("col", 10);
            int cw = body["cells"][0].AsInt(6);
            _bodyRows = body["cells"][1].AsInt(3);
            var f = Room.Frame;
            Vector2 baseP = Room.FloorBase(new Vector2Int(_bodyCol, _bodyRows - 1), cw);
            transform.position = baseP;
            Rb.position = baseP;

            // Body: blocks walking and ink (shots at it land on the boss).
            Width = cw;
            SetSprite(FloorSprites.Get(body.Str("sprite", "F1_boss_body")));
            var bodyCol = new GameObject("BodyCollider") { layer = Layers.Obstacle };
            bodyCol.transform.SetParent(transform, false);
            var box = bodyCol.AddComponent<BoxCollider2D>();
            box.size = new Vector2(cw * f.CellSize.x * 0.95f, _bodyRows * f.CellSize.y);
            box.offset = new Vector2(0f, _bodyRows * f.CellSize.y * 0.5f - f.CellSize.y * 0.15f);
            ShadowSr.enabled = false;

            // Flywheel on the left side of the body.
            var fw = _b["flywheel"];
            _flywheel = FloorSprites.Make(transform, "Flywheel", FloorSprites.Get(fw.Str("sprite")), fw.Num("width", 2.4f), 0);
            _flywheel.transform.localPosition = new Vector3(-cw * 0.5f - 0.2f, _bodyRows * f.CellSize.y * 0.55f, 0f);

            // Platen in the mouth (front middle), the weak spot.
            var pl = _b["platen"];
            _mouth = baseP + new Vector2(0f, 0.25f);
            var platenGo = new GameObject("Platen") { layer = Layers.Obstacle };
            platenGo.transform.SetParent(transform, false);
            platenGo.transform.position = _mouth + new Vector2(0f, -0.05f);
            _platen = FloorSprites.Make(platenGo.transform, "Sprite", FloorSprites.Get(pl.Str("closed")), pl.Num("width", 2.6f), 0);
            _platenGlow = MakeGlow(platenGo.transform, 2.6f, new Color(1f, 0.8f, 0.55f, 0f));
            _platenGlow.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var pbox = platenGo.AddComponent<BoxCollider2D>();
            pbox.size = new Vector2(pl.Num("width", 2.6f) * 0.9f, 0.7f);
            pbox.offset = new Vector2(0f, 0.3f);
            var fwd = platenGo.AddComponent<HitForwarder>();
            fwd.OnHit = hit =>
            {
                if (_platenOpen) hit.Damage *= pl.Num("weakMul", 2f);
                return TakeHit(hit);
            };

            _whir = Game.Audio?.PlayLoop("boss_flywheel");
            float grace = 1.5f;
            _rollerT = RateAdjusted(_b["roller"]["interval"][0].AsFloat(3.4f)) + grace;
            _fanT = RateAdjusted(_b["fan"].Num("interval", 3f)) + grace * 0.5f;
            _ringT = _b["ring"].Num("interval", 4f);
            _lettersT = _b["letters"].Num("interval", 10f);
            _copiesT = 2f;
            _headlineT = 3f;
            Game.Ctrl?.ShowBossBar(true, 1f);
        }

        private float RateAdjusted(float interval) => Fraction < _b["enrage"].Num("below", 0.1f) ? interval / _b["enrage"].Num("rateMul", 2f) : interval;

        protected override void Update()
        {
            base.Update();
            if (Room == null || _flywheel == null) return;
            float speed = _b["flywheel"]["speed"][_phase - 1].AsFloat(80f);
            if (!Alive) speed *= Mathf.Clamp01(1f - (Time.time - _deadAt) / 1.8f);
            _flywheel.transform.Rotate(0f, 0f, -speed * Time.deltaTime);
            int order = FloorSprites.Order(Feet.y);
            _flywheel.sortingOrder = order - 2;
            _platen.sortingOrder = order + 3;
            _platenGlow.sortingOrder = order + 2;
            if (_platenOpen && Time.time > _platenUntil) SetPlaten(false);
        }

        private float _deadAt;

        protected override void FixedUpdate() { }

        protected override void Tick(float dt)
        {
            Desired = Vector2.zero;
            // Phase changes.
            var ph = _b["phases"];
            int want = Fraction <= ph[1].AsFloat(0.3f) ? 3 : Fraction <= ph[0].AsFloat(0.65f) ? 2 : 1;
            if (want != _phase) EnterPhase(want);

            _rollerT -= dt;
            if (_rollerT <= 0f)
            {
                _rollerT = RateAdjusted(_b["roller"]["interval"][_phase - 1].AsFloat(3f));
                StartCoroutine(RollerPass(_phase >= 2 ? 2 : 1));
            }
            _fanT -= dt;
            if (_fanT <= 0f && _phase != 2)
            {
                _fanT = RateAdjusted(_b["fan"].Num("interval", 3f));
                Fan();
            }
            if (_phase >= 2)
            {
                _ringT -= dt;
                if (_ringT <= 0f)
                {
                    _ringT = RateAdjusted(_b["ring"].Num("interval", 4f));
                    Ring();
                }
                _lettersT -= dt;
                if (_lettersT <= 0f && _phase == 2)
                {
                    _lettersT = RateAdjusted(_b["letters"].Num("interval", 10f));
                    SpitLetters();
                }
            }
            if (_phase >= 3)
            {
                _copies.RemoveAll(c => c == null || !c.Alive);
                _copiesT -= dt;
                if (_copiesT <= 0f)
                {
                    _copiesT = RateAdjusted(_b["copies"].Num("interval", 7f));
                    if (_copies.Count < _b["copies"].Int("max", 2)) PrintCopy();
                }
                _headlineT -= dt;
                if (_headlineT <= 0f)
                {
                    _headlineT = RateAdjusted(_b["headline"].Num("interval", 6f));
                    StartCoroutine(Headline());
                }
            }
        }

        protected override void OnDamaged(HitInfo hit) => Game.Ctrl?.ShowBossBar(true, Fraction);

        private void EnterPhase(int p)
        {
            _phase = p;
            Game.Camera?.Shake(0.25f, 0.4f);
            Game.Audio?.Play("press_clack");
            Game.Fx?.Burst(_mouth + Vector2.up, FloorFx.Ink, 30, 5f, 0.15f, 0.6f);
            if (p == 2) SetSideBelts(true);
            if (p == 3) SetSideBelts(false);
        }

        private void SetSideBelts(bool on)
        {
            var f = Room.Frame;
            var left = new List<Vector2Int>();
            var right = new List<Vector2Int>();
            for (int r = _bodyRows; r < f.Cells.y; r++)
            {
                left.Add(new Vector2Int(0, r));
                right.Add(new Vector2Int(f.Cells.x - 1, r));
            }
            Room.SetBelt(left, on ? new Vector2Int(0, 1) : Vector2Int.zero);
            Room.SetBelt(right, on ? new Vector2Int(0, -1) : Vector2Int.zero);
            foreach (var g in _sideBelts) if (g != null) Destroy(g);
            _sideBelts.Clear();
            if (!on) return;
            int last = f.Cells.y - 1;
            _sideBelts.Add(Room.AddBeltRun(f.CellCenter(0, _bodyRows), f.CellCenter(0, last), new Vector2Int(0, 1), false, last - _bodyRows + 1));
            _sideBelts.Add(Room.AddBeltRun(f.CellCenter(f.Cells.x - 1, _bodyRows), f.CellCenter(f.Cells.x - 1, last), new Vector2Int(0, -1), false, last - _bodyRows + 1));
        }

        private void SetPlaten(bool open)
        {
            _platenOpen = open;
            var pl = _b["platen"];
            _platen.sprite = FloorSprites.Get(open ? pl.Str("open") : pl.Str("closed"));
            _platen.transform.localScale = Vector3.one * FloorSprites.ScaleFor(_platen.sprite, pl.Num("width", 2.6f));
            _platenGlow.color = new Color(1f, 0.8f, 0.55f, open ? 0.55f : 0f);
            if (open)
            {
                _platenUntil = Time.time + pl.Num("open_time", 2f);
                Game.Audio?.Play("boss_platen");
            }
        }

        // ------------------------------------------------------------------ attacks

        private (float yTop, float yBottom) Band(int b, int bands)
        {
            var f = Room.Frame;
            float rows = f.Cells.y - _bodyRows;
            float r0 = _bodyRows + rows * b / bands, r1 = _bodyRows + rows * (b + 1) / bands;
            float top = f.FloorRect.yMax - r0 * f.CellSize.y, bottom = f.FloorRect.yMax - r1 * f.CellSize.y;
            return (top, bottom);
        }

        private IEnumerator RollerPass(int count)
        {
            var rs = _b["roller"];
            int bands = rs.Int("bands", 3);
            var picks = new List<int>();
            for (int i = 0; i < bands; i++) picks.Add(i);
            // Prefer the band Elias stands in for one of the rollers.
            int eliasBand = -1;
            for (int i = 0; i < bands; i++)
            {
                var (top, bottom) = Band(i, bands);
                if (PlayerPos.y <= top && PlayerPos.y >= bottom) eliasBand = i;
            }
            var chosen = new List<int>();
            if (eliasBand >= 0 && Random.value < 0.7f) { chosen.Add(eliasBand); picks.Remove(eliasBand); }
            while (chosen.Count < count && picks.Count > 0)
            {
                int k = Random.Range(0, picks.Count);
                chosen.Add(picks[k]);
                picks.RemoveAt(k);
            }
            var f = Room.Frame;
            var shades = new List<SpriteRenderer>();
            foreach (int bnd in chosen)
            {
                var (top, bottom) = Band(bnd, bands);
                var shade = FloorSprites.Quad(Room.DecalRoot, "BandShade", new Color(0.02f, 0.02f, 0.03f, 0f),
                    new Vector2(f.FloorRect.width, top - bottom), FloorSprites.FloorDecalOrder + 650, FloorSprites.Unlit);
                shade.transform.position = new Vector2(f.FloorRect.center.x, (top + bottom) * 0.5f);
                shades.Add(shade);
            }
            float tel = rs.Num("telegraph", 1f), t = 0f;
            while (t < tel)
            {
                t += Time.deltaTime;
                foreach (var s in shades) s.color = new Color(0.02f, 0.02f, 0.03f, 0.45f * Mathf.Clamp01(t / tel));
                yield return null;
            }
            // Rollers cross the bands from left to right.
            var rollers = new List<(SpriteRenderer sr, float top, float bottom)>();
            var sprite = FloorSprites.Get(rs.Str("sprite", "F1_boss_roller"));
            foreach (int bnd in chosen)
            {
                var (top, bottom) = Band(bnd, bands);
                var sr = FloorSprites.Make(Room.ActorRoot, "Roller", sprite, 0f, 0);
                if (sprite != null)
                {
                    var sb = sprite.bounds.size;
                    float len = top - bottom;
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                    sr.transform.localScale = new Vector3(len * 1.15f / sb.x, 0.9f / sb.y, 1f);
                }
                sr.transform.position = new Vector2(f.FloorRect.xMin - 0.6f, (top + bottom) * 0.5f);
                rollers.Add((sr, top, bottom));
            }
            var snd = Game.Audio?.Play("boss_roller");
            float speed = rs.Num("speed", 13f);
            int dmg = rs.Int("damage", 2);
            float x = f.FloorRect.xMin - 0.6f;
            while (x < f.FloorRect.xMax + 0.6f)
            {
                x += speed * Time.deltaTime;
                foreach (var r in rollers)
                {
                    r.sr.transform.position = new Vector2(x, (r.top + r.bottom) * 0.5f);
                    r.sr.sortingOrder = FloorSprites.Order(r.bottom);
                    var p = PlayerPos;
                    if (Game.Player != null && Mathf.Abs(p.x - x) < 0.55f && p.y <= r.top + 0.1f && p.y >= r.bottom - 0.1f)
                        Game.Player.Hurt(dmg, Vector2.right, "boss");
                }
                if (Random.value < 0.4f) Game.Camera?.Shake(0.05f, 0.05f);
                yield return null;
            }
            foreach (var r in rollers) Destroy(r.sr.gameObject);
            foreach (var s in shades) Game.Fx?.FadeOut(s, 0f, 0.3f);
            if (snd != null) Game.Audio?.Stop(snd, 0.2f);
            if (Alive) SetPlaten(true);
        }

        private void Fan()
        {
            var fs = _b["fan"];
            int n = fs.Int("count", 5);
            float spread = fs.Num("spread", 60f), speed = fs.Num("speed", 5f);
            Vector2 aim = (PlayerPos - _mouth).normalized;
            for (int i = 0; i < n; i++)
            {
                float a = n > 1 ? -spread * 0.5f + spread * i / (n - 1) : 0f;
                Sheet(Rotate(aim, a), speed);
            }
            Game.Audio?.Play("boss_sheets");
        }

        private void Ring()
        {
            var rs = _b["ring"];
            int n = rs.Int("count", 12);
            float off = _ringOffset ? 180f / n : 0f;
            _ringOffset = !_ringOffset;
            for (int i = 0; i < n; i++)
            {
                Vector2 d = Rotate(Vector2.right, off + 360f / n * i);
                if (d.y > 0.35f) continue;   // not into the machine itself
                Sheet(d, rs.Num("speed", 4.2f));
            }
            Game.Audio?.Play("boss_sheets");
        }

        private void Sheet(Vector2 dir, float speed)
        {
            var s = Game.Shots.Spawn(Faction.Enemy, ShotKind.Sheet, _mouth + dir * 0.6f, dir * speed, 22f, 1f, 0.7f);
            s.Killer = "boss";
            s.Hp = _b["sheet"].Int("hp", 1);
        }

        private void SpitLetters()
        {
            Game.Audio?.Play("letters_clatter");
            EnemyFactory.CreateLetters(Room, _mouth + Vector2.down * 1.2f, _b["letters"].Int("count", 3), false, false);
        }

        private void PrintCopy()
        {
            Game.Audio?.Play("boss_print_copy");
            SetPlaten(true);
            var go = new GameObject("InkCopy");
            go.transform.SetParent(Room.ActorRoot, false);
            var c = go.AddComponent<InkCopy>();
            var spec = _b["copies"];
            c.Configure(spec.Num("delay", 3f));
            c.Setup("boss", Game.DB.Enemy("letters"), Room, _mouth + Vector2.down * 1.0f, false, spec.Num("hp", 8f), 1.05f);
            _copies.Add(c);
        }

        private IEnumerator Headline()
        {
            var hs = _b["headline"];
            Vector2 target = PlayerPos;
            float size = hs.Num("size", 2f);
            var shadow = FloorSprites.Make(Room.DecalRoot, "HeadlineShadow", UI.ProceduralSprites.SoftDot(64, 0.6f), 0f, FloorSprites.FloorDecalOrder + 800, FloorSprites.Unlit);
            shadow.transform.position = target;
            shadow.color = new Color(0f, 0f, 0f, 0.6f);
            var sb = shadow.sprite.bounds.size;
            float tel = hs.Num("telegraph", 1.5f), t = 0f;
            while (t < tel)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / tel);
                shadow.transform.localScale = new Vector3(size * k / sb.x, size * 0.75f * k / sb.y, 1f);
                yield return null;
            }
            // The paper falls.
            var paper = FloorSprites.Make(Room.ActorRoot, "Headline", FloorSprites.Get("F1_boss_newspaper"), size * 1.1f, FloorSprites.Order(target.y) + 20);
            float fall = 0.22f;
            t = 0f;
            Game.Audio?.Play("boss_headline");
            while (t < fall)
            {
                t += Time.deltaTime;
                paper.transform.position = target + Vector2.up * (6f * (1f - t / fall));
                yield return null;
            }
            paper.transform.position = target;
            paper.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));
            Game.Camera?.Shake(0.2f, 0.25f);
            Game.Fx?.Burst(target, FloorFx.Paper, 20, 4f, 0.13f, 0.5f);
            if (Game.Player != null && Vector2.Distance(PlayerPos, target) < size * 0.55f)
                Game.Player.Hurt(hs.Int("damage", 2), PlayerPos - target, "boss");
            Destroy(shadow.gameObject);
            Game.Fx?.FadeOut(paper, 0.8f, 0.6f);
        }

        // ------------------------------------------------------------------ death

        protected override void OnDeath(bool red)
        {
            _deadAt = Time.time;
            if (_whir != null) Game.Audio?.Stop(_whir, 1.5f);
            SetSideBelts(false);
            Game.Ctrl?.ShowBossBar(false, 0f);
            Game.Ctrl?.OnBossDefeated(this);
        }

        /// <summary>The body stays (a jammed machine) instead of vanishing.</summary>
        protected override bool DestroyOnDeath => false;

        public Vector2 Mouth => _mouth;
    }
}
