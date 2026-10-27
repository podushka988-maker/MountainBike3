using UnityEngine;
using UnityEngine.EventSystems;

namespace BikeGame
{
    /// <summary>Кнопка меню «живёт»: при наведении чуть увеличивается, при нажатии утапливается, слегка покачивается в покое.</summary>
    public class ButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public float hoverScale = 1.05f, pressScale = 0.93f;
        [Tooltip("Покачивание в покое (как табличка на ветру), °.")]
        public float sway = 0.8f;
        private bool _down, _over;
        private Vector3 _base;
        private float _s = 1f, _phase;

        private void Awake() { _base = transform.localScale; _phase = Random.value * 10f; }
        public void OnPointerDown(PointerEventData e) => _down = true;
        public void OnPointerUp(PointerEventData e) => _down = false;
        public void OnPointerEnter(PointerEventData e) => _over = true;
        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }

        private void Update()
        {
            float target = _down ? pressScale : (_over ? hoverScale : 1f);
            _s = Mathf.MoveTowards(_s, target, Time.unscaledDeltaTime * 1.5f);
            transform.localScale = _base * _s;
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 1.3f + _phase) * sway);
        }
    }
}
