using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Внешний вид байка и гонщика: спрайты деталей и цвета.
    /// Слои Paint нарисованы белым/серым и перекрашиваются в цвета ниже —
    /// новый «скин» для магазина = копия ассета с другими цветами (или своими спрайтами).
    /// Создать: ПКМ в Project → Create → Bike → Bike Skin.
    /// Все спрайты: Pixels Per Unit = 256, Pivot = Center (сборщик сцены настраивает это сам).
    /// </summary>
    [CreateAssetMenu(menuName = "Bike/Bike Skin", fileName = "BikeSkin")]
    public class BikeSkin : ScriptableObject
    {
        /// <summary>Часть тела/детали из трёх слоёв: основа, перекрашиваемый слой, детали поверх.</summary>
        [System.Serializable]
        public class Layered
        {
            public Sprite baseLayer;
            [Tooltip("Перекрашивается в цвет (джерси, шорты, шлем).")]
            public Sprite paintLayer;
            public Sprite topLayer;
            [Tooltip("Свет солнца, когда к солнцу повёрнута сторона +Y спрайта (тёплый свет и холодная тень). BikeVisual смешивает " +
                     "Light Up / Light Down по повороту части — свет на теле всегда с одной стороны, как у 3D-модели.")]
            public Sprite lightUp;
            public Sprite lightDown;
        }

        [Header("Цвета байка")]
        [Tooltip("Цвет рамы и маятника (по умолчанию — карбон).")]
        public Color frameColor = new Color(0.2f, 0.21f, 0.24f);

        [Header("Цвета гонщика")]
        [Tooltip("Джерси (свободное, с длинным рукавом; светлые полосы и гидропак — поверх, свои).")]
        public Color jerseyColor = new Color(0.85f, 0.42f, 0.16f);
        [Tooltip("Штаны.")]
        public Color shortsColor = new Color(0.2f, 0.21f, 0.25f);
        [Tooltip("Основной цвет шлема (графика поверх — своя).")]
        public Color helmetColor = new Color(0.15f, 0.15f, 0.16f);   // матовый чёрный (шлем v11)
        [HideInInspector] public int lookVersion;   // 3 — гонщик v6 (реалистичный, объёмный свет); 4 — шлем v11 (матовый чёрный); старые ассеты получают новые цвета
        [Tooltip("Насколько темнее дальние рука и нога (за рамой) — для глубины.")]
        [Range(0.2f, 1f)] public float farSideShade = 0.58f;

        [Header("Байк")]
        [Tooltip("Центр = ось колеса.")]
        public Sprite wheel;
        [Tooltip("Центр = начало координат рамы (середина между осями).")]
        public Layered frame = new Layered();
        [Tooltip("Трещины на раме (центр — как у рамы): появляются, когда байк сломан от сильного удара.")]
        public Sprite frameCrack;
        [Tooltip("Рама, разломанная пополам (центр — как у рамы): задняя часть и передняя (рулевой стакан с обломками труб). " +
                 "Показываются вместо рамы, когда она ломается от сильного удара. Нет картинок — вместо разлома видны трещины.")]
        public Layered frameRear = new Layered(), frameFront = new Layered();
        [Tooltip("Центр = точка поворота маятника.")]
        public Layered swingarm = new Layered();
        [Tooltip("Центр = корона вилки, ноги смотрят вниз (-Y).")]
        public Sprite forkUpper;
        [Tooltip("Центр = ось колеса, смотрит вверх (+Y).")]
        public Sprite forkLower;
        [Tooltip("Центр = каретка, шатун вдоль +X.")]
        public Sprite crankNear;
        public Sprite crankFar;

        [Header("Гонщик (центр = сустав, сегмент идёт вдоль +X)")]
        public Layered torso = new Layered();      // таз → плечо
        [Tooltip("Таз в штанах (ягодицы, низ живота), центр = тазобедренный сустав, в системе корпуса. Под джерси, поверх бедра; " +
                 "к бёдрам плавно прозрачный — переход от тела к ногам без шва.")]
        public Layered pelvis = new Layered();
        public Layered thigh = new Layered();      // бедро → колено
        public Layered shin = new Layered();       // колено → голеностоп (штанина до щиколотки)
        public Layered upperArm = new Layered();   // плечо → локоть
        public Layered forearm = new Layered();    // локоть → кисть (рукав, перчатка на руле)
        [Tooltip("Нога целиком, прямая: таз (центр картинки) → колено → щиколотка вдоль +X, перёд ноги — +Y. Если задана, " +
                 "нога гнётся в колене плавно (SkinnedLimb), а Thigh/Shin не нужны.")]
        public Layered leg = new Layered();
        [Tooltip("Рука целиком, прямая: плечо (центр картинки) → локоть → кисть на грипсе вдоль +X, сгиб локтя — в +Y.")]
        public Layered arm = new Layered();
        [Tooltip("Центр = педаль, ступня горизонтально.")]
        public Layered shoe = new Layered();
        [Tooltip("Центр = центр головы, лицом вправо (+X).")]
        public Layered head = new Layered();
    }
}
