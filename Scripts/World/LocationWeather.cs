using System.Collections.Generic;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Погода и «жизнь» локации (висит на камере, вид задаёт BackgroundThemes):
    ///   Fluff  — Альпы: пушинки одуванчиков плывут по ветру;
    ///   Leaves — осень: листопад (кружатся, переворачиваются; мелкие — за байком, крупные — перед камерой);
    ///   Snow   — зима: снегопад в три слоя (мелкий — перед дальним лесом, средний, крупные хлопья у камеры) и позёмка у земли;
    ///   Dust   — каньон: порывы пыли у земли и перекати-поле;
    ///   Mist   — туман: клочья тумана ползут над землёй, морось.
    /// Частицы живут в мире (не прилипают к экрану) — при езде проплывают мимо. Плотность — на квадратный метр кадра,
    /// поэтому при отдалении камеры частиц не становится меньше (с потолком).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LocationWeather : MonoBehaviour
    {
        public enum Kind { None, Fluff, Leaves, Snow, Dust, Mist }
        public Kind kind = Kind.None;
        [Range(0f, 2f)] public float intensity = 1f;
        [Tooltip("Ветер, м/с (слева направо, как пылинки и трава).")]
        public float wind = 0.7f;
        [Header("Материалы (Art/FX)")]
        public Material leafMaterial;      // leaf_sheet.png: 4 листа в ряд
        public Material snowMaterial;      // snowflake.png
        public Material fluffMaterial;     // fluff.png
        public Material dustMaterial;      // dust.png
        public Material fogMaterial;       // fog_wisp.png
        public Material dropMaterial;      // mote.png (капли вытягиваются по скорости)
        [Tooltip("Перекати-поле (каньон).")]
        public Sprite tumbleweed;
        public Material spriteMaterial;
        public TerrainGenerator terrain;

        private Camera _cam;
        private class Area { public ParticleSystem ps; public float density; public int max; }
        private readonly List<Area> _areas = new List<Area>();
        private class Ground { public ParticleSystem ps; public float perMeter; public Vector2 height; public float acc; }
        private readonly List<Ground> _grounds = new List<Ground>();
        private Transform _tw; private SpriteRenderer _twSr;
        private float _twR, _twVx, _twVy, _twNext;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            float k = intensity;
            switch (kind)
            {
                case Kind.Fluff:
                    AreaSystem("Fluff", fluffMaterial, 9, 0.012f * k, 60, new Vector2(7f, 11f), new Vector2(0.06f, 0.11f),
                               new Color(1f, 0.98f, 0.92f, 0.8f), new Color(1f, 0.94f, 0.82f, 0.6f), new Vector2(0.4f, 0.9f), new Vector2(-0.08f, 0.12f), 0.35f, 40f);
                    AreaSystem("Fluff Near", fluffMaterial, 70, 0.0015f * k, 10, new Vector2(5f, 8f), new Vector2(0.14f, 0.2f),
                               new Color(1f, 0.98f, 0.92f, 0.6f), new Color(1f, 0.94f, 0.82f, 0.45f), new Vector2(0.8f, 1.4f), new Vector2(-0.1f, 0.1f), 0.4f, 40f);
                    break;
                case Kind.Leaves:
                    Leaves("Falling Leaves", 3, 0.014f * k, 90, new Vector2(0.07f, 0.13f), new Color(0.92f, 0.9f, 0.88f, 1f));
                    Leaves("Falling Leaves Near", 70, 0.0022f * k, 18, new Vector2(0.15f, 0.24f), new Color(0.78f, 0.74f, 0.72f, 1f));
                    GroundSystem("Leaf Gusts", leafMaterial, 6, 0.05f * k, new Vector2(0.05f, 0.6f), new Vector2(2f, 3.5f), new Vector2(0.06f, 0.11f),
                                 new Color(0.9f, 0.88f, 0.85f, 1f), new Vector2(1.6f, 3f), new Vector2(0.2f, 0.9f), sheet: true);
                    break;
                case Kind.Snow:
                    AreaSystem("Snow Far", snowMaterial, -55, 0.3f * k, 900, new Vector2(7f, 10f), new Vector2(0.025f, 0.045f),
                               new Color(1f, 1f, 1f, 0.75f), new Color(0.92f, 0.94f, 1f, 0.55f), new Vector2(0.2f, 0.6f), new Vector2(-0.45f, -0.7f), 0.25f, 0f);
                    AreaSystem("Snow", snowMaterial, 9, 0.14f * k, 500, new Vector2(6f, 9f), new Vector2(0.045f, 0.075f),
                               new Color(1f, 1f, 1f, 0.9f), new Color(0.94f, 0.96f, 1f, 0.75f), new Vector2(0.3f, 0.9f), new Vector2(-0.6f, -1.0f), 0.35f, 0f);
                    AreaSystem("Snow Near", snowMaterial, 70, 0.012f * k, 60, new Vector2(4f, 6f), new Vector2(0.1f, 0.16f),
                               new Color(1f, 1f, 1f, 0.7f), new Color(0.94f, 0.96f, 1f, 0.5f), new Vector2(0.6f, 1.3f), new Vector2(-1.0f, -1.6f), 0.4f, 0f);
                    GroundSystem("Drifting Snow", dustMaterial, 6, 0.12f * k, new Vector2(0f, 0.35f), new Vector2(1.5f, 3f), new Vector2(0.8f, 2f),
                                 new Color(1f, 1f, 1f, 0.16f), new Vector2(2f, 4f), new Vector2(0.05f, 0.3f));
                    break;
                case Kind.Dust:
                    GroundSystem("Dust Gusts", dustMaterial, 6, 0.09f * k, new Vector2(0f, 0.6f), new Vector2(3f, 6f), new Vector2(1.5f, 4.5f),
                                 new Color(0.86f, 0.6f, 0.44f, 0.14f), new Vector2(1.5f, 3.2f), new Vector2(0.05f, 0.35f));
                    GroundSystem("Dust Near", dustMaterial, 72, 0.015f * k, new Vector2(0f, 0.4f), new Vector2(2.5f, 4f), new Vector2(2.5f, 5f),
                                 new Color(0.86f, 0.62f, 0.46f, 0.09f), new Vector2(2f, 3.5f), new Vector2(0.05f, 0.25f));
                    if (tumbleweed != null) MakeTumbleweed();
                    break;
                case Kind.Mist:
                    GroundSystem("Mist", fogMaterial, 6, 0.06f * k, new Vector2(0.2f, 2.2f), new Vector2(8f, 14f), new Vector2(3f, 7f),
                                 new Color(0.82f, 0.86f, 0.88f, 0.26f), new Vector2(0.2f, 0.6f), new Vector2(-0.02f, 0.05f), aspect: 2f);
                    GroundSystem("Mist Near", fogMaterial, 72, 0.012f * k, new Vector2(0f, 1.2f), new Vector2(8f, 12f), new Vector2(4f, 8f),
                                 new Color(0.84f, 0.88f, 0.9f, 0.13f), new Vector2(0.3f, 0.7f), new Vector2(-0.02f, 0.04f), aspect: 2f);
                    Drizzle("Drizzle", 9, 0.35f * k, 400, new Color(0.82f, 0.88f, 0.95f, 0.32f), new Vector2(0.012f, 0.018f));
                    Drizzle("Drizzle Near", 70, 0.03f * k, 60, new Color(0.82f, 0.88f, 0.95f, 0.22f), new Vector2(0.025f, 0.035f));
                    break;
            }
            LateUpdate();
        }

        // ------------------------------------------------------------------ частицы по всему кадру

        private ParticleSystem NewSystem(string name, Material mat, int order, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 10f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sortingOrder = order;
            if (mat != null) r.sharedMaterial = mat;
            return ps;
        }

        private static void Fade(ParticleSystem ps, float inT, float outT)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inT), new GradientAlphaKey(1f, 1f - outT), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void Velocity(ParticleSystem ps, Vector2 vx, Vector2 vy)
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(vx.x, vx.y);
            vel.y = new ParticleSystem.MinMaxCurve(vy.x, vy.y);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        private static void Noise(ParticleSystem ps, float strength, float freq)
        {
            var n = ps.noise;
            n.enabled = strength > 0f;
            n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.3f;
        }

        private ParticleSystem AreaSystem(string name, Material mat, int order, float density, int max, Vector2 life, Vector2 size,
                                          Color c0, Color c1, Vector2 vx, Vector2 vy, float noise, float spin, bool play = true)
        {
            if (mat == null) return null;
            var ps = NewSystem(name, mat, order, max);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.prewarm = true;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box;
            float h = _cam.orthographicSize * 2f, w = h * _cam.aspect;               // размер области — до прогрева
            shape.scale = new Vector3(w * 1.3f, h * 1.3f, 0.1f);
            var em = ps.emission;
            em.rateOverTime = Mathf.Min(density * w * h * 1.69f, max / Mathf.Max(0.5f, life.y));
            Fade(ps, 0.15f, 0.2f);
            Velocity(ps, new Vector2(vx.x * wind / 0.7f, vx.y * wind / 0.7f), vy);
            Noise(ps, noise, 0.4f);
            if (spin > 0f)
            {
                var rot = ps.rotationOverLifetime; rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-spin * Mathf.Deg2Rad, spin * Mathf.Deg2Rad);
            }
            _areas.Add(new Area { ps = ps, density = density, max = max });
            if (play) ps.Play();                                                       // иначе — после донастройки (листья, морось)
            return ps;
        }

        /// <summary>Листья: кружатся по z и переворачиваются (вращение по x/y), картинка — один из 4 листьев.</summary>
        private void Leaves(string name, int order, float density, int max, Vector2 size, Color tint)
        {
            var ps = AreaSystem(name, leafMaterial, order, density, max, new Vector2(7f, 11f), size, tint, tint * 0.85f,
                                new Vector2(0.3f, 1.1f), new Vector2(-0.45f, -0.9f), 0.6f, 0f, play: false);
            if (ps == null) return;
            var main = ps.main;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            Sheet(ps);
            ps.Play();
        }

        private static void Sheet(ParticleSystem ps)
        {
            var ts = ps.textureSheetAnimation;
            ts.enabled = true;
            ts.mode = ParticleSystemAnimationMode.Grid;
            ts.numTilesX = 4; ts.numTilesY = 1;
            ts.animation = ParticleSystemAnimationType.WholeSheet;
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);   // у каждой частицы — свой (случайный) лист на всю жизнь
            ts.cycleCount = 1;
        }

        /// <summary>Морось: капли вытягиваются по скорости (штрихи), падают косо по ветру.</summary>
        private void Drizzle(string name, int order, float density, int max, Color c, Vector2 width)
        {
            var ps = AreaSystem(name, dropMaterial, order, density, max, new Vector2(1.2f, 1.8f), width, c, c * 0.85f,
                                new Vector2(1.2f, 1.8f), new Vector2(-6.5f, -8f), 0f, 0f, play: false);
            if (ps == null) return;
            Fade(ps, 0.05f, 0.1f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 2f;
            ps.Play();
        }

        // ------------------------------------------------------------------ частицы у земли (появляются вдоль трассы)

        private void GroundSystem(string name, Material mat, int order, float perMeter, Vector2 height, Vector2 life, Vector2 size,
                                  Color c, Vector2 vx, Vector2 vy, bool sheet = false, float aspect = 1f)
        {
            if (mat == null) return;
            var ps = NewSystem(name, mat, order, 200);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            if (Mathf.Approximately(aspect, 1f)) main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            else
            {
                main.startSize3D = true;                                              // клочок тумана — вдвое шире, чем выше
                main.startSizeX = new ParticleSystem.MinMaxCurve(size.x * aspect, size.y * aspect);
                main.startSizeY = new ParticleSystem.MinMaxCurve(size.x, size.y);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(1f, 1f);
            }
            main.startColor = new ParticleSystem.MinMaxGradient(c, c * new Color(0.92f, 0.92f, 0.95f, 0.8f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            Fade(ps, 0.3f, 0.35f);
            Velocity(ps, vx * (wind / 0.7f), vy);
            Noise(ps, sheet ? 0.8f : 0.15f, sheet ? 0.8f : 0.2f);
            if (!Mathf.Approximately(aspect, 1f)) main.startRotation = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var rot = ps.rotationOverLifetime; rot.enabled = true;
            rot.z = sheet ? new ParticleSystem.MinMaxCurve(-6f, 6f)
                          : Mathf.Approximately(aspect, 1f) ? new ParticleSystem.MinMaxCurve(-0.15f, 0.15f) : new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            if (sheet) Sheet(ps);
            _grounds.Add(new Ground { ps = ps, perMeter = perMeter, height = height });
            ps.Play();
        }

        // ------------------------------------------------------------------ перекати-поле

        private void MakeTumbleweed()
        {
            var go = new GameObject("Tumbleweed");
            _tw = go.transform;
            _twSr = go.AddComponent<SpriteRenderer>();
            _twSr.sprite = tumbleweed;
            if (spriteMaterial != null) _twSr.sharedMaterial = spriteMaterial;
            _twSr.sortingOrder = 6;
            _twSr.color = new Color(0.92f, 0.86f, 0.8f);
            go.SetActive(false);
            _twNext = Time.time + Random.Range(3f, 8f);
        }

        private void UpdateTumbleweed(float dt)
        {
            if (_tw == null || terrain == null) return;
            float halfW = _cam.orthographicSize * _cam.aspect;
            Vector3 c = _cam.transform.position;
            if (!_tw.gameObject.activeSelf)
            {
                if (Time.time < _twNext) return;
                _twR = Random.Range(0.3f, 0.45f);
                float s = _twR * 2f / Mathf.Max(0.01f, tumbleweed.bounds.size.x);
                _tw.localScale = new Vector3(s, s, 1f);
                _twVx = Random.Range(2.2f, 3.8f) * wind / 0.7f;
                float x = c.x - halfW - 1.5f;                                           // катится из-за левого края кадра…
                if (Random.value < 0.6f) x = c.x + halfW * Random.Range(0.2f, 0.9f);   // …или уже в кадре впереди
                _tw.position = new Vector3(x, terrain.HeightAt(x) + _twR, 0f);
                _twVy = 0f;
                _tw.gameObject.SetActive(true);
                return;
            }
            Vector3 p = _tw.position;
            p.x += _twVx * dt;
            float g = terrain.HeightAt(p.x) + _twR;
            _twVy -= 9.8f * dt;
            p.y += _twVy * dt;
            if (p.y <= g)
            {
                p.y = g;
                float slope = (terrain.HeightAt(p.x + 0.3f) - terrain.HeightAt(p.x - 0.3f)) / 0.6f;
                _twVy = Random.value < 0.08f ? Random.Range(1.2f, 2.6f) : Mathf.Max(0f, slope * _twVx * 0.6f);   // подскоки на кочках
                _twVx = Mathf.Clamp(_twVx - slope * 3f * dt, 1.2f, 5f);
            }
            _tw.position = p;
            _tw.Rotate(0f, 0f, -_twVx / _twR * Mathf.Rad2Deg * dt);
            if (Mathf.Abs(p.x - c.x) > halfW + 6f || p.y < c.y - _cam.orthographicSize - 8f)
            {
                _tw.gameObject.SetActive(false);
                _twNext = Time.time + Random.Range(6f, 16f);
            }
        }

        // ------------------------------------------------------------------ каждый кадр

        private void LateUpdate()
        {
            if (_cam == null) return;
            float h = _cam.orthographicSize * 2f, w = h * _cam.aspect;
            foreach (Area a in _areas)
            {
                if (a.ps == null) continue;
                var shape = a.ps.shape;
                shape.scale = new Vector3(w * 1.3f, h * 1.3f, 0.1f);
                var em = a.ps.emission;
                float life = a.ps.main.startLifetime.constantMax;
                em.rateOverTime = Mathf.Min(a.density * w * h * 1.69f, a.max / Mathf.Max(0.5f, life));
            }
            float dt = Time.deltaTime;
            if (terrain != null && _grounds.Count > 0)
            {
                Vector3 c = _cam.transform.position;
                foreach (Ground g in _grounds)
                {
                    g.acc += g.perMeter * w * 1.6f * dt;                               // на метр кадра в секунду
                    int n = Mathf.Min(8, (int)g.acc);
                    g.acc -= n;
                    for (int i = 0; i < n; i++)
                    {
                        float x = c.x + Random.Range(-w * 0.75f, w * 0.85f);
                        var ep = new ParticleSystem.EmitParams
                        {
                            position = new Vector3(x, terrain.HeightAt(x) + Random.Range(g.height.x, g.height.y), 0f),
                            applyShapeToPosition = false
                        };
                        g.ps.Emit(ep, 1);
                    }
                }
            }
            UpdateTumbleweed(dt);
        }
    }
}
