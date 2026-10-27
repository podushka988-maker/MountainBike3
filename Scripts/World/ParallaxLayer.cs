using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Бесконечный слой фона с параллаксом: картинка повторяется по горизонтали (3 копии, перекладываются за камерой).
    ///   Follow X — насколько слой «едет» за камерой: 1 — стоит на месте экрана (бесконечно далеко), 0 — как трасса.
    ///   Follow Y — то же по вертикали: дальние слои ≈ 1 (горизонт на уровне глаз), ближние меньше — опускаются, когда едем в гору.
    /// Под слоем — заливка цветом его нижнего края, плавно переходящая в дымку долины (Fill Haze Color).
    /// Высота слоя считается не от старта, а от сглаженной высоты трассы вокруг камеры: на длинных спусках и в каньонах фон
    /// опускается вместе с трассой, параллакс остаётся на местных холмах и прыжках. И низ картинки никогда не поднимается
    /// выше нижнего края экрана (при любом зуме), поэтому заливка под слоем на экран не попадает.
    /// Висит на объекте слоя; создаётся сборщиком сцены.
    /// </summary>
    [DefaultExecutionOrder(100)]   // после CameraFollow2D — без дрожания
    public class ParallaxLayer : MonoBehaviour
    {
        public Transform cameraTransform;
        public Sprite sprite;
        public Material material;
        public int sortingOrder = -50;

        [Range(0f, 1f)] public float followX = 0.9f;
        [Range(0f, 1f)] public float followY = 1f;
        [Tooltip("Центр слоя относительно камеры по вертикали, м.")]
        public float offsetY = 1.77f;
        [Tooltip("Насколько слой может уехать по вертикали относительно камеры, м (чтобы на больших спусках не закрыл небо).")]
        public float maxDriftY = 4f;
        [Tooltip("Собственное движение слоя (ветер гонит туман), м/с.")]
        public float driftSpeed = 0f;
        [Tooltip("Заливка под слоем цветом его нижнего края. Прозрачный цвет — без заливки.")]
        public Color fillColor = Color.clear;
        [Tooltip("Заливка НАД слоем цветом его верхнего края (для неба). Прозрачный — без заливки.")]
        public Color fillTopColor = Color.clear;
        [Tooltip("Насколько слой масштабируется вместе с зумом камеры: 1 — как бесконечно далёкий (на экране не меняется), " +
                 "0 — как трасса. Отрицательное — как Follow X. Без этого при отдалении камеры видно края фона.")]
        public float zoomFollow = -1f;
        public Color tint = Color.white;
        [Header("Низ слоя")]
        [Tooltip("Трасса: высота фона считается от её сглаженной высоты вокруг камеры (пусто — найдётся сама).")]
        public TerrainGenerator terrain;
        [Tooltip("За сколько секунд фон «догоняет» высоту трассы (долгий спуск — опускается вместе с ней).")]
        public float groundFollowTime = 3f;
        [Tooltip("Низ картинки слоя никогда не поднимается выше нижнего края экрана (заливку под ним не видно).")]
        public bool keepBottomOffscreen = true;
        [Tooltip("Заливка под слоем плавно переходит в этот цвет — дымку долины (прозрачный — заливка одного цвета).")]
        public Color fillHazeColor = Color.clear;
        [Tooltip("На скольких метрах под слоем цвет заливки переходит в дымку.")]
        public float fillFadeHeight = 5f;
        [Tooltip("Плавность, с которой слой опускается, чтобы не открылся его низ (впадина въехала в кадр), и возвращается обратно, с.")]
        public float pushSmoothDown = 0.5f, pushSmoothUp = 1.2f;
        [Tooltip("Насколько слой можно опустить, м, и с какой высоты камеры над землёй (м) это плавно отключается (за Push Fade Range м).")]
        public float maxPush = 3f, pushFadeFrom = 3f, pushFadeRange = 5f;

        private SpriteRenderer[] _tiles;
        private Transform _fill, _fillTop;
        private SpriteRenderer _fillSr, _fillTopSr;
        private Camera _cam;
        private float _refSize;
        private float _width, _height, _refY, _drift;
        private static Sprite _white;
        private float _ground0, _groundRef;
        private bool _hasGround;
        private float _push, _pushVel;
        private static int _groundFrame = -1;
        private static float _groundNow;

        private void Start()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (sprite == null || cameraTransform == null) { enabled = false; return; }
            _width = sprite.bounds.size.x;
            _height = sprite.bounds.size.y;
            _refY = cameraTransform.position.y;
            _cam = cameraTransform.GetComponent<Camera>();
            _refSize = _cam != null && _cam.orthographic ? _cam.orthographicSize : 1f;
            if (zoomFollow < 0f) zoomFollow = followX;
            _tiles = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Tile " + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                if (material != null) sr.sharedMaterial = material;
                sr.sortingOrder = sortingOrder;
                sr.color = tint;
                _tiles[i] = sr;
            }
            if (terrain == null) terrain = FindAnyObjectByType<TerrainGenerator>();
            if (fillColor.a > 0f)
            {
                if (_white == null)
                    _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 1f), 4f);
                var go = new GameObject("Fill");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = fillHazeColor.a > 0f ? HazeFillSprite() : _white;
                if (material != null) sr.sharedMaterial = material;
                sr.sortingOrder = sortingOrder;
                sr.color = fillHazeColor.a > 0f ? Color.white : fillColor;
                go.transform.localScale = new Vector3(_width * 3f, 60f, 1f);   // спрайт 1×1 м, опора сверху
                _fill = go.transform; _fillSr = sr;
            }
            if (fillTopColor.a > 0f)
            {
                var top = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0f), 4f);
                var go = new GameObject("FillTop");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = top;
                if (material != null) sr.sharedMaterial = material;
                sr.sortingOrder = sortingOrder;
                sr.color = fillTopColor;
                go.transform.localScale = new Vector3(_width * 3f, 60f, 1f);   // опора снизу — заливка уходит вверх
                _fillTop = go.transform; _fillTopSr = sr;
            }
            LateUpdate();
        }

        /// <summary>Средняя высота трассы вокруг камеры (±60 м, плавные веса) — одна на кадр для всех слоёв.</summary>
        private static float GroundAround(TerrainGenerator t, float x)
        {
            if (_groundFrame == Time.frameCount) return _groundNow;
            float sum = 0f;
            // широкое окно с плавными весами (края весят меньше) — гора, въехавшая в окно, меняет среднее постепенно
            float wsum = 0f;
            for (int i = -10; i <= 10; i++)
            {
                float w = 1f - Mathf.Abs(i) / 11f;
                sum += t.HeightAt(x + i * 6f) * w; wsum += w;
            }
            _groundNow = sum / wsum; _groundFrame = Time.frameCount;
            return _groundNow;
        }

        private static float Tanh(float x) { float e = Mathf.Exp(-2f * Mathf.Abs(x)); return Mathf.Sign(x) * (1f - e) / (1f + e); }

        private static int _lowFrame = -1;
        private static float _lowNow;

        /// <summary>Самая низкая точка трассы в кадре (шаг 2 м) — одна на кадр для всех слоёв.</summary>
        private static float LowestGround(TerrainGenerator t, float x, float halfW)
        {
            if (_lowFrame == Time.frameCount) return _lowNow;
            float lo = float.MaxValue;
            // точки — на сетке мира (не «ездят» вместе с камерой: минимум не скачет от кадра к кадру)
            for (float sx = Mathf.Floor((x - halfW) / 2f) * 2f; sx <= x + halfW + 2f; sx += 2f) lo = Mathf.Min(lo, t.HeightAt(sx));
            _lowNow = lo; _lowFrame = Time.frameCount;
            return lo;
        }

        /// <summary>Текстура заливки: сверху — цвет нижнего края слоя, за Fill Fade Height метров плавно переходит в дымку.</summary>
        private Sprite HazeFillSprite()
        {
            const int n = 128; const float total = 60f;                         // квадрат n×n пикселей = 1×1 м; заливка — 60 м (масштаб объекта)
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var row = new Color[n];
            for (int r = 0; r < n; r++)
            {
                float depth = (1f - (r + 0.5f) / n) * total;                     // от верха заливки вниз, м
                float t = Mathf.Clamp01(depth / Mathf.Max(0.1f, fillFadeHeight));
                Color c = Color.Lerp(fillColor, fillHazeColor, t * t * (3f - 2f * t)); c.a = 1f;
                for (int i = 0; i < n; i++) row[i] = c;
                tex.SetPixels(0, r, n, 1, row);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 1f), n);   // 1 × 1 м (масштабируется как раньше)
        }

        /// <summary>
        /// Свет времени суток (DayNightCycle): картинка и заливка снизу умножаются на light; заливка над слоем (небо) —
        /// цветом top (прозрачный — тоже умножается на light).
        /// </summary>
        public void SetTint(Color light, Color top)
        {
            if (_tiles == null) return;
            Color c = tint * light; c.a = tint.a;
            foreach (SpriteRenderer sr in _tiles) sr.color = c;
            if (_fillSr != null)
            {
                Color f = fillHazeColor.a > 0f ? light : fillColor * light;   // переход в дымку нарисован в текстуре — только свет
                f.a = fillHazeColor.a > 0f ? 1f : fillColor.a; _fillSr.color = f;
            }
            if (_fillTopSr != null)
            {
                Color f = top.a > 0f ? top : fillTopColor * light;
                f.a = fillTopColor.a; _fillTopSr.color = f;
            }
        }

        private void LateUpdate()
        {
            if (_tiles == null) return;
            Vector3 cam = cameraTransform.position;
            _drift += driftSpeed * Time.deltaTime;

            // зум: далёкий слой на экране не уменьшается, когда камера отдаляется (иначе видно его края)
            float zoom = _cam != null && _cam.orthographic && _refSize > 0f ? _cam.orthographicSize / _refSize : 1f;
            float sc = Mathf.Lerp(1f, zoom, zoomFollow);
            transform.localScale = new Vector3(sc, sc, 1f);
            // где слой относительно камеры — в его собственных метрах (без зума), и только потом масштаб вокруг камеры.
            // Раньше копия выбиралась по ширине С зумом: при отдалении/приближении камеры номер копии k (на 2 км трассы —
            // десятки) умножался на изменившуюся ширину, и слой съезжал на десятки метров туда-сюда (особенно ближний лес)
            float originX = cam.x * followX + _drift;
            int k = Mathf.FloorToInt((cam.x - originX) / _width + 0.5f);
            float dx = originX + k * _width - cam.x;                           // от −ширина/2 до +ширина/2
            // высота фона — от сглаженной высоты трассы вокруг камеры (а не от старта): долгий спуск фон не «поднимает»
            float refY = _refY;
            if (terrain != null)
            {
                float g = GroundAround(terrain, cam.x);
                if (!_hasGround) { _ground0 = _groundRef = g; _hasGround = true; }
                _groundRef += (g - _groundRef) * Mathf.Min(1f, Time.deltaTime / Mathf.Max(0.05f, groundFollowTime));
                refY = _refY + (_groundRef - _ground0);
            }
            // параллакс по высоте: слой отстаёт от камеры на (1 − Follow Y). Предел — мягкий (tanh): раньше при большом отрыве
            // от земли (высокий прыжок) смещение упиралось в жёсткий предел, и слой резко «прилипал» к камере — дёргался
            float raw = (cam.y - refY) * (followY - 1f);
            float drift = maxDriftY > 0f ? maxDriftY * Tanh(raw / maxDriftY) : raw;
            float y = cam.y + (offsetY + drift) * sc;
            // низ картинки не выше самой низкой видимой точки фона: это самая низкая точка трассы в кадре (под ней — земля),
            // но не ниже нижнего края экрана. Так заливку под слоем не видно ни при каком зуме и спуске, а на обычной трассе
            // (низ картинки и так под землёй) слой не трогаем — лес не прячется за землю
            if (keepBottomOffscreen && fillColor.a > 0f && _cam != null && _cam.orthographic)
            {
                float halfH = _cam.orthographicSize, halfW = halfH * _cam.aspect;
                float lowest = Mathf.Max(cam.y - halfH, terrain != null ? LowestGround(terrain, cam.x, halfW) : cam.y - halfH) - 0.3f;
                float bottom = y - _height * 0.5f * sc;
                // плавно: впадина, въехавшая в кадр, не дёргает слой — он опускается и возвращается за ~0.8 с
                float need = Mathf.Max(0f, bottom - lowest);
                // высоко над землёй (большой прыжок, дроп) слой не прижимаем к земле: иначе он «стоит» в мире, пока камера
                // летит вверх, и на экране резко уезжает вниз, а после приземления — обратно. Там низ слоя уходит в дымку
                // долины (Fill Haze Color) — это и выглядит естественно. Прижимаем только у земли и не больше Max Push.
                float above = terrain != null ? cam.y - terrain.HeightAt(cam.x) : 0f;
                float t = Mathf.Clamp01((above - pushFadeFrom) / Mathf.Max(0.1f, pushFadeRange));
                need = Mathf.Min(need, maxPush) * (1f - t * t * (3f - 2f * t));
                _push = Mathf.SmoothDamp(_push, need, ref _pushVel, need > _push ? pushSmoothDown : pushSmoothUp);
            }
            else _push = Mathf.SmoothDamp(_push, 0f, ref _pushVel, pushSmoothUp);
            y -= _push;
            transform.position = new Vector3(cam.x + dx * sc, y, 0f);
            for (int i = 0; i < 3; i++)
                _tiles[i].transform.localPosition = new Vector3((i - 1) * _width, 0f, 0f);
            if (_fill != null) _fill.localPosition = new Vector3(0f, -_height * 0.5f + 0.02f, 0f);
            if (_fillTop != null) _fillTop.localPosition = new Vector3(0f, _height * 0.5f - 0.02f, 0f);
        }
    }
}
