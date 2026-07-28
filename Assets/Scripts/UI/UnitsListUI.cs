using UnityEngine;
using UnityEngine.SceneManagement;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.UI
{
    /// <summary>
    /// Reference catalogue: every unit definition in a scrollable table, with
    /// both team icon variants, for quick stat lookup. Reached from Testing.
    /// </summary>
    public class UnitsListUI : MonoBehaviour
    {
        // (label, x offset, width) within the 1760-wide table area
        static readonly (string label, float x, float w)[] Columns =
        {
            ("ICON",      0,    110),
            ("NAME",      110,  300),
            ("CATEGORY",  410,  150),
            ("ATK",       560,  90),
            ("DEF",       650,  90),
            ("ARM",       740,  90),
            ("SPEED",     830,  120),
            ("MANPOWER",  950,  140),
            ("AMMO TYPE", 1090, 670),
        };

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            var canvas = UIFactory.CreateCanvas("UnitsListCanvas");

            var bg = UIFactory.CreatePanel(canvas.transform, "Background", GameConfig.UiBackground);
            UIFactory.Stretch(bg);

            var title = UIFactory.CreateText(canvas.transform, "UNITS LIST", 48,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -70), new Vector2(600, 70));

            var back = UIFactory.CreateButton(canvas.transform, "< BACK",
                () => SceneManager.LoadScene(GameConfig.SceneTesting),
                GameConfig.UiPanelLight, GameConfig.UiText, 24);
            UIFactory.Place((RectTransform)back.transform, new Vector2(1f, 1f), new Vector2(-80, -70), new Vector2(180, 60));

            var count = UIFactory.CreateText(canvas.transform, $"{UnitDatabase.All.Count} unit types",
                20, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(count.rectTransform, new Vector2(0f, 1f), new Vector2(80, -122), new Vector2(400, 30));

            // ---- table ----
            var table = UIFactory.CreateGroup(canvas.transform, "Table");
            UIFactory.Place(table, new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(1760, 760));

            var header = UIFactory.CreatePanel(table, "Header", GameConfig.UiPanel);
            header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1);
            header.pivot = new Vector2(0.5f, 1);
            header.offsetMin = new Vector2(0, -40); header.offsetMax = Vector2.zero;
            foreach (var col in Columns)
                Cell(header, col.label, col.x, col.w, 15, GameConfig.UiAccent, FontStyle.Bold);

            var scroll = UIFactory.CreateScrollView(table, out RectTransform content);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -44);

            foreach (var def in UnitDatabase.All) CreateRow(content, def);
        }

        void CreateRow(Transform parent, UnitDefinition def)
        {
            var row = UIFactory.CreatePanel(parent, "Row_" + def.id, new Color(1, 1, 1, 0.03f));
            row.sizeDelta = new Vector2(0, 58);

            PlaceIcon(row, "Friendly", def.id, Columns[0].x + 6);
            PlaceIcon(row, "Enemy", def.id, Columns[0].x + 58);

            Cell(row, def.name, Columns[1].x, Columns[1].w, 17, GameConfig.UiText);
            Cell(row, def.Category == UnitCategory.Drone ? "Drone" : "Core Ground",
                Columns[2].x, Columns[2].w, 14, GameConfig.UiTextDim);
            Cell(row, $"{def.attack:0}", Columns[3].x, Columns[3].w, 15, GameConfig.UiText);
            Cell(row, $"{def.defence:0}", Columns[4].x, Columns[4].w, 15, GameConfig.UiText);
            Cell(row, $"{def.armour:0}", Columns[5].x, Columns[5].w, 15, GameConfig.UiText);
            Cell(row, $"{def.speedKmh:0} km/h", Columns[6].x, Columns[6].w, 15, GameConfig.UiText);
            Cell(row, $"{def.manpower:n0}", Columns[7].x, Columns[7].w, 15, GameConfig.UiText);
            Cell(row, def.ammoType, Columns[8].x, Columns[8].w, 14, GameConfig.UiTextDim);
        }

        void PlaceIcon(Transform parent, string folder, string unitId, float x)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite == null) return;
            var img = UIFactory.CreateImage(parent, sprite, folder + "Icon");
            var rt = (RectTransform)img.transform;
            UIFactory.Place(rt, new Vector2(0f, 0.5f), new Vector2(x, 0), new Vector2(46, 46));
            rt.pivot = new Vector2(0, 0.5f);
        }

        void Cell(Transform parent, string text, float x, float w, int fontSize,
            Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var t = UIFactory.CreateText(parent, text, fontSize, color, TextAnchor.MiddleLeft, style);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(x + 4, 0), new Vector2(w - 8, 40));
            t.rectTransform.pivot = new Vector2(0, 0.5f);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                SceneManager.LoadScene(GameConfig.SceneTesting);
        }
    }
}
