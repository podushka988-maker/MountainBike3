using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BikeGame
{
    /// <summary>
    /// Лента выбора локации (после «ИГРАТЬ»): каждая локация — картинка на весь экран, название — табличка в левом нижнем углу.
    /// Листается вверх-вниз (палец, колёсико мыши, стрелки/W-S), после отпускания плавно встаёт на ближайшую локацию.
    /// Нажатие на картинку — заезд по этой локации (LocationCard). Справа — точки: на какой локации лента. Создаёт сборщик меню.
    /// </summary>
    public class LocationFeed : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        public ScrollRect scroll;
        [Tooltip("Панели локаций по порядку (дети Content).")]
        public RectTransform[] pages;
        [Tooltip("Точки справа (по одной на локацию).")]
        public Image[] dots;
        public Color dotOn = new Color(1f, 0.8f, 0.35f, 1f), dotOff = new Color(1f, 1f, 1f, 0.45f);
        [Tooltip("Как быстро лента встаёт на локацию.")]
        public float snapSpeed = 10f;

        private bool _dragging;
        private int _target = -1;
        private Vector2 _size;
        private float _wheelCd;

        /// <summary>Какая локация сейчас на экране.</summary>
        public int Current { get; private set; }

        private void OnEnable() { Layout(true); }

        /// <summary>Сразу показать локацию (без прокрутки) — например, последнюю выбранную.</summary>
        public void JumpTo(int index)
        {
            Layout(true);
            index = Mathf.Clamp(index, 0, Mathf.Max(0, pages.Length - 1));
            Current = index; _target = index;
            if (scroll != null) { scroll.StopMovement(); scroll.verticalNormalizedPosition = Norm(index); }
            Dots();
        }

        public void OnBeginDrag(PointerEventData e) { _dragging = true; _target = -1; }
        public void OnEndDrag(PointerEventData e)
        {
            _dragging = false;
            // бросок пальцем — на следующую/предыдущую, иначе — на ближайшую
            float v = scroll != null ? scroll.velocity.y : 0f;
            int near = Nearest();
            if (Mathf.Abs(v) > 600f) near = Mathf.Clamp(Current + (v > 0f ? 1 : -1), 0, pages.Length - 1);
            _target = near;
        }

        private void Update()
        {
            if (scroll == null || pages == null || pages.Length == 0) return;
            Layout(false);
            // колёсико мыши и клавиши — на соседнюю локацию
            ReadKeys(out float wheel, out bool down, out bool up, out bool enter);
            _wheelCd -= Time.unscaledDeltaTime;
            if (!_dragging && Mathf.Abs(wheel) > 0.01f && _wheelCd <= 0f) { Step(wheel < 0f ? 1 : -1); _wheelCd = 0.3f; }   // один щелчок — одна локация
            if (down) Step(1);
            if (up) Step(-1);
            if (enter)
            {
                var card = pages[Current] != null ? pages[Current].GetComponent<LocationCard>() : null;
                if (card != null) card.Pick();
            }

            if (!_dragging && _target < 0 && scroll.velocity.sqrMagnitude < 400f * 400f) _target = Nearest();
            if (!_dragging && _target >= 0)
            {
                scroll.StopMovement();
                float goal = Norm(_target);
                scroll.verticalNormalizedPosition = Mathf.Lerp(scroll.verticalNormalizedPosition, goal, 1f - Mathf.Exp(-snapSpeed * Time.unscaledDeltaTime));
            }
            int cur = Nearest();
            if (cur != Current) { Current = cur; Dots(); }
        }

        private static void ReadKeys(out float wheel, out bool down, out bool up, out bool enter)
        {
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current; var kb = Keyboard.current;
            wheel = m != null ? m.scroll.ReadValue().y : 0f;
            down = kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame);
            up = kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame);
            enter = kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
#else
            wheel = Input.mouseScrollDelta.y;
            down = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
            up = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
            enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
#endif
        }

        private void Step(int d)
        {
            int from = _target >= 0 ? _target : Current;
            _target = Mathf.Clamp(from + d, 0, pages.Length - 1);
        }

        /// <summary>Каждая панель — ровно в размер экрана (вьюпорта): при любом соотношении сторон и повороте.</summary>
        private void Layout(bool force)
        {
            if (scroll == null || scroll.viewport == null || scroll.content == null || pages == null) return;
            Vector2 s = scroll.viewport.rect.size;
            if (!force && (s - _size).sqrMagnitude < 0.25f) return;
            float keep = scroll.verticalNormalizedPosition;
            _size = s;
            scroll.content.sizeDelta = new Vector2(0f, s.y * pages.Length);
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i] == null) continue;
                pages[i].anchorMin = pages[i].anchorMax = new Vector2(0.5f, 1f);
                pages[i].pivot = new Vector2(0.5f, 1f);
                pages[i].sizeDelta = s;
                pages[i].anchoredPosition = new Vector2(0f, -s.y * i);
            }
            scroll.verticalNormalizedPosition = keep;
        }

        private float Norm(int i) => pages.Length <= 1 ? 1f : 1f - i / (float)(pages.Length - 1);

        private int Nearest()
        {
            if (scroll == null || pages.Length <= 1) return 0;
            return Mathf.Clamp(Mathf.RoundToInt((1f - scroll.verticalNormalizedPosition) * (pages.Length - 1)), 0, pages.Length - 1);
        }

        private void Dots()
        {
            if (dots == null) return;
            for (int i = 0; i < dots.Length; i++)
                if (dots[i] != null)
                {
                    dots[i].color = i == Current ? dotOn : dotOff;
                    dots[i].rectTransform.localScale = Vector3.one * (i == Current ? 1.35f : 1f);
                }
        }
    }
}
