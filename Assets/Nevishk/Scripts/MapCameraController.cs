using UnityEngine;
using UnityEngine.EventSystems;

namespace Nevishk
{
    /// <summary>
    /// Strategy-map camera: left-drag pans, right/middle-drag (or Shift+left) orbits,
    /// wheel zooms. Touch: one finger pans, two fingers pinch-zoom and twist-rotate.
    /// FlyTo() smoothly moves to a point of interest.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class MapCameraController : MonoBehaviour
    {
        [Header("Framing")]
        public Vector3 focus = Vector3.zero;
        public float distance = 900f;
        public float yaw = 0f;
        [Range(15f, 89f)] public float pitch = 55f;

        [Header("Limits")]
        public float minDistance = 40f;
        public float maxDistance = 1400f;
        public float minPitch = 20f;
        public float maxPitch = 85f;
        public Rect panBounds = new Rect(-640, -300, 1280, 600);

        [Header("Feel")]
        public float rotateSpeed = 0.25f;
        public float zoomSpeed = 0.15f;
        public float smoothing = 10f;

        /// <summary>True while a button/finger is held and has moved past the drag threshold.</summary>
        public bool IsDragging => _held && _dragged;
        /// Optional: ground height under a world point, so the camera never dips into hills.
        public System.Func<Vector3, float> groundHeight;
        /// <summary>Stays true after release until the next press, so a drag never counts as a click.</summary>
        public bool LastPressWasDrag => _dragged;
        bool _held, _dragged;

        Vector3 _focusT; float _distT, _yawT, _pitchT;
        Vector2 _lastPointer; Vector2 _pressPos;
        bool _pressStartedOverUI;
        float _lastPinchDist, _lastPinchAngle;
        Camera _cam;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _focusT = focus; _distT = distance; _yawT = yaw; _pitchT = pitch;
            Apply();
        }

        public void FlyTo(Vector3 point, float newDistance = -1f, float? newPitch = null)
        {
            _focusT = point;
            if (newDistance > 0) _distT = Mathf.Clamp(newDistance, minDistance, maxDistance);
            if (newPitch.HasValue) _pitchT = Mathf.Clamp(newPitch.Value, minPitch, maxPitch);
        }

        public void ResetView(Vector3 center, float dist)
        {
            FlyTo(center, dist, 55f);
            _yawT = 0f;
        }

        static bool PointerOverUI()
        {
            if (EventSystem.current == null) return false;
            if (MapInput.TouchCount > 0)
                return EventSystem.current.IsPointerOverGameObject(0) || EventSystem.current.IsPointerOverGameObject();
            return EventSystem.current.IsPointerOverGameObject();
        }

        void Update()
        {
            HandleTouch();
            if (MapInput.TouchCount == 0) HandleMouse();
            ClampTargets();

            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            focus = Vector3.Lerp(focus, _focusT, k);
            distance = Mathf.Lerp(distance, _distT, k);
            yaw = Mathf.LerpAngle(yaw, _yawT, k);
            pitch = Mathf.Lerp(pitch, _pitchT, k);
            Apply();
        }

        void HandleMouse()
        {
            Vector2 p = MapInput.PointerPosition;

            if (MapInput.PrimaryDown || MapInput.SecondaryDown)
            {
                _pressStartedOverUI = PointerOverUI();
                _lastPointer = p; _pressPos = p; _dragged = false;
            }

            bool primary = MapInput.PrimaryHeld, secondary = MapInput.SecondaryHeld;
            if ((primary || secondary) && !_pressStartedOverUI)
            {
                Vector2 delta = p - _lastPointer;
                if ((p - _pressPos).sqrMagnitude > 36f) _dragged = true;
                if (secondary || (primary && MapInput.ShiftHeld)) Orbit(delta);
                else if (primary) Pan(delta);
            }
            _held = primary || secondary;
            if (!_held) _pressStartedOverUI = false;
            _lastPointer = p;

            float scroll = MapInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f && !PointerOverUI())
                _distT *= Mathf.Pow(1f - zoomSpeed, Mathf.Clamp(scroll, -3f, 3f));
        }

        void HandleTouch()
        {
            int n = MapInput.TouchCount;
            if (n == 1)
            {
                Vector2 p = MapInput.TouchPosition(0);
                if (MapInput.PrimaryDown)
                {
                    _pressStartedOverUI = PointerOverUI();
                    _lastPointer = p; _pressPos = p; _dragged = false;
                }
                _held = true;
                if (!_pressStartedOverUI)
                {
                    if ((p - _pressPos).sqrMagnitude > 100f) _dragged = true;
                    Pan(p - _lastPointer);
                }
                _lastPointer = p;
            }
            else if (n >= 2)
            {
                Vector2 a = MapInput.TouchPosition(0), b = MapInput.TouchPosition(1);
                float dist = Vector2.Distance(a, b);
                float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                if (_lastPinchDist > 0f)
                {
                    _distT *= _lastPinchDist / Mathf.Max(dist, 1f);
                    _yawT += Mathf.DeltaAngle(_lastPinchAngle, angle);
                }
                _lastPinchDist = dist; _lastPinchAngle = angle;
                _held = true; _dragged = true;
                _lastPointer = (a + b) * 0.5f;
            }
            if (n < 2) _lastPinchDist = 0f;
        }

        void Pan(Vector2 screenDelta)
        {
            // Convert pixels to world units at the focus depth.
            float worldPerPixel = 2f * distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 right = yawRot * Vector3.right;
            Vector3 forward = yawRot * Vector3.forward;
            _focusT -= (right * screenDelta.x + forward * screenDelta.y) * worldPerPixel;
        }

        void Orbit(Vector2 delta)
        {
            _yawT += delta.x * rotateSpeed;
            _pitchT -= delta.y * rotateSpeed;
        }

        void ClampTargets()
        {
            _distT = Mathf.Clamp(_distT, minDistance, maxDistance);
            _pitchT = Mathf.Clamp(_pitchT, minPitch, maxPitch);
            _focusT.x = Mathf.Clamp(_focusT.x, panBounds.xMin, panBounds.xMax);
            _focusT.z = Mathf.Clamp(_focusT.z, panBounds.yMin, panBounds.yMax);
        }

        void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus - rot * Vector3.forward * distance;
            if (groundHeight != null)
            {
                float floor = groundHeight(pos) + 6f;
                if (pos.y < floor) pos.y = floor;
            }
            transform.position = pos;
            transform.rotation = rot;
        }
    }
}
