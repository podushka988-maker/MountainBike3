using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>
    /// Итоги заезда (после падения): деревянная доска падает на экран, строки по очереди «досчитывают» очки за дистанцию
    /// и сальто, затем счётчик монет крутится со звоном (тон растёт), в конце — «дзынь» и баланс кошелька.
    /// Нажатие на экран — сразу показать итог. Всё на реальном времени (падение может идти в замедлении).
    /// Создаёт сборщик сцены (FreeRideSceneBuilder.BuildUI).
    /// </summary>
    public class RunResults : MonoBehaviour, IPointerClickHandler
    {
        [Serializable]
        public class Row
        {
            public RectTransform root;
            public Text label, value;
        }

        public struct Data
        {
            public string title;
            public bool newRecord;
            public float distance, best;
            public int distancePoints, backflips, frontflips, backflipPoints, frontflipPoints, points, coins, walletBefore, walletAfter;
        }

        [Header("Части панели")]
        public CanvasGroup group;
        public Image dim;
        public RectTransform board;
        public Text title, recordText;
        public Row distanceRow, backflipRow, frontflipRow, totalRow;
        public RectTransform divider;
        public RectTransform coinRow;
        public RectTransform coinIcon;
        public Text coinsText;
        public Text walletText;
        public CanvasGroup buttons;

        [Header("Звуки")]
        public AudioClip whoosh, pointTick, coinTick, coinTotal;
        [Range(0f, 1f)] public float volume = 0.9f;

        private AudioSource _sfx, _tick;
        private bool _skip, _done;
        private Data _d;
        private float _dimAlpha = 0.6f;

        private void Awake()
        {
            _sfx = gameObject.AddComponent<AudioSource>();
            _tick = gameObject.AddComponent<AudioSource>();
            foreach (AudioSource a in new[] { _sfx, _tick }) { a.playOnAwake = false; a.spatialBlend = 0f; a.ignoreListenerPause = true; }
            if (dim != null) _dimAlpha = dim.color.a;
        }

        public void OnPointerClick(PointerEventData e) => _skip = true;

        public void Show(Data d)
        {
            _d = d;
            _skip = false; _done = false;
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ анимация

        private IEnumerator Run()
        {
            Prepare();
            Play(_sfx, whoosh, 1f, 0.8f);
            // затемнение и доска: падает сверху и пружинит
            yield return Tween(0.5f, t =>
            {
                if (group != null) group.alpha = Mathf.Clamp01(t * 3f);
                if (board != null)
                {
                    float k = EaseOutBack(t);
                    board.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, k);
                    board.localRotation = Quaternion.Euler(0f, 0f, (1f - k) * 6f);
                }
            });
            yield return Punch(title != null ? title.rectTransform : null, 0.35f, 0.35f);
            if (_d.newRecord && recordText != null)
            {
                recordText.gameObject.SetActive(true);
                yield return Punch(recordText.rectTransform, 0.3f, 0.5f);
            }

            yield return CountRow(distanceRow, _d.distancePoints, 0.55f);
            yield return CountRow(backflipRow, _d.backflips * _d.backflipPoints, 0.4f);
            yield return CountRow(frontflipRow, _d.frontflips * _d.frontflipPoints, 0.4f);
            if (divider != null) { divider.gameObject.SetActive(true); yield return Tween(0.15f, t => divider.localScale = new Vector3(t, 1f, 1f)); }
            yield return CountRow(totalRow, _d.points, 0.45f, "");

            // монеты: счётчик со звоном, монета крутится
            if (coinRow != null)
            {
                coinRow.gameObject.SetActive(true);
                yield return Tween(0.2f, t => coinRow.localScale = Vector3.one * EaseOutBack(t));
            }
            float dur = Mathf.Clamp(0.7f + _d.coins * 0.02f, 0.7f, 2.4f);
            int shown = -1;
            float lastTick = -1f, spin = 0f;
            yield return Tween(dur, t =>
            {
                float e = 1f - Mathf.Pow(1f - t, 2.2f);
                int v = Mathf.RoundToInt(e * _d.coins);
                spin += Time.unscaledDeltaTime * Mathf.Lerp(900f, 200f, t);
                if (coinIcon != null) coinIcon.localScale = new Vector3(Mathf.Cos(spin * Mathf.Deg2Rad), 1f, 1f);
                if (v != shown)
                {
                    shown = v;
                    if (coinsText != null) coinsText.text = "+" + PlayerWallet.Format(v);
                    if (Time.unscaledTime - lastTick > 0.045f && v > 0)
                    {
                        lastTick = Time.unscaledTime;
                        Play(_tick, coinTick, Mathf.Lerp(0.95f, 1.6f, t), 0.55f);
                        if (coinsText != null) coinsText.rectTransform.localScale = Vector3.one * 1.12f;
                    }
                }
                if (coinsText != null)
                    coinsText.rectTransform.localScale = Vector3.Lerp(coinsText.rectTransform.localScale, Vector3.one, Time.unscaledDeltaTime * 14f);
            });
            Final();
            Play(_sfx, coinTotal, 1f, 1f);
            yield return Punch(coinsText != null ? coinsText.rectTransform : null, 0.45f, 0.45f);

            // баланс кошелька досчитывает
            if (walletText != null)
            {
                int w = -1;
                yield return Tween(0.6f, t =>
                {
                    int v = Mathf.RoundToInt(Mathf.Lerp(_d.walletBefore, _d.walletAfter, 1f - Mathf.Pow(1f - t, 3f)));
                    if (v != w) { w = v; walletText.text = "Wallet  " + PlayerWallet.Format(v); }
                });
            }
            yield return ShowButtons();
            _done = true;
        }

        /// <summary>Всё в начальное состояние: строки спрятаны, числа — нули.</summary>
        private void Prepare()
        {
            if (group != null) { group.alpha = 0f; group.blocksRaycasts = true; }
            if (title != null) title.text = _d.title;
            if (recordText != null) recordText.gameObject.SetActive(false);
            SetRow(distanceRow, $"DISTANCE  {_d.distance:0} m", false);
            SetRow(backflipRow, $"BACKFLIPS  ×{_d.backflips}", false);
            SetRow(frontflipRow, $"FRONTFLIPS  ×{_d.frontflips}", false);
            SetRow(totalRow, "TOTAL POINTS", false);
            if (divider != null) divider.gameObject.SetActive(false);
            if (coinRow != null) coinRow.gameObject.SetActive(false);
            if (coinsText != null) coinsText.text = "+0";
            if (coinIcon != null) coinIcon.localScale = Vector3.one;
            if (walletText != null) walletText.text = "Wallet  " + PlayerWallet.Format(_d.walletBefore);
            if (buttons != null) { buttons.alpha = 0f; buttons.interactable = false; buttons.blocksRaycasts = false; }
        }

        /// <summary>Итог сразу (нажали на экран или анимация закончилась).</summary>
        private void Final()
        {
            if (group != null) group.alpha = 1f;
            if (board != null) { board.localScale = Vector3.one; board.localRotation = Quaternion.identity; }
            if (recordText != null) recordText.gameObject.SetActive(_d.newRecord);
            FinishRow(distanceRow, _d.distancePoints, "+");
            FinishRow(backflipRow, _d.backflips * _d.backflipPoints, "+");
            FinishRow(frontflipRow, _d.frontflips * _d.frontflipPoints, "+");
            FinishRow(totalRow, _d.points, "");
            if (divider != null) { divider.gameObject.SetActive(true); divider.localScale = Vector3.one; }
            if (coinRow != null) { coinRow.gameObject.SetActive(true); coinRow.localScale = Vector3.one; }
            if (coinIcon != null) coinIcon.localScale = Vector3.one;
            if (coinsText != null) { coinsText.text = "+" + PlayerWallet.Format(_d.coins); coinsText.rectTransform.localScale = Vector3.one; }
        }

        private IEnumerator ShowButtons()
        {
            if (walletText != null) walletText.text = "Wallet  " + PlayerWallet.Format(_d.walletAfter);
            if (buttons == null) yield break;
            buttons.interactable = true; buttons.blocksRaycasts = true;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.25f;
                buttons.alpha = Mathf.Clamp01(t);
                buttons.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(Mathf.Clamp01(t)));
                yield return null;
            }
        }

        private void Update()
        {
            // пропуск: нажали — сразу итог (звон один раз)
            if (_skip && !_done)
            {
                _skip = false;
                _done = true;
                StopAllCoroutines();
                Final();
                Play(_sfx, coinTotal, 1f, 1f);
                StartCoroutine(ShowButtons());
            }
        }

        // ------------------------------------------------------------------ строки

        private static void SetRow(Row r, string label, bool visible)
        {
            if (r == null || r.root == null) return;
            if (r.label != null) r.label.text = label;
            if (r.value != null) r.value.text = "";
            r.root.gameObject.SetActive(visible);
        }

        private static void FinishRow(Row r, int value, string sign)
        {
            if (r == null || r.root == null) return;
            r.root.gameObject.SetActive(true);
            r.root.localScale = Vector3.one;
            SetAlpha(r, 1f);
            if (r.value != null) r.value.text = sign + PlayerWallet.Format(value);
        }

        private IEnumerator CountRow(Row r, int value, float dur, string sign = "+")
        {
            if (r == null || r.root == null) yield break;
            r.root.gameObject.SetActive(true);
            if (r.value != null) r.value.text = sign + "0";
            yield return Tween(0.18f, t => { SetAlpha(r, t); r.root.localScale = Vector3.one * Mathf.LerpUnclamped(0.8f, 1f, EaseOutBack(t)); });
            if (value <= 0) { yield return Wait(0.12f); yield break; }
            int shown = 0; float lastTick = -1f;
            yield return Tween(dur, t =>
            {
                int v = Mathf.RoundToInt((1f - Mathf.Pow(1f - t, 2f)) * value);
                if (v == shown) return;
                shown = v;
                if (r.value != null) r.value.text = sign + PlayerWallet.Format(v);
                if (Time.unscaledTime - lastTick > 0.05f) { lastTick = Time.unscaledTime; Play(_tick, pointTick, Mathf.Lerp(0.9f, 1.3f, t), 0.6f); }
            });
            yield return Wait(0.08f);
        }

        private static void SetAlpha(Row r, float a)
        {
            foreach (Text t in new[] { r.label, r.value })
                if (t != null) { Color c = t.color; c.a = a; t.color = c; }
        }

        // ------------------------------------------------------------------ помощники

        private void Play(AudioSource src, AudioClip clip, float pitch, float vol)
        {
            if (src == null || clip == null) return;
            src.pitch = pitch;
            src.PlayOneShot(clip, vol * volume);
        }

        private IEnumerator Tween(float dur, Action<float> f)
        {
            float t = 0f;
            f(0f);
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / Mathf.Max(0.01f, dur));
                f(t);
                yield return null;
            }
        }

        private static IEnumerator Wait(float s)
        {
            float end = Time.unscaledTime + s;
            while (Time.unscaledTime < end) yield return null;
        }

        private IEnumerator Punch(RectTransform rt, float dur, float amount)
        {
            if (rt == null) yield break;
            yield return Tween(dur, t => rt.localScale = Vector3.one * (1f + amount * Mathf.Sin(t * Mathf.PI) * (1f - t)));
            rt.localScale = Vector3.one;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }
}
