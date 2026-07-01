using ShooterPrototype.Matchmaking;
using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Attenuates remote combat sounds when world geometry blocks line-of-sight from the listener.
    /// </summary>
    public static class CombatAudioOcclusion
    {
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

        public struct Settings
        {
            public float OccludedVolumeMultiplier;
            public float OccludedPitchMultiplier;
            public float OccludedLowPassCutoffHz;
        }

        public struct Result
        {
            public bool IsOccluded;
            public float VolumeMultiplier;
            public float PitchMultiplier;
            public float LowPassCutoffHz;
        }

        public static bool ShouldSkipForSource(GameObject sourceRoot)
        {
            if (sourceRoot == null)
            {
                return true;
            }

            if (ActiveMatchContext.IsOfflineSoloSession)
            {
                return true;
            }

            if (sourceRoot.GetComponent<DuelNavBotController>() != null ||
                sourceRoot.GetComponent<TrainingBotController>() != null)
            {
                return true;
            }

            return false;
        }

        public static Result Evaluate(Vector3 soundWorldPosition, Transform soundRoot, in Settings settings)
        {
            var clear = new Result
            {
                IsOccluded = false,
                VolumeMultiplier = 1f,
                PitchMultiplier = 1f,
                LowPassCutoffHz = 22000f,
            };

            if (soundRoot != null && ShouldSkipForSource(soundRoot.gameObject))
            {
                return clear;
            }

            if (!TryResolveListenerPosition(out var listenerPosition))
            {
                return clear;
            }

            var toSound = soundWorldPosition - listenerPosition;
            var distance = toSound.magnitude;
            if (distance <= 0.35f)
            {
                return clear;
            }

            var direction = toSound / distance;
            var hitCount = Physics.RaycastNonAlloc(
                listenerPosition,
                direction,
                HitBuffer,
                distance - 0.15f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (hitCount <= 0)
            {
                return clear;
            }

            var closestOccluderDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = HitBuffer[i];
                if (!IsOccluderCollider(hit.collider, soundRoot))
                {
                    continue;
                }

                if (hit.distance < closestOccluderDistance)
                {
                    closestOccluderDistance = hit.distance;
                }
            }

            if (!float.IsFinite(closestOccluderDistance))
            {
                return clear;
            }

            return new Result
            {
                IsOccluded = true,
                VolumeMultiplier = Mathf.Clamp01(settings.OccludedVolumeMultiplier),
                PitchMultiplier = Mathf.Clamp(settings.OccludedPitchMultiplier, 0.5f, 1f),
                LowPassCutoffHz = Mathf.Clamp(settings.OccludedLowPassCutoffHz, 300f, 5000f),
            };
        }

        private static bool TryResolveListenerPosition(out Vector3 listenerPosition)
        {
            var camera = GameplayRuntimeCache.LocalPlayerCamera;
            if (camera != null)
            {
                listenerPosition = camera.transform.position;
                return true;
            }

            var localPlayer = GameplayRuntimeCache.LocalPlayer;
            if (localPlayer != null)
            {
                listenerPosition = localPlayer.transform.position + Vector3.up * 1.55f;
                return true;
            }

            listenerPosition = Vector3.zero;
            return false;
        }

        private static bool IsOccluderCollider(Collider collider, Transform soundRoot)
        {
            if (collider == null || !collider.enabled)
            {
                return false;
            }

            if (soundRoot != null && collider.transform.IsChildOf(soundRoot))
            {
                return false;
            }

            if (collider.GetComponent<CharacterController>() != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<PlayerNetworkIdentity>() != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<FpsCharacterController>() != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<TrainingBotController>() != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<DuelNavBotController>() != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<PlayerBoneHitbox>() != null)
            {
                return false;
            }

            if (collider.GetComponentInParent<PlayerBoneHitboxRig>() != null)
            {
                return false;
            }

            return true;
        }
    }
}
