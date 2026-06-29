using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class TrainingBotController : MonoBehaviour
    {
        private const float DestinationRefreshMinSeconds = 3f;
        private const float DestinationRefreshMaxSeconds = 7f;

        [SerializeField] private float respawnDelaySeconds = 3f;
        [SerializeField] private float wanderRadius = 28f;

        private int botNumber;
        private NavMeshAgent agent;
        private PlayerHealth health;
        private Transform visualRoot;
        private float nextDestinationAt;
        private Coroutine respawnRoutine;
        private bool configured;

        public string TicketId { get; private set; } = string.Empty;
        public string KillBannerVictimLabel => $"бота #{botNumber}";
        public string KillFeedVictimLabel => $"Бот #{botNumber}";

        public void Initialize(int number)
        {
            botNumber = Mathf.Max(1, number);
            TicketId = $"training_bot_{botNumber}";

            health = GetComponent<PlayerHealth>();
            agent = GetComponent<NavMeshAgent>();
            visualRoot = transform.Find("ThirdPersonBody") ?? transform.Find("Visual");

            if (health != null)
            {
                health.TrainingBotDied -= HandleBotDied;
                health.TrainingBotDied += HandleBotDied;
            }

            configured = CanControlAgent(agent);
            if (configured)
            {
                PickNextDestination(true);
            }
        }

        private void Update()
        {
            if (!configured || health == null || health.IsDead || !CanControlAgent(agent))
            {
                return;
            }

            if (Time.time < nextDestinationAt)
            {
                return;
            }

            if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                PickNextDestination(false);
            }
        }

        public void ApplyHit(float damage, Vector3 hitDirection)
        {
            health?.ApplyLocalShooterDamage(damage, hitDirection);
        }

        public void WarpTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            health?.ForceReviveAt(position, rotation);
            SetVisualVisible(true);
            RemotePlayerLocomotionUtility.RestoreTrainingBotLocomotion(gameObject);

            if (agent == null)
            {
                configured = false;
                return;
            }

            RestoreNavMeshAgent(position);
            configured = CanControlAgent(agent);
            if (configured)
            {
                PickNextDestination(true);
                return;
            }

            StartCoroutine(ResumeNavMeshAfterWarpRoutine(position));
        }

        private IEnumerator ResumeNavMeshAfterWarpRoutine(Vector3 position)
        {
            yield return null;

            if (health == null || health.IsDead || agent == null)
            {
                yield break;
            }

            RestoreNavMeshAgent(position);
            configured = CanControlAgent(agent);
            if (configured)
            {
                PickNextDestination(true);
            }
        }

        private void RestoreNavMeshAgent(Vector3 position)
        {
            agent.enabled = true;
            agent.updatePosition = true;
            agent.updateRotation = true;
            agent.isStopped = false;

            if (NavMesh.SamplePosition(position, out var navHit, 2f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
            }
            else
            {
                agent.Warp(position);
            }
        }

        private void HandleBotDied()
        {
            StopAgent();
            SetVisualVisible(false);
            MatchTrainingController.Active?.NotifyBotKilled(this);

            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
            }

            respawnRoutine = StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, respawnDelaySeconds));

            var spawnPose = MatchTrainingController.Active != null
                ? MatchTrainingController.Active.SampleBotSpawnPose()
                : (transform.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            WarpTo(spawnPose.Item1, spawnPose.Item2);
            MatchTrainingController.Active?.NotifyBotRespawned();
            respawnRoutine = null;
        }

        private void PickNextDestination(bool immediate)
        {
            if (!CanControlAgent(agent))
            {
                configured = false;
                return;
            }

            var origin = transform.position;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var offset = Random.insideUnitSphere * wanderRadius;
                offset.y = 0f;
                if (!NavMesh.SamplePosition(origin + offset, out var hit, wanderRadius, NavMesh.AllAreas))
                {
                    continue;
                }

                agent.isStopped = false;
                agent.SetDestination(hit.position);
                nextDestinationAt = Time.time +
                                    (immediate
                                        ? 0.75f
                                        : Random.Range(DestinationRefreshMinSeconds, DestinationRefreshMaxSeconds));
                return;
            }
        }

        private void StopAgent()
        {
            if (agent == null)
            {
                return;
            }

            if (CanControlAgent(agent))
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            agent.enabled = false;
            configured = false;
        }

        private static bool CanControlAgent(NavMeshAgent navAgent)
        {
            return navAgent != null && navAgent.enabled && navAgent.isOnNavMesh;
        }

        private void SetVisualVisible(bool visible)
        {
            if (visualRoot != null)
            {
                visualRoot.gameObject.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.TrainingBotDied -= HandleBotDied;
            }
        }
    }
}
