using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Right-side panel showing full data for the clicked unit: identity,
    /// echelon, manpower, training, morale, combat power, ammunition, fuel,
    /// food and current status.
    /// </summary>
    public class UnitInfoPanel : MonoBehaviour
    {
        RectTransform _panel;
        Image _icon;
        Text _title, _body;
        UnitActor _current;

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitInfoPanel", GameConfig.UiPanel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-360, 0);
            _panel.offsetMax = new Vector2(0, -70);

            var close = UIFactory.CreateButton(_panel, "✕", Hide, GameConfig.UiPanelLight, null, 20);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-8, -8), new Vector2(40, 40));

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(_panel, false);
            _icon = iconGo.GetComponent<Image>();
            _icon.preserveAspect = true;
            UIFactory.Place((RectTransform)iconGo.transform, new Vector2(0.5f, 1f), new Vector2(0, -80), new Vector2(120, 120));

            _title = UIFactory.CreateText(_panel, "", 24, GameConfig.UiText, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -165), new Vector2(340, 60));

            _body = UIFactory.CreateText(_panel, "", 19, GameConfig.UiText, TextAnchor.UpperLeft);
            _body.rectTransform.anchorMin = new Vector2(0, 0);
            _body.rectTransform.anchorMax = new Vector2(1, 1);
            _body.rectTransform.offsetMin = new Vector2(24, 16);
            _body.rectTransform.offsetMax = new Vector2(-24, -210);

            Hide();
        }

        public void Show(UnitActor unit)
        {
            _current = unit;
            if (unit == null) { Hide(); return; }
            _panel.gameObject.SetActive(true);
            string folder = unit.State.TeamEnum == Team.User ? "Friendly" : "Enemy";
            _icon.sprite = UIFactory.LoadIconSprite(folder, unit.Def.id);
            Refresh();
        }

        public void Hide()
        {
            _current = null;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_current != null && Time.frameCount % 30 == 0) Refresh();
        }

        void Refresh()
        {
            if (_current == null) return;
            var s = _current.State;
            var d = _current.Def;
            int manpower = Mathf.RoundToInt(d.manpower *
                EchelonInfo.ManpowerMultiplier(s.EchelonEnum) * Mathf.Clamp01(s.strength));

            _title.text = string.IsNullOrEmpty(s.customName) ? d.name : s.customName;
            _body.text =
                $"<color=#9aa4b0>Type</color>  {d.name} ({d.category})\n" +
                $"<color=#9aa4b0>Team</color>  {(s.TeamEnum == Team.User ? "User (Blue)" : "Enemy (Red)")}\n" +
                $"<color=#9aa4b0>Affiliation</color>  {s.affiliation}\n" +
                $"<color=#9aa4b0>Echelon</color>  {s.echelon}  {EchelonInfo.Indicator(s.EchelonEnum)}\n" +
                $"<color=#9aa4b0>Status</color>  {s.status}\n" +
                $"\n" +
                $"<color=#d9a521>STRENGTH</color>\n" +
                $"  Strength   {s.strength * 100f:0}%\n" +
                $"  Manpower   {manpower:n0}\n" +
                $"  Training   {d.training:0}/100\n" +
                $"  Morale     {s.morale:0}/100\n" +
                $"  Organisation {s.organisation:0}/100\n" +
                $"  Combat power {_current.CurrentPower():n0}\n" +
                $"\n" +
                $"<color=#d9a521>COMBAT</color>\n" +
                $"  Attack {d.attack:0}   Hard atk {d.hardAttack:0}\n" +
                $"  Defence {d.defence:0}   Armour {d.armour:0}\n" +
                $"  Anti-air {d.antiAir:0}\n" +
                $"  Weapon range {d.weaponRangeKm:0.#} km\n" +
                $"  View range {d.viewRangeKm:0.#} km\n" +
                $"  Speed {d.speedKmh:0} km/h\n" +
                $"\n" +
                $"<color=#d9a521>SUSTAINMENT</color>\n" +
                $"  Ammo type  {d.ammoType}\n" +
                $"  Ammo stock {s.ammo:n0} / {d.ammoStock:n0}\n" +
                $"  Fuel       {s.fuel:n0} / {d.fuelStock:n0} L\n" +
                $"  Food       {s.foodDays} days\n" +
                $"\n" +
                $"<color=#9aa4b0>Position</color>\n" +
                $"  {s.latitude:0.0000}°N  {s.longitude:0.0000}°E";
        }
    }
}
