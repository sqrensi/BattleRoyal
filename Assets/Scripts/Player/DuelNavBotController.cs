using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class DuelNavBotController : MonoBehaviour
    {
        private const float ThinkIntervalSeconds = 0.11f;
        private const float DmTargetRefreshSeconds = 0.72f;
        private const float AimEyeHeight = 1.55f;
        private const float BodyYawTurnSpeed = 18f;

        [SerializeField] private float wanderRadius = 5.5f;
        [SerializeField] private float maxEngageDistance = 24f;

        private NavMeshAgent agent;
        private PlayerHealth health;
        private RemoteWeaponPresentation weaponPresentation;
        private RemotePlayerShotEffects shotEffects;
        private RemoteLookPitchPosture lookPitchPosture;
        private ProceduralLocomotionRig locomotionRig;
        private Transform combatTarget;
        private WeaponKind equippedWeaponKind = WeaponKind.AssaultRifle;
        private PlayerSkinNetworkState botSkinState;
        private float skill = 0.65f;
        private float aggression = 0.6f;
        private float nextThinkAt;
        private float nextShotAt;
        private int strafeDir = 1;
        private bool combatEnabled;
        private bool configured;
        private bool freeTargetMode;
        private float currentLookPitch;
        private float nextTargetRefreshAt;
        private Coroutine handPresentationRefreshRoutine;
        private Coroutine visualPresentationRefreshRoutine;
        private int presentationFrameOffset;

        private RemoteAnimatorHolsterPresentation holsterPresentation;

        public string Nickname { get; private set; } = "Бот";
        public string ScoreboardTicketId { get; private set; } = "offline-bot";
        public string KillFeedVictimLabel => Nickname;
        public bool ControlsAimPresentation => combatEnabled && combatTarget != null && !health.IsDead;

        public event Action BotEliminated;

        public void Initialize(string nickname, float botSkill, in PlayerSkinNetworkState skinState)
        {
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "Бот" : nickname.Trim();
            botSkinState = skinState;
            skill = Mathf.Clamp(botSkill, 0.3f, 0.9f);
            aggression = Mathf.Clamp(0.38f + skill * 0.28f, 0.38f, 0.8f);
            health = GetComponent<PlayerHealth>();
            agent = GetComponent<NavMeshAgent>();
            weaponPresentation = GetComponent<RemoteWeaponPresentation>();
            shotEffects = GetComponent<RemotePlayerShotEffects>();
            lookPitchPosture = GetComponent<RemoteLookPitchPosture>();
            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            holsterPresentation = GetComponent<RemoteAnimatorHolsterPresentation>();

            if (CanControlAgent(agent))
            {
                agent.acceleration = 18f;
                agent.angularSpeed = 540f;
                agent.updateRotation = false;
            }

            if (health != null)
            {
                health.TrainingBotDied -= HandleBotDied;
                health.TrainingBotDied += HandleBotDied;
            }

            configured = CanControlAgent(agent);
            presentationFrameOffset = GetInstanceID() & 3;
            DisarmWeaponPresentation();
        }

        public void RefreshVisualPresentation()
        {
            TrainingBotFactory.RefreshDuelBotVisual(gameObject, botSkinState);
        }

        public void RefreshVisualPresentationDeferred()
        {
            if (visualPresentationRefreshRoutine != null)
            {
                StopCoroutine(visualPresentationRefreshRoutine);
            }

            visualPresentationRefreshRoutine = StartCoroutine(RefreshVisualPresentationRoutine());
        }

        public void SyncWeaponPresentationAfterVisualRefresh()
        {
            if (!combatEnabled || health == null || health.IsDead)
            {
                return;
            }

            EquipWeaponPresentation();
        }

        public void RefreshWeaponHandPresentationDeferred()
        {
            RefreshWeaponHandPresentationImmediate();
            if (handPresentationRefreshRoutine != null)
            {
                StopCoroutine(handPresentationRefreshRoutine);
            }

            handPresentationRefreshRoutine = StartCoroutine(RefreshWeaponHandPresentationRoutine());
        }

        public void RefreshWeaponHandPresentationImmediate()
        {
            weaponPresentation ??= GetComponent<RemoteWeaponPresentation>();
            weaponPresentation?.RefreshHandGripIk();
        }

        private IEnumerator RefreshVisualPresentationRoutine()
        {
            yield return null;
            yield return null;
            RefreshVisualPresentation();
            SyncWeaponPresentationAfterVisualRefresh();
            RefreshWeaponHandPresentationImmediate();
            visualPresentationRefreshRoutine = null;
        }

        private IEnumerator RefreshWeaponHandPresentationRoutine()
        {
            yield return null;
            yield return null;
            RefreshWeaponHandPresentationImmediate();
            handPresentationRefreshRoutine = null;
        }

        public void SetCombatTarget(Transform target)
        {
            combatTarget = target;
        }

        public void SetFreeTargetMode(bool enabled)
        {
            freeTargetMode = enabled;
            if (enabled)
            {
                RefreshFreeTarget(force: true);
            }
        }

        public void SetScoreboardTicketId(string ticketId)
        {
            ScoreboardTicketId = string.IsNullOrWhiteSpace(ticketId) ? "offline-bot" : ticketId.Trim();
        }

        public void PrepareCombatRound()
        {
            equippedWeaponKind = RollRoundWeaponKind();
            EquipWeaponPresentation();
        }

        public void SetCombatEnabled(bool enabled)
        {
            combatEnabled = enabled;
            if (!enabled)
            {
                StopAgent();
                if (health == null || !health.IsDead)
                {
                    DisarmWeaponPresentation();
                }
            }
            else
            {
                EquipWeaponPresentation();
                RefreshWeaponHandPresentationDeferred();
                RestoreNavMeshLocomotion(stopped: false);
            }
        }

        public void ApplyHit(float damage, Vector3 hitDirection)
        {
            health?.ApplyLocalShooterDamage(
                damage,
                hitDirection,
                "offline-local",
                PlayerProfileService.Nickname);
        }

        public void WarpTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            health?.ForceReviveAt(position, rotation);

            if (agent == null)
            {
                configured = false;
                return;
            }

            agent.enabled = true;
            if (NavMesh.SamplePosition(position, out var navHit, 3f, NavMesh.AllAreas))
            {
                agent.Warp(navHit.position);
            }
            else
            {
                agent.Warp(position);
            }

            RestoreNavMeshLocomotion(stopped: !combatEnabled);
        }

        public void RestoreAfterDeathRevival()
        {
            RestoreNavMeshLocomotion(stopped: !combatEnabled);
            if (combatEnabled)
            {
                EquipWeaponPresentation();
            }
        }

        private void Update()
        {
            if (!combatEnabled || health == null || health.IsDead)
            {
                return;
            }

            if (freeTargetMode)
            {
                if (combatTarget == null ||
                    combatTarget.GetComponentInParent<PlayerHealth>() is not PlayerHealth targetHealth ||
                    targetHealth.IsDead ||
                    Time.time >= nextTargetRefreshAt)
                {
                    RefreshFreeTarget(force: false);
                }
            }

            if (combatTarget == null)
            {
                return;
            }

            if (EnemyPresentationVisibilityUtility.IsPresentationActive(gameObject))
            {
                UpdateBodyYawPresentation(combatTarget.position);
                if (ShouldRunFullPresentationSolveThisFrame())
                {
                    UpdateLookPitchPresentation(combatTarget.position);
                }
            }

            if (Time.time < nextThinkAt)
            {
                return;
            }

            nextThinkAt = Time.time + (freeTargetMode
                ? GameplayPerformanceOptions.DmBotThinkIntervalSeconds
                : ThinkIntervalSeconds);
            TickCombat();
        }

        private void TickCombat()
        {
            if (!configured || !CanControlAgent(agent))
            {
                configured = CanControlAgent(agent);
                return;
            }

            var targetPos = combatTarget.position;
            var toTarget = targetPos - transform.position;
            toTarget.y = 0f;
            var distance = toTarget.magnitude;
            if (distance < 0.05f)
            {
                return;
            }

            var preferredRange = 9.5f;
            var moveSpeed = 4.1f + aggression * 3.1f;
            Vector3 destination;

            if (distance > preferredRange + 1.5f)
            {
                destination = targetPos;
            }
            else if (distance < preferredRange - 2.5f)
            {
                destination = transform.position - toTarget.normalized * 3.5f;
            }
            else
            {
                if (UnityEngine.Random.value < 0.28f)
                {
                    strafeDir = -strafeDir;
                }

                var perp = new Vector3(-toTarget.normalized.z, 0f, toTarget.normalized.x);
                destination = transform.position + perp * strafeDir * 3.8f;
            }

            if (NavMesh.SamplePosition(destination, out var hit, wanderRadius, NavMesh.AllAreas) &&
                CanControlAgent(agent))
            {
                agent.isStopped = false;
                agent.speed = moveSpeed;
                agent.SetDestination(hit.position);
            }

            if (distance <= maxEngageDistance &&
                Time.time >= nextShotAt &&
                DuelBotLineOfSight.CanSeeTarget(transform, combatTarget))
            {
                TryShoot(targetPos);
            }
        }

        private void UpdateBodyYawPresentation(Vector3 targetPos)
        {
            var aimOrigin = transform.position + Vector3.up * AimEyeHeight;
            var look = targetPos + Vector3.up * 1.35f - aimOrigin;
            if (look.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var horizontal = new Vector3(look.x, 0f, look.z);
            if (horizontal.sqrMagnitude > 0.0001f)
            {
                var targetRotation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * BodyYawTurnSpeed);
            }
        }

        private void UpdateLookPitchPresentation(Vector3 targetPos)
        {
            var aimOrigin = transform.position + Vector3.up * AimEyeHeight;
            var look = targetPos + Vector3.up * 1.35f - aimOrigin;
            if (look.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var horizontal = new Vector3(look.x, 0f, look.z);
            var pitch = -Mathf.Atan2(look.y, horizontal.magnitude) * Mathf.Rad2Deg;
            currentLookPitch = Mathf.Clamp(pitch, -55f, 55f);
            lookPitchPosture?.SetNetworkLookPitch(currentLookPitch);
            weaponPresentation?.SetNetworkLookPitch(currentLookPitch);
            locomotionRig?.SetNetworkLookPitch(currentLookPitch);
        }

        private void UpdateAimPresentation(Vector3 targetPos)
        {
            UpdateBodyYawPresentation(targetPos);
            UpdateLookPitchPresentation(targetPos);
        }

        private bool ShouldRunFullPresentationSolveThisFrame()
        {
            var interval = Mathf.Max(1, GameplayPerformanceOptions.BotPresentationSolveIntervalFrames);
            return (Time.frameCount + presentationFrameOffset) % interval == 0;
        }

        private void TryShoot(Vector3 targetPos)
        {
            var baseInterval = 0.58f + (1f - skill) * 0.4f;
            nextShotAt = Time.time + baseInterval + UnityEngine.Random.Range(0.06f, 0.32f);

            var hitChance = 0.07f + skill * 0.26f;
            if (UnityEngine.Random.value > hitChance)
            {
                PlayShotPresentation(targetPos, false);
                return;
            }

            if (!DuelBotLineOfSight.CanSeeTarget(transform, combatTarget))
            {
                return;
            }

            var targetHealth = combatTarget.GetComponentInParent<PlayerHealth>();
            if (targetHealth == null || targetHealth.IsDead)
            {
                return;
            }

            PlayShotPresentation(targetPos, true);

            var damage = UnityEngine.Random.value < 0.03f + skill * 0.04f ? 100f : 18f + skill * 8f;
            var hitDir = (targetPos - transform.position).normalized;
            if (targetHealth.IsTrainingBotMode)
            {
                targetHealth.ApplyLocalShooterDamage(damage, hitDir, ScoreboardTicketId, Nickname);
            }
            else if (MatchOfflineDeathmatchController.Active != null)
            {
                targetHealth.ApplyEnvironmentalDamage(damage, hitDir, ScoreboardTicketId, Nickname);
            }
            else
            {
                targetHealth.ApplyEnvironmentalDamage(damage, hitDir);
            }

            MatchScoreboardTracker.AddDamageDealt(ScoreboardTicketId, Mathf.RoundToInt(damage));
        }

        private void RefreshFreeTarget(bool force)
        {
            if (!force && Time.time < nextTargetRefreshAt)
            {
                return;
            }

            nextTargetRefreshAt = Time.time + DmTargetRefreshSeconds;

            if (MatchOfflineDeathmatchController.Active != null)
            {
                if (MatchOfflineDeathmatchController.Active.TryFindNearestEnemy(
                        transform.position,
                        health,
                        maxEngageDistance + 8f,
                        out var registryTarget))
                {
                    combatTarget = registryTarget;
                }
                else
                {
                    combatTarget = null;
                }

                return;
            }

            Transform bestTarget = null;
            var bestDistance = float.MaxValue;
            var origin = transform.position;
            var candidates = FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidateHealth = candidates[i];
                if (candidateHealth == null ||
                    candidateHealth == health ||
                    candidateHealth.IsDead)
                {
                    continue;
                }

                var candidateRoot = candidateHealth.transform;
                var distance = Vector3.Distance(origin, candidateRoot.position);
                if (distance > maxEngageDistance + 8f || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestTarget = candidateRoot;
            }

            combatTarget = bestTarget;
        }

        private void PlayShotPresentation(Vector3 targetPos, bool aimedShot)
        {
            shotEffects ??= GetComponent<RemotePlayerShotEffects>();
            if (shotEffects == null)
            {
                return;
            }

            shotEffects.ApplyForWeaponKind(equippedWeaponKind);

            var origin = transform.position + Vector3.up * AimEyeHeight;
            var direction = targetPos + Vector3.up * 1.2f - origin;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = transform.forward;
            }
            else
            {
                direction.Normalize();
            }

            if (!aimedShot)
            {
                direction = Quaternion.Euler(
                    UnityEngine.Random.Range(-6f, 6f),
                    UnityEngine.Random.Range(-10f, 10f),
                    0f) * direction;
            }

            var endPoint = origin + direction * 80f;
            shotEffects.PlayRemoteShot(origin, direction, endPoint, true, currentLookPitch);
        }

        private void EquipWeaponPresentation()
        {
            weaponPresentation ??= GetComponent<RemoteWeaponPresentation>();
            shotEffects ??= GetComponent<RemotePlayerShotEffects>();
            if (weaponPresentation == null)
            {
                return;
            }

            var kindByte = (byte)equippedWeaponKind;
            weaponPresentation.SetNetworkWeaponSkins(in botSkinState);
            weaponPresentation.SetWeaponLoadout(
                kindByte,
                PlayerWeaponLoadout.EmptySlotKind,
                0,
                holstered: false,
                hasWeapon: true,
                activeWeaponKind: kindByte);
            shotEffects?.ApplyForWeaponKind(equippedWeaponKind);

            holsterPresentation?.ApplyArmedLayerWeightsImmediate();
            weaponPresentation.RefreshHandGripIk();
        }

        private void DisarmWeaponPresentation()
        {
            weaponPresentation ??= GetComponent<RemoteWeaponPresentation>();
            if (weaponPresentation == null)
            {
                return;
            }

            weaponPresentation.SetHolstered(true);
            weaponPresentation.SetWeaponEquipped(false);

            holsterPresentation ??= GetComponent<RemoteAnimatorHolsterPresentation>();
            holsterPresentation?.SetHolstered(true);
            holsterPresentation?.SetWeaponEquipped(false);
        }

        private static WeaponKind RollRoundWeaponKind()
        {
            return WeaponKindUtility.ClampKind(UnityEngine.Random.Range(0, 4));
        }

        private void HandleBotDied()
        {
            StopAgent();
            if (agent != null)
            {
                agent.enabled = false;
            }

            BotEliminated?.Invoke();
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
        }

        private void RestoreNavMeshLocomotion(bool stopped)
        {
            if (agent == null)
            {
                configured = false;
                return;
            }

            agent.enabled = true;
            agent.updatePosition = true;
            agent.updateRotation = false;
            if (!agent.isOnNavMesh)
            {
                configured = false;
                return;
            }

            agent.isStopped = stopped;
            if (!stopped)
            {
                agent.ResetPath();
            }

            configured = true;
        }

        private static bool CanControlAgent(NavMeshAgent navAgent)
        {
            return navAgent != null && navAgent.enabled && navAgent.isOnNavMesh;
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
