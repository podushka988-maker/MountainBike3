using UnityEngine;
using UnityEngine.UI;

namespace BikeGame
{
    /// <summary>
    /// Панель локации в ленте выбора (после «ИГРАТЬ»): картинка местности на весь экран и табличка с названием в левом
    /// нижнем углу. Нажатие — сразу заезд по этой локации. Создаёт сборщик меню.
    /// Название — обычный текст (Label): поменять можно в Locations.Titles (потом Bike → Создать сцену Free Ride)
    /// или прямо в сцене MainMenu: Canvas → Locations → Viewport → Content → Location … → Board → Label.
    /// </summary>
    public class LocationCard : MonoBehaviour
    {
        public string id;
        [Tooltip("Картинка локации (кнопка).")]
        public Image card;
        [Tooltip("Название на табличке.")]
        public Text label;
        [Tooltip("Отметка последней выбранной локации (необязательно).")]
        public GameObject selectedFrame;
        [HideInInspector] public MainMenu menu;

        private void Awake()
        {
            Button b = card != null ? card.GetComponent<Button>() : GetComponent<Button>();
            if (b != null) b.onClick.AddListener(Pick);
        }

        public void Pick()
        {
            if (menu != null) menu.PickLocation(id);
        }

        public void SetSelected(bool selected)
        {
            if (selectedFrame != null) selectedFrame.SetActive(selected);
        }
    }
}
