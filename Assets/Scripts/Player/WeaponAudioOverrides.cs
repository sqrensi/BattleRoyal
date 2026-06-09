using UnityEngine;

namespace ShooterPrototype.Player
{
    public struct WeaponAudioOverrides
    {
        public AudioClip ShotClip;
        public AudioClip ReloadPullClip;
        public AudioClip ReloadInsertClip;
        public AudioClip RemoteReloadClip;
        public float ShotVolume;
        public float ReloadPullVolume;
        public float ReloadInsertVolume;
        public float ReloadInsertNormalizedTime;

        public bool HasShotClip => ShotClip != null;
        public bool HasReloadClip =>
            ReloadPullClip != null || ReloadInsertClip != null || RemoteReloadClip != null;

        public static WeaponAudioOverrides FromProfile(WeaponProfile profile)
        {
            if (profile == null)
            {
                return default;
            }

            return profile.CreateAudioOverrides();
        }
    }
}
