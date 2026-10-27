using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Байки магазина: у каждого свои настройки физики (BikeConfig), внешний вид (BikeSkin) и цена в монетах.
    /// До того, как байк соберёт физику и спрайты, подставляет выбранный (PlayerBike), если он куплен; иначе — первый
    /// (бесплатный). Висит на объекте байка; заполняет сборщик сцены. Порядок в списке = порядок карточек в магазине.
    /// </summary>
    [DefaultExecutionOrder(-1000)]   // раньше BikeController2D / RiderBody / BikeVisual
    public class BikeChoice : MonoBehaviour
    {
        [System.Serializable]
        public class Model
        {
            [Tooltip("Постоянный идентификатор (по нему хранится выбор и покупка) — не меняйте после выхода игры.")]
            public string id = "bike";
            public string displayName = "Bike";
            [TextArea] public string description = "";
            [Tooltip("Цена в монетах. 0 — бесплатный (всегда есть у игрока).")]
            public int price;
            public BikeConfig config;
            public BikeSkin skin;
            [Tooltip("Картинка для карточки в магазине.")]
            public Sprite preview;

            public bool Owned => price <= 0 || PlayerWallet.Owns(id);
        }

        public Model[] models = new Model[0];

        /// <summary>Выбранная модель (если не куплена или не найдена — первая).</summary>
        public Model Current => Resolve(models);

        public static Model Resolve(Model[] list)
        {
            if (list == null || list.Length == 0) return null;
            string id = PlayerBike.Id;
            foreach (Model m in list)
                if (m != null && m.id == id && m.Owned) return m;
            foreach (Model m in list)
                if (m != null && m.price <= 0) return m;
            return list[0];
        }

        private void Awake()
        {
            Model m = Current;
            if (m == null) return;
            var bike = GetComponent<BikeController2D>();
            var visual = GetComponent<BikeVisual>();
            if (bike != null && m.config != null) bike.config = m.config;
            if (visual != null && m.skin != null) visual.skin = m.skin;
        }
    }
}
