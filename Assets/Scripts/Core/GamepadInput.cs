using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// A gamepad, expressed as the signals the rest of the game already reads.
    ///
    /// The companion to <see cref="TouchInput"/>, and there for the same reason:
    /// this game is built on a mouse and a keyboard, and the **Steam Deck has
    /// neither**. Half the map editor's verbs are keys — WASD pans, Q/E rotate,
    /// R/F zoom, C faces a formation, Ctrl+Z undoes, Tab opens the casualty
    /// list — and on a handheld none of them exist unless something puts them
    /// back.
    ///
    /// | Keyboard / mouse | Pad |
    /// |---|---|
    /// | WASD pan | **Left stick** |
    /// | Q / E rotate | **Right stick, horizontally** |
    /// | R / F zoom | **Triggers** |
    /// | Middle-drag tilt | **Right stick, vertically** |
    /// | **Right click** | **B** — <see cref="SecondaryDown"/> |
    /// | `C` face a formation | **X** |
    /// | `Esc` cancel | **B**, which is also Unity's own Cancel |
    /// | `Tab` casualties | **Back / View** |
    ///
    /// **Steam Input is not a reason to skip this.** A Deck can be told to send
    /// WASD and a mouse, and for an unported game that is the whole answer — but
    /// it is a per-player configuration, it shows the wrong glyphs, and it makes
    /// the right stick a mouse that has to be dragged across a 7-inch screen to
    /// reach the far side of the map. A game that reads the pad directly works
    /// under the *default* template, which is the one nearly everybody uses.
    ///
    /// **The axis numbers differ between Windows and Linux.** The right stick
    /// and the triggers are the same physical controls and different axes on
    /// each — the legacy Input System's oldest wart. Both sets are declared in
    /// <c>ProjectSettings/InputManager.asset</c> and chosen between here, once,
    /// so nothing downstream ever learns about it.
    ///
    /// Everything reports zero with no pad attached, so a desktop build behaves
    /// exactly as it did. See docs/42-STEAM-DECK.md.
    /// </summary>
    public static class GamepadInput
    {
        /// <summary>
        /// Below this a stick is being held still, not pushed. Generous: a Deck
        /// that has been in a bag has stick drift, and a map that pans on its
        /// own while nobody is touching it is the most obvious possible fault.
        /// </summary>
        public const float StickDeadZone = 0.22f;

        /// <summary>Below this a trigger is being rested on, not pulled.</summary>
        public const float TriggerDeadZone = 0.12f;

        static bool _linuxAxes;
        static bool _probed;

        /// <summary>
        /// True when a pad is attached. Polled rather than cached: a Deck in a
        /// dock has controllers coming and going, and a game that decided at
        /// startup would be wrong for the rest of the session.
        /// </summary>
        public static bool Present
        {
            get
            {
                var names = Input.GetJoystickNames();
                foreach (var n in names)
                    if (!string.IsNullOrEmpty(n)) return true;
                return false;
            }
        }

        /// <summary>
        /// Which set of axis names this platform uses. SteamOS is Linux, so a
        /// Deck running a native build takes the Linux set; a Deck running the
        /// Windows build under Proton takes the Windows one, because Proton
        /// presents XInput.
        /// </summary>
        static bool LinuxAxes
        {
            get
            {
                if (_probed) return _linuxAxes;
                _probed = true;
                _linuxAxes = Application.platform == RuntimePlatform.LinuxPlayer ||
                             Application.platform == RuntimePlatform.LinuxEditor;
                return _linuxAxes;
            }
        }

        // ------------------------------------------------------------ sticks

        /// <summary>
        /// The left stick, dead-zoned. The pan control — same meaning as WASD,
        /// and it shares the axis with them, so the keyboard still works
        /// alongside it on a desktop with a pad plugged in.
        /// </summary>
        public static Vector2 LeftStick =>
            DeadZone(new Vector2(Axis("Horizontal"), Axis("Vertical")), StickDeadZone);

        /// <summary>
        /// The right stick, dead-zoned. X orbits, Y tilts — the two halves of
        /// what a middle-mouse drag does, kept in the same relationship so a
        /// player who learns one learns the other.
        /// </summary>
        public static Vector2 RightStick => DeadZone(new Vector2(
            Axis(LinuxAxes ? "PadRightStickX_Linux" : "PadRightStickX"),
            Axis(LinuxAxes ? "PadRightStickY_Linux" : "PadRightStickY")), StickDeadZone);

        /// <summary>
        /// The triggers as one signed zoom control: **positive pulls in**.
        ///
        /// XInput folds both onto a single axis — left positive, right negative,
        /// both cancelling — which is its design rather than a bug to work
        /// around, so it is read as the one control it already is. Linux reports
        /// them separately and they are subtracted into the same shape.
        /// </summary>
        public static float Zoom
        {
            get
            {
                float value = LinuxAxes
                    ? Axis("PadRightTrigger_Linux") - Axis("PadLeftTrigger_Linux")
                    : -Axis("PadTriggers");
                return Mathf.Abs(value) < TriggerDeadZone ? 0f : value;
            }
        }

        // ----------------------------------------------------------- buttons

        /// <summary>A — confirm. Unity's own Submit, so uGUI already answers to it.</summary>
        public static bool ConfirmDown => Input.GetKeyDown(KeyCode.JoystickButton0);

        /// <summary>
        /// B — the right mouse button.
        ///
        /// The pad has no second click either, and right-click here is the move
        /// order, the context menu and the cancel on every armed tool. B is
        /// where "the other thing" lives on every pad ever made, and it is also
        /// Unity's Cancel, so the two meanings never disagree.
        /// </summary>
        public static bool SecondaryDown => Input.GetKeyDown(KeyCode.JoystickButton1);

        /// <summary>X — face a formation, which is <c>C</c> on a keyboard.</summary>
        public static bool FaceDown => Input.GetKeyDown(KeyCode.JoystickButton2);

        /// <summary>Y — the spare. Nothing is bound to it yet; see docs/42 §7.</summary>
        public static bool SpareDown => Input.GetKeyDown(KeyCode.JoystickButton3);

        /// <summary>Left bumper — step back through a list, the info panel's ◄.</summary>
        public static bool PreviousDown => Input.GetKeyDown(KeyCode.JoystickButton4);

        /// <summary>Right bumper — step forward, the info panel's ►.</summary>
        public static bool NextDown => Input.GetKeyDown(KeyCode.JoystickButton5);

        /// <summary>Back / View — the casualty list, which is <c>Tab</c>.</summary>
        public static bool ListDown => Input.GetKeyDown(KeyCode.JoystickButton6);

        /// <summary>Start / Menu — the pause menu, which is <c>Esc</c>.</summary>
        public static bool MenuDown => Input.GetKeyDown(KeyCode.JoystickButton7);

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// One axis, or zero if it is not in the input manager.
        ///
        /// A project whose <c>InputManager.asset</c> has not been updated throws
        /// on every frame from <c>GetAxis</c>, which turns a missing setting into
        /// an unreadable console. Reported once instead, and the game carries on
        /// with a pad that does less than it should.
        /// </summary>
        static float Axis(string name)
        {
            try { return Input.GetAxis(name); }
            catch (System.Exception)
            {
                if (_missing.Add(name))
                    Debug.LogWarning($"[GamepadInput] No '{name}' axis in the input manager — " +
                                     "that pad control does nothing. See docs/42-STEAM-DECK.md §3.");
                return 0f;
            }
        }

        static readonly System.Collections.Generic.HashSet<string> _missing =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>
        /// Dead-zoned **radially**, not per axis.
        ///
        /// Testing each axis on its own carves a cross out of the stick's range,
        /// so a diagonal push that is well past the threshold on neither axis
        /// reads as nothing while a straight one works. Measuring the magnitude
        /// and rescaling what is left also means the control starts from zero at
        /// the edge of the dead zone rather than jumping to it.
        /// </summary>
        static Vector2 DeadZone(Vector2 raw, float threshold)
        {
            float magnitude = raw.magnitude;
            if (magnitude < threshold) return Vector2.zero;
            return raw.normalized * Mathf.InverseLerp(threshold, 1f, Mathf.Min(magnitude, 1f));
        }
    }
}
