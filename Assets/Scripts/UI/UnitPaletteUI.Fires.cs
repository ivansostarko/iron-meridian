using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Save;
using IronMeridian.Units;
using IronMeridian.Vfx;
using IronMeridian.Weather;

namespace IronMeridian.UI
{
    /// <summary>
    /// <see cref="UnitPaletteUI"/> — the called-fire sections: artillery, air strikes, air supply, UAV strikes and naval gunfire.
    ///
    /// One part of a class split across files purely for size: the editor
    /// palette is the largest screen in the game, and a single file made every
    /// change to it a scroll hunt. Nothing here is independent of the other
    /// parts — the fields and lifecycle live in UnitPaletteUI.cs.
    ///
    /// Sections: artillery section, air strike section, air supply section, uav strike section, navy strike section.
    /// </summary>
    public partial class UnitPaletteUI
    {
        // --------------------------------------------------- artillery section

        ArtilleryStrikeSystem _artillery;
        readonly List<(ArtilleryCaliber caliber, Image fill, Text label)> _artilleryButtons =
            new List<(ArtilleryCaliber, Image, Text)>();
        readonly Dictionary<ArtilleryOrigin, RectTransform> _artilleryPages =
            new Dictionary<ArtilleryOrigin, RectTransform>();
        readonly List<(ArtilleryOrigin origin, Image fill, Text label)> _originTabs =
            new List<(ArtilleryOrigin, Image, Text)>();
        ArtilleryOrigin _artilleryOrigin = ArtilleryOrigin.Nato;

        /// <summary>
        /// Button glyph per nature. The catalogue owns the numbers; the UI owns
        /// the pictures. Chosen by kind and weight rather than by exact calibre,
        /// so a new nature gets a sensible icon without touching this.
        /// </summary>
        static Sprite CaliberGlyph(ArtilleryDef def)
        {
            if (def.kind == ArtilleryKind.Mortar) return UiIcons.MortarBomb;
            if (def.calibreMm <= 105) return UiIcons.ShellLight;
            if (def.calibreMm >= 152) return UiIcons.ShellHeavy;
            return UiIcons.ShellMedium;
        }

        /// <summary>
        /// The fire-support menu, driven entirely from <see cref="ArtilleryCatalog"/>.
        ///
        /// Fourteen natures will not fit in one column, and stacking them into a
        /// scroll would bury the choice that actually matters. They are split by
        /// **inventory** instead — NATO or Enemy — because that is the first
        /// decision a player makes and it halves the list. Within a page they run
        /// mortars then guns, ascending by calibre, so the beaten zone grows
        /// monotonically down the page and the trade-off between natures is
        /// legible without reading a word.
        /// </summary>
        void BuildArtillerySection(RectTransform content)
        {
            SectionLabel(content, "CALL FOR FIRE", -8);
            StrikeBudgetRow(content, -28f);

            BuildOriginTabs(content, -64f);

            // One page per inventory, both laid out at the same origin; only the
            // selected one is active.
            foreach (ArtilleryOrigin origin in System.Enum.GetValues(typeof(ArtilleryOrigin)))
            {
                var page = UIFactory.CreateGroup(content, "ArtyPage_" + origin);
                page.anchorMin = new Vector2(0, 0); page.anchorMax = new Vector2(1, 1);
                page.offsetMin = Vector2.zero; page.offsetMax = Vector2.zero;
                _artilleryPages[origin] = page;
                BuildOriginPage(page, origin);
            }

            ShowArtilleryOrigin(_artilleryOrigin);
            RefreshArtillery();
        }

