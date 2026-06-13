using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Per-weapon stats on the weapon prefab root. Applied when the player equips the weapon.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponProfile : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private WeaponKind weaponKind = WeaponKind.AssaultRifle;

        [Header("Fire")]
        [SerializeField] private bool automatic = true;
        [SerializeField] private float fireRate = 9f;
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private float reloadDuration = 1.8f;

        [Header("Ballistics")]
        [SerializeField] private float maxDistance = 180f;
        [SerializeField] private float bulletDropAngleDegrees = 0.25f;

        [Header("Damage")]
        [SerializeField] private float legDamage = 15f;
        [SerializeField] private float bodyDamage = 25f;
        [SerializeField] private float neckDamage = 80f;
        [SerializeField] private float headDamage = 100f;

        [Header("Spray / Recoil")]
        [SerializeField] private float spreadStartDegrees = 0.08f;
        [SerializeField] private float spreadPerShotDegrees = 0.22f;
        [SerializeField] private float spreadMaxDegrees = 2.3f;
        [SerializeField] private float hipFireSpreadMultiplier = 1.75f;
        [SerializeField] private float adsSpreadMultiplier = 0.65f;
        [SerializeField] private float hipFireRecoilMultiplier = 1f;
        [SerializeField] private float adsRecoilMultiplier = 0.72f;

        [Header("Aim Down Sights")]
        [SerializeField] private bool hasScope = false;
        [SerializeField] private float adsCameraFov = 55f;
        [SerializeField] private float adsMaxLookAngle = 40f;
        [SerializeField] private float adsLookSensitivityMultiplier = 1f;
        [SerializeField] private bool overrideAdsCameraPose;
        [SerializeField] private Vector3 adsCameraLocalPosition = new Vector3(0f, -0.14f, 0.35f);
        [SerializeField] private Vector3 adsCameraLocalEuler = new Vector3(0f, -1f, 0f);

        [Header("Presentation")]
        [SerializeField] private float handsScaleMultiplier = 1f;

        [Header("Effects")]
        [SerializeField] private GameObject muzzleFlashVfx;
        [SerializeField] private AudioClip shotClip;
        [SerializeField] private AudioClip reloadPullClip;
        [SerializeField] private AudioClip reloadInsertClip;
        [SerializeField] private AudioClip remoteReloadClip;
        [SerializeField] private float shotVolume = 0.85f;
        [SerializeField] private float reloadPullVolume = 0.55f;
        [SerializeField] private float reloadInsertVolume = 0.62f;
        [SerializeField] private float reloadInsertNormalizedTime = 0.78f;

        public WeaponKind Kind => weaponKind;
        public bool HasScope => hasScope;
        public float HandsScaleMultiplier => handsScaleMultiplier;
        public GameObject MuzzleFlashVfx => muzzleFlashVfx;
        public float ReloadInsertNormalizedTime => reloadInsertNormalizedTime;
        public float ReloadDuration => reloadDuration;

        public WeaponAudioOverrides CreateAudioOverrides()
        {
            return new WeaponAudioOverrides
            {
                ShotClip = shotClip,
                ReloadPullClip = reloadPullClip,
                ReloadInsertClip = reloadInsertClip,
                RemoteReloadClip = remoteReloadClip,
                ShotVolume = shotVolume,
                ReloadPullVolume = reloadPullVolume,
                ReloadInsertVolume = reloadInsertVolume,
                ReloadInsertNormalizedTime = reloadInsertNormalizedTime
            };
        }

        public void ApplyTo(
            PlayerWeaponController weaponController,
            PlayerWeaponMount weaponMount,
            FpsCharacterController fpsController)
        {
            if (weaponController != null)
            {
                weaponController.ApplyWeaponProfile(this);
            }

            if (weaponMount != null)
            {
                weaponMount.ApplyWeaponProfile(this);
            }

            if (fpsController != null)
            {
                fpsController.ConfigureAdsMaxLookAngle(adsMaxLookAngle);
                fpsController.ConfigureAdsLookSensitivityMultiplier(adsLookSensitivityMultiplier);
            }
        }

        public static WeaponProfile CreateRuntimeAssaultDefaults(GameObject host)
        {
            var profile = host.AddComponent<WeaponProfile>();
            profile.weaponKind = WeaponKind.AssaultRifle;
            profile.automatic = true;
            profile.fireRate = 9f;
            profile.magazineSize = 30;
            profile.reloadDuration = 1.8f;
            profile.maxDistance = 180f;
            profile.bulletDropAngleDegrees = 0.25f;
            profile.legDamage = 15f;
            profile.bodyDamage = 25f;
            profile.neckDamage = 80f;
            profile.headDamage = 100f;
            profile.spreadStartDegrees = 0.08f;
            profile.spreadPerShotDegrees = 0.22f;
            profile.spreadMaxDegrees = 2.3f;
            profile.hipFireSpreadMultiplier = 1.75f;
            profile.adsSpreadMultiplier = 0.65f;
            profile.hipFireRecoilMultiplier = 1f;
            profile.adsRecoilMultiplier = 0.72f;
            profile.hasScope = false;
            profile.adsCameraFov = 55f;
            profile.adsMaxLookAngle = 40f;
            profile.adsLookSensitivityMultiplier = 1.85f;
            profile.overrideAdsCameraPose = false;
            profile.handsScaleMultiplier = 1f;
            return profile;
        }

        private static void ApplyDefaultSniperEffects(WeaponProfile profile)
        {
            profile.muzzleFlashVfx = WeaponCatalog.LoadMuzzleFlash(WeaponKind.SniperRifle);
            profile.shotClip = WeaponCatalog.LoadShotClip(WeaponKind.SniperRifle);
            profile.reloadPullClip = WeaponCatalog.LoadReloadPullClip(WeaponKind.SniperRifle);
            profile.reloadInsertClip = WeaponCatalog.LoadReloadInsertClip(WeaponKind.SniperRifle);
            profile.remoteReloadClip = profile.reloadPullClip;
            profile.shotVolume = 0.92f;
            profile.reloadPullVolume = 0.48f;
            profile.reloadInsertVolume = 0.56f;
            profile.reloadInsertNormalizedTime = 0.72f;
        }

        private static void ApplyDefaultPistolEffects(WeaponProfile profile)
        {
            profile.muzzleFlashVfx = WeaponCatalog.LoadMuzzleFlash(WeaponKind.Pistol);
            profile.shotClip = WeaponCatalog.LoadShotClip(WeaponKind.Pistol);
            profile.reloadPullClip = WeaponCatalog.LoadReloadPullClip(WeaponKind.Pistol);
            profile.reloadInsertClip = WeaponCatalog.LoadReloadInsertClip(WeaponKind.Pistol);
            profile.remoteReloadClip = profile.reloadPullClip;
            profile.shotVolume = 0.72f;
            profile.reloadPullVolume = 0.42f;
            profile.reloadInsertVolume = 0.5f;
            profile.reloadInsertNormalizedTime = 0.74f;
        }

        public static WeaponProfile CreateRuntimeSniperDefaults(GameObject host)
        {
            var profile = host.AddComponent<WeaponProfile>();
            profile.weaponKind = WeaponKind.SniperRifle;
            profile.automatic = true;
            profile.fireRate = 1.15f;
            profile.magazineSize = 7;
            profile.reloadDuration = 2.85f;
            profile.maxDistance = 260f;
            profile.bulletDropAngleDegrees = 0.18f;
            profile.legDamage = 45f;
            profile.bodyDamage = 62f;
            profile.neckDamage = 88f;
            profile.headDamage = 110f;
            profile.spreadStartDegrees = 0.04f;
            profile.spreadPerShotDegrees = 0.08f;
            profile.spreadMaxDegrees = 0.9f;
            profile.hipFireSpreadMultiplier = 2.4f;
            profile.adsSpreadMultiplier = 0.12f;
            profile.hipFireRecoilMultiplier = 1.35f;
            profile.adsRecoilMultiplier = 0.55f;
            profile.hasScope = true;
            profile.adsCameraFov = 22f;
            profile.adsMaxLookAngle = 28f;
            profile.adsLookSensitivityMultiplier = 2.6f;
            profile.overrideAdsCameraPose = true;
            profile.adsCameraLocalPosition = new Vector3(0f, -0.1f, 0.33f);
            profile.adsCameraLocalEuler = new Vector3(1.5f, 0f, 0f);
            profile.handsScaleMultiplier = 1.45f;
            ApplyDefaultSniperEffects(profile);
            return profile;
        }

        public static WeaponProfile CreateRuntimePistolDefaults(GameObject host)
        {
            var profile = host.AddComponent<WeaponProfile>();
            profile.weaponKind = WeaponKind.Pistol;
            profile.automatic = false;
            profile.fireRate = 4.5f;
            profile.magazineSize = 12;
            profile.reloadDuration = 1.35f;
            profile.maxDistance = 90f;
            profile.bulletDropAngleDegrees = 0.2f;
            profile.legDamage = 12f;
            profile.bodyDamage = 22f;
            profile.neckDamage = 55f;
            profile.headDamage = 85f;
            profile.spreadStartDegrees = 0.12f;
            profile.spreadPerShotDegrees = 0.28f;
            profile.spreadMaxDegrees = 2.8f;
            profile.hipFireSpreadMultiplier = 2f;
            profile.adsSpreadMultiplier = 0.45f;
            profile.hipFireRecoilMultiplier = 0.85f;
            profile.adsRecoilMultiplier = 0.6f;
            profile.hasScope = false;
            profile.adsCameraFov = 58f;
            profile.adsMaxLookAngle = 38f;
            profile.adsLookSensitivityMultiplier = 1.4f;
            profile.overrideAdsCameraPose = true;
            profile.adsCameraLocalPosition = new Vector3(0f, -0.12f, 0.28f);
            profile.adsCameraLocalEuler = new Vector3(0.5f, 0f, 0f);
            profile.handsScaleMultiplier = 1.05f;
            ApplyDefaultPistolEffects(profile);
            return profile;
        }

        private static void ApplyDefaultMp7Effects(WeaponProfile profile)
        {
            profile.muzzleFlashVfx = WeaponCatalog.LoadMuzzleFlash(WeaponKind.Mp7);
            profile.shotClip = WeaponCatalog.LoadShotClip(WeaponKind.Mp7);
            profile.reloadPullClip = WeaponCatalog.LoadReloadPullClip(WeaponKind.Mp7);
            profile.reloadInsertClip = WeaponCatalog.LoadReloadInsertClip(WeaponKind.Mp7);
            profile.remoteReloadClip = profile.reloadPullClip;
            profile.shotVolume = 0.78f;
            profile.reloadPullVolume = 0.45f;
            profile.reloadInsertVolume = 0.52f;
            profile.reloadInsertNormalizedTime = 0.76f;
        }

        public static WeaponProfile CreateRuntimeMp7Defaults(GameObject host)
        {
            var profile = host.AddComponent<WeaponProfile>();
            profile.weaponKind = WeaponKind.Mp7;
            profile.automatic = true;
            profile.fireRate = 13f;
            profile.magazineSize = 30;
            profile.reloadDuration = 1.65f;
            profile.maxDistance = 120f;
            profile.bulletDropAngleDegrees = 0.22f;
            profile.legDamage = 11f;
            profile.bodyDamage = 18f;
            profile.neckDamage = 48f;
            profile.headDamage = 72f;
            profile.spreadStartDegrees = 0.1f;
            profile.spreadPerShotDegrees = 0.18f;
            profile.spreadMaxDegrees = 3.2f;
            profile.hipFireSpreadMultiplier = 2.1f;
            profile.adsSpreadMultiplier = 0.55f;
            profile.hipFireRecoilMultiplier = 0.95f;
            profile.adsRecoilMultiplier = 0.68f;
            profile.hasScope = false;
            profile.adsCameraFov = 52f;
            profile.adsMaxLookAngle = 40f;
            profile.adsLookSensitivityMultiplier = 1.55f;
            profile.overrideAdsCameraPose = true;
            profile.adsCameraLocalPosition = new Vector3(0f, -0.13f, 0.32f);
            profile.adsCameraLocalEuler = new Vector3(0.5f, 0f, 0f);
            profile.handsScaleMultiplier = 1.15f;
            ApplyDefaultMp7Effects(profile);
            return profile;
        }

        internal void CopyTo(PlayerWeaponController controller)
        {
            controller.SetFromWeaponProfile(
                automatic,
                fireRate,
                magazineSize,
                reloadDuration,
                maxDistance,
                bulletDropAngleDegrees,
                legDamage,
                bodyDamage,
                neckDamage,
                headDamage,
                spreadStartDegrees,
                spreadPerShotDegrees,
                spreadMaxDegrees,
                hipFireSpreadMultiplier,
                adsSpreadMultiplier,
                hipFireRecoilMultiplier,
                adsRecoilMultiplier);
        }

        internal void CopyMountSettingsTo(PlayerWeaponMount mount)
        {
            mount.SetAdsCameraFov(adsCameraFov);
            mount.SetHandsScaleMultiplier(handsScaleMultiplier);
            if (overrideAdsCameraPose)
            {
                mount.SetAdsCameraPose(adsCameraLocalPosition, adsCameraLocalEuler);
            }
            else
            {
                mount.RestoreDefaultAdsCameraPose();
            }
        }
    }
}
