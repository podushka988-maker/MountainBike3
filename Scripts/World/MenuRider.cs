using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BikeGame
{
    /// <summary>
    /// Главное меню: камера медленно плывёт вдоль трассы, а время от времени по кадру проезжает гонщик —
    /// настоящий байк с физикой на автопилоте. Автопилот жмёт те же кнопки, что и игрок (W / S / A / D / прыжок):
    ///   • на ровном едет на заднем колесе: сбрасывает скорость до ~9 км/ч, ставит передачу полегче, крутя педали поднимает перед (A + W) до точки
    ///     баланса, дальше держит его чуть ниже баланса — вес назад, педали плавно и без остановок, при перевесе мягко
    ///     отпускает педали и подтормаживает (нажим педалей и тормоза дозируется 0…1, меняется плавно);
    ///     перед фигурой или через несколько секунд бросает педали и опускает перед;
    ///   • перед трамплином или дропом разгоняется; на огромных фигурах Rampage в 80% случаев берёт идеальную скорость
    ///     для приземления в склон, в 20% — перебирает со скоростью (перелетает); в полёте байк ставит под уклон
    ///     приземления авто-выравнивание (BikeController2D.AutoLevel — только у бота); на ровном иногда делает банни-хоп;
    ///   • рама может треснуть, как у игрока, — тогда через 1.5 с трасса начинается заново с новым случайным сидом.
    /// Камера всегда держит бота в кадре.
    /// Поведение проверено на зеркальной модели (Tools/physics): 4 трассы по 1,5 км — без падений, вилли по 3–6 с, педали крутятся ~75 % времени вилли (паузы — только при подъёме перед).
    /// Камера едет за ботом; трасса со случайным сидом, локации идут по порядку (TerrainGenerator.sequentialFeatures): волны, стол,
    /// дроп, горка-трамплин, 7 огромных трамплинов и 4 огромных дропа Rampage — и снова по кругу. Упал или застрял — трасса заново.
    /// </summary>
    public class MenuRider : MonoBehaviour
    {
        public BikeController2D bike;
        public Camera cam;
        public TerrainGenerator terrain;
        [Tooltip("Скорость, с которой камера плывёт вправо, если бота нет, м/с.")]
        public float cameraDrift = 0.6f;
        [Tooltip("Камера едет за ботом: насколько он левее центра кадра (доля половины ширины) — впереди видна трасса.")]
        [Range(0f, 0.8f)] public float followAhead = 0.3f;
        [Tooltip("Скорость гонщика, м/с.")]
        public float cruiseSpeed = 6.5f;
        [Tooltip("Скорость захода на трамплин или дроп, м/с.")]
        public float jumpSpeed = 8.6f;
        [Tooltip("(Не используется: скорость захода на огромные фигуры — идеальная для каждой, TerrainGenerator.BigFeatureAhead.)")]
        public float bigSpeed = 13f;
        [Tooltip("Доля заездов на огромные фигуры, где бот перебирает со скоростью (перелетает идеальное место приземления).")]
        [Range(0f, 1f)] public float overspeedChance = 0.2f;
        [Tooltip("Насколько перебирает со скоростью, м/с (от и до).")]
        public Vector2 overspeedBy = new Vector2(2.5f, 4f);
        [Tooltip("Камера всегда держит бота в кадре: не дальше этой доли половины ширины и высоты кадра от центра.")]
        [Range(0.3f, 0.95f)] public float keepInView = 0.8f;
        [Header("Камера: фигура целиком")]
        [Tooltip("Когда бот подъезжает к фигуре (ближе стольких метров), камера отдаляется так, чтобы были видны " +
                 "и начало трамплина или дропа, и его конец (приземление), и сам бот.")]
        public float frameLookAhead = 30f;
        [Tooltip("Запас по краям кадра вокруг фигуры, м.")]
        public float frameMargin = 4f;
        [Tooltip("Наибольшее отдаление камеры (половина высоты кадра, м).")]
        public float maxCameraSize = 20f;
        [Tooltip("Плавность движения и зума камеры, с.")]
        public float cameraSmooth = 0.7f;
        [Tooltip("Пауза перед следующим проездом, с (от и до).")]
        public Vector2 respawnDelay = new Vector2(2f, 6f);
        [Tooltip("Как часто гонщик делает трюк на ровном (вилли или банни-хоп), с (от и до).")]
        public Vector2 hopEvery = new Vector2(1.5f, 4f);
        [Tooltip("Вероятность, что трюк на ровном — вилли (иначе банни-хоп).")]
        [Range(0f, 1f)] public float wheelieChance = 0.65f;
        [Tooltip("Сколько ехать на заднем колесе, с (от и до).")]
        public Vector2 wheelieTime = new Vector2(6f, 12f);
        [Tooltip("Скорость, с которой гонщик поднимает перед, м/с (быстрее — тормозит перед трюком).")]
        public float wheelieSpeed = 2.5f;   // ~9 км/ч: сил ног хватает поднять и тяжёлое переднее колесо (18 кг)
        [Tooltip("На сколько градусов ниже точки баланса гонщик держит перед в вилли (там нужны педали — крутит их без остановок).")]
        public float holdBelow = 4f;
        [Tooltip("Насколько камера выше земли, м (земля — в нижней части кадра).")]
        public float groundOffset = 3.5f;

        private enum Mode { Cruise, WheeliePrep, WheelieUp, Wheelie, WheelieDown }

        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        private BikeAudio _audio;
        private readonly List<Vector2> _offsets = new List<Vector2>();
        private float _nextTrick, _crashTime = -1f;
        private bool _away;
        private float _speedFor = float.NaN, _speedPlus;
        private float _stuckTime;
        private Mode _mode;
        private float _modeTime, _wheelieEnd, _trim, _u;
        private bool _inAir;
        private float _baseSize = -1f, _sizeVel, _lastShift, _doneUntil = float.NegativeInfinity;
        private Vector3 _camVel;

        private void Start()
        {
            if (bike == null) return;
            bike.input.Autopilot = true;
            _audio = bike.GetComponent<BikeAudio>();
            Vector2 f = bike.frame.position;
            foreach (var rb in bike.GetComponentsInChildren<Rigidbody2D>(true)) { _bodies.Add(rb); _offsets.Add(rb.position - f); }
            bike.CanBreak = true;                                             // рама может треснуть, как у игрока
            bike.AutoLevel = true;                                            // в полёте — авто-выравнивание (только у бота)
            bike.Broken += () => StartCoroutine(RestartAfter(1.5f));          // треснула — через 1.5 с новая трасса (сид случайный)
            Respawn(3f);                                                       // старт трассы (ровный участок); трасса — со случайным сидом
            if (cam != null) cam.transform.position = new Vector3(bike.frame.position.x + HalfWidth() * followAhead, cam.transform.position.y, cam.transform.position.z);
        }

        /// <summary>
        /// Камера меню. Обычно плывёт вправо и держит землю внизу кадра. Когда бот подъезжает к фигуре, камера встаёт
        /// и отдаляется так, чтобы в кадре были начало трамплина (дропа) и его конец, вся высота рельефа и сам бот
        /// (в высоком полёте — отдаляется ещё). Бот проехал фигуру — камера плывёт дальше, быстрее, пока фигура не уйдёт за кадр.
        /// </summary>
        private void UpdateCamera(float dt)
        {
            if (cam == null) return;
            if (_baseSize < 0f) _baseSize = cam.orthographicSize;
            Vector3 p = cam.transform.position;
            float size = _baseSize;
            bool framed = false;
            if (bike != null && !_away && !bike.IsCrashed && terrain != null)
            {
                Vector2 bp = bike.frame.position;
                if (terrain.FeatureSpan(bp.x - 3f, bp.x + frameLookAhead, out float a, out float b) && a > _doneUntil)
                {
                    if (bp.x > b + 3f) _doneUntil = b + frameMargin;               // проехал — больше эту фигуру не показываем
                    else
                    {
                        float x0 = Mathf.Min(a - frameMargin, bp.x - 3f), x1 = Mathf.Max(b + frameMargin, bp.x + 3f);
                        float lo = float.MaxValue, hi = float.MinValue;
                        for (float sx = x0; sx <= x1; sx += 1f) { float h = terrain.HeightAt(sx); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
                        lo = Mathf.Min(lo, bp.y - 1f) - 0.8f;
                        hi = Mathf.Max(hi + 2.5f, bp.y + 2.5f);                    // над землёй — рост гонщика, в полёте — сам бот
                        size = Mathf.Clamp(Mathf.Max((hi - lo) * 0.5f, (x1 - x0) * 0.5f / Mathf.Max(0.5f, cam.aspect)) * 1.05f, _baseSize, maxCameraSize);
                        float cy = lo + size * 0.85f;                              // земля — в нижней части кадра
                        if (hi > cy + size * 0.92f) cy = hi - size * 0.92f;
                        Vector3 target = new Vector3((x0 + x1) * 0.5f, cy, p.z);
                        p = Vector3.SmoothDamp(p, target, ref _camVel, cameraSmooth);
                        framed = true;
                    }
                }
            }
            if (!framed)
            {
                // камера едет за ботом (он в левой части кадра, впереди видна трасса) и держит землю внизу кадра
                float halfW = cam.orthographicSize * cam.aspect;
                float tx = bike != null ? bike.frame.position.x + halfW * followAhead : p.x + cameraDrift * dt;
                p.x = Mathf.SmoothDamp(p.x, tx, ref _camVel.x, 0.5f);
                _camVel.y = 0f;
                if (terrain != null)
                {
                    float gy = terrain.HeightAt(p.x) + groundOffset * cam.orthographicSize / _baseSize;
                    if (bike != null) gy = Mathf.Max(gy, bike.frame.position.y + 1.5f - cam.orthographicSize * 0.3f);   // бот не уходит вниз за кадр
                    p.y = Mathf.Lerp(p.y, gy, dt * 1.5f);
                }
            }
            cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, size, ref _sizeVel, cameraSmooth);
            // бот всегда в кадре: если плавная камера отстала (прыжок, разгон, падение) — догоняем сразу
            if (bike != null)
            {
                Vector2 bp = bike.frame.position;
                float mx = cam.orthographicSize * cam.aspect * keepInView, my = cam.orthographicSize * keepInView;
                if (bp.x > p.x + mx) p.x = bp.x - mx; else if (bp.x < p.x - mx) p.x = bp.x + mx;
                if (bp.y + 1.2f > p.y + my) p.y = bp.y + 1.2f - my; else if (bp.y - 0.5f < p.y - my) p.y = bp.y - 0.5f + my;
            }
            cam.transform.position = p;
        }

        /// <summary>Перезапуск меню через delay секунд реального времени (замедление при поломке рамы не растягивает паузу).</summary>
        private System.Collections.IEnumerator RestartAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>Передачи — как у игрока: каденс выше 100 об/мин — тяжелее, ниже 60 — легче (в вилли не переключает).</summary>
        private void AutoShift()
        {
            if (_mode != Mode.Cruise || !bike.IsGrounded || Time.time - _lastShift < 0.4f) return;
            float cad = Mathf.Abs(bike.rearWheel.angularVelocity - bike.frame.angularVelocity) * Mathf.Deg2Rad
                        / Mathf.Max(0.05f, bike.GearRatio) * 60f / (2f * Mathf.PI);
            if (cad > 100f && bike.Gear < bike.GearCount) { bike.ShiftGear(1); _lastShift = Time.time; }
            else if (cad < 60f && bike.Gear > 1) { bike.ShiftGear(-1); _lastShift = Time.time; }
        }

        private float HalfWidth() => cam != null ? cam.orthographicSize * cam.aspect : 8f;

        private void SetMode(Mode m) { _mode = m; _modeTime = 0f; }

        private void Update()
        {
            float dt = Time.deltaTime;
            UpdateCamera(dt);
            if (bike == null) return;

            if (bike.IsCrashed)
            {
                if (_crashTime < 0f) _crashTime = Time.time;
                if (Time.time - _crashTime > 2.5f) SceneManager.LoadScene(SceneManager.GetActiveScene().name);   // кукла не собирается обратно
                return;
            }

            float camX = cam != null ? cam.transform.position.x : bike.frame.position.x;
            float x = bike.frame.position.x;
            // звук байка слышен, пока он в кадре или рядом (уехал — затихает)
            if (_audio != null) _audio.master = 0.6f * Mathf.Clamp01(1f - (Mathf.Abs(x - camX) - HalfWidth()) / 6f);
            // бот едет без остановок, локации идут по порядку; застрял (стоит 4 с) — начинаем заново с новой трассой
            _stuckTime = bike.Speed < 0.4f ? _stuckTime + dt : 0f;
            if (_stuckTime > 4f) { SceneManager.LoadScene(SceneManager.GetActiveScene().name); return; }
            AutoShift();

            float speed = bike.Speed;
            float pitch = Mathf.DeltaAngle(0f, bike.frame.rotation);      // + = перед вверх
            float rate = bike.frame.angularVelocity;                         // °/с
            float want = 0f, lean = 0f;                                        // want: +педали / −тормоз (0…1)
            bool hop = false;
            _modeTime += dt;

            if (!bike.IsGrounded)
            {
                // ---------- полёт: педали не крутит, выравнивает байк ----------
                _inAir = true;
                lean = 0f;                                                     // кнопки не жмёт: байк ставит под приземление авто-выравнивание
                if (_mode != Mode.Cruise) SetMode(Mode.Cruise);
            }
            else
            {
                if (_inAir) { _inAir = false; _nextTrick = Mathf.Max(_nextTrick, Time.time + 0.8f); }
                bool featureSoon = FeatureAhead(x, 2f, 12f);                 // фигура совсем рядом — никаких трюков
                bool onScreen = x > camX - HalfWidth() + 1f && x < camX + HalfWidth() * 0.35f;

                switch (_mode)
                {
                    case Mode.Cruise:
                    {
                        if (terrain != null && terrain.BigFeatureAhead(x - 1f, x + 40f, out float fStart, out float ideal))
                        {
                            // огромная фигура: в 80% — идеальная скорость для приземления в склон, в 20% — перебор (решает раз на фигуру)
                            if (!Mathf.Approximately(fStart, _speedFor))
                            {
                                _speedFor = fStart;
                                _speedPlus = !Mathf.Approximately(ideal, TerrainGenerator.SmallJumpSpeed) && Random.value < overspeedChance
                                    ? Random.Range(overspeedBy.x, overspeedBy.y) : 0f;   // небольшие трамплины — всегда на 30 км/ч
                            }
                            want = Mathf.Clamp(0.3f + 0.5f * (ideal + _speedPlus - speed), -1f, 1f);
                        }
                        else
                        {
                            float target = FeatureAhead(x, -1f, 25f) ? jumpSpeed : cruiseSpeed;
                            want = Mathf.Clamp(0.3f + 0.5f * (target - speed), -1f, 1f);   // плавно держит скорость
                            if (speed < target * 1.35f) want = Mathf.Max(want, 0f);          // тормозит только если сильно разогнался
                        }
                        if (Time.time > _nextTrick && onScreen && bike.RearGrounded && bike.FrontGrounded && FlatAhead(x, 22f))
                        {
                            if (Random.value < wheelieChance) SetMode(Mode.WheeliePrep);
                            else hop = true;
                            _nextTrick = Time.time + Random.Range(hopEvery.x, hopEvery.y);
                        }
                        break;
                    }
                    case Mode.WheeliePrep:
                    {
                        // вилли поднимают на небольшой скорости (~9 км/ч): быстрее — ногам не хватает каденса удержать перед
                        want = Mathf.Clamp(0.3f + 0.5f * (wheelieSpeed - speed), -1f, 1f);
                        // передача для подъёма переда: на скорости подъёма каденс 38–56 об/мин — ноги давят сильно,
                        // и запаса каденса хватает разогнаться в вилли до ~16 км/ч (проверено на модели для переднего колеса 0.9–18 кг)
                        float cw = wheelieSpeed / Mathf.Max(0.1f, bike.WheelRadius) / Mathf.Max(0.05f, bike.GearRatio) * 60f / (2f * Mathf.PI);
                        bool gearOk = true;
                        if (Time.time - _lastShift > 0.3f)
                        {
                            if (cw > 56f && bike.Gear < bike.GearCount) { bike.ShiftGear(1); _lastShift = Time.time; gearOk = false; }
                            else if (cw < 38f && bike.Gear > 1) { bike.ShiftGear(-1); _lastShift = Time.time; gearOk = false; }
                        }
                        else gearOk = false;
                        if (featureSoon || _modeTime > 4f) SetMode(Mode.Cruise);
                        else if (gearOk && speed < wheelieSpeed + 0.3f && bike.RearGrounded && bike.FrontGrounded)
                        {
                            SetMode(Mode.WheelieUp);
                            _wheelieEnd = Time.time + Random.Range(wheelieTime.x, wheelieTime.y);
                        }
                        break;
                    }
                    case Mode.WheelieUp:
                    {
                        // A + W: вес назад и педали в полную силу, пока перед (с упреждением по вращению) не подойдёт к точке баланса
                        want = 1f; lean = -1f;
                        if (!bike.FrontGrounded && pitch + 0.35f * rate > BalancePitch() - holdBelow - 2f) { SetMode(Mode.Wheelie); _trim = 0.2f; }
                        else if (_modeTime > 1.5f || featureSoon) SetMode(Mode.WheelieDown);   // не поднялось — бросает
                        break;
                    }
                    case Mode.Wheelie:
                    {
                        if (bike.FrontGrounded) { SetMode(Mode.Cruise); break; }
                        // баланс как у живого райдера: вес всё время назад, педали крутит плавно и без остановок. Держит перед
                        // чуть ниже точки баланса (там педали нужны, чтобы перед не опустился); быстрее ~16 км/ч — ближе к балансу,
                        // чтобы не разгоняться. Сколько давить — подбирает сам (медленная «память» _trim), плюс поправка по углу
                        // и вращению; перевалил за баланс — педали плавно отпускает и так же плавно подтормаживает
                        lean = -1f;
                        float below = Mathf.Clamp(holdBelow - 3f * (speed - 4.5f), 0f, holdBelow + 2f);
                        float e = pitch - (BalancePitch() - below);
                        _trim = Mathf.Clamp(_trim - 0.1f * e * dt, -0.3f, 0.8f);
                        want = Mathf.Clamp(_trim - 0.06f * e - 0.015f * rate, -1f, 1f);
                        if (Time.time > _wheelieEnd || featureSoon || speed > 11f || !FlatAhead(x, 8f)) SetMode(Mode.WheelieDown);
                        break;
                    }
                    case Mode.WheelieDown:
                    {
                        // бросает педали и мягко подтормаживает — перед опускается
                        want = pitch > 12f ? -0.4f : 0f;
                        if (bike.FrontGrounded || _modeTime > 2f)
                        {
                            SetMode(Mode.Cruise);
                            _nextTrick = Time.time + Random.Range(hopEvery.x, hopEvery.y);
                        }
                        break;
                    }
                }
            }
            // руки и ноги — плавно (не быстрее 8 в секунду от «нет» до «полностью»)
            _u = Mathf.MoveTowards(_u, want, 8f * dt);
            float pedal = Mathf.Max(0f, _u), brake = Mathf.Max(0f, -_u) * 0.5f;
            bike.input.SetAuto(pedal, brake, lean, hop);
        }

        private float BalancePitch() => bike.rider != null ? bike.rider.BalancePitch : 35f;

        /// <summary>
        /// В полёте: смотрит, куда байк остановится, если сейчас нажать обратную кнопку (ω²/(2·ускорение)),
        /// и подкручивает к уклону места приземления (чуть задним колесом вперёд).
        /// </summary>
        private float AirLean(float pitch, float rate)
        {
            float land = PredictLanding();
            BikeConfig c = bike.config;
            float accel = c != null && c.airArcade ? c.arcadeSpinAccel + c.arcadeStopAccel : 600f;
            float target = land + 4f;
            float pred = pitch + rate * Mathf.Abs(rate) / (2f * Mathf.Max(1f, accel));
            float e = pred - target;
            return e > 6f ? 1f : (e < -6f ? -1f : 0f);
        }

        /// <summary>Баллистика рамы до земли: уклон места приземления, °.</summary>
        private float PredictLanding()
        {
            if (terrain == null) return 0f;
            Vector2 p = bike.frame.position, v = bike.Velocity;
            float g = Physics2D.gravity.y * bike.frame.gravityScale;
            const float h = 0.03f;
            for (int i = 0; i < 120; i++)
            {
                p += v * h; v.y += g * h;
                if (p.y < terrain.HeightAt(p.x) + 0.45f) break;
            }
            return GroundSlope(p.x);
        }

        private bool FeatureAhead(float x, float from, float to) => terrain != null && terrain.FeatureBetween(x + from, x + to);

        /// <summary>Ровно ли впереди на distance метров: ни фигур, ни крутых склонов (там удобно ехать на заднем колесе).</summary>
        private bool FlatAhead(float x, float distance)
        {
            if (terrain == null) return true;
            if (terrain.FeatureBetween(x - 1f, x + distance)) return false;
            for (float d = 0f; d <= distance; d += 2f)
                if (Mathf.Abs(GroundSlope(x + d)) > 7f) return false;
            return true;
        }

        private float GroundSlope(float x)
        {
            if (terrain == null) return 0f;
            return Mathf.Atan2(terrain.HeightAt(x + 0.5f) - terrain.HeightAt(x - 0.5f), 1f) * Mathf.Rad2Deg;
        }

        /// <summary>Переставить весь байк с гонщиком на x (на землю), скорость — крейсерская.</summary>
        private void Respawn(float x)
        {
            // не посреди фигуры (в каньоне, на склоне кикера) — перед ней, с разгоном
            if (terrain != null && terrain.FeatureSpan(x - 2f, x + 2f, out float fa, out float fb) && x > fa - 12f) x = fa - 16f;
            float v0 = terrain != null && terrain.BigFeatureAhead(x, x + 45f, out _, out float iv) ? iv : cruiseSpeed;
            float ground = terrain != null ? terrain.HeightAt(x) : 0f;
            Vector2 framePos = new Vector2(x, ground + 0.4f);
            for (int i = 0; i < _bodies.Count; i++)
            {
                Rigidbody2D rb = _bodies[i];
                if (!rb.simulated) continue;
                rb.position = framePos + _offsets[i];
                rb.rotation = 0f;
                rb.SetVelocity(new Vector2(v0, 0f));
                rb.angularVelocity = 0f;
                rb.transform.SetPositionAndRotation(rb.position, Quaternion.identity);
            }
            float r = bike.WheelRadius;
            bike.rearWheel.angularVelocity = bike.frontWheel.angularVelocity = -v0 / Mathf.Max(0.1f, r) * Mathf.Rad2Deg;
            SetMode(Mode.Cruise);
            _inAir = false;
            _u = 0.5f;
            _nextTrick = Time.time + Random.Range(0.6f, 2f);
        }
    }
}
