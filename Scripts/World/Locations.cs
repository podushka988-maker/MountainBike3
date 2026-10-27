using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Локации трассы. Игрок выбирает её в главном меню (после «ИГРАТЬ»), выбор хранится в PlayerPrefs и читается сценой игры:
    /// фон (6 слоёв параллакса), земля, трава/снег, кусты у трассы и погода (листопад, снегопад, пыль, туман) — BackgroundThemes.
    /// </summary>
    public static class Locations
    {
        public const string PrefKey = "Location";
        /// <summary>Порядок карточек в меню.</summary>
        public static readonly string[] Ids = { "alpine", "canyon", "misty", "autumn", "winter" };
        public static readonly string[] Titles = { "ALPS", "CANYON", "FOG", "AUTUMN", "WINTER" };

        /// <summary>Выбранная локация (id). По умолчанию — Альпы.</summary>
        public static string Selected
        {
            get => PlayerPrefs.GetString(PrefKey, Ids[0]);
            set { PlayerPrefs.SetString(PrefKey, value); PlayerPrefs.Save(); }
        }

        public static string Title(string id)
        {
            int i = System.Array.IndexOf(Ids, id);
            return i >= 0 ? Titles[i] : id;
        }
    }
}
