using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Drives remote-style locomotion animator params and footstep audio from NavMeshAgent motion.
    /// Mirrors MatchPresenceSync.DriveRemoteLocomotion for offline bots.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class TrainingBotLocomotionPresenter : MonoBehaviour
    {
        private const float WalkSpeedReference = 3.3f;
        private const float SprintSpeedReference = 5.94f;
        private const float AnimSpeedSmoothTime = 0.12f;
        private const float AnimStopSmoothTime = 0.05f;
        private const float MoveInputSmoothTime = 0.1f;
        private const float MoveStopSmoothTime = 0.05f;

        [SerializeField] private float footstepIntervalSlow = 0.5f;
        [SerializeField] private float footstepIntervalFast = 0.42f;
        [SerializeField] private float footstepIntervalSprint = 0.28f;
        [SerializeField] private float footstepMoveThreshold = 0.12f;

        private NavMeshAgent agent;
        private ProceduralLocomotionRig locomotionRig;
        private PlayerHealth health;
        private DuelNavBotController duelBot;
        private PlayerAudioController audioController;
        private bool remoteAudioConfigured;
        private float smoothedAnimSpeed;
        private float animSpeedVelocity;
        private float smoothedMoveInputX;
        private float smoothedMoveInputZ;
        private float moveInputXVelocity;
        private float moveInputZVelocity;
        private float nextFootstepAt;
        private Vector3 lastTrackedPosition;
        private bool hasTrackedPosition;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            health = GetComponent<PlayerHealth>();
            duelBot = GetComponent<DuelNavBotController>();
            audioController = GetComponent<PlayerAudioController>();
        }

        private void OnEnable()
        {
            lastTrackedPosition = transform.position;
            hasTrackedPosition = true;
            RemotePlayerLocomotionUtility.EnsureSyntyLocomotionDriver(gameObject);
            TryConfigureRemoteAudio();
        }

        private void Update()
        {
            if (locomotionRig == null || health != null && health.IsDead)
            {
                return;
            }

            if (!EnemyPresentationVisibilityUtility.IsPresentationActive(gameObject))
            {
                return;
            }

            TryConfigureRemoteAudio();

            var dt = Mathf.Max(0.0001f, Time.deltaTime);
            var velocity = ResolveHorizontalVelocity(dt);
            var horizontalSpeed = velocity.magnitude;
            var isSprinting = horizontalSpeed > WalkSpeedReference * 0.92f;
            var speedReference = isSprinting ? SprintSpeedReference : WalkSpeedReference;
            var targetAnimSpeed = horizontalSpeed <= 0.05f
                ? 0f
                : Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, speedReference));

            var animSpeedSmooth = Mathf.Max(
                0.01f,
                targetAnimSpeed < smoothedAnimSpeed ? AnimStopSmoothTime : AnimSpeedSmoothTime);
            smoothedAnimSpeed = Mathf.SmoothDamp(
                smoothedAnimSpeed,
                targetAnimSpeed,
                ref animSpeedVelocity,
                animSpeedSmooth,
                Mathf.Infinity,
                dt);

            ResolveMoveInput(velocity, targetAnimSpeed, out var moveInputX, out var moveInputZ);
            var moveTargetMag = moveInputX * moveInputX + moveInputZ * moveInputZ;
            var moveCurrentMag = smoothedMoveInputX * smoothedMoveInputX + smoothedMoveInputZ * smoothedMoveInputZ;
            var moveSmooth = Mathf.Max(
                0.01f,
                moveTargetMag < moveCurrentMag ? MoveStopSmoothTime : MoveInputSmoothTime);
            smoothedMoveInputX = Mathf.SmoothDamp(
                smoothedMoveInputX,
                moveInputX,
                ref moveInputXVelocity,
                moveSmooth,
                Mathf.Infinity,
                dt);
            smoothedMoveInputZ = Mathf.SmoothDamp(
                smoothedMoveInputZ,
                moveInputZ,
                ref moveInputZVelocity,
                moveSmooth,
                Mathf.Infinity,
                dt);

            locomotionRig.DriveNetworkLocomotionFromVelocity(
                velocity,
                grounded: true,
                jumpState: 0,
                isCrouching: false,
                isSprinting: isSprinting);
            locomotionRig.SetNetworkMoveInput(smoothedMoveInputX, smoothedMoveInputZ);
            locomotionRig.SetNetworkAnimationState(
                smoothedAnimSpeed,
                grounded: true,
                jumpState: 0,
                animPhase01: 0f,
                isCrouching: false,
                isSprinting: isSprinting);
            if (duelBot == null || !duelBot.ControlsAimPresentation)
            {
                locomotionRig.SetNetworkLookPitch(0f);
            }

            TryEmitFootstep(horizontalSpeed, isSprinting);
        }

        private Vector3 ResolveHorizontalVelocity(float dt)
        {
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                var agentVelocity = agent.velocity;
                agentVelocity.y = 0f;
                if (agentVelocity.sqrMagnitude > 0.01f)
                {
                    return agentVelocity;
                }
            }

            if (!hasTrackedPosition)
            {
                lastTrackedPosition = transform.position;
                hasTrackedPosition = true;
                return Vector3.zero;
            }

            var delta = transform.position - lastTrackedPosition;
            lastTrackedPosition = transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= 0.000001f || dt <= 0.0001f)
            {
                return Vector3.zero;
            }

            return delta / dt;
        }

        private void TryEmitFootstep(float horizontalSpeed, bool isSprinting)
        {
            if (!TryConfigureRemoteAudio() ||
                horizontalSpeed < footstepMoveThreshold ||
                Time.time < nextFootstepAt)
            {
                return;
            }

            var cadenceFast = isSprinting
                ? Mathf.Max(0.06f, footstepIntervalSprint)
                : Mathf.Max(0.08f, footstepIntervalFast);
            var cadence = Mathf.Lerp(
                Mathf.Max(0.1f, footstepIntervalSlow),
                cadenceFast,
                Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, WalkSpeedReference)));

            nextFootstepAt = Time.time + cadence;
            audioController.PlayFootstep(isLocal: false, isSprinting);
        }

        private bool TryConfigureRemoteAudio()
        {
            if (remoteAudioConfigured)
            {
                return audioController != null && audioController.enabled;
            }

            if (audioController == null)
            {
                audioController = GetComponent<PlayerAudioController>();
            }

            if (audioController == null || !audioController.enabled)
            {
                return false;
            }

            var localAudio = GameplayRuntimeCache.LocalPlayerAudio;
            if (localAudio == null)
            {
                return false;
            }

            audioController.InheritFrom(localAudio);
            remoteAudioConfigured = true;
            return true;
        }

        private void ResolveMoveInput(Vector3 worldVelocity, float speed01, out float moveInputX, out float moveInputZ)
        {
            moveInputX = 0f;
            moveInputZ = 0f;
            if (speed01 <= 0.05f || worldVelocity.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var localDirection = transform.InverseTransformDirection(worldVelocity.normalized);
            moveInputX = Mathf.Clamp(localDirection.x, -1f, 1f);
            moveInputZ = Mathf.Clamp(localDirection.z, -1f, 1f);
        }
    }
}
