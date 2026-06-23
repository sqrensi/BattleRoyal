using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class PlayerWeaponRaycastFilters
    {
        public static bool IsBoneHitbox(Collider collider)
        {
            return collider != null &&
                   collider.GetComponentInParent<PlayerBoneHitbox>(true) != null;
        }

        public static bool ShouldIgnoreCollider(Collider collider, Transform shooterRoot)
        {
            if (collider == null)
            {
                return true;
            }

            if (shooterRoot != null && collider.transform.IsChildOf(shooterRoot))
            {
                return true;
            }

            if (IsRemoteWeaponCollider(collider))
            {
                return true;
            }

            if (collider.GetComponent<CharacterController>() != null)
            {
                return true;
            }

            if (IsBoneHitbox(collider))
            {
                return false;
            }

            if (collider.GetComponentInParent<PlayerNetworkIdentity>() != null)
            {
                return true;
            }

            if (collider.GetComponentInParent<FpsCharacterController>() != null)
            {
                return true;
            }

            if (collider.GetComponentInParent<ChallengeTarget>(true) != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<PickupSpawnZone>(true) != null)
            {
                return true;
            }

            return false;
        }

        public static bool TrySelectClosestHit(
            RaycastHit[] hits,
            int hitCount,
            Transform shooterRoot,
            out RaycastHit selectedHit)
        {
            selectedHit = default;
            if (hits == null || hitCount <= 0)
            {
                return false;
            }

            RaycastHit? bestBoneHit = null;
            RaycastHit? bestWorldHit = null;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = hits[i];
                if (ShouldIgnoreCollider(hit.collider, shooterRoot))
                {
                    continue;
                }

                if (IsBoneHitbox(hit.collider))
                {
                    if (!bestBoneHit.HasValue || hit.distance < bestBoneHit.Value.distance)
                    {
                        bestBoneHit = hit;
                    }

                    continue;
                }

                if (!bestWorldHit.HasValue || hit.distance < bestWorldHit.Value.distance)
                {
                    bestWorldHit = hit;
                }
            }

            if (bestBoneHit.HasValue &&
                (!bestWorldHit.HasValue || bestBoneHit.Value.distance <= bestWorldHit.Value.distance))
            {
                selectedHit = bestBoneHit.Value;
                return true;
            }

            if (bestWorldHit.HasValue)
            {
                selectedHit = bestWorldHit.Value;
                return true;
            }

            return false;
        }

        public static bool IsPlayerBoneHit(Collider collider, Transform shooterRoot)
        {
            if (ShouldIgnoreCollider(collider, shooterRoot))
            {
                return false;
            }

            if (!IsBoneHitbox(collider))
            {
                return false;
            }

            var identity = collider.GetComponentInParent<PlayerNetworkIdentity>();
            return identity == null || !identity.IsLocalPlayer;
        }

        private static bool IsRemoteWeaponCollider(Collider targetCollider)
        {
            var current = targetCollider != null ? targetCollider.transform : null;
            while (current != null)
            {
                var name = current.name;
                if (string.Equals(name, "WeaponModel", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "RemoteWeaponTarget", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
