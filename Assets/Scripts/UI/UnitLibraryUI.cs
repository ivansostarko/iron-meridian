using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Models;

namespace IronMeridian.UI
{
    /// <summary>
    /// UNITS — the encyclopaedia, reached from EXTRAS.
    ///
    /// **Two pages, one scene.** An arm-of-service board, then that arm's
    /// formations with filters, full data and a 3D model. Moving between them is
    /// instant, because a scene load between two pages of a browse is a loading
    /// screen in the middle of reading — the same argument
    /// <see cref="SinglePlayerUI"/> makes for the campaign boards.
    ///
    /// **This is not the DEVELOPMENT units screen.** That one is a data table
    /// you can edit — every field of six catalogues, sortable, tunable, written
    /// to your own file. This is for reading: pick an arm, see what it fields,
    /// look at the model. A player wanting to know what a unit *is* should not
    /// have to walk through a screen whose first affordance is EDIT, and a
    /// designer tuning armour values should not have to browse an encyclopaedia
    /// to get at them.
    ///
    /// The arms shown are the five the request named — infantry, artillery,
    /// armour, air and navy — plus the rest of the catalogue under MORE, because
    /// a browser that silently omits 60 of 117 unit types is worse than one that
    /// admits to a sixth category.
    ///
    /// **And a seventh board that is not units at all.** LOGISTICS lists the six
    /// installations a scenario can be given. They are not formations — they do
    /// not fight, move or die, and they have none of a unit's numbers — so the
    /// list page carries a second column set and a second detail block for them.
    /// They are here because a reader asking what a side fields is asking about
    /// the rear area too, and a book covering 117 formation types that omits the
    /// six things keeping them supplied is a book with a hole in it. See
    /// docs/26-LOGISTICS.md.
    /// </summary>
    public class UnitLibraryUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        const float ScreenMargin = 60f, ColumnGap = 24f;
        const float PanelW = 560f, PanelY = -170f, BottomMargin = 44f;
        const float ToolbarY = -170f, HintY = -228f, TableY = -252f;
        const float PanelInset = 46f;
        const float RowPad = 8f;

        /// <summary>
        /// Air between the board's title and the line that counts what is on it.
        /// Named rather than folded into the offset so it stays visible as a
        /// deliberate piece of spacing.
        /// </summary>
        const float SubtitleTopPad = 15f;

        /// <summary>One board entry: a caption and the branches behind it.</summary>
        class Arm
        {
            public string title;
            public string blurb;
            /// <summary>
            /// The board glyph, **as a factory rather than a sprite**.
            ///
            /// <see cref="Arms"/> is a static field on a MonoBehaviour type, and
            /// Unity runs those initialisers during its serialization pass —
            /// where creating a texture is illegal and throws
            /// <c>SupportsTextureFormatNative is not allowed to be called from a
            /// MonoBehaviour constructor</c>, failing the build. Every
            /// <see cref="UiIcons"/> sprite is drawn procedurally on first
            /// access, so naming one here built it at exactly the wrong moment.
            /// A delegate stores nothing until the board is drawn.
            /// </summary>
            public System.Func<Sprite> glyph;
            public Color tone;
            public UnitBranch[] branches;

            /// <summary>
            /// True for the one board entry that is **not** a set of unit types.
            ///
            /// A logistic installation is not a formation — it does not fight,
            /// move or die, and it has none of a unit's numbers — so it cannot
            /// be a branch filter over <see cref="UnitDatabase"/>. It is on this
            /// board anyway because a reader asking "what does this side field"
            /// is asking about the rear area too, and an encyclopaedia that
            /// covers 117 formation types and omits the six things that keep
            /// them supplied is an encyclopaedia with a hole in it. See
            /// docs/26-LOGISTICS.md.
            /// </summary>
            public bool installations;
        }

        static readonly Arm[] Arms =
        {
            new Arm
            {
                title = "INFANTRY", glyph = () => UiIcons.Person,
                blurb = "Dismounted manoeuvre formations. They fight on their feet and hold the worst ground.",
                tone = new Color(0.16f, 0.26f, 0.18f),
                branches = new[] { UnitBranch.Infantry }
            },
            new Arm
            {
                title = "ARTILLERY", glyph = () => UiIcons.Artillery,
                blurb = "Everything that shoots at what it cannot see, and the observers that aim it.",
                tone = new Color(0.30f, 0.20f, 0.12f),
                branches = new[] { UnitBranch.Artillery }
            },
            new Arm
            {
                title = "ARMOUR", glyph = () => UiIcons.Equipment,
                blurb = "Tanks, cavalry and the anti-armour arm that exists to kill them — with the " +
                        "mechanised infantry that rides alongside.",
                tone = new Color(0.22f, 0.20f, 0.14f),
                branches = new[] { UnitBranch.Armour, UnitBranch.Mechanised }
            },
            new Arm
            {
                title = "AIR", glyph = () => UiIcons.Jet,
                blurb = "Crewed aviation and unmanned systems, and the ground-based air defence " +
                        "that exists to meet them. Neither holds ground.",
                tone = new Color(0.14f, 0.22f, 0.34f),
                branches = new[] { UnitBranch.Air, UnitBranch.AntiAircraft }
            },
            new Arm
            {
                title = "NAVY", glyph = () => UiIcons.Warship,
                blurb = "Vessels. Weeks of fuel and rations, and no terrain to take.",
                tone = new Color(0.12f, 0.24f, 0.30f),
                branches = new[] { UnitBranch.Navy }
            },
            new Arm
            {
                title = "LOGISTICS", glyph = () => UiIcons.Pallet,
                blurb = "The rear area: depots, supply, fuel, ammunition, repair and medical points. " +
                        "They hold no ground and fire nothing — everything that does depends on them.",
                tone = new Color(0.14f, 0.24f, 0.24f),
                branches = System.Array.Empty<UnitBranch>(),
                installations = true
            },
            new Arm
            {
                title = "MORE", glyph = () => UiIcons.Layers,
                blurb = "Sustainment, and the combat support that belongs to no single arm: " +
                        "engineers, signals, ISR, influence, cyber.",
                tone = new Color(0.22f, 0.18f, 0.26f),
                branches = new[] { UnitBranch.Logistics, UnitBranch.Other }
            }
        };

