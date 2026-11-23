using TMPro;
using UnityEngine;

namespace Nevishk
{
    public enum PinKind { District, Landmark }

    /// <summary>Clickable marker for a district or landmark, with a camera-facing label.</summary>
    public class MapPin : MonoBehaviour
    {
        public PinKind kind;
        public string id;
        public Transform head;          // the visible marker that scales on hover
        public TextMeshPro label;
        public SpriteRenderer plate;            // dark box behind the label; fades with it
        public float labelShowDistance = 450f;   // landmarks: label visible when camera is closer than this
        public bool hasLore;

        Vector3 _baseScale;
        bool _hovered, _selected;
        Camera _cam;

        void Start()
        {
            _cam = Camera.main;
            if (head != null) _baseScale = head.localScale;
        }

        public void SetHovered(bool v) => _hovered = v;
        public void SetSelected(bool v) => _selected = v;

        void LateUpdate()
        {
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }

            if (head != null)
            {
                float target = _selected ? 1.6f : _hovered ? 1.35f : 1f;
                head.localScale = Vector3.Lerp(head.localScale, _baseScale * target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            }

            if (label != null)
            {
                // Billboard: face the camera, keep upright.
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - _cam.transform.position, _cam.transform.up);

                float d = Vector3.Distance(_cam.transform.position, transform.position);
                float alpha;
                if (kind == PinKind.District)
                    alpha = Mathf.InverseLerp(60f, 160f, d);     // fade district names when very close
                else
                    alpha = (_hovered || _selected) ? 1f : Mathf.InverseLerp(labelShowDistance, labelShowDistance * 0.7f, d);
                label.alpha = Mathf.Lerp(label.alpha, alpha, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
                if (plate != null)
                {
                    var c = NevishkTheme.LabelPlate; c.a *= label.alpha;
                    plate.color = c;
                    plate.enabled = label.alpha > 0.01f;
                }

                // Landmark pins grow with distance so they stay visible from the overview;
                // labels keep a roughly constant on-screen size either way.
                float grow = kind == PinKind.Landmark ? Mathf.Clamp(d / 420f, 1f, 2.2f) : 1f;
                if (kind == PinKind.Landmark) transform.localScale = Vector3.one * grow;
                float s = Mathf.Clamp(d / 600f, 0.15f, 2.5f);
                label.transform.localScale = Vector3.one * (s / grow);
            }
        }
    }
}
