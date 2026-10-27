using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Локации: фон (6 слоёв параллакса), земля, трава/снег, кусты у трассы и погода. В игре — та, что выбрана в меню
    /// (Locations.Selected); в главном меню с ботом — случайная (и не та же, что в прошлый раз), как витрина.
    /// Всё подставляется ДО запуска слоёв и трассы (порядок выполнения −500): картинки и заливки слоёв, текстуры почвы,
    /// перегноя/снега, полос травы, кусты, оттенок камней, качание травы; погода — LocationWeather, пылинки — AmbientMotes.
    /// Картинки: Art/Background/&lt;локация&gt;, Art/Ground/&lt;локация&gt;, Art/Grass/&lt;локация&gt;, Art/Grass/Tufts/&lt;локация&gt;
    /// (генераторы Tools/art/environment/bg_themes.py и locations_art.py). Висит на камере; заполняет сборщик сцены.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class BackgroundThemes : MonoBehaviour
    {
        [System.Serializable]
        public class Theme
        {
            public string name;
            public Sprite sky, mountainsFar, mountainsMid, forestFar, fog, forestNear;
            [Tooltip("Цвет под небом и над ним (верх неба), под горами и лесом (нижний край картинки) и дымка под лесом.")]
            public Color skyFill, skyTop, farFill, midFill, forestFarFill, forestFarHaze, forestNearFill, forestNearHaze;

            [Header("Земля и трава (пусто — как в TerrainGenerator)")]
            public Texture ground, topsoil, grassBack, grassFront;
            public Sprite[] tufts;
            [Tooltip("Насколько качается каждый куст (камни, сугробы — 0).")]
            public float[] tuftSway;
            [Tooltip("Оттенок почвы (прозрачный — не менять).")]
            public Color groundTint = Color.clear;
            [Tooltip("Оттенок камней на обрывах (прозрачный — не менять).")]
            public Color rockTint = Color.clear;
            [Tooltip("Высота полос травы (снега), м; 0 — не менять.")]
            public float grassBackHeight, grassFrontHeight;
            [Tooltip("Кустов на 10 м; 0 — не менять.")]
            public float tuftsPer10m;
            [Tooltip("Качание полос травы ветром (0 — снег, не качается).")]
            public float grassSway = 1f;
            [Tooltip("Сцепление шин с землёй (1 — обычное, 0.5 — вдвое меньше: снег и лёд).")]
            [Range(0.1f, 1f)] public float grip = 1f;

            [Header("Погода")]
            public LocationWeather.Kind weather = LocationWeather.Kind.None;
            [Tooltip("Пылинки в воздухе: цвет и сколько в секунду (0 — нет).")]
            public Color moteColor = new Color(1f, 0.92f, 0.72f, 0.55f);
            public float moteRate = 14f;
        }

        public enum Pick { Chosen, Random }

        public Theme[] themes;
        public ParallaxLayer sky, mountainsFar, mountainsMid, forestFar, fog, forestNear;
        [Tooltip("Chosen — локация, выбранная в меню (игра); Random — случайная (меню с ботом).")]
        public Pick pick = Pick.Chosen;
        [Tooltip("Номер локации для проверки (−1 — как задано в Pick).")]
        public int forceTheme = -1;
        [Header("Что ещё настраивает локация")]
        public TerrainGenerator terrain;
        public LocationWeather weather;
        public AmbientMotes motes;

        /// <summary>Какая локация сейчас (id).</summary>
        public static string Current { get; private set; } = "";

        private void Awake()
        {
            if (themes == null || themes.Length == 0) return;
            int i;
            if (forceTheme >= 0) i = Mathf.Min(forceTheme, themes.Length - 1);
            else if (pick == Pick.Chosen) i = Find(Locations.Selected);
            else
            {
                i = Random.Range(0, themes.Length);
                int last = PlayerPrefs.GetInt("BgTheme", -1);
                if (themes.Length > 1 && i == last) i = (i + 1 + Random.Range(0, themes.Length - 1)) % themes.Length;   // не тот же, что в прошлый раз
                PlayerPrefs.SetInt("BgTheme", i);
            }
            Apply(themes[i]);
        }

        private int Find(string id)
        {
            for (int k = 0; k < themes.Length; k++)
                if (themes[k] != null && themes[k].name == id) return k;
            return 0;
        }

        private void Apply(Theme t)
        {
            Current = t.name;
            Set(sky, t.sky, t.skyFill, Color.clear);
            if (sky != null) sky.fillTopColor = t.skyTop;
            Set(mountainsFar, t.mountainsFar, t.farFill, Color.clear);
            Set(mountainsMid, t.mountainsMid, t.midFill, Color.clear);
            Set(forestFar, t.forestFar, t.forestFarFill, t.forestFarHaze);
            Set(fog, t.fog, Color.clear, Color.clear);
            Set(forestNear, t.forestNear, t.forestNearFill, t.forestNearHaze);
            var cam = GetComponent<Camera>();
            if (cam != null) cam.backgroundColor = t.skyTop;

            if (terrain != null)
            {
                if (t.ground != null) terrain.groundTexture = t.ground;
                if (t.topsoil != null) terrain.topsoilTexture = t.topsoil;
                if (t.grassBack != null) terrain.grassBackTexture = t.grassBack;
                if (t.grassFront != null) terrain.grassFrontTexture = t.grassFront;
                if (t.tufts != null && t.tufts.Length > 0) { terrain.tufts = t.tufts; terrain.tuftSway = t.tuftSway; }
                if (t.groundTint.a > 0f) terrain.groundTint = t.groundTint;
                if (t.rockTint.a > 0f) terrain.rockTint = t.rockTint;
                if (t.grassBackHeight > 0f) terrain.grassBackHeight = t.grassBackHeight;
                if (t.grassFrontHeight > 0f) terrain.grassFrontHeight = t.grassFrontHeight;
                if (t.tuftsPer10m > 0f) terrain.tuftsPer10m = t.tuftsPer10m;
                terrain.grassSway = t.grassSway;
                if (t.grip < 0.999f && terrain.groundPhysics != null)
                {
                    // трение пары шина–земля в Box2D — среднее геометрическое √(шина·земля): чтобы сцепление стало
                    // в grip раз меньше, трение земли уменьшаем в grip² раз (своя копия материала, ассет не меняется)
                    PhysicsMaterial2D src = terrain.groundPhysics;
                    terrain.groundPhysics = new PhysicsMaterial2D(src.name + " (" + t.name + ")")
                    {
                        friction = src.friction * t.grip * t.grip,
                        bounciness = src.bounciness
                    };
                }
            }
            if (weather != null) weather.kind = t.weather;
            if (motes != null)
            {
                motes.color = t.moteColor;
                motes.rate = t.moteRate;
                if (t.moteRate <= 0f) motes.enabled = false;
            }
        }

        private static void Set(ParallaxLayer l, Sprite s, Color fill, Color haze)
        {
            if (l == null || s == null) return;
            l.sprite = s;
            if (l.fillColor.a > 0f || fill.a > 0f) { l.fillColor = fill; l.fillHazeColor = haze; }
        }
    }
}
