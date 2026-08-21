using UnityEngine;

namespace IronMeridian.Models
{
    /// <summary>
    /// <see cref="ProceduralModels"/> — the six **logistic installations**, built
    /// in code for the same reason the drones and the airlifter are: a rear area
    /// must not be able to lose its depot to an asset pack somebody removed.
    ///
    /// **Why an installation gets a model at all.** A depot used to be a symbol
    /// and nothing else, which was the right call while the map had no models on
    /// it. Now that GENERAL → SHOW UNIT 3D MODELS stands every formation up on
    /// the ground, a rear area drawn only as flat overlay reads as the one part
    /// of the battlefield that is not really there — you fly the camera down to
    /// a supply point that supplies a brigade and find a decal. The counter does
    /// not go away: the symbol still rides above the model, exactly as it does
    /// over a formation, so the installation is identifiable from a distance at
    /// which the model is a smudge. See docs/09-3D-MODELS.md §3.
    ///
    /// **Six installations, six silhouettes.** The same rule the glyphs follow
    /// (docs/26-LOGISTICS.md §2): what survives being small is *shape*, so the
    /// six are deliberately different masses rather than the same shed painted
    /// six ways — a pitched-roof warehouse, open canopies over pallets, a
    /// bunded tank farm, revetted ammunition bays, a gantry over a stripped
    /// hull, and a pair of hospital tents under a red cross.
    ///
    /// **Authored ground-up.** Every model has its origin on the pad at
    /// <c>y = 0</c> and grows in <c>+Y</c>, because <see cref="Logistics.LogisticsSite"/>
    /// stands them on the sampled terrain with local up = geodetic up. Sizes are
    /// roughly life-like in metres; the caller normalises from the model's own
    /// bounds, so what the figures buy is honest *proportion*, not scale.
    ///
    /// **Static, not animated.** A depot is a place. The airframes rock and spin
    /// because a rigid object flying in a straight line reads as a prop; a shed
    /// that swayed would read as an earthquake. Their <see cref="UnitModelDef"/>
    /// rows carry <c>idleClip = null</c> to match.
    /// </summary>
    public static partial class ProceduralModels
    {
        // ------------------------------------------------------------ the ids

        public const string SupplyDepotSite = "supply_depot_site";
        public const string SupplyPointSite = "supply_point_site";
        public const string FuelPointSite = "fuel_point_site";
        public const string AmmoPointSite = "ammo_point_site";
        public const string RepairPointSite = "repair_point_site";
        public const string MedicalPointSite = "medical_point_site";

        /// <summary>
        /// Builds one installation, or returns null if the id is not one of
        /// these six. Called from <see cref="Build"/>'s default arm, so the
        /// airframes above stay unaware of it.
        /// </summary>
        static GameObject BuildLogisticsSite(string proceduralId) => proceduralId switch
        {
            SupplyDepotSite => BuildSupplyDepot(),
            SupplyPointSite => BuildSupplyPoint(),
            FuelPointSite => BuildFuelPoint(),
            AmmoPointSite => BuildAmmoPoint(),
            RepairPointSite => BuildRepairPoint(),
            MedicalPointSite => BuildMedicalPoint(),
            _ => null
        };

        // ------------------------------------------------------------ palette
        //
        // Deliberately **side-neutral**. The owning side is carried by the
        // ground ring and the marker plate, which are repainted the moment a
        // site changes hands; painting the buildings blue or red as well would
        // put the same fact in two places and make a captured depot need a
        // rebuild to say so.

