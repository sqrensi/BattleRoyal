using System;
using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class DuelNavBotController : MonoBehaviour
    {
        private const float ThinkIntervalSeconds = 0.08f;
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
        private float currentLookPitch;

        public string Nickname { get; private set; } = "Бот";
        public string KillFeedVictimLabel => Nickname;
        public bool ControlsAimPresentation => combatEnabled && combatTarget != null && !health.IsDead;

        public event Action BotEliminated;

        public void Initialize(string nickname, float botSkill, in PlayerSkinNetworkState skinState)
        {
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "Бот" : nickname.Trim();
            botSkinState = skinState;
            skill = Mathf.Clamp(botSkill, 0.35f, 0.95f);
            aggression = Mathf.Clamp(0.5f + skill * 0.35f, 0.5f, 0.95f);
            health = GetComponent<PlayerHealth>();
            agent = GetComponent<NavMeshAgent>();
            weaponPresentation = GetComponent<RemoteWeaponPresentation>();
            shotEffects = GetComponent<RemotePlayerShotEffects>();
            lookPitchPosture = GetComponent<RemoteLookPitchPosture>();
            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);

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
            DuelBotLineOfSight.PrepareScene(gameObject.scene);
            DisarmWeaponPresentation();
        }

        public void SetCombatTarget(Transform target)
        {
            combatTarget = target;
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
                DuelBotLineOfSight.PrepareScene(gameObject.scene);
                EquipWeaponPresentation();
                RestoreNavMeshLocomotion(stopped: false);
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
            if (!combatEnabled || health == null || health.IsDead || combatTarget == null)
            {
                return;
            }

            UpdateAimPresentation(combatTarget.position);

            if (Time.time < nextThinkAt)
            {
                return;
            }

            nextThinkAt = Time.time + ThinkIntervalSeconds;
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

            var preferredRange = 8.5f;
            var moveSpeed = 4.8f + aggression * 3.8f;
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

            if (NavMesh.SamplePosition(destination, out var hit, wanderRadius, NavMesh.AllAreas))
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

        private void UpdateAimPresentation(Vector3 targetPos)
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

            var pitch = -Mathf.Atan2(look.y, horizontal.magnitude) * Mathf.Rad2Deg;
            currentLookPitch = Mathf.Clamp(pitch, -55f, 55f);
            lookPitchPosture?.SetNetworkLookPitch(currentLookPitch);
            weaponPresentation?.SetNetworkLookPitch(currentLookPitch);
            locomotionRig?.SetNetworkLookPitch(currentLookPitch);
        }

        private void TryShoot(Vector3 targetPos)
        {
            var baseInterval = 0.48f + (1f - skill) * 0.32f;
            nextShotAt = Time.time + baseInterval + UnityEngine.Random.Range(0.02f, 0.22f);

            var hitChance = 0.1f + skill * 0.34f;
            if (UnityEngine.Random.value > hitChance)
            {
                PlayShotPresentation(targetPos, false);
                return;
            }

            if (!DuelBotLineOfSight.CanSeeTarget(transform, combatTarget))
            {
                return;
            }

            var targetHealth = combatTarget.GetComponent<PlayerHealth>();
            if (targetHealth == null || targetHealth.IsDead)
            {
                return;
            }

            PlayShotPresentation(targetPos, true);

            var damage = UnityEngine.Random.value < 0.04f + skill * 0.06f ? 100f : 22f + skill * 10f;
            var hitDir = (targetPos - transform.position).normalized;
            targetHealth.ApplyEnvironmentalDamage(damage, hitDir);
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
                    UnityEngine.Random.Range(-4f, 4f),
                    UnityEngine.Random.Range(-7f, 7f),
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

            weaponPresentation.SetWeaponKind(equippedWeaponKind);
            weaponPresentation.SetNetworkWeaponSkins(in botSkinState);
            weaponPresentation.SetHolstered(false);
            weaponPresentation.SetWeaponEquipped(true);
            shotEffects?.ApplyForWeaponKind(equippedWeaponKind);

            var holsterPresentation = GetComponent<RemoteAnimatorHolsterPresentation>();
            holsterPresentation?.SetWeaponEquipped(true);
            holsterPresentation?.SetHolstered(false);
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

            var holsterPresentation = GetComponent<RemoteAnimatorHolsterPresentation>();
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
            agent.isStopped = stopped;
            if (!stopped)
            {
                agent.ResetPath();
            }

            configured = CanControlAgent(agent);
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
