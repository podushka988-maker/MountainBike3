using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>
    /// Экранная кнопка: удержание (газ, тормоз, наклон) и одиночные нажатия (передачи + / −).
    /// Мультитач: можно одновременно жать газ и наклон. Каждый палец учитывается отдельно.
    /// Скольжение пальцем:
    ///   • Slide In — палец, уже прижатый к экрану, заехал на кнопку — она нажимается (уехал — отпускается);
    ///   • Keep While Dragging — кнопка держится, пока палец, который её нажал, прижат к экрану, даже если он съехал.
    /// Так одним большим пальцем: прижал ГАЗ, сдвинул палец на ТОРМОЗ — работают оба; вернул на газ — тормоз отпущен.
    /// Нажатая кнопка становится ярче и слегка «утапливается» (уменьшается). Повесьте на UI-объект с Image.
    /// </summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public bool IsHeld => _pressed.Count > 0 || _slid.Count > 0;
        /// <summary>Палец только что коснулся кнопки (одно событие на нажатие; скольжение на кнопку его не вызывает).</summary>
        public event Action Pressed;

        [Tooltip("Палец, уже прижатый к экрану, заехал на кнопку — она нажимается.")]
        public bool slideIn = false;
        [Tooltip("Кнопка держится, пока нажавший её палец прижат к экрану, даже если он съехал с кнопки.")]
        public bool keepWhileDragging = false;

        [Range(0f, 1f)] public float idleAlpha = 0.8f;
        [Range(0f, 1f)] public float pressedAlpha = 1f;
        [Tooltip("Яркость кнопки без нажатия (1 — как нарисована).")]
        [Range(0.5f, 1f)] public float idleBrightness = 0.88f;
        [Tooltip("Насколько кнопка уменьшается при нажатии.")]
        [Range(0.7f, 1f)] public float pressedScale = 0.9f;

        private readonly HashSet<int> _pressed = new HashSet<int>();   // пальцы, которые нажали эту кнопку
        private readonly HashSet<int> _slid = new HashSet<int>();      // пальцы, которые заехали на неё скольжением
        private Graphic _graphic;
        private Vector3 _baseScale = Vector3.one;
        private float _scale = 1f;
        private bool _wasHeld;

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
            _baseScale = transform.localScale;
            Refresh();
        }

        public void OnPointerDown(PointerEventData e)
        {
            _slid.Remove(e.pointerId);
            _pressed.Add(e.pointerId);
            Refresh();
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData e)
        {
            // палец оторвали от экрана (событие приходит кнопке, которую он нажал, где бы он ни был)
            _pressed.Remove(e.pointerId);
            _slid.Remove(e.pointerId);
            Refresh();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            // заехал на кнопку пальцем, который уже прижат (нажал что-то другое) — нажимаем
            if (!slideIn || _pressed.Contains(e.pointerId) || e.pointerPress == null) return;
            _slid.Add(e.pointerId);
            Refresh();
        }

        public void OnPointerExit(PointerEventData e)
        {
            _slid.Remove(e.pointerId);
            if (!keepWhileDragging || e.pointerPress == null) _pressed.Remove(e.pointerId);   // съехал (или отпустил) — отпускаем
            Refresh();
        }

        private void OnDisable()
        {
            _pressed.Clear();
            _slid.Clear();
            _scale = 1f;
            transform.localScale = _baseScale;
            Refresh();
        }

        private void Update()
        {
            float target = IsHeld ? pressedScale : 1f;
            if (IsHeld != _wasHeld) Refresh();
            if (Mathf.Approximately(_scale, target)) return;
            _scale = Mathf.MoveTowards(_scale, target, Time.unscaledDeltaTime * 2.5f);
            transform.localScale = _baseScale * _scale;
        }

        private void Refresh()
        {
            _wasHeld = IsHeld;
            if (_graphic == null) return;
            float k = IsHeld ? 1f : idleBrightness;
            _graphic.color = new Color(k, k, k, IsHeld ? pressedAlpha : idleAlpha);
        }
    }
}
