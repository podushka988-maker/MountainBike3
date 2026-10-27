using System.Collections.Generic;
using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Падение: гонщик отцепляется от байка и падает «куклой» — корпус, голова, бёдра, голени, плечи и предплечья
    /// становятся отдельными телами на шарнирах с пределами суставов (колено и локоть гнутся в одну сторону, бедро и плечо —
    /// в своих пределах). Каждая часть получает скорость, с которой двигалась (вместе с байком и телом), поэтому гонщик
    /// летит и кувыркается по инерции. Байк дальше падает сам — уже без массы гонщика.
    /// Создаёт BikeVisual в момент падения; спрайты частей переходят к телам куклы как есть (поза в момент падения).
    /// </summary>
    public class RiderRagdoll : MonoBehaviour
    {
        // доли массы частей (Демпстер); голень — вместе со ступнёй
        private const float MTrunk = 0.497f, MHead = 0.081f, MUArm = 0.028f, MFArm = 0.022f, MThigh = 0.100f, MShank = 0.061f;

        private readonly List<Collider2D> _colliders = new List<Collider2D>();
        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>();
        private Rigidbody2D _torso;
        /// <summary>Корпус куклы — за ним следит камера после падения.</summary>
        public Rigidbody2D Torso => _torso;

        // ---------- мышцы: человек при падении не «тряпка» ----------
        private enum Kind { Neck, Hip, Knee, Shoulder, Elbow }
        private class Muscle
        {
            public Rigidbody2D parent, child;
            public float lo, hi;          // пределы угла «ребёнок − родитель», °
            public Kind kind;
            public float hold, target;    // угол, который держит (после удара — поза в момент удара), и текущая цель
        }
        private readonly List<Muscle> _muscles = new List<Muscle>();
        private float _age, _stunAt = -1f;

        [Header("Мышцы при падении")]
        [Tooltip("Частота «мышц», Гц: насколько быстро сустав приходит в нужную позу (5 — резкий рефлекс).")]
        public float reflexFrequency = 5f;
        [Tooltip("Демпфирование мышц (0.7 — без раскачки).")]
        public float reflexDamping = 0.7f;
        [Tooltip("Тонус: в полёте (группируется и выставляет руки), сразу после удара (оглушён — держит позу вполсилы) " +
                 "и лёжа (почти расслаблен).")]
        public float toneFalling = 1f, toneStunned = 0.3f, toneLying = 0.1f;
        [Tooltip("Через сколько секунд после удара тело расслабляется совсем.")]
        public float relaxTime = 1.5f;
        [Tooltip("Удар сильнее этого (м/с) оглушает: рефлексы выключаются, тело держит позу вполсилы.")]
        public float stunImpact = 4.5f;
        private float _lastImpact = -1f;
        private int _impacts;

        /// <summary>Удар куклы о землю (точка, скорость удара м/с) — для пыли, встряски камеры и глухого звука.</summary>
        public static event System.Action<Vector2, float> Impact;
        [Tooltip("С какой скорости удара тело о землю слышно и видно (м/с).")]
        public float impactMinSpeed = 3.5f;
        private Vector2 _riderVel, _riderPos;
        private Rigidbody2D _frame;
        private float _gravityScale, _massTotal;
        private PhysicsMaterial2D _material;

        public static RiderRagdoll Create(BikeVisual visual, BikeController2D bike, RiderBody rider,
            Transform torso, Transform head, Transform thighN, Transform shinN, Transform thighF, Transform shinF,
            Transform upperArmN, Transform forearmN, Transform upperArmF, Transform forearmF, Transform footN, Transform footF)
        {
            var root = new GameObject("RiderRagdoll").AddComponent<RiderRagdoll>();
            root._frame = bike.frame;
            root._riderVel = rider.body != null ? rider.body.GetVelocity() : bike.frame.GetVelocity();
            root._riderPos = rider.body != null ? rider.body.position : bike.frame.position;
            root._gravityScale = bike.config != null ? bike.config.gravityScale : 1f;
            root._massTotal = bike.config != null ? bike.config.riderMass * bike.config.massScale : 3.6f;
            root._material = new PhysicsMaterial2D("Rider") { friction = 0.8f, bounciness = 0.05f };

            // тело гонщика из физики больше не тянет раму: его масса уходит в куклу
            if (rider.body != null) rider.body.simulated = false;

            Rigidbody2D rTorso = root.Body(torso, MTrunk, visual.torsoLength, 0.11f, true);
            Rigidbody2D rHead = root.Body(head, MHead, 0f, 0.12f, true);
            root._torso = rTorso;
            rTorso.gameObject.AddComponent<RagdollImpact>().owner = root;    // корпус и голова сообщают об ударах о землю
            rHead.gameObject.AddComponent<RagdollImpact>().owner = root;
            Rigidbody2D rThighN = root.Body(thighN, MThigh, visual.thigh, 0.075f, false);
            Rigidbody2D rThighF = root.Body(thighF, MThigh, visual.thigh, 0.075f, false);
            Rigidbody2D rShinN = root.Body(shinN, MShank, visual.shin, 0.055f, false);
            Rigidbody2D rShinF = root.Body(shinF, MShank, visual.shin, 0.055f, false);
            Rigidbody2D rUArmN = root.Body(upperArmN, MUArm, visual.upperArm, 0.05f, false);
            Rigidbody2D rUArmF = root.Body(upperArmF, MUArm, visual.upperArm, 0.05f, false);
            Rigidbody2D rFArmN = root.Body(forearmN, MFArm, visual.forearm, 0.045f, false);
            Rigidbody2D rFArmF = root.Body(forearmF, MFArm, visual.forearm, 0.045f, false);
            // ступни остаются на голенях (как есть в момент падения)
            if (footN != null) footN.SetParent(rShinN.transform, true);
            if (footF != null) footF.SetParent(rShinF.transform, true);

            // суставы: (родитель, ребёнок, точка, пределы «ребёнок − родитель» в градусах — как у человека)
            Vector2 shoulder = (Vector2)torso.position + (Vector2)torso.right * visual.torsoLength;
            root.Hinge(rTorso, rHead, shoulder, -150f, -30f, Kind.Neck);                 // шея
            root.Hinge(rTorso, rThighN, torso.position, -195f, -25f, Kind.Hip);         // тазобедренные
            root.Hinge(rTorso, rThighF, torso.position, -195f, -25f, Kind.Hip);
            root.Hinge(rThighN, rShinN, shinN.position, -150f, 0f, Kind.Knee);            // колени гнутся только назад
            root.Hinge(rThighF, rShinF, shinF.position, -150f, 0f, Kind.Knee);
            root.Hinge(rTorso, rUArmN, upperArmN.position, -220f, 10f, Kind.Shoulder);   // плечи
            root.Hinge(rTorso, rUArmF, upperArmF.position, -220f, 10f, Kind.Shoulder);
            root.Hinge(rUArmN, rFArmN, forearmN.position, 0f, 150f, Kind.Elbow);           // локти гнутся только вперёд
            root.Hinge(rUArmF, rFArmF, forearmF.position, 0f, 150f, Kind.Elbow);

            // части куклы не цепляются друг за друга и за байк (только за землю)
            var bikeCols = bike.GetComponentsInChildren<Collider2D>();
            foreach (Collider2D a in root._colliders)
            {
                foreach (Collider2D b in root._colliders) if (a != b) Physics2D.IgnoreCollision(a, b);
                foreach (Collider2D b in bikeCols) Physics2D.IgnoreCollision(a, b);
            }
            return root;
        }

        /// <summary>Тело для части: там же и так же повёрнуто, как спрайт; спрайт становится его ребёнком.</summary>
        private Rigidbody2D Body(Transform part, float massShare, float length, float radius, bool continuous)
        {
            var go = new GameObject("Ragdoll " + part.name);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(part.position, part.rotation);
            go.layer = part.gameObject.layer;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.useAutoMass = false;
            rb.gravityScale = _gravityScale;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = continuous ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
            rb.SetDamping(0.05f, 0.6f);
            Collider2D col;
            if (length <= 0f)
            {
                var c = go.AddComponent<CircleCollider2D>(); c.radius = radius; col = c;
            }
            else
            {
                var c = go.AddComponent<CapsuleCollider2D>();
                c.direction = CapsuleDirection2D.Horizontal;
                c.size = new Vector2(length + 2f * radius, 2f * radius);
                c.offset = new Vector2(length * 0.5f, 0f);
                col = c;
            }
            col.sharedMaterial = _material;
            _colliders.Add(col);
            rb.mass = Mathf.Max(0.01f, massShare * _massTotal);
            _bodies.Add(rb);
            // скорость части: как двигалось тело гонщика + вращение вместе с байком
            Vector2 centre = length > 0f ? (Vector2)part.position + (Vector2)part.right * (length * 0.5f) : (Vector2)part.position;
            rb.SetVelocity(_riderVel + (_frame.GetPointVelocity(centre) - _frame.GetPointVelocity(_riderPos)));
            rb.angularVelocity = _frame.angularVelocity;
            part.SetParent(go.transform, true);
            return rb;
        }

        /// <summary>
        /// Человек при падении: пока летит — инстинктивно группируется (колени и бёдра согнуты, шея напряжена) и выставляет
        /// руки навстречу земле по ходу падения; после сильного удара оглушён — мышцы держат позу вполсилы, руки и ноги
        /// не болтаются, а гаснут; через Relax Time почти расслаблен и лежит. Мышца — момент между соседними частями
        /// (PD-регулятор по углу сустава, масштаб — приведённая инерция пары), поэтому сила удара и инерция главнее мышц.
        /// </summary>
        private void FixedUpdate()
        {
            if (_muscles.Count == 0 || _torso == null) return;
            float dt = Time.fixedDeltaTime;
            _age += dt;
            if (_stunAt < 0f && _age > 1.6f) Stun();                      // долго летит / катится — всё равно сдаётся
            float tone;
            if (_stunAt < 0f) tone = toneFalling;
            else
            {
                float t = _age - _stunAt;
                tone = t < relaxTime ? Mathf.Lerp(toneStunned, toneLying, t / relaxTime) : toneLying;
            }
            // куда тянуться руками: вниз и по ходу падения (как ловят себя, падая)
            Vector2 v = _torso.GetVelocity();
            Vector2 reach = Vector2.down + (v.sqrMagnitude > 0.01f ? v.normalized * 0.7f : Vector2.zero);
            float reachAngle = Mathf.Atan2(reach.y, reach.x) * Mathf.Rad2Deg;
            float w = 2f * Mathf.PI * reflexFrequency;
            foreach (Muscle m in _muscles)
            {
                if (m.parent == null || m.child == null) continue;
                float centre = (m.lo + m.hi) * 0.5f;
                if (_stunAt < 0f)
                {
                    switch (m.kind)
                    {
                        case Kind.Hip: m.target = -115f; break;                        // бёдра к груди (группировка)
                        case Kind.Knee: m.target = -65f; break;                        // колени согнуты
                        case Kind.Elbow: m.target = 18f; break;                        // руки почти прямые, локти мягкие
                        case Kind.Shoulder: m.target = reachAngle - m.parent.rotation; break;   // руки навстречу земле
                        default: m.target = m.hold; break;                             // шея напряжена — голова не болтается
                    }
                }
                else m.target = m.hold;                                                // оглушён: держит позу, как упал
                float target = Mathf.Clamp(centre + Mathf.DeltaAngle(centre, m.target), m.lo, m.hi);
                float rel = centre + Mathf.DeltaAngle(centre, m.child.rotation - m.parent.rotation);
                float err = (target - rel) * Mathf.Deg2Rad;
                float relVel = (m.child.angularVelocity - m.parent.angularVelocity) * Mathf.Deg2Rad;
                float I = 1f / (1f / Mathf.Max(1e-6f, m.child.inertia) + 1f / Mathf.Max(1e-6f, m.parent.inertia));
                float acc = Mathf.Clamp(w * w * err - 2f * reflexDamping * w * relVel, -300f, 300f);   // рад/с²
                float torque = tone * I * acc;
                m.child.AddTorque(torque);
                m.parent.AddTorque(-torque);
            }
        }

        /// <summary>Сильный удар: рефлексы выключаются, каждый сустав держит угол, с которым тело ударилось.</summary>
        private void Stun()
        {
            _stunAt = _age;
            foreach (Muscle m in _muscles)
                if (m.parent != null && m.child != null)
                {
                    float centre = (m.lo + m.hi) * 0.5f;
                    m.hold = centre + Mathf.DeltaAngle(centre, m.child.rotation - m.parent.rotation);
                }
        }

        /// <summary>
        /// Рама разломалась: гонщика бросает через руль — всем частям добавляется скорость dv (м/с), корпус кувыркается
        /// вперёд (spin °/с, минус — по часовой, через голову), руки и ноги болтаются за ним на суставах.
        /// </summary>
        public void Fling(Vector2 dv, float spin)
        {
            foreach (Rigidbody2D rb in _bodies)
            {
                rb.SetVelocity(rb.GetVelocity() + dv);
                rb.angularVelocity += spin * (rb == _torso ? 1f : 0.6f);
            }
        }

        /// <summary>Удар корпуса или головы о землю (вызывает RagdollImpact): не чаще раза в 0.12 с и не больше 6 раз.</summary>
        public void ReportImpact(Vector2 point, float speed)
        {
            if (speed >= stunImpact && _stunAt < 0f) Stun();
            if (speed < impactMinSpeed || _impacts >= 6 || Time.time - _lastImpact < 0.12f) return;
            _lastImpact = Time.time; _impacts++;
            Impact?.Invoke(point, speed);
        }

        /// <summary>
        /// Шарнир с пределами сустава. lo/hi — пределы угла «ребёнок − родитель» (°). Unity считает пределы от угла
        /// в момент создания (Reference Angle); знак угла проверяем по нему же, чтобы колено не согнулось в другую сторону.
        /// </summary>
        private void Hinge(Rigidbody2D parent, Rigidbody2D child, Vector2 worldPoint, float lo, float hi, Kind kind)
        {
            float rel = child.rotation - parent.rotation;
            float centre = (lo + hi) * 0.5f;
            rel = centre + Mathf.DeltaAngle(centre, rel);                   // тот же угол, ближайший к середине диапазона
            lo = Mathf.Min(lo, rel - 2f); hi = Mathf.Max(hi, rel + 2f);      // поза вне пределов — не дёргаем сустав
            _muscles.Add(new Muscle { parent = parent, child = child, lo = lo, hi = hi, kind = kind, hold = rel, target = rel });

            var j = child.gameObject.AddComponent<HingeJoint2D>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = child.transform.InverseTransformPoint(worldPoint);
            j.connectedAnchor = parent.transform.InverseTransformPoint(worldPoint);
            j.enableCollision = false;
            // знак: Reference Angle = угол ребёнка относительно родителя (или наоборот — тогда пределы зеркалим)
            float sign = 1f;
            float r = Mathf.Abs(Mathf.DeltaAngle(0f, rel));
            if (r > 5f && r < 175f)
                sign = Mathf.Abs(Mathf.DeltaAngle(j.referenceAngle, rel)) <= Mathf.Abs(Mathf.DeltaAngle(j.referenceAngle, -rel)) ? 1f : -1f;
            var lim = new JointAngleLimits2D
            {
                min = sign > 0f ? lo - rel : -(hi - rel),
                max = sign > 0f ? hi - rel : -(lo - rel),
            };
            j.limits = lim;
            j.useLimits = true;
        }
    }
}
