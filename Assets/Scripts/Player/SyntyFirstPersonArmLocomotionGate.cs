using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Local first-person split view only needs weapon-driven FP arms.
    /// Disables the Synty skeleton Animator so locomotion clips do not move hidden bones
    /// or the shared arm chain. Third-person / remote avatars keep animation enabled.
    /// When the weapon is armed, layer "Right Arm Idle" (forearms/fingers mask) is blended in.
    /// Left hand is driven by weapon IK only.
    /// </summary>
    [DefaultExecutionOrder(305)]
    public sealed class SyntyFirstPersonArmLocomotionGate : MonoBehaviour
    {
        public const int ArmsLocomotionLayerIndex = 1;
        public const int RightArmIdleLayerIndex = 2;
        public const int LeftArmIdleLayerIndex = 3;
        public const int ArmsLocomotionSyncedLayerIndex = 4;

        [SerializeField] private Animator animator;
        [SerializeField] private bool suppressArmLocomotionInFirstPerson = true;
        [SerializeField] private bool disableSkeletonAnimatorInFirstPerson = true;
        [SerializeField] private float thirdPersonArmsLayerWeight = 1f;
        [SerializeField] private float firstPersonArmsLayerWeight = 0f;
        [SerializeField] private float armsIdleLayerBlendTime = 0.14f;

        private PlayerViewPresentation viewPresentation;
        private SyntyFirstPersonArmsPresenter armsPresenter;
        private PlayerWeaponHolsterController weaponHolster;
        private bool storedAnimatorEnabled = true;
        private float armsIdleLayerWeight;

        public bool IsSkeletonAnimatorFrozen => ShouldFreezeSkeletonForFirstPersonView();

        public void Configure(Animator targetAnimator)
        {
            animator = targetAnimator;
            ApplyAnimatorState();
        }

        public void SetSuppressArmLocomotionInFirstPerson(bool suppress)
        {
            suppressArmLocomotionInFirstPerson = suppress;
            ApplyAnimatorState();
        }

        public void SetDisableSkeletonAnimatorInFirstPerson(bool disable)
        {
            disableSkeletonAnimatorInFirstPerson = disable;
            ApplyAnimatorState();
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (animator != null)
            {
                storedAnimatorEnabled = animator.enabled;
            }

            viewPresentation = GetComponent<PlayerViewPresentation>();
            armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
        }

        private void Update()
        {
            ApplyAnimatorState();
        }

        private void LateUpdate()
        {
            ApplyAnimatorState();
        }

        private void ApplyAnimatorState()
        {
            if (animator == null || IsRemoteThirdPersonAvatar())
            {
                return;
            }

            ApplyArmsIdleHolsterLayerWeight();

            if (disableSkeletonAnimatorInFirstPerson && ShouldFreezeSkeletonForFirstPersonView())
            {
                if (ShouldKeepAnimatorEnabledForFingerIdle())
                {
                    if (!animator.enabled && storedAnimatorEnabled)
                    {
                        animator.enabled = true;
                    }

                    SuppressLocomotionLayersForFirstPersonFingerIdle();
                    return;
                }

                if (animator.enabled)
                {
                    storedAnimatorEnabled = true;
                    animator.enabled = false;
                }

                return;
            }

            if (!animator.enabled && storedAnimatorEnabled)
            {
                animator.enabled = true;
            }

            ApplyArmLayerWeight();
        }

        private void ApplyArmsIdleHolsterLayerWeight()
        {
            if (animator == null || animator.layerCount <= LeftArmIdleLayerIndex)
            {
                return;
            }

            if (weaponHolster == null)
            {
                weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            }

            var keepRightArmIdle = weaponHolster != null && weaponHolster.ShouldKeepRightArmIdleAnimation;
            var targetWeight = keepRightArmIdle ? 1f : 0f;
            if (keepRightArmIdle)
            {
                armsIdleLayerWeight = 1f;
            }
            else
            {
                var step = armsIdleLayerBlendTime <= 0.0001f
                    ? 1f
                    : Time.deltaTime / armsIdleLayerBlendTime;
                armsIdleLayerWeight = Mathf.MoveTowards(armsIdleLayerWeight, targetWeight, step);
            }

            animator.SetLayerWeight(RightArmIdleLayerIndex, armsIdleLayerWeight);
            animator.SetLayerWeight(LeftArmIdleLayerIndex, 0f);
        }

        private bool ShouldKeepAnimatorEnabledForFingerIdle()
        {
            if (weaponHolster != null && weaponHolster.ShouldKeepRightArmIdleAnimation)
            {
                return true;
            }

            return armsIdleLayerWeight > 0.0001f;
        }

        private void SuppressLocomotionLayersForFirstPersonFingerIdle()
        {
            if (animator.layerCount > 0)
            {
                animator.SetLayerWeight(0, 0f);
            }

            if (animator.layerCount > ArmsLocomotionLayerIndex)
            {
                animator.SetLayerWeight(ArmsLocomotionLayerIndex, 0f);
            }

            if (animator.layerCount > ArmsLocomotionSyncedLayerIndex)
            {
                animator.SetLayerWeight(ArmsLocomotionSyncedLayerIndex, 0f);
            }
        }

        private void ApplyArmLayerWeight()
        {
            if (!suppressArmLocomotionInFirstPerson ||
                animator == null ||
                animator.layerCount <= ArmsLocomotionLayerIndex)
            {
                return;
            }

            var targetWeight = ShouldFreezeSkeletonForFirstPersonView()
                ? firstPersonArmsLayerWeight
                : thirdPersonArmsLayerWeight;
            var currentWeight = animator.GetLayerWeight(ArmsLocomotionLayerIndex);
            if (Mathf.Abs(currentWeight - targetWeight) > 0.0001f)
            {
                animator.SetLayerWeight(ArmsLocomotionLayerIndex, targetWeight);
            }
        }

        private bool ShouldFreezeSkeletonForFirstPersonView()
        {
            if (!suppressArmLocomotionInFirstPerson || IsRemoteThirdPersonAvatar())
            {
                return false;
            }

            if (viewPresentation == null)
            {
                viewPresentation = GetComponent<PlayerViewPresentation>();
            }

            if (viewPresentation == null)
            {
                return false;
            }

            if (!viewPresentation.IsLocalPlayerView)
            {
                return false;
            }

            if (viewPresentation != null && !viewPresentation.UsesSingleModelForFirstPerson)
            {
                return true;
            }

            if (armsPresenter == null)
            {
                armsPresenter = GetComponent<SyntyFirstPersonArmsPresenter>();
            }

            return armsPresenter == null || armsPresenter.HasFirstPersonArms;
        }

        private bool IsRemoteThirdPersonAvatar()
        {
            return GetComponent<RemoteThirdPersonPlayerBootstrap>() != null;
        }
    }
}
