using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IronMeridian.Data
{
    /// <summary>
    /// Static description of a unit type, loaded from StreamingAssets/Data/units.json.
    /// Values are given at COMPANY equivalent; echelon multipliers scale them.
    /// </summary>
    [Serializable]
    public class UnitDefinition
    {
        public string id;               // stable slug, also the icon file name
        public string name;             // display name
        public string category;         // "CoreGround" | "Drone" | "Air" | "Naval" — behaviour
        public string branch;           // "Infantry" | "Armour" | ... — arm of service, display only
        public string description;

        // Combat
        public float attack;            // soft attack power
        public float hardAttack;        // effectiveness vs armour
        public float defence;
        public float armour;            // 0..100 protection
        public float antiAir;           // effectiveness vs UAS / air
        public float weaponRangeKm;     // engagement range
        public float viewRangeKm;       // spotting range

        // People & readiness
        public int manpower;            // company-level personnel
        public float training;          // 0..100
        public float morale;            // 0..100
        public float organisation;      // 0..100, how fast it recovers

        // Mobility
        public float speedKmh;

        // Sustainment
        public string ammoType;         // e.g. "5.56mm NATO", "155mm HE"
        public int ammoStock;           // rounds / munitions carried
        public float fuelStock;         // litres carried
        public float fuelUsePerKm;      // litres per km (0 for foot units)
        public int foodDays;            // days of rations carried
        public float supplyUsePerDay;   // abstract supply consumption

        // Special capabilities
        public bool canIndirectFire;
        public bool canCounterUas;
        public bool isSupport;          // support units fight poorly alone

        /// <summary>
        /// How this type behaves. Anything unrecognised falls back to
        /// CoreGround — a unit that stands on the map and can be shot at is the
        /// safe reading of a typo, where treating it as an aircraft would make
        /// it invisible to half the game.
        /// </summary>
        public UnitCategory Category => category switch
        {
            "Drone" => UnitCategory.Drone,
            "Air" => UnitCategory.Air,
            "Naval" => UnitCategory.Naval,
            _ => UnitCategory.CoreGround
        };

        /// <summary>
        /// Arm of service. Display only — see <see cref="UnitBranch"/>. Unknown
        /// or absent values read as Other, which is what an uncategorised unit
        /// honestly is.
        /// </summary>
        /// <remarks>
        /// Parsed once and kept. The palette walks all 117 definitions once per
        /// branch to group its list, so a property that re-parsed the string
        /// would do four figures of <c>Enum.TryParse</c> on every keystroke in
        /// the search box. Definitions are shared singletons owned by
        /// <see cref="UnitDatabase"/> and the game is single-threaded, so the
        /// cache is safe; <c>NonSerialized</c> keeps it out of JsonUtility's way.
        /// </remarks>
        [NonSerialized] UnitBranch? _branch;

        public UnitBranch Branch
        {
            get
            {
                _branch ??= Enum.TryParse(branch, out UnitBranch b) ? b : UnitBranch.Other;
                return _branch.Value;
            }
        }

        /// <summary>
        /// Drops the parsed-<see cref="branch"/> cache. Anything that writes to
        /// <see cref="branch"/> after load — the tuning layer, and the
        /// DEVELOPMENT screen's editor — must call this, or the record keeps
        /// reporting the arm of service it had when it was first read.
        /// </summary>
        public void RefreshDerived() => _branch = null;

        /// <summary>True for types that stand on the ground and can take terrain.</summary>
        public bool HoldsGround => Category == UnitCategory.CoreGround;

        /// <summary>Composite combat power at a given echelon and strength (0..1).</summary>
        public float PowerAt(Echelon echelon, float strength01)
        {
            float ech = EchelonInfo.ManpowerMultiplier(echelon);
            float quality = (training * 0.6f + morale * 0.4f) / 100f;
            float basePower = (attack + defence * 0.8f + hardAttack * 0.5f + antiAir * 0.3f);
            return basePower * ech * Mathf.Clamp01(strength01) * (0.5f + quality);
        }
    }

    [Serializable]
    public class UnitDatabaseFile
    {
        public List<UnitDefinition> units = new List<UnitDefinition>();
    }

    /// <summary>Loads and indexes unit definitions. Same catalogue is used by both teams.</summary>
    public static class UnitDatabase
    {
        static Dictionary<string, UnitDefinition> _byId;
        static List<UnitDefinition> _all;

        public static IReadOnlyList<UnitDefinition> All { get { EnsureLoaded(); return _all; } }

        public static UnitDefinition Get(string id)
        {
            EnsureLoaded();
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        static void EnsureLoaded()
        {
            if (_all != null) return;
            // Through StreamingAssetsFile: on Android this lives inside the
            // APK and System.IO cannot open it — docs/40-ANDROID.md.
            string json = Core.StreamingAssetsFile.ReadAllText("Data/units.json");
            var file = string.IsNullOrEmpty(json)
                ? null : JsonUtility.FromJson<UnitDatabaseFile>(json);

            if (file == null || file.units == null || file.units.Count == 0)
            {
                // Nothing else in the game works without this, so it is the one
                // read here that is fatal enough to say so loudly. An empty list
                // still gets built, so callers get "no such unit type" rather
                // than a null reference on every lookup.
                Debug.LogError("[UnitDatabase] units.json could not be read. " +
                               "No unit types are available.");
                _all = new List<UnitDefinition>();
                _byId = new Dictionary<string, UnitDefinition>();
                return;
            }

            _all = file.units;
            _byId = new Dictionary<string, UnitDefinition>();
            foreach (var u in _all) _byId[u.id] = u;

            // The player's own tuning goes on last, over the generated file, so
            // regenerating units.json never silently discards it and the values
            // the game fights with are the ones the DEVELOPMENT screen shows.
            // See Save.TuningStore and docs/04-UNITS.md.
            foreach (var u in _all)
            {
                Save.TuningStore.Apply(GameCatalogs.Units, u.id, u);
                u.RefreshDerived();
            }

            Debug.Log($"[UnitDatabase] Loaded {_all.Count} unit definitions.");
        }
    }
}
