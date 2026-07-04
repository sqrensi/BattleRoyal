using UnityEngine;

namespace ShooterPrototype.Player
{
    public static class GameplayAudioPrewarm
    {
        public static void PrewarmCombatClips()
        {
            PrewarmClip(WeaponCatalog.LoadShotClip(WeaponKind.AssaultRifle));
            PrewarmClip(WeaponCatalog.LoadShotClip(WeaponKind.SniperRifle));
            PrewarmClip(WeaponCatalog.LoadShotClip(WeaponKind.Pistol));
            PrewarmClip(WeaponCatalog.LoadShotClip(WeaponKind.Mp7));

            PrewarmClip(WeaponCatalog.LoadReloadPullClip(WeaponKind.AssaultRifle));
            PrewarmClip(WeaponCatalog.LoadReloadPullClip(WeaponKind.SniperRifle));
            PrewarmClip(WeaponCatalog.LoadReloadPullClip(WeaponKind.Pistol));
            PrewarmClip(WeaponCatalog.LoadReloadPullClip(WeaponKind.Mp7));

            PrewarmClip(WeaponCatalog.LoadReloadInsertClip(WeaponKind.AssaultRifle));
            PrewarmClip(WeaponCatalog.LoadReloadInsertClip(WeaponKind.SniperRifle));
            PrewarmClip(WeaponCatalog.LoadReloadInsertClip(WeaponKind.Pistol));
            PrewarmClip(WeaponCatalog.LoadReloadInsertClip(WeaponKind.Mp7));
        }

        public static bool TryEnsureLoaded(AudioClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            switch (clip.loadState)
            {
                case AudioDataLoadState.Loaded:
                    return true;
                case AudioDataLoadState.Failed:
                    return false;
                case AudioDataLoadState.Unloaded:
                    clip.LoadAudioData();
                    break;
            }

            return clip.loadState == AudioDataLoadState.Loaded ||
                   clip.loadState == AudioDataLoadState.Loading;
        }

        private static void PrewarmClip(AudioClip clip)
        {
            TryEnsureLoaded(clip);
        }
    }
}
