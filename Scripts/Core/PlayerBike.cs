using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Какой байк выбран в магазине — его id (BikeChoice.Model.id) хранится в PlayerPrefs, действует в меню и в игре.
    /// Пусто или байк не куплен — едем на первом (бесплатном) байке.
    /// </summary>
    public static class PlayerBike
    {
        public const string Key = "bike_id";
        public static string Id => PlayerPrefs.GetString(Key, "");

        public static void Set(string id)
        {
            PlayerPrefs.SetString(Key, id ?? "");
            PlayerPrefs.Save();
        }
    }
}
