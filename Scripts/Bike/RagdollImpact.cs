using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Часть куклы гонщика (корпус, голова): при ударе о землю сообщает кукле точку и скорость удара —
    /// по ним BikeEffects поднимает пыль и трясёт камеру, BikeAudio играет глухой удар тела.
    /// </summary>
    public class RagdollImpact : MonoBehaviour
    {
        public RiderRagdoll owner;

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (owner == null || c.contactCount == 0) return;
            Vector2 n = c.GetContact(0).normal;
            float speed = Mathf.Abs(Vector2.Dot(c.relativeVelocity, n));       // скорость поперёк земли
            owner.ReportImpact(c.GetContact(0).point, speed);
        }
    }
}