        /// <summary>Hardstanding: the graded pad every installation stands on.</summary>
        static readonly Color Pad = new Color(0.26f, 0.26f, 0.25f);
        /// <summary>Structure: shed walls, posts, gantries.</summary>
        static readonly Color Structure = new Color(0.44f, 0.45f, 0.42f);
        /// <summary>Roofing and tarpaulin — a shade up from the walls so a roof reads from above.</summary>
        static readonly Color Roof = new Color(0.33f, 0.36f, 0.34f);
        /// <summary>Spoil and revetment: the earth pushed up round a bay.</summary>
        static readonly Color Earth = new Color(0.36f, 0.31f, 0.23f);
        /// <summary>Olive stores — crates, pallets, containers.</summary>
        static readonly Color Stores = new Color(0.36f, 0.39f, 0.26f);
        /// <summary>Bare steel: tanks, drums, hoists.</summary>
        static readonly Color Steel = new Color(0.55f, 0.57f, 0.58f);
        /// <summary>A doorway or an open bay mouth — the dark that gives a wall depth.</summary>
        static readonly Color Opening = new Color(0.10f, 0.11f, 0.11f);
        /// <summary>Hazard banding on fuel and ammunition. The one warning colour here.</summary>
        static readonly Color Hazard = new Color(0.85f, 0.58f, 0.16f);
        /// <summary>Tentage. Pale, because a field hospital is not trying to hide.</summary>
        static readonly Color Tent = new Color(0.72f, 0.71f, 0.66f);
        /// <summary>The cross. Nothing else on this map is this colour.</summary>
        static readonly Color Aid = new Color(0.82f, 0.16f, 0.16f);

        // ------------------------------------------------------- supply depot

        /// <summary>
        /// SUPPLY DEPOT — the strategic one, and the only installation here that
        /// is a **building**.
        ///
        /// Everything forward of it is canopies, revetments and tents that could
        /// be struck in an afternoon; a depot is a warehouse with a pitched roof,
        /// a loading dock, a container park and a hardstanding wide enough to
        /// turn a lorry on. That difference is the whole point of the pair — see
        /// <see cref="BuildSupplyPoint"/>, which is deliberately the same
        /// function drawn as something that packed up this morning.
        /// </summary>
        static GameObject BuildSupplyDepot()
        {
            var root = new GameObject("SupplyDepot_Procedural");

            Apron(root, 40f, 26f);

            // The shed. Walls, then a pitched roof over them — a flat-roofed box
            // is a container, and the silhouette has to say "building".
            Box(root, "Shed", new Vector3(-3f, 3.6f, 0f), new Vector3(24f, 7.2f, 15f), Structure);
            PitchedRoof(root, "ShedRoof", new Vector3(-3f, 7.2f, 0f), 24.8f, 15.6f, 3.4f);

            // Roller doors down the long side, facing the apron.
            for (int i = -1; i <= 1; i++)
                Box(root, $"Door{i + 1}", new Vector3(-3f + i * 7.5f, 2.4f, 7.6f),
                    new Vector3(4.4f, 4.8f, 0.3f), Opening);

            // Loading dock: a raised lip along the doors, so the lorries have
            // something to back onto and the wall has a base to sit on.
            Box(root, "Dock", new Vector3(-3f, 0.7f, 9.6f), new Vector3(25f, 1.4f, 4.4f), Pad);

            // Container park to one flank — the stock that will not fit inside.
            for (int i = 0; i < 3; i++)
            {
                float z = -5.5f + i * 5.5f;
                Box(root, $"Container{i}", new Vector3(14f, 1.4f, z), new Vector3(9f, 2.8f, 3.0f), Stores);
                Box(root, $"ContainerLid{i}", new Vector3(14f, 2.95f, z), new Vector3(9.2f, 0.3f, 3.2f), Roof);
            }
            // A second tier on the middle stack. A container park stacks; a flat
            // row of three reads as three sheds.
            Box(root, "ContainerTop", new Vector3(14f, 4.3f, 0f), new Vector3(9f, 2.8f, 3.0f), Stores);
            Box(root, "ContainerTopLid", new Vector3(14f, 5.85f, 0f), new Vector3(9.2f, 0.3f, 3.2f), Roof);

            // Mast at the gate. Cheap, and it gives the mass a vertical to be
            // read against — without it the whole model is one horizontal slab.
            Box(root, "Mast", new Vector3(-16f, 5.5f, 10f), new Vector3(0.4f, 11f, 0.4f), Steel);

            Bollards(root, 17f, 11.5f);
            return root;
        }

