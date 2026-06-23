using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Drives remote-style locomotion animator params from NavMeshAgent velocity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingBotLocomotionPresenter : MonoBehaviour
    {
        private const float WalkSpeedReference = 3.3f;
        private const float AnimSpeedSmoothTime = 0.12f;
        private const float MoveInputSmoothTime = 0.1f;

        private NavMeshAgent agent;
        private ProceduralLocomotionRig locomotionRig;
        private PlayerHealth health;
        private float smoothedAnimSpeed;
        private float animSpeedVelocity;
        private float smoothedMoveInputX;
        private float smoothedMoveInputZ;
        private float moveInputXVelocity;
        private float moveInputZVelocity;
        private int idleUpdatePhase;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            locomotionRig = GetComponentInChildren<ProceduralLocomotionRig>(true);
            health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (locomotionRig == null || health != null && health.IsDead)
            {
                return;
            }

            var dt = Mathf.Max(0.0001f, Time.deltaTime);
            var velocity = agent != null && agent.enabled ? agent.velocity : Vector3.zero;
            velocity.y = 0f;

            var targetAnimSpeed = Mathf.Clamp01(velocity.magnitude / WalkSpeedReference);
            if (targetAnimSpeed <= 0.02f && smoothedAnimSpeed <= 0.05f)
            {
                idleUpdatePhase++;
                if ((idleUpdatePhase & 1) != 0)
                {
                    return;
                }
            }
            else
            {
                idleUpdatePhase = 0;
            }

            smoothedAnimSpeed = Mathf.SmoothDamp(
                smoothedAnimSpeed,
                targetAnimSpeed,
                ref animSpeedVelocity,
                AnimSpeedSmoothTime,
                Mathf.Infinity,
                dt);

            ResolveMoveInput(velocity, targetAnimSpeed, out var moveInputX, out var moveInputZ);
            smoothedMoveInputX = Mathf.SmoothDamp(
                smoothedMoveInputX,
                moveInputX,
                ref moveInputXVelocity,
                MoveInputSmoothTime,
                Mathf.Infinity,
                dt);
            smoothedMoveInputZ = Mathf.SmoothDamp(
                smoothedMoveInputZ,
                moveInputZ,
                ref moveInputZVelocity,
                MoveInputSmoothTime,
                Mathf.Infinity,
                dt);

            locomotionRig.DriveNetworkLocomotionFromVelocity(
                velocity,
                grounded: true,
                jumpState: 0,
                isCrouching: false,
                isSprinting: false);
            locomotionRig.SetNetworkMoveInput(smoothedMoveInputX, smoothedMoveInputZ);
            locomotionRig.SetNetworkAnimationState(
                smoothedAnimSpeed,
                grounded: true,
                jumpState: 0,
                animPhase01: 0f,
                isCrouching: false,
                isSprinting: false);
            locomotionRig.SetNetworkLookPitch(0f);
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
