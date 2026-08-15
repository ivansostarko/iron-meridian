using System;
using System.Collections.Generic;
using IronMeridian.Vfx;

namespace IronMeridian.Data
{
    /// <summary>One record in a catalogue, flattened to what a list needs.</summary>
    public class CatalogEntry
    {
        /// <summary>Stable id — a unit id, or an enum member name.</summary>
        public string id;
        public string name;
        /// <summary>One line under the name.</summary>
        public string detail;
        /// <summary>Second grouping line: branch, origin, role — whatever the table sorts by.</summary>
        public string group;
        /// <summary>The live record. Editing it edits the catalogue the game reads.</summary>
        public object record;
    }

    /// <summary>A whole data table, named and loadable.</summary>
    public class CatalogGroup
    {
        /// <summary>Key written into the tuning file. Never change one that has shipped.</summary>
        public string key;
        /// <summary>Tab caption.</summary>
        public string title;
        /// <summary>What this table is, one line.</summary>
        public string blurb;
        /// <summary>Docs page that documents it, for the screen's footer.</summary>
        public string doc;
        /// <summary>Fields the editor shows read-only — identity, not tuning.</summary>
        public string[] readOnlyFields = Array.Empty<string>();

        public Func<List<CatalogEntry>> Load;
    }

    /// <summary>
    /// The register of every data table the game is built from, in one place, so
    /// the DEVELOPMENT → UNITS LIST screen is driven by data rather than
    /// by six hand-written panels.
    ///
    /// Adding a weapon family means adding a row here as well as the catalogue
    /// itself — that is the point: a family missing from this list is a family
    /// nobody can inspect or tune.
    ///
    /// See docs/04-UNITS.md, 17-ARTILLERY, 18-AIR-STRIKES, 19-UAV-STRIKES,
    /// 20-MISSILE-SYSTEMS and 21-NAVAL-GUNFIRE.
    /// </summary>
    public static class GameCatalogs
    {
        public const string Units = "Units";
        public const string Artillery = "Artillery";
        public const string AirStrike = "AirStrike";
        public const string Uav = "Uav";
        public const string Missiles = "Missiles";
        public const string Naval = "Naval";

        static readonly string[] IdOnly = { "id" };

        public static readonly CatalogGroup[] All =
        {
            new CatalogGroup
            {
                key = Units, title = "UNITS",
                blurb = "Every formation type both sides can field. Values are given at company " +
                        "equivalent; echelon multipliers scale them.",
                doc = "docs/04-UNITS.md",
                readOnlyFields = IdOnly,
                Load = () =>
                {
                    var list = new List<CatalogEntry>(UnitDatabase.All.Count);
                    foreach (var d in UnitDatabase.All)
                        list.Add(new CatalogEntry
                        {
                            id = d.id,
                            name = d.name,
                            detail = d.description,
                            group = UnitBranchInfo.DisplayName(d.Branch),
                            record = d
                        });
                    return list;
                }
            },

            new CatalogGroup
            {
                key = Artillery, title = "ARTILLERY",
                blurb = "Called fire missions. One nature per calibre — what it sounds like, " +
                        "how wide it lands and what it does to a formation.",
                doc = "docs/17-ARTILLERY.md",
                readOnlyFields = new[] { "caliber" },
                Load = () =>
                {
                    var list = new List<CatalogEntry>();
                    foreach (var d in ArtilleryCatalog.All)
                        list.Add(new CatalogEntry
                        {
                            id = d.caliber.ToString(), name = d.name, detail = d.detail,
                            group = d.origin.ToString(), record = d
                        });
                    return list;
                }
            },

            new CatalogGroup
            {
                key = AirStrike, title = "AIR STRIKES",
                blurb = "Tasked airframes. How they fly the run, what they release and how " +
                        "much of it lands on the target.",
                doc = "docs/18-AIR-STRIKES.md",
                readOnlyFields = new[] { "aircraft" },
                Load = () =>
                {
                    var list = new List<CatalogEntry>();
                    foreach (var d in AirStrikeCatalog.All)
                        list.Add(new CatalogEntry
                        {
                            id = d.aircraft.ToString(), name = d.name, detail = d.detail,
                            group = "Airframe", record = d
                        });
                    return list;
                }
            },

            new CatalogGroup
            {
                key = Uav, title = "UAV STRIKES",
                blurb = "Unmanned systems, from a quadcopter dropping a grenade to a one-way " +
                        "Shahed. Deliberately the smallest warheads in the game.",
                doc = "docs/19-UAV-STRIKES.md",
                readOnlyFields = new[] { "uav" },
                Load = () =>
                {
                    var list = new List<CatalogEntry>();
                    foreach (var d in UavCatalog.All)
                        list.Add(new CatalogEntry
                        {
                            id = d.uav.ToString(), name = d.name, detail = d.detail,
                            group = "UAV", record = d
                        });
                    return list;
                }
            },

            new CatalogGroup
            {
                key = Missiles, title = "MISSILES",
                blurb = "Missile systems by origin, role and weight — interceptors through to " +
                        "the heaviest ballistic warheads.",
                doc = "docs/20-MISSILE-SYSTEMS.md",
                readOnlyFields = IdOnly,
                Load = () =>
                {
                    var list = new List<CatalogEntry>();
                    foreach (var d in MissileCatalog.All)
                        list.Add(new CatalogEntry
                        {
                            id = d.id.ToString(), name = d.name, detail = d.detail,
                            group = $"{d.origin} · {d.role}", record = d
                        });
                    return list;
                }
            },

            new CatalogGroup
            {
                key = Naval, title = "NAVAL GUNS",
                blurb = "Naval gunfire support. Calibre-matched to the artillery natures, " +
                        "fired from off the map.",
                doc = "docs/21-NAVAL-GUNFIRE.md",
                readOnlyFields = new[] { "gun" },
                Load = () =>
                {
                    var list = new List<CatalogEntry>();
                    foreach (var d in NavalCatalog.All)
                        list.Add(new CatalogEntry
                        {
                            id = d.gun.ToString(), name = d.name, detail = d.detail,
                            group = d.origin.ToString(), record = d
                        });
                    return list;
                }
            }
        };

        public static CatalogGroup Get(string key)
        {
            foreach (var g in All) if (g.key == key) return g;
            return null;
        }

        /// <summary>
        /// Touches every catalogue so its tuning overrides are applied and its
        /// baselines captured. Called by the tuning screen before it draws, and
        /// by the reset path, which needs a baseline for records the player has
        /// never opened.
        /// </summary>
        public static void EnsureAllLoaded()
        {
            foreach (var g in All) g.Load();
        }

        /// <summary>
        /// Puts every record in every catalogue back to the values it shipped
        /// with, and deletes the override file. Returns how many records were
        /// carrying an override.
        /// </summary>
        public static int ResetAll()
        {
            EnsureAllLoaded();

            int reverted = 0;
            foreach (var g in All)
                foreach (var e in g.Load())
                    if (Save.TuningStore.Revert(g.key, e.id, e.record)) reverted++;

            Save.TuningStore.Clear();
            return reverted;
        }
    }
}
