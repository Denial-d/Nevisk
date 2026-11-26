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
        // Ask the Input System every time: Unity switches EnhancedTouch off when Play stops, and with
        // "Enter Play Mode Options" (no domain reload) a remembered flag would wrongly say it is still on.
        static void EnsureTouch()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
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

        // Input System 1.8+ reports about ±1 per wheel notch; older versions (and some platforms) report ±120.
        public static float Scroll
        {
            get
            {
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                return Mathf.Abs(y) >= 20f ? y / 120f : y;
            }
        }

        /// Keyboard camera controls: x = right, y = forward (WASD / arrows), rotate (Q/E), zoom (R/F, +/-).
        public static Vector2 KeyPan
        {
            get
            {
                var k = Keyboard.current; if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
                float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
                return new Vector2(x, y);
            }
        }
        public static float KeyRotate { get { var k = Keyboard.current; return k == null ? 0 : (k.eKey.isPressed ? 1 : 0) - (k.qKey.isPressed ? 1 : 0); } }
        public static float KeyZoom
        {
            get
            {
                var k = Keyboard.current; if (k == null) return 0;
                return (k.rKey.isPressed || k.equalsKey.isPressed || k.numpadPlusKey.isPressed ? 1 : 0)
                     - (k.fKey.isPressed || k.minusKey.isPressed || k.numpadMinusKey.isPressed ? 1 : 0);
            }
        }
        /// True while the user is typing in a UI text field (so WASD don't move the camera).
        public static bool TypingInUI =>
            UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null;
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
        public static Vector2 KeyPan => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        public static float KeyRotate => (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
        public static float KeyZoom => (Input.GetKey(KeyCode.R) ? 1 : 0) - (Input.GetKey(KeyCode.F) ? 1 : 0);
        public static bool TypingInUI =>
            UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null;
        public static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static bool EscapeDown => Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
