using UnityEngine;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>
    /// Время суток в мире. Солнца на небе не видно — вместо него в игре идут часы: каждая секунда — минута в мире
    /// (Minutes Per Second), полные сутки — 24 минуты игры. От времени зависят:
    ///   • небо — днём голубое, на закате и рассвете — тёплое (родная картинка неба), ночью — тёмно-синее со звёздами;
    ///   • горы и лес — цвет света (днём нейтральный, ночью синеватый и тёмный);
    ///   • вся сцена (трасса, трава, гонщик) — лёгкая засветка поверх: тёплая на рассвете и закате, тёмно-синяя ночью;
    ///   • свет на гонщике (BikeVisual) — откуда светит солнце (утром слева, днём сверху, вечером справа), ночью — слабая луна.
    /// Время сохраняется между заездами и в меню (PlayerPrefs «WorldHour»).
    /// Висит на камере; создаётся сборщиком сцены.
    /// </summary>
    [DefaultExecutionOrder(110)]   // после камеры и параллакса
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Часы")]
        [Tooltip("Сколько минут мира проходит за секунду игры (1 — сутки за 24 минуты).")]
        public float minutesPerSecond = 1f;
        [Tooltip("С какого часа начинается самая первая игра (дальше время сохраняется).")]
        [Range(0f, 24f)] public float startHour = 8f;
        [Tooltip("Текст часов на экране (ЧЧ:ММ). Можно не задавать.")]
        public Text clockText;

        [Header("Что окрашивать (задаёт сборщик сцены)")]
        public ParallaxLayer sky;
        public ParallaxLayer[] layers;
        [Tooltip("Засветка поверх всей сцены (картинка на Canvas под интерфейсом).")]
        public Image screenTint;
        public Material material;

        [Header("Сила")]
        [Tooltip("Насколько темно ночью (0 — как днём, 1 — как задумано).")]
        [Range(0f, 1.5f)] public float nightDarkness = 1f;
        [Tooltip("Свет на гонщике: днём и ночью (луна).")]
        public float daySunStrength = 1f, nightSunStrength = 0.3f;

        /// <summary>Текущее время, часы 0…24.</summary>
        public static float Hour { get; private set; } = -1f;

        // ключи суток: час, покрытие неба (цвет, альфа), свет на горах, засветка сцены (цвет, альфа), звёзды, свет на гонщике, цвет верха неба
        private struct Key
        {
            public float h;
            public Color cover, layer, tint, top;
            public float stars, sun;
            public Key(float h, Color cover, Color layer, Color tint, Color top, float stars, float sun)
            { this.h = h; this.cover = cover; this.layer = layer; this.tint = tint; this.top = top; this.stars = stars; this.sun = sun; }
        }

        private static readonly Color NightCover = new Color(0.03f, 0.05f, 0.12f, 0.95f);
        private static readonly Color NightLayer = new Color(0.32f, 0.37f, 0.55f);
        private static readonly Color NightTint = new Color(0.03f, 0.05f, 0.16f, 0.5f);
        private static readonly Color NightTop = new Color(0.02f, 0.03f, 0.08f);
        private static readonly Color DayCover = new Color(0.58f, 0.73f, 0.9f, 0.86f);    // спокойнее, без «мультяшной» синевы
        private static readonly Color DayLayer = new Color(0.93f, 0.98f, 1f);
        private static readonly Color DayTop = new Color(0.36f, 0.52f, 0.78f);
        private static readonly Color DuskTop = new Color(36f / 255f, 45f / 255f, 86f / 255f);

        private static readonly Key[] Keys =
        {
            new Key(0f,    NightCover, NightLayer, NightTint, NightTop, 1f, 0f),
            new Key(4.5f,  NightCover, NightLayer, NightTint, NightTop, 1f, 0f),
            new Key(5.5f,  new Color(0.14f, 0.13f, 0.28f, 0.7f), new Color(0.6f, 0.55f, 0.7f), new Color(0.1f, 0.08f, 0.22f, 0.32f), DuskTop, 0.4f, 0.4f),
            new Key(6.5f,  new Color(0.95f, 0.7f, 0.6f, 0.1f), new Color(0.97f, 0.86f, 0.84f), new Color(0.95f, 0.55f, 0.35f, 0.12f), DuskTop, 0f, 0.8f),
            new Key(8.5f,  DayCover, DayLayer, new Color(1f, 1f, 1f, 0f), DayTop, 0f, 1f),
            new Key(16.5f, DayCover, DayLayer, new Color(1f, 1f, 1f, 0f), DayTop, 0f, 1f),
            new Key(18.3f, new Color(0.95f, 0.7f, 0.6f, 0f), Color.white, new Color(1f, 0.6f, 0.3f, 0.08f), DuskTop, 0f, 0.85f),
            new Key(19.6f, new Color(0.14f, 0.13f, 0.3f, 0.65f), new Color(0.62f, 0.56f, 0.7f), new Color(0.1f, 0.08f, 0.24f, 0.3f), DuskTop, 0.4f, 0.4f),
            new Key(21f,   NightCover, NightLayer, NightTint, NightTop, 1f, 0f),
            new Key(24f,   NightCover, NightLayer, NightTint, NightTop, 1f, 0f),
        };

        private Camera _cam;
        private SpriteRenderer _cover, _coverGrad, _stars;
        [Tooltip("Днём у горизонта небо светлее и теплее (дымка): доля покрытия неба, которая убирается у горизонта.")]
        [Range(0f, 1f)] public float horizonHaze = 0.55f;
        private BikeVisual[] _riders = new BikeVisual[0];
        private float _findTimer, _saveTimer;
        private static Sprite _white;
        private int _shownMinute = -1;
        private static string[] _clock;
        /// <summary>Строки «ЧЧ:ММ» на все минуты суток — готовые, чтобы не создавать строку каждый кадр (мусор → рывки сборщика).</summary>
        private static string[] Clock
        {
            get
            {
                if (_clock == null)
                {
                    _clock = new string[1440];
                    for (int i = 0; i < 1440; i++) _clock[i] = $"{i / 60:00}:{i % 60:00}";
                }
                return _clock;
            }
        }

        private static bool Near(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.004f;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            if (Hour < 0f) Hour = Mathf.Repeat(PlayerPrefs.GetFloat("WorldHour", startHour), 24f);
            if (_white == null)
                _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            int coverOrder = sky != null ? sky.sortingOrder + 1 : -99;
            _cover = MakeQuad("Sky Cover", _white, coverOrder);
            _coverGrad = MakeQuad("Sky Cover Top", MakeGradient(), coverOrder);   // днём: к зениту гуще, у горизонта — тёплая дымка неба
            _stars = MakeQuad("Stars", MakeStars(), coverOrder + 1);
#if UNITY_2023_1_OR_NEWER
            _riders = FindObjectsByType<BikeVisual>(FindObjectsSortMode.None);
#else
            _riders = FindObjectsOfType<BikeVisual>();
#endif
            Apply();
        }

        private SpriteRenderer MakeQuad(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sortingOrder = order; sr.color = Color.clear;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>Вертикальный градиент прозрачности: внизу (горизонт) 0, вверху 1 — спрайт 1×1 м.</summary>
        private static Sprite MakeGradient()
        {
            const int n = 64;
            var tex = new Texture2D(4, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            {
                float t = Mathf.Clamp01((y / (float)(n - 1) - 0.3f) / 0.6f);      // нижние 30% — без покрытия, к верху — полное
                Color c = new Color(1f, 1f, 1f, t * t * (3f - 2f * t));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, n), new Vector2(0.5f, 0.5f), n);
        }

        /// <summary>Звёздное небо: точки разной яркости, несколько крупных.</summary>
        private static Sprite MakeStars()
        {
            const int w = 512, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var rnd = new System.Random(7);
            for (int i = 0; i < 420; i++)
            {
                int x = rnd.Next(w), y = rnd.Next(h / 6, h);                  // у горизонта звёзд нет — там горы и дымка
                float b = (float)rnd.NextDouble();
                byte a = (byte)(80 + 175 * b * b * ((float)y / h));
                px[y * w + x] = new Color32(255, 255, (byte)(225 + rnd.Next(30)), a);
                if (b > 0.93f)                                                 // крупная звезда — крестик из соседних пикселей
                    foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int xx = x + d.Item1, yy = y + d.Item2;
                        if (xx >= 0 && xx < w && yy >= 0 && yy < h) px[yy * w + xx] = new Color32(255, 255, 240, (byte)(a / 2));
                    }
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private void Update()
        {
            Hour = Mathf.Repeat(Hour + minutesPerSecond * Time.deltaTime / 60f, 24f);
            _saveTimer += Time.unscaledDeltaTime;
            if (_saveTimer > 5f) { _saveTimer = 0f; PlayerPrefs.SetFloat("WorldHour", Hour); }
            // гонщиков ищем только пока не нашли (обход всех объектов сцены — заметный рывок кадра, если делать его часто)
            _findTimer -= Time.unscaledDeltaTime;
            if (_riders.Length == 0 && _findTimer <= 0f)
            {
                _findTimer = 2f;
#if UNITY_2023_1_OR_NEWER
                _riders = FindObjectsByType<BikeVisual>(FindObjectsSortMode.None);
#else
                _riders = FindObjectsOfType<BikeVisual>();
#endif
            }
        }

        private void OnDisable() { if (Hour >= 0f) PlayerPrefs.SetFloat("WorldHour", Hour); }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (Hour < 0f) return;
            // соседние ключи и плавный переход между ними
            int i = 0;
            while (i < Keys.Length - 2 && Hour >= Keys[i + 1].h) i++;
            Key a = Keys[i], b = Keys[i + 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.h, b.h, Hour));
            float dark = Mathf.Clamp01(nightDarkness);
            Color cover = Color.Lerp(a.cover, b.cover, t);
            Color layer = Color.Lerp(Color.white, Color.Lerp(a.layer, b.layer, t), dark);
            Color tint = Color.Lerp(a.tint, b.tint, t); tint.a *= nightDarkness;
            Color top = Color.Lerp(a.top, b.top, t);
            float stars = Mathf.Lerp(a.stars, b.stars, t);
            float sun = Mathf.Lerp(a.sun, b.sun, t);

            if (_cam != null)
            {
                _cam.backgroundColor = top;
                float hgt = 2f * _cam.orthographicSize, wid = hgt * _cam.aspect;
                Vector3 c = _cam.transform.position; c.z = 0f;
                // днём покрытие неба у горизонта слабее: просвечивает тёплое небо картинки — воздушная дымка, как в жизни
                float haze = horizonHaze * sun * Mathf.Clamp01(1f - stars);
                if (_cover != null)
                {
                    _cover.transform.position = c;
                    _cover.transform.localScale = new Vector3(wid * 1.1f, hgt * 1.1f, 1f);   // спрайт 1×1 м
                    Color cu = cover; cu.a *= 1f - haze; _cover.color = cu;
                }
                if (_coverGrad != null)
                {
                    _coverGrad.transform.position = c;
                    _coverGrad.transform.localScale = new Vector3(wid * 1.1f * 16f, hgt * 1.1f, 1f);   // спрайт 4/64 × 1 м
                    Color cg = cover; cg.a *= haze; _coverGrad.color = cg;
                }
                if (_stars != null)
                {
                    // звёзды бесконечно далеко: на экране стоят на месте, верхняя часть кадра
                    float k = wid / Mathf.Max(0.01f, _stars.sprite.bounds.size.x);
                    _stars.transform.localScale = new Vector3(k, k, 1f);
                    _stars.transform.position = c + new Vector3(0f, hgt * 0.5f - _stars.sprite.bounds.size.y * k * 0.5f, 0f);
                    _stars.color = new Color(1f, 1f, 1f, stars * (0.85f + 0.15f * Mathf.Sin(Time.time * 0.7f)));
                }
            }
            if (sky != null) sky.SetTint(Color.white, top);
            if (layers != null)
                foreach (ParallaxLayer l in layers)
                    if (l != null) l.SetTint(layer, Color.clear);
            // интерфейс перестраивается при каждом изменении — меняем засветку и часы, только когда видно разницу
            if (screenTint != null && !Near(screenTint.color, tint)) screenTint.color = tint;

            // свет на гонщике: солнце встаёт слева (6:00), в полдень сверху, садится справа (18:00); ночью — луна, наоборот
            float dayAngle = Mathf.PI * (Hour - 6f) / 12f;
            bool day = Hour >= 6f && Hour < 18f;
            float ang = day ? dayAngle : dayAngle - Mathf.PI;
            Vector2 dir = new Vector2(-Mathf.Cos(ang), Mathf.Max(0.25f, Mathf.Sin(ang))).normalized;
            float strength = Mathf.Lerp(nightSunStrength, daySunStrength, sun);
            foreach (BikeVisual r in _riders)
                if (r != null) { r.sunDirection = dir; r.sunStrength = strength; }

            if (clockText != null)
            {
                int m = Mathf.FloorToInt(Hour * 60f) % 1440;
                if (m != _shownMinute) { _shownMinute = m; clockText.text = Clock[m]; }
            }
        }
    }
}
