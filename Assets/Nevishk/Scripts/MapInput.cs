using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace Nevishk
{
    /// <summary>
    /// Thin wrapper so the map works with either the new Input System (Unity 6 default)
    /// or the legacy Input Manager, on desktop and touch screens.
    /// </summary>
    public static class MapInput
    {
#if ENABLE_INPUT_SYSTEM
        static bool _touchEnabled;
        static void EnsureTouch()
        {
            if (_touchEnabled) return;
            EnhancedTouchSupport.Enable();
            _touchEnabled = true;
        }

        public static int TouchCount { get { EnsureTouch(); return Touch.activeTouches.Count; } }
        public static Vector2 TouchPosition(int i) { EnsureTouch(); return Touch.activeTouches[i].screenPosition; }

        public static Vector2 PointerPosition =>
            TouchCount > 0 ? TouchPosition(0) : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        public static bool PrimaryDown => TouchCount == 1
            ? Touch.activeTouches[0].phase == UnityEngine.InputSystem.TouchPhase.Began
            : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool PrimaryHeld => TouchCount == 1 || (Mouse.current != null && Mouse.current.leftButton.isPressed);
        public static bool PrimaryUp => TouchCount == 1
            ? Touch.activeTouches[0].phase == UnityEngine.InputSystem.TouchPhase.Ended
            : Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;

        public static bool SecondaryHeld => Mouse.current != null &&
            (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.isPressed);
        public static bool SecondaryDown => Mouse.current != null &&
            (Mouse.current.rightButton.wasPressedThisFrame || Mouse.current.middleButton.wasPressedThisFrame);

        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        public static bool EscapeDown => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        public static int TouchCount => Input.touchCount;
        public static Vector2 TouchPosition(int i) => Input.GetTouch(i).position;
        public static Vector2 PointerPosition => Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
        public static bool PrimaryDown => Input.touchCount == 1 ? Input.GetTouch(0).phase == TouchPhase.Began : Input.GetMouseButtonDown(0);
        public static bool PrimaryHeld => Input.touchCount == 1 || Input.GetMouseButton(0);
        public static bool PrimaryUp => Input.touchCount == 1 ? Input.GetTouch(0).phase == TouchPhase.Ended : Input.GetMouseButtonUp(0);
        public static bool SecondaryHeld => Input.GetMouseButton(1) || Input.GetMouseButton(2);
        public static bool SecondaryDown => Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static bool EscapeDown => Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
