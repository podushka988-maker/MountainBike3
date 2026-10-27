using UnityEngine;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>Образец цвета в магазине: нажали — MainMenu сохраняет цвет (PlayerLook) и перекрашивает гонщика.</summary>
    [RequireComponent(typeof(Button))]
    public class ColorSwatch : MonoBehaviour
    {
        public string key;
        public Color color = Color.white;
        [Tooltip("Рамка: обычная / выбранная.")]
        public Image frame;
        [HideInInspector] public MainMenu menu;

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(() => { if (menu != null) menu.Pick(key, color); });
        }
    }
}
