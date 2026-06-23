using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class ChallengeTarget : MonoBehaviour
    {
        private static AudioClip hitClip;

        private bool destroyed;

        public bool IsDestroyed => destroyed;

        private void Awake()
        {
            EnsureCollider();
        }

        public void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>(true) != null)
            {
                return;
            }

            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                gameObject.AddComponent<BoxCollider>();
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            var box = gameObject.AddComponent<BoxCollider>();
            var localCenter = transform.InverseTransformPoint(bounds.center);
            var localSize = transform.InverseTransformVector(bounds.size);
            box.center = localCenter;
            box.size = new Vector3(
                Mathf.Abs(localSize.x),
                Mathf.Abs(localSize.y),
                Mathf.Abs(localSize.z));
        }

        public void RegisterHit(Vector3 hitPoint)
        {
            if (destroyed)
            {
                return;
            }

            destroyed = true;
            PlayHitSound(hitPoint);
            MatchChallengeController.Active?.NotifyTargetDestroyed(this);
            gameObject.SetActive(false);
        }

        private static void PlayHitSound(Vector3 position)
        {
            if (hitClip == null)
            {
                hitClip = Resources.Load<AudioClip>("Sounds/target");
            }

            if (hitClip == null)
            {
                return;
            }

            AudioSource.PlayClipAtPoint(hitClip, position);
        }
    }
}
