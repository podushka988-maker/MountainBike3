using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Держит интерфейс внутри «безопасной зоны» экрана телефона: кнопки и надписи не уходят под вырез камеры
    /// и закруглённые углы. Висит на растянутом на весь экран RectTransform внутри Canvas (создаёт сборщик сцены).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeArea : MonoBehaviour
    {
        private RectTransform _rt;
        private Rect _lastArea;
        private Vector2Int _lastScreen;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            Apply();
        }

        private void Update()
        {
            if (Screen.safeArea != _lastArea || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y) Apply();
        }

        private void Apply()
        {
            _lastArea = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Vector2 min = _lastArea.position, max = _lastArea.position + _lastArea.size;
            min.x /= Screen.width; max.x /= Screen.width;
            min.y /= Screen.height; max.y /= Screen.height;
            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
