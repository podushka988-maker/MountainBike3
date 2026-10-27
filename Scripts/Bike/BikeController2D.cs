using System;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Физика велосипеда с реальными массами и силами:
    ///   • рама + два колеса на WheelJoint2D (подвеска), гонщик — отдельное тело на «мышцах» (RiderBody);
    ///   • педали: момент на шатунах ограничен силой ног на малом каденсе и мощностью на большом,
    ///     два толчка за оборот шатунов; реакция педалей приподнимает перед (с весом назад — вилли);
    ///   • 11 передач (1 — самая лёгкая, 11 — самая тяжёлая): момент на колесе = момент на шатунах / передаточное число,
    ///     на высоком каденсе ноги не успевают — на каждой передаче своя предельная скорость;
    ///   • тормоза отдельно на переднее и заднее колесо;
    ///   • сопротивление качению и воздуха (байк не разгоняется бесконечно и катится по инерции правдоподобно);
    ///   • рывок руками: резкий перенос веса назад тянет руль — так выдёргивают перед в мэнуал;
    ///   • никаких искусственных моментов на земле: перед поднимается только от веса, рывка и педалей.
    /// Все числа — в реальных единицах в BikeConfig; внутри умножаются на massScale (законы движения не меняются).
    /// Висит на корневом объекте Bike.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class BikeController2D : MonoBehaviour
    {
        [Header("Настройки")]
        public BikeConfig config;
        public BikeInput input;
        [Tooltip("Тело гонщика (масса, мышцы, перенос веса).")]
        public RiderBody rider;

        [Header("Части байка")]
        public Rigidbody2D frame;
        public Rigidbody2D rearWheel;
        public Rigidbody2D frontWheel;
        [Tooltip("WheelJoint2D на раме, связанный с задним колесом")]
        public WheelJoint2D rearJoint;
        [Tooltip("WheelJoint2D на раме, связанный с передним колесом")]
        public WheelJoint2D frontJoint;

        [Header("Земля")]
        [Tooltip("Слои, которые считаются землёй (для проверки 'колёса на земле').")]
        public LayerMask groundLayers = ~0;

        /// <summary>Вызывается один раз при падении.</summary>
        public event Action Crashed;
        /// <summary>Передача переключилась (номер новой передачи).</summary>
        public event Action<int> GearChanged;

        public bool IsCrashed { get; private set; }
        public bool RearGrounded { get; private set; }
        public bool FrontGrounded { get; private set; }
        public bool IsGrounded => RearGrounded || FrontGrounded;
        /// <summary>
        /// Колесо касается земли или до неё меньше Ground Grace (3 см): так «чувствует» землю гонщик —
        /// шина проминается, и на кочках контакт не пропадает. Жёсткое колесо Box2D на кочках дребезжит
        /// (мгновенные отрывы на доли секунды) — по этим флагам камера, звук, пыль и ноги гонщика не дёргаются.
        /// Управление байком (полёт, выравнивание, вилли) смотрит на настоящий контакт — RearGrounded / FrontGrounded.
        /// </summary>
        public bool RearNear { get; private set; }
        public bool FrontNear { get; private set; }
        public bool IsOnGround => RearNear || FrontNear;
        /// <summary>Скорость в м/с.</summary>
        public float Speed => frame != null ? frame.GetVelocity().magnitude : 0f;
        public Vector2 Velocity => frame != null ? frame.GetVelocity() : Vector2.zero;
        /// <summary>Угол шатунов, ° (для отрисовки ног). На накате гонщик ставит педали горизонтально.</summary>
        public float CrankAngle { get; private set; }
        /// <summary>Мощность ног сейчас, Вт: рывок до Pedal Peak Power, через несколько секунд — длительная Pedal Power.</summary>
        public float LegPower => config == null ? 0f :
            config.pedalPower + (config.pedalPeakPower - config.pedalPower) * Mathf.Exp(-_pedalTime / Mathf.Max(0.1f, config.peakPowerTime));

        /// <summary>Текущая передача: 1 — самая лёгкая … GearCount — самая тяжёлая (самая быстрая).</summary>
        public int Gear { get; private set; } = 1;
        /// <summary>Сколько всего передач (по числу звёзд кассеты).</summary>
        public int GearCount => config != null && config.cassette != null && config.cassette.Length > 0 ? config.cassette.Length : 1;
        /// <summary>Передаточное число: оборотов колеса на оборот шатунов (32/46 ≈ 0.7 … 32/11 ≈ 2.9).</summary>
        public float GearRatio
        {
            get
            {
                if (config == null || config.cassette == null || config.cassette.Length == 0) return 1f;
                int cog = config.cassette[Mathf.Clamp(Gear, 1, config.cassette.Length) - 1];
                return Mathf.Max(1, config.chainring) / (float)Mathf.Max(1, cog);
            }
        }
        /// <summary>Каденс — обороты шатунов в минуту (пока гонщик крутит педали).</summary>
        public float Cadence { get; private set; }
        /// <summary>Цепь сейчас перескакивает на другую звезду.</summary>
        public bool IsShifting => _shiftTimer > 0f;
        /// <summary>Сколько секунд переднее колесо уже не касается земли.</summary>
        public float FrontAirTime => _frontAirTime;
        /// <summary>Радиус колеса, м (по коллайдеру заднего колеса).</summary>
        public float WheelRadius => _rearCol is CircleCollider2D cc ? cc.radius * Mathf.Abs(rearWheel.transform.lossyScale.x) : 0.35f;

        private const float RpmToRad = 2f * Mathf.PI / 60f;

        private Collider2D _rearCol, _frontCol;
        private PhysicsMaterial2D _tireMaterial;
        private float _effort, _crankVel;
        private float _pullTime = 99f;
        private bool _pullArmed = true;
        private float _popTime = 99f;
        private bool _popArmed = true;
        private float _hopTimer, _hopCool;
        /// <summary>Присед перед прыжком, 0…1 (RiderBody опускает тело).</summary>
        public float HopCrouch { get; private set; }
        /// <summary>Гонщик оттолкнулся (прыжок).</summary>
        public event Action Hopped;
        private float _pedalTime, _airTime, _frontAirTime;
        private bool _airSteered;            // в этом полёте игрок уже крутил байк — дальше байк летит по инерции, без автовыравнивания
        private float _airFrom;              // с какой секунды полёта работает наклон
        private int _airPrevLean;
        private float _airPush;              // насколько гонщик уже перенёс вес в полёте (−1…1), плавно
        private const float AirSpinResponse = 0.03f;   // с: за сколько гонщик выходит на нужную скорость вращения (не быстрее Air Spin Accel)
        private const float LevelClearance = 0.6f;     // м: центр масс рамы над землёй, когда байк стоит на колёсах
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];
        private ContactFilter2D _groundFilter;
        private readonly Collider2D[] _near = new Collider2D[8];
        private float _shiftTimer;
        // полёт как в жизни: поворот байка относительно тела, группировка, рывок на краю трамплина
        private float _rel, _relV, _tuck = 1f;
        private bool _lipOk, _edgeOk, _prevFront = true;
        private float _lipK = 1f, _hopTime = -9f, _arcadeAir;
        private const float RelGain = 12f, RelTau = 0.03f;
        /// <summary>В воздухе: насколько гонщик повернул байк относительно своего тела, ° (+ — нос вверх). Тело повернуто на −это относительно рамы.</summary>
        public float AirTwist => _rel;
        /// <summary>Группировка в воздухе: доля инерции (меньше 1 — собрался, вращение быстрее; больше 1 — раскрылся).</summary>
        public float AirTuck => _tuck;

        private void Awake()
        {
            _rearCol = rearWheel.GetComponent<Collider2D>();
            _frontCol = frontWheel.GetComponent<Collider2D>();
            IgnoreSelfCollisions();
            ApplyConfig();
            if (config != null) Gear = Mathf.Clamp(config.startGear, 1, GearCount);
        }

        // ------------------------------------------------------------------ передачи

        /// <summary>Переключить передачу: +1 — тяжелее (быстрее), −1 — легче.</summary>
        public void ShiftGear(int delta)
        {
            if (config == null || delta == 0) return;
            int g = Mathf.Clamp(Gear + delta, 1, GearCount);
            if (g == Gear) return;
            Gear = g;
            _shiftTimer = config.shiftTime;      // под нагрузкой цепь перескакивает не мгновенно
            GearChanged?.Invoke(Gear);
        }

        /// <summary>
        /// Момент ног на заднем колесе на текущей передаче, Н·м (реальные единицы, без пульсации).
        /// wheelRel — скорость колеса относительно рамы, рад/с (знак не важен).
        /// </summary>
        public float PedalTorqueAt(float wheelRel)
        {
            if (config == null) return 0f;
            float ratio = GearRatio;
            float crankW = Mathf.Abs(wheelRel) / ratio;                                       // шатуны, рад/с
            float crankT = Mathf.Min(config.crankTorque, LegPower / Mathf.Max(0.5f, crankW)); // сила ног или мощность
            float wFull = config.cadenceFull * RpmToRad;
            float wMax = Mathf.Max(wFull + 0.1f, config.cadenceMax * RpmToRad);
            float fade = Mathf.Clamp01((wMax - crankW) / (wMax - wFull));                     // ноги не успевают крутить
            float shifting = _shiftTimer > 0f ? config.shiftTorque : 1f;
            return crankT / ratio * fade * shifting;
        }

        /// <summary>Цель мотора колеса — всегда чуть быстрее колеса: мотор только толкает (как трещотка втулки) и никогда не тормозит.</summary>
        private float DriveSpeed(float wheelRel)
        {
            float dir = config.invertMotor ? 1f : -1f;      // вперёд = по часовой = отрицательная скорость
            return dir * Mathf.Min(config.maxWheelSpeed, Mathf.Abs(wheelRel) * Mathf.Rad2Deg + 1440f);
        }

        /// <summary>Части байка не должны сталкиваться друг с другом.</summary>
        private void IgnoreSelfCollisions()
        {
            var cols = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < cols.Length; i++)
                for (int j = i + 1; j < cols.Length; j++)
                    Physics2D.IgnoreCollision(cols[i], cols[j], true);
        }

        /// <summary>
        /// Box2D считает пружину WheelJoint2D по приведённой массе «колесо — рама»: с лёгким передним колесом при той же частоте
        /// вилка стала бы в разы мягче. Множитель частоты, при котором жёсткость (Н/м) — как с колесом Wheel Mass.
        /// </summary>
        private float FrontSpringScale()
        {
            float mf = config.frameMass;
            float refM = 1f / (1f / Mathf.Max(0.01f, config.wheelMass) + 1f / mf);
            float m = 1f / (1f / Mathf.Max(0.01f, config.FrontWheelMass) + 1f / mf);
            return Mathf.Sqrt(refM / m);
        }

        /// <summary>Переносит значения из BikeConfig в физику (с внутренним масштабом масс).</summary>
        public void ApplyConfig()
        {
            if (config == null) return;
            float s = config.massScale;

            // часть инерции тела гонщика поворачивается вместе с рамой (тело гонщика само не вращается)
            // гонщик держит руль и педали жёстко: часть инерции его тела поворачивается вместе с рамой
            float riderI = config.riderInertiaCoupling * config.riderInertia + config.riderRigidity * config.riderMass * 0.55f * 0.55f;
            SetBody(frame, config.frameMass * s, (config.frameInertia + riderI) * s, config.frameCenterOfMass);
            SetBody(rearWheel, config.wheelMass * s, config.wheelInertia * s, Vector2.zero);
            SetBody(frontWheel, config.FrontWheelMass * s, config.FrontWheelInertia * s, Vector2.zero);
            if (rider != null && rider.body != null)
            {
                SetBody(rider.body, config.riderMass * s, config.riderInertia * s, Vector2.zero);
                rider.body.freezeRotation = true;
            }

            // Ось подвески (angle) задаётся при сборке байка и здесь не трогается.
            foreach (var joint in new[] { rearJoint, frontJoint })
            {
                JointSuspension2D sus = joint.suspension;
                sus.frequency = joint == rearJoint && config.rearSuspensionFrequency > 0f ? config.rearSuspensionFrequency : config.suspensionFrequency;
                if (joint == frontJoint) sus.frequency *= FrontSpringScale();     // лёгкое колесо — та же жёсткость вилки
                sus.dampingRatio = config.suspensionDamping;
                joint.suspension = sus;
            }

            if (_tireMaterial == null) _tireMaterial = new PhysicsMaterial2D("Tire");
            _tireMaterial.friction = config.tireFriction;
            _tireMaterial.bounciness = 0f;
            _rearCol.sharedMaterial = _tireMaterial;
            _frontCol.sharedMaterial = _tireMaterial;
        }

        /// <summary>До земли меньше Ground Grace (шина проминается — контакт не теряется на кочках).</summary>
        private bool NearGround(Rigidbody2D wheel)
        {
            if (config.groundGrace <= 0f) return false;
            _groundFilter.useTriggers = false;
            _groundFilter.SetLayerMask(groundLayers);
            int n = Physics2D.OverlapCircle(wheel.position, WheelRadius + config.groundGrace, _groundFilter, _near);
            for (int i = 0; i < n; i++)
                if (_near[i].attachedRigidbody == null || _near[i].attachedRigidbody.bodyType == RigidbodyType2D.Static) return true;   // трасса, а не свой байк
            return false;
        }

        private void SetBody(Rigidbody2D rb, float mass, float inertia, Vector2 com)
        {
            rb.useAutoMass = false;
            rb.mass = mass;
            rb.centerOfMass = com;
            rb.inertia = inertia;            // после массы и центра масс, чтобы Unity не пересчитал
            rb.gravityScale = config.gravityScale;
        }

