using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.UI
{
    /// <summary>
    /// The HUD's icon set, drawn in code.
    ///
    /// The project ships no icon font and uses legacy <c>Text</c> with
    /// LegacyRuntime.ttf, whose glyph coverage beyond basic geometric shapes is
    /// not guaranteed — a missing gear or bin renders as a blank box on some
    /// machines. Drawing them from shape maths keeps the interface identical
    /// everywhere and costs one small texture each, built once and cached.
    ///
    /// Icons are white so callers tint them with <c>Image.color</c>. Shapes are
    /// authored in a 0..1 square with v pointing up.
    /// </summary>
    public static class UiIcons
    {
        const int Size = 64;

        static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // ------------------------------------------------------------ public

        /// <summary>Settings.</summary>
        public static Sprite Gear => Get(nameof(Gear), (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            // Radius modulated by an 8-lobed square wave — a toothed ring.
            float teeth = Mathf.Cos(ang * 8f) > 0.15f ? 0.075f : 0f;
            return Band(d, 0.235f + teeth, 0.085f);
        });

        /// <summary>Search field affordance.</summary>
        public static Sprite Search => Get(nameof(Search), (u, v) =>
            Mathf.Max(
                RingAt(u, v, 0.42f, 0.58f, 0.235f, 0.075f),
                Seg(u, v, 0.585f, 0.415f, 0.80f, 0.20f, 0.075f)));

        /// <summary>Destructive action (remove unit).</summary>
        public static Sprite Trash => Get(nameof(Trash), (u, v) =>
        {
            float body = RectOutline(u, v, 0.28f, 0.10f, 0.72f, 0.64f, 0.07f);
            float lid = Rect(u, v, 0.20f, 0.68f, 0.80f, 0.76f);
            float handle = RectOutline(u, v, 0.40f, 0.76f, 0.60f, 0.88f, 0.07f);
            return Mathf.Max(body, Mathf.Max(lid, handle));
        });

        /// <summary>Row overflow menu (⋮).</summary>
        public static Sprite Kebab => Get(nameof(Kebab), (u, v) =>
            Mathf.Max(DiscAt(u, v, 0.5f, 0.20f, 0.085f),
                Mathf.Max(DiscAt(u, v, 0.5f, 0.50f, 0.085f),
                          DiscAt(u, v, 0.5f, 0.80f, 0.085f))));

        /// <summary>Panel emblem.</summary>
        public static Sprite Shield => Get(nameof(Shield), (u, v) =>
            InPoly(u, v, new[]
            {
                0.50f, 0.94f, 0.14f, 0.72f, 0.14f, 0.40f,
                0.50f, 0.08f, 0.86f, 0.40f, 0.86f, 0.72f
            }));

        /// <summary>Units / personnel.</summary>
        public static Sprite Person => Get(nameof(Person), (u, v) =>
        {
            float head = DiscAt(u, v, 0.5f, 0.74f, 0.17f);
            // Shoulders: the top of a large disc, clipped to the lower half.
            float body = v < 0.55f ? DiscAt(u, v, 0.5f, 0.06f, 0.36f) : 0f;
            return Mathf.Max(head, body);
        });

        /// <summary>General / briefing section.</summary>
        public static Sprite Flag => Get(nameof(Flag), (u, v) =>
            Mathf.Max(Rect(u, v, 0.22f, 0.08f, 0.30f, 0.92f),
                      RectOutline(u, v, 0.30f, 0.54f, 0.80f, 0.90f, 0.07f)));

        /// <summary>Map / layers section.</summary>
        public static Sprite Layers => Get(nameof(Layers), (u, v) =>
            Mathf.Max(Diamond(u, v, 0.5f, 0.68f, 0.40f, 0.22f, hollow: false),
                Mathf.Max(Diamond(u, v, 0.5f, 0.46f, 0.40f, 0.22f, hollow: true),
                          Diamond(u, v, 0.5f, 0.26f, 0.40f, 0.22f, hollow: true))));

        // --- right-panel tabs ---
        public static Sprite Info => Get(nameof(Info), (u, v) =>
            Mathf.Max(RingAt(u, v, 0.5f, 0.5f, 0.36f, 0.075f),
                Mathf.Max(DiscAt(u, v, 0.5f, 0.68f, 0.055f),
                          Rect(u, v, 0.455f, 0.28f, 0.545f, 0.56f))));

        public static Sprite Equipment => Get(nameof(Equipment), (u, v) =>
            Mathf.Max(RectOutline(u, v, 0.10f, 0.24f, 0.90f, 0.52f, 0.07f),
                Mathf.Max(RectOutline(u, v, 0.32f, 0.52f, 0.70f, 0.74f, 0.07f),
                          Rect(u, v, 0.66f, 0.60f, 0.94f, 0.66f))));

        public static Sprite Orders => Get(nameof(Orders), (u, v) =>
            Mathf.Max(Chevron(u, v, 0.30f, 0.72f, 0.20f),
                Mathf.Max(Chevron(u, v, 0.30f, 0.50f, 0.20f),
                          Chevron(u, v, 0.30f, 0.28f, 0.20f))));

        public static Sprite Pulse => Get(nameof(Pulse), (u, v) =>
            Mathf.Max(Seg(u, v, 0.06f, 0.50f, 0.30f, 0.50f, 0.07f),
                Mathf.Max(Seg(u, v, 0.30f, 0.50f, 0.42f, 0.84f, 0.07f),
                    Mathf.Max(Seg(u, v, 0.42f, 0.84f, 0.56f, 0.16f, 0.07f),
                        Mathf.Max(Seg(u, v, 0.56f, 0.16f, 0.68f, 0.50f, 0.07f),
                                  Seg(u, v, 0.68f, 0.50f, 0.94f, 0.50f, 0.07f))))));

        /// <summary>Zoom in.</summary>
        public static Sprite Plus => Get(nameof(Plus), (u, v) =>
            Mathf.Max(Rect(u, v, 0.16f, 0.44f, 0.84f, 0.56f),
                      Rect(u, v, 0.44f, 0.16f, 0.56f, 0.84f)));

        /// <summary>Zoom out.</summary>
        public static Sprite Minus => Get(nameof(Minus), (u, v) =>
            Rect(u, v, 0.16f, 0.44f, 0.84f, 0.56f));

        /// <summary>Compass needle — the half pointing north is what gets tinted.</summary>
        public static Sprite CompassNeedle => Get(nameof(CompassNeedle), (u, v) =>
            InPoly(u, v, new[] { 0.50f, 0.92f, 0.36f, 0.44f, 0.50f, 0.50f, 0.64f, 0.44f }));

        /// <summary>Compass dial: outer ring with cardinal ticks.</summary>
        public static Sprite CompassRose => Get(nameof(CompassRose), (u, v) =>
        {
            float ring = RingAt(u, v, 0.5f, 0.5f, 0.44f, 0.045f);
            // Four cardinal ticks, longer than the eight intercardinal ones.
            float ticks = Mathf.Max(
                Mathf.Max(Rect(u, v, 0.485f, 0.80f, 0.515f, 0.94f), Rect(u, v, 0.485f, 0.06f, 0.515f, 0.20f)),
                Mathf.Max(Rect(u, v, 0.06f, 0.485f, 0.20f, 0.515f), Rect(u, v, 0.80f, 0.485f, 0.94f, 0.515f)));
            float hub = RingAt(u, v, 0.5f, 0.5f, 0.06f, 0.04f);
            return Mathf.Max(ring, Mathf.Max(ticks, hub));
        });

        /// <summary>Fire effect.</summary>
        public static Sprite Flame => Get(nameof(Flame), (u, v) =>
        {
            // Teardrop: a disc for the body, tapering to a point at the top.
            float body = DiscAt(u, v, 0.5f, 0.34f, 0.28f);
            float taper = v > 0.34f
                ? Cov((0.28f * (1f - (v - 0.34f) / 0.60f)) - Mathf.Abs(u - 0.5f))
                : 0f;
            // Bite out of the base so it reads as flame, not a balloon.
            float bite = DiscAt(u, v, 0.5f, 0.08f, 0.13f);
            return Mathf.Clamp01(Mathf.Max(body, taper) - bite);
        });

        /// <summary>Explosion effect.</summary>
        public static Sprite Burst => Get(nameof(Burst), (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            // Star: radius spikes on a 6-lobed wave.
            float r = 0.20f + 0.20f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 0.6f);
            return Cov(r - d);
        });

        /// <summary>Smoke effect — distinct from the weather cloud: a rising column.</summary>
        public static Sprite SmokeStack => Get(nameof(SmokeStack), (u, v) =>
            Mathf.Max(DiscAt(u, v, 0.44f, 0.80f, 0.19f),
                Mathf.Max(DiscAt(u, v, 0.60f, 0.62f, 0.17f),
                    Mathf.Max(DiscAt(u, v, 0.42f, 0.44f, 0.15f),
                        Mathf.Max(DiscAt(u, v, 0.56f, 0.26f, 0.13f),
                                  Rect(u, v, 0.30f, 0.06f, 0.70f, 0.12f))))));

        // ------------------------------------------------- artillery strike
        //
        // The four natures are told apart by silhouette, not by size: at 24 px
        // a "big shell" and a "small shell" are the same picture. So the mortar
        // bomb gets fins, the light gun gets a plain slim round, the medium a
        // banded round, and the heavy a squat round with a driving band and a
        // blunt nose — each recognisable on its own.

        /// <summary>ARTILLERY STRIKE section: a field gun in profile.</summary>
        public static Sprite Artillery => Get(nameof(Artillery), (u, v) =>
            // Barrel raised to the right, a breech block, trail leg and wheel.
            Mathf.Max(Seg(u, v, 0.30f, 0.42f, 0.88f, 0.76f, 0.10f),
                Mathf.Max(DiscAt(u, v, 0.30f, 0.40f, 0.13f),
                    Mathf.Max(Seg(u, v, 0.28f, 0.36f, 0.08f, 0.18f, 0.075f),
                              RingAt(u, v, 0.34f, 0.22f, 0.13f, 0.075f)))));

        /// <summary>105 mm: a slim round with a pointed nose.</summary>
        public static Sprite ShellLight => Get(nameof(ShellLight), (u, v) =>
            Mathf.Max(Shell(u, v, halfWidth: 0.105f, baseV: 0.16f, shoulderV: 0.60f, tipV: 0.88f),
                      Rect(u, v, 0.5f - 0.135f, 0.13f, 0.5f + 0.135f, 0.19f)));

        /// <summary>120 mm mortar bomb: teardrop body on a finned tail.</summary>
        public static Sprite MortarBomb => Get(nameof(MortarBomb), (u, v) =>
            Mathf.Max(Shell(u, v, halfWidth: 0.135f, baseV: 0.34f, shoulderV: 0.62f, tipV: 0.90f),
                // Tail boom plus three fins — the whole point of the silhouette.
                Mathf.Max(Rect(u, v, 0.5f - 0.045f, 0.10f, 0.5f + 0.045f, 0.38f),
                    Mathf.Max(Seg(u, v, 0.5f, 0.30f, 0.28f, 0.11f, 0.055f),
                        Mathf.Max(Seg(u, v, 0.5f, 0.30f, 0.72f, 0.11f, 0.055f),
                                  Seg(u, v, 0.5f, 0.30f, 0.5f, 0.08f, 0.055f))))));

        /// <summary>155 mm: a banded round — the reference nature.</summary>
        public static Sprite ShellMedium => Get(nameof(ShellMedium), (u, v) =>
            Mathf.Clamp01(
                Mathf.Max(Shell(u, v, halfWidth: 0.155f, baseV: 0.12f, shoulderV: 0.56f, tipV: 0.90f),
                          Rect(u, v, 0.5f - 0.19f, 0.09f, 0.5f + 0.19f, 0.16f))
                // Driving band bitten out of the body, which is what makes it
                // read as a different round rather than a bigger one.
                - Rect(u, v, 0.5f - 0.16f, 0.30f, 0.5f + 0.16f, 0.36f)));

        /// <summary>203 mm: squat, blunt-nosed, with a heavy driving band.</summary>
        public static Sprite ShellHeavy => Get(nameof(ShellHeavy), (u, v) =>
            Mathf.Clamp01(
                Mathf.Max(Shell(u, v, halfWidth: 0.215f, baseV: 0.12f, shoulderV: 0.52f, tipV: 0.80f),
                          Rect(u, v, 0.5f - 0.25f, 0.08f, 0.5f + 0.25f, 0.17f))
                - Mathf.Max(Rect(u, v, 0.5f - 0.225f, 0.26f, 0.5f + 0.225f, 0.33f),
                            Rect(u, v, 0.5f - 0.225f, 0.38f, 0.5f + 0.225f, 0.43f))));

        /// <summary>
        /// A shell body: a parallel-sided case up to <paramref name="shoulderV"/>,
        /// then an ogive tapering to a nose at <paramref name="tipV"/>. Shared by
        /// every nature so they are unmistakably the same family of object.
        /// </summary>
        static float Shell(float u, float v, float halfWidth, float baseV, float shoulderV, float tipV)
        {
            if (v < baseV || v > tipV) return 0f;

            float w = halfWidth;
            if (v > shoulderV)
            {
                // Ogive rather than a straight cone: squared falloff gives the
                // curved shoulder that reads as a shell instead of a crayon.
                float k = (v - shoulderV) / Mathf.Max(1e-5f, tipV - shoulderV);
                w = halfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
            }

            return Cov(Mathf.Min(w - Mathf.Abs(u - 0.5f),
                                 Mathf.Min(v - baseV, tipV - v)));
        }

        // ------------------------------------------------------- air strike

        /// <summary>
        /// AIR STRIKE section and the B-2 button: a flying wing seen from above,
        /// nose up.
        ///
        /// Drawn as a swept delta with a notched trailing edge, which is the one
        /// silhouette nobody mistakes for anything else — and is why a B-2 is
        /// recognisable in a photograph at any size.
        /// </summary>
        public static Sprite FlyingWing => Get(nameof(FlyingWing), (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f);

            // Swept leading edges running back from the nose at (0.5, 0.92).
            float leading = Cov((0.92f - v) * 0.62f - x);
            // Trailing edge, cutting the delta off at the bottom.
            float trailing = Cov(v - 0.24f);
            float body = Mathf.Min(leading, trailing);

            // The double-W notch along the trailing edge. Two bites taken out,
            // which is what turns a plain delta into this aircraft.
            float notch = Mathf.Max(
                Rect(u, v, 0.5f - 0.13f, 0.06f, 0.5f + 0.13f, 0.30f - Mathf.Abs(u - 0.5f) * 0.35f),
                Mathf.Max(Rect(u, v, 0.12f, 0.06f, 0.27f, 0.34f),
                          Rect(u, v, 0.73f, 0.06f, 0.88f, 0.34f)));

            // Cockpit blister, so the nose end is unambiguous.
            float canopy = DiscAt(u, v, 0.5f, 0.72f, 0.055f);

            return Mathf.Clamp01(Mathf.Max(Mathf.Clamp01(body - notch), canopy));
        });

        /// <summary>
        /// UAV STRIKES section and the kamikaze drone button: a quadcopter seen
        /// from above — four rotor discs on an X frame around a body.
        ///
        /// Read from overhead rather than in profile because that is the only
        /// angle at which a multirotor is unmistakable; from the side it is a box.
        /// </summary>
        public static Sprite Quadcopter => Get(nameof(Quadcopter), (u, v) =>
        {
            // Four arms out to the corners.
            float arms = Mathf.Max(
                Seg(u, v, 0.28f, 0.28f, 0.72f, 0.72f, 0.075f),
                Seg(u, v, 0.28f, 0.72f, 0.72f, 0.28f, 0.075f));

            // Rotor discs at the ends, drawn as rings so the frame reads through.
            float rotors = 0f;
            float[] cx = { 0.24f, 0.76f, 0.24f, 0.76f };
            float[] cy = { 0.24f, 0.24f, 0.76f, 0.76f };
            for (int i = 0; i < 4; i++)
                rotors = Mathf.Max(rotors, RingAt(u, v, cx[i], cy[i], 0.155f, 0.055f));

            // Body: a solid centre so it is not just an empty cross.
            float body = DiscAt(u, v, 0.5f, 0.5f, 0.115f);

            return Mathf.Clamp01(Mathf.Max(arms, Mathf.Max(rotors, body)));
        });

        /// <summary>
        /// Attack helicopter, seen from above: a slim fuselage with a tail boom
        /// under a rotor disc. The disc is what carries the identity — a
        /// helicopter without one is just a fish.
        /// </summary>
        public static Sprite Helicopter => Get(nameof(Helicopter), (u, v) =>
        {
            // Fuselage forward of centre, tail boom running back.
            float body = Mathf.Max(
                DiscAt(u, v, 0.5f, 0.66f, 0.115f),
                Rect(u, v, 0.5f - 0.055f, 0.20f, 0.5f + 0.055f, 0.70f));
            // Tail plane.
            float tail = Rect(u, v, 0.5f - 0.14f, 0.20f, 0.5f + 0.14f, 0.26f);
            // Rotor disc: two long blades crossed over the fuselage.
            float rotor = Mathf.Max(
                Seg(u, v, 0.08f, 0.78f, 0.92f, 0.78f, 0.055f),
                Seg(u, v, 0.28f, 0.95f, 0.72f, 0.61f, 0.048f));

            return Mathf.Clamp01(Mathf.Max(Mathf.Max(body, tail), rotor));
        });

        /// <summary>
        /// Strike fighter, seen from above: a swept delta with a pointed nose,
        /// tailplanes and a fin. Narrower and sharper than the flying wing, so
        /// the two are not confused in the same list.
        /// </summary>
        public static Sprite Jet => Get(nameof(Jet), (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f);

            // Fuselage: a long spine from tail to nose.
            float fuselage = Cov(Mathf.Min(0.058f - x, Mathf.Min(v - 0.10f, 0.95f - v)));

            // Main wings: swept back from mid-body.
            float wings = v > 0.30f && v < 0.62f
                ? Cov(Mathf.Min((0.62f - v) * 1.35f - x + 0.06f, v - 0.30f))
                : 0f;

            // Tailplanes at the back, and a fin along the spine.
            float tail = v > 0.12f && v < 0.26f
                ? Cov(Mathf.Min((0.26f - v) * 1.5f - x + 0.03f, v - 0.12f))
                : 0f;

            return Mathf.Clamp01(Mathf.Max(fuselage, Mathf.Max(wings, tail)));
        });

        /// <summary>Weather conditions section.</summary>
        public static Sprite Cloud => Get(nameof(Cloud), (u, v) =>
            Mathf.Max(DiscAt(u, v, 0.34f, 0.48f, 0.20f),
                Mathf.Max(DiscAt(u, v, 0.56f, 0.56f, 0.26f),
                    Mathf.Max(DiscAt(u, v, 0.74f, 0.46f, 0.18f),
                              Rect(u, v, 0.34f, 0.28f, 0.74f, 0.50f)))));

        /// <summary>Date &amp; time section.</summary>
        public static Sprite Clock => Get(nameof(Clock), (u, v) =>
            Mathf.Max(RingAt(u, v, 0.5f, 0.5f, 0.38f, 0.08f),
                Mathf.Max(Seg(u, v, 0.5f, 0.5f, 0.5f, 0.76f, 0.075f),
                          Seg(u, v, 0.5f, 0.5f, 0.70f, 0.42f, 0.075f))));

        /// <summary>Start battle.</summary>
        public static Sprite Play => Get(nameof(Play), (u, v) =>
            InPoly(u, v, new[] { 0.20f, 0.10f, 0.20f, 0.90f, 0.86f, 0.50f }));

        /// <summary>Pause battle — the same control once it is running.</summary>
        public static Sprite PauseBars => Get(nameof(PauseBars), (u, v) =>
            Mathf.Max(Rect(u, v, 0.22f, 0.12f, 0.42f, 0.88f),
                      Rect(u, v, 0.58f, 0.12f, 0.78f, 0.88f)));

        /// <summary>Dismiss a panel.</summary>
        public static Sprite Close => Get(nameof(Close), (u, v) =>
            Mathf.Max(Seg(u, v, 0.24f, 0.24f, 0.76f, 0.76f, 0.085f),
                      Seg(u, v, 0.76f, 0.24f, 0.24f, 0.76f, 0.085f)));

        /// <summary>
        /// Go back. A chevron with a shaft behind it rather than a bare "&lt;":
        /// the shaft is what makes the glyph read as a direction of travel at
        /// 20 px, which is the whole job of a back control.
        /// </summary>
        public static Sprite ArrowLeft => Get(nameof(ArrowLeft), (u, v) =>
            Mathf.Max(
                Seg(u, v, 0.88f, 0.50f, 0.26f, 0.50f, 0.085f),
                Mathf.Max(Seg(u, v, 0.52f, 0.82f, 0.18f, 0.50f, 0.095f),
                          Seg(u, v, 0.18f, 0.50f, 0.52f, 0.18f, 0.095f))));

        /// <summary>
        /// Reconnaissance: an eye over a scanned footprint. Used for the recon
        /// drone, which is the one thing in the UAV menu that looks rather than
        /// destroys — see docs/19-UAV-STRIKES.md.
        /// </summary>
        public static Sprite ReconEye => Get(nameof(ReconEye), (u, v) =>
        {
            // Lens: a ring with a pupil, drawn high in the square.
            float lens = Mathf.Max(RingAt(u, v, 0.5f, 0.62f, 0.24f, 0.075f),
                                   DiscAt(u, v, 0.5f, 0.62f, 0.10f));
            // Two sweep bars beneath it, the footprint being looked at.
            float sweep = Mathf.Max(Seg(u, v, 0.20f, 0.20f, 0.80f, 0.20f, 0.065f),
                                    Seg(u, v, 0.32f, 0.06f, 0.68f, 0.06f, 0.055f));
            return Mathf.Max(lens, sweep);
        });

        // --- bottom tool row ---
        public static Sprite Cursor => Get(nameof(Cursor), (u, v) =>
            InPoly(u, v, new[]
            {
                0.26f, 0.92f, 0.26f, 0.16f, 0.44f, 0.36f,
                0.55f, 0.10f, 0.68f, 0.16f, 0.57f, 0.42f, 0.74f, 0.42f
            }));

        public static Sprite Pencil => Get(nameof(Pencil), (u, v) =>
            Mathf.Max(SegFilled(u, v, 0.34f, 0.34f, 0.80f, 0.80f, 0.115f),
                      InPoly(u, v, new[] { 0.10f, 0.10f, 0.34f, 0.22f, 0.22f, 0.34f })));

        public static Sprite Square => Get(nameof(Square), (u, v) =>
            RectOutline(u, v, 0.16f, 0.16f, 0.84f, 0.84f, 0.075f));

        public static Sprite Pin => Get(nameof(Pin), (u, v) =>
            Mathf.Max(RingAt(u, v, 0.5f, 0.66f, 0.26f, 0.085f),
                      InPoly(u, v, new[] { 0.30f, 0.56f, 0.70f, 0.56f, 0.50f, 0.06f })));

        public static Sprite Chart => Get(nameof(Chart), (u, v) =>
            Mathf.Max(Rect(u, v, 0.16f, 0.12f, 0.34f, 0.50f),
                Mathf.Max(Rect(u, v, 0.41f, 0.12f, 0.59f, 0.86f),
                          Rect(u, v, 0.66f, 0.12f, 0.84f, 0.64f))));

        /// <summary>
        /// A plain filled circle. Not an icon so much as a shape the layout
        /// needs — uGUI's built-in sprite is a rounded rectangle, and the unit
        /// cluster markers have to read as counters on a map rather than as
        /// buttons floating over it.
        /// </summary>
        public static Sprite Disc => Get(nameof(Disc), (u, v) => DiscAt(u, v, 0.5f, 0.5f, 0.48f));

        // ---------------------------------------------------- missile systems
        //
        // Three glyphs for ten launchers, matching the two things a player is
        // choosing between: what the system is *for* (defend the sky, or hit the
        // ground) and how heavy it is. Ten bespoke pictograms would be ten
        // pictograms nobody could tell apart at 22 px.

        /// <summary>Air-defence battery: an interceptor climbing out of a launch rail.</summary>
        public static Sprite Interceptor => Get(nameof(Interceptor), (u, v) =>
            Mathf.Max(
                // Body, leaning back the way a canted launcher sits.
                Seg(u, v, 0.40f, 0.16f, 0.62f, 0.82f, 0.13f),
                Mathf.Max(
                    // Nose.
                    Seg(u, v, 0.62f, 0.82f, 0.66f, 0.94f, 0.05f),
                    // Launch rail under it.
                    Seg(u, v, 0.16f, 0.10f, 0.52f, 0.10f, 0.07f))));

        /// <summary>Surface-to-surface missile: a ballistic arc onto a ground point.</summary>
        public static Sprite BallisticArc => Get(nameof(BallisticArc), (u, v) =>
        {
            // The arc itself, sampled as a chain of short segments — a real
            // parabola through (0.08, 0.30) → (0.5, 0.88) → (0.92, 0.14).
            float arc = 0f;
            const int Steps = 10;
            for (int i = 0; i < Steps; i++)
            {
                float t0 = i / (float)Steps, t1 = (i + 1) / (float)Steps;
                arc = Mathf.Max(arc, Seg(u, v,
                    ArcX(t0), ArcY(t0), ArcX(t1), ArcY(t1), 0.075f));
            }
            // Impact mark where it comes down.
            return Mathf.Max(arc, DiscAt(u, v, 0.92f, 0.14f, 0.11f));
        });

        /// <summary>Heavy ballistic missile: the arc, with a second heavier warhead mark.</summary>
        public static Sprite HeavyMissile => Get(nameof(HeavyMissile), (u, v) =>
            Mathf.Max(
                // Vertical body with a pointed nose — read from directly ahead,
                // which is how a missile coming down at you looks.
                Seg(u, v, 0.5f, 0.10f, 0.5f, 0.74f, 0.17f),
                Mathf.Max(
                    Seg(u, v, 0.5f, 0.74f, 0.5f, 0.94f, 0.07f),
                    // Tail fins, splayed.
                    Mathf.Max(Seg(u, v, 0.5f, 0.24f, 0.20f, 0.06f, 0.075f),
                              Seg(u, v, 0.5f, 0.24f, 0.80f, 0.06f, 0.075f)))));

        // ------------------------------------------------------ naval gunfire

        /// <summary>
        /// Naval gunfire support: a hull seen from the side with a gun mounting
        /// forward. The hull's flared sheer is what makes it read as a ship at
        /// 16 px rather than as a wedge — the gun alone would be another shell
        /// glyph, and the whole point of the row is that the rounds come from
        /// the sea.
        /// </summary>
        public static Sprite Warship => Get(nameof(Warship), (u, v) =>
        {
            // Hull: a shallow trapezium, wider at the deck than at the keel.
            float hull = InPoly(u, v, new[]
            {
                0.06f, 0.40f, 0.94f, 0.40f, 0.82f, 0.16f, 0.20f, 0.16f
            });
            // Superstructure block amidships.
            float bridge = Rect(u, v, 0.44f, 0.40f, 0.68f, 0.62f);
            // Mast over it.
            float mast = Rect(u, v, 0.545f, 0.62f, 0.585f, 0.86f);
            // Gun mounting forward, barrel raised.
            float mount = Rect(u, v, 0.20f, 0.40f, 0.36f, 0.54f);
            float barrel = Seg(u, v, 0.28f, 0.52f, 0.10f, 0.72f, 0.055f);

            return Mathf.Max(hull, Mathf.Max(bridge, Mathf.Max(mast, Mathf.Max(mount, barrel))));
        });

        // ---------------------------------------------------------- logistics
        // Six installations that have to be told apart at 20 px on a rail
        // button and at whatever the camera makes of them on the map, so each
        // is a different *silhouette* rather than a different letter in the
        // same box: a roof, stacked crates, a droplet, rounds, crossed tools, a
        // cross. See docs/26-LOGISTICS.md.

        /// <summary>Supply depot — a warehouse: pitched roof over an open shed.</summary>
        public static Sprite Depot => Get(nameof(Depot), (u, v) =>
        {
            float roof = InPoly(u, v, new[] { 0.08f, 0.60f, 0.50f, 0.90f, 0.92f, 0.60f });
            float body = RectOutline(u, v, 0.16f, 0.14f, 0.84f, 0.62f, 0.075f);
            float door = Rect(u, v, 0.42f, 0.14f, 0.58f, 0.42f);
            return Mathf.Max(roof, Mathf.Max(body, door));
        });

        /// <summary>Supply point — two crates, the forward stock of the depot above.</summary>
        public static Sprite Crates => Get(nameof(Crates), (u, v) =>
        {
            float lower = RectOutline(u, v, 0.10f, 0.10f, 0.56f, 0.50f, 0.07f);
            float lowerStrap = Seg(u, v, 0.10f, 0.30f, 0.56f, 0.30f, 0.06f);
            float upper = RectOutline(u, v, 0.44f, 0.44f, 0.90f, 0.86f, 0.07f);
            float upperStrap = Seg(u, v, 0.44f, 0.65f, 0.90f, 0.65f, 0.06f);
            return Mathf.Max(Mathf.Max(lower, lowerStrap), Mathf.Max(upper, upperStrap));
        });

        /// <summary>Fuel point — a droplet.</summary>
        public static Sprite FuelDrop => Get(nameof(FuelDrop), (u, v) =>
        {
            float bowl = DiscAt(u, v, 0.5f, 0.34f, 0.28f);
            float tip = InPoly(u, v, new[] { 0.24f, 0.42f, 0.50f, 0.92f, 0.76f, 0.42f });
            return Mathf.Max(bowl, tip);
        });

        /// <summary>Ammunition point — two rounds standing on their bases.</summary>
        public static Sprite Rounds => Get(nameof(Rounds), (u, v) =>
        {
            float shape = 0f;
            for (int i = 0; i < 2; i++)
            {
                float x = 0.20f + i * 0.34f;
                float body = Rect(u, v, x, 0.14f, x + 0.20f, 0.58f);
                float nose = InPoly(u, v, new[]
                {
                    x, 0.58f, x + 0.10f, 0.84f, x + 0.20f, 0.58f
                });
                shape = Mathf.Max(shape, Mathf.Max(body, nose));
            }
            return shape;
        });

        /// <summary>Repair point — crossed tools, one with a ring spanner head.</summary>
        public static Sprite Tools => Get(nameof(Tools), (u, v) =>
        {
            float shaft = Seg(u, v, 0.20f, 0.24f, 0.72f, 0.70f, 0.105f);
            float head = RingAt(u, v, 0.78f, 0.76f, 0.115f, 0.085f);
            float driver = Seg(u, v, 0.24f, 0.76f, 0.64f, 0.34f, 0.085f);
            float grip = Seg(u, v, 0.64f, 0.34f, 0.80f, 0.18f, 0.14f);
            return Mathf.Max(Mathf.Max(shaft, head), Mathf.Max(driver, grip));
        });

        /// <summary>Medical point — the cross, which needs no explaining anywhere.</summary>
        public static Sprite MedicalCross => Get(nameof(MedicalCross), (u, v) =>
            Mathf.Max(Rect(u, v, 0.40f, 0.12f, 0.60f, 0.88f),
                      Rect(u, v, 0.12f, 0.40f, 0.88f, 0.60f)));

        /// <summary>The rail row for the whole LOGISTICS section — a loaded pallet.</summary>
        public static Sprite Pallet => Get(nameof(Pallet), (u, v) =>
        {
            float load = RectOutline(u, v, 0.20f, 0.36f, 0.80f, 0.84f, 0.075f);
            float deck = Rect(u, v, 0.08f, 0.22f, 0.92f, 0.32f);
            float legL = Rect(u, v, 0.14f, 0.10f, 0.26f, 0.22f);
            float legR = Rect(u, v, 0.74f, 0.10f, 0.86f, 0.22f);
            return Mathf.Max(load, Mathf.Max(deck, Mathf.Max(legL, legR)));
        });

        /// <summary>Air supply — a canopy over its load. The one aircraft menu that gives rather than takes.</summary>
        public static Sprite Parachute => Get(nameof(Parachute), (u, v) =>
        {
            // Canopy: a dome, drawn as the top half of a disc.
            float dome = v >= 0.56f ? DiscAt(u, v, 0.5f, 0.56f, 0.40f) : 0f;
            // Its skirt, so the mouth of the chute reads at button size.
            float skirt = Rect(u, v, 0.10f, 0.52f, 0.90f, 0.58f);
            // Rigging down to the load.
            float lineL = Seg(u, v, 0.14f, 0.54f, 0.44f, 0.22f, 0.05f);
            float lineR = Seg(u, v, 0.86f, 0.54f, 0.56f, 0.22f, 0.05f);
            float load = Rect(u, v, 0.40f, 0.06f, 0.60f, 0.24f);
            return Mathf.Max(Mathf.Max(dome, skirt), Mathf.Max(Mathf.Max(lineL, lineR), load));
        });

        /// <summary>The rail row for GROUPS — three counters standing together.</summary>
        public static Sprite Group => Get(nameof(Group), (u, v) =>
        {
            float a = RectOutline(u, v, 0.06f, 0.46f, 0.46f, 0.82f, 0.075f);
            float b = RectOutline(u, v, 0.54f, 0.46f, 0.94f, 0.82f, 0.075f);
            float c = RectOutline(u, v, 0.30f, 0.12f, 0.70f, 0.44f, 0.075f);
            return Mathf.Max(a, Mathf.Max(b, c));
        });

        /// <summary>
        /// The glyph for a logistic installation. One mapping, read by the
        /// LOGISTICS panel's buttons, its placement ghost and the map marker —
        /// three pictures of the same thing that must never disagree.
        /// </summary>
        public static Sprite GlyphFor(Data.LogisticsKind kind) => kind switch
        {
            Data.LogisticsKind.SupplyDepot => Depot,
            Data.LogisticsKind.SupplyPoint => Crates,
            Data.LogisticsKind.FuelPoint => FuelDrop,
            Data.LogisticsKind.AmmoPoint => Rounds,
            Data.LogisticsKind.RepairPoint => Tools,
            _ => MedicalCross
        };

        static float ArcX(float t) => Mathf.Lerp(0.08f, 0.92f, t);
        static float ArcY(float t) => 0.30f + t * 1.62f - t * t * 1.78f;

        // ------------------------------------------------------------ shapes

        static float Band(float d, float radius, float thickness) =>
            Cov(thickness * 0.5f - Mathf.Abs(d - radius));

        static float RingAt(float u, float v, float cx, float cy, float r, float thickness)
        {
            float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
            return Band(d, r, thickness);
        }

        static float DiscAt(float u, float v, float cx, float cy, float r)
        {
            float d = Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
            return Cov(r - d);
        }

        static float Rect(float u, float v, float x0, float y0, float x1, float y1)
        {
            // Signed distance to the box, negative outside.
            float dx = Mathf.Min(u - x0, x1 - u);
            float dy = Mathf.Min(v - y0, y1 - v);
            return Cov(Mathf.Min(dx, dy));
        }

        static float RectOutline(float u, float v, float x0, float y0, float x1, float y1, float t)
        {
            float outer = Rect(u, v, x0, y0, x1, y1);
            float inner = Rect(u, v, x0 + t, y0 + t, x1 - t, y1 - t);
            return Mathf.Clamp01(outer - inner);
        }

        /// <summary>Distance-to-segment stroke, used for every line in the set.</summary>
        static float Seg(float u, float v, float x0, float y0, float x1, float y1, float t)
        {
            var p = new Vector2(u, v);
            var a = new Vector2(x0, y0);
            var ab = new Vector2(x1 - x0, y1 - y0);
            float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Cov(t * 0.5f - Vector2.Distance(p, a + ab * k));
        }

        /// <summary>Segment with square ends — reads as a body rather than a stroke.</summary>
        static float SegFilled(float u, float v, float x0, float y0, float x1, float y1, float t) =>
            Seg(u, v, x0, y0, x1, y1, t);

        static float Chevron(float u, float v, float x, float cy, float size) =>
            Mathf.Max(Seg(u, v, x, cy + size * 0.5f, x + size * 0.7f, cy, 0.075f),
                      Seg(u, v, x + size * 0.7f, cy, x, cy - size * 0.5f, 0.075f));

        static float Diamond(float u, float v, float cx, float cy, float w, float h, bool hollow)
        {
            float d = Mathf.Abs(u - cx) / (w * 0.5f) + Mathf.Abs(v - cy) / (h * 0.5f);
            if (!hollow) return Cov((1f - d) * 0.12f);
            return Cov((1f - d) * 0.12f) - Cov((0.72f - d) * 0.12f);
        }

        /// <summary>Even-odd point-in-polygon over a flat x,y array.</summary>
        static float InPoly(float u, float v, float[] pts)
        {
            bool inside = false;
            int n = pts.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = pts[i * 2], yi = pts[i * 2 + 1];
                float xj = pts[j * 2], yj = pts[j * 2 + 1];
                if ((yi > v) != (yj > v) &&
                    u < (xj - xi) * (v - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
            return inside ? 1f : 0f;
        }

        /// <summary>Signed distance to coverage, softened by roughly one texel.</summary>
        static float Cov(float signedDistance) =>
            Mathf.Clamp01(signedDistance * Size + 0.5f);

        // ------------------------------------------------------------ baking

        static Sprite Get(string key, Func<float, float, float> shape)
        {
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "UiIcon_" + key,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // 3x3 supersample: these are hairline strokes at 16–20 px on
                    // screen and alias badly at one sample per texel.
                    float a = 0f;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            a += Mathf.Clamp01(shape((x + (sx + 0.5f) / 3f) / Size,
                                                     (y + (sy + 0.5f) / 3f) / Size));
                    px[y * Size + x] = new Color(1f, 1f, 1f, a / 9f);
                }

            tex.SetPixels(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            _cache[key] = sprite;
            return sprite;
        }
    }
}
