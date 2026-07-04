using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Shared wiring for third-person remote locomotion (animator driver, rig, audio).
    /// Used by network remotes and offline duel bots.
    /// </summary>
    public static class RemotePlayerLocomotionUtility
    {
        public static void FinalizeDuelBotPresentation(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var bootstrap = root.GetComponent<RemoteThirdPersonPlayerBootstrap>();
            bootstrap?.ApplyRemoteThirdPersonMode();

            ConfigureLocomotionRig(root);
            EnsureSyntyLocomotionDriver(root);
            EnsureRemoteAudio(root);
            EnemyPresentationVisibilityUtility.ConfigureEnemyPresentation(root);

            if (root.GetComponent<TrainingBotLocomotionPresenter>() == null)
            {
                root.AddComponent<TrainingBotLocomotionPresenter>();
            }
            else
            {
                root.GetComponent<TrainingBotLocomotionPresenter>().enabled = true;
            }
        }

        public static void EnsureNetworkRemoteLocomotion(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ConfigureLocomotionRig(root);
            EnsureSyntyLocomotionDriver(root);
            EnsureRemoteAudio(root);
            EnemyPresentationVisibilityUtility.ConfigureEnemyPresentation(root);
        }

        public static void ConfigureLocomotionRig(GameObject root)
        {
            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig == null)
            {
                return;
            }

            locomotionRig.SetNetworkMode(true);
            locomotionRig.SetProceduralVisualsEnabled(GameplayPerformanceOptions.UseProceduralRemoteLocomotion);
        }

        public static SyntyLocomotionDriver EnsureSyntyLocomotionDriver(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            var animator = ResolveSyntyAnimator(root);
            var driver = root.GetComponent<SyntyLocomotionDriver>();
            if (driver == null)
            {
                driver = root.GetComponentInChildren<SyntyLocomotionDriver>(true);
            }

            if (driver == null)
            {
                driver = root.AddComponent<SyntyLocomotionDriver>();
            }

            driver.Configure(animator, null, locomotionRig);
            driver.SetNetworkMode(true);
            var usesBotPresenter = root.GetComponent<TrainingBotLocomotionPresenter>() != null;
            driver.enabled = animator != null && !usesBotPresenter;
            return driver;
        }

        public static void EnsureRemoteAudio(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var remoteAudio = root.GetComponent<PlayerAudioController>();
            if (remoteAudio == null)
            {
                remoteAudio = root.AddComponent<PlayerAudioController>();
            }

            remoteAudio.enabled = true;

            var localAudio = GameplayRuntimeCache.LocalPlayerAudio;
            if (localAudio != null)
            {
                remoteAudio.InheritFrom(localAudio);
            }
        }

        public static Animator ResolveSyntyAnimator(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var thirdPersonBody = root.transform.Find("ThirdPersonBody");
            var syntyVisual = thirdPersonBody != null ? thirdPersonBody.Find("SyntyVisual") : null;
            var animator = syntyVisual != null ? syntyVisual.GetComponent<Animator>() : null;
            return animator != null ? animator : root.GetComponentInChildren<Animator>(true);
        }

        public static void RestoreNetworkRemoteLocomotion(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ConfigureLocomotionRig(root);
            var driver = EnsureSyntyLocomotionDriver(root);
            if (driver != null)
            {
                driver.enabled = true;
            }

            EnsureRemoteAudio(root);
            EnemyPresentationVisibilityUtility.ConfigureEnemyPresentation(root);
        }

        public static void StopLocomotionOnDeath(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var presenter = root.GetComponent<TrainingBotLocomotionPresenter>();
            if (presenter != null)
            {
                presenter.enabled = false;
            }

            var locomotionRig = root.GetComponentInChildren<ProceduralLocomotionRig>(true);
            if (locomotionRig != null)
            {
                locomotionRig.SetNetworkMoveInput(0f, 0f);
                locomotionRig.SetNetworkAnimationState(0f, true, 0, 0f, false, false);
            }

            var driver = root.GetComponent<SyntyLocomotionDriver>();
            if (driver == null)
            {
                driver = root.GetComponentInChildren<SyntyLocomotionDriver>(true);
            }

            if (driver != null)
            {
                driver.enabled = false;
            }

            var animator = ResolveSyntyAnimator(root);
            if (animator != null)
            {
                animator.SetFloat(SyntyLocomotionDriver.SpeedHash, 0f);
                animator.SetFloat(SyntyLocomotionDriver.MoveXHash, 0f);
                animator.SetFloat(SyntyLocomotionDriver.MoveYHash, 0f);
                animator.SetBool(SyntyLocomotionDriver.GroundedHash, true);
                animator.SetBool(SyntyLocomotionDriver.SprintingHash, false);
                animator.SetBool(SyntyLocomotionDriver.CrouchingHash, false);
                animator.SetInteger(SyntyLocomotionDriver.JumpStateHash, 0);
            }
        }

        public static void RestoreTrainingBotLocomotion(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ConfigureLocomotionRig(root);
            EnsureSyntyLocomotionDriver(root);

            var presenter = root.GetComponent<TrainingBotLocomotionPresenter>();
            if (presenter != null)
            {
                presenter.enabled = true;
            }
        }

    }
}
