using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Models;

namespace IronMeridian.UI
{
    /// <summary>
    /// 3D MODELS — every model in <see cref="UnitModelLibrary"/>, shown in 3D
    /// with where it came from and what uses it.
    ///
    /// **It reports the truth, not the catalogue.** A row says whether the
    /// prefab is actually installed, whether the model is built procedurally, or
    /// whether nothing resolves — because the library naming a path is not the
    /// same as the path existing, and an art pack that was never imported is
    /// exactly what this screen is for. The answer used to require deploying a
    /// unit on the map and flying the camera to it.
    ///
    /// **The preview is the same rig the rest of the game uses**
    /// (<see cref="ModelPreview"/>): a parked camera on a render texture, drag
    /// to orbit, wheel to zoom. Models resolve through the library, never
    /// through a <c>Resources.Load</c> at this call site (golden rule 10).
    ///
    /// Reached from DEVELOPMENT. See docs/09-3D-MODELS.md.
    /// </summary>
    public class ModelListUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        const float ScreenMargin = 60f;
        const float ListWidth = 520f;
        const float ContentTop = 190f, ContentBottom = 60f;
        const float RowHeight = 76f, RowGap = 6f;
        /// <summary>Height of the detail block under the preview.</summary>
        const float DetailHeight = 150f;

        /// <summary>What a row can say about a model, in the order it is worth knowing.</summary>
        enum State { Installed, Procedural, Missing }

        class Entry
        {
            public string Id;
            public UnitModelDef Def;
            public State State;
            /// <summary>Unit types that resolve to this model, for the detail block.</summary>
            public List<string> Users = new List<string>();
            /// <summary>
            /// Things that are **not** unit types and wear this model anyway:
            /// logistic installations, and the air-dropped bundle.
            ///
            /// Kept apart from <see cref="Users"/> rather than merged into it,
            /// because the two answer different questions. "Which formations
            /// wear this" is what a designer checking a model assignment asks;
            /// "what else on the map is this" is what somebody wondering why a
            /// model with no unit types is in the library asks — and that line
            /// used to read "not yet assigned", which was wrong for six of them.
            /// </summary>
            public List<string> OtherUsers = new List<string>();

            /// <summary>Everything that draws this model, unit types first.</summary>
            public int UserCount => Users.Count + OtherUsers.Count;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly Dictionary<string, Image> _rowFills = new Dictionary<string, Image>();
        Entry _selected;

        Canvas _canvas;
        RectTransform _listContent;
        ModelPreview _preview;
        Text _detailName, _detailSource, _detailPath, _detailUsers;

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Play(MusicTrack.MenuTheme);

            _canvas = UIFactory.CreateCanvas("ModelListCanvas");
            UIFactory.CreateScreenBackground(_canvas.transform, BackgroundId.Interior,
                BackgroundCatalog.DenseScreenScrim);

            LoadEntries();
            BuildHeader();
            BuildList();
            BuildPreview();

            if (_entries.Count > 0) Select(_entries[0]);
        }

