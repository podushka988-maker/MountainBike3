using System.Collections.Generic;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Бесконечная трасса для свободного заезда. Строится кусками (чанками) впереди байка
    /// и удаляется позади, поэтому длина трассы не ограничена.
    ///
    /// Рельеф = естественные холмы (шум Перлина трёх масштабов; где-то луга, где-то крутые горы)
    /// + фигуры, как на настоящей трассе: столы, даблы, гэпы с разгоном, дропы и лестницы дропов, ступени вверх,
    /// волны (памп) и каменистые участки. Чем дальше — тем фигуры больше и чаще.
    /// Внутри фигуры холмы плавно «замирают» — фигура стоит на ровном основании и стыкуется без изломов;
    /// после дропа или стола трасса продолжается ниже. Фигуры проверены расчётом (Tools/terrain/ride_test.py):
    /// проходятся и медленно (из ям можно выкатиться), и на полной скорости (длинные приземления).
    /// Высота считается функцией HeightAt(x), поэтому куски стыкуются без швов,
    /// а один и тот же seed всегда даёт одну и ту же трассу. Прототип — Tools/terrain/track.py.
    /// Висит на объекте Terrain.
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Кого отслеживать")]
        public Transform target;

        [Header("Сид")]
        [Tooltip("Новая трасса при каждом заезде.")]
        public bool randomSeed = true;
        public int seed = 12345;

        [Header("Чанки")]
        public float chunkWidth = 20f;
        [Tooltip("Шаг между точками рельефа, м. Меньше — глаже, но тяжелее.")]
        public float step = 0.5f;
        [Tooltip("Насколько строить вперёд от байка, м.")]
        public float buildAhead = 70f;
        [Tooltip("Насколько держать позади байка, м.")]
        public float keepBehind = 30f;
        [Tooltip("Толщина земли под поверхностью (чтобы не было видно низа).")]
        public float groundDepth = 80f;   // глубже, чем видно даже при сильном отдалении камеры (меню — до 20 м половина кадра)

        [Header("Рельеф")]
        [Tooltip("Ровный участок на старте, м.")]
        public float flatStart = 15f;
        [Tooltip("За сколько метров рельеф плавно 'включается' после старта.")]
        public float rampIn = 40f;
        [Tooltip("Дистанция, на которой сложность достигает максимума, м.")]
        public float maxDifficultyDistance = 2500f;
        [Tooltip("Общий уклон холмов (положительный = в среднем под горку). Дропы и столы и так уводят трассу вниз (~1%).")]
        public float downhill = 0f;
        public float bigHillsAmplitude = 14f;
        public float bigHillsFrequency = 0.008f;
        public float hillsAmplitude = 2.8f;
        public float hillsFrequency = 0.035f;
        public float bumpsAmplitude = 0.18f;
        public float bumpsFrequency = 0.18f;
        [Tooltip("Разнообразие: множитель высоты холмов меняется вдоль трассы (ровные луга ↔ крутые горы), от и до.")]
        public Vector2 hillsVariety = new Vector2(0.4f, 1.25f);
        [Tooltip("Как часто меняется характер местности (меньше — длиннее участки).")]
        public float hillsVarietyFrequency = 0.0025f;

        [Header("Фигуры: трамплины, дропы, волны…")]
        public bool features = true;
        [Tooltip("Где начинается первая фигура, м.")]
        public float firstFeature = 70f;
        [Tooltip("Промежуток между фигурами в начале трассы, м: минимум и случайная добавка.")]
        public Vector2 featureGapStart = new Vector2(25f, 25f);
        [Tooltip("Промежуток между фигурами на максимальной сложности, м: минимум и случайная добавка.")]
        public Vector2 featureGapHard = new Vector2(14f, 12f);
        [Tooltip("На скольких метрах холмы плавно переходят в ровное основание фигуры (и обратно).")]
        public float featureLead = 8f;
        [Tooltip("Ширина обрыва дропа, м (почти вертикальная стенка).")]
        public float cliffWidth = 0.12f;
        [Tooltip("Как часто встречается каждая фигура (0 — никогда, 1 — как задумано).")]
        [Range(0f, 3f)] public float rollersWeight = 1f, tablesWeight = 1f, doublesWeight = 1f, dropsWeight = 1f,
                                     stairsWeight = 1f, gapsWeight = 1f, stepUpsWeight = 1f, rocksWeight = 1f, moundsWeight = 1f;
        [Tooltip("Небольшие трамплины, которые идеально прыгаются на 30 км/ч (5 видов: высокий, дальний, дроп-трамплин в уклон, " +
                 "степ-ап, дабл). Подобраны на физической модели: Tools/terrain/jumps30.py.")]
        [Range(0f, 3f)] public float smallJumpsWeight = 1f;
        /// <summary>Скорость, на которую рассчитаны небольшие трамплины: 30 км/ч.</summary>
        public const float SmallJumpSpeed = 30f / 3.6f;
        [Tooltip("Горка-трамплин (как в Mountain Bike Xtreme): угол спуска-приземления за вершиной, °.")]
        public float moundLandingAngle = 32f;

        [Header("Огромные фигуры (как на Red Bull Rampage)")]
        [Tooltip("Как часто встречаются огромные трамплины и дропы (0 — никогда).")]
        [Range(0f, 3f)] public float rampageWeight = 2.9f;
        [Tooltip("С какой сложности (0–1) они появляются: 0.05 ≈ с 130 м, 0.15 ≈ с 400 м.")]
        public float rampageFrom = 0.05f;
        [Tooltip("Локации по порядку (для главного меню): волны → стол → дроп → горка-трамплин → 5 небольших трамплинов → 7 огромных трамплинов → " +
                 "4 огромных дропа → 5 фигур с настоящего Rampage → и снова по кругу. С какой локации начать — по сиду трассы (случайно). Веса выше не используются.")]
        public bool sequentialFeatures = false;
        [Tooltip("Масштаб высоты огромных фигур (1 — как задумано).")]
        [Range(0.5f, 1.5f)] public float rampageScale = 1.5f;
        [Tooltip("Ширина каньона (1 — 5.3–7.5 м, 1.5 — 8–11 м).")]
        [Range(0.5f, 1.5f)] public float canyonWidth = 1.5f;
        [Tooltip("Дерево: материал и текстуры настила (доски) и опор.")]
        public Material woodMaterial;
        public Texture woodDeckTexture, woodPostTexture;
        public Color woodTint = new Color(0.85f, 0.78f, 0.72f);

        [Header("Камни на обрывах и каменистых участках")]
        public Sprite[] rocks;
        public Color rockTint = new Color(0.9f, 0.86f, 0.84f);

        [Header("Вид")]
        public Material material;
        public Color groundTop = new Color(0.36f, 0.27f, 0.2f);
        public Color groundBottom = new Color(0.16f, 0.12f, 0.1f);
        public Color grassColor = new Color(0.33f, 0.55f, 0.24f);
        public float grassWidth = 0.28f;
        public Color outlineColor = new Color(0.1f, 0.14f, 0.08f);
        public float outlineWidth = 0.07f;

        [Header("Текстуры земли")]
        [Tooltip("Материал с текстурой почвы (повторяется). Пусто — заливка цветом, как раньше.")]
        public Material groundMaterial;
        [Tooltip("Размер одного повтора текстуры почвы, м.")]
        public float groundTile = 3f;
        [Tooltip("Оттенок почвы под вечернее освещение (белый — как на текстуре).")]
        public Color groundTint = new Color(0.82f, 0.76f, 0.74f);
        [Tooltip("Тёмный слой перегноя с корнями под поверхностью.")]
        public Material topsoilMaterial;
        public float topsoilDepth = 1.1f;
        public float topsoilTile = 3f;

        [Tooltip("Текстуры для материалов выше (спрайтовый шейдер берёт текстуру с рендерера, а не из материала).")]
        public Texture groundTexture, topsoilTexture, grassBackTexture, grassFrontTexture;

        [Header("Трава")]
        [Tooltip("Густая трава за байком (полоса вдоль рельефа).")]
        public Material grassBackMaterial;
        public float grassBackHeight = 0.753f;
        [Tooltip("Насколько корни травы утоплены в землю, м.")]
        public float grassBackRoot = 0.07f;
        [Tooltip("Низкая трава перед колёсами.")]
        public Material grassFrontMaterial;
        public float grassFrontHeight = 0.3f;
        public float grassFrontRoot = 0.04f;
        public float grassTile = 3.01f;
        [Tooltip("Кусты травы с колосками и цветами.")]
        public Sprite[] tufts;
        [Tooltip("Насколько качается каждый куст (по номеру в Tufts; нет — 1): камни, сугробы, кучки листьев — 0.")]
        public float[] tuftSway;
        public Material tuftMaterial;
        [Tooltip("Кустов на 10 м трассы.")]
        public float tuftsPer10m = 4f;
        [Range(0f, 1f)] public float tuftsInFront = 0.2f;
        [Tooltip("Ветер: сила качания травы, м и скорость.")]
        public float windStrength = 0.05f;
        public float windSpeed = 1.6f;
        [Tooltip("Качание полос травы (0 — не качаются: снег в зимней локации). Кусты качаются всегда.")]
        public float grassSway = 1f;
        [Tooltip("Колёса, которые пригибают траву (необязательно).")]
        public Transform[] wheels;

        [Header("Физика")]
        public PhysicsMaterial2D groundPhysics;
        [Tooltip("Слой земли (должен входить в Ground Layers у BikeController2D).")]
        public int groundLayer = 0;

        private readonly Dictionary<int, GameObject> _chunks = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, ChunkGrass> _grass = new Dictionary<int, ChunkGrass>();

        /// <summary>Трава одного чанка: ленты с исходными вершинами и кусты (для ветра).</summary>
        private class ChunkGrass
        {
            public Mesh back, front;
            public Vector3[] backBase, frontBase, backWork, frontWork;
            public readonly List<Transform> tufts = new List<Transform>();
            public readonly List<float> tuftAngle = new List<float>();
            public readonly List<float> tuftHeight = new List<float>();
            public readonly List<float> tuftSway = new List<float>();
        }
        private float _ox1, _ox2, _ox3, _ox4;

        private void Awake()
        {
            if (randomSeed) seed = Random.Range(1, 99999);
            var rng = new System.Random(seed);
            _ox1 = (float)rng.NextDouble() * 1000f;
            _ox2 = (float)rng.NextDouble() * 1000f;
            _ox3 = (float)rng.NextDouble() * 1000f;
            _ox4 = (float)rng.NextDouble() * 1000f;
            UpdateChunks(force: true);
        }

        private void Update()
        {
            UpdateChunks(false);
            Wind();
        }

        private void UpdateChunks(bool force)
        {
            float x = target != null ? target.position.x : 0f;
            int first = Mathf.FloorToInt((x - keepBehind) / chunkWidth);
            int last = Mathf.FloorToInt((x + buildAhead) / chunkWidth);

            // не больше одного куска за кадр (кроме первой постройки): иначе несколько сеток, коллайдеров, трава и доски
            // строятся в одном кадре — рывок. Кусков впереди с запасом, поэтому очередной успевает построиться заранее
            for (int i = first; i <= last; i++)
                if (!_chunks.ContainsKey(i))
                {
                    _chunks[i] = BuildChunk(i);
                    if (!force) break;
                }

            if (_chunks.Count > last - first + 1)
            {
                var remove = new List<int>();
                foreach (var kv in _chunks)
                    if (kv.Key < first || kv.Key > last) remove.Add(kv.Key);
                foreach (int k in remove)
                {
                    DestroyChunk(_chunks[k]);
                    _chunks.Remove(k);
                    _grass.Remove(k);
                }
            }
        }

        // ------------------------------------------------------------------ рельеф

        /// <summary>Высота поверхности в точке x (мировые координаты).</summary>
        public float HeightAt(float x)
        {
            if (x <= flatStart) return 0f;
            if (!features) return Hills(x);
            GenerateUntil(x);
            int i = FindFeature(x);
            if (i < 0) return Hills(x);
            Feature f = _features[i];
            float a = f.a, b = f.b, L = f.lead, lost, off, prof = 0f;
            if (x < a)
            {   // подход: аргумент шума растёт всё медленнее — холмы плавно выравниваются
                float t = (x - (a - L)) / L;
                lost = f.lostBefore + L * (t * t * t - 0.5f * t * t * t * t); off = f.offIn;
            }
            else if (x <= b)
            {   // сама фигура на ровном основании
                lost = f.lostBefore + 0.5f * L + (x - a); off = f.offIn; prof = f.Profile(x - a);
            }
            else if (x < b + L)
            {   // выход: холмы плавно «оживают»
                float t = (x - b) / L;
                lost = f.lostBefore + 0.5f * L + (b - a) + L * (t - t * t * t + 0.5f * t * t * t * t); off = f.offIn + f.end;
            }
            else { lost = f.lostBefore + (b - a) + L; off = f.offIn + f.end; }
            return Hills(x - lost) + off + prof;
        }

        /// <summary>Естественные холмы от «аргумента шума» u (внутри фигур u не растёт).</summary>
        private float Hills(float u)
        {
            if (u <= flatStart) return 0f;
            float d = u - flatStart;
            float blend = Mathf.SmoothStep(0f, 1f, d / rampIn);
            float difficulty = Mathf.Clamp01(d / maxDifficultyDistance);
            // характер местности: где-то ровные луга, где-то крутые горы
            float variety = Mathf.Lerp(hillsVariety.x, hillsVariety.y, Mathf.PerlinNoise(u * hillsVarietyFrequency + _ox4, 4.4f));

            float big = (Mathf.PerlinNoise(u * bigHillsFrequency + _ox1, 0.37f) - 0.5f) * 2f * bigHillsAmplitude;
            float mid = (Mathf.PerlinNoise(u * hillsFrequency + _ox2, 1.71f) - 0.5f) * 2f * hillsAmplitude * (0.6f + difficulty);
            float small = (Mathf.PerlinNoise(u * bumpsFrequency + _ox3, 2.93f) - 0.5f) * 2f * bumpsAmplitude * (0.5f + 1.5f * difficulty);
            // чтобы большие холмы начинались с нуля, а не со случайной высоты
            float big0 = (Mathf.PerlinNoise(flatStart * bigHillsFrequency + _ox1, 0.37f) - 0.5f) * 2f * bigHillsAmplitude;

            return ((big - big0 + mid) * variety + small) * blend - d * downhill;
        }

        private float Difficulty(float x) => Mathf.Clamp01((x - flatStart) / maxDifficultyDistance);

        private uint Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return h;
            }
        }

        private float Hash01(int a, int b) => (Hash(a, b) & 0xFFFFFF) / (float)0x1000000;

        // ------------------------------------------------------------------ фигуры

        private enum PieceKind { Flat, Lin, Arc, Cos, Out, Hump, In }
        private enum FeatureKind
        {
            Rollers, Table, Doubles, Drop, Stairs, Gap, StepUp, Rocks, Mound,
            // небольшие трамплины под 30 км/ч: высокий, дальний, дроп-трамплин в уклон, степ-ап, дабл
            JHigh, JLong, JDropKick, JStepUp, JDouble,
            // огромные фигуры в духе Red Bull Rampage: 7 трамплинов и 4 дропа
            RpHigh, RpLong, RpCanyon, RpStepDown, RpStepUp, RpWood, RpCombo,
            RdWood, RdCliff, RdBooter, RdDouble,
            // ещё 5 — с настоящего Rampage: Oakley Sender, Super T-Drop, гребень-«нож», жёлоб 63°, Drop XXL
            RpSender, RdSuperT, RpRidge, RpChute, RdXXL
        }
        private const int FirstRampage = (int)FeatureKind.RpHigh;

        /// <summary>Кусок профиля фигуры на [s0, s1] (локально от начала фигуры), высота от y0 до y1.</summary>
        private struct Piece
        {
            public float s0, s1, y0, y1;
            public PieceKind kind;

            public float Y(float s)
            {
                float len = s1 - s0;
                float t = len > 0f ? Mathf.Clamp01((s - s0) / len) : 1f, dy = y1 - y0;
                switch (kind)
                {
                    case PieceKind.Lin: return y0 + dy * t;
                    case PieceKind.Arc:                                          // дуга окружности: переход кикера
                    {
                        float R = (len * len + dy * dy) / (2f * dy), u = t * len;
                        return y0 + R - Mathf.Sqrt(Mathf.Max(0f, R * R - u * u));
                    }
                    case PieceKind.Cos: return y0 + dy * (1f - Mathf.Cos(Mathf.PI * t)) * 0.5f;   // плавно в начале и в конце
                    case PieceKind.Out: return y0 + dy * Mathf.Sin(Mathf.PI * t * 0.5f);          // круто в начале, плавно в конце
                    case PieceKind.Hump: return y0 + dy * (1f - Mathf.Cos(2f * Mathf.PI * t)) * 0.5f; // волна туда и обратно
                    case PieceKind.In: return y0 + dy * (1f - Mathf.Cos(Mathf.PI * t * 0.5f));     // плавно в начале, круто в конце (перегиб)
                    default: return y0;
                }
            }
        }

        /// <summary>Деревянный настил (локально): от s0 до s1 по профилю фигуры; земля под ним — прямая от g0 до g1.</summary>
        private struct WoodSpan { public float s0, s1, g0, g1; }

        /// <summary>Камень-украшение фигуры (локально от начала фигуры): x, y центра, ширина, номер картинки.</summary>
        private struct RockSpot { public float s, y, width; public int sprite; public bool flip; }

        private class Feature
        {
            public float a, b, lead, offIn, end, lostBefore, difficulty;
            /// <summary>Высота ровного основания фигуры (холмы «замерли» + сдвиг от прошлых фигур).</summary>
            public float BaseY;
            /// <summary>Где на фигуре голая земля без высокой травы (локально): от кикера/обрыва до конца приземления.</summary>
            public float bareLo = float.MaxValue, bareHi = float.MinValue;
            public FeatureKind kind;
            public readonly List<Piece> pieces = new List<Piece>();
            public readonly List<float> breaks = new List<float>();     // изломы (локально) — точные точки коллайдера
            public readonly List<RockSpot> rocks = new List<RockSpot>();
            public readonly List<WoodSpan> wood = new List<WoodSpan>();

            public float Profile(float s)
            {
                int lo = 0, hi = pieces.Count - 1;
                while (lo < hi)
                {
                    int m = (lo + hi + 1) / 2;
                    if (pieces[m].s0 <= s) lo = m; else hi = m - 1;
                }
                return pieces[lo].Y(s);
            }
        }

        /// <summary>Собирает профиль фигуры кусками слева направо.</summary>
        private class Builder
        {
            public readonly Feature f;
            public float s, y;
            public Builder(Feature feature) { f = feature; }

            public Builder Add(float length, PieceKind kind, float y1)
            {
                f.pieces.Add(new Piece { s0 = s, s1 = s + length, y0 = y, y1 = y1, kind = kind });
                if (kind == PieceKind.Hump) { s += length; return this; }   // волна возвращается на ту же высоту, излома нет
                f.breaks.Add(s);
                s += length; y = y1;
                f.breaks.Add(s);
                return this;
            }

            public Builder Flat(float length) => Add(length, PieceKind.Flat, y);
            public Builder Wood(float s0, float s1, float g0, float g1) { f.wood.Add(new WoodSpan { s0 = s0, s1 = s1, g0 = g0, g1 = g1 }); return this; }
            public Builder BareStart() { f.bareLo = Mathf.Min(f.bareLo, s); return this; }
            public Builder BareEnd() { f.bareHi = Mathf.Max(f.bareHi, s); return this; }

            /// <summary>
            /// Кикер высотой H: переход — дуга радиуса R (байк поворачивается плавно даже на большой скорости),
            /// затем прямой край длиной lip (≈ колёсная база) — байк вылетает под углом th без вращения.
            /// Невысокий кикер не умещает крутой край — угол уменьшается.
            /// </summary>
            public Builder Kicker(float H, float th, float R = 6f, float lip = 1.2f)
            {
                float a = th * Mathf.Deg2Rad;
                if (KickerHeight(a, R, lip) > H)
                {
                    float lo = 0f, hi = a;
                    for (int i = 0; i < 40; i++)
                    {
                        float m = (lo + hi) * 0.5f;
                        if (KickerHeight(m, R, lip) > H) hi = m; else lo = m;
                    }
                    a = Mathf.Max(lo, 0.01f);
                }
                float Ht = R * (1f - Mathf.Cos(a)), Lt = R * Mathf.Sin(a), y0 = y;
                Add(Lt, PieceKind.Arc, y0 + Ht);
                return Add((H - Ht) / Mathf.Tan(a), PieceKind.Lin, y0 + H);
            }

            private static float KickerHeight(float a, float R, float lip) => R * (1f - Mathf.Cos(a)) + lip * Mathf.Tan(a);
        }

        private readonly List<Feature> _features = new List<Feature>();
        private float _cursor = -1f, _offset, _lost;
        private int _featureCount;
        private FeatureKind _lastKind = FeatureKind.Rocks;
        private bool _hasLast;

        private static float Tan(float deg) => Mathf.Tan(deg * Mathf.Deg2Rad);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Строит фигуры по порядку, пока не покроет x (с запасом).</summary>
        private void GenerateUntil(float x)
        {
            if (_cursor < 0f) _cursor = firstFeature;
            while (_cursor < x + 200f)
            {
                int k = _featureCount;
                float d = Difficulty(_cursor);
                FeatureKind kind = Pick(k, d);
                if (k == 1) d = 0f;                                    // первый стол — маленький
                var f = new Feature { kind = kind, lead = featureLead, offIn = _offset, lostBefore = _lost, difficulty = d };
                var bld = new Builder(f);
                Make(bld, kind, k, d);
                f.a = _cursor + featureLead;
                f.BaseY = Hills(f.a - _lost - 0.5f * featureLead) + _offset;   // высота ровного основания фигуры
                f.b = f.a + bld.s;
                f.end = bld.y;
                _features.Add(f);
                _offset += f.end;
                _lost += (f.b - f.a) + featureLead;
                float gap = Lerp(featureGapStart.x, featureGapHard.x, d) + Hash01(k, 2) * Lerp(featureGapStart.y, featureGapHard.y, d);
                _cursor = f.b + featureLead + gap;
                _featureCount++;
                _lastKind = kind; _hasLast = true;
            }
        }

        private static readonly FeatureKind[] Sequence =
        {
            FeatureKind.Rollers, FeatureKind.Table, FeatureKind.Drop, FeatureKind.Mound,
            FeatureKind.JHigh, FeatureKind.JLong, FeatureKind.JDropKick, FeatureKind.JStepUp, FeatureKind.JDouble,
            FeatureKind.RpHigh, FeatureKind.RpLong, FeatureKind.RpCanyon, FeatureKind.RpStepDown, FeatureKind.RpStepUp, FeatureKind.RpWood, FeatureKind.RpCombo,
            FeatureKind.RdWood, FeatureKind.RdCliff, FeatureKind.RdBooter, FeatureKind.RdDouble,
            FeatureKind.RpSender, FeatureKind.RdSuperT, FeatureKind.RpRidge, FeatureKind.RpChute, FeatureKind.RdXXL,
        };

        private FeatureKind Pick(int k, float d)
        {
            if (sequentialFeatures)
            {
                int start = (int)(Hash01(7, 991) * Sequence.Length);           // первая локация — по сиду (случайная)
                return Sequence[(k + start) % Sequence.Length];
            }
            if (k == 0) return FeatureKind.Rollers;
            if (k == 1) return FeatureKind.Table;
            bool early = d < 0.12f;                                    // первые ~300 м — без даблов, гэпов и лестниц
            float[] w =
            {
                1.0f * rollersWeight, 1.3f * tablesWeight, early ? 0f : (0.5f + 0.7f * d) * doublesWeight,
                (0.9f + 0.5f * d) * dropsWeight, early ? 0f : (0.3f + 0.7f * d) * stairsWeight, early ? 0f : (0.1f + 0.9f * d) * gapsWeight,
                0.6f * stepUpsWeight, 0.5f * rocksWeight, 3.0f * (1f + 0.4f * d) * moundsWeight,
                0.9f * smallJumpsWeight, 0.9f * smallJumpsWeight, 0.9f * smallJumpsWeight, 0.9f * smallJumpsWeight, 0.9f * smallJumpsWeight,
                0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                0f, 0f, 0f, 0f, 0f,
            };
            for (int i = FirstRampage; i < w.Length; i++)
                w[i] = d < rampageFrom ? 0f : rampageWeight * (0.12f + 0.18f * d);
            if (_hasLast) w[(int)_lastKind] *= 0.25f;                   // реже одно и то же подряд
            float total = 0f;
            foreach (float v in w) total += v;
            if (total <= 0f) return FeatureKind.Rollers;
            float r = Hash01(k, 1) * total;
            for (int i = 0; i < w.Length; i++)
            {
                r -= w[i];
                if (r <= 0f && w[i] > 0f) return (FeatureKind)i;
            }
            for (int i = w.Length - 1; i >= 0; i--) if (w[i] > 0f) return (FeatureKind)i;
            return FeatureKind.Rollers;
        }

        /// <summary>Профиль фигуры (размеры растут со сложностью d = 0…1). Числа — как в Tools/terrain/track.py.</summary>
        private void Make(Builder b, FeatureKind kind, int k, float d)
        {
            float R(int j) => Hash01(k, 20 + j);
            switch (kind)
            {
                case FeatureKind.Table:      // кикер → стол → приземление вниз
                {
                    float H = Lerp(0.6f + 0.4f * d, 1.0f + 1.0f * d, R(1)), th = Lerp(22f, 30f, R(2)), D = Lerp(1.5f, 4f, R(3));
                    float Hl = Lerp(0f, 0.8f + 1.2f * d, R(4));
                    b.Flat(3f).BareStart().Kicker(H, th).Flat(D);
                    b.Add((H + Hl) * Mathf.PI / (2f * Tan(30f)), PieceKind.Cos, -Hl).BareEnd().Flat(4f);
                    break;
                }
                case FeatureKind.Doubles:    // 2–3 дабла: кикер, неглубокая яма (не перелетел — выкатишься), приземление ниже
                {
                    int n = 2 + (R(1) > 0.7f - 0.4f * d ? 1 : 0);
                    b.Flat(3f);
                    for (int i = 0; i < n; i++)
                    {
                        int ki = k * 7 + i;
                        float H = Lerp(0.6f + 0.2f * d, 0.9f + 0.5f * d, Hash01(ki, 41)), th = Lerp(24f, 29f, Hash01(ki, 42));
                        float G = Lerp(0.8f, 1.4f + 1.0f * d, Hash01(ki, 43));
                        float y0 = b.y, pit = 0.35f * H, HL = 0.75f * H, step = Lerp(0.3f, 0.6f, Hash01(ki, 45));
                        b.BareStart().Kicker(H, th).Add((H - pit) * Mathf.PI / (2f * Tan(40f)), PieceKind.Cos, y0 + pit).Flat(G);
                        b.Add((HL - pit) * Mathf.PI / (2f * Tan(26f)), PieceKind.Cos, y0 + HL);
                        b.Add((HL + step) * Mathf.PI / (2f * Tan(26f)), PieceKind.Cos, y0 - step).BareEnd();
                        b.Flat(Lerp(16f, 21f, Hash01(ki, 44)));  // перелетел — приземлишься на ровное, а не в следующий кикер (g 0.75: летит дальше)
                    }
                    break;
                }
                case FeatureKind.Gap:        // гэп: разгонный спуск, кикер, яма, приземление ниже вылета
                {
                    float H = Lerp(0.8f, 1.2f + 0.5f * d, R(1)), th = Lerp(26f, 30f, R(2));
                    float HL = H - Lerp(0.4f, 0.8f, R(3)), Pd = HL - Lerp(0.35f, 0.5f + 0.2f * d, R(6));
                    float G = Lerp(1f, 1.8f + 1.8f * d, R(4)), Hl = Lerp(0.8f, 1f + 1.4f * d, R(5));
                    float Rin = Lerp(0.8f, 1.2f + 0.6f * d, R(7));
                    b.Flat(2f).Add(Rin * Mathf.PI / (2f * Tan(12f)), PieceKind.Cos, -Rin).Flat(3f);
                    float y0 = b.y;
                    b.BareStart().Kicker(H, th).Add((H - Pd) * Mathf.PI / (2f * Tan(40f)), PieceKind.Cos, y0 + Pd);
                    for (int i = 0; i < 2; i++)   // камни в яме: видно, что это гэп (выкатиться можно)
                        AddRock(b.f, b.s + G * (0.3f + 0.4f * i), y0 + Pd - 0.14f, Lerp(0.5f, 0.75f, Hash01(k * 7 + i, 84)), k * 7 + i + 500);
                    b.Flat(G);
                    b.Add((HL - Pd) * Mathf.PI / (2f * Tan(22f)), PieceKind.Cos, y0 + HL);
                    b.Add((HL + Hl) * Mathf.PI / (2f * Tan(30f)), PieceKind.Cos, y0 - Hl).BareEnd().Flat(4f);
                    break;
                }
                case FeatureKind.Drop:       // дроп: обрыв, приземление на склон (или на ровное — сложнее)
                {
                    float Hd = Lerp(0.9f + 0.5f * d, 1.4f + 1.5f * d, R(1));
                    bool toFlat = R(2) < 0.25f + 0.3f * d;
                    b.Flat(3f).BareStart().Flat(3f);
                    LipRock(b, k, 0);
                    if (toFlat) { Cliff(b, -Hd, k, 1); b.Flat(2.5f).BareEnd().Flat(3.5f); }
                    else
                    {
                        float c = 0.6f * Hd;
                        Cliff(b, -c, k, 1);
                        b.Add((Hd - c) * Mathf.PI / (2f * Tan(35f)), PieceKind.Out, -Hd).BareEnd().Flat(5f);
                    }
                    break;
                }
                case FeatureKind.Stairs:     // лестница из 2–4 дропов
                {
                    int n = 2 + (R(1) > 0.5f ? 1 : 0) + (d > 0.6f && R(2) > 0.5f ? 1 : 0);
                    b.Flat(3f).BareStart().Flat(2f);
                    for (int i = 0; i < n; i++)
                    {
                        float h = Lerp(0.5f, 0.8f + 0.6f * d, Hash01(k * 7 + i, 60));
                        Cliff(b, b.y - h, k * 7 + i, 2);
                        b.Flat(Lerp(4.8f, 6.5f, Hash01(k * 7 + i, 61)));    // полки ≥ 4.8 м: успеть выровняться
                    }
                    b.BareEnd().Flat(2f);
                    break;
                }
                case FeatureKind.StepUp:     // ступень вверх: крутой подъём на полку
                {
                    float Hu = Lerp(0.5f, 0.9f + 0.6f * d, R(1));
                    b.Flat(4f).BareStart().Flat(1f).Add(Hu * Mathf.PI / (2f * Tan(33f)), PieceKind.Cos, Hu).Flat(2f).BareEnd().Flat(Lerp(2f, 5f, R(2)));
                    break;
                }
                case FeatureKind.Rollers:    // волны: катятся на обычной скорости, на большой — короткие перелёты
                {
                    int n = 3 + (int)(R(1) * 4f);
                    float A = Lerp(0.4f, 0.6f + 0.5f * d, R(2)), lam = Lerp(11f, 16f, R(3));   // длиннее при гравитации 0.75
                    b.Flat(2f).BareStart();
                    for (int i = 0; i < n; i++) b.Add(lam, PieceKind.Hump, b.y + A * Lerp(0.8f, 1.15f, Hash01(k * 7 + i, 70)));
                    b.BareEnd().Flat(2f);
                    break;
                }
                case FeatureKind.RpHigh: case FeatureKind.RpLong: case FeatureKind.RpCanyon: case FeatureKind.RpStepDown:
                case FeatureKind.RpStepUp: case FeatureKind.RpWood: case FeatureKind.RpCombo:
                case FeatureKind.RdWood: case FeatureKind.RdCliff: case FeatureKind.RdBooter: case FeatureKind.RdDouble:
                case FeatureKind.RpSender: case FeatureKind.RdSuperT: case FeatureKind.RpRidge: case FeatureKind.RpChute: case FeatureKind.RdXXL:
                    Rampage(b, kind, k);
                    break;
                case FeatureKind.JHigh: case FeatureKind.JLong: case FeatureKind.JDropKick: case FeatureKind.JStepUp: case FeatureKind.JDouble:
                    SmallJump(b, kind, k);
                    break;
                case FeatureKind.Mound:      // горка-трамплин как в Mountain Bike Xtreme: крутой подъём, острая вершина, длинный спуск-приземление
                {
                    float H = Lerp(1.6f + 0.6f * d, 2.4f + 1.2f * d, R(1)), th = Lerp(32f, 40f, R(2));
                    float Hl = Lerp(2f + 2f * d, 4f + 3f * d, R(3));
                    b.Flat(3f).BareStart().Kicker(H, th, 8f, 1f);
                    b.Add((H + Hl) * Mathf.PI / (2f * Tan(moundLandingAngle)), PieceKind.Cos, -Hl).BareEnd().Flat(6f);
                    break;
                }
                default:                     // каменистый участок: мелкие ступеньки вверх-вниз
                {
                    int n = 5 + (int)(R(1) * 5f);
                    b.Flat(2f).BareStart();
                    for (int i = 0; i < n; i++)
                    {
                        float ri = Hash01(k * 7 + i, 80);
                        float h = i % 2 == 0 ? Lerp(0.08f, 0.22f, ri) : -Lerp(0.1f, 0.32f + 0.1f * d, ri);
                        float top = Mathf.Max(b.y, b.y + h);
                        b.Add(0.25f, PieceKind.Lin, b.y + h);
                        float w = Lerp(0.6f, 1.0f, Hash01(k * 7 + i, 82));
                        AddRock(b.f, b.s - 0.12f, top - 0.3f * w, w, k * 7 + i);              // камень-ступенька, чуть выше земли
                        float fl = Lerp(0.7f, 1.8f, Hash01(k * 7 + i, 81));
                        if (Hash01(k * 7 + i, 83) < 0.5f)                                      // ещё камень на ровном, утоплен
                        {
                            float w2 = Lerp(0.35f, 0.6f, Hash01(k * 7 + i, 85));
                            AddRock(b.f, b.s + fl * 0.5f, b.y - 0.35f * w2, w2, k * 7 + i + 300);
                        }
                        b.Flat(fl);
                    }
                    b.BareEnd().Flat(2f);
                    break;
                }
            }
        }

        /// <summary>
        /// Небольшие трамплины, рассчитанные на заход 30 км/ч (газ держат, кнопок не жмут): склон приземления — по траектории
        /// байка с этой скоростью, удар при приземлении на 30 км/ч — меньше 1.2 м/с; на ±2.5 км/ч — тоже в склон или чуть
        /// недолёт/перелёт. Профили подобраны на физической модели (Tools/terrain/jumps30.py → jumps30.json): тройки
        /// «длина, высота в конце, вид куска»; первый кусок — ровный подъезд, последний — ровный выкат.
        /// </summary>
        private const float JF = (float)PieceKind.Flat, JL = (float)PieceKind.Lin, JA = (float)PieceKind.Arc,
                            JC = (float)PieceKind.Cos, JO = (float)PieceKind.Out, JI = (float)PieceKind.In;
        private static readonly float[][] SmallJumps =
        {
            // высокий: крутой кикер 1.6 м / 40°, высокий полёт (~0.75 с), приземление 41° на горбе выше вылета
            new float[] { 3.0f, 0.0f, JF, 2.5015f, 0.8787f, JA, 0.9f, 1.6f, JL, 0.3921f, 1.2f, JC, 0.4575f, 1.2f, JF, 0.4575f, 2.0953f, JC, 0.6505f, 1.7353f, JI, 2.2869f, -0.2527f, JL, 1.5396f, -1.1047f, JO, 6.0f, -1.1047f, JF },
            // дальний: пологий кикер 0.9 м / 22°, пролёт ~5.5 м над ямой, приземление 36° под горку (ниже на 4 м)
            new float[] { 3.0f, 0.0f, JF, 2.7007f, 0.4696f, JA, 1.2f, 0.9f, JL, 1.375f, -0.6292f, JC, 1.6041f, -0.6292f, JF, 1.6041f, -0.1292f, JC, 0.6486f, -0.4292f, JI, 3.8539f, -3.2292f, JL, 2.5944f, -4.4292f, JO, 6.0f, -4.4292f, JF },
            // дроп-трамплин в уклон: маленький кикер 0.35 м на краю полки, обрыв 1.2 м, приземление в склон 35°
            new float[] { 3.0f, 0.0f, JF, 0.7765f, 0.1022f, JA, 0.9247f, 0.35f, JL, 0.12f, -0.8251f, JL, 4.3802f, -0.8251f, JF, 0.673f, -1.1251f, JI, 2.7992f, -3.0851f, JL, 1.8844f, -3.9251f, JO, 6.0f, -3.9251f, JF },
            // степ-ап: кикер 1.4 м / 34°, приземление на полку выше вылета (19°)
            new float[] { 3.0f, 0.0f, JF, 2.6657f, 0.7698f, JA, 1.0f, 1.4f, JL, 0.5013f, 1.1f, JC, 0.5848f, 1.1f, JF, 0.5848f, 2.0335f, JC, 0.3285f, 1.9615f, JI, 2.5371f, 1.0879f, JL, 1.708f, 0.7135f, JO, 6.0f, 0.7135f, JF },
            // дабл: кикер 1.1 м / 30°, яма, приземление 31°
            new float[] { 3.0f, 0.0f, JF, 2.3266f, 0.5743f, JA, 1.0f, 1.1f, JL, 0.7404f, 0.7f, JC, 0.8638f, 0.7f, JF, 0.8638f, 1.5484f, JC, 0.8627f, 1.2184f, JI, 3.0523f, -0.6156f, JL, 2.0548f, -1.4016f, JO, 6.0f, -1.4016f, JF },
        };

        private void SmallJump(Builder b, FeatureKind kind, int k)
        {
            float[] p = SmallJumps[kind - FeatureKind.JHigh];
            int n = p.Length / 3;
            for (int i = 0; i < n; i++)
            {
                float len = p[i * 3], y1 = p[i * 3 + 1];
                var pk = (PieceKind)(int)p[i * 3 + 2];
                if (i == n - 1) b.BareEnd();
                float top = b.y;
                b.Add(len, pk, y1);
                if (i == 0) b.BareStart();
                if (pk == PieceKind.Lin && len < 0.2f && top - y1 > 0.5f)          // обрыв дроп-трамплина: камни на стенке
                {
                    float h = top - y1;
                    AddRock(b.f, b.s - 0.25f, y1 + h * 0.35f, Mathf.Clamp(h * 0.9f, 0.6f, 1.2f), k * 7 + 1);
                }
            }
        }

        /// <summary>Разгонный спуск перед огромной фигурой: скорость набирается сама.</summary>
        private void RunIn(Builder b, int k, float lo = 3f, float hi = 6f, float ang = 20f)
        {
            float rin = Lerp(lo, hi, Hash01(k, 29));
            b.Flat(4f).Add(rin * Mathf.PI / (2f * Tan(ang)), PieceKind.Cos, b.y - rin).Flat(3f);
        }

        /// <summary>Длинное крутое приземление (как на Rampage) с плавным выкатом.</summary>
        private static void Landing(Builder b, float drop, float ang, float flat = 8f)
        {
            b.Add(drop * Mathf.PI / (2f * Tan(ang)), PieceKind.Cos, b.y - drop).BareEnd().Flat(flat);
        }

        /// <summary>Огромные фигуры в духе Red Bull Rampage. Числа — как в Tools/terrain/track.py (rampage).</summary>
        private void Rampage(Builder b, FeatureKind kind, int k)
        {
            float R(int j) => Hash01(k, 20 + j);
            float big = rampageScale;
            switch (kind)
            {
                case FeatureKind.RpHigh:     // высотный: крутой кикер 48–55°, высоко вверх и длинное приземление 40°
                {
                    RunIn(b, k);
                    float H = Lerp(3.2f, 4.5f, R(1)) * big;
                    b.BareStart().Kicker(H, Lerp(48f, 55f, R(2)), 10f, 1.2f);
                    Landing(b, H + Lerp(5f, 8f, R(3)) * big, 40f);
                    break;
                }
                case FeatureKind.RpLong:     // дальний: пологий быстрый кикер 30°, длинный стол 8–14 м, приземление 36°
                {
                    RunIn(b, k, 4f, 7f);
                    float H = Lerp(2.4f, 3.4f, R(1)) * big;
                    b.BareStart().Kicker(H, Lerp(28f, 32f, R(2)), 12f, 1.5f).Flat(Lerp(8f, 14f, R(3)));
                    Landing(b, H + Lerp(4f, 7f, R(4)) * big, 36f);
                    break;
                }
                case FeatureKind.RpCanyon:   // каньон: кикер, глубокая пропасть с отвесными стенками, приземление на спуск
                {
                    RunIn(b, k, 5f, 8f);
                    float y0 = b.y, H = Lerp(2.4f, 3f, R(1));
                    b.BareStart().Kicker(H, Lerp(33f, 37f, R(2)), 10f, 1.2f);
                    float W = Lerp(5.3f, 7.5f, R(3)) * canyonWidth,   // ширина под земную гравитацию (при 0.75 g была 7–10 м)
                           D = Lerp(6f, 9f, R(4)), Hl = Lerp(1f, 2f, R(5));
                    Cliff(b, y0 - D, k, 3); b.Flat(W - 2f * cliffWidth); Cliff(b, y0 - Hl, k, 4);
                    Landing(b, Lerp(5f, 8f, R(6)), 36f);
                    break;
                }
                case FeatureKind.RpStepDown: // степ-даун: кикер, сразу обрыв на нижнюю полку, приземление уходит ещё ниже
                {
                    RunIn(b, k);
                    float y0 = b.y, H = Lerp(2f, 3f, R(1));
                    b.BareStart().Kicker(H, Lerp(28f, 33f, R(2)), 10f, 1.2f);
                    Cliff(b, y0 - Lerp(2.5f, 4.5f, R(3)), k, 3);
                    Landing(b, Lerp(5f, 8f, R(4)) * big, 36f);
                    break;
                }
                case FeatureKind.RpStepUp:   // степ-ап: кикер 40°, приземление на полку выше вылета, длинная полка, спуск
                {
                    RunIn(b, k, 5f, 8f);
                    float y0 = b.y, H = Lerp(2.4f, 3.2f, R(1));
                    b.BareStart().Kicker(H, Lerp(38f, 42f, R(2)), 10f, 1.2f);
                    float U = H + Lerp(0.3f, 1f, R(3)), G = Lerp(2f, 3.5f, R(4));
                    b.Add(1f, PieceKind.Cos, y0 + H - 0.8f).Flat(G).Add((U - (H - 0.8f)) / Tan(55f), PieceKind.Lin, y0 + U);
                    b.Flat(Lerp(10f, 14f, R(5)));
                    Landing(b, U + Lerp(3f, 5f, R(6)), 32f);
                    break;
                }
                case FeatureKind.RpWood:     // деревянный кикер: настил из досок на опорах; за ним пустота и приземление 40°
                {
                    RunIn(b, k);
                    float y0 = b.y, H = Lerp(2.6f, 3.8f, R(1)) * big, s0 = b.s;
                    b.BareStart().Kicker(H, Lerp(36f, 42f, R(2)), 8f, 1.4f);
                    b.Wood(s0 - 0.5f, b.s, y0, y0);
                    float G = Lerp(3f, 5f, R(3));
                    b.Add(cliffWidth, PieceKind.Lin, y0).Flat(G);                  // задняя стенка настила — до земли
                    Landing(b, Lerp(4f, 7f, R(4)) * big, 40f);
                    break;
                }
                case FeatureKind.RpCombo:    // связка: огромный кикер → приземление → сразу второй кикер → приземление
                {
                    RunIn(b, k);
                    for (int i = 0; i < 2; i++)
                    {
                        int ki = k * 7 + i;
                        // второй кикер ниже: на него можно заехать и без разгона; приземления длинные — перелёт на скорости не страшен
                        float H = (i == 0 ? Lerp(2.4f, 3.4f, Hash01(ki, 111)) : Lerp(1.8f, 2.6f, Hash01(ki, 111))) * big;
                        float Hd = Lerp(4.5f, 7f, Hash01(ki, 113));
                        b.BareStart().Kicker(H, Lerp(32f, 38f, Hash01(ki, 112)), 10f, 1.2f);
                        b.Add((H + Hd) * Mathf.PI / (2f * Tan(36f)), PieceKind.Cos, b.y - H - Hd);
                        b.Flat(Lerp(16f, 21f, Hash01(ki, 114)));   // перелетел приземление — сядешь на ровное, а не в лицо второму кикеру
                    }
                    b.BareEnd().Flat(4f);
                    break;
                }
                case FeatureKind.RdWood:     // деревянный дроп: настил над обрывом, срываешься с края досок на крутое приземление
                {
                    b.Flat(4f);
                    float y0 = b.y, L = Lerp(3f, 5f, R(1)), Hd = Lerp(4f, 7f, R(2)) * big, s0 = b.s;
                    b.BareStart().Flat(L);
                    b.Wood(s0, s0 + L, y0, y0 - Hd * 0.85f);                        // опоры стоят на скальном склоне
                    b.Add(cliffWidth, PieceKind.Lin, y0 - Hd);
                    Landing(b, Lerp(5f, 8f, R(3)) * big, 40f);
                    break;
                }
                case FeatureKind.RdCliff:    // естественный скальный дроп 4–9 м на крутое приземление
                {
                    b.Flat(4f).BareStart().Flat(2f);
                    float y0 = b.y, Hd = Lerp(4f, 9f, R(1)) * big;
                    AddRock(b.f, b.s - 0.5f, b.y - 0.3f, 1.6f, k * 13, 2);
                    Cliff(b, y0 - Hd * 0.75f, k, 1);
                    Landing(b, Hd * 0.25f + Lerp(5f, 8f, R(2)) * big, 42f);
                    break;
                }
                case FeatureKind.RdBooter:   // деревянный кикер-дроп: короткий настил с подъёмом на краю, затем большой дроп
                {
                    b.Flat(4f);
                    float y0 = b.y, s0 = b.s, Hk = Lerp(0.5f, 0.9f, R(1)), Hd = Lerp(4f, 7f, R(2)) * big;
                    b.BareStart().Flat(2f).Kicker(Hk, Lerp(15f, 20f, R(3)), 4f, 0.8f);
                    b.Wood(s0, b.s, y0, y0);
                    b.Add(cliffWidth, PieceKind.Lin, y0 - Hd);
                    Landing(b, Lerp(5f, 8f, R(4)) * big, 40f);
                    break;
                }
                case FeatureKind.RpSender:   // Oakley Sender (Rampage 2008–2013): деревянный роллин вниз, настил на высоких опорах
                {                            // над обрывом, кик на краю досок и огромный дроп в крутой склон
                    b.Flat(4f);
                    float y0 = b.y, s0 = b.s, Hr = Lerp(1.5f, 2.2f, R(1));
                    b.BareStart().Add(Hr * Mathf.PI / (2f * Tan(28f)), PieceKind.Cos, y0 - Hr).Flat(Lerp(2.5f, 3.5f, R(2)));
                    float yk = b.y;
                    b.Kicker(Lerp(0.5f, 0.8f, R(3)), Lerp(16f, 20f, R(4)), 4f, 0.8f);
                    float Hd = Lerp(6f, 8f, R(5)) * big;
                    b.Wood(s0, b.s, y0, yk - Hd * 0.8f);                           // опоры стоят на скальном склоне
                    b.Add(cliffWidth, PieceKind.Lin, yk - Hd);
                    Landing(b, Lerp(6f, 9f, R(6)) * big, 42f);
                    break;
                }
                case FeatureKind.RdSuperT:   // Super T-Drop (Tyler Klassen, 2002): крутой скальный жёлоб, полка, скальный обрыв 10+ м,
                {                            // крутое каменистое приземление
                    b.Flat(4f);
                    float y0 = b.y, Hc = Lerp(2.5f, 3.5f, R(1));
                    b.BareStart().Add(Hc * Mathf.PI / (2f * Tan(30f)), PieceKind.Cos, y0 - Hc).Flat(Lerp(2f, 3f, R(2)));
                    AddRock(b.f, b.s - 0.5f, b.y - 0.3f, 1.6f, k * 13, 2);
                    float Hd = Lerp(7f, 8.5f, R(3)) * big;
                    Cliff(b, b.y - Hd * 0.8f, k, 1);
                    Landing(b, Hd * 0.2f + Lerp(6f, 8f, R(4)) * big, 45f);
                    LandingRocks(b, 4, k, 1);
                    break;
                }
                case FeatureKind.RpRidge:    // Knife-edge ridgeline (Pierre-Edouard Ferry, 2013): спуск по узкому гребню 24° —
                {                            // каменные ступени-дропы одна за другой, в конце большой дроп с гребня
                    b.Flat(4f).BareStart();
                    int n = 3 + (R(1) > 0.5f ? 1 : 0);
                    for (int i = 0; i < n; i++)
                    {
                        int ki = k * 7 + i;
                        float Lr = Lerp(4f, 6f, Hash01(ki, 131));
                        b.Add(Lr, PieceKind.Lin, b.y - Lr * Tan(24f));
                        Cliff(b, b.y - Lerp(0.8f, 1.4f, Hash01(ki, 132)), ki, 6);
                    }
                    float Le = Lerp(3f, 4f, R(2));
                    b.Add(Le, PieceKind.Lin, b.y - Le * Tan(24f));
                    Cliff(b, b.y - Lerp(2.5f, 3.5f, R(3)) * big, k * 7 + 9, 7);
                    Landing(b, Lerp(4f, 6f, R(4)) * big, 40f);
                    break;
                }
                case FeatureKind.RpChute:    // 63° (Pierre-Edouard Ferry, 2017): перегиб и почти отвесный жёлоб 60°, выкат дугой
                {
                    b.Flat(4f).BareStart();
                    float ang = Lerp(58f, 62f, R(1)), H = Lerp(6f, 8f, R(2)) * big, h1 = 1.6f;
                    b.Add(h1 * Mathf.PI / (2f * Tan(ang)), PieceKind.In, b.y - h1);
                    float h2 = H - h1;
                    b.Add(h2 / Tan(ang), PieceKind.Lin, b.y - h2);
                    SteepOut(b, ang);
                    b.BareEnd().Flat(10f);
                    break;
                }
                case FeatureKind.RdXXL:      // Drop XXL (Kyle Strait, 2013): кик на краю гребня и огромный дроп глубоко в длинный склон
                {
                    RunIn(b, k, 2f, 3f, 18f);
                    float y0 = b.y;
                    b.BareStart().Kicker(Lerp(0.9f, 1.2f, R(1)), Lerp(20f, 24f, R(2)), 5f, 1f);
                    float Hd = Lerp(5f, 7f, R(3)) * big;
                    Cliff(b, y0 - Hd, k, 1);
                    Landing(b, Lerp(9f, 12f, R(4)) * big, 44f);
                    break;
                }
                default:                     // RdDouble — двойной дроп: скала → полка с приземлением → вторая скала → приземление
                {
                    b.Flat(4f).BareStart().Flat(2f);
                    for (int i = 0; i < 2; i++)
                    {
                        int ki = k * 7 + i;
                        float y0 = b.y, Hd = Lerp(3f, 5f, Hash01(ki, 121)) * big;
                        Cliff(b, y0 - Hd * 0.7f, ki, 5);
                        // приземление на полке — длинное и крутое (при земной гравитации с 20–35 км/ч летят 4–12 м), затем ровная полка
                        if (i == 0) Landing(b, Hd * 0.3f + Lerp(5f, 7f, Hash01(ki, 123)) * big, 40f, Lerp(5f, 7f, Hash01(ki, 122)));
                        else Landing(b, Hd * 0.3f + Lerp(4f, 6f, Hash01(ki, 122)), 40f);
                    }
                    break;
                }
            }
        }

        /// <summary>Обрыв до высоты y1 и камни на его стенке.</summary>
        private void Cliff(Builder b, float y1, int k, int j)
        {
            float top = b.y, h = top - y1, x = b.s;
            b.Add(cliffWidth, PieceKind.Lin, y1);
            int n = h < 0.9f ? 1 : (h < 1.8f ? 2 : 3);
            for (int i = 0; i < n; i++)
            {
                float w = Mathf.Clamp(h / n * 1.5f, 0.7f, 1.5f) * Lerp(0.9f, 1.1f, Hash01(k + 31 * i, 90 + j));
                float y = y1 + h * (i + 0.5f) / n + (Hash01(k + 31 * i, 92 + j) - 0.5f) * 0.12f;
                AddRock(b.f, x - 0.25f * w, y, w, k + 31 * i + j);       // камень вдавлен в стенку, торчит на четверть
            }
        }

        /// <summary>Плоский камень на краю дропа — край выглядит как скальный уступ.</summary>
        /// <summary>Камни, вросшие в склон приземления (каменистое приземление): по поверхности последнего куска.</summary>
        private void LandingRocks(Builder b, int n, int k, int j)
        {
            Piece p = b.f.pieces[b.f.pieces.Count - 1];
            for (int i = 0; i < n; i++)
            {
                float sl = Lerp(p.s0, p.s1, (i + 0.5f + (Hash01(k * 5 + i, 140 + j) - 0.5f) * 0.6f) / n);
                float w = Lerp(0.6f, 1.1f, Hash01(k * 5 + i, 141 + j));
                AddRock(b.f, sl, p.Y(sl) - 0.35f * w, w, k * 5 + i + 400);
            }
        }

        /// <summary>Выкат из крутого жёлоба: уклон уменьшается ступенями на отрезках по 1.6 м (как дуга).</summary>
        private static void SteepOut(Builder b, float ang, int n = 5)
        {
            for (int i = 0; i < n; i++)
            {
                float a = ang * (n - 1 - i) / n + 6f;
                b.Add(1.6f, PieceKind.Lin, b.y - 1.6f * Tan(a));
            }
        }

        private void LipRock(Builder b, int k, int j) => AddRock(b.f, b.s - 0.5f, b.y - 0.24f, 1.2f, k * 13 + j, 2);

        private void AddRock(Feature f, float s, float y, float width, int key, int forceSprite = -1)
        {
            if (rocks == null || rocks.Length == 0) return;
            int sp = forceSprite >= 0 ? Mathf.Min(forceSprite, rocks.Length - 1) : (int)(Hash(key, 95) % (uint)rocks.Length);
            f.rocks.Add(new RockSpot { s = s, y = y, width = width, sprite = sp, flip = Hash01(key, 96) < 0.5f });
        }

        /// <summary>
        /// Высота травы (доля от обычной): на трамплинах и дропах — голая утоптанная земля (форма фигуры хорошо видна),
        /// на волнах и камнях — низкая трава. Края плавные (1.5 м).
        /// </summary>
        private float GrassFactor(float x)
        {
            if (!features) return 1f;
            int i = FindFeature(x + 2f);
            float g = 1f;
            for (int j = Mathf.Max(0, i - 1); j <= i; j++)
            {
                Feature f = _features[j];
                if (f.bareHi < f.bareLo) continue;
                float lo = f.a + f.bareLo - 1.5f, hi = f.a + f.bareHi + 1.5f;
                if (x < lo || x > hi) continue;
                float bare = f.kind == FeatureKind.Rollers ? 0.55f : (f.kind == FeatureKind.Rocks ? 0.35f : 0.12f);
                g = Mathf.Min(g, Mathf.Lerp(1f, bare, Mathf.Clamp01(Mathf.Min(x - lo, hi - x) / 1.5f)));
            }
            return g;
        }

        /// <summary>Под деревянным настилом: высота земли в точке x (мировые координаты).</summary>
        private bool WoodGroundAt(float x, out float gy)
        {
            gy = 0f;
            int i = FindFeature(x);
            for (int j = Mathf.Max(0, i - 1); j <= i && j >= 0; j++)
            {
                Feature f = _features[j];
                foreach (WoodSpan w in f.wood)
                {
                    float s = x - f.a;
                    if (s < w.s0 || s > w.s1) continue;
                    gy = f.BaseY + Mathf.Lerp(w.g0, w.g1, Mathf.InverseLerp(w.s0, w.s1, s));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Деревянные фигуры: боковина настила (доски по профилю, толщина 0.25 м), опоры-брусья до земли каждые ~1.4 м
        /// и раскосы между высокими опорами. Только в пределах чанка [x0, x1).
        /// </summary>
        private void BuildWood(Transform parent, float x0, float x1)
        {
            if (woodMaterial == null) return;
            var dv = new List<Vector3>(); var duv = new List<Vector2>(); var dt = new List<int>();
            var pv = new List<Vector3>(); var puv = new List<Vector2>(); var pt = new List<int>();
            const float thick = 0.25f, postW = 0.16f, braceW = 0.09f, postStep = 1.4f;
            foreach (Feature f in _features)
            {
                if (f.b < x0 - 1f || f.a > x1 + 1f) continue;
                foreach (WoodSpan w in f.wood)
                {
                    float a = Mathf.Max(f.a + w.s0, x0), b = Mathf.Min(f.a + w.s1, x1);
                    if (b <= a) continue;
                    // боковина настила: лента вдоль профиля
                    int n = Mathf.Max(2, Mathf.CeilToInt((b - a) / 0.2f) + 1);
                    int start = dv.Count;
                    for (int i = 0; i < n; i++)
                    {
                        float x = Mathf.Lerp(a, b, i / (float)(n - 1)), y = f.BaseY + f.Profile(x - f.a);
                        dv.Add(new Vector3(x, y + 0.02f)); dv.Add(new Vector3(x, y - thick));
                        duv.Add(new Vector2(x / 2f, 1f)); duv.Add(new Vector2(x / 2f, 0.5f));
                        if (i > 0) { int k = start + i * 2; dt.AddRange(new[] { k - 2, k, k - 1, k - 1, k, k + 1 }); }
                    }
                    // опоры: от низа настила до земли (+30 см в землю), раскосы между соседними высокими опорами
                    float lastX = float.NaN, lastTop = 0f, lastBot = 0f;
                    float sx = f.a + w.s0 + 0.25f, ex = f.a + w.s1 - 0.12f;
                    int cnt = Mathf.Max(1, Mathf.CeilToInt((ex - sx) / postStep));
                    for (int i = 0; i <= cnt; i++)
                    {
                        float x = Mathf.Lerp(sx, ex, i / (float)cnt);
                        float topY = f.BaseY + f.Profile(x - f.a) - thick;
                        float botY = f.BaseY + Mathf.Lerp(w.g0, w.g1, Mathf.InverseLerp(w.s0, w.s1, x - f.a)) - 0.3f;
                        if (topY - botY < 0.45f) { lastX = float.NaN; continue; }
                        if (x >= x0 && x < x1)
                            Quad(pv, puv, pt, new Vector3(x - postW / 2, botY), new Vector3(x + postW / 2, botY),
                                 new Vector3(x + postW / 2, topY), new Vector3(x - postW / 2, topY), (topY - botY) / 2f);
                        if (!float.IsNaN(lastX) && topY - botY > 1.2f && lastTop - lastBot > 1.2f && x >= x0 && lastX < x1)
                        {
                            Vector3 p0 = new Vector3(lastX, lastTop - 0.1f), p1 = new Vector3(x, botY + 0.4f);
                            Vector3 nrm = Vector3.Cross(p1 - p0, Vector3.forward).normalized * braceW * 0.5f;
                            Quad(pv, puv, pt, p0 - nrm, p1 - nrm, p1 + nrm, p0 + nrm, (p1 - p0).magnitude / 2f);
                        }
                        lastX = x; lastTop = topY; lastBot = botY;
                    }
                }
            }
            if (pv.Count > 0) WoodMesh(parent, "Wood Posts", pv, puv, pt, woodPostTexture, 2);
            if (dv.Count > 0) WoodMesh(parent, "Wood Deck", dv, duv, dt, woodDeckTexture, 3);
        }

        private static void Quad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float len)
        {
            int k = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, len)); uv.Add(new Vector2(0f, len));
            t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
        }

        private void WoodMesh(Transform parent, string name, List<Vector3> v, List<Vector2> uv, List<int> t, Texture tex, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            var cols = new Color[v.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = woodTint;
            mesh.colors = cols;
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = woodMaterial;
            SetTexture(mr, tex);
            mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Индекс последней фигуры, у которой зона подхода уже началась до x (или −1).</summary>
        /// <summary>Есть ли фигура (трамплин, дроп, волны…) с подходом и выходом на отрезке [x0, x1] — для автопилота в меню.</summary>
        public bool FeatureBetween(float x0, float x1)
        {
            if (!features || x1 <= flatStart) return false;
            GenerateUntil(x1);
            int i = FindFeature(x1);
            for (; i >= 0; i--)
            {
                Feature f = _features[i];
                if (f.b + f.lead < x0) break;
                if (f.a - f.lead <= x1) return true;
            }
            return false;
        }

        /// <summary>Есть ли на отрезке [x0, x1] огромная фигура (Rampage: трамплины и дропы) — бот заходит на них быстрее.</summary>
        public bool BigFeatureBetween(float x0, float x1)
        {
            if (!features || x1 <= flatStart) return false;
            GenerateUntil(x1);
            for (int i = FindFeature(x1); i >= 0; i--)
            {
                Feature f = _features[i];
                if (f.b + f.lead < x0) break;
                if (f.a - f.lead <= x1 && (int)f.kind >= FirstRampage) return true;
            }
            return false;
        }

        /// <summary>
        /// Огромная фигура на отрезке [x0, x1]: её начало (мировой x) и идеальная скорость захода, м/с, — с ней байк
        /// приземляется в склон приземления с наименьшим ударом. Подобрано перебором скоростей на модели
        /// (Tools/terrain/menu_bot.py, при Rampage Scale 1.5 и Canyon Width 1.5): кикеры 8–12 м/с, дропы 4.5–5.5 м/с.
        /// </summary>
        public bool BigFeatureAhead(float x0, float x1, out float start, out float idealSpeed)
        {
            start = 0f; idealSpeed = 0f;
            if (!features || x1 <= flatStart) return false;
            GenerateUntil(x1);
            for (int i = FindFeature(x1); i >= 0; i--)
            {
                Feature f = _features[i];
                if (f.b + f.lead < x0) break;
                if (f.a - f.lead <= x1 && Tuned(f.kind)) { start = f.a; idealSpeed = IdealSpeed(f.kind); return true; }
            }
            return false;
        }

        /// <summary>Фигуры с рассчитанной скоростью захода: огромные (Rampage) и небольшие трамплины под 30 км/ч.</summary>
        private static bool Tuned(FeatureKind k) => (int)k >= FirstRampage || IsSmallJump(k);
        private static bool IsSmallJump(FeatureKind k) => k >= FeatureKind.JHigh && k <= FeatureKind.JDouble;

        private static float IdealSpeed(FeatureKind k)
        {
            if (IsSmallJump(k)) return SmallJumpSpeed;
            switch (k)
            {
                case FeatureKind.RpHigh: return 10f;
                case FeatureKind.RpLong: return 9f;
                case FeatureKind.RpCanyon: return 12f;
                case FeatureKind.RpStepDown: return 7.5f;
                case FeatureKind.RpStepUp: return 11f;
                case FeatureKind.RpWood: return 11f;
                case FeatureKind.RpCombo: return 8f;
                case FeatureKind.RdWood: return 4.5f;
                case FeatureKind.RdCliff: return 5.5f;
                case FeatureKind.RdBooter: return 5f;
                case FeatureKind.RdDouble: return 5f;
                case FeatureKind.RpSender: return 6f;
                case FeatureKind.RdSuperT: return 5f;
                case FeatureKind.RpRidge: return 4f;
                case FeatureKind.RpChute: return 3f;      // на жёлоб 60° — медленно: быстрее — байк отрывается на перегибе
                case FeatureKind.RdXXL: return 7f;
                default: return 9f;
            }
        }

        /// <summary>
        /// Первая фигура, которая заходит на отрезок [x0, x1]: её начало (начало подхода к трамплину/дропу) и конец
        /// (конец выката после приземления), мировые x. Для камеры меню: показать фигуру целиком.
        /// </summary>
        public bool FeatureSpan(float x0, float x1, out float start, out float end)
        {
            start = end = 0f;
            if (!features || x1 <= flatStart) return false;
            GenerateUntil(x1);
            int i = FindFeature(x0);
            if (i < 0) i = 0;
            for (; i < _features.Count; i++)
            {
                Feature f = _features[i];
                if (f.a > x1) break;
                if (f.b < x0) continue;
                start = f.a; end = f.b;
                return true;
            }
            return false;
        }

        private int FindFeature(float x)
        {
            int lo = 0, hi = _features.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int m = (lo + hi) / 2;
                if (_features[m].a - _features[m].lead <= x) { ans = m; lo = m + 1; }
                else hi = m - 1;
            }
            return ans;
        }

        // ------------------------------------------------------------------ построение

        /// <summary>Точки поверхности чанка: равномерно через step + точные изломы фигур (край кикера, обрыв дропа).</summary>
        private Vector2[] SurfacePoints(float x0)
        {
            float x1 = x0 + chunkWidth;
            int n = Mathf.RoundToInt(chunkWidth / step) + 1;
            var xs = new List<float>(n + 16);
            for (int i = 0; i < n; i++) xs.Add(x0 + i * step);
            if (features)
            {
                GenerateUntil(x1);
                foreach (Feature f in _features)
                {
                    if (f.b < x0 || f.a > x1) continue;
                    foreach (float sb in f.breaks)
                    {
                        float x = f.a + sb;
                        if (x > x0 + 0.005f && x < x1 - 0.005f) xs.Add(x);
                    }
                }
                xs.Sort();
                // слишком близкие точки (меньше 5 мм) — одна
                var clean = new List<float>(xs.Count) { xs[0] };
                for (int i = 1; i < xs.Count; i++) if (xs[i] - clean[clean.Count - 1] > 0.005f) clean.Add(xs[i]);
                if (clean[clean.Count - 1] < x1 - 0.001f) clean.Add(x1);
                xs = clean;
            }
            var top = new Vector2[xs.Count];
            for (int i = 0; i < xs.Count; i++) top[i] = new Vector2(xs[i], HeightAt(xs[i]));
            return top;
        }

        private GameObject BuildChunk(int index)
        {
            float x0 = index * chunkWidth;
            Vector2[] top = SurfacePoints(x0);
            int n = top.Length;

            var go = new GameObject($"Chunk {index}");
            go.transform.SetParent(transform, false);
            go.layer = groundLayer;

            // --- коллайдер ---
            var edge = go.AddComponent<EdgeCollider2D>();
            edge.points = top;
            edge.sharedMaterial = groundPhysics;
            // соседние точки — чтобы колесо не 'спотыкалось' на стыке чанков
            edge.useAdjacentStartPoint = true;
            edge.adjacentStartPoint = new Vector2(x0 - step, HeightAt(x0 - step));
            edge.useAdjacentEndPoint = true;
            float xe = x0 + chunkWidth + step;
            edge.adjacentEndPoint = new Vector2(xe, HeightAt(xe));
            // (xe считается от равномерной сетки: последняя точка чанка всегда x0 + chunkWidth)

            // --- под деревянным настилом земля ниже поверхности (коллайдер — по доскам, заливка — по земле) ---
            var ground = new Vector2[n];
            bool anyWood = false;
            for (int i = 0; i < n; i++)
            {
                ground[i] = top[i];
                if (features && WoodGroundAt(top[i].x, out float gy)) { ground[i].y = Mathf.Min(top[i].y, gy); anyWood = true; }
            }
            Vector2[] deck = top;
            top = ground;                                                    // дальше земля, перегной и трава — по земле

            // --- заливка земли (текстура почвы в мировых координатах — стыков между чанками нет) ---
            // по глубине — несколько рядов: под поверхностью почва быстро темнеет (как в настоящем разрезе: сверху светлый
            // сухой слой, глубже — влажная тёмная земля), дальше — почти тёмная; всё непрозрачное
            const int R = 5;
            float[] depthAt = { 0f, 1.2f, 4f, 12f, groundDepth };
            float[] shadeAt = { 1f, 0.82f, 0.58f, 0.4f, 0.3f };
            var verts = new Vector3[n * R];
            var colors = new Color[n * R];
            var uvs = new Vector2[n * R];
            var tris = new int[(n - 1) * (R - 1) * 6];
            bool textured = groundMaterial != null;
            for (int i = 0; i < n; i++)
            {
                for (int r = 0; r < R; r++)
                {
                    int v = i * R + r;
                    verts[v] = new Vector3(top[i].x, top[i].y - Mathf.Min(depthAt[r], groundDepth));
                    float k = shadeAt[r];
                    colors[v] = textured ? new Color(groundTint.r * k, groundTint.g * k, groundTint.b * k, 1f)
                                         : Color.Lerp(groundTop, groundBottom, 1f - (k - 0.3f) / 0.7f);
                    uvs[v] = (Vector2)verts[v] / groundTile;
                }
                if (i < n - 1)
                    for (int r = 0; r < R - 1; r++)
                    {
                        int t = (i * (R - 1) + r) * 6, v = i * R + r;
                        tris[t] = v; tris[t + 1] = v + R; tris[t + 2] = v + 1;
                        tris[t + 3] = v + 1; tris[t + 4] = v + R; tris[t + 5] = v + R + 1;
                    }
            }
            var mesh = new Mesh { name = go.name };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = textured ? groundMaterial : material;
            if (textured) SetTexture(mr, groundTexture);
            mr.sortingOrder = 0;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (!textured)
            {
                // старый вид: линия травы и контур
                AddLine(go.transform, "Grass", top, grassColor, grassWidth, 1, -grassWidth * 0.3f);
                AddLine(go.transform, "Outline", top, outlineColor, outlineWidth, 2, 0f);
                return go;
            }

            // --- перегной с корнями под поверхностью ---
            if (topsoilMaterial != null)
            {
                var rv = new Vector3[n * 2]; var ruv = new Vector2[n * 2];
                for (int i = 0; i < n; i++)
                {
                    float u = top[i].x / topsoilTile;                       // по x — без стыков между чанками
                    rv[i * 2] = top[i] + Vector2.up * 0.01f;
                    rv[i * 2 + 1] = top[i] - Vector2.up * topsoilDepth;
                    ruv[i * 2] = new Vector2(u, 1f);
                    ruv[i * 2 + 1] = new Vector2(u, 0f);
                }
                Ribbon(go.transform, "Topsoil", rv, ruv, topsoilMaterial, 1, topsoilTexture);
            }

            // --- трава растёт вертикально из земли; u по x — травинки не растягиваются на склонах ---
            var g = new ChunkGrass();
            var cliff = new bool[n - 1];                                   // на отвесных стенках трава не растёт
            for (int i = 0; i < n - 1; i++)
                cliff[i] = Mathf.Abs(top[i + 1].y - top[i].y) > 1.5f * Mathf.Max(0.001f, top[i + 1].x - top[i].x)
                           || deck[i].y > top[i].y + 0.05f || deck[i + 1].y > top[i + 1].y + 0.05f;   // под настилом трава не видна
            if (grassBackMaterial != null)
                g.back = GrassRibbon(go.transform, "Grass Back", top, grassBackMaterial, grassBackTexture, grassBackHeight, grassBackRoot, 5, cliff, out g.backBase);
            if (grassFrontMaterial != null)
                g.front = GrassRibbon(go.transform, "Grass Front", top, grassFrontMaterial, grassFrontTexture, grassFrontHeight, grassFrontRoot, 60, cliff, out g.frontBase);
            if (g.backBase != null) g.backWork = (Vector3[])g.backBase.Clone();
            if (g.frontBase != null) g.frontWork = (Vector3[])g.frontBase.Clone();

            // --- кусты травы ---
            if (tufts != null && tufts.Length > 0)
            {
                var rng = new System.Random(seed * 31 + index);
                int count = Mathf.RoundToInt(chunkWidth / 10f * tuftsPer10m);
                for (int k = 0; k < count; k++)
                {
                    float x = x0 + (float)rng.NextDouble() * chunkWidth;
                    float y = HeightAt(x);
                    float slope = Mathf.Atan((HeightAt(x + 0.3f) - HeightAt(x - 0.3f)) / 0.6f) * Mathf.Rad2Deg;
                    if (Mathf.Abs(slope) > 35f) continue;                     // на обрывах не растёт
                    if (GrassFactor(x) < 0.6f) continue;                      // на трамплинах и дропах — голая земля
                    bool front = rng.NextDouble() < tuftsInFront;
                    int ti = rng.Next(tufts.Length);
                    Sprite sp = tufts[ti];
                    if (sp == null) continue;
                    var t = new GameObject("Tuft");
                    t.transform.SetParent(go.transform, false);
                    float sc = front ? Mathf.Lerp(0.3f, 0.5f, (float)rng.NextDouble()) : Mathf.Lerp(0.5f, 1f, (float)rng.NextDouble());
                    t.transform.localPosition = new Vector3(x, y - 0.03f, 0f);
                    t.transform.localScale = new Vector3(rng.NextDouble() < 0.5 ? -sc : sc, sc, 1f);
                    float baseAngle = slope * 0.25f;                          // трава тянется вверх, чуть по склону
                    t.transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle);
                    var sr = t.AddComponent<SpriteRenderer>();
                    sr.sprite = sp;
                    if (tuftMaterial != null) sr.sharedMaterial = tuftMaterial;
                    sr.sortingOrder = front ? 61 : 4;
                    float shade = front ? 1f : Mathf.Lerp(0.82f, 1f, (float)rng.NextDouble());
                    sr.color = new Color(shade, shade, shade);
                    g.tufts.Add(t.transform); g.tuftAngle.Add(baseAngle); g.tuftHeight.Add(sc);
                    g.tuftSway.Add(tuftSway != null && ti < tuftSway.Length ? tuftSway[ti] : 1f);
                }
            }
            _grass[index] = g;

            // --- деревянные настилы и опоры огромных фигур ---
            if (anyWood || features) BuildWood(go.transform, x0, x0 + chunkWidth);

            // --- камни на обрывах дропов и на каменистых участках ---
            if (features && rocks != null && rocks.Length > 0)
            {
                foreach (Feature f in _features)
                {
                    if (f.b < x0 - 2f || f.a > x0 + chunkWidth + 2f) continue;
                    foreach (RockSpot r in f.rocks)
                    {
                        float x = f.a + r.s;
                        if (x < x0 || x >= x0 + chunkWidth) continue;          // каждый камень — в одном чанке
                        Sprite sp = rocks[r.sprite];
                        if (sp == null) continue;
                        var rg = new GameObject("Rock");
                        rg.transform.SetParent(go.transform, false);
                        float sc = r.width / Mathf.Max(0.01f, sp.bounds.size.x);
                        rg.transform.localPosition = new Vector3(x, f.BaseY + r.y, 0f);
                        rg.transform.localScale = new Vector3(r.flip ? -sc : sc, sc, 1f);
                        var sr = rg.AddComponent<SpriteRenderer>();
                        sr.sprite = sp;
                        if (tuftMaterial != null) sr.sharedMaterial = tuftMaterial;
                        sr.sortingOrder = 4;                                      // перед землёй, за густой травой и байком
                        sr.color = rockTint;
                    }
                }
            }

            return go;
        }

        private Mesh GrassRibbon(Transform parent, string name, Vector2[] top, Material mat, Texture tex, float height, float root, int order, bool[] skip, out Vector3[] baseVerts)
        {
            int n = top.Length;
            var v = new Vector3[n * 2]; var uv = new Vector2[n * 2];
            for (int i = 0; i < n; i++)
            {
                float u = top[i].x / grassTile;
                v[i * 2] = new Vector3(top[i].x, top[i].y - root + height * GrassFactor(top[i].x));   // на фигурах трава низкая
                v[i * 2 + 1] = new Vector3(top[i].x, top[i].y - root);
                uv[i * 2] = new Vector2(u, 1f);
                uv[i * 2 + 1] = new Vector2(u, 0f);
            }
            baseVerts = v;
            return Ribbon(parent, name, v, uv, mat, order, tex, skip);
        }

        private Mesh Ribbon(Transform parent, string name, Vector3[] v, Vector2[] uv, Material mat, int order, Texture tex, bool[] skip = null)
        {
            int n = v.Length / 2;
            var list = new List<int>((n - 1) * 6);
            for (int i = 0; i < n - 1; i++)
            {
                if (skip != null && skip[i]) continue;                         // промежуток (обрыв) — без ленты
                int k = i * 2;
                list.Add(k); list.Add(k + 2); list.Add(k + 1);
                list.Add(k + 1); list.Add(k + 2); list.Add(k + 3);
            }
            int[] tris = list.ToArray();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = tris;
            var cols = new Color[v.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            mesh.colors = cols;
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + new Vector3(1f, 1f, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            SetTexture(mr, tex);
            mr.sortingOrder = order;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mesh;
        }

        /// <summary>Спрайтовый шейдер URP берёт _MainTex с рендерера ([PerRendererData]) — передаём текстуру напрямую.</summary>
        private static void SetTexture(Renderer r, Texture tex)
        {
            if (tex == null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_MainTex"))
                tex = r.sharedMaterial.GetTexture("_MainTex");
            if (tex == null) return;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetTexture("_MainTex", tex);
            r.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------ ветер и колёса

        /// <summary>Качает траву ветром (порывы бегут вдоль склона) и пригибает её под колёсами.</summary>
        private void Wind()
        {
            float t = Time.time;
            foreach (var g in _grass.Values)
            {
                if (grassSway > 0f)
                {
                    if (g.back != null) Sway(g.back, g.backBase, g.backWork, t, windStrength * grassSway, 0.35f);
                    if (g.front != null) Sway(g.front, g.frontBase, g.frontWork, t, windStrength * 0.6f * grassSway, 0.2f);
                }
                for (int i = 0; i < g.tufts.Count; i++)
                {
                    Transform tr = g.tufts[i];
                    if (tr == null) continue;
                    float x = tr.position.x;
                    float sway = g.tuftSway[i];
                    if (sway <= 0f) continue;                                    // камень, сугроб — не качаются
                    float a = WindAt(x, t) * 7f / Mathf.Max(0.4f, g.tuftHeight[i]);
                    a += WheelPush(x, tr.position.y, 0.45f) * -35f;
                    a *= sway;
                    tr.localRotation = Quaternion.Euler(0f, 0f, g.tuftAngle[i] + a);
                }
            }
        }

        private float WindAt(float x, float t) =>
            Mathf.Sin(t * windSpeed + x * 0.9f) * 0.6f + Mathf.Sin(t * windSpeed * 0.37f + x * 0.21f) * 0.5f
            + Mathf.Max(0f, Mathf.Sin(t * 0.5f - x * 0.08f)) * 0.8f;          // порыв, бегущий по трассе

        /// <summary>-1…1: колесо рядом слева/справа (трава отклоняется от колеса), 0 — далеко.</summary>
        private float WheelPush(float x, float y, float radius)
        {
            if (wheels == null) return 0f;
            float push = 0f;
            foreach (var w in wheels)
            {
                if (w == null) continue;
                Vector2 p = w.position;
                if (p.y - y > 0.9f) continue;                                   // колесо высоко — траву не задевает
                float d = x - p.x;
                if (Mathf.Abs(d) < radius) push += Mathf.Sign(d) * (1f - Mathf.Abs(d) / radius);
            }
            return Mathf.Clamp(push, -1f, 1f);
        }

        private void Sway(Mesh mesh, Vector3[] baseV, Vector3[] work, float t, float strength, float push)
        {
            for (int i = 0; i < baseV.Length; i += 2)                          // верхние вершины ленты
            {
                Vector3 b = baseV[i];
                float off = WindAt(b.x, t) * strength + WheelPush(b.x, baseV[i + 1].y, 0.5f) * push;
                work[i] = new Vector3(b.x + off, b.y - Mathf.Abs(off) * 0.4f, b.z);
            }
            mesh.vertices = work;
        }

        private void AddLine(Transform parent, string name, Vector2[] pts, Color color, float width, int order, float yOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.sharedMaterial = material;
            lr.startColor = lr.endColor = color;
            lr.startWidth = lr.endWidth = width;
            lr.numCornerVertices = 2;
            lr.sortingOrder = order;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.positionCount = pts.Length;
            for (int i = 0; i < pts.Length; i++)
                lr.SetPosition(i, new Vector3(pts[i].x, pts[i].y + yOffset));
        }

        /// <summary>Удаляет чанк вместе с его мешем (иначе меши копятся в памяти).</summary>
        private static void DestroyChunk(GameObject chunk)
        {
            if (chunk == null) return;
            foreach (var mf in chunk.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(chunk);
        }

        private void OnDestroy()
        {
            foreach (var kv in _chunks) DestroyChunk(kv.Value);
            _chunks.Clear();
            _grass.Clear();
        }
    }
}
