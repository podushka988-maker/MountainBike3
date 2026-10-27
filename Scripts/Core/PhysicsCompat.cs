using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// В Unity 6 свойство Rigidbody2D.velocity переименовали в linearVelocity.
    /// Этот помощник работает в любой версии.
    /// </summary>
    public static class PhysicsCompat
    {
        public static Vector2 GetVelocity(this Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVelocity(this Rigidbody2D rb, Vector2 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        /// <summary>Сопротивление движению и вращению (в Unity 6 — linearDamping / angularDamping).</summary>
        public static void SetDamping(this Rigidbody2D rb, float linear, float angular)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = linear; rb.angularDamping = angular;
#else
            rb.drag = linear; rb.angularDrag = angular;
#endif
        }
    }
}