        /// <summary>
        /// One table column. <see cref="Cell"/> takes <c>object</c> rather than
        /// a <see cref="UnitDefinition"/> because this table serves two record
        /// types now — formations and logistic installations — and the
        /// alternative was a second copy of the header, row, sort and highlight
        /// code that differed only in a cast.
        /// </summary>
        class Column
        {
            public readonly string Label;
            public readonly float Weight;
            public readonly string Sort;
            /// <summary>Cell text for one record; null for the icon column.</summary>
            public readonly System.Func<object, string> Cell;
            public float Start, End;
            public Column(string label, float weight, string sort, System.Func<object, string> cell)
            { Label = label; Weight = weight; Sort = sort; Cell = cell; }
        }

        static readonly Column[] Columns =
        {
            new Column("ICON",     1.40f, null,       null),
            new Column("NAME",     3.20f, "name",     r => Def(r).name),
            new Column("BRANCH",   1.30f, "branch",   r => UnitBranchInfo.DisplayName(Def(r).Branch)),
            new Column("ATK",      0.70f, "attack",   r => $"{Def(r).attack:0}"),
            new Column("DEF",      0.70f, "defence",  r => $"{Def(r).defence:0}"),
            new Column("ARM",      0.70f, "armour",   r => $"{Def(r).armour:0}"),
            new Column("RANGE",    1.00f, "range",    r => $"{Def(r).weaponRangeKm:0.#} km"),
            new Column("SPEED",    1.05f, "speed",    r => $"{Def(r).speedKmh:0} km/h"),
            new Column("MANPOWER", 1.15f, "manpower", r => $"{Def(r).manpower:n0}")
        };

        /// <summary>
        /// The installations table. Deliberately **not** the formation columns
        /// with blanks in them: an installation has no attack, no armour and no
        /// speed, and a row of dashes under those headings would state six
        /// values that do not exist. What it has instead is a reach, a stock and
        /// a service, which is exactly what distinguishes one from another.
        /// </summary>
        static readonly Column[] InstallationColumns =
        {
            new Column("ICON",    1.40f, null,      null),
            new Column("NAME",    2.60f, "name",    r => Site(r).name),
            new Column("SERVES",  1.60f, "serves",  r => ServesText(Site(r))),
            new Column("REACH",   1.10f, "reach",   r => $"{Site(r).serviceRadiusKm:0.#} km"),
            new Column("ISSUES",  1.00f, "issues",  r => $"{Site(r).defaultStock:0.#}"),
            new Column("PURPOSE", 3.10f, null,      r => Site(r).detail)
        };

        static UnitDefinition Def(object record) => (UnitDefinition)record;
        static LogisticsDef Site(object record) => (LogisticsDef)record;

        /// <summary>
        /// What one issue from this kind actually puts back — the sentence a
        /// reader needs before the radius or the stock figure means anything.
        /// </summary>
        static string RestoresText(LogisticsDef d) => d.service switch
        {
            SupplyService.Ammunition => "Half an establishment of rounds",
            SupplyService.Fuel => "Half a tank of fuel",
            SupplyService.Medical => "8 % strength, up to 75 %",
            SupplyService.Repair => "15 % serviceability, up to fully fit",
            SupplyService.General => "All of the above, at 70 %",
            _ => "Nothing — this kind is a map graphic"
        };

        static string ServesText(LogisticsDef d) => d.service switch
        {
            SupplyService.Ammunition => "Ammunition",
            SupplyService.Fuel => "Fuel",
            SupplyService.Medical => "Casualties",
            SupplyService.Repair => "Vehicles",
            SupplyService.General => "Everything",
            _ => "Map graphic"
        };

        static void NormaliseColumns(Column[] columns)
        {
            float total = 0f;
            foreach (var c in columns) total += c.Weight;
            if (total <= 0f) return;

            float cursor = 0f;
            foreach (var c in columns)
            {
                c.Start = cursor / total;
                cursor += c.Weight;
                c.End = cursor / total;
            }
        }

        /// <summary>The columns for whichever kind of record the open arm holds.</summary>
        Column[] ActiveColumns => Installations ? InstallationColumns : Columns;

        /// <summary>True while the board's LOGISTICS entry is open.</summary>
        bool Installations => _arm != null && _arm.installations;

        // ------------------------------------------------------------- state
        Canvas _canvas;
        RectTransform _boardPage, _listPage;
        Arm _arm;

        string _sortKey = "name";
        bool _sortAscending = true;
        string _search = "";
        /// <summary>Sub-branch filter inside an arm; null shows the whole arm.</summary>
        UnitBranch? _branch;
        UnitDefinition _selected;
        /// <summary>The installation being read, on the LOGISTICS board. Null on every other.</summary>
        LogisticsDef _selectedSite;
        /// <summary>
        /// The row currently lit, by id. One field for both record types, so
        /// the highlight logic does not have to know which board is open.
        /// </summary>
        string _selectedId;

        RectTransform _header, _rowsContent, _branchRow;
        Text _listTitle, _resultCount, _hint, _detailName, _detailSub;
        RectTransform _detailIcons, _detailStats;
        InputField _searchField;
        ModelPreview _preview;
        Button _back;
        readonly Dictionary<string, Image> _rowImages = new Dictionary<string, Image>();
        readonly List<System.Action> _repaints = new List<System.Action>();