        void BuildOriginTabs(RectTransform content, float y)
        {
            var origins = new[] { ArtilleryOrigin.Nato, ArtilleryOrigin.Enemy };
            var names = new[] { "NATO", "ENEMY" };
            float w = (InnerWidth - 6f) / 2f;

            for (int i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                var frame = UIFactory.CreateBorderedPanel(content, "Origin_" + names[i],
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 6f), y),
                    new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, names[i], () => ShowArtilleryOrigin(origin),
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
                UIFactory.Stretch((RectTransform)btn.transform);

                _originTabs.Add((origin, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        void BuildOriginPage(RectTransform page, ArtilleryOrigin origin)
        {
            // Clear of the section label, the allowance readout and the tabs.
            float y = -102f;
            ArtilleryKind? lastKind = null;

            foreach (var def in ArtilleryCatalog.OfOrigin(origin))
            {
                // A heading each time the class changes: a mortar and a gun of
                // the same calibre are different weapons, and the list should say so.
                if (lastKind != def.kind)
                {
                    SectionLabel(page, def.kind == ArtilleryKind.Mortar ? "MORTARS" : "GUNS & HOWITZERS", y);
                    y -= 22f;
                    lastKind = def.kind;
                }

                ArtilleryButton(page, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(page, "StandDown", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "STAND DOWN",
                () => { if (_artillery != null) _artillery.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(page,
                "Pick a nature, then click the map to place the target area. A ten second countdown runs in the " +
                "HUD, then " + ArtilleryCatalog.ShellsPerMission + " rounds land inside the circle. The number on " +
                "each button is that nature's beaten zone. A mission cannot be recalled once away — STAND DOWN " +
                "only clears the tube. Several can be in the air at once, so fire can be walked across a position.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 44f),
                new Vector2(InnerWidth, 130));
        }

        void ShowArtilleryOrigin(ArtilleryOrigin origin)
        {
            _artilleryOrigin = origin;
            foreach (var kv in _artilleryPages) kv.Value.gameObject.SetActive(kv.Key == origin);

            foreach (var (o, fill, label) in _originTabs)
            {
                bool on = o == origin;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        void ArtilleryButton(RectTransform content, ArtilleryDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Arty_" + def.caliber, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_artillery != null) _artillery.Toggle(def.caliber); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, CaliberGlyph(def), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(22, 22));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                40f, InnerWidth - 88f, topInset: 6f);

            // Beaten zone on the right. It is the number that decides which
            // nature to call for, so it belongs on the button rather than only
            // in the hint text.
            var radius = UIFactory.CreateText(frame, def.radiusMeters.ToString("0") + " m", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 6), new Vector2(52, 14));

            AllowanceLabel(frame, ArtilleryCatalog.BudgetKey(def.caliber), def.missions);

            _artilleryButtons.Add((def.caliber, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshArtillery()
        {
            if (_artillery == null) return;
            foreach (var (caliber, fill, label) in _artilleryButtons)
            {
                bool on = _artillery.Armed.HasValue && _artillery.Armed.Value == caliber;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- air strike section

        AirStrikeSystem _airStrike;
        readonly List<(StrikeAircraft aircraft, Image fill, Text label)> _airStrikeButtons =
            new List<(StrikeAircraft, Image, Text)>();

        /// <summary>
        /// Button glyph per airframe. The flying wing is the bomber's own
        /// silhouette; the other two borrow shapes that read at 24 px — a rotor
        /// disc for the helicopter, a swept dart for the fighter.
        /// </summary>
        static Sprite AirframeGlyph(StrikeAircraft aircraft) => aircraft switch
        {
            StrikeAircraft.AttackHelicopter => UiIcons.Helicopter,
            StrikeAircraft.StrikeFighter => UiIcons.Jet,
            _ => UiIcons.FlyingWing
        };

        /// <summary>
        /// The air-tasking menu. Same shape as the artillery panel because it is
        /// the same decision — pick a delivery means, then commit a piece of
        /// ground — and driven entirely from <see cref="AirStrikeCatalog"/>.
        /// </summary>
        void BuildAirStrikeSection(RectTransform content)
        {
            SectionLabel(content, "TASK AN AIRFRAME", -8);
            StrikeBudgetRow(content, -28f);

            float y = -64f;
            foreach (var def in AirStrikeCatalog.All)
            {
                AirStrikeButton(content, def, y);
                y -= 58f;
            }

            var abort = UIFactory.CreateBorderedPanel(content, "Abort", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(abort, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var abortBtn = UIFactory.CreateButton(abort, "ABORT TASKING",
                () => { if (_airStrike != null) _airStrike.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)abortBtn.transform);

            var hint = UIFactory.CreateText(content,
                $"Pick an airframe, then click the map to place the target area. A " +
                $"{AirStrikeCatalog.CountdownSeconds:0} second countdown runs in the HUD, then the aircraft " +
                $"runs in and releases {AirStrikeCatalog.BombsPerStrike} weapons in one pass. The stick walks " +
                "along its track, so the blasts follow the aeroplane rather than landing in a heap. The attack " +
                "heading is different every time. A tasked strike cannot be recalled — abort only clears the " +
                "airframe before it is sent.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 160));

            RefreshAirStrike();
        }

        void AirStrikeButton(RectTransform content, AircraftDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Air_" + def.label, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_airStrike != null) _airStrike.Toggle(def.aircraft); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, AirframeGlyph(def.aircraft), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                46f, InnerWidth - 92f, topInset: 9f);

            var radius = UIFactory.CreateText(frame, $"{def.radiusMeters:0} m", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 7), new Vector2(52, 14));

            AllowanceLabel(frame, AirStrikeCatalog.BudgetKey(def.aircraft), def.missions);

            _airStrikeButtons.Add((def.aircraft, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshAirStrike()
        {
            if (_airStrike == null) return;
            foreach (var (aircraft, fill, label) in _airStrikeButtons)
            {
                bool on = _airStrike.Armed.HasValue && _airStrike.Armed.Value == aircraft;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- air supply section

        AirSupplySystem _airSupply;
        readonly List<(SupplyKind kind, Image fill, Text label)> _airSupplyButtons =
            new List<(SupplyKind, Image, Text)>();

        /// <summary>The load's own glyph — the same three the LOGISTICS panel uses.</summary>
        static Sprite SupplyGlyph(SupplyKind kind) => kind switch
        {
            SupplyKind.Ammo => UiIcons.Rounds,
            SupplyKind.Oil => UiIcons.FuelDrop,
            _ => UiIcons.MedicalCross
        };

        /// <summary>
        /// The airdrop menu, driven entirely from <see cref="AirSupplyCatalog"/>.
        ///
        /// **The one page in this dock that gives something.** It sits beside
        /// AIR STRIKE because the two are flown by the same kind of thing and
        /// tasked in exactly the same way — pick, place, wait, watch — and the
        /// pairing is the clearest way of saying that an aircraft overhead is
        /// not always bad news.
        ///
        /// The three loads carry the **same glyphs as the LOGISTICS panel's**
        /// ammunition, fuel and medical points, because that is precisely what a
        /// drop leaves on the ground: not an effect, a supply point that was not
        /// there before. See docs/29-AIR-SUPPLY.md.
        /// </summary>
        void BuildAirSupplySection(RectTransform content)
        {
            SectionLabel(content, "DROP SUPPLIES", -8);
            StrikeBudgetRow(content, -28f);

            float y = -64f;
            foreach (var def in AirSupplyCatalog.All)
            {
                AirSupplyButton(content, def, y);
                y -= 58f;
            }

            var abort = UIFactory.CreateBorderedPanel(content, "AbortSupply", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(abort, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var abortBtn = UIFactory.CreateButton(abort, "ABORT TASKING",
                () => { if (_airSupply != null) _airSupply.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)abortBtn.transform);

            var hint = UIFactory.CreateText(content,
                $"Pick a load, then click the map to place the drop zone. A " +
                $"{AirSupplyCatalog.CountdownSeconds:0} second countdown runs in the HUD, then a transport " +
                "runs in low and pushes its bundles out over the zone. Each canopy that lands leaves a " +
                "supply point on the map — the same object the LOGISTICS panel places by hand, with the " +
                "same icon, and removable the same way. The run-in heading is different every time.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 170));

            RefreshAirSupply();
        }

        void AirSupplyButton(RectTransform content, SupplyDropDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Supply_" + def.kind, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_airSupply != null) _airSupply.Toggle(def.kind); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, SupplyGlyph(def.kind), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                46f, InnerWidth - 92f, topInset: 9f);

            // Bundles, not a beaten zone: the figure that matters here is how
            // many supply points the mission leaves behind.
            var bundles = UIFactory.CreateText(frame, $"{def.bundles} bundles", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            bundles.raycastTarget = false;
            UIFactory.Place(bundles.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 7), new Vector2(60, 14));

            AllowanceLabel(frame, AirSupplyCatalog.BudgetKey(def.kind), def.missions);

            _airSupplyButtons.Add((def.kind, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshAirSupply()
        {
            if (_airSupply == null) return;
            foreach (var (kind, fill, label) in _airSupplyButtons)
            {
                bool on = _airSupply.Armed.HasValue && _airSupply.Armed.Value == kind;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // --------------------------------------------------- uav strike section

        UavStrikeSystem _uavStrike;
        readonly List<(UavType uav, Image fill, Text label)> _uavButtons =
            new List<(UavType, Image, Text)>();

        /// <summary>
        /// The unmanned menu. Kept separate from AIR STRIKE rather than folded
        /// into it, because what is being tasked is a different kind of thing: an
        /// airframe comes back and a loitering munition does not, so the two ask
        /// different questions of the player and are answered from different
        /// stocks. Driven entirely from <see cref="UavCatalog"/>.
        /// </summary>
        void BuildUavStrikeSection(RectTransform content)
        {
            SectionLabel(content, "TASK A UAV", -8);
            StrikeBudgetRow(content, -28f);

            float y = -64f;
            foreach (var def in UavCatalog.All)
            {
                UavButton(content, def, y);
                y -= 58f;
            }

            var abort = UIFactory.CreateBorderedPanel(content, "AbortUav", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(abort, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 32));
            var abortBtn = UIFactory.CreateButton(abort, "ABORT TASKING",
                () => { if (_uavStrike != null) _uavStrike.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)abortBtn.transform);

            var hint = UIFactory.CreateText(content,
                "Pick a type, then click the map to place the objective. A ten second countdown runs in the HUD, " +
                "then the drone launches and flies in.\n\n" +
                "The attack types are expended on the target — one aircraft, one warhead, and nothing comes back. " +
                "Their blast is deliberately the smallest of any strike here: a loitering munition carries a few " +
                "kilograms, not a shell.\n\n" +
                "The RECONNAISSANCE DRONE carries no warhead. The ring under the cursor is the 10 km it will " +
                "uncover; it holds an orbit over the point for five operational minutes, lifts the fog off " +
                "everything inside that circle, and flies home. What it saw stays on the map as last-known " +
                "contacts. Turn FOG OF WAR on in GENERAL and start the battle, or there is nothing for it to " +
                "uncover.\n\n" +
                "Each type has its own allowance — the second figure on its button. Every sortie, " +
                "armed or not, spends one of them, and running one type out does not touch the others.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 48f),
                new Vector2(InnerWidth, 300));

            RefreshUavStrike();
        }

        void UavButton(RectTransform content, UavDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Uav_" + def.uav, UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 52));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_uavStrike != null) _uavStrike.Toggle(def.uav); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            // The recon type gets its own glyph. It is the one row in this menu
            // that does not end in an explosion, and a quadcopter icon shared
            // with the loitering munitions would be the menu saying otherwise.
            var icon = UIFactory.CreateImage(frame, def.isRecon ? UiIcons.ReconEye : UiIcons.Quadcopter, "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(12, 0), new Vector2(24, 24));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                46f, InnerWidth - 92f, topInset: 9f);

            // Metres for a warhead's beaten zone; kilometres for a search area.
            // Ten thousand metres is a number nobody reads as ten kilometres.
            string figure = def.isRecon
                ? $"{def.reconRadiusKm:0} km"
                : def.radiusMeters.ToString("0") + " m";
            var radius = UIFactory.CreateText(frame, figure, UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 7), new Vector2(52, 14));

            AllowanceLabel(frame, UavCatalog.BudgetKey(def.uav), def.missions);

            _uavButtons.Add((def.uav, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshUavStrike()
        {
            if (_uavStrike == null) return;
            foreach (var (uav, fill, label) in _uavButtons)
            {
                bool on = _uavStrike.Armed.HasValue && _uavStrike.Armed.Value == uav;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }

        // -------------------------------------------------- navy strike section

        NavalStrikeSystem _naval;
        readonly List<(NavalGun gun, Image fill, Text label)> _navalButtons =
            new List<(NavalGun, Image, Text)>();
        readonly Dictionary<NavalOrigin, RectTransform> _navalPages =
            new Dictionary<NavalOrigin, RectTransform>();
        readonly List<(NavalOrigin origin, Image fill, Text label)> _navalTabs =
            new List<(NavalOrigin, Image, Text)>();
        NavalOrigin _navalOrigin = NavalOrigin.Nato;

        /// <summary>
        /// Glyph per gun. Chosen by weight, exactly as the artillery menu does:
        /// nine bespoke pictograms would be nine pictograms nobody could tell
        /// apart at 22 px, and what the player is choosing between is how heavy
        /// the shell is.
        /// </summary>
        static Sprite NavalGlyph(NavalGunDef def)
        {
            if (def.calibreMm <= 76) return UiIcons.ShellLight;
            if (def.calibreMm >= 127) return UiIcons.ShellHeavy;
            return UiIcons.ShellMedium;
        }

        /// <summary>
        /// Naval gunfire support, driven entirely from <see cref="NavalCatalog"/>.
        ///
        /// Same shape as the artillery menu — inventory tabs over a list of
        /// calibres — because it is the same decision made about a different
        /// kind of gun, and two fire menus that behaved differently would be two
        /// things to learn instead of one. The **fleets** split the list the way
        /// the artillery menu's inventories do: it is the first choice a player
        /// makes and it halves what they have to read.
        /// </summary>
        void BuildNavalStrikeSection(RectTransform content)
        {
            SectionLabel(content, "CALL FOR NAVAL GUNFIRE", -8);
            StrikeBudgetRow(content, -28f);

            BuildNavalTabs(content, -64f);

            foreach (NavalOrigin origin in System.Enum.GetValues(typeof(NavalOrigin)))
            {
                var page = UIFactory.CreateGroup(content, "NavyPage_" + origin);
                page.anchorMin = new Vector2(0, 0); page.anchorMax = new Vector2(1, 1);
                page.offsetMin = Vector2.zero; page.offsetMax = Vector2.zero;
                _navalPages[origin] = page;
                BuildNavalPage(page, origin);
            }

            ShowNavalOrigin(_navalOrigin);
            RefreshNavalStrike();
        }

        void BuildNavalTabs(RectTransform content, float y)
        {
            var origins = new[] { NavalOrigin.Nato, NavalOrigin.Enemy };
            var names = new[] { "NATO NAVY", "ENEMY NAVY" };
            float w = (InnerWidth - 6f) / 2f;

            for (int i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                var frame = UIFactory.CreateBorderedPanel(content, "NavyOrigin_" + names[i],
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 6f), y),
                    new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, names[i], () => ShowNavalOrigin(origin),
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontLabel);
                UIFactory.Stretch((RectTransform)btn.transform);

                _navalTabs.Add((origin, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        void BuildNavalPage(RectTransform page, NavalOrigin origin)
        {
            float y = -102f;

            foreach (var def in NavalCatalog.OfOrigin(origin))
            {
                NavalButton(page, def, y);
                y -= 50f;
            }

            var stop = UIFactory.CreateBorderedPanel(page, "CheckFire", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 6f), new Vector2(InnerWidth, 30));
            var stopBtn = UIFactory.CreateButton(stop, "CHECK FIRE",
                () => { if (_naval != null) _naval.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(page,
                "Pick a gun, then click the map to place the target area. The ring under the cursor is that " +
                "gun's beaten zone — it is wider than a land gun's of the same calibre, because the rounds " +
                "come from a moving ship at extreme range. A ten second countdown runs in the HUD, then the " +
                "mission lands: every round is resolved where it actually falls, and each leaves its own " +
                "burst, smoke and report.\n\n" +
                "Naval mountings are automatic, so a mission is more rounds, faster, than a battery's five. " +
                "The number on each button is the beaten zone; the round count is on the line beneath it.\n\n" +
                "A mission cannot be recalled once away — CHECK FIRE only stands the gun down. Each mounting " +
                "has its own allowance, shown as the second figure on its button.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 44f),
                new Vector2(InnerWidth, 250));
        }

        void ShowNavalOrigin(NavalOrigin origin)
        {
            _navalOrigin = origin;
            foreach (var kv in _navalPages) kv.Value.gameObject.SetActive(kv.Key == origin);

            foreach (var (o, fill, label) in _navalTabs)
            {
                bool on = o == origin;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
        }

        void NavalButton(RectTransform content, NavalGunDef def, float y)
        {
            var frame = UIFactory.CreateBorderedPanel(content, "Navy_" + def.gun,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(InnerWidth, 46));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_naval != null) _naval.Toggle(def.gun); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, NavalGlyph(def), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(22, 22));

            // The round count moves into the detail line. It is a fixed property
            // of the mounting — it never changes while you play — so it belongs
            // with the prose that describes the gun, and it frees the right-hand
            // column for the two figures that do change the decision: the beaten
            // zone and how many missions are left.
            var (name, _) = UIFactory.CreateStackedLabels(frame,
                def.label, $"{def.detail}  ·  {def.roundsPerMission} rds",
                40f, InnerWidth - 88f, topInset: 6f);

            var radius = UIFactory.CreateText(frame, $"{def.radiusMeters:0} m",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 6),
                new Vector2(52, 14));

            AllowanceLabel(frame, NavalCatalog.BudgetKey(def.gun), def.missions);

            _navalButtons.Add((def.gun, frame.Find("Fill").GetComponent<Image>(), name));
        }

        /// <summary>Repaints from the system's state — it owns what is armed, not the panel.</summary>
        void RefreshNavalStrike()
        {
            if (_naval == null) return;
            foreach (var (gun, fill, label) in _navalButtons)
            {
                bool on = _naval.Armed.HasValue && _naval.Armed.Value == gun;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }
    }
}
