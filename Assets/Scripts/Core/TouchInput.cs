using UnityEngine;
using UnityEngine.EventSystems;

namespace IronMeridian.Core
{
    /// <summary>
    /// The gestures a touch screen has, expressed as the signals the rest of the
    /// game already reads.
    ///
    /// **Why a translation layer rather than a port.** This game is built on
    /// legacy <c>UnityEngine.Input</c> and a mouse: left-click selects,
    /// right-click orders, the wheel zooms, the middle button orbits, WASD pans.
    /// Unity already maps a single touch onto mouse button 0 and
    /// <c>Input.mousePosition</c>, so tapping to select, to place and to press a
    /// button works on Android with nothing done to it. What has no touch
    /// equivalent at all is **the right button, the wheel and the middle
    /// button** — and those carry ordering a move, zooming and orbiting, which is
    /// most of what the game is.
    ///
    /// So the gestures are defined here once and read from the handful of places
    /// that need them, instead of every one of the thirty-odd
    /// <c>Input.GetMouseButton</c> call sites learning about touch.
    ///
    /// | Mouse | Touch |
    /// |---|---|
    /// | Left click | Tap |
    /// | **Right click** | **Long press** — <see cref="SecondaryDown"/> |
    /// | **Wheel** | **Pinch** — <see cref="PinchDelta"/> |
    /// | **Middle drag** | **Two-finger twist / drag** — <see cref="TwistDegrees"/>, <see cref="TwoFingerDrag"/> |
    /// | WASD | **One-finger drag on the map** — <see cref="PanDelta"/> |
    ///
    /// **A long press is a right click, and it commits on release.** Holding
    /// still for <see cref="LongPressSeconds"/> and then lifting is the gesture;
    /// moving more than <see cref="DragSlopPixels"/> cancels it, because a press
    /// that turned into a drag was a pan. Firing on the *timer* rather than the
    /// lift was tried first and is wrong: it means the map acts under a finger
    /// that has not decided yet.
    ///
    /// Everything here reports zero on a platform with no touch screen, so a
    /// desktop build behaves exactly as it did. See docs/40-ANDROID.md.
    /// </summary>
    public static class TouchInput
    {
        /// <summary>How long a finger must rest before a press counts as a right click.</summary>
        public const float LongPressSeconds = 0.45f;
        /// <summary>How far a finger may wander and still be a press rather than a drag.</summary>
        public const float DragSlopPixels = 24f;

        /// <summary>
        /// Pinch distance converted to the wheel's units, so a caller can feed it
        /// to the same zoom maths <c>Input.GetAxis("Mouse ScrollWheel")</c>
        /// drives. One wheel notch is about 0.1, and a hundred pixels of pinch is
        /// about a notch.
        /// </summary>
        const float PinchToWheel = 0.001f;

        /// <summary>True on a build whose primary pointer is a finger.</summary>
        public static bool Active => Input.touchSupported && Input.touchCount > 0;

        /// <summary>True on a platform where touch is the expected way to play.</summary>
        public static bool IsTouchPlatform =>
            Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer;

        // ------------------------------------------------------- long press

        static int _pressFinger = -1;
        static Vector2 _pressStart;
        static float _pressBegan;
        static bool _pressValid;
        static bool _secondaryDown;
        static Vector2 _secondaryPos;

        /// <summary>
        /// True for the one frame a long press is released — the touch screen's
        /// right click. <see cref="SecondaryPosition"/> is where it happened.
        /// </summary>
        public static bool SecondaryDown => _secondaryDown;

        /// <summary>Screen position of the long press that <see cref="SecondaryDown"/> reports.</summary>
        public static Vector2 SecondaryPosition => _secondaryPos;

        /// <summary>
        /// True while a finger has been held long enough to become a right click
        /// but has not been lifted yet — what a "hold to order" hint would show.
        /// </summary>
        public static bool LongPressPending =>
            _pressValid && _pressFinger >= 0 &&
            Time.unscaledTime - _pressBegan >= LongPressSeconds;

        // --------------------------------------------------------- gestures

