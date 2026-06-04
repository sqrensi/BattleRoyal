using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Remote TP animator layers: holstered = Opsive arm locomotion (synced to legs), armed = right-arm idle only.
    /// Left hand pose comes from weapon IK, not LeftArmIdle.
    /// </summary>
    [DefaultExecutionOrder(350)]
    public sealed class RemoteAnimatorHolsterPresentation : MonoBehaviour
    {
        public const int LegsLayerIndex = 0;
        public const int TorsoLayerIndex = 1;
        public const int RightArmIdleLayerIndex = 2;
        public const int LeftArmIdleLayerIndex = 3;
        public const int ArmsLocomotionLayerIndex = 4;

        [SerializeField] private Animator animator;
        [SerializeField] private float layerBlendTime = 0.14f;

        private bool networkHolstered;
        private bool networkHasWeapon;
        private bool networkUsingMedkit;
        private float armsIdleWeight;
        private float armsLocomotionWeight;

        public bool NetworkHolstered => networkHolstered;

        public void Configure(Animator targetAnimator)
        {
            animator = targetAnimator;
            networkHasWeapon = false;
            networkHolstered = true;
            armsIdleWeight = 0f;
            armsLocomotionWeight = animator != null && animator.layerCount > ArmsLocomotionLayerIndex ? 1f : 0f;
            ApplyLayerWeights();
        }

        public void SetWeaponEquipped(bool hasWeapon)
        {
            networkHasWeapon = hasWeapon;
            if (!hasWeapon)
            {
                networkHolstered = true;
                armsIdleWeight = 0f;
                armsLocomotionWeight = animator != null && animator.layerCount > ArmsLocomotionLayerIndex ? 1f : 0f;
                ApplyLayerWeights();
            }
        }

        public void SetHolstered(bool holstered)
        {
            networkHolstered = !networkHasWeapon || holstered;
        }

        public void SetMedkitUsing(bool usingMedkit)
        {
            networkUsingMedkit = usingMedkit;
            if (usingMedkit)
            {
                armsIdleWeight = 0f;
                armsLocomotionWeight = 0f;
                ApplyLayerWeights();
            }
        }

        public void ApplyArmedLayerWeightsImmediate()
        {
            networkHasWeapon = true;
            networkHolstered = false;
            armsIdleWeight = 1f;
            armsLocomotionWeight = 0f;
            ApplyLayerWeights();
        }

        private void LateUpdate()
        {
            if (!ShouldApply() || animator == null || animator.layerCount <= LeftArmIdleLayerIndex)
            {
                return;
            }

            if (networkUsingMedkit)
            {
                armsIdleWeight = 0f;
                armsLocomotionWeight = 0f;
                ApplyLayerWeights();
                return;
            }

            var targetIdle = networkHasWeapon && !networkHolstered ? 1f : 0f;
            var hasArmsLocomotionLayer = animator.layerCount > ArmsLocomotionLayerIndex;
            var targetLoco = networkHasWeapon && !networkHolstered || !hasArmsLocomotionLayer
                ? 0f
                : 1f;
            var step = layerBlendTime <= 0.0001f
                ? 1f
                : Time.deltaTime / layerBlendTime;

            armsIdleWeight = Mathf.MoveTowards(armsIdleWeight, targetIdle, step);
            armsLocomotionWeight = Mathf.MoveTowards(armsLocomotionWeight, targetLoco, step);
            ApplyLayerWeights();
        }

        private void ApplyLayerWeights()
        {
            animator.SetLayerWeight(RightArmIdleLayerIndex, armsIdleWeight);
            animator.SetLayerWeight(LeftArmIdleLayerIndex, 0f);

            if (animator.layerCount > ArmsLocomotionLayerIndex)
            {
                animator.SetLayerWeight(ArmsLocomotionLayerIndex, armsLocomotionWeight);
            }
        }

        private bool ShouldApply()
        {
            return GetComponent<RemoteThirdPersonPlayerBootstrap>() != null;
        }
    }
}
