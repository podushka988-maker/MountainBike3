using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Фон: ветер в кронах и редкие птицы (бесшовная петля forest_loop). На большой скорости фон чуть тише —
    /// его заглушает ветер в ушах, как в жизни. Висит на камере.
    /// </summary>
    public class AmbientAudio : MonoBehaviour
    {
        public AudioClip forest;
        public BikeController2D bike;
        [Range(0f, 1f)] public float volume = 0.4f;
        [Tooltip("Насколько тише фон на полной скорости (0 — не тише).")]
        [Range(0f, 1f)] public float duckAtSpeed = 0.45f;
        public float fullSpeed = 14f;

        private AudioSource _src;

        private void Start()
        {
            if (forest == null) return;
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = forest; _src.loop = true; _src.spatialBlend = 0f; _src.volume = 0f;
            _src.time = Random.Range(0f, forest.length * 0.9f);
            _src.Play();
        }

        private void Update()
        {
            if (_src == null) return;
            float k = bike != null ? Mathf.Clamp01(bike.Speed / fullSpeed) : 0f;
            float target = volume * (1f - duckAtSpeed * k);
            _src.volume = Mathf.MoveTowards(_src.volume, target, Time.deltaTime / 1.5f);   // плавно появляется в начале заезда
        }
    }
}
