using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Атмосферные эффекты байка (всё создаётся из кода, ничего настраивать не нужно):
    ///   • облако пыли при приземлении — тем больше, чем сильнее удар;
    ///   • пыль из-под заблокированного заднего колеса (юз при торможении);
    ///   • лёгкая пыль из-под колёс на большой скорости;
    ///   • встряска камеры при жёстком приземлении.
    /// Висит на объекте байка (рядом с BikeController2D). Материал — Materials/Dust.mat (Art/FX/dust.png).
    /// </summary>
    public class BikeEffects : MonoBehaviour
    {
        public BikeController2D bike;
        public CameraFollow2D cameraFollow;
        [Tooltip("Материал частиц (мягкое пятно пыли, цвет берётся из частиц).")]
        public Material dustMaterial;
        [Tooltip("Цвет пыли (под цвет земли).")]
        public Color dustColor = new Color(0.72f, 0.6f, 0.47f, 0.55f);
        public int sortingOrder = 8;

        [Header("Приземление")]
        [Tooltip("Скорость удара о землю (м/с), с которой появляется пыль.")]
        public float landingMinImpact = 2.5f;
        [Tooltip("Удар (м/с), при котором пыли и встряски максимум.")]
        public float landingMaxImpact = 11f;
        [Tooltip("Сколько частиц при самом сильном приземлении.")]
        public int landingMaxParticles = 70;
        [Tooltip("Сила встряски камеры при самом сильном приземлении (м).")]
        public float maxShake = 0.28f;

        [Header("Юз и качение")]
        [Tooltip("Разница скорости колеса и земли (м/с), с которой колесо считается в юзе.")]
        public float skidSlip = 1.5f;
        public float skidRate = 45f;
        [Tooltip("С какой скорости (м/с) из-под колёс понемногу летит пыль.")]
        public float rollDustSpeed = 9f;
        public float rollRate = 10f;

        [Header("Рама разломалась")]
        [Tooltip("Замедление времени в момент разлома (0.3 — в 3 раза медленнее) и сколько оно длится, с реального времени " +
                 "(первые 40% — замедленно, дальше плавно до обычной скорости).")]
        [Range(0.05f, 1f)] public float breakSlowMotion = 0.3f;
        public float breakSlowTime = 1.8f;
        [Tooltip("Встряска камеры при разломе (м) и при ударе гонщика о землю (самом сильном).")]
        public float breakShake = 0.6f, riderImpactShake = 0.45f;

        private ParticleSystem _dust;
        private bool _wasGrounded = true;
        private float _airTime;
        private Vector2 _prevVel;
        private float _skidAcc, _rollAcc;
        private float _slowLeft = -1f;

        private void Awake()
        {
            if (bike == null) bike = GetComponent<BikeController2D>();
            _dust = CreateDust();
        }

        private void Start()
        {
            if (bike != null) bike.Broken += OnBroken;
            RiderRagdoll.Impact += OnRiderImpact;
        }

        private void OnDestroy()
        {
            if (bike != null) bike.Broken -= OnBroken;
            RiderRagdoll.Impact -= OnRiderImpact;
            if (_slowLeft >= 0f) Time.timeScale = 1f;                     // замедление не переходит в следующую сцену
        }

        private void Update()
        {
            if (_slowLeft < 0f) return;
            _slowLeft -= Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(1f - _slowLeft / Mathf.Max(0.01f, breakSlowTime));
            Time.timeScale = _slowLeft <= 0f ? 1f : Mathf.Lerp(breakSlowMotion, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.6f));
            if (_slowLeft <= 0f) _slowLeft = -1f;
        }

        /// <summary>Рама разломалась: облако пыли из-под обеих половин, сильная встряска и замедление времени.</summary>
        private void OnBroken()
        {
            Vector2 vx = bike.Velocity;
            Burst(bike.rearWheel.position, 35, 1f, vx.x);
            Burst(bike.frontWheel.position, 35, 1f, vx.x);
            DustAt(bike.frame.position, 20, 1f);
            if (cameraFollow != null) cameraFollow.Shake(breakShake, 0.9f);
            if (breakSlowMotion < 1f && breakSlowTime > 0f) { _slowLeft = breakSlowTime; Time.timeScale = breakSlowMotion; }
        }

        /// <summary>Гонщик ударился о землю: пыль в точке удара и встряска — по силе удара.</summary>
        private void OnRiderImpact(Vector2 point, float speed)
        {
            if (_dust == null) return;
            float k = Mathf.InverseLerp(3.5f, 12f, speed);
            DustAt(point, Mathf.RoundToInt(Mathf.Lerp(8f, 40f, k)), k);
            if (cameraFollow != null) cameraFollow.Shake(riderImpactShake * (0.35f + 0.65f * k), 0.25f + 0.35f * k);
        }

        private void DustAt(Vector2 at, int n, float k)
        {
            for (int i = 0; i < n; i++)
            {
                var v = new Vector2(Random.Range(-1f, 1f) * (1f + 2.5f * k), Random.Range(0.3f, 1.2f + 1.8f * k));
                Emit(at + Random.insideUnitCircle * 0.2f, v, Random.Range(0.3f, 0.6f + 0.7f * k), Random.Range(0.8f, 1.4f + 0.6f * k));
            }
        }

        private ParticleSystem CreateDust()
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = 0f;                                   // скорость задаём сами при выбросе
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = dustColor;
            main.gravityModifier = 0.05f;
            main.maxParticles = 400;

            var em = ps.emission;
            em.enabled = false;                                     // только ручные выбросы (Emit)
            var shape = ps.shape;
            shape.enabled = false;

            // растут и тают: облако расползается и исчезает
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.4f;
            limit.dampen = 0.08f;                                   // воздух тормозит пыль
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingOrder = sortingOrder;
            if (dustMaterial != null) r.sharedMaterial = dustMaterial;
            ps.Play();
            return ps;
        }

        private void FixedUpdate()
        {
            if (bike == null || bike.frame == null || _dust == null) return;
            bool grounded = bike.IsOnGround;
            Vector2 vel = bike.Velocity;

            if (!grounded) _airTime += Time.fixedDeltaTime;
            else
            {
                if (!_wasGrounded && _airTime > 0.2f) Land(_prevVel);
                _airTime = 0f;
                Skid(Time.fixedDeltaTime);
            }
            _wasGrounded = grounded;
            _prevVel = vel;
        }

        /// <summary>Приземление: пыль из-под колёс в обе стороны и встряска — по силе удара
        /// (скорость поперёк склона перед касанием: точно в приземление на крутой склон — мягко, в плоское — жёстко).</summary>
        private void Land(Vector2 vBefore)
        {
            Vector2 nrm = bike.frame.transform.up;                  // после касания рама лежит вдоль склона
            float impact = Mathf.Max(0f, -Vector2.Dot(vBefore, nrm));
            if (impact < landingMinImpact) return;
            float k = Mathf.InverseLerp(landingMinImpact, landingMaxImpact, impact);
            int count = Mathf.RoundToInt(Mathf.Lerp(8f, landingMaxParticles, k));
            if (bike.RearNear) Burst(bike.rearWheel.position, count / 2 + 1, k, vBefore.x);
            if (bike.FrontNear) Burst(bike.frontWheel.position, count / 2 + 1, k, vBefore.x);
            if (cameraFollow != null) cameraFollow.Shake(maxShake * k * k, 0.25f + 0.25f * k);
        }

        private void Burst(Vector2 wheel, int n, float k, float vx)
        {
            Vector2 at = wheel + Vector2.down * bike.WheelRadius * 0.85f;
            for (int i = 0; i < n; i++)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                var v = new Vector2(side * Random.Range(0.6f, 2.8f + 2.5f * k) + vx * 0.25f, Random.Range(0.2f, 1.2f + 1.5f * k));
                Emit(at + Random.insideUnitCircle * 0.15f, v, Random.Range(0.3f, 0.6f + 0.6f * k), Random.Range(0.7f, 1.3f + 0.6f * k));
            }
        }

        /// <summary>Юз заднего колеса (скорость обода не совпадает со скоростью земли) и лёгкая пыль на скорости.</summary>
        private void Skid(float dt)
        {
            if (!bike.RearNear) return;
            float r = bike.WheelRadius;
            float vx = bike.Velocity.x;
            float rim = -bike.rearWheel.angularVelocity * Mathf.Deg2Rad * r;   // скорость обода (вращение по часовой — вперёд)
            float slip = Mathf.Abs(vx - rim);
            Vector2 at = (Vector2)bike.rearWheel.position + Vector2.down * r * 0.9f;

            if (slip > skidSlip && Mathf.Abs(vx) > 1f)
            {
                _skidAcc += skidRate * Mathf.Clamp01((slip - skidSlip) / 4f + 0.3f) * dt;
                while (_skidAcc >= 1f)
                {
                    _skidAcc -= 1f;
                    Emit(at, new Vector2(-Mathf.Sign(vx) * Random.Range(0.3f, 1.2f) + vx * 0.3f, Random.Range(0.2f, 0.9f)),
                         Random.Range(0.25f, 0.55f), Random.Range(0.6f, 1.1f));
                }
            }
            else if (bike.Speed > rollDustSpeed)
            {
                _rollAcc += rollRate * Mathf.InverseLerp(rollDustSpeed, rollDustSpeed * 2f, bike.Speed) * dt;
                while (_rollAcc >= 1f)
                {
                    _rollAcc -= 1f;
                    Emit(at, new Vector2(-Mathf.Sign(vx) * Random.Range(0.2f, 0.8f), Random.Range(0.1f, 0.5f)),
                         Random.Range(0.15f, 0.35f), Random.Range(0.4f, 0.8f));
                }
            }
        }

        private void Emit(Vector2 pos, Vector2 vel, float size, float life)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, pos.y, 0f),
                velocity = new Vector3(vel.x, vel.y, 0f),
                startSize = size,
                startLifetime = life,
                startColor = dustColor,
                applyShapeToPosition = false,
            };
            _dust.Emit(p, 1);
        }
    }
}
