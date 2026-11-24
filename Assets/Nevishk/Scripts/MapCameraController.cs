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
        public float minDistance = 55f;          // zoom cap: closest the camera gets
        public float maxDistance = 1400f;
        public float minPitch = 20f;
        public float maxPitch = 85f;
        public Rect panBounds = new Rect(-640, -300, 1280, 600);

        [Header("Feel")]
        public float rotateSpeed = 0.25f;
        public float zoomSpeed = 0.15f;
        public float smoothing = 10f;
        [Tooltip("Scroll/pinch zooms toward the point under the cursor or fingers, like an online map.")]
        public bool zoomToCursor = true;

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

        bool _reportedError, _reportedNaN;
        string _lastBlock = "";

        void Update()
        {
            try
            {
                HandleTouch();
                if (MapInput.TouchCount == 0) HandleMouse();
            }
            catch (System.Exception e)
            {
                // Never let an input error freeze the camera; report it once.
                if (!_reportedError) { _reportedError = true; Debug.LogError("[Nevishk] Camera input error (camera keeps running): " + e); }
            }
            ClampTargets();
            GuardAgainstNaN();

            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            focus = Vector3.Lerp(focus, _focusT, k);
            distance = Mathf.Lerp(distance, _distT, k);
            yaw = Mathf.LerpAngle(yaw, _yawT, k);
            pitch = Mathf.Lerp(pitch, _pitchT, k);
            Apply();

#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f2Key.wasPressedThisFrame) _showDebug = !_showDebug;
#else
            if (Input.GetKeyDown(KeyCode.F2)) _showDebug = !_showDebug;
#endif
        }

        static bool Bad(float f) => float.IsNaN(f) || float.IsInfinity(f);
        static bool Bad(Vector3 v) => Bad(v.x) || Bad(v.y) || Bad(v.z);

        /// If any target became NaN/∞ (which freezes the view), fall back to a sane overview.
        void GuardAgainstNaN()
        {
            if (!Bad(_focusT) && !Bad(_distT) && !Bad(_yawT) && !Bad(_pitchT) &&
                !Bad(focus) && !Bad(distance) && !Bad(yaw) && !Bad(pitch)) return;
            if (!_reportedNaN) { _reportedNaN = true; Debug.LogWarning("[Nevishk] Camera target became invalid (NaN); reset to overview."); }
            _focusT = focus = new Vector3(panBounds.center.x, 0f, panBounds.center.y);
            _distT = distance = Mathf.Clamp(maxDistance * 0.7f, minDistance, maxDistance);
            _yawT = yaw = 0f; _pitchT = pitch = 55f;
        }

        // ---- F2: on-screen input diagnostics
        bool _showDebug;
        void OnGUI()
        {
            if (!_showDebug) return;
            var es = EventSystem.current;
            string over = es == null ? "no EventSystem" : (es.IsPointerOverGameObject() ? "YES" : "no");
            string text =
                $"Nevishk camera (F2 to hide)\n" +
                $"pointer {MapInput.PointerPosition}  touches {MapInput.TouchCount}\n" +
                $"LMB held {MapInput.PrimaryHeld}  RMB held {MapInput.SecondaryHeld}  scroll {MapInput.Scroll:F2}\n" +
                $"keys pan {MapInput.KeyPan}  typing in UI {MapInput.TypingInUI}\n" +
                $"pointer over UI: {over}  {_lastBlock}\n" +
                $"press started over UI {_pressStartedOverUI}  dragging {IsDragging}\n" +
                $"focus {focus:F0} → {_focusT:F0}\n" +
                $"distance {distance:F0} → {_distT:F0}  yaw {yaw:F0}  pitch {pitch:F0}\n" +
                $"bounds {panBounds}  enabled {enabled}";
            GUI.Box(new Rect(10, Screen.height - 190, 560, 180), GUIContent.none);
            GUI.Label(new Rect(18, Screen.height - 186, 548, 176), text);
        }

        void HandleMouse()
        {
            Vector2 p = MapInput.PointerPosition;

            if (MapInput.PrimaryDown || MapInput.SecondaryDown)
            {
                _pressStartedOverUI = PointerOverUI();
                _lastPointer = p; _pressPos = p; _dragged = false;
                if (_pressStartedOverUI) ReportBlockingUI(p);
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
                ZoomAt(p, Mathf.Pow(1f - zoomSpeed, Mathf.Clamp(scroll, -3f, 3f)));

            HandleKeys();
        }

        void HandleKeys()
        {
            if (MapInput.TypingInUI) return;
            float dt = Time.unscaledDeltaTime;
            Vector2 pan = MapInput.KeyPan;
            if (pan.sqrMagnitude > 0f)
            {
                // Move about 60% of the visible width per second, whatever the zoom.
                float speed = distance * 0.6f;
                Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
                _focusT += (yawRot * Vector3.right * pan.x + yawRot * Vector3.forward * pan.y) * speed * dt;
            }
            _yawT += MapInput.KeyRotate * 90f * dt;
            float z = MapInput.KeyZoom;
            if (z != 0f) ZoomAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), Mathf.Pow(1f - zoomSpeed, z * dt * 8f));
        }

        bool _reportedBlock;

        /// One-time Console note naming the UI that swallowed a click, to diagnose "the map won't drag".
        void ReportBlockingUI(Vector2 screenPos)
        {
            if (EventSystem.current == null) return;
            bool log = !_reportedBlock;
            _reportedBlock = true;
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            var names = new System.Text.StringBuilder();
            foreach (var h in hits) names.Append(h.gameObject.name).Append(" (").Append(h.module != null ? h.module.GetType().Name : "?").Append("); ");
            if (log) Debug.Log($"[Nevishk] A drag started over UI and was ignored by the camera. Under the cursor: {(hits.Count > 0 ? names.ToString() : "nothing reported by raycasters")}");
            _lastBlock = hits.Count > 0 ? "(" + hits[0].gameObject.name + ")" : "";
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
                    ZoomAt((a + b) * 0.5f, _lastPinchDist / Mathf.Max(dist, 1f));
                    _yawT += Mathf.DeltaAngle(_lastPinchAngle, angle);
                }
                _lastPinchDist = dist; _lastPinchAngle = angle;
                _held = true; _dragged = true;
                _lastPointer = (a + b) * 0.5f;
            }
            if (n < 2) _lastPinchDist = 0f;
        }

        /// Zoom by factor (&lt;1 = in), keeping the ground point under screenPos fixed on screen.
        void ZoomAt(Vector2 screenPos, float factor)
        {
            float newDist = Mathf.Clamp(_distT * factor, minDistance, maxDistance);
            float k = newDist / _distT;
            if (Mathf.Abs(k - 1f) < 1e-4f) return;            // already at the cap
            if (zoomToCursor && TryGroundPoint(screenPos, out var hit))
            {
                Vector3 off = _focusT - hit;
                _focusT.x = hit.x + off.x * k;
                _focusT.z = hit.z + off.z * k;
                _focusT.y = Mathf.Lerp(hit.y, _focusT.y, k);
            }
            _distT = newDist;
        }

        /// Where the ray under a screen point meets the ground (terrain if known, else the focus plane).
        bool TryGroundPoint(Vector2 screenPos, out Vector3 point)
        {
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (groundHeight != null)
            {
                float step = 6f, prev = 0f;
                for (float t = step; t < _cam.farClipPlane; t += step)
                {
                    Vector3 q = ray.GetPoint(t);
                    if (q.y <= groundHeight(q))
                    {
                        // Refine between the last two samples.
                        float lo = prev, hi = t;
                        for (int i = 0; i < 8; i++)
                        {
                            float mid = (lo + hi) * 0.5f; Vector3 m = ray.GetPoint(mid);
                            if (m.y <= groundHeight(m)) hi = mid; else lo = mid;
                        }
                        point = ray.GetPoint(hi);
                        return true;
                    }
                    prev = t;
                    if (q.y < -60f) break;
                }
            }
            var plane = new Plane(Vector3.up, new Vector3(0f, _focusT.y, 0f));
            if (plane.Raycast(ray, out float d)) { point = ray.GetPoint(d); return true; }
            point = default;
            return false;
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
