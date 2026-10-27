using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Внешний вид байка и гонщика из спрайтов BikeSkin. Физику не трогает — только читает положения рамы, колёс и тела.
    ///   • Байк: колёса крутятся, задний маятник качается за осью, вилка телескопически сжимается,
    ///     шатуны крутятся только при педалировании.
    ///   • Гонщик собран из частей (корпус, бедро, голень, ступня, плечо, предплечье, голова) и стоит в стойке райдера:
    ///     корпус наклонён вперёд, локти и колени согнуты. Тело рисуется ТАМ, ГДЕ ЕГО МАССА В ФИЗИКЕ: по центру масс
    ///     из RiderBody (перенос веса, присед на приземлении, отставание по инерции) подбирается положение таза так, чтобы
    ///     центр масс нарисованного тела (массы частей по Демпстеру) совпал с физическим. Поэтому присед, вынос таза назад,
    ///     бросок вперёд — следствие движения массы, а не отдельная анимация.
    ///   • Ноги крутят педали (IK), стопы «работают»: на педалировании носок опускается внизу хода, на накате пятки вниз.
    ///   • Голова держит взгляд по ходу движения, шея — пружина с гашением (на ударах голова чуть «кивает» и выравнивается).
    ///   • Падение: гонщик отцепляется от байка и падает куклой (RiderRagdoll) — руки и ноги в пределах суставов.
    /// Геометрия совпадает с нарисованными спрайтами и с Tools/art/rider_pose_new.py — меняйте вместе.
    /// Висит на корневом объекте Bike.
    /// </summary>
    public class BikeVisual : MonoBehaviour
    {
        public BikeController2D bike;
        public BikeInput input;
        public BikeSkin skin;
        [Tooltip("Unlit-материал для спрайтов (создаётся сборщиком сцены).")]
        public Material material;
        [Tooltip("Рама разломалась: насколько гонщика бросает через руль, м/с (вперёд по раме, вверх).")]
        public Vector2 brokenFling = new Vector2(3f, 2.2f);
        [Tooltip("Рама разломалась: кувырок гонщика через голову, °/с (минус — вперёд).")]
        public float brokenSpin = -380f;

        [Header("Геометрия байка (локально относительно рамы, метры)")]
        public Vector2 bottomBracket = new Vector2(-0.141f, -0.039f);
        [Tooltip("Нижняя ось маятника (за кареткой). Перья и верхние перья к качалке — одна жёсткая деталь.")]
        public Vector2 swingarmPivot = new Vector2(-0.115f, 0.028f);
        public Vector2 headTubeBottom = new Vector2(0.354f, 0.369f);
        public Vector2 grip = new Vector2(0.335f, 0.585f);
        public Vector2 saddle = new Vector2(-0.31f, 0.66f);
        public float crankLength = 0.17f;

        [Header("Скелет гонщика")]
        [Tooltip("Физическое тело гонщика: откуда брать центр масс, перенос веса и наклон тела.")]
        public RiderBody rider;
        [Tooltip("Длины частей тела, м (взрослый ~180 см): корпус — от таза до плеча.")]
        public float torsoLength = 0.48f;
        public float thigh = 0.46f, shin = 0.45f;
        public float upperArm = 0.30f, forearm = 0.29f;
        [Tooltip("Голеностоп относительно оси педали (ступня горизонтально).")]
        public Vector2 ankleOffset = new Vector2(-0.07f, 0.075f);
        [Tooltip("Голова от плеча (корпус под 40°): вперёд-вверх.")]
        public Vector2 neck = new Vector2(0.085f, 0.175f);
        [Tooltip("Дальнее плечо чуть сдвинуто — чтобы руки не сливались.")]
        public Vector2 farShoulderOffset = new Vector2(-0.025f, 0.01f);
        [Tooltip("Доля длины ноги, больше которой нога не разгибается.")]
        [Range(0.8f, 1f)] public float maxLegExtension = 0.97f;

        [Header("Стойка (как её рисуем)")]
        [Tooltip("Угол корпуса к раме, °: вес назад / нейтрально / вперёд.")]
        public float torsoBack = 36f, torsoNeutral = 40f, torsoForward = 26f;
        [Tooltip("Насколько выпрямлены руки (доля длины): назад / нейтрально / вперёд.")]
        public float armBack = 0.97f, armNeutral = 0.86f, armForward = 0.70f;
        [Tooltip("Руки: не сгибаются сильнее и не выпрямляются больше этих долей длины.")]
        public float armMin = 0.66f, armMax = 0.985f;
        [Tooltip("Угол корпуса к раме — пределы, °.")]
        public float torsoMin = 12f, torsoMax = 85f;
        [Tooltip("Угол корпуса к горизонту — пределы, °: нос байка вниз — гонщик выпрямляется, вверх — наклоняется.")]
        public float torsoWorldMin = 24f, torsoWorldMax = 72f;
        [Tooltip("Таз в нарисованных стойках (вес назад / нейтрально / вперёд). Разница центра масс такой фигуры и физической " +
                 "стойки добавляется к физическому центру масс: статичная поза — настоящая стойка райдера, а движения — из физики.")]
        public Vector2 hipVisBack = new Vector2(-0.50f, 0.54f), hipVisNeutral = new Vector2(-0.36f, 0.67f), hipVisForward = new Vector2(-0.13f, 0.62f);
        [Tooltip("Таз в обычной стойке не опускается ниже, м; в приседе на приземлении — ещё на Hip Floor Squat ниже (по силе удара).")]
        public float hipFloor = 0.56f, hipFloorSquat = 0.08f;
        [Tooltip("Присед на приземлении: насколько грудь опускается к рулю, ° и насколько сгибаются руки.")]
        public float compressTorso = 8f, compressArms = 0.08f;
        [Tooltip("Ступня на педалировании: средний угол, размах, фаза (°). На накате — пятки вниз на Foot Coast°.")]
        public float footMean = -6f, footSwing = 13f, footPhase = 45f, footCoast = 10f;

        [Header("Педалирование всем телом")]
        [Tooltip("Райдер крутит педали стоя, работая всем телом: на каждом рабочем ходе (педаль впереди) вес падает на педаль — " +
                 "таз чуть опускается и уходит вперёд (м), корпус почти неподвижен (°), голова смотрит вперёд по трассе (°); " +
                 "рука со стороны давящей ноги тянет руль на себя — плечо подходит к рулю, локоть сгибается (м), " +
                 "другая рука выпрямляется. Сильнее всего — в разгоне с места, на скорости и сидя — мягче, на каденсе 90+ — только ноги.")]
        public float pumpHipDrop = 0.012f, pumpHipForward = 0.012f, pumpTorso = 2f, pumpHead = 0f, pumpShoulder = 0.03f;
        [Tooltip("За сколько секунд «раскачка» телом нарастает и затихает, с.")]
        public float pumpResponse = 0.25f;
        [Tooltip("Скорость (м/с), на которой раскачка телом слабее всего (доля Pump At Speed от полной).")]
        public float pumpFadeSpeed = 14f;
        [Range(0f, 1f)] public float pumpAtSpeed = 0.4f;
        [Tooltip("Каденс, об/мин: до Pump Cadence Full тело работает в полную силу, к Pump Cadence Still раскачка плавно гаснет, " +
                 "а с него и выше (быстрое верчение, 90+) работают только ноги — корпус, руки и голова неподвижны.")]
        public float pumpCadenceFull = 70f, pumpCadenceStill = 90f;

        [Header("Посадка (по видео-образцу)")]
        [Tooltip("По ровному и в подъём гонщик едет СИДЯ: таз на седле, корпус почти вертикален, верх тела неподвижен — " +
                 "крутят только ноги (как в видео-образце). Встаёт на спуске круче Sit Max Descent°, при переносе веса (A/D), " +
                 "в приседе на приземлении, в прыжке и в полёте.")]
        public bool sitWhenRiding = true;
        public float sitMaxDescent = 5f;
        [Tooltip("Угол корпуса к раме сидя, ° (стоя — Torso Neutral).")]
        public float torsoSeated = 60f;
        [Tooltip("За сколько секунд садится и встаёт.")]
        public float sitTime = 0.35f;

        [Header("Рельеф: подъём и спуск (как настоящий райдер)")]
        [Tooltip("Уклон земли (°), с которого гонщик начинает менять стойку, и при котором — полностью.")]
        public float terrainFrom = 4f, terrainFull = 14f;
        [Tooltip("В подъём: грудь ниже к рулю (°) и таз вперёд (м) — вес на переднее колесо, чтобы перед не задирался.")]
        public float climbChest = 8f, climbHipForward = 0.05f;
        [Tooltip("На спуске: грудь ниже (°) и локти согнуты сильнее (доля длины руки) — вес на ногах, руки «лёгкие», " +
                 "таз назад (его уводит физика RiderBody).")]
        public float descentChest = 10f, descentElbows = 0.1f;

        [Header("Голова и шея")]
        [Tooltip("Насколько голова поворачивается относительно рамы, °.")]
        public float headMin = -40f, headMax = 35f;
        [Tooltip("Пружина шеи: частота, рад/с, и гашение.")]
        public float neckFrequency = 9f, neckDamping = 0.75f;

        [Header("Суставы")]
        [Tooltip("Ноги и руки плавно гнутся в коленях и локтях (цельные картинки ноги и руки на сетке, SkinnedLimb), " +
                 "ягодица и дельта частично идут за корпусом. Выключите — будут отдельные части, как раньше.")]
        public bool bendJoints = true;

        [Header("Свет")]
        [Tooltip("Откуда светит солнце (в мире): тёплый свет на обращённой к нему стороне тела, холодная тень на другой.")]
        public Vector2 sunDirection = new Vector2(-0.45f, 0.89f);
        [Range(0f, 1.5f)] public float sunStrength = 1f;

        [Tooltip("Датчик падения (голова) — двигается вместе с нарисованной головой.")]
        public Transform headTrigger;

        [Header("Сортировка")]
        [Tooltip("С этого Order in Layer начинаются слои байка и гонщика (всего ~35 слоёв).")]
        public int startOrder = 10;

        // массы частей тела (доли, Демпстер) и где центр масс части (доля длины от проксимального сустава)
        private const float MTrunk = 0.497f, MHead = 0.081f, MUArm = 0.028f, MFArm = 0.022f, MThigh = 0.100f, MShank = 0.0465f, MFoot = 0.0145f;

        private class Limb { public Transform upper, lower, foot; }
        private class Lit { public Transform node; public SpriteRenderer up, down; public float k; }
        private readonly System.Collections.Generic.List<Lit> _lit = new System.Collections.Generic.List<Lit>();
        private struct Skel
        {
            public Vector2 hip, sh, shN, shF, head, kneeN, kneeF, ankleN, ankleF, elbowN, elbowF;
            public float theta, arm;
        }

        private Transform _swingarm, _forkUpper, _forkLower, _crankNear, _crankFar;
        private Transform _torso, _head, _pelvis;
        private Transform _upperArmNear, _forearmNear, _upperArmFar, _forearmFar;
        private Limb _legNear, _legFar;

        private Vector2 _restRearAxle;
        private int _order;
        private SpriteRenderer _crack;
        private Transform _frameNode, _frameRear, _frameFront;   // рама целиком и её половины (видны, когда рама разломалась)
        private Vector2 _visN, _visB, _visF;          // поправка центра масс стоек (нарисованная − физическая)
        private Vector2 _hip;                         // таз с прошлого кадра (для решателя)
        private bool _hasHip;
        private float _headAngle, _headVel;
        private bool _hasHead;
        private RiderRagdoll _ragdoll;
        private float _pump, _pullN, _pullF, _climb, _descent, _sit;          // раскачка телом при педалировании 0…1; плечи к рулю (м): ближнее, дальнее
        /// <summary>Кукла гонщика после падения (null, пока он на байке).</summary>
        public RiderRagdoll Ragdoll => _ragdoll;
        /// <summary>Центр тела гонщика в мире (середина корпуса) — за ним следит камера.</summary>
        public Vector2 RiderCenter
        {
            get
            {
                if (_ragdoll != null && _ragdoll.Torso != null) return _ragdoll.Torso.position + (Vector2)_ragdoll.Torso.transform.right * (torsoLength * 0.5f);
                if (_torso != null) return (Vector2)_torso.position + (Vector2)_torso.right * (torsoLength * 0.5f);
                return bike.frame.position;
            }
        }
        private SkinnedLimb _legSkinN, _legSkinF, _armSkinN, _armSkinF;
        private readonly System.Collections.Generic.List<SkinnedLimb> _skins = new System.Collections.Generic.List<SkinnedLimb>();

        private void Awake()
        {
            if (skin == null)
            {
                Debug.LogWarning("[BikeVisual] Не назначен BikeSkin — байк не будет нарисован.", this);
                enabled = false;
                return;
            }

            if (skin.wheel == null || skin.frame.baseLayer == null)
                Debug.LogError("[BikeVisual] В BikeSkin нет спрайтов — байк будет невидимым. Выделите папку Assets/_Project/Art → ПКМ → Reimport, " +
                               "затем Bike → Создать сцену Free Ride.", this);

            Transform f = bike.frame.transform;
            _restRearAxle = f.InverseTransformPoint(bike.rearWheel.transform.position);
            _order = startOrder;
            float k = skin.farSideShade;
            Color far = new Color(k, k, k, 1f);

            // Порядок создания = порядок отрисовки (от дальнего к ближнему).
            bool bend = bendJoints && HasLayers(skin.leg) && HasLayers(skin.arm);
            // --- дальняя сторона гонщика ---
            if (bend)
            {
                // кости (без картинок) и цельные нога/рука на сетке; корпус как кость нужен сразу — картинки ему добавим позже
                _torso = MakeNode(f, "Torso");
                _legFar = new Limb { upper = MakeNode(f, "ThighFar"), lower = MakeNode(f, "ShinFar") };
                _legSkinF = MakeSkin(f, "LegFar", skin.leg, _legFar.upper, _legFar.lower, thigh, -1f, far);
                _legSkinF.AddSegment(-0.2f, 9f, 0f, 0f, ref _order);
                _legFar.foot = MakePart(f, "ShoeFar", skin.shoe, far, far);
                _upperArmFar = MakeNode(f, "UpperArmFar"); _forearmFar = MakeNode(f, "ForearmFar");
                _armSkinF = MakeSkin(f, "ArmFar", skin.arm, _upperArmFar, _forearmFar, upperArm, +1f, far);
                _armSkinF.AddSegment(-0.2f, 9f, -0.1f, -0.03f, ref _order);    // у плеча — плавно в корпус
            }
            else
            {
                _legFar = MakeLeg(f, "Far", far);
                _upperArmFar = MakePart(f, "UpperArmFar", skin.upperArm, far, Mul(skin.jerseyColor, k));
                _forearmFar = MakePart(f, "ForearmFar", skin.forearm, far, Mul(skin.jerseyColor, k));
            }
            _crankFar = MakeNode(f, "CrankFar");
            AddSprite(_crankFar, skin.crankFar, Color.white);

            // --- байк ---
            AddSprite(bike.rearWheel.transform, skin.wheel, Color.white);
            AddSprite(bike.frontWheel.transform, skin.wheel, Color.white);
            _swingarm = MakePart(f, "Swingarm", skin.swingarm, Color.white, skin.frameColor);
            _swingarm.localPosition = swingarmPivot;
            _forkLower = MakeNode(f, "ForkLower");
            AddSprite(_forkLower, skin.forkLower, Color.white);
            _frameNode = MakePart(f, "FrameSprites", skin.frame, Color.white, skin.frameColor);
            if (HasLayers(skin.frameRear) && HasLayers(skin.frameFront))
            {
                // половины разломанной рамы — на месте рамы, пока скрыты
                _frameRear = MakePart(f, "FrameRear", skin.frameRear, Color.white, skin.frameColor);
                _frameFront = MakePart(f, "FrameFront", skin.frameFront, Color.white, skin.frameColor);
                _frameRear.gameObject.SetActive(false);
                _frameFront.gameObject.SetActive(false);
            }
            else
            {
                _crack = AddSprite(_frameNode, skin.frameCrack, Color.white);   // нет картинок разлома — трещины
                if (_crack != null) _crack.enabled = false;
            }
            _forkUpper = MakeNode(f, "ForkUpper");
            _forkUpper.localPosition = headTubeBottom;
            AddSprite(_forkUpper, skin.forkUpper, Color.white);
            _crankNear = MakeNode(f, "CrankNear");
            AddSprite(_crankNear, skin.crankNear, Color.white);
            _crankNear.localPosition = _crankFar.localPosition = bottomBracket;

            // --- ближняя сторона гонщика: бедро под корпусом (джерси закрывает пояс штанов), голень и стопа — поверх ---
            if (bend)
            {
                _legNear = new Limb { upper = MakeNode(f, "ThighNear"), lower = MakeNode(f, "ShinNear") };
                _legSkinN = MakeSkin(f, "LegNear", skin.leg, _legNear.upper, _legNear.lower, thigh, -1f, Color.white);
                _legSkinN.AddSegment(-0.2f, thigh + 0.1f, 0f, 0f, ref _order);                       // бедро — под корпусом
                AddPelvisAndTorso();
                _legSkinN.AddSegment(thigh - 0.1f, 9f, thigh - 0.1f, thigh - 0.04f, ref _order);   // голень — поверх, без шва
                _legNear.foot = MakePart(f, "ShoeNear", skin.shoe, Color.white, Color.white);
                _upperArmNear = MakeNode(f, "UpperArmNear"); _forearmNear = MakeNode(f, "ForearmNear");
                _armSkinN = MakeSkin(f, "ArmNear", skin.arm, _upperArmNear, _forearmNear, upperArm, +1f, Color.white);
                _armSkinN.AddSegment(-0.2f, 9f, -0.1f, -0.03f, ref _order);
            }
            else
            {
                _legNear = new Limb { upper = MakePart(f, "ThighNear", skin.thigh, Color.white, skin.shortsColor) };
                _torso = MakeNode(f, "Torso");
                AddPelvisAndTorso();
                _legNear.lower = MakePart(f, "ShinNear", skin.shin, Color.white, skin.shortsColor);
                _legNear.foot = MakePart(f, "ShoeNear", skin.shoe, Color.white, Color.white);
                _upperArmNear = MakePart(f, "UpperArmNear", skin.upperArm, Color.white, skin.jerseyColor);
                _forearmNear = MakePart(f, "ForearmNear", skin.forearm, Color.white, skin.jerseyColor);
            }
            _head = MakePart(f, "Head", skin.head, Color.white, skin.helmetColor);

            ComputeStanceOffsets();
            ComputeRestAngles();
            // цвета, выбранные в магазине (если выбирали)
            ApplyColors(PlayerLook.Get(PlayerLook.JerseyKey, skin.jerseyColor), PlayerLook.Get(PlayerLook.PantsKey, skin.shortsColor),
                        PlayerLook.Get(PlayerLook.HelmetKey, skin.helmetColor));
            LateUpdate();
        }

        /// <summary>Перекрасить экипировку: джерси, штаны, шлем (дальние рука и нога — темнее, как в BikeSkin).</summary>
        public void ApplyColors(Color jersey, Color pants, Color helmet)
        {
            Paint(_torso, jersey); Paint(_upperArmNear, jersey); Paint(_forearmNear, jersey); Paint(_upperArmFar, jersey); Paint(_forearmFar, jersey);
            Paint(_legNear.upper, pants); Paint(_legNear.lower, pants); Paint(_legFar.upper, pants); Paint(_legFar.lower, pants);
            Paint(_head, helmet);
            Paint(_pelvis, pants);
            float k = skin != null ? skin.farSideShade : 0.58f;
            if (_legSkinN != null) _legSkinN.paint = pants;
            if (_legSkinF != null) _legSkinF.paint = Mul(pants, k);
            if (_armSkinN != null) _armSkinN.paint = jersey;
            if (_armSkinF != null) _armSkinF.paint = Mul(jersey, k);
        }

        private static void Paint(Transform node, Color c)
        {
            if (node == null) return;
            // только свои слои (прямые дети узла): у корпуса есть ребёнок-таз со своим цветом (штаны)
            float shade = 1f;
            foreach (Transform ch in node)
            {
                var sr = ch.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null && sr.sprite.name.EndsWith("_base")) { shade = sr.color.r; break; }
            }
            foreach (Transform ch in node)
            {
                var sr = ch.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null && sr.sprite.name.EndsWith("_paint")) sr.color = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
            }
        }

        private void Start()
        {
            if (bike != null) { bike.Crashed += OnCrashed; bike.Broken += OnBroken; }
        }

        /// <summary>
        /// Рама разломалась: вместо целой рамы — две половины; передняя (рулевой стакан с обломками труб) и вилка
        /// переходят к отдельному телу передней части (BikeController2D.FrontPiece) и улетают вместе с передним колесом.
        /// </summary>
        private void OnBroken()
        {
            Rigidbody2D front = bike.FrontPiece;
            if (_frameRear != null && front != null)
            {
                _frameNode.gameObject.SetActive(false);
                _frameRear.gameObject.SetActive(true);
                _frameFront.gameObject.SetActive(true);
                Transform ff = front.transform;                                // в момент разлома — в той же позе, что рама
                _frameFront.SetParent(ff, true);
                _forkUpper.SetParent(ff, true);
                _forkLower.SetParent(ff, true);
            }
            else if (_crack != null) _crack.enabled = true;
        }

        private void OnDestroy()
        {
            if (bike != null) { bike.Crashed -= OnCrashed; bike.Broken -= OnBroken; }
            foreach (SkinnedLimb sl in _skins) sl.Dispose();
            _skins.Clear();
        }

        /// <summary>Падение: гонщик отцепляется от байка и падает куклой.</summary>
        private void OnCrashed()
        {
            if (_ragdoll != null || rider == null) return;
            _ragdoll = RiderRagdoll.Create(this, bike, rider,
                _torso, _head, _legNear.upper, _legNear.lower, _legFar.upper, _legFar.lower,
                _upperArmNear, _forearmNear, _upperArmFar, _forearmFar, _legNear.foot, _legFar.foot);
            // рама разломалась: гонщика бросает через руль — вперёд и вверх, кувырком через голову, и он врезается в землю
            if (bike.IsBroken)
                _ragdoll.Fling((Vector2)bike.frame.transform.right * brokenFling.x + Vector2.up * brokenFling.y, brokenSpin);
        }

        // ------------------------------------------------------------------ создание

        private static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static Transform MakeNode(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private Limb MakeLeg(Transform parent, string side, Color shade)
        {
            return new Limb
            {
                upper = MakePart(parent, "Thigh" + side, skin.thigh, shade, Mul(skin.shortsColor, shade.r)),
                lower = MakePart(parent, "Shin" + side, skin.shin, shade, Mul(skin.shortsColor, shade.r)),
                foot = MakePart(parent, "Shoe" + side, skin.shoe, shade, shade),
            };
        }

        /// <summary>Узел со слоями: основа (tint), краска (paint), детали (tint) и свет солнца (две стороны).</summary>
        private Transform MakePart(Transform parent, string name, BikeSkin.Layered layers, Color tint, Color paint)
        {
            var node = MakeNode(parent, name);
            AddLayers(node, layers, tint, paint);
            return node;
        }

        /// <summary>
        /// Таз в штанах (ягодицы, низ живота) — ребёнок корпуса, рисуется под джерси и поверх ближнего бедра; туда, где выходит
        /// бедро, картинка таза плавно прозрачна — бедро продолжается из него без шва. Затем — картинки корпуса.
        /// </summary>
        private void AddPelvisAndTorso()
        {
            if (HasLayers(skin.pelvis)) _pelvis = MakePart(_torso, "Pelvis", skin.pelvis, Color.white, skin.shortsColor);
            else _order += 5;
            AddLayers(_torso, skin.torso, Color.white, skin.jerseyColor);
        }

        private static bool HasLayers(BikeSkin.Layered l) => l != null && l.paintLayer != null && l.paintLayer.rect.width > 8f;

        /// <summary>Нога/рука, которая гнётся в суставах: корень — корпус, кости — b1, b2 (см. SkinnedLimb).</summary>
        private SkinnedLimb MakeSkin(Transform parent, string name, BikeSkin.Layered layers, Transform b1, Transform b2,
                                     float len1, float innerSign, Color tint)
        {
            var sl = new SkinnedLimb(parent, name, layers, material, _torso, b1, b2, len1, innerSign) { tint = tint, lightK = tint.r };
            _skins.Add(sl);
            return sl;
        }

        /// <summary>Угол бедра и плеча к корпусу в нейтральной стойке — в ней у таза и плеча нет изгиба.</summary>
        private void ComputeRestAngles()
        {
            if (_skins.Count == 0) return;
            Vector2 pn = bottomBracket + new Vector2(crankLength, 0f), pf = bottomBracket - new Vector2(crankLength, 0f);
            Pref(0f, out float th, out float e);
            Skel s = Skeleton(hipVisNeutral, th, e, pn, pf, footCoast, footCoast);
            float leg = Angle(s.kneeN - s.hip) - s.theta, arm = Angle(s.elbowN - s.sh) - s.theta;
            if (_legSkinN != null) _legSkinN.rest = leg;
            if (_legSkinF != null) _legSkinF.rest = leg;
            if (_armSkinN != null) _armSkinN.rest = arm;
            if (_armSkinF != null) _armSkinF.rest = arm;
        }

        private void UpdateSkins()
        {
            if (_skins.Count == 0) return;
            Vector2 sun = sunDirection.sqrMagnitude > 1e-6f ? sunDirection.normalized : Vector2.up;
            foreach (SkinnedLimb sl in _skins) sl.Update(sun, sunStrength);
        }

        /// <summary>Слои картинки (основа, краска, детали, свет солнца) на уже созданный узел.</summary>
        private void AddLayers(Transform node, BikeSkin.Layered layers, Color tint, Color paint)
        {
            AddSprite(node, layers.baseLayer, tint);
            AddSprite(node, layers.paintLayer, paint);
            AddSprite(node, layers.topLayer, tint);
            if (layers.lightUp != null || layers.lightDown != null)
                _lit.Add(new Lit { node = node, up = AddSprite(node, layers.lightUp, Color.clear), down = AddSprite(node, layers.lightDown, Color.clear), k = tint.r });
        }

        private SpriteRenderer AddSprite(Transform parent, Sprite sprite, Color color)
        {
            int order = _order++;
            if (sprite == null) return null;
            var go = new GameObject(sprite.name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>
        /// Свет солнца на теле: у каждой части два слоя — освещена сторона +Y или −Y спрайта. Смешиваем по тому, как часть
        /// повёрнута к солнцу в мире, — рука, нога, корпус, шлем вращаются, а свет остаётся с одной стороны (как у 3D-модели).
        /// </summary>
        private void UpdateLight()
        {
            Vector2 sun = sunDirection.sqrMagnitude > 1e-6f ? sunDirection.normalized : Vector2.up;
            foreach (Lit l in _lit)
            {
                if (l.node == null) continue;
                float w = Vector2.Dot(l.node.up, sun) * sunStrength * l.k;
                if (l.up != null) l.up.color = new Color(1f, 1f, 1f, Mathf.Clamp01(w));
                if (l.down != null) l.down.color = new Color(1f, 1f, 1f, Mathf.Clamp01(-w));
            }
        }

        // ------------------------------------------------------------------ каждый кадр

        private void LateUpdate()
        {
            Transform f = bike.frame.transform;
            Transform ff = bike.FrontPiece != null && _forkUpper.parent != f ? bike.FrontPiece.transform : f;   // вилка — на передней части рамы
            Vector2 rearAxle = f.InverseTransformPoint(bike.rearWheel.transform.position);
            Vector2 frontAxle = ff.InverseTransformPoint(bike.frontWheel.transform.position);

            // маятник поворачивается вокруг оси так, чтобы дропаут оставался на оси колеса
            float rest = Angle(_restRearAxle - swingarmPivot);
            float now = Angle(rearAxle - swingarmPivot);
            _swingarm.localRotation = Quaternion.Euler(0f, 0f, now - rest);

            // вилка: верх смотрит от короны на ось, низ — от оси на корону
            Vector2 forkDir = frontAxle - headTubeBottom;
            _forkUpper.localRotation = Quaternion.Euler(0f, 0f, Angle(forkDir) + 90f);
            _forkLower.localPosition = frontAxle;
            _forkLower.localRotation = Quaternion.Euler(0f, 0f, Angle(-forkDir) - 90f);

            // шатуны: угол считает физика (ритм педалей через передачу, выравнивание на накате)
            float crankAngle = bike.CrankAngle;
            _crankNear.localRotation = Quaternion.Euler(0f, 0f, crankAngle);
            _crankFar.localRotation = Quaternion.Euler(0f, 0f, crankAngle + 180f);
            if (_ragdoll != null) { UpdateLight(); UpdateSkins(); return; }   // гонщик упал — его двигает кукла
            float ca = crankAngle * Mathf.Deg2Rad;
            Vector2 crank = new Vector2(Mathf.Cos(ca), Mathf.Sin(ca)) * crankLength;
            Vector2 pedalNear = bottomBracket + crank;
            Vector2 pedalFar = bottomBracket - crank;

            PoseRider(pedalNear, pedalFar, crankAngle, Time.deltaTime);
            UpdateLight();
            UpdateSkins();
        }

        /// <summary>Стойка и положение тела гонщика (повторяет Tools/art/rider_pose_new.py).</summary>
        private void PoseRider(Vector2 pedalN, Vector2 pedalF, float crankAngle, float dt)
        {
            float lean = 0f, tilt = 0f, seat = 0f, compress = 0f;
            Vector2 C = new Vector2(-0.106f, 0.757f);
            if (rider != null)
            {
                C = rider.RenderCom(out compress);
                lean = rider.PoseLean; tilt = rider.BodyTilt; seat = rider.Seat;
            }
            tilt -= bike.AirTwist;                                         // в воздухе гонщик повернул байк относительно тела
            // центр масс нарисованной стойки (поправка вращается вместе с телом, которое держится по гравитации)
            C += (Vector2)(Quaternion.Euler(0f, 0f, tilt * (1f - seat)) * (Vector3)StanceOffset(lean));

            // педалирование всем телом: ритм — от шатунов. Рабочий ход ближней ноги — педаль впереди (угол 0°), дальней — 180°:
            // stroke = 1 на каждом рабочем ходе (дважды за оборот), side = +1 — давит ближняя нога, −1 — дальняя
            bool pedalingNow = input != null && input.Pedal > 0f && !bike.IsCrashed && bike.IsOnGround;
            // посадка: по ровному и в подъём — сидя (как в видео-образце), на спуске, с переносом веса, в прыжке и в полёте — стоя
            float slopeNow = rider != null ? rider.SlopeAngle : 0f;
            bool sitWanted = sitWhenRiding && !bike.IsCrashed && bike.IsGrounded && slopeNow > -sitMaxDescent
                             && Mathf.Abs(lean) < 0.35f && compress < 0.2f && bike.HopCrouch < 0.05f;
            _sit = Mathf.MoveTowards(_sit, sitWanted ? 1f : 0f, dt / Mathf.Max(0.05f, sitTime));
            float sitK = SmoothStep(_sit);
            float seated = Mathf.Max(seat, sitK);
            float spin = 1f - SmoothStep((bike.Cadence - pumpCadenceFull) / Mathf.Max(1f, pumpCadenceStill - pumpCadenceFull));   // 90+ об/мин — только ноги
            float pumpTarget = pedalingNow
                ? Mathf.Clamp01(input.Pedal) * Mathf.Lerp(1f, pumpAtSpeed, Mathf.Clamp01(bike.Speed / Mathf.Max(1f, pumpFadeSpeed))) * (1f - 0.9f * seated) * spin   // сидя верх тела неподвижен
                : 0f;
            _pump += (pumpTarget - _pump) * Mathf.Min(1f, dt / Mathf.Max(0.02f, pumpResponse));
            float ca = crankAngle * Mathf.Deg2Rad;
            float stroke = 0.5f + 0.5f * Mathf.Cos(2f * ca), side = Mathf.Cos(ca);
            C += new Vector2(pumpHipForward * (2f * stroke - 1f), -pumpHipDrop * stroke) * _pump;   // вес падает на педаль
            // рельеф: в подъём — таз вперёд и грудь к рулю, на спуске — грудь ниже и локти согнуты (уклон — по земле, в полёте — плавно к нулю)
            float slope = rider != null && bike.IsOnGround ? rider.SlopeAngle : 0f;
            float k = Mathf.Min(1f, dt / 0.3f);
            _climb += (SmoothStep((slope - terrainFrom) / Mathf.Max(0.1f, terrainFull - terrainFrom)) - _climb) * k;
            _descent += (SmoothStep((-slope - terrainFrom) / Mathf.Max(0.1f, terrainFull - terrainFrom)) - _descent) * k;
            C += new Vector2(climbHipForward * _climb, 0f);
            _pullN = pumpShoulder * _pump * side;                         // рука давящей стороны тянет руль
            _pullF = -pumpShoulder * _pump * side;

            float framePitch = Mathf.DeltaAngle(0f, bike.frame.rotation);
            Pref(lean, out float th0, out float e0);
            th0 = Mathf.Lerp(th0, torsoSeated, sitK);                      // сидя — корпус почти вертикален
            th0 += tilt * 0.8f;                                            // тело частично держится по гравитации (как в физике)
            th0 -= pumpTorso * stroke * _pump;                             // корпус почти неподвижен: работают ноги и руки
            th0 -= climbChest * _climb + descentChest * _descent;         // подъём и спуск — грудь ниже
            e0 -= descentElbows * _descent;                                // на спуске локти согнуты — руки работают как подвеска
            th0 -= compressTorso * compress;                               // присед: грудь к рулю
            e0 -= compressArms * compress;
            float fade = 1f - SmoothStep((Mathf.Abs(framePitch) - 60f) / 50f);   // в сальто — вместе с байком
            th0 += (Mathf.Max(0f, torsoWorldMin - framePitch - th0) - Mathf.Max(0f, th0 + framePitch - torsoWorldMax)) * fade;

            bool pedaling = input != null && input.Pedal > 0f && !bike.IsCrashed;
            float footN = FootAngle(crankAngle, pedaling, compress), footF = FootAngle(crankAngle + 180f, pedaling, compress);

            // таз: чтобы центр масс нарисованного тела совпал с физическим (но в обычной стойке не ниже Hip Floor)
            float floor = hipFloor - hipFloorSquat * compress;
            Vector2 hip = _hasHip ? _hip : new Vector2(C.x - 0.22f, C.y - 0.02f);
            for (int i = 0; i < 10; i++)
            {
                hip = ClampHip(hip, pedalN, pedalF, footN, footF, floor);
                Skel sk = Skeleton(hip, th0, e0, pedalN, pedalF, footN, footF);
                hip += (C - ComOf(sk, pedalN, pedalF)) / 0.72f;
            }
            hip = ClampHip(hip, pedalN, pedalF, footN, footF, floor);
            if (seated > 0f) hip = Vector2.Lerp(hip, saddle, seated);     // сидя (и вилли сидя) — таз на седле
            _hip = hip; _hasHip = true;
            Skel s = Skeleton(hip, th0, e0, pedalN, pedalF, footN, footF);

            // голова: взгляд по ходу движения (почти горизонт), пружина шеи, пределы поворота
            Vector2 v = bike.Velocity;
            float look = Mathf.Atan2(v.y, Mathf.Max(1f, Mathf.Abs(v.x))) * Mathf.Rad2Deg * 0.4f;
            float want = Mathf.Clamp(Mathf.DeltaAngle(0f, look - framePitch), headMin, headMax) - pumpHead * stroke * _pump;   // кивок в такт
            if (!_hasHead) { _headAngle = want; _headVel = 0f; _hasHead = true; }
            float w = neckFrequency;
            for (int i = 0; i < 4; i++)
            {
                float h = dt / 4f;
                _headVel += (w * w * (want - _headAngle) - 2f * neckDamping * w * _headVel) * h;
                _headAngle += _headVel * h;
            }
            _headAngle = Mathf.Clamp(_headAngle, headMin - 10f, headMax + 10f);

            Place(_torso, s.hip, s.sh);
            _head.localPosition = s.head;
            _head.localRotation = Quaternion.Euler(0f, 0f, _headAngle);
            if (headTrigger != null) headTrigger.localPosition = s.head;
            PlaceLeg(_legNear, s.hip, s.kneeN, s.ankleN, pedalN, footN);
            PlaceLeg(_legFar, s.hip, s.kneeF, s.ankleF, pedalF, footF);
            Place(_upperArmNear, s.shN, s.elbowN); Place(_forearmNear, s.elbowN, grip);
            Place(_upperArmFar, s.shF, s.elbowF); Place(_forearmFar, s.elbowF, grip);
        }

        private void Pref(float lean, out float theta, out float arm)
        {
            if (lean < 0f) { theta = Mathf.Lerp(torsoNeutral, torsoBack, -lean); arm = Mathf.Lerp(armNeutral, armBack, -lean); }
            else { theta = Mathf.Lerp(torsoNeutral, torsoForward, lean); arm = Mathf.Lerp(armNeutral, armForward, lean); }
        }

        private float FootAngle(float crank, bool pedaling, float compress) =>
            pedaling ? footMean + footSwing * Mathf.Cos((crank - footPhase) * Mathf.Deg2Rad) : footCoast + 12f * compress;

        private Vector2 StanceOffset(float lean) =>
            lean < 0f ? Vector2.Lerp(_visN, _visB, -lean) : Vector2.Lerp(_visN, _visF, lean);

        /// <summary>Разница центра масс нарисованных стоек и физических (RiderBody) — считается один раз.</summary>
        private void ComputeStanceOffsets()
        {
            Vector2 pn = bottomBracket + new Vector2(crankLength, 0f), pf = bottomBracket - new Vector2(crankLength, 0f);
            Vector2 physN = new Vector2(-0.106f, 0.757f), physB = new Vector2(-0.22f, 0.599f), physF = new Vector2(0.112f, 0.644f);
            if (rider != null)
            {
                physN = rider.hipNeutral + rider.comFromHipNeutral;
                physB = rider.hipBack + rider.comFromHipBack;
                physF = rider.hipForward + rider.comFromHipForward;
            }
            _visN = StanceCom(hipVisNeutral, 0f, pn, pf) - physN;
            _visB = StanceCom(hipVisBack, -1f, pn, pf) - physB;
            _visF = StanceCom(hipVisForward, 1f, pn, pf) - physF;
        }

        private Vector2 StanceCom(Vector2 hip, float lean, Vector2 pn, Vector2 pf)
        {
            Pref(lean, out float th, out float e);
            return ComOf(Skeleton(hip, th, e, pn, pf, footCoast, footCoast), pn, pf);
        }

        /// <summary>Плечо: корпус как можно ближе к предпочтительному углу, руки в пределах [armMin, armMax] длины.</summary>
        private Vector2 Shoulder(Vector2 hip, float thetaPref, float armPref, out float armUsed)
        {
            float armLen = upperArm + forearm;
            float d = Vector2.Distance(grip, hip);
            float bestCost = float.MaxValue; Vector2 best = Vector2.zero; armUsed = 1f;
            for (int i = 0; i < 25; i++)
            {
                float e = armMin + (armMax - armMin) * i / 24f;
                if (d > torsoLength + e * armLen || d < Mathf.Abs(torsoLength - e * armLen)) continue;
                Vector2 sh = CircleMeet(hip, torsoLength, grip, e * armLen);
                float th = Angle(sh - hip);
                if (th < torsoMin || th > torsoMax) continue;
                float cost = (th - thetaPref) * (th - thetaPref) + 400f * (e - armPref) * (e - armPref);
                if (cost < bestCost) { bestCost = cost; best = sh; armUsed = e; }
            }
            if (bestCost < float.MaxValue) return best;
            // руль далеко или близко: корпус под ближайшим допустимым углом
            float a = Mathf.Clamp(Angle(grip - hip), torsoMin, torsoMax) * Mathf.Deg2Rad;
            Vector2 s = hip + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * torsoLength;
            armUsed = Mathf.Clamp(Vector2.Distance(grip, s) / armLen, armMin, 1f);
            return s;
        }

        private Skel Skeleton(Vector2 hip, float thetaPref, float armPref, Vector2 pedalN, Vector2 pedalF, float footN, float footF)
        {
            var s = new Skel { hip = hip };
            s.sh = Shoulder(hip, thetaPref, armPref, out s.arm);
            s.theta = Angle(s.sh - hip);
            s.head = s.sh + Rotate(neck, (s.theta - 40f) * 0.6f);
            s.shN = PullShoulder(s.sh, _pullN);                             // плечи двигаются за руками: тянет руль — к рулю
            s.shF = PullShoulder(s.sh + farShoulderOffset, _pullF);
            s.ankleN = pedalN + Rotate(ankleOffset, footN);
            s.ankleF = pedalF + Rotate(ankleOffset, footF);
            s.kneeN = TwoBone(hip, s.ankleN, thigh, shin, +1f);           // колено вперёд
            s.kneeF = TwoBone(hip, s.ankleF, thigh, shin, +1f);
            s.elbowN = TwoBone(s.shN, grip, upperArm, forearm, -1f);       // локоть вниз-назад
            s.elbowF = TwoBone(s.shF, grip, upperArm, forearm, -1f);
            return s;
        }

        /// <summary>Плечо сдвинуто к рулю на pull (м; минус — от руля), но рука не вытягивается длиннее 98% своей длины.</summary>
        private Vector2 PullShoulder(Vector2 sh, float pull)
        {
            if (Mathf.Abs(pull) < 1e-5f) return sh;
            Vector2 d = grip - sh; float l = d.magnitude;
            if (l < 1e-4f) return sh;
            float maxL = 0.98f * (upperArm + forearm);
            float newL = Mathf.Min(l - pull, Mathf.Max(l, maxL));         // от руля — только пока рука не прямая
            return grip - d / l * Mathf.Max(0.3f, newL);
        }

        private Vector2 ComOf(Skel s, Vector2 pedalN, Vector2 pedalF)
        {
            Vector2 c = MTrunk * Vector2.Lerp(s.hip, s.sh, 0.5f) + MHead * s.head
                      + MUArm * (Vector2.Lerp(s.shN, s.elbowN, 0.436f) + Vector2.Lerp(s.shF, s.elbowF, 0.436f))
                      + MFArm * (Vector2.Lerp(s.elbowN, grip, 0.682f) + Vector2.Lerp(s.elbowF, grip, 0.682f))
                      + MThigh * (Vector2.Lerp(s.hip, s.kneeN, 0.433f) + Vector2.Lerp(s.hip, s.kneeF, 0.433f))
                      + MShank * (Vector2.Lerp(s.kneeN, s.ankleN, 0.433f) + Vector2.Lerp(s.kneeF, s.ankleF, 0.433f))
                      + MFoot * (Vector2.Lerp(s.ankleN, pedalN, 0.6f) + Vector2.Lerp(s.ankleF, pedalF, 0.6f));
            return c / (MTrunk + MHead + 2f * (MUArm + MFArm + MThigh + MShank + MFoot));
        }

        private Vector2 ClampHip(Vector2 hip, Vector2 pedalN, Vector2 pedalF, float footN, float footF, float floor)
        {
            Vector2 d = hip - grip;
            if (d.magnitude > 0.92f) hip = grip + d.normalized * 0.92f;    // руки и корпус не растягиваются
            float legMax = maxLegExtension * (thigh + shin);
            hip = KeepWithin(hip, pedalN + Rotate(ankleOffset, footN), legMax);
            hip = KeepWithin(hip, pedalF + Rotate(ankleOffset, footF), legMax);
            hip.y = Mathf.Max(hip.y, floor);
            return hip;
        }

        private static Vector2 KeepWithin(Vector2 p, Vector2 c, float r)
        {
            Vector2 d = p - c; float l = d.magnitude;
            return l > r ? c + d * (r / l) : p;
        }

        private void PlaceLeg(Limb leg, Vector2 hip, Vector2 knee, Vector2 ankle, Vector2 pedal, float foot)
        {
            Place(leg.upper, hip, knee);
            Place(leg.lower, knee, ankle);
            leg.foot.localPosition = pedal;
            leg.foot.localRotation = Quaternion.Euler(0f, 0f, foot);       // стопа «работает» на педали
        }

        /// <summary>Верхняя точка пересечения двух окружностей (центр c1 радиус r1, центр c2 радиус r2).</summary>
        private static Vector2 CircleMeet(Vector2 c1, float r1, Vector2 c2, float r2)
        {
            Vector2 d = c2 - c1;
            float dist = d.magnitude;
            if (dist < 1e-4f) return c1 + Vector2.up * r1;
            Vector2 u = d / dist;
            if (dist >= r1 + r2) return c1 + u * r1;
            float a = (r1 * r1 - r2 * r2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, r1 * r1 - a * a));
            return c1 + u * a + new Vector2(-u.y, u.x) * h;                // решение сверху (плечи над линией таз–руль)
        }

        /// <summary>Ставит сегмент в точку from и поворачивает его на точку to (спрайт идёт вдоль +X).</summary>
        private static void Place(Transform t, Vector2 from, Vector2 to)
        {
            t.localPosition = from;
            t.localRotation = Quaternion.Euler(0f, 0f, Angle(to - from));
        }

        // ------------------------------------------------------------------ помощники

        private static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        private static float SmoothStep(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Двухзвенная IK: положение сустава (колена/локтя). bendSign: +1 — изгиб влево от направления, -1 — вправо.</summary>
        private static Vector2 TwoBone(Vector2 root, Vector2 target, float l1, float l2, float bendSign)
        {
            Vector2 d = target - root;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
            float cos = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
            float a = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * bendSign;
            Vector2 dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.right;
            Vector2 rotated = new Vector2(dir.x * Mathf.Cos(a) - dir.y * Mathf.Sin(a), dir.x * Mathf.Sin(a) + dir.y * Mathf.Cos(a));
            return root + rotated * l1;
        }
    }
}
