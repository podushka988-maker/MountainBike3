using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Звук байка (все звуки синтезированы — Tools/art/audio_gen.py, лежат в Assets/_Project/Audio):
    ///   • ветер в ушах — громче и выше с ростом скорости, в полёте сильнее;
    ///   • шины по грунту — пока колёса на земле, громкость и тон от скорости;
    ///   • трещотка втулки — на накате (педали не крутятся): на малой скорости отдельные щелчки (freehub_slow_loop),
    ///     на большой — сплошной треск (freehub_loop); частота щелчков — от скорости колеса;
    ///   • цепь при педалировании (pedal_loop) — тон от каденса, громче, когда гонщик давит;
    ///   • юз — когда заднее колесо скользит (заблокировано тормозом);
    ///   • удар при приземлении — по силе удара; звук падения.
    /// Висит на объекте байка рядом с BikeController2D. Источники звука создаются из кода.
    /// </summary>
    public class BikeAudio : MonoBehaviour
    {
        public BikeController2D bike;
        public AudioClip wind, roll, freehub, skid, crash;
        [Tooltip("Медленная трещотка (отдельные щелчки, 30 в секунду) и цепь при педалировании (90 об/мин).")]
        public AudioClip freehubSlow, pedal;
        public AudioClip[] landings;
        [Tooltip("Треск рамы: удар сильнее, чем рама выдерживает (Bike Config → Прочность рамы).")]
        public AudioClip frameCrack;

        [Header("Громкость")]
        [Range(0f, 1f)] public float master = 0.9f;
        [Range(0f, 1f)] public float windVolume = 0.55f;
        [Range(0f, 1f)] public float rollVolume = 0.45f;
        [Range(0f, 1f)] public float freehubVolume = 0.22f;
        [Range(0f, 1f)] public float pedalVolume = 0.2f;
        [Range(0f, 1f)] public float skidVolume = 0.5f;
        [Range(0f, 1f)] public float landVolume = 0.8f;
        [Range(0f, 1f)] public float crashVolume = 0.9f;

        [Header("Поведение")]
        [Tooltip("Скорость (м/с), на которой ветер и шины звучат в полную силу.")]
        public float fullSpeed = 14f;
        [Tooltip("Щелчков трещотки на оборот колеса (точки зацепления втулки).")]
        public int freehubPoints = 36;
        [Tooltip("Удар о землю (м/с поперёк склона), с которого слышно приземление, и при котором — в полную силу.")]
        public float landMinImpact = 1.2f, landMaxImpact = 9f;

        private AudioSource _wind, _roll, _freehub, _freehubSlow, _pedal, _skid, _oneShot, _thud;
        private float _rollGround, _airTime, _lastLand;
        private bool _wasGrounded = true;
        private Vector2 _prevVel;

        private void Awake()
        {
            if (bike == null) bike = GetComponent<BikeController2D>();
            _wind = Loop(wind); _roll = Loop(roll); _freehub = Loop(freehub); _freehubSlow = Loop(freehubSlow);
            _pedal = Loop(pedal); _skid = Loop(skid);
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false; _oneShot.spatialBlend = 0f;
            _thud = gameObject.AddComponent<AudioSource>();                  // удары гонщика о землю — свой источник (своя высота тона)
            _thud.playOnAwake = false; _thud.spatialBlend = 0f;
        }

        private void Start()
        {
            if (bike != null) { bike.Crashed += OnCrashed; bike.Hopped += OnHopped; bike.Broken += OnBroken; }
            RiderRagdoll.Impact += OnRiderImpact;
        }

        private void OnDestroy()
        {
            if (bike != null) { bike.Crashed -= OnCrashed; bike.Hopped -= OnHopped; bike.Broken -= OnBroken; }
            RiderRagdoll.Impact -= OnRiderImpact;
        }

        private AudioSource Loop(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
            if (clip != null) { s.time = Random.Range(0f, clip.length * 0.9f); s.Play(); }   // петли с разных мест — не звучат одинаково
            return s;
        }

        private void FixedUpdate()
        {
            if (bike == null || bike.frame == null) return;
            bool grounded = bike.IsOnGround;
            if (!grounded) _airTime += Time.fixedDeltaTime;
            else
            {
                if (!_wasGrounded && _airTime > 0.15f) Land(_prevVel);
                _airTime = 0f;
            }
            _wasGrounded = grounded;
            _prevVel = bike.Velocity;
        }

        private void Update()
        {
            if (bike == null || bike.frame == null) return;
            float dt = Time.deltaTime;
            bool alive = !bike.IsCrashed;
            float speed = bike.Speed, k = Mathf.Clamp01(speed / fullSpeed);

            // ветер: громкость ~ скорость² (как напор воздуха), в полёте чуть сильнее
            float air = bike.IsOnGround ? 0f : 0.25f;
            Set(_wind, windVolume * Mathf.Clamp01(k * k * 1.1f + air * k), 0.85f + 0.35f * k, dt, 0.3f);

            // шины: только на земле; при отрыве звук гаснет быстро, при касании появляется быстро
            _rollGround = Mathf.MoveTowards(_rollGround, bike.IsOnGround && alive ? 1f : 0f, dt / 0.06f);
            Set(_roll, rollVolume * _rollGround * Mathf.Pow(k, 0.8f), 0.7f + 0.55f * k, dt, 0.05f);

            // трещотка: накат (педали стоят), колесо крутится быстрее звёздочки — щелчки «точек зацепления»
            float wheelRev = Mathf.Abs((bike.rearWheel.angularVelocity - bike.frame.angularVelocity) / 360f);   // оборотов в секунду
            float clicks = wheelRev * freehubPoints;                                                          // щелчков в секунду
            bool coasting = alive && (bike.input == null || bike.input.Pedal <= 0f || bike.Cadence < 1f) && clicks > 3f;
            float fh = coasting ? freehubVolume * Mathf.Clamp01(clicks / 25f) : 0f;
            if (_freehubSlow != null && _freehubSlow.clip != null)
            {
                // медленно — отдельные щелчки (петля 30/с), быстро — сплошной треск (петля 120/с): тон каждой петли меняется
                // немного, иначе щелчок «пищит» или «бубнит»
                float fast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 90f, clicks));
                Set(_freehubSlow, fh * (1f - fast), Mathf.Clamp(clicks / 30f, 0.6f, 2f), dt, 0.04f);
                Set(_freehub, fh * fast, Mathf.Clamp(clicks / 120f, 0.6f, 1.6f), dt, 0.04f);
            }
            else Set(_freehub, fh, Mathf.Clamp(clicks / 120f, 0.35f, 2f), dt, 0.04f);

            // цепь: пока крутятся шатуны; тон — от каденса (петля записана на 90 об/мин), громче под нагрузкой и в передаче потяжелее
            float cad = alive ? bike.Cadence : 0f;
            float push = bike.input != null ? bike.input.Pedal : 0f;
            Set(_pedal, cad > 5f ? pedalVolume * Mathf.Clamp01(cad / 50f) * (0.55f + 0.45f * push) : 0f,
                Mathf.Clamp(cad / 90f, 0.45f, 1.7f), dt, 0.08f);

            // юз: обод крутится не со скоростью земли (колесо заблокировано)
            float slip = 0f;
            if (bike.RearNear && alive)
            {
                float rim = -bike.rearWheel.angularVelocity * Mathf.Deg2Rad * bike.WheelRadius;
                slip = Mathf.Abs(bike.Velocity.x - rim);
            }
            Set(_skid, skidVolume * Mathf.Clamp01((slip - 1.2f) / 4f) * Mathf.Clamp01(speed / 3f), 0.9f + 0.2f * k, dt, 0.05f);
        }

        private void Set(AudioSource s, float volume, float pitch, float dt, float smooth)
        {
            if (s == null || s.clip == null) return;
            s.volume = Mathf.MoveTowards(s.volume, volume * master, dt / Mathf.Max(0.01f, smooth));
            s.pitch = Mathf.Lerp(s.pitch, pitch, Mathf.Min(1f, dt / 0.15f));
        }

        /// <summary>Приземление: громкость по скорости удара поперёк склона (мягко в приземление на склон, жёстко — в плоское).</summary>
        private void Land(Vector2 vBefore)
        {
            if (landings == null || landings.Length == 0 || Time.time - _lastLand < 0.2f) return;
            Vector2 nrm = bike.frame.transform.up;
            float impact = Mathf.Max(0f, -Vector2.Dot(vBefore, nrm));
            if (impact < landMinImpact) return;
            float k = Mathf.InverseLerp(landMinImpact, landMaxImpact, impact);
            AudioClip c = landings[Random.Range(0, landings.Length)];
            if (c == null) return;
            _oneShot.pitch = Random.Range(0.92f, 1.06f) - 0.08f * k;     // сильный удар — ниже и тяжелее
            _oneShot.PlayOneShot(c, master * landVolume * Mathf.Lerp(0.25f, 1f, k));
            _lastLand = Time.time;
        }

        /// <summary>Толчок прыжка: короткий глухой «чпок» шин и подвески (тот же звук удара, тише и выше).</summary>
        private void OnHopped()
        {
            if (landings == null || landings.Length == 0 || landings[0] == null) return;
            _oneShot.pitch = 1.35f;
            _oneShot.PlayOneShot(landings[0], master * landVolume * 0.3f);
        }

        /// <summary>Гонщик ударился о землю: глухой удар тела (звук приземления ниже и тише — по силе удара).</summary>
        private void OnRiderImpact(Vector2 point, float speed)
        {
            if (landings == null || landings.Length == 0 || _thud == null) return;
            AudioClip c = landings[Random.Range(0, landings.Length)];
            if (c == null) return;
            float k = Mathf.InverseLerp(3.5f, 12f, speed);
            _thud.pitch = Random.Range(0.62f, 0.75f);
            _thud.PlayOneShot(c, master * landVolume * Mathf.Lerp(0.35f, 1f, k));
        }

        private void OnBroken()
        {
            if (frameCrack != null) { _oneShot.pitch = 1f; _oneShot.PlayOneShot(frameCrack, master * crashVolume); }
        }

        private void OnCrashed()
        {
            if (crash != null) { _oneShot.pitch = 1f; _oneShot.PlayOneShot(crash, master * crashVolume); }
        }
    }
}