#if UNITY_EDITOR
        private string _lastConfigJson;
        /// <summary>В редакторе: если BikeConfig поменяли во время Play — применить сразу.</summary>
        private void ApplyConfigIfChanged()
        {
            if (config == null) return;
            string json = JsonUtility.ToJson(config);
            if (json == _lastConfigJson) return;
            _lastConfigJson = json;
            ApplyConfig();
        }
#endif

        private void FixedUpdate()
        {
#if UNITY_EDITOR
            ApplyConfigIfChanged(); // в редакторе можно крутить настройки прямо во время Play
#endif
            if (config == null) return;
            float dt = Time.fixedDeltaTime;
            float s = config.massScale;
            RearGrounded = _rearCol.IsTouchingLayers(groundLayers);
            FrontGrounded = _frontCol.IsTouchingLayers(groundLayers);
            RearNear = RearGrounded || NearGround(rearWheel);
            FrontNear = FrontGrounded || NearGround(frontWheel);

            bool alive = !IsCrashed && input != null;
            bool brake = alive && input.Brake > 0f;
            bool pedal = alive && input.Pedal > 0f;          // газ и тормоз можно держать вместе: ноги крутят, тормоз тормозит
            float wheelRel = (rearWheel.angularVelocity - frame.angularVelocity) * Mathf.Deg2Rad; // рад/с, <0 — вперёд

            // --- усилие ног нарастает за pedalEffortRise, отпускание — быстро ---
            _effort = pedal ? Mathf.Min(1f, _effort + dt / Mathf.Max(0.01f, config.pedalEffortRise))
                            : Mathf.Max(0f, _effort - dt / 0.1f);
            _pedalTime = pedal ? _pedalTime + dt : Mathf.Max(0f, _pedalTime - dt * 2.5f);   // отдых восстанавливает силы
            CheckBreak(dt);
            _airTime = IsGrounded ? 0f : _airTime + dt;
            _frontAirTime = FrontGrounded ? 0f : _frontAirTime + dt;

            // --- передачи: + / − на клавиатуре или кнопки на экране ---
            int shift = input != null ? input.ConsumeShift() : 0;
            if (alive && shift != 0) ShiftGear(shift);
            _shiftTimer = pedal ? Mathf.Max(0f, _shiftTimer - dt) : 0f;   // без нагрузки цепь перескакивает сразу

            bool rearWheelie = alive && rider != null && rider.OnRearWheel && (!brake || rider.WheelieBrake);

            // --- шатуны: крутятся с колесом через передачу (быстрее ног на cadenceMax не бывает), на накате встают горизонтально ---
            if (pedal)
            {
                float crankW = Mathf.Clamp(wheelRel / GearRatio, -config.cadenceMax * RpmToRad, config.cadenceMax * RpmToRad);
                CrankAngle += crankW * Mathf.Rad2Deg * dt;   // по часовой, как колесо
                Cadence = Mathf.Abs(crankW) / RpmToRad;
                _crankVel = 0f;
            }
            else
            {
                float level = Mathf.Round(CrankAngle / 180f) * 180f;
                CrankAngle = Mathf.SmoothDamp(CrankAngle, level, ref _crankVel, 0.35f, Mathf.Infinity, dt);
                Cadence = 0f;
            }

            // --- педали и тормоза через моторы колёс ---
            if (brake && !rearWheelie)
            {
                // задний: если и газ держат — на колесо идёт разница между ногами и тормозом (обычно тормоз сильнее)
                float rearBrake = config.brakeRear * input.Brake;
                // жмёт тормоз — ноги сами давят слабее (Gas Brake Ground Share), иначе тормоз почти не тормозит
                float drive = pedal && _effort > 0f
                    ? PedalTorqueAt(wheelRel) * (1f + config.pedalRipple * Mathf.Cos(2f * CrankAngle * Mathf.Deg2Rad)) * _effort * Mathf.Clamp01(input.Pedal) * config.gasBrakeGroundShare : 0f;
                if (drive > rearBrake) SetMotor(rearJoint, DriveSpeed(wheelRel), (drive - rearBrake) * s);
                else SetMotor(rearJoint, 0f, (rearBrake - drive) * s);
                // передний — сколько даёт гонщик: изо всех сил, но не через руль (с D — стоппи)
                SetMotor(frontJoint, 0f, (rider != null ? rider.FrontBrakeCommand : config.brakeFront) * input.Brake * s);
            }
            else if (rearWheelie)
            {
                // на заднем колесе гонщик сам дозирует педали и задний тормоз (RiderBody)
                float t = rider.RearTorqueCommand;
                if (t > 0f)
                {
                    float ripple = 1f + 0.5f * config.pedalRipple * Mathf.Cos(2f * CrankAngle * Mathf.Deg2Rad);
                    SetMotor(rearJoint, DriveSpeed(wheelRel), t * ripple * s);
                }
                else if (t < 0f) SetMotor(rearJoint, 0f, -t * s);
                else rearJoint.useMotor = false;
                frontJoint.useMotor = false;
            }
            else if (_effort > 0f)
            {
                float ripple = 1f + config.pedalRipple * Mathf.Cos(2f * CrankAngle * Mathf.Deg2Rad);
                float torque = PedalTorqueAt(wheelRel);       // сила ног через текущую передачу
                SetMotor(rearJoint, DriveSpeed(wheelRel), torque * ripple * _effort * (input != null && input.Pedal > 0f ? Mathf.Clamp01(input.Pedal) : 1f) * s);   // нажим 0…1 (автопилот — плавно)
                frontJoint.useMotor = false;
            }
            else
            {
                rearJoint.useMotor = false;   // накат
                frontJoint.useMotor = false;
            }

            // --- подвеска: отбой (разжимается медленно) и упор в конце хода ---
            Suspension(rearJoint, rearWheel, s);
            Suspension(frontJoint, frontWheel, s);

            // --- сопротивление качению и воздуха ---
            Vector2 v = frame.GetVelocity();
            float speed = v.magnitude;
            if (speed > 0.05f)
            {
                float totalMass = config.frameMass + config.riderMass + config.WheelsMass;
                float g = -Physics2D.gravity.y * config.gravityScale;
                float roll = IsGrounded ? config.rollingResistance * totalMass * g : 0f;
                float air = 0.5f * 1.2f * config.airDragArea * speed * speed;
                frame.AddForce(-v / speed * (roll + air) * s);
            }

            if (!alive) return;

            // --- рывок руками: свежий бросок веса назад тянет руль (только с колесом на земле) ---
            if (input.Lean < -0.5f && _pullArmed && RearGrounded && FrontGrounded) { _pullTime = 0f; _pullArmed = false; }   // рывок — оторвать перед от земли
            if (input.Lean > -0.2f) _pullArmed = true;
            if (input.Lean > -0.5f) _pullTime = Mathf.Max(_pullTime, config.armPullTime);   // отпустил — руки больше не тянут
            _pullTime += dt;
            float groundPitch = Mathf.DeltaAngle(0f, frame.rotation) - (rider != null ? rider.SlopeAngle : 0f);
            if (_pullTime < config.armPullTime && IsGrounded && groundPitch < config.armPullMaxPitch
                && frame.angularVelocity < config.armPullMaxRate && _frontAirTime < 0.1f)
                frame.AddTorque(config.armPullTorque * Mathf.Sin(Mathf.PI * _pullTime / config.armPullTime) * s);

            // --- прыжок (банни-хоп): присед → толчок ногами от земли ---
            bool jump = input.ConsumeJump();
            if (jump && RearGrounded && FrontGrounded && _hopTimer <= 0f && _hopCool <= 0f) _hopTimer = Mathf.Max(0.01f, config.hopPreload);
            _hopCool = Mathf.Max(0f, _hopCool - dt);
            HopCrouch = 0f;
            if (_hopTimer > 0f)
            {
                _hopTimer -= dt;
                float ht = Mathf.Clamp01(1f - _hopTimer / Mathf.Max(0.01f, config.hopPreload));
                HopCrouch = ht * ht * (3f - 2f * ht);
                if (_hopTimer <= 0f)
                {
                    _hopTimer = 0f; _hopCool = config.hopCooldown; HopCrouch = 0f;
                    if (RearGrounded && FrontGrounded) Hop(groundPitch, input.Lean);
                }
            }

            // --- рывок ногами: свежий бросок веса вперёд (D) с обоими колёсами на земле — ноги подтягивают зад ---
            // (с тормозом не дёргают: D + S — это стоппи, его дозирует игрок тормозом)
            if (input.Lean > 0.5f && _popArmed && RearGrounded && FrontGrounded && Speed > 1f) { _popTime = 0f; _popArmed = false; }
            if (input.Lean < 0.2f) _popArmed = true;
            if (input.Lean < 0.5f) _popTime = Mathf.Max(_popTime, config.footPopTime);   // отпустил — ноги больше не тянут
            _popTime += dt;
            if (_popTime < config.footPopTime && FrontGrounded && input.Brake <= 0f && groundPitch > -config.footPopMaxLift)
                frame.AddTorque(-config.footPopTorque * Mathf.Sin(Mathf.PI * _popTime / config.footPopTime) * s);

            float omega = frame.angularVelocity * Mathf.Deg2Rad;            // рад/с, + = перед вверх

            // --- в полёте: игрок крутит байк весом тела ---
            // вперёд — заднее колесо вверх (нос вниз), назад — перед вверх; отпустил — гонщик гасит вращение,
            // и байк остаётся под выбранным углом. Наклон, который держали ещё на земле (вес назад перед дропом, вилли), —
            // это поза, а не вращение: он включается только в долгом полёте. Нажатие в воздухе работает сразу.
            int leanDir = input.Lean > 0f ? 1 : (input.Lean < 0f ? -1 : 0);
            if (config.airArcade)
            {
                float spin = AirArcade(dt, omega, input.Lean);
                if (spin != 0f) SpinWholeBike(spin);
            }
            else if (config.airRealistic)
            {
                float spin = AirRealistic(dt, omega, input.Lean);
                if (spin != 0f) SpinWholeBike(spin);
            }
            else if (IsGrounded)
            {
                _airSteered = false;
                _airPush = 0f;
                // наклон, нажатый ещё на земле, крутит байк сразу после отрыва (0.05 с — чтобы не дёргало на кочках);
                // отскок заднего колеса в вилли (перед давно в воздухе) — чуть позже, иначе вилли переворачивалось бы
                float carry = _frontAirTime > config.airWheelieFront ? config.airCarryWheelie : config.airCarryDelay;
                _airFrom = leanDir != 0 ? carry : config.airControlDelay;
            }
            else
            {
                if (leanDir != 0 && leanDir != _airPrevLean) _airFrom = Mathf.Min(_airFrom, _airTime);   // нажал в воздухе
                float k = Mathf.SmoothStep(0f, 1f, (_airTime - _airFrom) / Mathf.Max(0.01f, config.airControlRamp));
                if (leanDir != 0 && k > 0f) _airSteered = true;
                if (_airSteered)
                {
                    // по инерции: вес переносится плавно (Air Push Time), наклон разгоняет вращение с ускорением Air Spin Accel;
                    // отпустил — байк крутится дальше (момент импульса сохраняется), лишь чуть гаснет (Air Coast).
                    // Остановить вращение — наклоном в обратную сторону.
                    float want = -input.Lean * k;
                    _airPush += (want - _airPush) * Mathf.Min(1f, dt / Mathf.Max(0.01f, config.airPushTime));
                    float acc = config.airSpinAccel * Mathf.Deg2Rad * _airPush;
                    float wMax = config.airSpinRate * Mathf.Deg2Rad;
                    if ((acc > 0f && omega > wMax) || (acc < 0f && omega < -wMax)) acc = 0f;   // быстрее не раскрутить
                    float spin = acc - config.airCoast * omega;
                    if (spin != 0f) SpinWholeBike(spin);
                }
            }
            _airPrevLean = leanDir;

            if (RearGrounded && !FrontGrounded && groundPitch > 3f)
            {
                // на заднем колесе руки и корпус держат байк жёстко — вращение гасится (балансировать можно, как в жизни)
                frame.AddTorque(-config.wheelieDamping * omega * s);
            }
            else if (RearGrounded && !FrontGrounded && input.Lean == 0f && omega < 0f)
            {
                // перед без опоры (край дропа): руки держат руль и не дают носу клевать
                frame.AddTorque(Mathf.Min(config.armHoldMax, -config.armHoldDamping * omega) * s);
            }
            if ((config.airAutoLevel || AutoLevel) && !IsGrounded && input.Lean == 0f && !_airSteered)
            {
                // (у игрока выключено по умолчанию; у бота меню — AutoLevel) без кнопок гонщик смотрит, куда приземлится,
                // и ставит байк под уклон там (чуть задним колесом вперёд)
                float target = (PredictLandingSlope(out float landSlope) ? landSlope : config.airLevelTarget) + config.airLevelBias;
                float gain = AutoLevel ? AutoLevelGain : config.airLevelGain, rate = AutoLevel ? AutoLevelRate : config.airLevelRate;
                float wDes = Mathf.Clamp(gain * (target - Mathf.DeltaAngle(0f, frame.rotation)), -rate, rate) * Mathf.Deg2Rad;
                float amax = (AutoLevel ? AutoLevelAccel : config.airLevelAccel) * Mathf.Deg2Rad;
                float level = Mathf.Clamp((wDes - omega) / AirSpinResponse, -amax, amax) * Mathf.SmoothStep(0f, 1f, _airTime / 0.15f);
                if (level != 0f) SpinWholeBike(level);
            }

            frame.angularVelocity = Mathf.Clamp(frame.angularVelocity, -config.maxAngularSpeed, config.maxAngularSpeed);
        }

        /// <summary>
        /// Полёт как в жизни (Air Realistic). В воздухе на байк с гонщиком нет внешнего момента — вращение сохраняется.
        ///   • A/D: гонщик руками и ногами поворачивает байк относительно своего тела (до Rel Max°). Байк легче тела, поэтому
        ///     поворачивается на долю Rel Bike Share, тело — в обратную сторону. Отпустил — держит байк, где он есть.
        ///   • Группировка: наклон по ходу вращения — собрался, вращение быстрее (Tuck In); против — раскрылся, медленнее (Tuck Out).
        ///     Отпустил — вращение возвращается к прежнему (момент вращения тот же).
        ///   • Рывок на краю трамплина или уступа: заднее колесо на последних Lip Time с кикера (или перед только что съехал с края
        ///     уступа) — A/D тянут или толкают байк через заднее колесо (опора о землю) и задают вращение на весь полёт.
        /// Возвращает угловое ускорение всей системы вокруг общего центра масс, рад/с².
        /// </summary>
        /// <summary>
        /// Сальто как в Mountain Bike Xtreme, с инерцией (Air Arcade): в воздухе держите A/D — байк плавно раскручивается до
        /// Arcade Spin Rate; отпустили — крутится дальше по инерции, лишь чуть затухая; остановить — обратной кнопкой.
        /// Возвращает угловое ускорение всей системы вокруг общего центра масс, рад/с².
        /// </summary>
        private float AirArcade(float dt, float omega, float lean)
        {
            if (IsGrounded) { _arcadeAir = 0f; return 0f; }
            _arcadeAir += dt;
            if (_arcadeAir < config.arcadeDelay) return 0f;
            float w = omega * Mathf.Rad2Deg, dIn = -lean;                     // +1 — A (нос вверх), −1 — D
            float acc = dIn != 0f
                ? Mathf.Clamp((dIn * config.arcadeSpinRate - w) / dt, -config.arcadeSpinAccel, config.arcadeSpinAccel)
                : Mathf.Clamp(-w / dt, -config.arcadeStopAccel, config.arcadeStopAccel);
            return acc * Mathf.Deg2Rad;
        }

        private float AirRealistic(float dt, float omega, float lean)
        {
            float dIn = -lean;                                                 // +1 — A (нос вверх), −1 — D
            bool hopped = Time.time - _hopTime < 0.4f;                         // перед оторвался от прыжка, а не от края
            if (!FrontGrounded && _prevFront && RearGrounded)
            {
                // переднее колесо только что оторвалось, зад на земле — край уступа (земля впереди ушла вниз)?
                // Вилли — не край: там перед поднимается сам, вращение уже есть.
                _edgeOk = !hopped && omega * Mathf.Rad2Deg < config.lipMaxRate && GroundDropsAhead();
            }
            if (FrontGrounded) _edgeOk = false;
            _prevFront = FrontGrounded;

            // окно рывка: зад на краю (на земле или над ней не выше Lip Reach — на краю колесо часто чуть отрывается) и
            //  • кикер — заднее колесо на последних ~Lip Time с кикера (под ним земля поднимается, а впереди — заметно положе).
            //    Перед на кикере часто отрывается раньше зада (переход кикера в прямой край): рывок — пока зад не сошёл с края;
            //  • уступ — перед только что съехал с края (не дольше Lip Time).
            _lipOk = false;
            Vector2 rp = rearWheel.position;
            if (!hopped && Speed > config.lipMinSpeed && GroundAt(rp.x, rp.y, out float rh)
                && GroundAt(rp.x - 0.6f, rp.y, out float hb0) && GroundAt(rp.x - 0.1f, rp.y, out float hb1))
            {
                // уклон кикера под задним колесом — по земле позади точки касания: у самого края впереди уже обрыв (бутер,
                // гэп), и уклон «вокруг колеса» оборвал бы окно рывка раньше, чем зад сойдёт с края
                float at = Mathf.Atan2(hb1 - hb0, 0.5f) * Mathf.Rad2Deg;
                // колесо на склоне касается земли не под центром — зазор считаем поперёк склона
                bool rearNear = RearGrounded || rp.y - rh - WheelRadius / Mathf.Max(0.3f, Mathf.Cos(at * Mathf.Deg2Rad)) < config.lipReach;
                if (rearNear)
                {
                    float ahead = GroundSlopeAt(rp.x + Mathf.Clamp(Speed * config.lipTime, 0.5f, config.lipAhead), rp.y);
                    Vector2 v = frame.GetVelocity();
                    float rise = Mathf.Atan2(v.y, Mathf.Abs(v.x) + 1e-6f) * Mathf.Rad2Deg;
                    // тянуть (A) можно и с передом на кикере (гонщик «выстреливает» с края), толкать вперёд (D) — только когда
                    // перед уже за краем (впереди него земля не поднимается): иначе он упирается в кикер
                    Vector2 fp = frontWheel.position;
                    bool frontOver = !FrontGrounded && GroundSlopeAt(fp.x + 0.4f, fp.y) < at - config.lipRise;
                    if (at > config.lipRise && at - ahead > config.lipRise && (dIn > 0f || frontOver))
                    {
                        // кикер даёт полный рывок (байк летит вверх — есть во что упереться), пологий — слабее
                        _lipOk = true;
                        _lipK = Mathf.Max(config.lipEdge, Mathf.Clamp01(Mathf.Max(at, rise) / Mathf.Max(1f, config.lipRiseFull)));
                    }
                    else if (RearGrounded && !FrontGrounded && _edgeOk && _frontAirTime < config.lipTime)
                    {
                        _lipOk = true; _lipK = config.lipEdge;                     // край уступа — слабый рывок, как мэнуал
                    }
                }
            }
            if (_lipOk || IsGrounded)
            {
                _rel = 0f; _relV = 0f; _tuck = 1f;
                if (_lipOk && dIn != 0f)
                {
                    float sgn = Mathf.Sign(dIn);
                    float wMax = (sgn > 0f ? config.lipSpinBack : config.lipSpinForward) * Mathf.Deg2Rad * _lipK;
                    if (sgn * omega < wMax) return dIn * config.lipSpinAccel * Mathf.Deg2Rad;
                }
                return 0f;
            }
            float tgt = dIn != 0f ? dIn * config.relMax : _rel;
            float wDes = Mathf.Clamp(RelGain * (tgt - _rel), -config.relRate, config.relRate);
            float acc = Mathf.Clamp((wDes - _relV) / RelTau, -config.relAccel, config.relAccel);
            _relV += acc * dt;
            _rel += _relV * dt;
            float spin = config.relBikeShare * acc * Mathf.Deg2Rad;
            float baseW = omega - config.relBikeShare * _relV * Mathf.Deg2Rad;    // вращение системы без поворота байка относительно тела
            float target = 1f;
            if (Mathf.Abs(baseW) * Mathf.Rad2Deg > config.tuckMinRate && dIn != 0f)
                target = dIn * baseW > 0f ? config.tuckIn : config.tuckOut;
            float old = _tuck;
            _tuck = Mathf.MoveTowards(_tuck, target, config.tuckRate * dt);
            if (_tuck != old) spin += baseW * (old / _tuck - 1f) / dt;           // момент вращения тот же, инерция другая
            return spin;
        }

        /// <summary>Высота земли (статичные коллайдеры трассы) под точкой x — луч вниз с высоты y + 4 м.</summary>
        private bool GroundAt(float x, float y, out float h)
        {
            h = 0f;
            _groundFilter.useTriggers = false;
            _groundFilter.SetLayerMask(groundLayers);
            int n = Physics2D.Raycast(new Vector2(x, y + 4f), Vector2.down, _groundFilter, _hits, 12f);
            for (int i = 0; i < n; i++)
                if (_hits[i].rigidbody == null || _hits[i].rigidbody.bodyType == RigidbodyType2D.Static) { h = _hits[i].point.y; return true; }
            return false;
        }

        /// <summary>Уклон земли около x (по точкам x ± 0.4 м), ° (+ — в гору); земли нет (обрыв) — −90.</summary>
        private float GroundSlopeAt(float x, float y)
        {
            if (!GroundAt(x - 0.4f, y, out float h0) || !GroundAt(x + 0.4f, y, out float h1)) return -90f;
            return Mathf.Atan2(h1 - h0, 0.8f) * Mathf.Rad2Deg;
        }

        /// <summary>Земля впереди переднего колеса ушла вниз (край уступа)?</summary>
        private bool GroundDropsAhead()
        {
            _groundFilter.useTriggers = false;
            _groundFilter.SetLayerMask(groundLayers);
            Vector2 origin = frontWheel.position + (Vector2)frame.transform.right * 0.35f;
            float reach = WheelRadius + config.lipDrop;
            int n = Physics2D.Raycast(origin, Vector2.down, _groundFilter, _hits, reach);
            for (int i = 0; i < n; i++)
                if (_hits[i].rigidbody == null || _hits[i].rigidbody.bodyType == RigidbodyType2D.Static) return false;   // земля рядом
            return true;
        }

        /// <summary>
        /// Толчок ногами от земли: вся система (рама, колёса, тело) получает скорость v = √(2·g·Hop Height) — поровну поперёк
        /// байка и вверх. Вращение в прыжке задаёт направление толчка (через центр масс или мимо него), а не то, что было до
        /// прыжка: ровно — лёгкий нос вверх (Hop Pitch Rate), поднятый перед (мэнуал) толчок выравнивает за Hop Level Time,
        /// A — +Hop Lean Back °/с, D — нос вниз (−Hop Lean Forward). Изменить вращение можно не больше чем на Hop Spin Max °/с.
        /// </summary>
        private void Hop(float groundPitch, float lean)
        {
            float g = -Physics2D.gravity.y * config.gravityScale;
            float v = Mathf.Sqrt(2f * g * Mathf.Max(0f, config.hopHeight));
            Vector2 dir = ((Vector2)frame.transform.up * 0.5f + Vector2.up * 0.5f).normalized;
            Rigidbody2D body = rider != null && rider.body != null && rider.body.simulated ? rider.body : null;
            Rigidbody2D[] bodies = body != null ? new[] { frame, rearWheel, frontWheel, body } : new[] { frame, rearWheel, frontWheel };
            float m = 0f; Vector2 com = Vector2.zero;
            foreach (Rigidbody2D rb in bodies) { m += rb.mass; com += rb.mass * rb.worldCenterOfMass; }
            com /= Mathf.Max(1e-4f, m);
            float wDeg = config.hopPitchRate;
            if (config.hopRealistic)
            {
                float want = config.hopPitchRate - groundPitch / Mathf.Max(0.05f, config.hopLevelTime);
                if (lean < -0.5f) want += config.hopLeanBack;
                else if (lean > 0.5f) want -= config.hopLeanForward;
                wDeg = Mathf.Clamp(want - frame.angularVelocity, -config.hopSpinMax, config.hopSpinMax);
            }
            float w = wDeg * Mathf.Deg2Rad;
            foreach (Rigidbody2D rb in bodies)
            {
                Vector2 r = rb.worldCenterOfMass - com;
                rb.SetVelocity(rb.GetVelocity() + dir * v + new Vector2(-w * r.y, w * r.x));
            }
            frame.angularVelocity += wDeg;
            _hopTime = Time.time;
            Hopped?.Invoke();
        }

        /// <summary>
        /// Гонщик смотрит, куда приземлится: полёт по параболе (центр масс рамы минус высота над землёй),
        /// первая встреча с землёй (статичные коллайдеры трассы, без своих колёс и тела). Уклон там, ° (+ — в гору).
        /// </summary>
        private bool PredictLandingSlope(out float slopeDeg)
        {
            slopeDeg = 0f;
            _groundFilter.useTriggers = false;
            _groundFilter.SetLayerMask(groundLayers);
            Vector2 p = frame.worldCenterOfMass - new Vector2(0f, LevelClearance);
            Vector2 v = frame.GetVelocity();
            Vector2 g = Physics2D.gravity * config.gravityScale;
            const float step = 0.04f;
            for (int i = 0; i < 60; i++)
            {
                Vector2 q = p + v * step + 0.5f * step * step * g;
                v += g * step;
                int n = Physics2D.Linecast(p, q, _groundFilter, _hits);
                float best = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    // свои колёса, рама и тело (у них Rigidbody2D) и земля снизу (нормаль вниз) — не то место приземления
                    if (_hits[k].rigidbody != null || _hits[k].normal.y < 0.2f || _hits[k].fraction >= best) continue;
                    best = _hits[k].fraction;
                    slopeDeg = Mathf.Atan2(-_hits[k].normal.x, _hits[k].normal.y) * Mathf.Rad2Deg;
                }
                if (best < float.MaxValue) return true;
                p = q;
            }
            return false;
        }

        /// <summary>
        /// Поворачивает всю систему (раму, колёса и гонщика) вокруг общего центра масс с угловым ускорением alpha, рад/с².
        /// Внутри ничего не напрягается: тело гонщика не сгибается, подвеска не сжимается, траектория полёта не меняется.
        /// </summary>
        private void SpinWholeBike(float alpha)
        {
            Rigidbody2D body = rider != null ? rider.body : null;
            float m = frame.mass + rearWheel.mass + frontWheel.mass + (body != null ? body.mass : 0f);
            Vector2 cm = frame.mass * frame.worldCenterOfMass + rearWheel.mass * rearWheel.worldCenterOfMass
                       + frontWheel.mass * frontWheel.worldCenterOfMass;
            if (body != null) cm += body.mass * body.worldCenterOfMass;
            cm /= m;
            SpinAround(frame, alpha, cm);
            frame.AddTorque(frame.inertia * alpha);
            SpinAround(rearWheel, alpha, cm);
            SpinAround(frontWheel, alpha, cm);
            if (body != null) SpinAround(body, alpha, cm);
        }

        /// <summary>Сила, которая ведёт центр масс тела по дуге вокруг cm с угловым ускорением alpha.</summary>
        private static void SpinAround(Rigidbody2D rb, float alpha, Vector2 cm)
        {
            Vector2 r = rb.worldCenterOfMass - cm;
            rb.AddForce(rb.mass * alpha * new Vector2(-r.y, r.x));
        }

        /// <summary>
        /// Box2D считает гашение пружины по лёгкому колесу — для тяжёлого байка с гонщиком подвеска получается «батутом».
        /// Добавляем настоящий демпфер отбоя (только на разжатие) и упор в конце хода.
        /// </summary>
        private void Suspension(WheelJoint2D joint, Rigidbody2D wheel, float s)
        {
            if (joint == null) return;
            float dt = Time.fixedDeltaTime;
            Rigidbody2D body = joint.attachedRigidbody;                         // рама (или её передняя часть, если рама разломалась)
            Vector2 axis = body.transform.up;
            Vector2 anchor = body.transform.TransformPoint(joint.anchor);
            float comp = Vector2.Dot(wheel.position - anchor, axis);                          // >0 — подвеска сжата
            float vel = Vector2.Dot(wheel.GetVelocity() - body.GetPointVelocity(anchor), axis); // >0 — сжимается
            float impulse = 0f;                                                                 // импульс на колесо вдоль оси
            float mEff = 1f / (1f / wheel.mass + 1f / body.mass);
            // отбой: точное экспоненциальное гашение скорости разжатия — устойчиво при шаге физики 0.01 с
            if (vel < 0f)
                impulse += mEff * (-vel) * (1f - Mathf.Exp(-config.reboundDamping * s * dt / mEff));
            // упор в конце хода: мягкая связь (как в Box2D), только отталкивает
            float travel = joint == rearJoint && config.rearSuspensionTravel > 0f ? config.rearSuspensionTravel : config.suspensionTravel;
            float over = comp - travel;
            if (over > 0f)
            {
                float mB = 1f / (1f / wheel.mass + 1f / (body.mass + config.bumpRiderShare * config.riderMass * s));
                float w = 2f * Mathf.PI * config.bumpFrequency;
                float a1 = 2f + dt * w, a2 = dt * w * a1, a3 = 1f / (1f + a2);
                float lambda = -(a2 * a3) * mB * (vel + w / a1 * over);
                impulse += Mathf.Min(0f, lambda);
            }
            if (impulse == 0f) return;
            Vector2 force = axis * (impulse / dt);
            wheel.AddForce(force);
            body.AddForceAtPosition(-force, anchor);
        }

        private static void SetMotor(WheelJoint2D joint, float speed, float torque)
        {
            JointMotor2D m = joint.motor;
            m.motorSpeed = speed;
            m.maxMotorTorque = torque;
            joint.motor = m;
            joint.useMotor = true;
        }

        /// <summary>Рама разломалась (удар сильнее, чем падение на ровное с Break Fall Height) — байк сломан, это падение.</summary>
        public bool IsBroken { get; private set; }
        /// <summary>Может ли рама сломаться (в главном меню бот катается без поломок).</summary>
        [System.NonSerialized] public bool CanBreak = true;
        /// <summary>
        /// Авто-выравнивание в полёте только для этого байка (бот главного меню): без кнопок гонщик сам ставит байк под уклон
        /// приземления — как Air Auto Level в настройках, но со своей силой (AutoLevelGain 1/с, Rate °/с, Accel °/с²).
        /// </summary>
        [System.NonSerialized] public bool AutoLevel;
        [System.NonSerialized] public float AutoLevelGain = 4f, AutoLevelRate = 220f, AutoLevelAccel = 700f;
        public event Action Broken;
        private Vector2 _airVel;

        /// <summary>
        /// Приземление после полёта: скорость удара поперёк земли (по нормали под колесом) сравнивается со скоростью падения
        /// на ровное с высоты Break Fall Height: v = √(2·g·h). Сильнее — рама трескается.
        /// </summary>
        private void CheckBreak(float dt)
        {
            if (IsBroken || !CanBreak || config == null || config.breakFallHeight <= 0f) return;
            if (!IsGrounded) { _airVel = frame.GetVelocity(); return; }
            if (_airTime < 0.15f) return;                                        // только что был в воздухе — это приземление
            Vector2 p = RearGrounded ? rearWheel.position : frontWheel.position;
            float slope = GroundSlopeAt(p.x, p.y);
            Vector2 n = slope > -89f ? new Vector2(-Mathf.Sin(slope * Mathf.Deg2Rad), Mathf.Cos(slope * Mathf.Deg2Rad)) : Vector2.up;
            float impact = Mathf.Max(0f, -Vector2.Dot(_airVel, n));
            float g = -Physics2D.gravity.y * config.gravityScale;
            if (impact < Mathf.Sqrt(2f * g * config.breakFallHeight)) return;
            IsBroken = true;
            SplitFrame(_airVel);
            Broken?.Invoke();
            Crash();
        }

        /// <summary>Передняя часть разломанной рамы (рулевой стакан, вилка, переднее колесо) — отдельное тело; null, пока рама цела.</summary>
        public Rigidbody2D FrontPiece { get; private set; }

        /// <summary>
        /// Рама разламывается пополам по нижней и верхней трубам у рулевого стакана: передняя часть с вилкой и колесом
        /// становится отдельным телом (переднее колесо переходит к ней на своей подвеске) и по инерции летит дальше,
        /// кувыркаясь через ось, задняя часть с гонщиком, кареткой и задним колесом остаётся на месте удара.
        /// </summary>
        private void SplitFrame(Vector2 vBefore)
        {
            if (FrontPiece != null || frontJoint == null) return;
            Transform f = frame.transform;
            var go = new GameObject("FrameFront");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(f.position, f.rotation);   // в той же позе — спрайты и опоры остаются на местах
            go.layer = frame.gameObject.layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.useAutoMass = false;
            rb.gravityScale = frame.gravityScale;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // рулевой стакан с обломками труб, руль и вынос (кромка — по линии разлома)
            var col = go.AddComponent<BoxCollider2D>();
            col.offset = new Vector2(0.30f, 0.44f);
            col.size = new Vector2(0.18f, 0.30f);
            Collider2D frameCol = frame.GetComponent<Collider2D>();
            if (frameCol != null) col.sharedMaterial = frameCol.sharedMaterial;
            float share = 0.3f;                                              // доля массы рамы у передней части
            rb.mass = frame.mass * share;
            frame.mass *= 1f - share;
            // рама теперь только сзади разлома
            if (frameCol is BoxCollider2D box)
            {
                float right = Mathf.Min(box.offset.x + box.size.x * 0.5f, 0.22f), left = box.offset.x - box.size.x * 0.5f;
                box.size = new Vector2(right - left, box.size.y);
                box.offset = new Vector2((left + right) * 0.5f, box.offset.y);
            }
            // переднее колесо — на той же подвеске, но уже к передней части
            var j = go.AddComponent<WheelJoint2D>();
            j.autoConfigureConnectedAnchor = false;
            j.connectedBody = frontWheel;
            j.anchor = frontJoint.anchor;
            j.connectedAnchor = frontJoint.connectedAnchor;
            j.suspension = frontJoint.suspension;
            j.enableCollision = false;
            j.useMotor = false;
            frontJoint.enabled = false;
            Destroy(frontJoint);
            frontJoint = j;
            // части байка друг за друга не цепляются
            foreach (Collider2D other in GetComponentsInChildren<Collider2D>(true))
                if (other != col) Physics2D.IgnoreCollision(col, other, true);
            // по инерции: передняя часть летит дальше и кувыркается вперёд, задняя от удара подпрыгивает назад
            Vector2 fwd = f.right, up = f.up;
            Vector2 anchorW = f.TransformPoint(col.offset);
            rb.SetVelocity(frame.GetPointVelocity(anchorW) + fwd * (1.5f + 0.15f * Mathf.Abs(vBefore.x)) + up * 1.2f);
            rb.angularVelocity = frame.angularVelocity - 420f;
            frame.SetVelocity(frame.GetVelocity() - fwd * 0.8f + up * 0.6f);
            frame.angularVelocity += 160f;
            FrontPiece = rb;
        }

        /// <summary>Падение: отключает управление и сообщает об этом всем подписчикам.</summary>
        public void Crash()
        {
            if (IsCrashed) return;
            IsCrashed = true;
            if (input != null) input.InputEnabled = false;
            Crashed?.Invoke();
        }
    }
}
