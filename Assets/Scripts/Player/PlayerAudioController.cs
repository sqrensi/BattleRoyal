using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAudioController : MonoBehaviour
    {
        [Header("Clips")]
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] private AudioClip[] sprintFootstepClips;
        [SerializeField] private AudioClip[] remoteFootstepClips;
        [SerializeField] private AudioClip jumpClip;
        [SerializeField] private AudioClip planeJumpClip;
        [SerializeField] private AudioClip landClip;
        [SerializeField] private AudioClip parachuteOpenClip;
        [SerializeField] private AudioClip shotClip;
        [SerializeField] private AudioClip reloadPullClip;
        [SerializeField] private AudioClip reloadInsertClip;
        [SerializeField] private AudioClip remoteReloadClip;
        [SerializeField] private AudioClip hitPlayerClip;

        [Header("Volume")]
        [Range(0f, 1f)]
        [SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float footstepVolume = 0.35f;
        [Range(0f, 1f)]
        [SerializeField] private float sprintFootstepVolumeMultiplier = 1.12f;
        [Range(0f, 1f)]
        [SerializeField] private float jumpVolume = 0.5f;
        [Range(0f, 1f)]
        [SerializeField] private float planeJumpVolume = 0.62f;
        [Range(0f, 1f)]
        [SerializeField] private float landVolume = 0.48f;
        [Range(0f, 1f)]
        [SerializeField] private float parachuteOpenVolume = 0.58f;
        [Range(0f, 1f)]
        [SerializeField] private float shotVolume = 0.8f;
        [Range(0f, 1f)]
        [SerializeField] private float reloadPullVolume = 0.55f;
        [Range(0f, 1f)]
        [SerializeField] private float reloadInsertVolume = 0.62f;
        [Range(0f, 1f)]
        [SerializeField] private float reloadInsertNormalizedTime = 0.78f;
        [Range(0f, 1f)]
        [SerializeField] private float hitPlayerVolume = 0.65f;
        [Range(0f, 1f)]
        [SerializeField] private float remoteShotVolumeMultiplier = 0.65f;
        [Range(0f, 1f)]
        [SerializeField] private float remoteReloadVolumeMultiplier = 0.6f;

        [Header("Distance")]
        [SerializeField] private float defaultMinDistance = 1.5f;
        [SerializeField] private float defaultMaxDistance = 16f;
        [SerializeField] private float shotMaxDistance = 38f;

        [Header("Wall occlusion")]
        [SerializeField] private bool enableWallOcclusion = true;
        [Range(0f, 1f)]
        [SerializeField] private float occludedVolumeMultiplier = 0.35f;
        [Range(0.5f, 1f)]
        [SerializeField] private float occludedPitchMultiplier = 0.86f;
        [SerializeField] private float occludedLowPassCutoffHz = 900f;

        private AudioSource nearSource;
        private AudioSource shotSource;
        private AudioSource reloadSource;
        private int footstepIndex;
        private int sprintFootstepIndex;
        private Coroutine reloadAudioRoutine;

        private void Awake()
        {
            nearSource = CreateSource("AudioNear", defaultMaxDistance);
            shotSource = CreateSource("AudioShot", shotMaxDistance);
            reloadSource = CreateSource("AudioReload", defaultMaxDistance);
            EnsureDefaultClips();
        }

        private void EnsureDefaultClips()
        {
            if (planeJumpClip == null)
            {
                planeJumpClip = Resources.Load<AudioClip>("BRAudio/PlaneJump");
            }

            if (landClip == null)
            {
                landClip = Resources.Load<AudioClip>("Sounds/3");
            }

            if (parachuteOpenClip == null)
            {
                parachuteOpenClip = Resources.Load<AudioClip>("Sounds/4");
            }
        }

        private static bool HasValidClips(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        public void PlayFootstep(bool isLocal, bool isSprinting = false)
        {
            var clip = GetNextFootstepClip(isLocal, isSprinting);
            if (clip == null)
            {
                return;
            }

            var volume = footstepVolume;
            if (isLocal && isSprinting)
            {
                volume *= sprintFootstepVolumeMultiplier;
            }

            PlayClip(nearSource, clip, volume, isLocal, defaultMaxDistance);
        }

        public void PlayJump(bool isLocal)
        {
            PlayClip(nearSource, jumpClip, jumpVolume, isLocal, defaultMaxDistance);
        }

        public void PlayPlaneJump()
        {
            var clip = planeJumpClip != null ? planeJumpClip : jumpClip;
            PlayClip(nearSource, clip, planeJumpVolume, true, defaultMaxDistance);
        }

        public void PlayLand(bool isLocal)
        {
            PlayClip(nearSource, landClip, landVolume, isLocal, defaultMaxDistance);
        }

        public void PlayParachuteOpen()
        {
            PlayClip(nearSource, parachuteOpenClip, parachuteOpenVolume, true, defaultMaxDistance);
        }

        public void PlayShot(bool isLocal, in WeaponAudioOverrides overrides = default)
        {
            PlayShot(isLocal, transform.position + Vector3.up * 1.35f, overrides);
        }

        public void PlayShot(
            bool isLocal,
            Vector3 soundWorldPosition,
            in WeaponAudioOverrides overrides = default)
        {
            var clip = overrides.HasShotClip ? overrides.ShotClip : shotClip;
            var volume = overrides.ShotVolume > 0f ? overrides.ShotVolume : shotVolume;
            volume *= isLocal ? 1f : Mathf.Clamp01(remoteShotVolumeMultiplier);
            PlayClip(shotSource, clip, volume, isLocal, shotMaxDistance, soundWorldPosition);
        }

        public void PlayReload(bool isLocal)
        {
            PlayReloadSequence(isLocal, 1f);
        }

        public void PlayReloadSequence(
            bool isLocal,
            float durationSeconds,
            in WeaponAudioOverrides overrides = default)
        {
            if (reloadAudioRoutine != null)
            {
                StopCoroutine(reloadAudioRoutine);
                reloadAudioRoutine = null;
            }

            reloadAudioRoutine = StartCoroutine(ReloadAudioRoutine(isLocal, durationSeconds, overrides));
        }

        public void StopReloadAudio()
        {
            if (reloadAudioRoutine != null)
            {
                StopCoroutine(reloadAudioRoutine);
                reloadAudioRoutine = null;
            }

            if (reloadSource != null && reloadSource.isPlaying)
            {
                reloadSource.Stop();
            }
        }

        public void PlayHitPlayer(bool isLocal)
        {
            PlayClip(nearSource, hitPlayerClip, hitPlayerVolume, isLocal, defaultMaxDistance);
        }

        public void InheritFrom(PlayerAudioController source)
        {
            if (source == null || ReferenceEquals(source, this))
            {
                return;
            }

            footstepClips = source.footstepClips;
            sprintFootstepClips = source.sprintFootstepClips;
            remoteFootstepClips = source.remoteFootstepClips;
            jumpClip = source.jumpClip;
            planeJumpClip = source.planeJumpClip;
            landClip = source.landClip;
            parachuteOpenClip = source.parachuteOpenClip;
            shotClip = source.shotClip;
            reloadPullClip = source.reloadPullClip;
            reloadInsertClip = source.reloadInsertClip;
            remoteReloadClip = source.remoteReloadClip;
            hitPlayerClip = source.hitPlayerClip;
            masterVolume = source.masterVolume;
            footstepVolume = source.footstepVolume;
            sprintFootstepVolumeMultiplier = source.sprintFootstepVolumeMultiplier;
            jumpVolume = source.jumpVolume;
            planeJumpVolume = source.planeJumpVolume;
            landVolume = source.landVolume;
            parachuteOpenVolume = source.parachuteOpenVolume;
            shotVolume = source.shotVolume;
            reloadPullVolume = source.reloadPullVolume;
            reloadInsertVolume = source.reloadInsertVolume;
            reloadInsertNormalizedTime = source.reloadInsertNormalizedTime;
            hitPlayerVolume = source.hitPlayerVolume;
            remoteShotVolumeMultiplier = source.remoteShotVolumeMultiplier;
            remoteReloadVolumeMultiplier = source.remoteReloadVolumeMultiplier;
            defaultMinDistance = source.defaultMinDistance;
            defaultMaxDistance = source.defaultMaxDistance;
            shotMaxDistance = source.shotMaxDistance;
            enableWallOcclusion = source.enableWallOcclusion;
            occludedVolumeMultiplier = source.occludedVolumeMultiplier;
            occludedPitchMultiplier = source.occludedPitchMultiplier;
            occludedLowPassCutoffHz = source.occludedLowPassCutoffHz;
        }

        private AudioSource CreateSource(string name, float maxDistance)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = Mathf.Max(0.1f, defaultMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);
            source.dopplerLevel = 0f;
            return source;
        }

        private void PlayClip(AudioSource source, AudioClip clip, float volume, bool isLocal, float maxDistance)
        {
            PlayClip(source, clip, volume, isLocal, maxDistance, transform.position + Vector3.up * 1.35f);
        }

        private void PlayClip(
            AudioSource source,
            AudioClip clip,
            float volume,
            bool isLocal,
            float maxDistance,
            Vector3 soundWorldPosition)
        {
            if (source == null || clip == null || !GameplayAudioPrewarm.TryEnsureLoaded(clip))
            {
                return;
            }

            source.spatialBlend = isLocal ? 0f : 1f;
            source.minDistance = Mathf.Max(0.1f, defaultMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance + 0.1f, maxDistance);

            var finalVolume = Mathf.Clamp01(volume) * Mathf.Clamp01(masterVolume);
            source.pitch = 1f;
            ApplyWallOcclusion(source, isLocal, soundWorldPosition, ref finalVolume);

            source.PlayOneShot(clip, finalVolume);
        }

        private void ApplyWallOcclusion(
            AudioSource source,
            bool isLocal,
            Vector3 soundWorldPosition,
            ref float volume)
        {
            if (isLocal || !enableWallOcclusion || source == null)
            {
                DisableLowPass(source);
                return;
            }

            var settings = new CombatAudioOcclusion.Settings
            {
                OccludedVolumeMultiplier = occludedVolumeMultiplier,
                OccludedPitchMultiplier = occludedPitchMultiplier,
                OccludedLowPassCutoffHz = occludedLowPassCutoffHz,
            };
            var occlusion = CombatAudioOcclusion.Evaluate(soundWorldPosition, transform, settings);
            if (!occlusion.IsOccluded)
            {
                DisableLowPass(source);
                return;
            }

            volume *= occlusion.VolumeMultiplier;
            source.pitch = occlusion.PitchMultiplier;

            var lowPass = source.GetComponent<AudioLowPassFilter>();
            if (lowPass == null)
            {
                lowPass = source.gameObject.AddComponent<AudioLowPassFilter>();
            }

            lowPass.enabled = true;
            lowPass.cutoffFrequency = occlusion.LowPassCutoffHz;
        }

        private static void DisableLowPass(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            var lowPass = source.GetComponent<AudioLowPassFilter>();
            if (lowPass != null)
            {
                lowPass.enabled = false;
            }
        }

        private AudioClip GetNextFootstepClip(bool isLocal, bool isSprinting)
        {
            AudioClip[] clips;
            if (isLocal)
            {
                clips = isSprinting ? sprintFootstepClips : footstepClips;
                if (clips == null || clips.Length == 0)
                {
                    clips = footstepClips;
                }
            }
            else
            {
                // Remote sprint uses the same clips as remote walk; cadence comes from network footstepSeq.
                clips = remoteFootstepClips;
                if (clips == null || clips.Length == 0)
                {
                    clips = footstepClips;
                }
            }

            if (clips == null || clips.Length == 0)
            {
                return null;
            }

            int idx;
            if (isLocal && isSprinting)
            {
                idx = Mathf.Abs(sprintFootstepIndex) % clips.Length;
                sprintFootstepIndex++;
            }
            else
            {
                idx = Mathf.Abs(footstepIndex) % clips.Length;
                footstepIndex++;
            }

            return clips[idx];
        }

        private System.Collections.IEnumerator ReloadAudioRoutine(
            bool isLocal,
            float durationSeconds,
            WeaponAudioOverrides overrides)
        {
            var totalDuration = Mathf.Max(0.1f, durationSeconds);
            var insertNormalizedTime = overrides.ReloadInsertNormalizedTime > 0f
                ? overrides.ReloadInsertNormalizedTime
                : reloadInsertNormalizedTime;
            var insertAt = totalDuration * Mathf.Clamp01(insertNormalizedTime);
            if (!isLocal)
            {
                var remoteClip = overrides.RemoteReloadClip != null
                    ? overrides.RemoteReloadClip
                    : overrides.ReloadPullClip != null
                        ? overrides.ReloadPullClip
                        : remoteReloadClip != null
                            ? remoteReloadClip
                            : reloadPullClip;
                var remoteVolume = (overrides.ReloadPullVolume > 0f
                        ? overrides.ReloadPullVolume
                        : reloadPullVolume) *
                    Mathf.Clamp01(remoteReloadVolumeMultiplier);
                PlayClip(reloadSource, remoteClip, remoteVolume, false, defaultMaxDistance);
                reloadAudioRoutine = null;
                yield break;
            }

            var pullClip = overrides.ReloadPullClip != null ? overrides.ReloadPullClip : reloadPullClip;
            var insertClip = overrides.ReloadInsertClip != null ? overrides.ReloadInsertClip : reloadInsertClip;
            var pullVolume = overrides.ReloadPullVolume > 0f ? overrides.ReloadPullVolume : reloadPullVolume;
            var insertVolume = overrides.ReloadInsertVolume > 0f ? overrides.ReloadInsertVolume : reloadInsertVolume;
            PlayClip(reloadSource, pullClip, pullVolume, isLocal, defaultMaxDistance);
            if (insertClip != null)
            {
                yield return new WaitForSeconds(Mathf.Max(0.01f, insertAt));
                PlayClip(reloadSource, insertClip, insertVolume, isLocal, defaultMaxDistance);
            }

            reloadAudioRoutine = null;
        }
    }
}
