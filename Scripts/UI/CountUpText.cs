using UnityEngine;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>Число на экране, которое плавно «досчитывает» до нового значения (баланс монет). Время — реальное.</summary>
    public class CountUpText : MonoBehaviour
    {
        public Text text;
        public string prefix = "", suffix = "";
        [Tooltip("Лёгкий «пульс» надписи при изменении числа.")]
        public float punch = 0.18f;

        private float _from, _to, _t = 1f, _dur = 0.6f;
        private int _shown = int.MinValue;
        private float _pulse;
        private Vector3 _scale = Vector3.one;

        public int Target => Mathf.RoundToInt(_to);

        private void Awake()
        {
            if (text == null) text = GetComponent<Text>();
            _scale = transform.localScale;
        }

        public void SetInstant(int value)
        {
            _from = _to = value; _t = 1f;
            Show(value);
        }

        public void AnimateTo(int value, float duration = 0.6f)
        {
            _from = Current(); _to = value; _t = 0f; _dur = Mathf.Max(0.01f, duration);
        }

        private float Current() => Mathf.Lerp(_from, _to, 1f - Mathf.Pow(1f - Mathf.Clamp01(_t), 3f));

        private void Update()
        {
            if (_t < 1f)
            {
                _t += Time.unscaledDeltaTime / _dur;
                int v = Mathf.RoundToInt(Current());
                if (v != _shown) { Show(v); _pulse = 1f; }
            }
            _pulse = Mathf.MoveTowards(_pulse, 0f, Time.unscaledDeltaTime * 6f);
            transform.localScale = _scale * (1f + punch * _pulse);
        }

        private void Show(int v)
        {
            _shown = v;
            if (text != null) text.text = prefix + PlayerWallet.Format(v) + suffix;
        }
    }
}
