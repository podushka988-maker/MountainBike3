using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Закатное солнце: диск и ореол в небе, дымка-засветка перед горами и лучи, пробивающиеся сквозь лес.
    /// Солнце бесконечно далеко — на экране стоит на месте и не меняет размер при зуме камеры.
    /// Свет на гонщике (BikeVisual.sunDirection) всегда идёт от солнца на экране.
    /// Висит на камере; спрайты создаются из кода (Art/FX/sun_*.png).
    /// </summary>
    [DefaultExecutionOrder(110)]   // после камеры и параллакса
    public class SunLight : MonoBehaviour
    {
        public Sprite disc, glow, rays;
        public Material material;
        [Tooltip("Где солнце на экране (доли ширины и высоты).")]
        public Vector2 viewport = new Vector2(0.2f, 0.8f);
        [Tooltip("Размеры относительно высоты кадра: диск, ореол, лучи.")]
        public float discSize = 0.1f, glowSize = 1.1f, raysSize = 1.7f;
        [Tooltip("Яркость: ореол в небе, дымка перед горами, лучи.")]
        [Range(0f, 1f)] public float glowAlpha = 0.85f, hazeAlpha = 0.35f, raysAlpha = 0.6f;
        public int skyOrder = -96, hazeOrder = -64, raysOrder = -58;

        private Camera _cam;
        private SpriteRenderer _disc, _glow, _haze, _rays;
        private BikeVisual[] _riders;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            _glow = Make("Sun Glow", glow, skyOrder, new Color(1f, 1f, 1f, glowAlpha));
            _disc = Make("Sun Disc", disc, skyOrder + 1, Color.white);
            _haze = Make("Sun Haze", glow, hazeOrder, new Color(1f, 0.95f, 0.9f, hazeAlpha));
            _rays = Make("Sun Rays", rays, raysOrder, new Color(1f, 1f, 1f, raysAlpha));
#if UNITY_2023_1_OR_NEWER
            _riders = FindObjectsByType<BikeVisual>(FindObjectsSortMode.None);
#else
            _riders = FindObjectsOfType<BikeVisual>();
#endif
        }

        private SpriteRenderer Make(string name, Sprite sprite, int order, Color color)
        {
            if (sprite == null) return null;
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sortingOrder = order; sr.color = color;
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            float h = 2f * _cam.orthographicSize;
            Vector3 sun = _cam.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, -_cam.transform.position.z));
            sun.z = 0f;
            Place(_glow, sun, glowSize * h, 0f);
            Place(_disc, sun, discSize * h, 0f);
            Place(_haze, sun + new Vector3(0f, -0.15f * h, 0f), glowSize * 1.4f * h, 0f);
            if (_rays != null)
            {
                // веер лучей из угла картинки: угол — у солнца; лучи чуть колышутся и «дышат»
                float t = Time.time;
                float size = raysSize * h;
                float sway = Mathf.Sin(t * 0.21f) * 1.5f + Mathf.Sin(t * 0.53f) * 0.6f;
                _rays.transform.rotation = Quaternion.Euler(0f, 0f, sway);
                float k = size / Mathf.Max(0.01f, _rays.sprite.bounds.size.x);
                _rays.transform.localScale = new Vector3(k, k, 1f);
                _rays.transform.position = sun + _rays.transform.rotation * new Vector3(size * 0.5f, -size * 0.5f, 0f);
                var c = _rays.color; c.a = raysAlpha * (0.8f + 0.2f * Mathf.Sin(t * 0.37f)); _rays.color = c;
            }
            // свет на гонщике — от солнца
            if (_riders != null)
                foreach (BikeVisual r in _riders)
                    if (r != null && r.bike != null && r.bike.frame != null) r.sunDirection = ((Vector2)sun - r.bike.frame.position).normalized;
        }

        private static void Place(SpriteRenderer sr, Vector3 pos, float size, float angle)
        {
            if (sr == null) return;
            sr.transform.position = pos;
            float k = size / Mathf.Max(0.01f, sr.sprite.bounds.size.x);
            sr.transform.localScale = new Vector3(k, k, 1f);
        }
    }
}
