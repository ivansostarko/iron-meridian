using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.UI
{
    /// <summary>
    /// Left-side unit palette. Pick team (Friendly/Enemy), affiliation and
    /// echelon, then DRAG a unit card onto the terrain to deploy it.
    /// </summary>
    public class UnitPaletteUI : MonoBehaviour
    {
        public System.Action<UnitDefinition, Team, Affiliation, Echelon, Vector2> DropRequested;

        Team _team = Team.User;
        Affiliation _affiliation = Affiliation.Friendly;
        Echelon _echelon = Echelon.Battalion;

        RectTransform _listContent;
        Button _blueTab, _redTab;
        Image _dragGhost;
        Canvas _canvas;
        UnitDefinition _dragging;

        public void Build(Canvas canvas)
        {
            _canvas = canvas;
            var panel = UIFactory.CreatePanel(canvas.transform, "UnitPalette", GameConfig.UiPanel);
            panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.offsetMin = new Vector2(0, 0);
            panel.offsetMax = new Vector2(330, -70);

            var title = UIFactory.CreateText(panel, "ORDER OF BATTLE", 24,
                GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -28), new Vector2(320, 40));

            // Team tabs
            _blueTab = UIFactory.CreateButton(panel, "FRIENDLY", () => SetTeam(Team.User),
                GameConfig.BlueTeam, Color.white, 20);
            UIFactory.Place((RectTransform)_blueTab.transform, new Vector2(0f, 1f), new Vector2(10, -60), new Vector2(150, 46));

            _redTab = UIFactory.CreateButton(panel, "ENEMY", () => SetTeam(Team.Enemy),
                GameConfig.UiPanelLight, Color.white, 20);
            UIFactory.Place((RectTransform)_redTab.transform, new Vector2(0f, 1f), new Vector2(170, -60), new Vector2(150, 46));

            // Affiliation + echelon dropdowns
            var affLabel = UIFactory.CreateText(panel, "Affiliation", 18, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(affLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12, -118), new Vector2(120, 30));
            var affDd = UIFactory.CreateDropdown(panel,
                new List<string> { "Friendly", "Hostile", "Neutral", "Unknown" }, 0,
                i => _affiliation = (Affiliation)i);
            UIFactory.Place((RectTransform)affDd.transform, new Vector2(0f, 1f), new Vector2(120, -112), new Vector2(198, 42));

            var echLabel = UIFactory.CreateText(panel, "Echelon", 18, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(echLabel.rectTransform, new Vector2(0f, 1f), new Vector2(12, -166), new Vector2(120, 30));
            var echNames = new List<string>(System.Enum.GetNames(typeof(Echelon)));
            var echDd = UIFactory.CreateDropdown(panel, echNames, (int)Echelon.Battalion,
                i => _echelon = (Echelon)i);
            UIFactory.Place((RectTransform)echDd.transform, new Vector2(0f, 1f), new Vector2(120, -160), new Vector2(198, 42));

            var hint = UIFactory.CreateText(panel, "Drag a unit onto the map to deploy",
                16, GameConfig.UiTextDim);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -204), new Vector2(320, 26));

            // Scrollable unit list
            var scroll = UIFactory.CreateScrollView(panel, out _listContent);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(6, 8);
            srt.offsetMax = new Vector2(-6, -222);

            // Drag ghost (top-most)
            var ghostGo = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            ghostGo.transform.SetParent(canvas.transform, false);
            _dragGhost = ghostGo.GetComponent<Image>();
            _dragGhost.raycastTarget = false;
            _dragGhost.preserveAspect = true;
            ((RectTransform)ghostGo.transform).sizeDelta = new Vector2(90, 90);
            ghostGo.SetActive(false);

            Populate();
        }

        void SetTeam(Team team)
        {
            _team = team;
            _affiliation = team == Team.User ? Affiliation.Friendly : Affiliation.Hostile;
            _blueTab.image.color = team == Team.User ? GameConfig.BlueTeam : GameConfig.UiPanelLight;
            _redTab.image.color = team == Team.Enemy ? GameConfig.RedTeam : GameConfig.UiPanelLight;
            Populate();
        }

        void Populate()
        {
            foreach (Transform child in _listContent) Destroy(child.gameObject);
            string folder = _team == Team.User ? "Friendly" : "Enemy";

            string lastCategory = null;
            foreach (var def in UnitDatabase.All)
            {
                if (def.category != lastCategory)
                {
                    lastCategory = def.category;
                    var header = UIFactory.CreateText(_listContent,
                        def.Category == UnitCategory.Drone ? "— DRONE UNITS —" : "— CORE GROUND UNITS —",
                        17, GameConfig.UiAccent);
                    var hrt = header.rectTransform;
                    hrt.sizeDelta = new Vector2(0, 30);
                }
                CreateCard(def, folder);
            }
        }

        void CreateCard(UnitDefinition def, string folder)
        {
            var card = UIFactory.CreatePanel(_listContent, "Card_" + def.id, GameConfig.UiPanelLight);
            card.sizeDelta = new Vector2(0, 64);

            var sprite = UIFactory.LoadIconSprite(folder, def.id);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(card, sprite, "Icon");
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(56, 56));
                ((RectTransform)icon.transform).pivot = new Vector2(0, 0.5f);
            }

            var name = UIFactory.CreateText(card, def.name, 19, null, TextAnchor.MiddleLeft);
            UIFactory.Stretch(name.rectTransform);
            name.rectTransform.offsetMin = new Vector2(74, 20);

            var stats = UIFactory.CreateText(card,
                $"ATK {def.attack:0}  DEF {def.defence:0}  SPD {def.speedKmh:0} km/h",
                14, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Stretch(stats.rectTransform);
            stats.rectTransform.offsetMin = new Vector2(74, 0);
            stats.rectTransform.offsetMax = new Vector2(0, -28);

            // Drag handling
            var trigger = card.gameObject.AddComponent<EventTrigger>();
            AddEvent(trigger, EventTriggerType.BeginDrag, e => BeginDrag(def, sprite));
            AddEvent(trigger, EventTriggerType.Drag, e => Drag((PointerEventData)e));
            AddEvent(trigger, EventTriggerType.EndDrag, e => EndDrag((PointerEventData)e));
        }

        static void AddEvent(EventTrigger t, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> cb)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(cb);
            t.triggers.Add(entry);
        }

        void BeginDrag(UnitDefinition def, Sprite sprite)
        {
            _dragging = def;
            _dragGhost.sprite = sprite;
            _dragGhost.gameObject.SetActive(sprite != null);
        }

        void Drag(PointerEventData e)
        {
            if (_dragging == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, e.position, _canvas.worldCamera, out Vector2 local);
            ((RectTransform)_dragGhost.transform).anchoredPosition = local;
        }

        void EndDrag(PointerEventData e)
        {
            _dragGhost.gameObject.SetActive(false);
            if (_dragging == null) return;
            DropRequested?.Invoke(_dragging, _team, _affiliation, _echelon, e.position);
            _dragging = null;
        }
    }
}