        /// <summary>
        /// Reads the library, resolves each model the way the game would, and
        /// works out which unit types land on it.
        /// </summary>
        void LoadEntries()
        {
            foreach (var pair in UnitModelLibrary.Entries)
            {
                var def = pair.Value;
                var entry = new Entry { Id = pair.Key, Def = def };

                entry.State = def.IsProcedural ? State.Procedural
                    : Resources.Load<GameObject>(def.resourcePath) != null ? State.Installed
                    : State.Missing;

                _entries.Add(entry);
            }

            // Which units wear which model, resolved through the same call the
            // map makes, so this cannot disagree with what is spawned.
            foreach (var unit in UnitDatabase.All)
            {
                var resolved = UnitModelLibrary.Resolve(unit);
                if (resolved == null) continue;
                foreach (var e in _entries)
                    if (e.Def == resolved) { e.Users.Add(unit.name); break; }
            }

            // The rear area. An installation is not a unit type and never
            // appears in the catalogue above, so without this its model would be
            // reported as belonging to nothing — see docs/26-LOGISTICS.md §4b.
            foreach (var site in LogisticsCatalog.All)
            {
                if (string.IsNullOrEmpty(site.modelId)) continue;
                foreach (var e in _entries)
                    if (e.Id == site.modelId) { e.OtherUsers.Add(site.name); break; }
            }

            // The one load that is dropped rather than deployed. Named here for
            // the same reason: nothing in units.json resolves to it.
            foreach (var e in _entries)
                if (e.Id == UnitModelLibrary.SupplyBundle)
                    e.OtherUsers.Add("AIR-DROPPED CACHE");

            // Installed first, then procedural, then missing — the order somebody
            // opening this screen is looking for.
            _entries.Sort((a, b) =>
            {
                int s = a.State.CompareTo(b.State);
                return s != 0 ? s : string.Compare(a.Id, b.Id, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        // ------------------------------------------------------------- frame

        void BuildHeader()
        {
            var title = UIFactory.CreateText(_canvas.transform, "3D MODELS", 52, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f),
                new Vector2(ScreenMargin, -70), new Vector2(700, 60));

            int installed = 0, procedural = 0, missing = 0;
            foreach (var e in _entries)
            {
                if (e.State == State.Installed) installed++;
                else if (e.State == State.Procedural) procedural++;
                else missing++;
            }

            var sub = UIFactory.CreateText(_canvas.transform,
                $"{_entries.Count} registered · {installed} installed · {procedural} procedural · " +
                $"{missing} not installed. Drag to orbit, wheel to zoom.",
                18, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f),
                new Vector2(ScreenMargin, -122), new Vector2(1300, 26));

            UIFactory.CreateBackButton(_canvas.transform, "BACK TO DEVELOPMENT", Leave);
        }

        void BuildList()
        {
            var scroll = UIFactory.CreateScrollView(_canvas.transform, out _listContent,
                withScrollbar: true, autoHideScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(ScreenMargin, ContentBottom);
            rt.offsetMax = new Vector2(ScreenMargin + ListWidth, -ContentTop);

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = RowGap;
            layout.padding = new RectOffset(0, 0, 0, 12);

            foreach (var entry in _entries) AddRow(entry);
        }

        void AddRow(Entry entry)
        {
            var e = entry;

            var frame = UIFactory.CreateBorderedPanel(_listContent, "Row_" + e.Id,
                UiTheme.Surface, UiTheme.Border);
            frame.sizeDelta = new Vector2(0, RowHeight);

            var btn = UIFactory.CreateButton(frame, "", () => Select(e),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var lamp = UIFactory.CreatePanel(frame, "Lamp", StateColour(e.State));
            UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(14, 0), new Vector2(7, 7));
            lamp.GetComponent<Image>().raycastTarget = false;

            var name = UIFactory.CreateText(frame, e.Id, 19, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(name.rectTransform, 34f, 12f, ListWidth - 170f, 24f);
            UIFactory.Fit(name, 12);

            string under = e.UserCount == 0
                ? e.Def.sourceAsset
                : $"{e.Def.sourceAsset}  ·  {e.UserCount} user(s)";
            var detail = UIFactory.CreateText(frame, under, 13, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(detail.rectTransform, 34f, 38f, ListWidth - 170f, 20f);
            UIFactory.Fit(detail, 9);

            var state = UIFactory.CreateText(frame, StateText(e.State), UiTheme.FontLabel,
                StateColour(e.State), TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(state.rectTransform, new Vector2(1f, 0.5f), new Vector2(-16, 0),
                new Vector2(130, 18));

            _rowFills[e.Id] = frame.Find("Fill").GetComponent<Image>();
        }

        static string StateText(State s) => s switch
        {
            State.Installed => "INSTALLED",
            State.Procedural => "PROCEDURAL",
            _ => "NOT INSTALLED"
        };

        static Color StateColour(State s) => s switch
        {
            State.Installed => UiTheme.Success,
            State.Procedural => UiTheme.Accent,
            _ => UiTheme.Danger
        };

        // ----------------------------------------------------------- preview

        void BuildPreview()
        {
            var frame = UIFactory.CreateBorderedPanel(_canvas.transform, "Preview",
                UiTheme.Panel, UiTheme.Border);
            frame.anchorMin = new Vector2(0, 0); frame.anchorMax = new Vector2(1, 1);
            frame.offsetMin = new Vector2(ScreenMargin + ListWidth + 28f, ContentBottom);
            frame.offsetMax = new Vector2(-ScreenMargin, -ContentTop);

            var stage = UIFactory.CreateGroup(frame, "Stage");
            stage.anchorMin = new Vector2(0, 0); stage.anchorMax = new Vector2(1, 1);
            stage.offsetMin = new Vector2(12, DetailHeight);
            stage.offsetMax = new Vector2(-12, -12);

            _preview = ModelPreview.Create(stage, new Vector2(900, 520));
            UIFactory.Stretch((RectTransform)_preview.transform);

            _detailName = UIFactory.CreateText(frame, "", 22, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_detailName.rectTransform, new Vector2(0f, 0f),
                new Vector2(16, DetailHeight - 28f), new Vector2(900, 24));
            UIFactory.Fit(_detailName, 13);

            _detailSource = UIFactory.CreateText(frame, "", 15, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_detailSource.rectTransform, new Vector2(0f, 0f),
                new Vector2(16, DetailHeight - 54f), new Vector2(900, 20));
            UIFactory.Fit(_detailSource, 10);

            _detailPath = UIFactory.CreateText(frame, "", 13, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_detailPath.rectTransform, new Vector2(0f, 0f),
                new Vector2(16, DetailHeight - 78f), new Vector2(900, 18));
            UIFactory.Fit(_detailPath, 9);

            _detailUsers = UIFactory.CreateText(frame, "", 13, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.Place(_detailUsers.rectTransform, new Vector2(0f, 0f),
                new Vector2(16, 12f), new Vector2(1100, 56));
        }

        void Select(Entry entry)
        {
            _selected = entry;

            foreach (var pair in _rowFills)
                pair.Value.color = pair.Key == entry.Id ? UiTheme.AccentWash : UiTheme.Surface;

            _preview.Show(entry.Def,
                $"'{entry.Id}' has no prefab installed.\n" +
                "Run Tools → Iron Meridian → Install Unit Models, or see docs/09-3D-MODELS.md.");

            _detailName.text = entry.Id;
            _detailSource.text = string.IsNullOrEmpty(entry.Def.sourceAsset)
                ? "Built in code" : "Source: " + entry.Def.sourceAsset;

            _detailPath.text = entry.Def.IsProcedural
                ? $"Procedural — ProceduralModels.{entry.Def.proceduralId}"
                : $"Resources/{entry.Def.resourcePath}" +
                  (entry.Def.animated ? $"   ·   animated, idle '{entry.Def.idleClip}'" : "   ·   static mesh");

            _detailUsers.text = DetailUsers(entry);
        }

        /// <summary>
        /// Who draws this model, in one line.
        ///
        /// Unit types and everything else are listed separately and labelled,
        /// because a model worn by no formation is not the same thing as a model
        /// worn by nothing: six of them are logistic installations, one is an
        /// air-dropped cache, and several belong to a weapon catalogue rather
        /// than to the unit list.
        /// </summary>
        static string DetailUsers(Entry entry)
        {
            var parts = new List<string>();
            if (entry.Users.Count > 0)
                parts.Add("Worn by " + string.Join(", ", entry.Users));
            if (entry.OtherUsers.Count > 0)
                parts.Add("Also drawn for " + string.Join(", ", entry.OtherUsers));

            return parts.Count == 0
                ? "No unit type resolves to this model — it is flown by a weapon catalogue or not yet assigned."
                : string.Join("   ·   ", parts);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Leave();
        }

        void Leave() => SceneManager.LoadScene(GameConfig.SceneTesting);
    }
}
