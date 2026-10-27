using UnityEngine;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>
    /// Карточка байка в магазине: картинка, цена на верхней полупрозрачной панели (монета + число, у бесплатного — FREE)
    /// и доска внизу: КУПИТЬ (байк не куплен) / ВЫБРАТЬ / ВЫБРАНО. Создаёт сборщик меню.
    /// </summary>
    public class BikeCard : MonoBehaviour
    {
        public int index;
        public string id;
        public int price;
        [Tooltip("Доска карточки: обычная / выбранная (золотая рамка).")]
        public Image card;
        [Tooltip("Кнопка: КУПИТЬ / ВЫБРАТЬ / ВЫБРАНО.")]
        public Image button;
        [Tooltip("Цена на верхней панели карточки.")]
        public Text priceText;
        [Tooltip("Монета рядом с ценой (у бесплатного байка скрыта).")]
        public Image priceCoin;
        [HideInInspector] public MainMenu menu;

        public bool Owned => price <= 0 || PlayerWallet.Owns(id);

        private float _shake, _pop;
        private Vector3 _baseScale = Vector3.one;
        private Vector2 _basePos;
        private Color _priceColor = Color.white;

        private void Awake()
        {
            Button b = button != null ? button.GetComponent<Button>() : null;
            if (b != null) b.onClick.AddListener(() => { if (menu != null) menu.CardButton(this); });
            Button cb = card != null ? card.GetComponent<Button>() : null;
            if (cb != null) cb.onClick.AddListener(() => { if (menu != null && Owned) menu.PickBike(id); });
            _baseScale = transform.localScale;
            _basePos = ((RectTransform)transform).anchoredPosition;
            if (priceText != null) _priceColor = priceText.color;
            LayoutPrice();
        }

        /// <summary>Цена: «FREE» по центру или монета + число (вместе по центру панели).</summary>
        public void LayoutPrice()
        {
            if (priceText == null) return;
            bool free = price <= 0;
            priceText.text = free ? "FREE" : PlayerWallet.Format(price);
            if (priceCoin != null) priceCoin.gameObject.SetActive(!free);
            float coin = free || priceCoin == null ? 0f : priceCoin.rectTransform.sizeDelta.x;
            float gap = coin > 0f ? 10f : 0f;
            float w = priceText.preferredWidth;
            float total = coin + gap + w;
            priceText.rectTransform.anchoredPosition = new Vector2(-total / 2f + coin + gap + w / 2f, priceText.rectTransform.anchoredPosition.y);
            if (priceCoin != null) priceCoin.rectTransform.anchoredPosition = new Vector2(-total / 2f + coin / 2f, priceCoin.rectTransform.anchoredPosition.y);
        }

        public void Refresh(bool selected, MainMenu m)
        {
            if (card != null && m.cardSprite != null) card.sprite = selected ? m.cardSelectedSprite : m.cardSprite;
            if (button == null) return;
            Sprite s = selected ? m.selectedSprite : Owned ? m.selectSprite : (m.buySprite != null ? m.buySprite : m.selectSprite);
            if (s != null) button.sprite = s;
        }

        /// <summary>Не хватает монет: карточка трясётся, цена вспыхивает красным.</summary>
        public void Denied() => _shake = 1f;

        /// <summary>Куплено: карточка «подпрыгивает».</summary>
        public void Purchased() => _pop = 1f;

        private void Update()
        {
            if (_shake <= 0f && _pop <= 0f) return;
            float dt = Time.unscaledDeltaTime;
            var rt = (RectTransform)transform;
            if (_shake > 0f)
            {
                _shake = Mathf.Max(0f, _shake - dt / 0.45f);
                rt.anchoredPosition = _basePos + new Vector2(Mathf.Sin(Time.unscaledTime * 60f) * 14f * _shake, 0f);
                if (priceText != null) priceText.color = Color.Lerp(_priceColor, new Color(1f, 0.3f, 0.25f), Mathf.Clamp01(_shake * 1.6f));
                if (_shake <= 0f) { rt.anchoredPosition = _basePos; if (priceText != null) priceText.color = _priceColor; }
            }
            if (_pop > 0f)
            {
                _pop = Mathf.Max(0f, _pop - dt / 0.5f);
                float k = Mathf.Sin(_pop * Mathf.PI) * 0.08f;
                transform.localScale = _baseScale * (1f + k);
                if (_pop <= 0f) transform.localScale = _baseScale;
            }
        }
    }
}
