using System.Collections.Generic;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Изредка над лесом пролетает стайка птиц (вдалеке, между слоями фона): машут крыльями и планируют.
    /// Птицы «далеко», поэтому почти не смещаются при движении камеры (как дальние слои параллакса).
    /// Висит на камере; спрайты — Art/FX/bird_0..2 (крылья вверх, горизонтально, вниз).
    /// </summary>
    public class BirdFlock : MonoBehaviour
    {
        public Sprite[] frames;
        public Material material;
        [Tooltip("Цвет силуэтов (под вечерний свет).")]
        public Color color = new Color(0.25f, 0.2f, 0.28f, 0.85f);
        [Tooltip("Пауза между стаями, с (от и до).")]
        public Vector2 interval = new Vector2(12f, 35f);
        [Tooltip("Насколько птицы следуют за камерой (ближе к 1 — дальше от нас).")]
        [Range(0f, 1f)] public float follow = 0.88f;
        public int sortingOrder = -64;

        private class Bird { public Transform t; public SpriteRenderer sr; public Vector2 local; public float phase, rate, glide; }
        private readonly List<Bird> _birds = new List<Bird>();
        private Camera _cam;
        private float _next, _dir, _speed;
        private Vector3 _prevCam;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            _next = Time.time + Random.Range(4f, 10f);
            _prevCam = transform.position;
        }

        private void Update()
        {
            if (frames == null || frames.Length < 3 || _cam == null) return;
            float dt = Time.deltaTime;
            Vector3 camDelta = transform.position - _prevCam;
            _prevCam = transform.position;
            if (_birds.Count == 0 && Time.time > _next) Spawn();

            float h = _cam.orthographicSize, w = h * _cam.aspect;
            for (int i = _birds.Count - 1; i >= 0; i--)
            {
                Bird b = _birds[i];
                // мир «далеко»: камера сдвинулась на d — птица на экране смещается лишь на (1 − follow)·d
                b.local += new Vector2(_dir * _speed * dt, Mathf.Sin(Time.time * 0.7f + b.phase) * 0.05f * dt) - (Vector2)camDelta * (1f - follow) / Mathf.Max(0.5f, h / 4.2f);
                b.t.position = transform.position + new Vector3(b.local.x * h / 4.2f, b.local.y * h / 4.2f, 10f);
                b.t.localScale = new Vector3(_dir * 0.32f * h / 4.2f, 0.32f * h / 4.2f, 1f);
                // взмахи с паузами-планированием
                b.glide -= dt;
                if (b.glide < -1.2f) b.glide = Random.Range(0.4f, 1.5f);
                int frame = b.glide > 0f ? 1 : (int)Mathf.PingPong((Time.time + b.phase) * b.rate, 2.999f);
                b.sr.sprite = frames[frame];
                if (Mathf.Abs(b.local.x) > 4.2f * (w / h) + 3f) { Destroy(b.t.gameObject); _birds.RemoveAt(i); }
            }
            if (_birds.Count == 0 && _next < Time.time) _next = Time.time + Random.Range(interval.x, interval.y);
        }

        private void Spawn()
        {
            float aspect = _cam.aspect;
            _dir = Random.value < 0.5f ? -1f : 1f;
            _speed = Random.Range(0.7f, 1.3f);                        // экранных метров в секунду (при обзоре 4.2)
            int n = Random.Range(3, 8);
            float y0 = Random.Range(1.4f, 3.2f), x0 = -_dir * (4.2f * aspect + 1.5f);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Bird");
                go.transform.SetParent(transform, true);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = frames[1]; sr.color = color; sr.sortingOrder = sortingOrder;
                if (material != null) sr.sharedMaterial = material;
                // клин: вожак впереди, остальные уступом сзади
                float back = (i + 1) / 2 * 0.55f, side = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 0.22f;
                _birds.Add(new Bird
                {
                    t = go.transform, sr = sr,
                    local = new Vector2(x0 - _dir * back + Random.Range(-0.1f, 0.1f), y0 + side + Random.Range(-0.08f, 0.08f)),
                    phase = Random.value * 10f, rate = Random.Range(5f, 7.5f), glide = Random.Range(-1f, 0.5f),
                });
            }
        }
    }
}
