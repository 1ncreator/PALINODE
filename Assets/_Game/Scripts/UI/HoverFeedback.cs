using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Palinode.UI
{
    /// <summary>
    /// Visible hover/selection state for menu controls: the pointer selects the control (so mouse and keyboard/gamepad
    /// share one highlight), the label brightens and the control grows slightly. Uses unscaled time (menus may run paused).
    /// </summary>
    public class HoverFeedback : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
    {
        public static readonly Color LabelHighlight = new Color(1f, 0.84f, 0.52f);

        public TextMeshProUGUI Label;
        public float Scale = 1.05f;
        public float Speed = 14f;

        private Selectable _selectable;
        private Color _labelNormal;
        private bool _captured;   // Label is assigned right after AddComponent, i.e. after Awake/OnEnable
        private bool _active;
        private float _t;

        private void Awake() => _selectable = GetComponent<Selectable>();

        private void OnEnable()
        {
            _active = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            _t = _active ? 1f : 0f;
            Apply();
        }

        private void OnDisable()
        {
            _active = false;
            _t = 0f;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_selectable != null && _selectable.IsInteractable() && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSelect(BaseEventData eventData) => _active = true;
        public void OnDeselect(BaseEventData eventData) => _active = false;

        private void Update()
        {
            float target = _active ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Speed * Time.unscaledDeltaTime);
            Apply();
        }

        private void Apply()
        {
            float e = _t * _t * (3f - 2f * _t);
            transform.localScale = Vector3.one * Mathf.Lerp(1f, Scale, e);
            if (Label == null) return;
            if (!_captured) { _labelNormal = Label.color; _captured = true; }
            Label.color = Color.Lerp(_labelNormal, LabelHighlight, e);
        }
    }
}
