using UnityEngine;

namespace BikeGame
{
    /// <summary>
    /// Голова гонщика. Висит на дочернем объекте Head рамы (CircleCollider2D с галочкой Is Trigger).
    /// Коснулась земли → байк падает (BikeController2D.Crash).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class CrashDetector : MonoBehaviour
    {
        public BikeController2D bike;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (bike == null || other.isTrigger) return;
            if (((1 << other.gameObject.layer) & bike.groundLayers.value) == 0) return;
            bike.Crash();
        }
    }
}
