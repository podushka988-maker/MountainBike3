using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Пылинки и пыльца, медленно плывущие в вечернем свете перед камерой — «воздух» в кадре.
    /// Висит на камере; система частиц создаётся из кода. Частицы живут в мире (не прилипают к экрану),
    /// поэтому при движении байка проплывают мимо — добавляет ощущение глубины и скорости.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AmbientMotes : MonoBehaviour
    {
        [Tooltip("Материал частиц (Art/FX/mote.png).")]
        public Material moteMaterial;
        public Color color = new Color(1f, 0.92f, 0.72f, 0.55f);
        [Tooltip("Частиц в секунду.")]
        public float rate = 14f;
        public int sortingOrder = 9;

        private ParticleSystem _ps;
        private Camera _cam;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            var go = new GameObject("Ambient Motes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 10f);   // перед камерой, на плоскости игры
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = true;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = color;
            main.maxParticles = 300;
            main.prewarm = true;

            var em = _ps.emission;
            em.rateOverTime = rate;
            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_cam.orthographicSize * _cam.aspect * 2.6f, _cam.orthographicSize * 2.4f, 0.1f);

            // мерцают и плавают (лёгкий шум как ветерок)
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.2f;
            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);        // ветер слева направо
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.08f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = sortingOrder;
            if (moteMaterial != null) r.sharedMaterial = moteMaterial;
            _ps.Play();
        }

        private void LateUpdate()
        {
            if (_ps == null) return;
            // область появления следует за зумом камеры
            var shape = _ps.shape;
            shape.scale = new Vector3(_cam.orthographicSize * _cam.aspect * 2.6f, _cam.orthographicSize * 2.4f, 0.1f);
        }
    }
}