        // ------------------------------------------------------- supply point

        /// <summary>
        /// SUPPLY POINT — the forward one: pallets under canopies on a graded
        /// pad, and nothing that could not be moved by last light.
        ///
        /// Drawn as the **opposite of the depot** on purpose. Both hand out
        /// everything, and the only thing that distinguishes them on the map is
        /// how far they reach and how permanent they look — so one is a
        /// warehouse and the other is two tarpaulins and a stack of boxes.
        /// </summary>
        static GameObject BuildSupplyPoint()
        {
            var root = new GameObject("SupplyPoint_Procedural");

            Apron(root, 30f, 20f);

            // Two open-sided canopies, each over a pallet stack.
            for (int bay = 0; bay < 2; bay++)
            {
                float x = bay == 0 ? -7f : 7f;
                Shelter(root, $"Canopy{bay}", new Vector3(x, 0f, 0f), 12f, 9f, 4.4f);
                PalletStack(root, $"Stack{bay}A", new Vector3(x - 2.6f, 0f, -1.8f), 2);
                PalletStack(root, $"Stack{bay}B", new Vector3(x + 2.6f, 0f, 1.8f), 3);
            }

            // Loose stores between the bays, where the working party is.
            PalletStack(root, "StackC", new Vector3(0f, 0f, 6.5f), 1);
            Box(root, "Drums", new Vector3(0f, 0.6f, -6.5f), new Vector3(3.2f, 1.2f, 1.6f), Stores);

            Bollards(root, 12.5f, 8.5f);
            return root;
        }

        // --------------------------------------------------------- fuel point

        /// <summary>
        /// FUEL POINT — a bunded tank farm with a dispensing gantry.
        ///
        /// The **bund** is the silhouette: an earth wall enclosing the tank,
        /// which is what fuel is actually stored behind and what tells this
        /// apart from the ammunition point's bays at a glance — one wall round
        /// one big cylinder, against several walls round several small stacks.
        /// </summary>
        static GameObject BuildFuelPoint()
        {
            var root = new GameObject("FuelPoint_Procedural");

            Apron(root, 30f, 22f);

            // The bund: four low earth walls round the tank standing in them.
            Bund(root, new Vector3(-4f, 0f, 0f), 13f, 9.5f, 2.2f, 1.4f);

            // Bulk tank, lying on its side on saddles. The one big curved mass
            // on this map's rear area, and the reason the fuel point reads at
            // distance without its glyph.
            var tank = Cylinder(root, "Tank", new Vector3(-4f, 3.6f, 0f),
                new Vector3(3.4f, 6.5f, 3.4f), Steel);
            tank.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // Under the tank's own span (x -7.25 .. -0.75), not beyond it: a
            // saddle outside the cylinder it carries is a post in a field.
            Box(root, "SaddleA", new Vector3(-6.5f, 1.1f, 0f), new Vector3(1.2f, 2.2f, 4.2f), Structure);
            Box(root, "SaddleB", new Vector3(-1.5f, 1.1f, 0f), new Vector3(1.2f, 2.2f, 4.2f), Structure);
            // Hazard band round the tank's waist.
            var band = Cylinder(root, "TankBand", new Vector3(-4f, 3.6f, 0f),
                new Vector3(3.5f, 0.5f, 3.5f), Hazard);
            band.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Dispensing gantry on the apron side: two posts, a header, and the
            // hoses hanging off it.
            Box(root, "GantryPostL", new Vector3(9f, 2.6f, -3.5f), new Vector3(0.5f, 5.2f, 0.5f), Steel);
            Box(root, "GantryPostR", new Vector3(9f, 2.6f, 3.5f), new Vector3(0.5f, 5.2f, 0.5f), Steel);
            Box(root, "GantryHeader", new Vector3(9f, 5.4f, 0f), new Vector3(0.6f, 0.6f, 8.0f), Steel);
            for (int i = -1; i <= 1; i += 2)
                Box(root, $"Hose{i + 1}", new Vector3(9f, 4.0f, i * 2.2f), new Vector3(0.22f, 2.4f, 0.22f), Opening);

            // Drums on the pad — the issue point, as opposed to the bulk store.
            for (int i = 0; i < 4; i++)
            {
                float z = -4.5f + i * 3f;
                Cylinder(root, $"Drum{i}", new Vector3(12.5f, 0.9f, z),
                    new Vector3(1.5f, 0.9f, 1.5f), Hazard);
            }

            return root;
        }

