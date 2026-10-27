using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Гонщик — отдельное физическое тело (Rigidbody2D, 72 кг, без вращения), которое держат на байке «мышцы» ног и рук.
    ///   • Мышцы = поддержка (m·(a_системы − g): тело движется вместе с байком) + мягкая пружина с демпфером к позе.
    ///     Сила ограничена (Leg Force) — на жёстких приземлениях ноги проседают.
    ///     Пружина посчитана как «мягкая связь» физического движка (Box2D soft constraint): устойчива при шаге 0.01 с,
    ///     тело не раскачивается («не колбасит») при любой жёсткости.
    ///   • Сила на тело и равная противоположная на раму приложены в одной точке → законы сохранения выполняются.
    ///   • Тело держится вертикально по гравитации (на подъёмах вперёд, на спусках назад), но лишь до разумного угла:
    ///     при сильном наклоне байка гонщик держится за руль и наклоняется вместе с ним, а не выгибается.
    ///     На крутом спуске и носом вниз в полёте гонщик сам уводит таз назад.
    ///     С весом назад руки прямые — гонщик уходит назад вместе с байком, поэтому у вилли есть точка баланса.
    ///   • Рефлексы на заднем колесе (как у настоящего гонщика): в вилли дозирует педали и задний тормоз,
    ///     в мэнуале — таз и мягкий задний тормоз.
    /// Висит на корневом объекте Bike. Тело гонщика создаёт сборщик сцены.
    /// Расчётная модель — Tools/physics/bike_sim.py (те же формулы).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class RiderBody : MonoBehaviour
    {
        public BikeController2D bike;
        public BikeInput input;
        [Tooltip("Физическое тело гонщика (без коллайдеров).")]
        public Rigidbody2D body;
        [HideInInspector, Tooltip("Устарело: сустав из старых сцен удаляется при запуске.")]
        public RelativeJoint2D joint;

        [Header("Позы таза (локально относительно рамы, м)")]
        [Tooltip("Нейтральная стойка: стоя на ровных педалях (с фото).")]
        public Vector2 hipNeutral = new Vector2(-0.33f, 0.745f);
        [Tooltip("Вес назад (мэнуал/вилли, по фото): таз низко за седлом, руки прямые, корпус почти вертикален на заднем колесе.")]
        public Vector2 hipBack = new Vector2(-0.50f, 0.55f);
        [Tooltip("Вес вперёд: тело над рулём.")]
        public Vector2 hipForward = new Vector2(-0.08f, 0.64f);

        [Tooltip("Вилли: гонщик сидит в седле (так вилли делают в жизни — сидя, на лёгкой передаче).")]
        public Vector2 hipSeat = new Vector2(-0.31f, 0.66f);
        public Vector2 comFromHipSeat = new Vector2(0.03f, 0.13f);
        [Tooltip("За сколько секунд гонщик садится в седло для вилли.")]
        public float seatTime = 0.25f;

        [Tooltip("Высокое вилли (по фото): сидит в седле, отклонился назад, руки согнуты.")]
        public Vector2 hipHigh = new Vector2(-0.27f, 0.63f);
        public Vector2 comFromHipHigh = new Vector2(0.202f, 0.049f);
        [Tooltip("С какого угла байка (°) гонщик переходит в позу высокого вилли и при каком переходит полностью.")]
        public float highFrom = 35f, highTo = 60f;
        [Tooltip("Длина рук в позе высокого вилли (доля прямых рук).")]
        public float highReach = 0.86f;

        [Header("Центр масс тела относительно таза (по сегментам тела)")]
        public Vector2 comFromHipNeutral = new Vector2(0.224f, 0.012f);
        public Vector2 comFromHipBack = new Vector2(0.280f, 0.049f);
        public Vector2 comFromHipForward = new Vector2(0.192f, 0.004f);

        [Header("Поведение")]
        [Tooltip("Каретка (опора ног), м — вокруг неё тело «остаётся вертикальным», когда байк наклоняется.")]
        public Vector2 bottomBracket = new Vector2(-0.141f, -0.039f);
        [Range(0f, 1f)]
        [Tooltip("Насколько гонщик держит тело вертикально по гравитации, а не по раме (0 — тело приклеено к раме).")]
        public float stayUpright = 0.6f;
        [Tooltip("Время, за которое гонщик переносит вес по команде, с.")]
        public float leanTime = 0.12f;
        [Tooltip("С поднятым передом гонщик выходит из позы «назад» медленнее, с (резкий бросок тела вперёд задрал бы перед ещё выше).")]
        public float leanRecoverTime = 0.5f;
        [Tooltip("Выход из позы «назад» на земле, с (резко встать из присеста — зад байка подпрыгнет).")]
        public float leanBackRecoverTime = 0.4f;
        [Tooltip("Наклон вперёд (D) из позы «назад» — гонщик нарочно бросает вес вперёд (так опускают перед из вилли), с.")]
        public float leanForwardTime = 0.2f;
        [Tooltip("В полёте гонщик переносит вес по кнопке почти мгновенно (сальто, поправки), с.")]
        public float airLeanTime = 0.06f;
        [Tooltip("Опускание переда: насколько таз уходит назад на скорость падения переда, м/(°/с).")]
        public float lowerHipRate = 0.003f;
        [Tooltip("Перед поднялся без веса назад: на сколько градусов гонщик успевает полностью отпустить педали.")]
        public float easeRange = 6f;
        [Range(0f, 1f)]
        [Tooltip("Крутя педали без наклона, гонщик подаётся вперёд — перед не задирается.")]
        public float pedalLean = 0.55f;
        [Tooltip("Дополнительный наклон вперёд на подъёмах: доля позы «вперёд» на каждые 20° уклона.")]
        public float climbLean = 0.5f;

        [Header("Сильный наклон байка")]
        [Tooltip("До какого угла байка, °, тело держится вертикально по гравитации: нос вверх / нос вниз. Дальше гонщик держится за руль и наклоняется вместе с байком (не выгибается к рулю).")]
        public float uprightMaxUp = 25f, uprightMaxDown = 40f;
        [Tooltip("Нос вниз (крутой спуск, полёт, стоппи): с какого угла, °, гонщик начинает уводить таз назад и при каком уводит полностью.")]
        public float descentFrom = 12f, descentTo = 40f;
        [Range(0f, 1f)]
        [Tooltip("Насколько гонщик уводит таз назад на крутом спуске (доля позы «вес назад»).")]
        public float descentBack = 0.8f;

        /// <summary>Перенос веса (сглаженное намерение): -1 назад … +1 вперёд.</summary>
        public float Lean { get; private set; }
        /// <summary>Перенос веса в позе тела: Lean плюс таз назад на крутом спуске носом вниз.</summary>
        public float PoseLean { get; private set; }
        /// <summary>Насколько гонщик сидит в седле (0 — стоит, 1 — сидит, вилли).</summary>
        public float Seat { get; private set; }
        /// <summary>Насколько гонщик в позе высокого вилли (0…1).</summary>
        public float High { get; private set; }
        /// <summary>Уклон земли под байком, ° (угол рамы, пока оба колеса на земле).</summary>
        public float SlopeAngle => _slope;
        /// <summary>Наклон тела относительно рамы, °.</summary>
        public float BodyTilt { get; private set; }
        /// <summary>Целевой центр масс гонщика в системе рамы.</summary>
        public Vector2 ComTarget { get; private set; }
        /// <summary>Угол рамы, при котором общий центр масс над задней осью (точка баланса вилли), °.</summary>
        public float BalancePitch { get; private set; }
        /// <summary>Гонщик сам управляет задним колесом (вилли/мэнуал или страховка от опрокидывания назад).</summary>
        public bool OnRearWheel { get; private set; }
        /// <summary>Момент на заднем колесе, который выбрал гонщик на заднем колесе, Н·м (+ педали, − тормоз).</summary>
        public float RearTorqueCommand { get; private set; }
        /// <summary>На заднем колесе S — только задний тормоз (дозированный), а не оба.</summary>
        public bool WheelieBrake { get; private set; }
        /// <summary>Передний тормоз, который даёт гонщик при S, Н·м: изо всех сил, но так, чтобы не перелететь через руль.</summary>
        public float FrontBrakeCommand { get; private set; }
        /// <summary>Стоппи: D + S — гонщик нарочно стоит на переднем колесе, зад в воздухе.</summary>
        public bool Stoppie { get; private set; }

        private float _leanVel, _shift, _slope, _reference, _descent, _ground = 1f, _rescueTime, _stoppieRescue;
        private float _squeeze;                 // нажим пальца на передний тормоз в стоппи, 0..1
        private bool _wasManual, _prevBrake, _manualLatch;
        private Vector2 _acc, _prevMomentum;
        private bool _hasPrev;
        private BikeConfig Cfg => bike != null ? bike.config : null;

        // ------------------------------------------------------------------ поза

        public Vector2 HipPose(float lean) =>
            lean < 0f ? Vector2.Lerp(hipNeutral, hipBack, -lean) : Vector2.Lerp(hipNeutral, hipForward, lean);

        public Vector2 ComFromHip(float lean) =>
            lean < 0f ? Vector2.Lerp(comFromHipNeutral, comFromHipBack, -lean) : Vector2.Lerp(comFromHipNeutral, comFromHipForward, lean);

        private Vector2 AroundBB(Vector2 p, float tiltDeg)
        {
            Vector2 rel = p - bottomBracket;
            return bottomBracket + (Vector2)(Quaternion.Euler(0f, 0f, tiltDeg) * rel);
        }

        /// <summary>Положение таза в системе рамы для отрисовки (поза + подстройка баланса + фактическое отставание тела).</summary>
        /// <summary>
        /// Где центр масс гонщика для отрисовки (в системе рамы): поза-цель мышц плюс отставание тела по инерции
        /// (в пределах Render Lag — присед до 30 см, вбок/вверх — чуть-чуть). compress — насколько присел (0…1).
        /// По этой точке BikeVisual строит скелет: нарисованное тело там, где его масса в физике.
        /// </summary>
        public Vector2 RenderCom(out float compress)
        {
            compress = 0f;
            if (body == null || bike == null || Cfg == null) return ComTarget;
            BikeConfig c = Cfg;
            Vector2 d = (Vector2)bike.frame.transform.InverseTransformPoint(body.transform.position) - ComTarget;
            compress = Mathf.Clamp01(-d.y / Mathf.Max(0.01f, c.renderLagDown));
            d.x = Mathf.Clamp(d.x, -c.renderLagSide, c.renderLagSide);
            d.y = Mathf.Clamp(d.y, -c.renderLagDown, c.renderLagUp);
            return ComTarget + d;
        }

        public Vector2 HipForRender()
        {
            Vector2 stand = Vector2.Lerp(AroundBB(HipPose(PoseLean), BodyTilt * (1f - Seat)), hipHigh, High);
            Vector2 hip = Vector2.Lerp(stand, hipSeat, Seat) + new Vector2(_shift, 0f);
            if (body == null || bike == null || Cfg == null) return hip;
            BikeConfig c = Cfg;
            Vector2 actualCom = bike.frame.transform.InverseTransformPoint(body.transform.position);
            Vector2 d = actualCom - ComTarget;
            // в пределах суставов: глубокий присед, но не растягивается вверх и вбок
            // на картинке отставание тела показываем сдержанно: присед — да, «растягивание» вбок — нет
            d.x = Mathf.Clamp(d.x, -c.renderLagSide, c.renderLagSide);
            d.y = Mathf.Clamp(d.y, -c.renderLagDown, c.renderLagUp);
            return hip + d;
        }

        // ------------------------------------------------------------------ физика

        private void Awake()
        {
            ComTarget = hipNeutral + comFromHipNeutral;
            if (joint == null) joint = GetComponentInChildren<RelativeJoint2D>(true);
            if (joint != null) { Destroy(joint); joint = null; }   // старые сцены: сустав заменён своими «мышцами»
            if (body != null) body.freezeRotation = true;
        }

        /// <summary>Угол рамы (°), при котором общий центр масс над задней осью, для позы гонщика comLocal.</summary>
        private float ComputeBalancePitch(Vector2 comLocal)
        {
            BikeConfig c = Cfg;
            Vector2 rearAxle = bike.frame.transform.InverseTransformPoint(bike.rearWheel.position);
            Vector2 frontAxle = bike.frame.transform.InverseTransformPoint(bike.frontWheel.position);
            float m = c.riderMass + c.frameMass + c.WheelsMass;
            Vector2 com = (c.riderMass * comLocal + c.frameMass * c.frameCenterOfMass + c.wheelMass * rearAxle + c.FrontWheelMass * frontAxle) / m;
            Vector2 d = com - rearAxle;
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        private void FixedUpdate()
        {
            BikeConfig c = Cfg;
            if (bike == null || body == null || c == null) return;
            if (!body.simulated) return;                                   // упал: тело стало куклой (RiderRagdoll)
            float dt = Time.fixedDeltaTime;
            Transform f = bike.frame.transform;
            float pitch = Mathf.DeltaAngle(0f, bike.frame.rotation);      // к горизонту, + = перед вверх
            float rate = bike.frame.angularVelocity;                         // °/с
            bool alive = input != null && !bike.IsCrashed;
            bool pedal = alive && input.Pedal > 0f;                          // газ держат и вместе с тормозом
            bool brakeKey = alive && input.Brake > 0f;
            bool rearOnly = bike.RearGrounded && !bike.FrontGrounded;

            // 1. Намерение гонщика: ввод + авто-наклон вперёд при педалировании и на подъёмах
            float lean = alive ? input.Lean : 0f;
            if (pedal && lean >= 0f)
                lean = Mathf.Max(lean, Mathf.Min(1f, pedalLean + Mathf.Max(0f, pitch / 20f) * climbLean));
            // край уступа: переднее колесо съехало, нос пошёл вниз — гонщик сам отклоняется назад
            if (alive && rearOnly && pitch - _slope < -2f && rate < -20f) lean = Mathf.Min(lean, -c.lipLean);
            if (c.airAutoLevel && alive && !bike.IsGrounded && input.Lean == 0f && rate < -30f && pitch < -5f) lean = Mathf.Min(lean, -c.lipLean * 0.6f);
            // спасение: перед ушёл далеко за точку баланса — гонщик решительно бросает педали, тормозит и подаётся вперёд
            if (alive && rearOnly && pitch > BalancePitch + c.rescueOver && !c.wheelieManual) _rescueTime = c.rescueTime;   // вилли игрока — без спасения
            _rescueTime = rearOnly ? Mathf.Max(0f, _rescueTime - dt) : 0f;
            bool rescue = _rescueTime > 0f;
            if (rescue) lean = Mathf.Max(lean, c.rescueLean);
            float smooth = leanTime;
            if (c.wheelieManual && rearOnly) smooth = c.wheelieManualLeanTime;      // на заднем колесе вес переносит игрок — быстро и одинаково
            else if (lean > Lean && Lean < 0f && !rescue)
            {
                smooth = rearOnly ? leanRecoverTime : leanBackRecoverTime;   // из позы «назад» — плавно
                if (alive && input.Lean > 0f) smooth = Mathf.Min(smooth, leanForwardTime);   // D — нарочно бросает вес вперёд
            }
            if (alive && input.Lean != 0f && !bike.IsGrounded) smooth = Mathf.Min(smooth, airLeanTime);   // в полёте вес переносит сразу
            Lean = Mathf.Clamp(Mathf.SmoothDamp(Lean, lean, ref _leanVel, smooth, Mathf.Infinity, dt), -1f, 1f);
            float back = Mathf.Max(0f, -Lean);

            // 2. Поза. Тело вертикально по гравитации; на заднем колесе — по уклону земли. С весом назад руки прямые.
            //    Вилли (вес назад + педали) — сидя в седле.
            if (bike.RearGrounded && bike.FrontGrounded)
                _slope += (pitch - _slope) * Mathf.Min(1f, dt / 0.15f);
            float reference = rearOnly && back > 0.3f ? _slope : pitch;
            // «вертикально по гравитации» — лишь до разумного угла: дальше руки на руле, тело наклоняется вместе с байком
            reference = Mathf.Clamp(reference, -uprightMaxDown, uprightMaxUp);
            _reference += (reference - _reference) * Mathf.Min(1f, dt / 0.15f);
            // круто носом вниз (спуск, полёт, стоппи) — гонщик сам уводит таз назад и вниз, спина ровная
            float descentT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(descentFrom, descentTo, -pitch)) * descentBack;
            if (Stoppie) descentT = 0f;                                       // стоппи: вес над рулём, таз назад не уводит
            _descent += (descentT - _descent) * Mathf.Min(1f, dt / 0.15f);
            PoseLean = Mathf.Lerp(Lean, -1f, _descent);
            float poseBack = Mathf.Max(0f, -PoseLean);
            bool sit = c.wheelieSeated && back > 0.3f && (pedal || (rearOnly && Seat > 0.5f));   // вилли сидя — по желанию
            Seat = Mathf.MoveTowards(Seat, sit ? 1f : 0f, dt / Mathf.Max(0.01f, seatTime));
            BodyTilt = -stayUpright * (1f - poseBack) * _reference;
            Vector2 standCom = HipPose(PoseLean) + ComFromHip(PoseLean);
            // высокое вилли: гонщик садится в седло и отклоняется назад (центр масс вперёд по раме — байку труднее перевернуться)
            float highT = rearOnly ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(highFrom, highTo, pitch)) * back : 0f;
            High += (highT - High) * Mathf.Min(1f, dt / 0.2f);
            standCom = Vector2.Lerp(standCom, hipHigh + comFromHipHigh, High);
            Vector2 seatCom = hipSeat + comFromHipSeat;
            Vector2 baseCom = AroundBB(Vector2.Lerp(standCom, seatCom, Seat), BodyTilt * (1f - Seat));
            BalancePitch = ComputeBalancePitch(baseCom);

            // 3. Рефлексы на заднем колесе (педали, задний тормоз, таз)
            // вилли игрока начинается, когда перед поднят с весом назад (A), и длится, пока перед в воздухе (дальше A / D — баланс).
            // Перед оторвался сам от сильных ног (без A) — гонщик ослабляет педали и опускает его: на старте байк не переворачивается
            _manualLatch = alive && rearOnly && pitch > 3f && c.wheelieManual && (_manualLatch || back > 0.3f);
            bool wheelie = alive && rearOnly && pitch > 3f && (back > 0.3f || _manualLatch);
            float wheelRel = Mathf.Abs(bike.rearWheel.angularVelocity - bike.frame.angularVelocity) * Mathf.Deg2Rad;
            float avail = pedal ? bike.PedalTorqueAt(wheelRel) : 0f;                 // сила ног на текущей передаче
            float overBalance = pitch - BalancePitch + c.predictTime * Mathf.Max(0f, rate);   // с упреждением
            bool save = alive && rearOnly && pitch > 3f && !wheelie && (overBalance > 0f || pedal);
            bool lowering = alive && rearOnly && pitch > 6f && !wheelie && rate < 0f;
            float shiftTarget = 0f;
            if (lowering) shiftTarget = Mathf.Clamp(lowerHipRate * rate, -c.hipBalanceRange, 0f) * c.balanceSkill;   // опускает перед мягко
            OnRearWheel = wheelie || save;
            if (save)
            {
                WheelieBrake = false;
                if (overBalance > 0f && bike.FrontAirTime < c.lipNoBrakeTime && bike.Speed > c.lipNoBrakeSpeed)
                    RearTorqueCommand = 0f;   // край трамплина на скорости: перед только что оторвался — бросает педали, но не тормозит
                else
                    RearTorqueCommand = overBalance > 0f
                        ? -Mathf.Clamp(c.wheeliePitchGain * overBalance + c.wheelieRateGain * Mathf.Max(0f, rate), 0f, c.brakeRear) * c.balanceSkill
                        : avail * Mathf.Clamp01(1f - (pitch - 3f) / easeRange);
            }
            else if (wheelie && c.wheelieManual)
            {
                // ВИЛЛИ ДЕРЖИТ ИГРОК: никаких рефлексов гонщика. W — педали в полную силу (перед вверх), S — задний тормоз блокирует
                // колесо (перед резко на землю), A / D — вес назад / вперёд (поза, точка баланса сдвигается вместе с тазом)
                float wAvail = bike.PedalTorqueAt(wheelRel);
                WheelieBrake = brakeKey;
                // нажим дозируется 0…1: клавиатура и кнопки — всегда 1, автопилот в меню — плавно
                float torque = brakeKey ? (c.wheelieLockTorque > 0f ? -c.wheelieLockTorque : -c.brakeRear * c.wheelieBrake) * Mathf.Clamp01(input.Brake)
                                        : (pedal ? wAvail * Mathf.Clamp01(input.Pedal) : 0f);
                RearTorqueCommand = Mathf.Clamp(torque, -Mathf.Max(c.brakeRear, c.wheelieLockTorque), pedal ? wAvail : 0f);
            }
            else if (wheelie)
            {
                // ВИЛЛИ — баланс держит игрок: W крутит педали (перед вверх), S — задний тормоз (перед вниз).
                float over = pitch - BalancePitch;                                // >0 — за точкой баланса
                // сила ног через текущую передачу: на лёгкой передаче разгон в вилли кончается быстро — переключайся вверх
                float wAvail = bike.PedalTorqueAt(wheelRel);
                float torque = 0f, hi = 0f;
                WheelieBrake = false;
                if (brakeKey && !pedal) { torque = c.wheelieLockTorque > 0f ? -c.wheelieLockTorque : -c.brakeRear * c.wheelieBrake; WheelieBrake = true; }   // колесо блокируется — перед резко на землю
                else if (pedal)
                {
                    if (c.wheelieDynamic)
                    {
                        // разгон в вилли: сила инерции m·a тянет нос вверх, её уравновешивает вес впереди оси, поэтому при разгоне
                        // точка равновесия ниже баланса на atan(a/g). Гонщик держит перед там, где ему хватает Wheelie Use силы ног,
                        // и давит ровно столько, сколько нужно для равновесия (+ поправка по углу). Кончается передача — сила ног
                        // падает, и перед опускается: переключайся вверх.
                        float gAcc = -Physics2D.gravity.y * c.gravityScale;
                        float mSys = c.riderMass + c.frameMass + c.WheelsMass, rWheel = bike.WheelRadius, vel = bike.Speed;
                        float res = c.rollingResistance * gAcc + 0.5f * 1.2f * c.airDragArea * vel * vel / mSys;   // сопротивление, м/с²
                        // сила ног наперёд: какой она будет на Wheelie Cadence Ahead об/мин выше (на высоком каденсе ноги слабеют)
                        float cad = wheelRel / Mathf.Max(0.1f, bike.GearRatio) * 60f / (2f * Mathf.PI);
                        float ahead = c.wheelieCadenceAhead > 0f ? bike.PedalTorqueAt(wheelRel * (1f + c.wheelieCadenceAhead / Mathf.Max(cad, 10f))) : wAvail;
                        float phi = Mathf.Min(c.wheelieDynamicMax,
                                              Mathf.Atan(Mathf.Max(0f, c.wheelieUse * ahead / (mSys * rWheel) - res) / gAcc) * Mathf.Rad2Deg);
                        // запас под балансом — только когда есть чем разгоняться; иначе — у самого баланса
                        float margin = c.wheelieMarginPhi > 0f ? c.wheelieDynamicEnd * Mathf.Clamp01(phi / c.wheelieMarginPhi) : c.wheelieDynamicEnd;
                        float holdAngle = BalancePitch - phi + Mathf.Min(-c.balanceBelow, margin);   // к какому углу гонщик держит перед
                        float holdTorque = mSys * rWheel * (gAcc * Mathf.Tan(Mathf.Max(0f, BalancePitch - holdAngle) * Mathf.Deg2Rad) + res);
                        torque = Mathf.Clamp(holdTorque + c.wheelieHoldGain * (holdAngle - pitch) - c.wheelieHoldRate * rate, 0f, wAvail);
                    }
                    else
                    {
                        // далеко за точкой баланса гонщик инстинктивно ослабляет нажим
                        torque = wAvail * c.wheeliePedal * Mathf.Clamp01((c.featherEnd - over - c.featherPredict * rate) / Mathf.Max(0.1f, c.featherRange));
                    }
                    hi = wAvail;
                    // W + S: ноги крутят дальше, задний тормоз сильнее — перед опускается (так вилли и держат в жизни)
                    if (brakeKey) { torque = c.wheelieLockTorque > 0f ? -c.wheelieLockTorque : torque * c.gasBrakeShare - c.brakeRear * c.wheelieBrake; WheelieBrake = true; }
                }
                // гонщик неидеален: нажим на педали и тело «гуляют» — без поправок игрока вилли срывается
                float tt = Time.time;
                torque += c.wobble * (0.6f * Mathf.Sin(tt * 2.3f + 1.1f) + 0.4f * Mathf.Sin(tt * 3.7f + 0.3f) + 0.3f * Mathf.Sin(tt * 0.9f));
                torque -= c.wheelieAssist * over;                                // слабое удержание (0 — всё делает игрок)
                // палец на заднем тормозе: перед ушёл за баланс — гонщик слегка притормаживает (до баланса не тормозит)
                torque -= Mathf.Clamp((over - c.coverStart + c.coverPredict * Mathf.Max(0f, rate)) * c.coverGain, 0f, c.coverMax * c.brakeRear);
                // без педалей (мэнуал) таз немного ловит баланс
                shiftTarget = pedal ? 0f : Mathf.Clamp(c.hipBalanceGain * over + c.hipBalanceRate * rate, -c.hipBalanceRange, c.hipBalanceRange) * back * c.manualHipAssist;
                if (rescue) { torque = bike.Speed > 1f ? -c.brakeRear : 0f; hi = 0f; WheelieBrake = true; }
                RearTorqueCommand = Mathf.Clamp(torque, -Mathf.Max(c.brakeRear, c.wheelieLockTorque), hi);
            }
            else { RearTorqueCommand = 0f; WheelieBrake = false; }
            // 3б. Передний тормоз дозирует гонщик, как в жизни: тормозит изо всех сил, пока зад не начинает отрываться.
            //     С наклоном вперёд (D + S) нарочно поднимает зад (стоппи) и держит его у нужного угла; перебор — отпускает.
            float lift = bike.RearGrounded ? 0f : _slope - pitch;            // насколько зад поднялся над землёй, °
            float rise = Mathf.Max(0f, -rate);                                // зад поднимается, °/с
            bool wantStoppie = c.stoppieWithForward && brakeKey && input.Lean > 0f && bike.Speed > c.stoppieMinSpeed;
            // СТОППИ, которое дозирует игрок: D + S — без подстраховки. Нажим растёт, пока держишь S (с того нажима, которым уже тормозил).
            bool manual = c.stoppieManual && brakeKey && alive && input.Lean > 0f && bike.FrontGrounded;
            if (manual && !_wasManual) _squeeze = _prevBrake ? FrontBrakeCommand / Mathf.Max(1f, c.brakeFront) : 0f;
            _squeeze = manual ? Mathf.Min(1f, _squeeze + dt / Mathf.Max(0.01f, c.frontSqueezeTime))
                              : Mathf.Max(0f, _squeeze - dt / Mathf.Max(0.01f, c.frontReleaseTime));
            _wasManual = manual;
            _prevBrake = brakeKey;
            float frontShare;
            if (manual)
            {
                _stoppieRescue = 0f;
                frontShare = _squeeze;                                        // зажал надолго — перелёт через руль
                Stoppie = !bike.RearGrounded;
            }
            else if (wantStoppie && bike.FrontGrounded && !c.stoppieManual)
            {
                if (lift > c.stoppieLift + c.stoppieRescue) _stoppieRescue = 0.4f;
                _stoppieRescue = Mathf.Max(0f, _stoppieRescue - dt);
                frontShare = _stoppieRescue > 0f ? 0f
                    : Mathf.Clamp01((c.stoppieLift - lift - c.stoppiePredict * rise) / Mathf.Max(0.1f, c.stoppieRange));
                Stoppie = !bike.RearGrounded;
            }
            else if (brakeKey && alive && bike.FrontGrounded && !bike.RearGrounded && lift > c.frontWheelBrakeLift)
            {
                // едешь на переднем колесе (зад высоко) и жмёшь тормоз — передний блокируется, через руль, как в жизни
                _stoppieRescue = 0f;
                frontShare = 1f;
                Stoppie = true;
            }
            else
            {
                _stoppieRescue = 0f;
                Stoppie = false;
                // насколько гонщик даёт заду оторваться: вес назад (A) — держит зад на земле, вперёд (D) — зад поднимается
                // (тормоз + вес вперёд = зад в воздухе, как в жизни); дальше — отпускает передний, чтобы не перелететь через руль
                float liftAllowed = c.endoLift + (c.endoLiftForward - c.endoLift) * Mathf.Max(0f, Lean)
                                  + (c.endoLiftBack - c.endoLift) * Mathf.Max(0f, -Lean);
                frontShare = Mathf.Clamp01((liftAllowed - lift - c.endoPredict * rise) / Mathf.Max(0.1f, c.endoRange));
            }
            FrontBrakeCommand = c.brakeFront * frontShare;

            _shift += (shiftTarget - _shift) * Mathf.Min(1f, dt / 0.06f);       // таз двигается плавно
            ComTarget = baseCom + new Vector2(_shift, 0f) - new Vector2(0f, c.hopCrouch * bike.HopCrouch);   // присед перед прыжком

            // 4. Ускорение байка по горизонтали (разгон/торможение) — ноги и руки двигают тело вместе с байком
            Vector2 p = Momentum(out float totalMass);
            if (_hasPrev)
            {
                Vector2 a = (p - _prevMomentum) / (totalMass * dt);
                _acc += (a - _acc) * (dt / (c.accelFilter + dt));
            }
            _prevMomentum = p; _hasPrev = true;
            _ground = Mathf.MoveTowards(_ground, bike.IsOnGround ? 1f : 0f, dt / 0.05f);   // шина в 3 см от земли — ноги ещё держат вес

            // 5. Мышцы: мягкая связь тела с позой (Box2D soft constraint) + поддержка веса
            Rigidbody2D fr = bike.frame;
            Vector2 target = f.TransformPoint(ComTarget);
            Vector2 C = body.position - target;
            Vector2 Cdot = body.GetVelocity() - fr.GetPointVelocity(target);
            Vector2 r = body.position - fr.worldCenterOfMass;
            float im = 1f / body.mass + 1f / fr.mass, iI = 1f / fr.inertia;
            float k11 = im + r.y * r.y * iI, k12 = -r.x * r.y * iI, k22 = im + r.x * r.x * iI;
            float det = k11 * k22 - k12 * k12;
            float w = 2f * Mathf.PI * c.legFrequency;
            float a1 = 2f * c.legDamping + dt * w, a2 = dt * w * a1, a3 = 1f / (1f + a2);
            float bias = w / a1, massScale = a2 * a3;
            // ноги гасят удар, а выпрямляются медленно — не «пружинят» и не подбрасывают байк
            // ограничиваем только выпрямление вверх после приседа; в остальные стороны тело идёт вместе с байком
            Vector2 bcl = f.InverseTransformDirection(bias * C);
            if (bcl.y < -c.legReturnSpeed) bcl.y = -c.legReturnSpeed;
            Vector2 bc = f.TransformDirection(bcl);
            Vector2 b = Cdot + bc;
            Vector2 J = new Vector2(-massScale * (k22 * b.x - k12 * b.y) / det, -massScale * (-k12 * b.x + k11 * b.y) / det);
            // поддержка: вес тела, пока колёса на земле (в воздухе тело и байк падают вместе), плюс разгон/торможение
            Vector2 g = Physics2D.gravity * c.gravityScale;
            float ax = Mathf.Clamp(_acc.x, -c.accelLimit, c.accelLimit);
            J += body.mass * new Vector2(ax, -g.y) * dt * _ground;
            Vector2 F = J / dt;
            float err = C.magnitude;
            float boost = 1f + c.legForceBoost * Mathf.Pow(Mathf.Clamp01((err - c.legSoftStart) / Mathf.Max(0.01f, c.bodyLimitDown - c.legSoftStart)), 2f);
            F = Vector2.ClampMagnitude(F, c.legForce * boost * c.massScale);

            // кости: тело не уходит дальше, чем позволяют руки и ноги (в системе рамы)
            Vector2 cl = f.InverseTransformDirection(C), cdl = f.InverseTransformDirection(Cdot);
            F += Limit(cl.x, cdl.x, -c.bodyLimitBack, c.bodyLimitForward, f.right, k11, k12, k22, dt, c);
            F += Limit(cl.y, cdl.y, -c.bodyLimitDown, c.bodyLimitUp, f.up, k11, k12, k22, dt, c);

            body.AddForce(F);
            fr.AddForceAtPosition(-F, body.position);               // равная и противоположная — на раму
        }

        /// <summary>Упор сустава: мягкая связь (как в Box2D) — не перескакивает, поэтому тело не дребезжит у предела.</summary>
        private static Vector2 Limit(float pos, float vel, float lo, float hi, Vector2 axis, float k11, float k12, float k22, float dt, BikeConfig c)
        {
            float over = pos > hi ? pos - hi : (pos < lo ? pos - lo : 0f);
            if (over == 0f) return Vector2.zero;
            float sgn = over > 0f ? 1f : -1f;
            float kn = axis.x * (k11 * axis.x + k12 * axis.y) + axis.y * (k12 * axis.x + k22 * axis.y);
            float w = 2f * Mathf.PI * c.bodyLimitFrequency;
            float a1 = 2f + dt * w, a2 = dt * w * a1, a3 = 1f / (1f + a2);
            float vn = vel * sgn;                                               // >0 — уходит дальше за предел
            float jn = -(a2 * a3) * (vn + Mathf.Min(c.bodyLimitPush, w / a1 * Mathf.Abs(over))) / kn;   // выталкивает мягко — без отскока
            jn = Mathf.Min(0f, jn) * sgn;                                       // только возвращает, не тянет
            return axis * (jn / dt);
        }

        private Vector2 Momentum(out float mass)
        {
            Rigidbody2D fr = bike.frame, rw = bike.rearWheel, fw = bike.frontWheel;
            mass = fr.mass + rw.mass + fw.mass + body.mass;
            return fr.mass * fr.GetVelocity() + rw.mass * rw.GetVelocity() + fw.mass * fw.GetVelocity() + body.mass * body.GetVelocity();
        }
    }
}
