using System;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Монеты игрока и купленные байки — хранятся в PlayerPrefs, общие для меню и игры.
    /// Экономика: за каждые 10 м — 1 очко, сальто назад — 10 очков, вперёд — 15; в конце заезда монет = очки / 2
    /// (с округлением вверх).
    /// </summary>
    public static class PlayerWallet
    {
        public const string CoinsKey = "wallet_coins";
        private const string OwnedPrefix = "bike_owned_";

        /// <summary>Очков за каждые столько метров.</summary>
        public const float MetersPerPoint = 10f;
        public const int BackflipPoints = 10, FrontflipPoints = 15;

        /// <summary>Баланс изменился: (было, стало).</summary>
        public static event Action<int, int> Changed;

        public static int Coins => Mathf.Max(0, PlayerPrefs.GetInt(CoinsKey, 0));

        /// <summary>Монеты за очки заезда: половина, с округлением вверх.</summary>
        public static int CoinsForPoints(int points) => points <= 0 ? 0 : (points + 1) / 2;

        public static int DistancePoints(float meters) => Mathf.Max(0, Mathf.FloorToInt(meters / MetersPerPoint));

        public static void Add(int amount)
        {
            if (amount <= 0) return;
            int before = Coins;
            Set(before + amount);
            Changed?.Invoke(before, Coins);
        }

        /// <summary>Списать монеты, если хватает. false — не хватает (ничего не списано).</summary>
        public static bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            int before = Coins;
            if (before < amount) return false;
            Set(before - amount);
            Changed?.Invoke(before, Coins);
            return true;
        }

        public static bool Owns(string bikeId) => !string.IsNullOrEmpty(bikeId) && PlayerPrefs.GetInt(OwnedPrefix + bikeId, 0) == 1;

        public static void SetOwned(string bikeId)
        {
            if (string.IsNullOrEmpty(bikeId)) return;
            PlayerPrefs.SetInt(OwnedPrefix + bikeId, 1);
            PlayerPrefs.Save();
        }

        /// <summary>1500 → «1 500».</summary>
        public static string Format(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

        private static void Set(int value)
        {
            PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }
}
