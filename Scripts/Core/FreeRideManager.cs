using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BikeGame
{
    /// <summary>
    /// Режим «Свободный заезд»: едем, пока не упадём. Считает дистанцию и скорость, показывает передачу,
    /// хранит рекорд, при падении показывает панель итогов и перезапускает заезд (кнопка, R или Пробел).
    /// Экономика: каждые 10 м — 1 очко, сальто назад — 10 очков, вперёд — 15; в конце заезда очки / 2 (вверх) — монеты в кошелёк.
    /// На телефоне: экран не гаснет во время игры, «Назад» дважды — выход из игры.
    /// Висит на объекте FreeRide.
    /// </summary>
    public class FreeRideManager : MonoBehaviour
    {
        private const string BestKey = "FreeRide_BestDistance";

        public BikeController2D bike;

        [Header("Интерфейс")]
        public Text distanceText;
        public Text speedText;
        public Text bestText;
        [Tooltip("Передача в шапке экрана: «Передача 5/11».")]
        public Text gearText;
        [Tooltip("Номер передачи между кнопками + и − (телефон).")]
        public Text gearTouchText;
        [Tooltip("Подсказка внизу экрана (например, «нажмите Назад ещё раз, чтобы выйти»).")]
        public Text hintText;
        public GameObject crashPanel;
        public Text crashText;
        public Button restartButton;
        [Tooltip("Кнопка «Меню» на экране падения и сцена главного меню.")]
        public Button menuButton;
        public string menuScene = "MainMenu";
        [Tooltip("Очки заезда (дистанция + трюки) — справа вверху.")]
        public Text scoreText;
        [Tooltip("Надпись трюка: «Backflip +10».")]
        public Text trickText;
        [Tooltip("Очков за каждое сальто назад и вперёд.")]
        public int backflipPoints = PlayerWallet.BackflipPoints, frontflipPoints = PlayerWallet.FrontflipPoints;
        [Tooltip("Панель итогов заезда (очки, монеты со счётчиком).")]
        public RunResults results;
        [Tooltip("Сальто засчитывается, если байк повернулся на 360° минус это, ° (приземление на склон).")]
        public float flipTolerance = 60f;
        [Tooltip("Панель с кнопками управления для телефона.")]
        public GameObject touchControls;
        [Tooltip("Показывать экранные кнопки и на компьютере (удобно для проверки).")]
        public bool showTouchOnDesktop = false;
        [Tooltip("Жмёте газ, а байк стоит (не заехал на огромный кикер, застрял в каньоне) — через столько секунд предложить начать заново.")]
        public float stuckTime = 3.5f;
        [Tooltip("Рама разломалась: через сколько секунд (реального времени) показать панель — чтобы досмотреть падение.")]
        public float brokenPanelDelay = 2f;

        public float Distance { get; private set; }
        /// <summary>Очки за трюки в этом заезде.</summary>
        public int Score { get; private set; }
        /// <summary>Сколько сальто назад / вперёд сделано за заезд.</summary>
        public int Backflips { get; private set; }
        public int Frontflips { get; private set; }
        /// <summary>Все очки заезда: за дистанцию (1 за 10 м) и за трюки.</summary>
        public int Points => PlayerWallet.DistancePoints(Distance) + Score;
        private bool _banked;       // монеты за заезд уже в кошельке
        public static float BestDistance
        {
            get => PlayerPrefs.GetFloat(BestKey, 0f);
            private set { PlayerPrefs.SetFloat(BestKey, value); PlayerPrefs.Save(); }
        }

        private float _startX;
        private float _gearPulse;   // короткая вспышка надписи при переключении
        private float _backTime = -10f, _hintTime;
        private float _airRot, _prevAngle, _groundTime, _trickTime;   // поворот рамы за полёт, °; сколько уже на земле
        private bool _inAir;
        private float _stuckTimer;
        private bool _stuck;   // застряли: показана панель «Застряли», байк не разбит

        private void Awake()
        {
            Application.targetFrameRate = 60;
            // физика 100 раз в секунду: тяжёлый гонщик на «мышцах» и подвеска считаются точнее и плавнее
            Time.fixedDeltaTime = 0.01f;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;   // телефон не гасит экран, пока едем
        }

        private void Start()
        {
            _startX = bike.frame.position.x;
            _prevAngle = bike.frame.rotation;
            if (trickText != null) trickText.text = "";
            bike.Crashed += OnCrashed;
            bike.GearChanged += OnGearChanged;
            if (crashPanel != null) crashPanel.SetActive(false);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (menuButton != null) menuButton.onClick.AddListener(ToMenu);
            if (touchControls != null) touchControls.SetActive(Application.isMobilePlatform || showTouchOnDesktop);
            if (results != null) results.gameObject.SetActive(false);
            SetupSingleSpeed();
            UpdateHud();
            // физика полёта как в жизни: первые заезды подсказываем, где задаётся вращение
            int runs = PlayerPrefs.GetInt("AirHintRuns", 0);
            if (bike.config != null && bike.config.airArcade && runs < 3)
            {
                PlayerPrefs.SetInt("AirHintRuns", runs + 1);
                ShowHint("In the air hold BACK or FORWARD to spin into a flip. Release — it keeps spinning, " +
                         "press the opposite button to stop", 7f);
            }
            else if (bike.config != null && bike.config.airRealistic && runs < 3)
            {
                PlayerPrefs.SetInt("AirHintRuns", runs + 1);
                ShowHint("Rotation is set at the lip of the jump: BACK — backflip, FORWARD — nose down. " +
                         "In the air the buttons only adjust the bike", 7f);
            }
        }

        private void OnDestroy()
        {
            if (bike != null) { bike.Crashed -= OnCrashed; bike.GearChanged -= OnGearChanged; }
        }

        private void Update()
        {
            if (!bike.IsCrashed)
            {
                Distance = Mathf.Max(Distance, bike.frame.position.x - _startX);
                TrackTricks();
                CheckStuck();
            }

            UpdateHud();

            if ((bike.IsCrashed || _stuck) && RestartPressed()) Restart();

            // «Назад» на Android (Escape на клавиатуре): первый раз — подсказка, второй за 2 секунды — в главное меню
            // (если меню нет в сборке — выход из игры)
            if (BackPressed())
            {
                bool hasMenu = Application.CanStreamedLevelBeLoaded(menuScene);
                if (Time.unscaledTime - _backTime < 2f) { if (hasMenu) ToMenu(); else Application.Quit(); }
                else { _backTime = Time.unscaledTime; ShowHint(hasMenu ? "Press Back again to return to the menu" : "Press Back again to exit", 2f); }
            }
            if (hintText != null && hintText.text.Length > 0 && Time.unscaledTime > _hintTime) hintText.text = "";
        }

        /// <summary>
        /// Сальто: считаем, на сколько повернулась рама за полёт. Приземлился и полсекунды не упал — засчитано
        /// (360° без Flip Tolerance — приземление на склон тоже считается). + — назад (нос вверх), − — вперёд.
        /// </summary>
        private void TrackTricks()
        {
            float angle = bike.frame.rotation;
            if (!bike.IsGrounded)
            {
                if (!_inAir) { _inAir = true; _airRot = 0f; }
                _airRot += Mathf.DeltaAngle(_prevAngle, angle);
                _groundTime = 0f;
            }
            else if (_inAir)
            {
                _airRot += Mathf.DeltaAngle(_prevAngle, angle);
                _groundTime += Time.deltaTime;
                if (_groundTime > 0.5f)
                {
                    _inAir = false;
                    int flips = Mathf.FloorToInt((Mathf.Abs(_airRot) + flipTolerance) / 360f);
                    if (flips > 0)
                    {
                        bool back = _airRot > 0f;
                        int pts = flips * (back ? backflipPoints : frontflipPoints);
                        Score += pts;
                        if (back) Backflips += flips; else Frontflips += flips;
                        string name = back ? "Backflip" : "Frontflip";
                        if (flips == 2) name = "Double " + name.ToLowerInvariant();
                        else if (flips > 2) name = $"{flips}× {name.ToLowerInvariant()}";
                        if (trickText != null) trickText.text = $"{name} +{pts}";
                        _trickTime = Time.unscaledTime + 2f;
                    }
                }
            }
            _prevAngle = angle;
            if (trickText != null && trickText.text.Length > 0 && Time.unscaledTime > _trickTime) trickText.text = "";
        }

        private void ShowHint(string text, float seconds)
        {
            if (hintText == null) return;
            hintText.text = text;
            _hintTime = Time.unscaledTime + seconds;
        }

        private int _hudDist = -1, _hudScore = -1, _hudSpeed = -1, _hudBest = -1, _hudGear = -1;

        private void UpdateHud()
        {
            // текст меняем, только когда число изменилось: новая строка каждый кадр — мусор для сборщика (рывки) и перестройка интерфейса
            int dist = Mathf.RoundToInt(Distance), spd = Mathf.RoundToInt(bike.Speed * 3.6f), best = Mathf.RoundToInt(Mathf.Max(BestDistance, Distance));
            if (distanceText != null && dist != _hudDist) { _hudDist = dist; distanceText.text = dist + " m"; }
            int points = Points;
            if (scoreText != null && points != _hudScore) { _hudScore = points; scoreText.text = points + " pts"; }
            if (speedText != null && spd != _hudSpeed) { _hudSpeed = spd; speedText.text = spd + " km/h"; }
            if (bestText != null && best != _hudBest) { _hudBest = best; bestText.text = "Record: " + best + " m"; }

            _gearPulse = Mathf.MoveTowards(_gearPulse, 0f, Time.unscaledDeltaTime / 0.25f);
            float scale = 1f + 0.3f * _gearPulse;
            bool gearChanged = bike.Gear != _hudGear;
            _hudGear = bike.Gear;
            if (gearText != null)
            {
                if (gearChanged) gearText.text = bike.GearCount <= 1 ? "Single speed" : $"Gear {bike.Gear}/{bike.GearCount}";
                gearText.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            if (gearTouchText != null)
            {
                if (gearChanged) gearTouchText.text = bike.Gear.ToString();
                gearTouchText.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void OnGearChanged(int gear) => _gearPulse = 1f;

        /// <summary>Байк с одной передачей: кнопки передач на экране не нужны.</summary>
        private void SetupSingleSpeed()
        {
            if (bike.GearCount > 1 || bike.input == null) return;
            if (bike.input.gearUpButton != null) bike.input.gearUpButton.gameObject.SetActive(false);
            if (bike.input.gearDownButton != null) bike.input.gearDownButton.gameObject.SetActive(false);
            if (gearTouchText != null) gearTouchText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Застряли: газ нажат, а скорость почти ноль несколько секунд (на земле) — например, не хватило разгона
        /// на огромный кикер или упали в каньон. Показываем панель «Застряли» с кнопкой «Заново».
        /// </summary>
        private void CheckStuck()
        {
            if (_stuck || bike.input == null) return;
            bool trying = bike.input.Pedal > 0f && bike.IsGrounded && bike.Speed < 0.3f;
            _stuckTimer = trying ? _stuckTimer + Time.deltaTime : 0f;
            if (_stuckTimer < stuckTime) return;
            _stuck = true;
            ShowEnd("Stuck!");
        }

        private void OnCrashed()
        {
            if (_stuck) return;
            if (bike.IsBroken) StartCoroutine(ShowEndLater("Frame snapped in half!", brokenPanelDelay));
            else ShowEnd("Crash!");
        }

        /// <summary>Рама разломалась: сначала даём досмотреть падение (замедленное), потом — панель с РЕСТАРТ и МЕНЮ.</summary>
        private System.Collections.IEnumerator ShowEndLater(string title, float delay)
        {
            if (touchControls != null) touchControls.SetActive(false);
            yield return new WaitForSecondsRealtime(delay);
            ShowEnd(title);
        }

        private void ShowEnd(string title)
        {
            bool newRecord = Distance > BestDistance;
            if (newRecord) BestDistance = Distance;
            int before = PlayerWallet.Coins;
            int coins = BankCoins();

            if (crashText != null)
                crashText.text = newRecord
                    ? $"New record!\n{Distance:0} m   {Points} pts   +{coins} coins"
                    : $"{title}\n{Distance:0} m   {Points} pts   +{coins} coins";
            if (crashPanel != null) crashPanel.SetActive(true);
            if (touchControls != null) touchControls.SetActive(false);
            if (results != null)
                results.Show(new RunResults.Data
                {
                    title = title, newRecord = newRecord, distance = Distance, best = BestDistance,
                    distancePoints = PlayerWallet.DistancePoints(Distance),
                    backflips = Backflips, frontflips = Frontflips, backflipPoints = backflipPoints, frontflipPoints = frontflipPoints,
                    points = Points, coins = coins, walletBefore = before, walletAfter = PlayerWallet.Coins,
                });
        }

        /// <summary>Монеты за заезд (очки / 2 с округлением вверх) — в кошелёк, один раз за заезд.</summary>
        private int BankCoins()
        {
            int coins = PlayerWallet.CoinsForPoints(Points);
            if (_banked) return coins;
            _banked = true;
            PlayerWallet.Add(coins);
            return coins;
        }

        public void ToMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(menuScene)) return;
            BankCoins();                 // вышли посреди заезда — заработанное не пропадает
            SceneManager.LoadScene(menuScene);
        }

        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static bool BackPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static bool RestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space);
#endif
        }
    }
}
