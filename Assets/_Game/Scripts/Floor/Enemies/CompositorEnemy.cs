using System.Collections;
using UnityEngine;

namespace Palinode.Floor
{
    /// <summary>
    /// Compositor: keeps 4–6 cells away, winds up (0.7 s, a thin line drawn on the floor toward Elias) and sets a
    /// "line" — five type letters fired one after another in a straight row; reloads for 2.5 s.
    /// </summary>
    public sealed class CompositorEnemy : EnemyBase
    {
        private Sprite _down, _right, _wDown, _wRight;
        private float _reload;
        private bool _winding;
        private float _strafe = 1f, _strafeT;
        private const string Type = "PALINODEMARAELIAS";

        protected override void OnSpawned()
        {
            _down = FloorSprites.Get("F1_comp_down");
            _right = FloorSprites.Get("F1_comp_right");
            _wDown = FloorSprites.Get("F1_comp_windup_down");
            _wRight = FloorSprites.Get("F1_comp_windup_right");
            SetSprite(_down);
            _reload = Spec.Num("reload", 2.5f) * Random.Range(0.4f, 0.8f);
        }

        protected override void Tick(float dt)
        {
            if (_winding) { Desired = Vector2.zero; return; }
            float speed = Spec.Num("speed", 2f);
            float d = DistToPlayer();
            Vector2 to = DirToPlayer();
            if (d < Spec.Num("keepMin", 4f)) Desired = Steer(Feet - to * 2f) * speed;
            else if (d > Spec.Num("keepMax", 6f)) Desired = Steer(PlayerPos) * speed;
            else
            {
                _strafeT -= dt;
                if (_strafeT <= 0f) { _strafeT = Random.Range(0.8f, 1.6f); _strafe = -_strafe; }
                Desired = new Vector2(-to.y, to.x) * _strafe * speed * 0.5f;
            }
            Face(to, false);
            _reload -= dt;
            if (_reload <= 0f) StartCoroutine(Attack());
        }

        private void Face(Vector2 dir, bool windup)
        {
            bool side = Mathf.Abs(dir.x) > Mathf.Abs(dir.y);
            var s = side ? (windup ? _wRight : _right) : (windup ? _wDown : _down);
            SetSprite(s, side && dir.x < 0f);
        }

        private IEnumerator Attack()
        {
            _winding = true;
            Vector2 dir = DirToPlayer();
            Face(dir, true);
            Game.Audio?.Play("compositor_windup");
            // Telegraph: a thin line on the floor along the direction of the coming line.
            float len = 7f;
            var line = FloorSprites.Quad(Room.DecalRoot, "Telegraph", new Color(0.05f, 0.04f, 0.05f, 0.0f), new Vector2(len, 0.05f),
                FloorSprites.FloorDecalOrder + 600, FloorSprites.Unlit);
            line.transform.position = Feet + dir * (len * 0.5f + 0.3f);
            line.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            float tel = Spec.Num("telegraph", 0.7f), t = 0f;
            while (t < tel)
            {
                if (!Alive) { if (line != null) Destroy(line.gameObject); yield break; }
                t += Time.deltaTime;
                line.color = new Color(0.05f, 0.04f, 0.05f, Mathf.Clamp01(t / tel) * 0.7f);
                yield return null;
            }
            Destroy(line.gameObject);
            int n = Spec.Int("line", 5);
            float gap = Spec.Num("lineGap", 0.12f), speed = Spec.Num("bulletSpeed", 6f);
            int start = Random.Range(0, Type.Length);
            for (int i = 0; i < n && Alive; i++)
            {
                FireShot(ShotKind.Letter, dir, speed, 12f, Type[(start + i) % Type.Length], 0.9f);
                Game.Audio?.Play("line_shot", 0.8f);
                yield return new WaitForSeconds(gap);
            }
            _reload = Spec.Num("reload", 2.5f);
            _winding = false;
            if (Alive) Face(dir, false);
        }
    }
}