        // --------------------------------------------------------- ammo point

        /// <summary>
        /// AMMO POINT — revetted bays, separated.
        ///
        /// **Separation is the model.** Ammunition is stored in small lots
        /// behind earth walls with distance between them precisely so one hit is
        /// not all of it, and drawing three walled bays rather than one big shed
        /// says what an ammunition point *is* more clearly than any amount of
        /// detail on the crates inside it.
        /// </summary>
        static GameObject BuildAmmoPoint()
        {
            var root = new GameObject("AmmoPoint_Procedural");

            Apron(root, 38f, 22f);

            // 11 m apart, against an outer width of 10.2 m a bay: the gap
            // between them is the whole point of a revetted ammunition area and
            // at 10 m spacing the walls met, turning three bays into one shed.
            for (int bay = 0; bay < 3; bay++)
            {
                float x = -11f + bay * 11f;

                // Three walls in a U, open toward the apron: the traverse across
                // the back and a wall down each side.
                Box(root, $"Bay{bay}Back", new Vector3(x, 1.4f, -5.2f), new Vector3(8.4f, 2.8f, 1.8f), Earth);
                Box(root, $"Bay{bay}Left", new Vector3(x - 4.2f, 1.4f, -1.4f), new Vector3(1.8f, 2.8f, 8.6f), Earth);
                Box(root, $"Bay{bay}Right", new Vector3(x + 4.2f, 1.4f, -1.4f), new Vector3(1.8f, 2.8f, 8.6f), Earth);

                // The lot inside it: crates, and rounds standing on their bases.
                PalletStack(root, $"Bay{bay}Lot", new Vector3(x, 0f, -2.4f), 2);
                for (int i = 0; i < 3; i++)
                {
                    float z = -0.4f + i * 1.3f;
                    Cylinder(root, $"Bay{bay}Round{i}", new Vector3(x + 2.6f, 0.85f, z),
                        new Vector3(0.55f, 0.85f, 0.55f), Hazard);
                    // The nose hangs off the root rather than off the case: the
                    // case is a scaled primitive, and a child of it would be
                    // stretched by that scale into a spike. It starts at the
                    // case's top (centre 0.85 + half of 0.85), not above it.
                    var nose = Cone(root, $"Bay{bay}Nose{i}", new Vector3(x + 2.6f, 1.27f, z),
                        0.27f, 0.55f, Hazard);
                    nose.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                }
            }

            Bollards(root, 16f, 9.5f);
            return root;
        }

        // ------------------------------------------------------- repair point