        void Start()
        {
            NormaliseColumns(Columns);
            NormaliseColumns(InstallationColumns);
            AudioManager.Apply();
            MusicManager.Play(MusicTrack.ExtrasTheme);

            _canvas = UIFactory.CreateCanvas("UnitLibraryCanvas");
            // The picture the EXTRAS board promised behind its UNITS row, not
            // the shared interior. A preview that changes the moment you act on
            // it is a preview of nothing — see BackgroundId.ExtrasUnits.
            //
            // Held back hard all the same: this screen is a table of a hundred
            // rows with a 3D model beside it, and legibility beats atmosphere
            // wherever the two disagree.
            UIFactory.CreateScreenBackground(_canvas.transform, BackgroundId.ExtrasUnits,
                BackgroundCatalog.DenseScreenScrim);

            _back = UIFactory.CreateBackButton(_canvas.transform, "BACK TO EXTRAS", GoBack);

            BuildBoard();
            BuildList();
            ShowBoard();
        }

        // ------------------------------------------------------- page 1: arms

        void BuildBoard()
        {
            _boardPage = UIFactory.CreateGroup(_canvas.transform, "BoardPage");
            UIFactory.Stretch(_boardPage);

            var title = UIFactory.CreateText(_boardPage, "UNITS", 56,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(80, -70), new Vector2(700, 80));

            // Fifteen pixels of air under the title rather than sitting on
            // its descenders: the count is a caption on UNITS, and a caption
            // crowding the word it captions reads as one clipped block of text.
            var sub = UIFactory.CreateText(_boardPage,
                $"{UnitDatabase.All.Count} formation types by arm of service, and the " +
                $"{LogisticsCatalog.All.Length} installations that supply them. Pick one to browse it.",
                20, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(sub.rectTransform, new Vector2(0f, 1f),
                new Vector2(80, -126 - SubtitleTopPad), new Vector2(1000, 28));

            // Counted once, here: the number on each card is what the whole board
            // is for, and recomputing it per card would walk the catalogue six times.
            var counts = new Dictionary<UnitBranch, int>();
            foreach (var d in UnitDatabase.All)
            {
                counts.TryGetValue(d.Branch, out int n);
                counts[d.Branch] = n + 1;
            }

            const float CardW = 430f, CardH = 220f, Gap = 26f, GridTop = 200f;
            const int Columns3 = 3;

            for (int i = 0; i < Arms.Length; i++)
            {
                int row = i / Columns3, col = i % Columns3;
                int inRow = Mathf.Min(Columns3, Arms.Length - row * Columns3);
                float rowWidth = inRow * CardW + (inRow - 1) * Gap;
                float x = -rowWidth * 0.5f + CardW * 0.5f + col * (CardW + Gap);
                float y = -(GridTop + row * (CardH + Gap)) - CardH * 0.5f;

                int total = Arms[i].installations ? LogisticsCatalog.All.Length : 0;
                foreach (var b in Arms[i].branches) { counts.TryGetValue(b, out int n); total += n; }

                var card = Card(_boardPage, Arms[i], total, CardW, CardH);
                UIFactory.Place(card, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(CardW, CardH));
                card.pivot = new Vector2(0.5f, 0.5f);
            }
        }

        /// <summary>
        /// One arm card.
        ///
        /// **It answers the cursor.** The cards used to be six inert boxes: the
        /// only thing that changed when you pointed at one was the mouse
        /// pointer itself, which on a board whose entire purpose is to be
        /// clicked made the screen read as a diagram rather than as a menu.
        /// Crossing a card now lifts its fill, brings its border up to the
        /// accent, whitens the glyph and the title, and grows the strip along
        /// its foot — the same vocabulary the menu board's rows use, so the
        /// two screens agree on what "you are about to press this" looks like.
        /// </summary>
        RectTransform Card(Transform parent, Arm arm, int count, float w, float h)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Arm_" + arm.title,
                UiTheme.Surface, UiTheme.Border);

            var btn = UIFactory.CreateButton(frame, "", () => ShowArm(arm),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var head = UIFactory.CreatePanel(frame, "Head", arm.tone);
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1);
            head.pivot = new Vector2(0.5f, 1);
            head.offsetMin = new Vector2(0, -78);
            head.offsetMax = Vector2.zero;
            head.GetComponent<Image>().raycastTarget = false;

