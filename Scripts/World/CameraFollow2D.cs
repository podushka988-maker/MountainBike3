using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Камера следует за байком как оператор: смотрит немного вперёд по скорости,
    /// плавно отдаляется на большой скорости, чтобы было видно трассу впереди.
    /// В полёте отдаляется только в настоящем большом прыжке (долго в воздухе и высоко над землёй) — и плавно, по высоте.
    /// Мелкий отрыв колеса, кочка или волна камеру не трогают: она не «прыгает» вместе с байком.
    /// При жёстком приземлении встряхивается. Висит на Main Camera (Orthographic).
    /// Следит за самим гонщиком: на байке — за его телом, после падения — за куклой (куда бы её ни бросило),
    /// и никогда не выпускает его из кадра: гонщик всегда в центральной части экрана (Keep In View), даже на большой
    /// скорости, в огромном прыжке или когда рама разломалась и тело улетело от байка.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        public BikeController2D bike;
        [Tooltip("Трасса: по высоте над землёй камера отдаляется в больших прыжках (чтобы было видно приземление).")]
        public TerrainGenerator terrain;

        [Header("Положение")]
        public Vector2 offset = new Vector2(1.45f, 0.75f);
        [Tooltip("На сколько секунд вперёд по скорости смотреть.")]
        public float lookAhead = 0.35f;
        public float maxLookAhead = 2.3f;
        [Tooltip("Плавность слежения по горизонтали, с.")]
        public float smoothTime = 0.18f;
        [Tooltip("Плавность слежения по вертикали, с: больше — камера не дёргается на кочках и мелких отрывах.")]
        public float smoothTimeY = 0.3f;
        [Tooltip("Скорость байка для взгляда вперёд сглаживается за столько секунд (удары о землю не дёргают кадр).")]
        public float velocityFilter = 0.35f;
        [Tooltip("Гонщик всегда внутри этой доли кадра от центра (0.6 — не дальше 60% половины ширины и высоты): " +
                 "если плавное слежение отстаёт, камера догоняет сразу.")]
        [Range(0.2f, 0.95f)] public float keepInView = 0.6f;
        [Tooltip("Насколько центр тела гонщика выше точки слежения байка (рамы), м — чтобы кадр был как раньше.")]
        public float riderHeight = 0.75f;

        [Header("Зум")]
        [Tooltip("Обзор в покое (половина высоты кадра, м). Как в Mountain Bike Xtreme — издалека, видно трассу.")]
        public float baseSize = 4.2f;
        [Tooltip("Обзор на большой скорости.")]
        public float maxSize = 6.2f;
        [Tooltip("Скорость (м/с), при которой зум максимальный.")]
        public float speedForMaxZoom = 12f;
        [Tooltip("Отдаление в полёте начинается, только если байк в воздухе дольше стольких секунд (мелкие отрывы не считаются).")]
        public float airZoomDelay = 0.3f;
        [Tooltip("…и выше стольких метров над землёй.")]
        public float airZoomHeight = 1f;
        [Tooltip("На сколько отдаляться за каждый метр высоты над землёй в полёте.")]
        public float heightZoom = 0.4f;
        [Tooltip("Наибольшее отдаление в полёте (м).")]
        public float maxHeightZoom = 4.5f;
        [Tooltip("Плавность зума: отдаление, с.")]
        public float zoomOutTime = 0.6f;
        [Tooltip("Плавность зума: приближение после приземления, с (медленнее — без «рывка» при касании земли).")]
        public float zoomInTime = 1.2f;

        private float _shake, _shakeTime, _shakeLeft;
        private Vector3 _shakeOffset;

        private Camera _cam;
        private BikeVisual _visual;
        private float _velX, _velY, _zoomVel, _airTime;
        private Vector2 _vSmooth;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = baseSize;
            if (bike != null) { _visual = bike.GetComponent<BikeVisual>(); _vSmooth = bike.Velocity; transform.position = TargetPosition(); }
        }

        /// <summary>Где гонщик (центр тела): на байке и после падения — кукла. Без картинки гонщика — рама.</summary>
        private Vector2 RiderPoint() => _visual != null ? _visual.RiderCenter : (Vector2)bike.frame.position + Vector2.up * riderHeight;

        /// <summary>Скорость гонщика: на байке — байка, после падения — куклы.</summary>
        private Vector2 RiderVelocity()
        {
            if (_visual != null && _visual.Ragdoll != null && _visual.Ragdoll.Torso != null) return _visual.Ragdoll.Torso.GetVelocity();
            return bike.Velocity;
        }

        private Vector3 TargetPosition()
        {
            Vector2 p = RiderPoint() - Vector2.up * riderHeight;
            Vector2 ahead = Vector2.ClampMagnitude(_vSmooth * lookAhead, maxLookAhead);
            ahead.y *= 0.4f;
            return new Vector3(p.x + offset.x + ahead.x, p.y + offset.y + ahead.y, transform.position.z);
        }

        private void LateUpdate()
        {
            if (bike == null) return;
            float dt = Time.deltaTime;
            _vSmooth += (RiderVelocity() - _vSmooth) * Mathf.Min(1f, dt / Mathf.Max(0.01f, velocityFilter));
            Vector2 rp = RiderPoint();
            bool flying = bike.IsCrashed
                ? terrain != null && rp.y - terrain.HeightAt(rp.x) > riderHeight + airZoomHeight    // тело летит после падения
                : !bike.IsOnGround;
            _airTime = flying ? _airTime + dt : 0f;

            transform.position -= _shakeOffset;                      // встряска не копится в положении камеры
            Vector3 target = TargetPosition(), pos = transform.position;
            pos.x = Mathf.SmoothDamp(pos.x, target.x, ref _velX, smoothTime);
            pos.y = Mathf.SmoothDamp(pos.y, target.y, ref _velY, smoothTimeY);
            // гонщик всегда в кадре: плавное слежение отстало — догоняем сразу (без рывка назад: скорость слежения сохраняется)
            float halfH = _cam.orthographicSize, halfW = halfH * _cam.aspect;
            float mx = halfW * keepInView, my = halfH * keepInView;
            if (rp.x > pos.x + mx) pos.x = rp.x - mx; else if (rp.x < pos.x - mx) pos.x = rp.x + mx;
            if (rp.y > pos.y + my) pos.y = rp.y - my; else if (rp.y < pos.y - my) pos.y = rp.y + my;
            transform.position = pos;

            float t = Mathf.Clamp01(_vSmooth.magnitude / speedForMaxZoom);
            float size = Mathf.Lerp(baseSize, maxSize, t * t);
            if (_airTime > 0f && terrain != null)
            {
                // настоящий прыжок: долго в воздухе и высоко над землёй — отдаляемся по высоте, чтобы приземление было в кадре
                Vector2 p = rp - Vector2.up * riderHeight;
                float h = p.y - terrain.HeightAt(p.x);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(airZoomDelay, airZoomDelay + 0.4f, _airTime));
                size += k * Mathf.Clamp((h - airZoomHeight) * heightZoom, 0f, maxHeightZoom);
            }
            float zt = size > _cam.orthographicSize ? zoomOutTime : zoomInTime;
            _cam.orthographicSize = Mathf.SmoothDamp(_cam.orthographicSize, size, ref _zoomVel, zt);

            // затухающая встряска (жёсткое приземление)
            _shakeOffset = Vector3.zero;
            if (_shakeLeft > 0f)
            {
                _shakeLeft -= dt;
                float a = _shake * Mathf.Clamp01(_shakeLeft / _shakeTime);
                float tt = Time.time * 38f;
                _shakeOffset = new Vector3((Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * 2f * a * 0.6f, (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * 2f * a, 0f);
                transform.position += _shakeOffset;
            }
        }

        /// <summary>Встряхнуть камеру: amplitude — размах (м), duration — сколько затухает (с).</summary>
        public void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0.001f) return;
            if (_shakeLeft > 0f && amplitude < _shake * _shakeLeft / _shakeTime) return;   // идёт встряска сильнее
            _shake = amplitude; _shakeTime = Mathf.Max(0.05f, duration); _shakeLeft = _shakeTime;
        }
    }
}
