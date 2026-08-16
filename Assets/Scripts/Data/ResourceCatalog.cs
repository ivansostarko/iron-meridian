using UnityEngine;

namespace IronMeridian.Data
{
    /// <summary>
    /// The stocks a force fights on. Ordered by how quickly running out of one
    /// stops the fight: fuel and ammunition first, then the people, then the
    /// things that keep both going.
    /// </summary>
    public enum ResourceKind
    {
        Fuel,
        LightAmmo,
        TankAmmo,
        ArtilleryAmmo,
        AirDefenceMissiles,
        Manpower,
        Rations,
        MedicalSupplies,
        SpareParts
    }

    /// <summary>
    /// Which class of ammunition a formation eats. Derived from what the
    /// formation *is* rather than stored on it — see
    /// <see cref="ResourceCatalog.AmmoClassOf"/>.
    /// </summary>
    public enum AmmoClass { Light, Tank, Artillery, AirDefence }

    /// <summary>One stock line: what it is, what it is counted in, how it is spent.</summary>
    public readonly struct ResourceDef
    {
        public readonly ResourceKind kind;
        /// <summary>Name on the panel row.</summary>
        public readonly string name;
        /// <summary>Unit of measure — the word after the number.</summary>
        public readonly string measure;
        /// <summary>What running out of it means, in one line.</summary>
        public readonly string detail;
        public readonly Color tint;

        public ResourceDef(ResourceKind kind, string name, string measure, string detail, Color tint)
        {
            this.kind = kind;
            this.name = name;
            this.measure = measure;
            this.detail = detail;
            this.tint = tint;
        }
    }

    /// <summary>
    /// The sustainment register: the nine stocks a side is tracked on, and the
    /// rules that turn the force on the map into a daily consumption figure.
    ///
    /// **Why consumption is derived rather than stored.** A stock figure a
    /// designer types is a decision; a burn rate is an arithmetic consequence of
    /// what is deployed, and asking anybody to keep the two in step by hand
    /// would guarantee they disagree. Every rate here is read off the units
    /// actually on the map — through the same <c>UnitDefinition</c> sustainment
    /// fields the unit catalogue already carries (<c>fuelUsePerKm</c>,
    /// <c>ammoStock</c>, <c>manpower</c>, <c>supplyUsePerDay</c>) scaled by
    /// echelon and by current strength — so deploying a tank brigade changes the
    /// fuel line without anybody touching it.
    ///
    /// **The rates are a model, not a claim about a real army.** They are chosen
    /// to be legible and to move in the right direction: a formation at half
    /// strength eats about half as much, armour drinks fuel and infantry does
    /// not, artillery is the only thing that consumes artillery ammunition. See
    /// docs/27-SUSTAINMENT.md.
    /// </summary>
    public static class ResourceCatalog
    {
        /// <summary>Declaration order is the order the panel lists them in.</summary>
        public static readonly ResourceDef[] All =
        {
            new ResourceDef(ResourceKind.Fuel, "FUEL", "litres",
                "Vehicles stop moving", new Color(1.00f, 0.72f, 0.28f)),
            new ResourceDef(ResourceKind.LightAmmo, "LIGHT AMMUNITION", "rounds",
                "Small arms and autocannon", new Color(0.85f, 0.85f, 0.55f)),
            new ResourceDef(ResourceKind.TankAmmo, "TANK AMMUNITION", "rounds",
                "Main gun natures", new Color(1.00f, 0.55f, 0.30f)),
            new ResourceDef(ResourceKind.ArtilleryAmmo, "ARTILLERY AMMUNITION", "rounds",
                "Guns, mortars and rockets", new Color(0.95f, 0.42f, 0.30f)),
            new ResourceDef(ResourceKind.AirDefenceMissiles, "AIR DEFENCE MISSILES", "missiles",
                "The sky stops being contested", new Color(0.55f, 0.80f, 1.00f)),
            new ResourceDef(ResourceKind.Manpower, "MANPOWER", "personnel",
                "Replacements for casualties", new Color(0.60f, 0.85f, 0.65f)),
            new ResourceDef(ResourceKind.Rations, "RATIONS", "man-days",
                "What the force eats", new Color(0.75f, 0.80f, 0.60f)),
            new ResourceDef(ResourceKind.MedicalSupplies, "MEDICAL SUPPLIES", "units",
                "Casualties stop being treatable", new Color(0.95f, 0.45f, 0.48f)),
            new ResourceDef(ResourceKind.SpareParts, "SPARE PARTS", "units",
                "Damaged vehicles stay damaged", new Color(0.75f, 0.78f, 0.85f))
        };

        public static ResourceDef Get(ResourceKind kind)
        {
            foreach (var d in All) if (d.kind == kind) return d;
            return All[0];
        }

        public static ResourceKind Parse(string name) =>
            System.Enum.TryParse(name, out ResourceKind kind) ? kind : ResourceKind.Fuel;

        // ------------------------------------------------------- consumption

        /// <summary>
        /// Hours a formation is assumed to be moving on an operational day.
        /// Not twenty-four: a day of operations is a few hours of movement, some
        /// fighting and a lot of waiting, and fuel figures built on a formation
        /// driving round the clock are wrong by an order of magnitude.
        /// </summary>
        public const float MoveHoursPerDay = 6f;

        /// <summary>
        /// Share of a basic load fired in a day of operations. A formation
        /// carries roughly a day's fighting, and a day of *operations* is not a
        /// day of fighting.
        /// </summary>
        public const float AmmoLoadsPerDay = 0.35f;

        /// <summary>Litres of fuel a day per person, for everything that is not a fighting vehicle.</summary>
        public const float FuelPerPersonPerDay = 1.5f;

        /// <summary>Medical stores consumed per thousand personnel per day.</summary>
        public const float MedicalPerThousandPerDay = 12f;

        /// <summary>Spare parts consumed per formation per day, scaled by echelon.</summary>
        public const float PartsPerCompanyPerDay = 2.5f;

        /// <summary>
        /// Replacements needed per thousand personnel per day, in a force in
        /// contact. Manpower is the one line that is *not* a rate the force
        /// chooses — it is what the enemy is doing to it — so this is a planning
        /// figure rather than a measurement.
        /// </summary>
        public const float ReplacementsPerThousandPerDay = 8f;

        /// <summary>
        /// Which ammunition a formation eats, from what it is rather than from a
        /// field somebody has to remember to set. Indirect fire is artillery
        /// whatever the calibre; armour is the only thing that fires tank
        /// natures; a battery that shoots at aircraft is on missiles.
        /// </summary>
        public static AmmoClass AmmoClassOf(UnitDefinition def)
        {
            if (def == null) return AmmoClass.Light;
            if (def.canIndirectFire) return AmmoClass.Artillery;
            if (def.Branch == UnitBranch.AntiAircraft) return AmmoClass.AirDefence;
            if (def.Branch == UnitBranch.Armour) return AmmoClass.Tank;
            return AmmoClass.Light;
        }
    }
}