            var icon = UIFactory.CreateImage(head, arm.glyph(), "Glyph");
            icon.color = GameConfig.UiAccent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(24, 0), new Vector2(32, 32));

            var t = UIFactory.CreateText(head, arm.title, 27, GameConfig.UiText,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(70, 0),
                new Vector2(w - 160f, 34));
            UIFactory.Fit(t, 15);

            var badge = UIFactory.CreateText(head, count.ToString(), 24, GameConfig.UiAccent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(-20, 0),
                new Vector2(70, 30));

            var b = UIFactory.CreateText(frame, arm.blurb, 17, GameConfig.UiTextDim, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(b.rectTransform, 24f, 96f, w - 48f, h - 120f);
            UIFactory.Fit(b, 12);

            var strip = UIFactory.CreatePanel(frame, "Strip", GameConfig.UiAccent);
            strip.anchorMin = Vector2.zero; strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.offsetMin = Vector2.zero;
            strip.offsetMax = new Vector2(0, CardStripRest);
            strip.GetComponent<Image>().raycastTarget = false;

            // Painted by hand rather than through Button's colour tint: the tint
            // multiplies every graphic under the button, which washes the accent
            // strip and the coloured head out instead of lifting them.
            var border = frame.GetComponent<Image>();
            var fill = frame.Find("Fill").GetComponent<Image>();
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddHover(trigger, EventTriggerType.PointerEnter,
                () => PaintCard(border, fill, strip, icon, t, true));
            AddHover(trigger, EventTriggerType.PointerExit,
                () => PaintCard(border, fill, strip, icon, t, false));
            PaintCard(border, fill, strip, icon, t, false);

            return frame;
        }

        /// <summary>Accent strip along a card's foot, at rest and under the cursor.</summary>
        const float CardStripRest = 4f, CardStripHover = 8f;

        static void PaintCard(Image border, Image fill, RectTransform strip, Image glyph,
            Text title, bool hover)
        {
            border.color = hover ? UiTheme.Accent : UiTheme.Border;
            fill.color = hover ? UiTheme.SurfaceHover : UiTheme.Surface;
            strip.offsetMax = new Vector2(0, hover ? CardStripHover : CardStripRest);
            glyph.color = hover ? Color.white : GameConfig.UiAccent;
            title.color = hover ? Color.white : GameConfig.UiText;
        }

        static void AddHover(EventTrigger trigger, EventTriggerType type, System.Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
        }

        // ------------------------------------------------------ page 2: list

        void BuildList()
        {
            _listPage = UIFactory.CreateGroup(_canvas.transform, "ListPage");
            UIFactory.Stretch(_listPage);

            _listTitle = UIFactory.CreateText(_listPage, "", 46,
                GameConfig.UiAccent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_listTitle.rectTransform, new Vector2(0f, 1f), new Vector2(80, -66), new Vector2(800, 70));

            _resultCount = UIFactory.CreateText(_listPage, "", 19, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_resultCount.rectTransform, new Vector2(0f, 1f), new Vector2(80, -116), new Vector2(900, 28));

            BuildToolbar();
            BuildTable();
            BuildDetailPanel();
        }

        void BuildToolbar()
        {
            var bar = UIFactory.CreateGroup(_listPage, "Toolbar");
            StretchToTableWidth(bar, ToolbarY, 46f);

            _searchField = UIFactory.CreateInputField(bar, "Search name, id or ammunition...", 18);
            UIFactory.Place((RectTransform)_searchField.transform, new Vector2(0f, 1f),
                new Vector2(0, 0), new Vector2(340, 44));
            _searchField.onValueChanged.AddListener(v =>
            {
                _search = v == null ? "" : v.Trim();
                Rebuild();
            });

            // Framed like the filter chips beside it rather than left as a
            // bare grey slab: it belongs to the same row of controls and it
            // undoes what they do, so it has to look like one of them.
            var resetFrame = UIFactory.CreateBorderedPanel(bar, "Reset",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(resetFrame, new Vector2(0f, 1f),
                new Vector2(356, 0), new Vector2(150, 44));

            var reset = UIFactory.CreateButton(resetFrame, "RESET FILTERS", () =>
            {
                _branch = null; _search = "";
                _sortKey = "name"; _sortAscending = true;
                if (_searchField != null) _searchField.text = "";
                Rebuild();
            }, new Color(0, 0, 0, 0), UiTheme.TextDim, 14);
            UIFactory.Stretch((RectTransform)reset.transform);
            UIFactory.Fit(reset.GetComponentInChildren<Text>(), 10);

            var resetBorder = resetFrame.GetComponent<Image>();
            var resetFill = resetFrame.Find("Fill").GetComponent<Image>();
            var resetText = reset.GetComponentInChildren<Text>();
            var resetTrigger = resetFrame.gameObject.AddComponent<EventTrigger>();
            AddHover(resetTrigger, EventTriggerType.PointerEnter, () =>
            {
                resetBorder.color = UiTheme.Accent;
                resetFill.color = UiTheme.SurfaceHover;
                resetText.color = UiTheme.Text;
            });
            AddHover(resetTrigger, EventTriggerType.PointerExit, () =>
            {
                resetBorder.color = UiTheme.Border;
                resetFill.color = UiTheme.Surface;
                resetText.color = UiTheme.TextDim;
            });

            _branchRow = UIFactory.CreateGroup(_listPage, "BranchFilter");
            StretchToTableWidth(_branchRow, ToolbarY - 52f, 40f);

            _hint = UIFactory.CreateText(_listPage, "", 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            StretchToTableWidth(_hint.rectTransform, HintY, 22f);
        }

        /// <summary>
        /// The sub-branch buttons for whichever arm is open. Rebuilt on entry
        /// rather than once, because ARMOUR holds two branches and NAVY one —
        /// a fixed row would be a row of buttons that mostly do nothing.
        /// </summary>
        void BuildBranchButtons()
        {
            ClearChildren(_branchRow);
            _repaints.Clear();
            // The installations board has one table and nothing to filter it by:
            // six rows is the whole catalogue. A row of chips that could only
            // ever say ALL would be a control that does nothing.
            if (_arm == null || _arm.installations) return;

            var counts = new Dictionary<UnitBranch, int>();
            foreach (var d in UnitDatabase.All)
            {
                counts.TryGetValue(d.Branch, out int n);
                counts[d.Branch] = n + 1;
            }

            // Chips rather than buttons: a bordered surface that goes to the
            // accent when it is the one in force, with an underline along its
            // foot. Three flat grey slabs, one of which happened to be blue,
            // gave the player no way to tell "pressable" from "pressed" except
            // the colour — which is the one cue a colour-blind player does not
            // have.
            var chips = new List<(RectTransform frame, Text label, RectTransform underline,
                UnitBranch? branch)>();
            const float segW = 150f;

            void Add(string label, UnitBranch? branch, int index)
            {
                var frame = UIFactory.CreateBorderedPanel(_branchRow, "Chip_" + label,
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f),
                    new Vector2(index * (segW + 6f), 0), new Vector2(segW, 38));

                var btn = UIFactory.CreateButton(frame, label,
                    () => { _branch = branch; Rebuild(); },
                    new Color(0, 0, 0, 0), UiTheme.Text, 14);
                UIFactory.Stretch((RectTransform)btn.transform);
                var text = btn.GetComponentInChildren<Text>();
                UIFactory.Fit(text, 10);

                var underline = UIFactory.CreatePanel(frame, "Underline", GameConfig.UiAccent);
                underline.anchorMin = Vector2.zero; underline.anchorMax = new Vector2(1, 0);
                underline.pivot = new Vector2(0.5f, 0);
                underline.offsetMin = Vector2.zero;
                underline.offsetMax = new Vector2(0, 3);
                underline.GetComponent<Image>().raycastTarget = false;

                chips.Add((frame, text, underline, branch));
            }

            int total = 0;
            foreach (var b in _arm.branches) { counts.TryGetValue(b, out int n); total += n; }

            // A single-branch arm has nothing to filter, so it gets no row at
            // all rather than one button that is always on.
            if (_arm.branches.Length > 1)
            {
                Add($"ALL {total}", null, 0);
                for (int i = 0; i < _arm.branches.Length; i++)
                {
                    var b = _arm.branches[i];
                    counts.TryGetValue(b, out int n);
                    Add($"{UnitBranchInfo.DisplayName(b).ToUpperInvariant()} {n}", b, i + 1);
                }
            }

            _repaints.Add(() =>
            {
                foreach (var (frame, text, underline, branch) in chips)
                {
                    bool on = branch == _branch;
                    // Solid accent when it is the one in force — a wash over a
                    // photographic background is not a fill, it is a tint of
                    // whatever happens to be behind it. The underline goes
                    // white so the state is legible without reading the colour.
                    frame.GetComponent<Image>().color = on ? UiTheme.Accent : UiTheme.Border;
                    var fillRt = frame.Find("Fill");
                    if (fillRt != null)
                        fillRt.GetComponent<Image>().color = on ? GameConfig.UiAccent : UiTheme.Surface;
                    underline.GetComponent<Image>().color = on ? Color.white : GameConfig.UiAccent;
                    underline.gameObject.SetActive(on);
                    if (text == null) continue;
                    text.color = on ? GameConfig.UiBackground : UiTheme.TextDim;
                    text.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                }
            });
        }

        void BuildTable()
        {
            var table = UIFactory.CreateGroup(_listPage, "Table");
            table.anchorMin = new Vector2(0, 0); table.anchorMax = new Vector2(1, 1);
            table.pivot = new Vector2(0.5f, 1f);
            table.offsetMin = new Vector2(ScreenMargin, BottomMargin);
            table.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), TableY);

            _header = UIFactory.CreatePanel(table, "Header", GameConfig.UiPanel);
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1);
            _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -40); _header.offsetMax = Vector2.zero;

            var scroll = UIFactory.CreateScrollView(table, out _rowsContent, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = Vector2.zero; srt.offsetMax = new Vector2(0, -44);
        }

        static void StretchToTableWidth(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(ScreenMargin, top - height);
            rt.offsetMax = new Vector2(-(ScreenMargin + PanelW + ColumnGap), top);
        }

        static void SpanColumn(RectTransform rt, Column col, float inset, float height)
        {
            rt.anchorMin = new Vector2(col.Start, 0.5f);
            rt.anchorMax = new Vector2(col.End, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, -height * 0.5f);
            rt.offsetMax = new Vector2(-4f, height * 0.5f);
        }

        static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        // ------------------------------------------------------ page switching

        void ShowBoard()
        {
            _arm = null;
            _selected = null;
            _selectedSite = null;
            _selectedId = null;
            _boardPage.gameObject.SetActive(true);
            _listPage.gameObject.SetActive(false);
            UIFactory.SetBackButtonLabel(_back, "BACK TO EXTRAS");
        }

        void ShowArm(Arm arm)
        {
            _arm = arm;
            _branch = null;
            _selected = null;
            _selectedSite = null;
            _selectedId = null;
            _search = "";
            _sortKey = "name";
            _sortAscending = true;
            if (_searchField != null) _searchField.text = "";

            _boardPage.gameObject.SetActive(false);
            _listPage.gameObject.SetActive(true);
            UIFactory.SetBackButtonLabel(_back, "BACK TO UNITS");

            _listTitle.text = arm.title;
            BuildBranchButtons();
            Rebuild();
        }

        void GoBack()
        {
            // One step at a time: an arm goes back to the board, the board goes
            // back to EXTRAS. A player who has just read about infantry wants the
            // other arms, not the main menu.
            if (_arm != null) { ShowBoard(); return; }
            SceneManager.LoadScene(GameConfig.SceneExtras);
        }

        // ---------------------------------------------------------- rebuild

        List<UnitDefinition> Visible()
        {
            var list = new List<UnitDefinition>();
            if (_arm == null) return list;

            foreach (var d in UnitDatabase.All)
            {
                bool inArm = false;
                foreach (var b in _arm.branches) if (d.Branch == b) { inArm = true; break; }
                if (!inArm) continue;
                if (_branch.HasValue && d.Branch != _branch.Value) continue;
                if (!MatchesSearch(d)) continue;
                list.Add(d);
            }

            list.Sort((a, b) =>
            {
                int c = Compare(a, b);
                if (c == 0) c = string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
                return _sortAscending ? c : -c;
            });
            return list;
        }

        bool MatchesSearch(UnitDefinition d)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return Has(d.name) || Has(d.id) || Has(d.ammoType) || Has(d.description);

            bool Has(string s) => !string.IsNullOrEmpty(s) &&
                s.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        int Compare(UnitDefinition a, UnitDefinition b) => _sortKey switch
        {
            "name" => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase),
            "branch" => ((int)a.Branch).CompareTo((int)b.Branch),
            "attack" => a.attack.CompareTo(b.attack),
            "defence" => a.defence.CompareTo(b.defence),
            "armour" => a.armour.CompareTo(b.armour),
            "range" => a.weaponRangeKm.CompareTo(b.weaponRangeKm),
            "speed" => a.speedKmh.CompareTo(b.speedKmh),
            "manpower" => a.manpower.CompareTo(b.manpower),
            _ => 0
        };

        void Rebuild()
        {
            BuildHeaderCells();
            foreach (var repaint in _repaints) repaint();

            ClearChildren(_rowsContent);
            _rowImages.Clear();

            if (Installations) { RebuildInstallations(); return; }

            var rows = Visible();
            foreach (var d in rows) CreateRow(d);

            _resultCount.text = $"{rows.Count} unit type(s)";
            _hint.text = _branch.HasValue
                ? UnitBranchInfo.Blurb(_branch.Value)
                : _arm?.blurb ?? "";

            if (rows.Count == 0)
            {
                Empty();
                Select(null);
                return;
            }

            if (_selected != null && rows.Contains(_selected)) Highlight();
            else Select(rows[0]);
        }

        void Empty()
        {
            var empty = UIFactory.CreateText(_rowsContent, "Nothing matches that search.",
                18, GameConfig.UiTextDim);
            ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 60);
        }

        // ------------------------------------------------- the installations

        void RebuildInstallations()
        {
            var rows = VisibleSites();
            foreach (var d in rows) CreateSiteRow(d);

            _resultCount.text = $"{rows.Count} installation(s)";
            _hint.text = _arm?.blurb ?? "";

            if (rows.Count == 0) { Empty(); SelectSite(null); return; }

            if (_selectedSite != null && rows.Contains(_selectedSite)) Highlight();
            else SelectSite(rows[0]);
        }

        List<LogisticsDef> VisibleSites()
        {
            var list = new List<LogisticsDef>();
            foreach (var d in LogisticsCatalog.All)
            {
                if (!string.IsNullOrEmpty(_search) &&
                    !Contains(d.name) && !Contains(d.detail) && !Contains(d.kind.ToString()))
                    continue;
                list.Add(d);
            }

            list.Sort((a, b) =>
            {
                int c = CompareSites(a, b);
                if (c == 0) c = string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
                return _sortAscending ? c : -c;
            });
            return list;

            bool Contains(string s) => !string.IsNullOrEmpty(s) &&
                s.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        int CompareSites(LogisticsDef a, LogisticsDef b) => _sortKey switch
        {
            "name" => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase),
            "serves" => string.Compare(ServesText(a), ServesText(b), System.StringComparison.OrdinalIgnoreCase),
            "reach" => a.serviceRadiusKm.CompareTo(b.serviceRadiusKm),
            "issues" => a.defaultStock.CompareTo(b.defaultStock),
            // The catalogue's own order is rear-to-front, which is how the
            // LOGISTICS panel reads and how a laydown is built. It is the right
            // default for a table nobody has asked to sort another way.
            _ => ((int)a.kind).CompareTo((int)b.kind)
        };

        void CreateSiteRow(LogisticsDef def)
        {
            string id = def.kind.ToString();
            var row = UIFactory.CreatePanel(_rowsContent, "Site_" + id, RowColour(false));
            row.sizeDelta = new Vector2(0, 54);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => SelectSite(def));
            _rowImages[id] = row.GetComponent<Image>();

            foreach (var col in InstallationColumns)
            {
                if (col.Cell == null)
                {
                    // The kind's own glyph in its own tint — the same picture
                    // the panel button, the placement preview and the map marker
                    // carry, so the four cannot disagree.
                    var glyph = UIFactory.CreateImage(row, UiIcons.GlyphFor(def.kind), "Glyph");
                    glyph.color = def.tint;
                    glyph.raycastTarget = false;
                    var grt = (RectTransform)glyph.transform;
                    grt.anchorMin = grt.anchorMax = new Vector2(col.Start, 0.5f);
                    grt.pivot = new Vector2(0f, 0.5f);
                    grt.anchoredPosition = new Vector2(30f, 0f);
                    grt.sizeDelta = new Vector2(34, 34);
                    continue;
                }

                bool lead = col.Label == "NAME";
                var t = UIFactory.CreateText(row, col.Cell(def), lead ? 17 : 15,
                    lead ? GameConfig.UiText : GameConfig.UiTextDim, TextAnchor.MiddleLeft);
                SpanColumn(t.rectTransform, col, 4f, 40f);
                UIFactory.Fit(t, 11);
            }
        }

        void SelectSite(LogisticsDef def)
        {
            Unhighlight();
            _selected = null;
            _selectedSite = def;
            _selectedId = def?.kind.ToString();
            Highlight();

            _detailName.text = def == null ? "—" : def.name;
            _detailSub.text = def == null
                ? "No installation selected"
                : $"Logistic installation  ·  {ServesText(def)}  ·  id: {def.kind}";

            ClearChildren(_detailIcons);
            if (def != null)
            {
                var glyph = UIFactory.CreateImage(_detailIcons, UiIcons.GlyphFor(def.kind), "Glyph");
                glyph.color = def.tint;
                glyph.raycastTarget = false;
                var grt = (RectTransform)glyph.transform;
                grt.anchorMin = grt.anchorMax = new Vector2(0f, 0.5f);
                grt.pivot = new Vector2(0f, 0.5f);
                grt.anchoredPosition = Vector2.zero;
                grt.sizeDelta = new Vector2(44, 44);
            }

            if (_preview != null)
            {
                bool shows = def != null && !string.IsNullOrEmpty(def.modelId);
                _preview.gameObject.SetActive(shows);
                if (shows) _preview.ShowModel(def.modelId, def.name);
            }

            BuildSiteStats(def);
        }

        void BuildSiteStats(LogisticsDef def)
        {
            ClearChildren(_detailStats);
            if (def == null) return;

            var d = UIFactory.CreateText(_detailStats, def.detail, 15,
                GameConfig.UiTextDim, TextAnchor.UpperLeft);
            d.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            ((RectTransform)d.transform).sizeDelta = new Vector2(0, 40);

            Section("WHAT IT DOES");
            Stat("Serves", ServesText(def));
            Stat("Service radius", $"{def.serviceRadiusKm:0.#} km");
            Stat("Stock when placed", $"{def.defaultStock:0.#} issues");
            // One issue is one formation's worth, scaled by echelon — the figure
            // above means nothing without it, and a reader on this screen has
            // not seen the supply panel where that is spelled out.
            Stat("One issue", "one formation's worth, √manpower-scaled");
            Stat("Restores", RestoresText(def));

            Section("ON THE MAP");
            Stat("Ground ring", "The owning side — blue or red");
            Stat("Marker", "Its glyph on a plate framed in the side's colour");
            Stat("Service ring", "Drawn on the terrain when asked for");

            Section("3D MODEL");
            var model = UnitModelLibrary.Get(def.modelId);
            Stat("Source", model?.sourceAsset ?? "none yet");
            Stat("Animation", model?.idleClip ?? "static — a depot is a place");
        }

        void BuildHeaderCells()
        {
            ClearChildren(_header);

            foreach (var col in ActiveColumns)
            {
                bool active = col.Sort != null && col.Sort == _sortKey;
                string label = col.Label + (active ? (_sortAscending ? "  ▲" : "  ▼") : "");

                if (col.Sort == null)
                {
                    var t = UIFactory.CreateText(_header, label, 14, GameConfig.UiAccent,
                        TextAnchor.MiddleLeft, FontStyle.Bold);
                    SpanColumn(t.rectTransform, col, 30f, 32f);
                    UIFactory.Fit(t, 10);
                    continue;
                }

                string key = col.Sort;
                var btn = UIFactory.CreateButton(_header, label, () =>
                {
                    if (_sortKey == key) _sortAscending = !_sortAscending;
                    else { _sortKey = key; _sortAscending = true; }
                    Rebuild();
                }, new Color(1f, 1f, 1f, active ? 0.08f : 0.02f),
                   active ? GameConfig.UiAccent : GameConfig.UiText, 14);
                SpanColumn((RectTransform)btn.transform, col, RowPad, 32f);

                var txt = btn.GetComponentInChildren<Text>();
                txt.alignment = TextAnchor.MiddleLeft;
                txt.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                txt.rectTransform.offsetMin = new Vector2(4, 0);
                UIFactory.Fit(txt, 10);
            }
        }

        void CreateRow(UnitDefinition def)
        {
            var row = UIFactory.CreatePanel(_rowsContent, "Row_" + def.id, RowColour(false));
            row.sizeDelta = new Vector2(0, 54);

            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = row.GetComponent<Image>();
            btn.onClick.AddListener(() => Select(def));
            _rowImages[def.id] = row.GetComponent<Image>();

            foreach (var col in Columns)
            {
                if (col.Cell == null)
                {
                    PlaceIcon(row, "Friendly", def.id, 30f, col);
                    continue;
                }

                bool lead = col.Label == "NAME";
                var t = UIFactory.CreateText(row, col.Cell(def), lead ? 17 : 15,
                    lead ? GameConfig.UiText : GameConfig.UiTextDim, TextAnchor.MiddleLeft);
                SpanColumn(t.rectTransform, col, 4f, 40f);
                UIFactory.Fit(t, 11);
            }
        }

        static Color RowColour(bool selected) =>
            selected
                ? new Color(GameConfig.UiAccent.r, GameConfig.UiAccent.g, GameConfig.UiAccent.b, 0.22f)
                : new Color(1f, 1f, 1f, 0.03f);

        void PlaceIcon(Transform parent, string folder, string unitId, float x, Column col = null)
        {
            var sprite = UIFactory.LoadIconSprite(folder, unitId);
            if (sprite == null) return;
            var img = UIFactory.CreateImage(parent, sprite, folder + "Icon");
            var rt = (RectTransform)img.transform;

            float left = col != null ? col.Start : 0f;
            rt.anchorMin = rt.anchorMax = new Vector2(left, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(44, 44);
            img.raycastTarget = false;
        }

        // ----------------------------------------------------- detail panel

        void BuildDetailPanel()
        {
            // Bordered, like every other block of chrome on this screen. A
            // flat fill against a photographic background has no edge of its
            // own, so the panel appeared to float rather than to be a panel.
            var panel = UIFactory.CreateBorderedPanel(_listPage, "DetailPanel",
                GameConfig.UiPanel, UiTheme.Border);
            panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(-(ScreenMargin + PanelW), BottomMargin);
            panel.offsetMax = new Vector2(-ScreenMargin, PanelY);

            float inner = PanelW - PanelInset * 2f;

            _detailName = UIFactory.CreateText(panel, "", 26, GameConfig.UiAccent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_detailName.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -16), new Vector2(inner, 34));

            _detailSub = UIFactory.CreateText(panel, "", 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_detailSub.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -52), new Vector2(inner, 22));

            _detailIcons = UIFactory.CreateGroup(panel, "Icons");
            UIFactory.Place(_detailIcons, new Vector2(0f, 1f), new Vector2(PanelInset, -80), new Vector2(inner, 46));

            var previewSize = new Vector2(inner, 300);
            _preview = ModelPreview.Create(panel, previewSize);
            UIFactory.Place((RectTransform)_preview.transform, new Vector2(0f, 1f),
                new Vector2(PanelInset, -134), previewSize);

            var hint = UIFactory.CreateText(panel, "Drag to orbit · scroll to zoom", 13,
                GameConfig.UiTextDim, TextAnchor.MiddleRight);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(PanelInset, -438), new Vector2(inner, 18));

            var scroll = UIFactory.CreateScrollView(panel, out _detailStats, withScrollbar: true);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(PanelInset, 16);
            srt.offsetMax = new Vector2(-PanelInset, -462);
        }

        void Select(UnitDefinition def)
        {
            Unhighlight();
            _selectedSite = null;
            _selected = def;
            _selectedId = def?.id;
            Highlight();

            _detailName.text = def == null ? "—" : def.name.ToUpperInvariant();
            _detailSub.text = def == null
                ? "No unit selected"
                : $"{UnitBranchInfo.DisplayName(def.Branch)}  ·  {UnitCategoryInfo.DisplayName(def.Category)}  ·  id: {def.id}";

            ClearChildren(_detailIcons);
            if (def != null)
            {
                PlaceIcon(_detailIcons, "Friendly", def.id, 0f);
                PlaceIcon(_detailIcons, "Enemy", def.id, 52f);
            }

            // Re-shown as well as re-pointed: the installations board hides the
            // preview for a kind with no model yet, and a formation arriving
            // after that would otherwise have its model drawn into a panel that
            // is switched off.
            if (_preview != null)
            {
                _preview.gameObject.SetActive(true);
                _preview.Show(def);
            }
            BuildStats(def);
        }

        void Highlight()
        {
            if (_selectedId == null) return;
            if (_rowImages.TryGetValue(_selectedId, out var image) && image != null)
                image.color = RowColour(true);
        }

        void Unhighlight()
        {
            if (_selectedId == null) return;
            if (_rowImages.TryGetValue(_selectedId, out var image) && image != null)
                image.color = RowColour(false);
        }

        void BuildStats(UnitDefinition def)
        {
            ClearChildren(_detailStats);
            if (def == null) return;

            if (!string.IsNullOrEmpty(def.description))
            {
                var d = UIFactory.CreateText(_detailStats, def.description, 15,
                    GameConfig.UiTextDim, TextAnchor.UpperLeft);
                d.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
                ((RectTransform)d.transform).sizeDelta = new Vector2(0, 54);
            }

            Section("CLASSIFICATION");
            Stat("Branch", UnitBranchInfo.DisplayName(def.Branch));
            Stat("Category", UnitCategoryInfo.DisplayName(def.Category));
            Stat("Holds ground", def.HoldsGround ? "Yes" : "No");

            Section("COMBAT");
            Stat("Attack", $"{def.attack:0}");
            Stat("Hard attack", $"{def.hardAttack:0}");
            Stat("Defence", $"{def.defence:0}");
            Stat("Armour", $"{def.armour:0}");
            Stat("Anti-air", $"{def.antiAir:0}");
            Stat("Weapon range", $"{def.weaponRangeKm:0.#} km");
            Stat("View range", $"{def.viewRangeKm:0.#} km");

            Section("FORMATION");
            Stat("Manpower (company)", $"{def.manpower:n0}");
            Stat("Training", $"{def.training:0}");
            Stat("Morale", $"{def.morale:0}");
            Stat("Organisation", $"{def.organisation:0}");
            Stat("Speed", $"{def.speedKmh:0} km/h");

            Section("LOGISTICS");
            Stat("Ammunition", string.IsNullOrEmpty(def.ammoType) ? "—" : def.ammoType);
            Stat("Ammo carried", $"{def.ammoStock:n0}");
            Stat("Fuel carried", def.fuelStock > 0 ? $"{def.fuelStock:n0} L" : "—");
            Stat("Fuel per km", def.fuelUsePerKm > 0 ? $"{def.fuelUsePerKm:0.#} L" : "—");
            Stat("Rations", $"{def.foodDays} days");

            var flags = new List<string>();
            if (def.canIndirectFire) flags.Add("Indirect fire");
            if (def.canCounterUas) flags.Add("Counter-UAS");
            if (def.isSupport) flags.Add("Support");
            Section("CAPABILITIES");
            Stat("Flags", flags.Count > 0 ? string.Join(", ", flags) : "—");

            Section("3D MODEL");
            var model = UnitModelLibrary.Resolve(def);
            Stat("Source", model?.sourceAsset ?? "none yet");
            Stat("Idle animation", model?.idleClip ?? "—");
        }

        void Section(string label)
        {
            var t = UIFactory.CreateText(_detailStats, label, 14, GameConfig.UiAccent,
                TextAnchor.LowerLeft, FontStyle.Bold);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, 30);
        }

        const float StatSplit = 0.46f;

        void Stat(string label, string value)
        {
            var row = UIFactory.CreateGroup(_detailStats, "Stat_" + label);
            row.sizeDelta = new Vector2(0, 26);

            var l = UIFactory.CreateText(row, label, 15, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0); l.rectTransform.anchorMax = new Vector2(StatSplit, 1);
            l.rectTransform.offsetMin = Vector2.zero; l.rectTransform.offsetMax = new Vector2(-6, 0);
            UIFactory.Fit(l, 11);

            var v = UIFactory.CreateText(row, value, 15, GameConfig.UiText, TextAnchor.MiddleRight);
            v.rectTransform.anchorMin = new Vector2(StatSplit, 0); v.rectTransform.anchorMax = new Vector2(1, 1);
            v.rectTransform.offsetMin = Vector2.zero; v.rectTransform.offsetMax = new Vector2(-4, 0);
            UIFactory.Fit(v, 11);
        }

        void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            var focused = EventSystem.current?.currentSelectedGameObject;
            if (focused != null && focused.GetComponent<InputField>() != null) return;

            GoBack();
        }
    }
}
