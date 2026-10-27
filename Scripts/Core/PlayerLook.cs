using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Выбранные в магазине цвета экипировки (джерси, штаны, шлем) — хранятся в PlayerPrefs и применяются к гонщику
    /// и в меню, и в игре. Пусто — цвета из BikeSkin.
    /// </summary>
    public static class PlayerLook
    {
        public const string JerseyKey = "look_jersey", PantsKey = "look_pants", HelmetKey = "look_helmet";

        public static Color Get(string key, Color fallback)
        {
            string s = PlayerPrefs.GetString(key, "");
            return !string.IsNullOrEmpty(s) && ColorUtility.TryParseHtmlString("#" + s, out Color c) ? c : fallback;
        }

        public static bool Has(string key) => !string.IsNullOrEmpty(PlayerPrefs.GetString(key, ""));

        public static void Set(string key, Color c)
        {
            PlayerPrefs.SetString(key, ColorUtility.ToHtmlStringRGB(c));
            PlayerPrefs.Save();
        }
    }
}