        /// <summary>
        /// REPAIR POINT — a gantry over a stripped hull.
        ///
        /// The **gantry** is what makes it read: an A-frame with a block hanging
        /// off it is the universal shape for "this is where things are taken
        /// apart", and it is the one mass on this map's rear area that is mostly
        /// air. Under it sits a hull with its turret lifted clear, which is the
        /// other half of the sentence.
        /// </summary>
        static GameObject BuildRepairPoint()
        {
            var root = new GameObject("RepairPoint_Procedural");

            Apron(root, 28f, 20f);

            // Workshop shelter to one side: posts under a flat roof, open all round.
            Shelter(root, "Workshop", new Vector3(-8f, 0f, 0f), 11f, 12f, 5.0f);
            Box(root, "Bench", new Vector3(-8f, 0.9f, -4.4f), new Vector3(9f, 1.8f, 1.2f), Structure);
            PalletStack(root, "Spares", new Vector3(-11.5f, 0f, 3.5f), 2);

            // The gantry: two A-frames and a runway beam between them.
            for (int i = -1; i <= 1; i += 2)
            {
                float z = i * 4.5f;
                // Feet 6.4 m apart, meeting at one apex over the hull. Both legs
                // starting from the same foot would be a Λ, not an A — and an
                // A-frame that a vehicle cannot be driven under is not one.
                Leg(root, $"LegOut{(i + 1) / 2}", new Vector3(6.5f - 3.2f, 0f, z), new Vector3(3.2f, 6.6f, 0f));
                Leg(root, $"LegIn{(i + 1) / 2}", new Vector3(6.5f + 3.2f, 0f, z), new Vector3(-3.2f, 6.6f, 0f));
            }
            Box(root, "Runway", new Vector3(6.5f, 6.8f, 0f), new Vector3(0.7f, 0.7f, 10.4f), Steel);

            // Hoist: a block on a cable, and the turret it has lifted off.
            Box(root, "Cable", new Vector3(6.5f, 5.4f, 1.5f), new Vector3(0.14f, 3.2f, 0.14f), Opening);
            Box(root, "Block", new Vector3(6.5f, 3.6f, 1.5f), new Vector3(1.0f, 0.9f, 1.0f), Steel);
            Box(root, "Turret", new Vector3(6.5f, 2.4f, 1.5f), new Vector3(3.2f, 1.5f, 3.6f), Stores);

            // The hull it came off, on stands, with a road wheel beside it.
            Box(root, "Hull", new Vector3(6.5f, 1.3f, -3.4f), new Vector3(4.0f, 1.6f, 7.4f), Stores);
            Box(root, "StandA", new Vector3(5.0f, 0.3f, -6.2f), new Vector3(0.8f, 0.6f, 0.8f), Structure);
            Box(root, "StandB", new Vector3(8.0f, 0.3f, -6.2f), new Vector3(0.8f, 0.6f, 0.8f), Structure);
            var wheel = Cylinder(root, "Wheel", new Vector3(2.6f, 0.9f, -6.6f),
                new Vector3(1.8f, 0.35f, 1.8f), Opening);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            return root;
        }

        // ------------------------------------------------------ medical point