        static Vector2 _panDelta;
        static float _pinchDelta;
        static float _twistDegrees;
        static Vector2 _twoFingerDrag;

        /// <summary>One finger dragging, in screen pixels this frame. The pan gesture.</summary>
        public static Vector2 PanDelta => _panDelta;
        /// <summary>Pinch this frame, in mouse-wheel units. Positive spreads the fingers — zoom in.</summary>
        public static float PinchDelta => _pinchDelta;
        /// <summary>Two-finger twist this frame, degrees clockwise. The orbit gesture.</summary>
        public static float TwistDegrees => _twistDegrees;
        /// <summary>Two fingers dragging together, in screen pixels. The tilt gesture.</summary>
        public static Vector2 TwoFingerDrag => _twoFingerDrag;

        // ----------------------------------------------------------- update

        static int _frame = -1;
        static Vector2 _lastPinchA, _lastPinchB;
        static bool _pinching;

        /// <summary>
        /// Reads the touch screen for this frame.
        ///
        /// Called from every place that reads a gesture, and guarded on the frame
        /// number so the order those places run in cannot matter — the first one
        /// this frame does the work and the rest see the same answer. A single
        /// driver component would be tidier and would also be one more thing that
        /// has to exist in sixteen scenes before the camera works.
        /// </summary>
        public static void Poll()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;

            _secondaryDown = false;
            _panDelta = Vector2.zero;
            _pinchDelta = 0f;
            _twistDegrees = 0f;
            _twoFingerDrag = Vector2.zero;

            if (!Input.touchSupported || Input.touchCount == 0)
            {
                _pressFinger = -1;
                _pressValid = false;
                _pinching = false;
                return;
            }

            if (Input.touchCount >= 2) PollTwoFinger();
            else PollOneFinger();
        }

        static void PollOneFinger()
        {
            _pinching = false;
            var touch = Input.GetTouch(0);

            // Over the interface, the canvas owns the finger: a drag on a
            // scrolling list must not also pan the map underneath it.
            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject(touch.fingerId);

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    _pressFinger = overUI ? -1 : touch.fingerId;
                    _pressStart = touch.position;
                    _pressBegan = Time.unscaledTime;
                    _pressValid = !overUI;
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (touch.fingerId != _pressFinger) break;
                    if ((touch.position - _pressStart).sqrMagnitude >
                        DragSlopPixels * DragSlopPixels)
                    {
                        // It became a drag, so it is a pan and never a right
                        // click. The pan starts from the moment the slop is
                        // broken rather than from the touch-down, or the map
                        // would jump by the slop on the first frame of a drag.
                        _pressValid = false;
                    }
                    if (!_pressValid) _panDelta = touch.deltaPosition;
                    break;

                case TouchPhase.Ended:
                    if (touch.fingerId == _pressFinger && _pressValid &&
                        Time.unscaledTime - _pressBegan >= LongPressSeconds)
                    {
                        _secondaryDown = true;
                        _secondaryPos = touch.position;
                    }
                    _pressFinger = -1;
                    _pressValid = false;
                    break;

                case TouchPhase.Canceled:
                    _pressFinger = -1;
                    _pressValid = false;
                    break;
            }
        }

        static void PollTwoFinger()
        {
            // A second finger means the first one was never a press.
            _pressFinger = -1;
            _pressValid = false;

            var a = Input.GetTouch(0);
            var b = Input.GetTouch(1);

            if (!_pinching)
            {
                _pinching = true;
                _lastPinchA = a.position;
                _lastPinchB = b.position;
                return;
            }

            Vector2 wasSpan = _lastPinchB - _lastPinchA;
            Vector2 nowSpan = b.position - a.position;

            _pinchDelta = (nowSpan.magnitude - wasSpan.magnitude) * PinchToWheel;
            _twistDegrees = Vector2.SignedAngle(nowSpan, wasSpan);

            // What both fingers did in common, which is the tilt gesture: the
            // spread and the twist have already been taken out of it above.
            _twoFingerDrag = ((a.deltaPosition + b.deltaPosition) * 0.5f);

            _lastPinchA = a.position;
            _lastPinchB = b.position;
        }
    }
}
