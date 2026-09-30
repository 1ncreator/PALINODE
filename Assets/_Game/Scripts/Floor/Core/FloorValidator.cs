using System.Collections.Generic;
using Palinode.Core;
using Palinode.Localization;

namespace Palinode.Floor
{
    /// <summary>Checks Floor I data at build time and in tests: templates, sprites, audio manifest, texts.</summary>
    public static class FloorValidator
    {
        /// <summary>Localization keys the floor uses (besides per-item / per-enemy keys built from ids).</summary>
        public static readonly string[] Keys =
        {
            "F1_HINT_MOVE", "F1_HINT_SHOOT", "F1_HINT_ACTIVE", "F1_HINT_INTERACT", "F1_HINT_MAP",
            "F1_GIRL_5", "F1_GIRL_10", "F1_GIRL_20", "F1_DRAWING_CAPTION", "F1_SECRET_NOTE", "F1_NOTE_TITLE",
            "F1_PAGE_TITLE", "F1_PAGE_1", "F1_WITNESS", "F1_BOSS_NAME", "F1_NEWS_HEADLINE", "F1_NEWS_NAME",
            "F1_PROMPT_TAKE", "F1_PROMPT_BUY", "F1_PROMPT_POOR", "F1_PROMPT_DESCEND",
            "F1_ARCH_TOOK", "F1_ARCH_LEFT", "F1_ARCH_10", "F1_ARCH_5", "F1_ARCH_FIRST", "F1_SPEAKER_ARCHIVIST",
            "F1_NEXT_CHAPTER", "F1_NEXT_CHAPTER_SUB", "F1_TO_BE_CONTINUED",
            "F1_DEATH_LINE", "F1_REVISION", "F1_ENEMY_unknown", "F1_ENEMY_boss",
            "F1_PAUSE", "F1_RESUME", "F1_QUIT_MENU", "F1_PAGES", "F1_MAP_TITLE", "F1_MAP_CHAPTER",
            "F1_ITEM_pencil", "F1_ITEM_pencil_DESC"
        };

        public static IEnumerable<string> AllKeys()
        {
            foreach (var k in Keys) yield return k;
            foreach (var id in FloorDB.ItemIds)
            {
                yield return "F1_ITEM_" + id;
                yield return "F1_ITEM_" + id + "_DESC";
            }
            foreach (var id in FloorDB.EnemyIds) yield return "F1_ENEMY_" + id;
        }

        public static List<string> Validate(GameConfig config, LocalizationTable table)
        {
            var errors = new List<string>();
            FloorDB db;
            try
            {
                db = FloorDB.FromConfig(config);
            }
            catch (System.Exception e)
            {
                errors.Add("Floor I data does not parse: " + e.Message);
                return errors;
            }
            if (db.Normal.Count < 12) errors.Add($"Floor I needs at least 12 combat templates, found {db.Normal.Count}.");
            foreach (var t in db.Normal)
            {
                if (t.Width != RoomTemplate.CellCols || t.Height != RoomTemplate.CellRows) errors.Add($"Template '{t.Name}' must be 13×7.");
                else if (!t.Validate(out string err)) errors.Add(err);
            }
            foreach (var t in db.Big)
                if (!t.Validate(out string err)) errors.Add(err);

            foreach (var kv in db.Backgrounds)
                if (config != null && !config.HasSprite(kv.Key)) errors.Add("Background sprite not registered: " + kv.Key);

            // Sprites referenced by the data.
            var sprites = new List<string>
            {
                "F1_door_closed", "F1_door_open", "F1_door_boss_closed", "F1_door_boss_open", "F1_wall_secret", "F1_wall_secret_broken",
                "F1_press", "F1_stack_1", "F1_stack_2", "F1_stack_3", "F1_typecase", "F1_drying", "F1_puddle", "F1_belt", "F1_belt_end",
                "F1_desk", "F1_pedestal", "F1_trapdoor_open", "F1_shoot_down", "F1_shoot_up", "F1_shoot_left", "F1_shoot_right",
                "F1_elias_fallen", "F1_elias_pickup", "F1_bug_a", "F1_bug_b", "F1_blot_idle", "F1_blot_squash", "F1_blot_jump", "F1_blot_small",
                "F1_comp_down", "F1_comp_right", "F1_comp_windup_down", "F1_comp_windup_right", "F1_headlights_full", "F1_headlights_ghost",
                "F1_ink_drop", "F1_splat_1", "F1_splat_2", "F1_splat_3", "F1_splat_4", "F1_sheet_proj",
                "F1_boss_body", "F1_boss_roller", "F1_boss_platen_closed", "F1_boss_platen_open", "F1_boss_flywheel", "F1_boss_newspaper",
                "F1_drawing_1", "F1_drawing_2", "F1_drawing_3", "F1_binder_a", "F1_binder_b", "F1_archivist_b",
                "F1_pickup_drop", "F1_pickup_inkwell", "F1_pickup_sheet", "F1_pickup_page", "F1_hp_full", "F1_hp_half", "F1_hp_empty",
                "F1_map_paper", "F1_icon_lamp", "F1_icon_musicbox", "F1_icon_gear", "F1_icon_star", "F1_icon_needle", "F1_icon_trapdoor",
                "F1_bossbar", "PR_redpencil", "PR_sheet", "PR_wallclock_face", "PR_wallclock_hour", "PR_wallclock_minute",
                "G_walk_down_01", "G_walk_up_01", "G_walk_right_01"
            };
            foreach (char c in "MARELISTON") sprites.Add("F1_letter_" + c);
            foreach (var id in FloorDB.ItemIds) sprites.Add(db.Item(id).Str("icon"));
            if (config != null)
                foreach (var s in sprites)
                    if (!config.HasSprite(s)) errors.Add("Floor I sprite missing: " + s);

            // Audio: every file exists, or is marked "expected" with a fallback.
            foreach (var (file, expected, fallback) in FloorAudio.Missing(db.Audio, config))
                if (!expected || fallback == "none") errors.Add($"Audio '{file}' is missing and not marked as expected with a fallback.");

            // Texts.
            if (table != null)
                foreach (var k in AllKeys())
                    if (!table.HasBoth(k)) errors.Add("Localization key missing (EN and RU): " + k);

            // Chances in data.
            var d = db.Data;
            if (!d["redPencil"].Has("chance")) errors.Add("floor1.json: redPencil.chance missing.");
            if (!d["generation"].Has("shopChance")) errors.Add("floor1.json: generation.shopChance missing.");
            return errors;
        }
    }
}