        /// <summary>
        /// MEDICAL POINT — two hospital tents under a cross, with a landing
        /// point beside them.
        ///
        /// **The cross is on the roof, not on a sign.** It is on the roof of a
        /// real one for the same reason it is here: the only angle that matters
        /// is from above. And the tents are *round*, which nothing else in this
        /// set is — a curved mass among six boxy ones is the cheapest way to
        /// make one installation unmistakable from any angle.
        /// </summary>
        static GameObject BuildMedicalPoint()
        {
            var root = new GameObject("MedicalPoint_Procedural");

            Apron(root, 30f, 22f);

            // Two tents, half-cylinders lying along Z. Sunk to the axis so what
            // is above the pad is the arch — the buried half is under the
            // terrain and never seen.
            // Diameter 4.4 (so the crown is at y = 2.2) and 7 m long (so the
            // ends are at z = ±3.5). Everything below is placed against those
            // two figures.
            const float TentRadius = 2.2f, TentHalfLength = 3.5f;

            for (int i = 0; i < 2; i++)
            {
                float x = -6f + i * 12f;
                var tent = Cylinder(root, $"Tent{i}", new Vector3(x, 0f, 0f),
                    new Vector3(TentRadius * 2f, TentHalfLength * 2f, TentRadius * 2f), Tent);
                tent.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // End walls, so the arch reads as enclosed rather than as a pipe.
                for (int end = -1; end <= 1; end += 2)
                    Box(root, $"TentEnd{(end < 0 ? "A" : "B")}{i}",
                        new Vector3(x, TentRadius * 0.5f, end * TentHalfLength),
                        new Vector3(TentRadius * 1.95f, TentRadius, 0.3f), Tent);

                // The cross on the crown, laid flat along the roof — which is
                // the only angle that matters.
                Box(root, $"CrossBar{i}", new Vector3(x, TentRadius + 0.05f, 0f),
                    new Vector3(3.4f, 0.18f, 1.1f), Aid);
                Box(root, $"CrossPost{i}", new Vector3(x, TentRadius + 0.05f, 0f),
                    new Vector3(1.1f, 0.18f, 3.4f), Aid);
            }

            // Connecting corridor between the two tents, kept under their crown.
            Box(root, "Corridor", new Vector3(0f, 0.9f, 0f), new Vector3(8f, 1.8f, 3.0f), Tent);

            // Ambulance on the pad — a box body with the same cross on its flank.
            Box(root, "AmbulanceBody", new Vector3(0f, 1.5f, 8.6f), new Vector3(3.0f, 2.4f, 6.2f), Tent);
            Box(root, "AmbulanceCab", new Vector3(0f, 1.1f, 12.2f), new Vector3(2.8f, 1.8f, 1.6f), Structure);
            Box(root, "AmbulanceCross", new Vector3(1.55f, 1.7f, 8.6f), new Vector3(0.1f, 1.4f, 0.4f), Aid);
            Box(root, "AmbulanceCrossBar", new Vector3(1.55f, 1.7f, 8.6f), new Vector3(0.1f, 0.4f, 1.4f), Aid);

            // Landing point: a marked circle clear of the tents. A casualty
            // point that cannot be flown out of is a casualty point in the
            // wrong place, and the ring says the ground has been chosen.
            Cylinder(root, "LandingPoint", new Vector3(0f, 0.08f, -9.5f),
                new Vector3(7.0f, 0.08f, 7.0f), Pad);
            // Marked on the root, not on the pad: the pad is a primitive
            // squashed to a disc, and anything parented to it inherits that.
            Box(root, "LandingCross", new Vector3(0f, 0.16f, -9.5f), new Vector3(0.6f, 0.1f, 4.4f), Aid);
            Box(root, "LandingCrossBar", new Vector3(0f, 0.16f, -9.5f), new Vector3(4.4f, 0.1f, 0.6f), Aid);

            return root;
        }

        // ------------------------------------------------------ shared pieces
        //
        // Every installation is assembled from these rather than from bare
        // boxes, so the six read as one estate: the same hardstanding, the same
        // canopy, the same pallets. It is also what keeps a change of look to
        // one edit instead of six.

        /// <summary>The graded hardstanding an installation is laid out on.</summary>
        static void Apron(GameObject parent, float x, float z)
        {
            // The rim goes **under** the pad and slightly wider, so what shows
            // is a border of spoil round the hardstanding. Laid on top it would
            // simply be a second, larger slab covering the first.
            Box(parent, "ApronRim", new Vector3(0f, 0.05f, 0f), new Vector3(x + 1.2f, 0.10f, z + 1.2f), Earth);
            Box(parent, "Apron", new Vector3(0f, 0.06f, 0f), new Vector3(x, 0.12f, z), Pad);
        }

        /// <summary>A pitched roof: two slabs leaning against each other, plus a ridge.</summary>
        static void PitchedRoof(GameObject parent, string name, Vector3 baseCentre,
            float span, float length, float rise)
        {
            float slope = Mathf.Sqrt(span * span * 0.25f + rise * rise);
            float angle = Mathf.Atan2(rise, span * 0.5f) * Mathf.Rad2Deg;

            for (int side = -1; side <= 1; side += 2)
            {
                var slab = Box(parent, $"{name}{(side < 0 ? "L" : "R")}",
                    baseCentre + new Vector3(side * span * 0.25f, rise * 0.5f, 0f),
                    new Vector3(slope, 0.35f, length), Roof);
                slab.transform.localRotation = Quaternion.Euler(0f, 0f, -side * angle);
            }
            Box(parent, name + "Ridge", baseCentre + new Vector3(0f, rise, 0f),
                new Vector3(0.5f, 0.4f, length + 0.4f), Structure);
        }

        /// <summary>An open-sided shelter: four posts under a slightly pitched sheet.</summary>
        static void Shelter(GameObject parent, string name, Vector3 origin,
            float x, float z, float height)
        {
            for (int i = 0; i < 4; i++)
            {
                float px = (i < 2 ? -1f : 1f) * (x * 0.5f - 0.4f);
                float pz = (i % 2 == 0 ? -1f : 1f) * (z * 0.5f - 0.4f);
                Box(parent, $"{name}Post{i}", origin + new Vector3(px, height * 0.5f, pz),
                    new Vector3(0.4f, height, 0.4f), Structure);
            }
            // A shallow pitch rather than a flat sheet: a canopy that does not
            // shed water is a canopy nobody put up.
            var sheet = Box(parent, name + "Sheet", origin + new Vector3(0f, height + 0.3f, 0f),
                new Vector3(x + 1.2f, 0.25f, z + 1.2f), Roof);
            sheet.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
        }

        /// <summary>Palletised stores: <paramref name="tiers"/> banded crates on a pallet.</summary>
        static void PalletStack(GameObject parent, string name, Vector3 origin, int tiers)
        {
            Box(parent, name + "Pallet", origin + new Vector3(0f, 0.12f, 0f),
                new Vector3(2.6f, 0.24f, 2.2f), Structure);

            for (int i = 0; i < tiers; i++)
            {
                float y = 0.24f + 0.85f + i * 1.6f;
                Box(parent, $"{name}Crate{i}", origin + new Vector3(0f, y, 0f),
                    new Vector3(2.4f, 1.5f, 2.0f), Stores);
                // One band across each crate — the universal read for "cargo",
                // and the thing that stops a stack looking like a wall.
                Box(parent, $"{name}Band{i}", origin + new Vector3(0f, y, 0f),
                    new Vector3(2.5f, 0.16f, 0.24f), Structure);
            }
        }

        /// <summary>An earth bund: four low walls enclosing a store.</summary>
        static void Bund(GameObject parent, Vector3 centre, float x, float z,
            float height, float thickness)
        {
            Box(parent, "BundN", centre + new Vector3(0f, height * 0.5f, -z * 0.5f),
                new Vector3(x, height, thickness), Earth);
            Box(parent, "BundS", centre + new Vector3(0f, height * 0.5f, z * 0.5f),
                new Vector3(x, height, thickness), Earth);
            Box(parent, "BundW", centre + new Vector3(-x * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, z), Earth);
            Box(parent, "BundE", centre + new Vector3(x * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, z), Earth);
        }

        /// <summary>One leaning member of an A-frame, from a foot to an apex offset.</summary>
        static void Leg(GameObject parent, string name, Vector3 foot, Vector3 toApex)
        {
            float length = toApex.magnitude;
            var leg = Box(parent, name, foot + toApex * 0.5f,
                new Vector3(0.45f, length, 0.45f), Steel);
            leg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, toApex.normalized);
        }

        /// <summary>Four corner posts marking the limit of the hardstanding.</summary>
        static void Bollards(GameObject parent, float x, float z)
        {
            for (int i = 0; i < 4; i++)
            {
                float px = (i < 2 ? -1f : 1f) * x;
                float pz = (i % 2 == 0 ? -1f : 1f) * z;
                Box(parent, $"Bollard{i}", new Vector3(px, 0.7f, pz),
                    new Vector3(0.3f, 1.4f, 0.3f), Structure);
            }
        }

        /// <summary>
        /// A cylinder, as a primitive with its collider taken off.
        ///
        /// Unity's cylinder is 2 units tall and 1 across, so the scale passed in
        /// is halved on Y — callers give a diameter and a **full** height, like
        /// every other size in this file, rather than having to remember which
        /// primitive is the odd one out.
        /// </summary>
        static GameObject Cylinder(GameObject parent, string name, Vector3 position,
            Vector3 size, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
            Paint(go, colour);
            return go;
        }
    }
}
