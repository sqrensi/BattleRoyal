using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Network;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerWeaponController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform muzzle;
        [SerializeField] private string muzzleName = "Muzzle";

        [Header("Weapon")]
        [SerializeField] private bool automatic = true;
        [SerializeField] private float fireRate = 9f;
        [SerializeField] private float maxDistance = 180f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private bool detectTriggerHitboxes = true;
        [SerializeField] private float legDamage = 15f;
        [SerializeField] private float bodyDamage = 25f;
        [SerializeField] private float neckDamage = 80f;
        [SerializeField] private float headDamage = 100f;
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private float reloadDuration = 1.8f;
        [SerializeField] private bool autoReloadWhenEmpty = true;

        [Header("Wall Collision Fire Block")]
        [SerializeField] private bool blockFireWhenWeaponCollides = true;
        [SerializeField, Range(0f, 1f)] private float fireBlockWallAvoidThreshold = 0.3f;
        [SerializeField] private bool blockFireWhenSprinting = true;

        [Header("Ballistics")]
        [SerializeField, Tooltip("Downward pitch on the sight ray; at ~100 m impacts sit slightly below the crosshair.")]
        private float bulletDropAngleDegrees = 0.25f;

        [Header("Spray / Recoil")]
        [SerializeField] private float sprayResetDelay = 0.24f;
        [SerializeField] private float spreadStartDegrees = 0.08f;
        [SerializeField] private float spreadPerShotDegrees = 0.22f;
        [SerializeField] private float spreadMaxDegrees = 2.3f;
        [SerializeField] private float recoilPitchMin = 0.38f;
        [SerializeField] private float recoilPitchMax = 0.58f;
        [SerializeField] private float recoilYawScale = 0.55f;
        [SerializeField] private float hipFireSpreadMultiplier = 1.75f;
        [SerializeField] private float adsSpreadMultiplier = 0.65f;
        [SerializeField] private float crouchSpreadMultiplier = 0.82f;
        [SerializeField] private float movingSpreadMultiplier = 1.9f;
        [SerializeField] private float jumpSpreadMultiplier = 2.8f;
        [SerializeField] private float movingSpreadInputThreshold = 0.08f;
        [SerializeField] private float hipFireRecoilMultiplier = 1f;
        [SerializeField] private float adsRecoilMultiplier = 0.72f;
        [SerializeField] private float crouchRecoilMultiplier = 0.85f;
        [SerializeField] private Vector2[] sprayPattern = new[]
        {
            new Vector2(0.0f, 0.85f), new Vector2(0.08f, 0.95f), new Vector2(-0.1f, 1.05f),
            new Vector2(0.14f, 1.15f), new Vector2(-0.18f, 1.22f), new Vector2(0.22f, 1.28f),
            new Vector2(-0.26f, 1.33f), new Vector2(0.3f, 1.37f), new Vector2(-0.34f, 1.4f),
            new Vector2(0.38f, 1.42f), new Vector2(-0.42f, 1.44f), new Vector2(0.46f, 1.45f)
        };

        [Header("Tracer")]
        [SerializeField] private bool showTracer = true;
        [SerializeField] private float tracerDuration = 0.05f;
        [SerializeField] private float tracerWidth = 0.01f;
        [SerializeField] private Color tracerColor = new Color(1f, 0.92f, 0.72f, 0.95f);

        [Header("VFX")]
        [SerializeField] private GameObject muzzleFlashVfx;
        [SerializeField] private GameObject playerHitVfx;
        [SerializeField] private GameObject worldHitVfx;
        [SerializeField] private float vfxAutoDestroySeconds = 2f;
        [SerializeField] private float headshotSingleRegistrationSeconds = 3.2f;

        private float nextFireTime;
        private float lastShotAt = -100f;
        private int burstShotCount;
        private int shotSequence;
        private int reloadSequence;
        private int hitPlayerSequence;
        private Vector3 lastShotOrigin;
        private Vector3 lastShotDirection = Vector3.forward;
        private Vector3 lastShotEndPoint;
        private bool lastShotHasEndPoint;
        private int currentAmmo;
        private bool isReloading;
        private Coroutine reloadCoroutine;
        private Material tracerMaterial;
        private FpsCharacterController fpsController;
        private PlayerWeaponMount weaponMount;
        private PlayerWeaponLoadoutController weaponLoadoutController;
        private PlayerAudioController audioController;
        private RealtimeTransportClient realtimeClient;
        private PlayerWeaponHolsterController weaponHolster;
        private readonly RaycastHit[] hitQueryBuffer = new RaycastHit[32];
        private readonly Dictionary<string, float> headshotRegistrationUntilByTarget = new Dictionary<string, float>();
        private WeaponStatDefaults weaponDefaults;
        private WeaponKind currentWeaponKind = WeaponKind.AssaultRifle;

        public WeaponKind CurrentWeaponKind => currentWeaponKind;

        public void Configure(Camera localCamera, Transform weaponMuzzle)
        {
            playerCamera = localCamera;
            muzzle = weaponMuzzle;
        }

        public int LastShotSequence => shotSequence;
        public Vector3 LastShotOrigin => lastShotOrigin;
        public Vector3 LastShotDirection => lastShotDirection;
        public Vector3 LastShotEndPoint => lastShotEndPoint;
        public bool LastShotHasEndPoint => lastShotHasEndPoint;
        public int LastReloadSequence => reloadSequence;
        public int LastHitPlayerSequence => hitPlayerSequence;
        public int CurrentAmmo => currentAmmo;
        public int MagazineSize => Mathf.Max(1, magazineSize);

        public void SetCurrentAmmo(int ammo)
        {
            currentAmmo = Mathf.Clamp(ammo, 0, MagazineSize);
        }
        public bool IsReloading => isReloading;
        public float ReloadDurationSeconds => Mathf.Max(0.05f, reloadDuration);
        public GameObject MuzzleFlashVfx => muzzleFlashVfx;
        public GameObject WorldHitVfx => worldHitVfx;
        public GameObject PlayerHitVfx => playerHitVfx;
        public float ShotMaxDistance => maxDistance;

        private void Awake()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }

            fpsController = GetComponent<FpsCharacterController>();
            weaponMount = GetComponent<PlayerWeaponMount>();
            weaponLoadoutController = GetComponent<PlayerWeaponLoadoutController>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            audioController = GetComponent<PlayerAudioController>();
            realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            currentAmmo = Mathf.Max(1, magazineSize);
            weaponDefaults = CaptureWeaponDefaults();
            RefreshWeaponAvailability();
        }

        public void ApplyWeaponProfile(WeaponProfile profile, bool resetAmmo = true)
        {
            CancelActiveReload();
            if (profile == null)
            {
                RestoreDefaultWeaponProfile();
            }
            else
            {
                profile.CopyTo(this);
                currentWeaponKind = profile.Kind;
            }

            if (resetAmmo)
            {
                currentAmmo = MagazineSize;
            }

            burstShotCount = 0;
            nextFireTime = 0f;
        }

        public void RestoreDefaultWeaponProfile()
        {
            ApplyWeaponDefaults(weaponDefaults);
            currentWeaponKind = WeaponKind.AssaultRifle;
            fpsController?.RestoreDefaultAdsMaxLookAngle();
            fpsController?.RestoreDefaultAdsLookSensitivityMultiplier();
            weaponMount?.RestoreDefaultAdsCameraFov();
            weaponMount?.RestoreDefaultAdsCameraPose();
            weaponMount?.RestoreDefaultHandsScale();
        }

        internal void SetFromWeaponProfile(
            bool profileAutomatic,
            float profileFireRate,
            int profileMagazineSize,
            float profileReloadDuration,
            float profileMaxDistance,
            float profileBulletDropAngleDegrees,
            float profileLegDamage,
            float profileBodyDamage,
            float profileNeckDamage,
            float profileHeadDamage,
            float profileSpreadStartDegrees,
            float profileSpreadPerShotDegrees,
            float profileSpreadMaxDegrees,
            float profileHipFireSpreadMultiplier,
            float profileAdsSpreadMultiplier,
            float profileHipFireRecoilMultiplier,
            float profileAdsRecoilMultiplier)
        {
            automatic = profileAutomatic;
            fireRate = profileFireRate;
            magazineSize = profileMagazineSize;
            reloadDuration = profileReloadDuration;
            maxDistance = profileMaxDistance;
            bulletDropAngleDegrees = profileBulletDropAngleDegrees;
            legDamage = profileLegDamage;
            bodyDamage = profileBodyDamage;
            neckDamage = profileNeckDamage;
            headDamage = profileHeadDamage;
            spreadStartDegrees = profileSpreadStartDegrees;
            spreadPerShotDegrees = profileSpreadPerShotDegrees;
            spreadMaxDegrees = profileSpreadMaxDegrees;
            hipFireSpreadMultiplier = profileHipFireSpreadMultiplier;
            adsSpreadMultiplier = profileAdsSpreadMultiplier;
            hipFireRecoilMultiplier = profileHipFireRecoilMultiplier;
            adsRecoilMultiplier = profileAdsRecoilMultiplier;
        }

        private WeaponStatDefaults CaptureWeaponDefaults()
        {
            return new WeaponStatDefaults
            {
                automatic = automatic,
                fireRate = fireRate,
                magazineSize = magazineSize,
                reloadDuration = reloadDuration,
                maxDistance = maxDistance,
                bulletDropAngleDegrees = bulletDropAngleDegrees,
                legDamage = legDamage,
                bodyDamage = bodyDamage,
                neckDamage = neckDamage,
                headDamage = headDamage,
                spreadStartDegrees = spreadStartDegrees,
                spreadPerShotDegrees = spreadPerShotDegrees,
                spreadMaxDegrees = spreadMaxDegrees,
                hipFireSpreadMultiplier = hipFireSpreadMultiplier,
                adsSpreadMultiplier = adsSpreadMultiplier,
                hipFireRecoilMultiplier = hipFireRecoilMultiplier,
                adsRecoilMultiplier = adsRecoilMultiplier
            };
        }

        private void ApplyWeaponDefaults(WeaponStatDefaults defaults)
        {
            SetFromWeaponProfile(
                defaults.automatic,
                defaults.fireRate,
                defaults.magazineSize,
                defaults.reloadDuration,
                defaults.maxDistance,
                defaults.bulletDropAngleDegrees,
                defaults.legDamage,
                defaults.bodyDamage,
                defaults.neckDamage,
                defaults.headDamage,
                defaults.spreadStartDegrees,
                defaults.spreadPerShotDegrees,
                defaults.spreadMaxDegrees,
                defaults.hipFireSpreadMultiplier,
                defaults.adsSpreadMultiplier,
                defaults.hipFireRecoilMultiplier,
                defaults.adsRecoilMultiplier);
        }

        private struct WeaponStatDefaults
        {
            public bool automatic;
            public float fireRate;
            public int magazineSize;
            public float reloadDuration;
            public float maxDistance;
            public float bulletDropAngleDegrees;
            public float legDamage;
            public float bodyDamage;
            public float neckDamage;
            public float headDamage;
            public float spreadStartDegrees;
            public float spreadPerShotDegrees;
            public float spreadMaxDegrees;
            public float hipFireSpreadMultiplier;
            public float adsSpreadMultiplier;
            public float hipFireRecoilMultiplier;
            public float adsRecoilMultiplier;
        }

        public void RefreshWeaponAvailability()
        {
            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
            }

            enabled = weaponMount != null && weaponMount.HasMountedWeapon;
        }

        private void OnEnable()
        {
            fpsController?.SetAutoRecoilRecoveryActive(false);
        }

        private void Update()
        {
            TryResolveRuntimeMuzzle();
            var firePressed = ReadFirePressed();

            if (!enabled)
            {
                return;
            }

            var medkit = GetComponent<PlayerMedkitController>();
            if (medkit != null && medkit.IsUsingMedkit)
            {
                return;
            }

            if (weaponHolster != null && weaponHolster.IsMedkitWeaponLocked)
            {
                return;
            }

            if (weaponHolster != null && !weaponHolster.IsWeaponReady)
            {
                return;
            }

            if (ReadReloadPressed())
            {
                TryStartReload();
            }

            // Reload input has priority over fire cadence: pressing reload while shooting
            // immediately stops firing and starts the reload flow.
            if (isReloading)
            {
                return;
            }

            if (Time.time < nextFireTime)
            {
                return;
            }

            if (!firePressed)
            {
                return;
            }

            if (currentAmmo <= 0)
            {
                if (autoReloadWhenEmpty)
                {
                    TryStartReload();
                }
                return;
            }

            if (ShouldBlockFireByWallCollision())
            {
                return;
            }

            if (ShouldBlockFireBySprint())
            {
                return;
            }

            FireOnce();
        }

        private bool ReadFirePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null)
            {
                return false;
            }
            return automatic
                ? Mouse.current.leftButton.isPressed
                : Mouse.current.leftButton.wasPressedThisFrame;
#else
            return automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
#endif
        }

        private void FireOnce()
        {
            nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
            UpdateBurstState();
            TryResolveRuntimeMuzzle();

            TryResolveCrosshairShot(out var shot);
            lastShotOrigin = shot.Origin;
            lastShotDirection = shot.Direction;
            shotSequence++;
            currentAmmo = Mathf.Max(0, currentAmmo - 1);
            SyncLoadoutMagAmmo();
            SimulateShotEffects(shot, applyRecoil: true);
            CaptureNetworkShotImpact(shot);
            SendNetworkShotEvent();
            GetComponent<MatchPresenceSync>()?.SendLocalPoseImmediate();
            audioController?.PlayShot(true, ResolveWeaponAudioOverrides());
            if (autoReloadWhenEmpty && currentAmmo <= 0)
            {
                TryStartReload();
            }
        }

        public void PlayRemoteShot(Vector3 networkOrigin, Vector3 networkDirection, float lookPitch)
        {
            TryResolveRuntimeMuzzle();
            var hasNetworkShot = networkDirection.sqrMagnitude > 0.0001f;
            CrosshairShot shot;
            if (hasNetworkShot)
            {
                shot = BuildCrosshairShotFromRay(networkOrigin, networkDirection);
            }
            else
            {
                var fallbackOrigin = playerCamera != null
                    ? playerCamera.transform.position
                    : transform.position + Vector3.up * 1.65f;
                shot = BuildCrosshairShotFromRay(fallbackOrigin, ResolveRemoteShootDirection(lookPitch));
            }

            SimulateShotEffects(shot, applyRecoil: false);
            audioController?.PlayShot(false, ResolveWeaponAudioOverrides());
        }

        public void PlayRemoteReload(float durationSeconds)
        {
            weaponMount?.PlayReloadAnimation(durationSeconds > 0f ? durationSeconds : ReloadDurationSeconds);
            audioController?.PlayReloadSequence(false, durationSeconds > 0f ? durationSeconds : ReloadDurationSeconds);
        }

        public void PlayRemoteHitPlayer()
        {
            audioController?.PlayHitPlayer(false);
        }

        public void RestoreAfterRespawn()
        {
            currentAmmo = MagazineSize;
            isReloading = false;
            if (reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                reloadCoroutine = null;
            }

            weaponMount?.SetLocalReloading(false);
            weaponHolster?.ForceArmedState();
            RefreshWeaponAvailability();
        }

        public void CancelActiveReload()
        {
            isReloading = false;
            if (reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                reloadCoroutine = null;
            }

            weaponMount?.StopReloadAnimation();
            weaponMount?.SetLocalReloading(false);
            audioController?.StopReloadAudio();
        }

        private struct CrosshairShot
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public Vector3 AimPoint;
            public bool HasHit;
            public RaycastHit Hit;
        }

        private bool TryResolveCrosshairShot(out CrosshairShot shot)
        {
            shot = new CrosshairShot
            {
                Origin = transform.position + Vector3.up * 1.65f,
                Direction = transform.forward,
                AimPoint = transform.position + transform.forward * maxDistance,
                HasHit = false
            };

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }

            if (ResolveAimPivot() == null && playerCamera == null)
            {
                return false;
            }

            var ray = BuildGameplayAimRay();
            ray = ApplySpreadToRay(ray);
            ray.direction = ApplyBulletDrop(ray.direction);

            shot.Origin = ray.origin;
            shot.Direction = ray.direction.normalized;
            shot.AimPoint = ray.origin + shot.Direction * maxDistance;
            if (TryRaycastIgnoringSelf(ray, maxDistance, out var hit))
            {
                shot.HasHit = true;
                shot.Hit = hit;
                shot.AimPoint = hit.point;
            }

            return shot.HasHit;
        }

        private Vector3 ApplyBulletDrop(Vector3 direction)
        {
            if (bulletDropAngleDegrees <= 0.0001f)
            {
                return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            }

            var pivot = ResolveAimPivot();
            var rightAxis = pivot != null ? pivot.right : Vector3.right;
            var normalized = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : pivot != null
                    ? pivot.forward
                    : Vector3.forward;
            return (Quaternion.AngleAxis(bulletDropAngleDegrees, rightAxis) * normalized).normalized;
        }

        private Transform ResolveAimPivot()
        {
            if (fpsController != null && fpsController.CameraPivot != null)
            {
                return fpsController.CameraPivot;
            }

            if (playerCamera != null && playerCamera.transform.parent != null)
            {
                return playerCamera.transform.parent;
            }

            return playerCamera != null ? playerCamera.transform : transform;
        }

        private Ray BuildGameplayAimRay()
        {
            var pivot = ResolveAimPivot();
            if (pivot != null)
            {
                return new Ray(pivot.position, pivot.forward);
            }

            if (playerCamera != null)
            {
                return playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            }

            return new Ray(transform.position + Vector3.up * 1.65f, transform.forward);
        }

        private void SendNetworkShotEvent()
        {
            if (realtimeClient == null)
            {
                realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            }

            realtimeClient?.SendShotEvent(
                shotSequence,
                lastShotOrigin,
                lastShotDirection,
                lastShotEndPoint,
                lastShotHasEndPoint);
        }

        private void CaptureNetworkShotImpact(CrosshairShot shot)
        {
            if (TryRaycastIgnoringSelf(new Ray(shot.Origin, shot.Direction), maxDistance, out var gameplayHit))
            {
                lastShotEndPoint = gameplayHit.point;
                lastShotHasEndPoint = true;
                return;
            }

            if (TryResolveNetworkImpactPoint(out var networkImpactPoint))
            {
                lastShotEndPoint = networkImpactPoint;
                lastShotHasEndPoint = true;
                return;
            }

            if (shot.HasHit)
            {
                lastShotEndPoint = shot.AimPoint;
                lastShotHasEndPoint = true;
                return;
            }

            lastShotHasEndPoint = false;
            lastShotEndPoint = shot.Origin + shot.Direction * maxDistance;
        }

        private RaycastHit? ResolveVisualImpactHit(CrosshairShot shot, RaycastHit? gameplayHit)
        {
            if (gameplayHit.HasValue && IsPlayerHit(gameplayHit.Value.collider))
            {
                return gameplayHit;
            }

            if (shot.HasHit && IsPlayerHit(shot.Hit.collider))
            {
                return shot.Hit;
            }

            if (gameplayHit.HasValue)
            {
                return gameplayHit;
            }

            if (shot.HasHit)
            {
                return shot.Hit;
            }

            return null;
        }

        private bool TryResolveNetworkImpactPoint(out Vector3 impactPoint)
        {
            impactPoint = default;
            if (ResolveAimPivot() == null && playerCamera == null)
            {
                return false;
            }

            // Network uses crosshair center without spread so observers match the reticle.
            var ray = BuildGameplayAimRay();
            ray.direction = ApplyBulletDrop(ray.direction);
            if (!TryRaycastIgnoringSelf(ray, maxDistance, out var hit))
            {
                return false;
            }

            impactPoint = hit.point;
            return true;
        }

        private CrosshairShot BuildCrosshairShotFromRay(Vector3 origin, Vector3 direction)
        {
            var normalizedDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            var result = new CrosshairShot
            {
                Origin = origin,
                Direction = normalizedDirection,
                AimPoint = origin + normalizedDirection * maxDistance,
                HasHit = false
            };

            if (TryRaycastIgnoringSelf(new Ray(origin, normalizedDirection), maxDistance, out var hit))
            {
                result.HasHit = true;
                result.Hit = hit;
                result.AimPoint = hit.point;
            }

            return result;
        }

        private void UpdateBurstState()
        {
            if (Time.time - lastShotAt > Mathf.Max(0.01f, sprayResetDelay))
            {
                burstShotCount = 0;
            }

            burstShotCount++;
            lastShotAt = Time.time;
        }

        private Ray ApplySpreadToRay(Ray baseRay)
        {
            var (spreadMultiplier, _) = ResolveStanceMultipliers();
            var spread = spreadStartDegrees + (burstShotCount - 1) * Mathf.Max(0f, spreadPerShotDegrees);
            spread *= Mathf.Max(0.05f, spreadMultiplier);
            spread = Mathf.Clamp(spread, 0f, Mathf.Max(0f, spreadMaxDegrees));

            var patternOffset = Vector2.zero;
            if (sprayPattern != null && sprayPattern.Length > 0)
            {
                var index = Mathf.Clamp(burstShotCount - 1, 0, sprayPattern.Length - 1);
                patternOffset = sprayPattern[index];
            }

            var randomOffset = Random.insideUnitCircle * spread * 0.18f;
            var yawOffset = patternOffset.x * recoilYawScale + randomOffset.x;
            var pitchOffset = patternOffset.y * 0.28f + randomOffset.y;
            var pivot = ResolveAimPivot();
            var pitchAxis = pivot != null ? pivot.right : Vector3.right;
            var offsetRotation = Quaternion.AngleAxis(yawOffset, Vector3.up) *
                                 Quaternion.AngleAxis(-pitchOffset, pitchAxis);
            return new Ray(baseRay.origin, offsetRotation * baseRay.direction);
        }

        private void ApplyRecoilKick()
        {
            if (fpsController == null)
            {
                return;
            }

            var (_, recoilMultiplier) = ResolveStanceMultipliers();
            recoilMultiplier = Mathf.Max(0.05f, recoilMultiplier);

            var pattern = Vector2.zero;
            if (sprayPattern != null && sprayPattern.Length > 0)
            {
                var index = Mathf.Clamp(burstShotCount - 1, 0, sprayPattern.Length - 1);
                pattern = sprayPattern[index];
            }

            var pitch = Random.Range(Mathf.Min(recoilPitchMin, recoilPitchMax), Mathf.Max(recoilPitchMin, recoilPitchMax));
            pitch += pattern.y * 0.38f;
            var yaw = pattern.x * recoilYawScale + Random.Range(-0.18f, 0.18f);
            pitch *= recoilMultiplier;
            yaw *= recoilMultiplier;
            fpsController.ApplyRecoil(pitch, yaw);

            var shakeIntensity = recoilMultiplier;
            if (weaponMount != null && weaponMount.AdsBlend > 0.5f)
            {
                shakeIntensity *= 0.55f;
            }

            fpsController.ApplyShootShake(shakeIntensity);
        }

        private (float Spread, float Recoil) ResolveStanceMultipliers()
        {
            var isAds = weaponMount != null
                ? weaponMount.AdsBlend > 0.5f
                : ReadAimPressed();
            var spread = isAds ? adsSpreadMultiplier : hipFireSpreadMultiplier;
            var recoil = isAds ? adsRecoilMultiplier : hipFireRecoilMultiplier;

            if (fpsController != null && fpsController.IsCrouching)
            {
                spread *= crouchSpreadMultiplier;
                recoil *= crouchRecoilMultiplier;
            }

            if (fpsController != null)
            {
                var moving = fpsController.IsGrounded && fpsController.MoveInputMagnitude > Mathf.Clamp01(movingSpreadInputThreshold);
                if (moving)
                {
                    spread *= Mathf.Max(1f, movingSpreadMultiplier);
                }

                if (!fpsController.IsGrounded)
                {
                    spread *= Mathf.Max(1f, jumpSpreadMultiplier);
                }
            }

            return (spread, recoil);
        }

        private void SimulateShotEffects(CrosshairShot shot, bool applyRecoil)
        {
            TryResolveRuntimeMuzzle();
            var muzzlePosition = muzzle != null
                ? muzzle.position
                : shot.Origin;

            SpawnVfx(ResolveMuzzleFlashVfx(), muzzlePosition, Quaternion.LookRotation(shot.Direction, Vector3.up));
            if (applyRecoil)
            {
                ApplyRecoilKick();
            }

            RaycastHit? gameplayHit = null;
            if (TryRaycastIgnoringSelf(new Ray(shot.Origin, shot.Direction), maxDistance, out var hit))
            {
                gameplayHit = hit;
                ProcessGameplayHit(hit, shot.Direction, applyRecoil);
            }

            var visualHit = ResolveVisualImpactHit(shot, gameplayHit);
            var visualEndPoint = visualHit.HasValue
                ? visualHit.Value.point
                : shot.Origin + shot.Direction * maxDistance;

            if (visualHit.HasValue)
            {
                SpawnVisualImpactVfx(visualHit.Value, shot.Direction);
            }

            if (showTracer)
            {
                StartCoroutine(SpawnTracer(muzzlePosition, visualEndPoint));
            }
        }

        private Vector3 ResolveRemoteShootDirection(float lookPitch)
        {
            if (muzzle != null)
            {
                return muzzle.forward;
            }

            var forwardWithPitch = Quaternion.Euler(lookPitch, transform.eulerAngles.y, 0f) * Vector3.forward;
            return forwardWithPitch.sqrMagnitude > 0.00001f ? forwardWithPitch.normalized : transform.forward;
        }

        private void ProcessGameplayHit(RaycastHit hit, Vector3 shotDirection, bool notifyNetworkHit)
        {
            var hitZone = ResolveHitZone(hit.collider);
            var playerHit = IsPlayerHit(hit.collider);
            if (!playerHit)
            {
                return;
            }

            var registerThisHit = ShouldRegisterHitOnce(hit.collider, hitZone);
            if (!registerThisHit)
            {
                return;
            }

            TryPlayHeadshotAudio(hitZone);
            if (notifyNetworkHit)
            {
                TrySendPlayerHitToServer(hit.collider, shotDirection, hitZone, hit.point);
            }
        }

        private void SpawnVisualImpactVfx(RaycastHit hit, Vector3 shotDirection)
        {
            var playerHit = IsPlayerHit(hit.collider);
            var targetVfx = playerHit ? playerHitVfx : worldHitVfx;
            if (targetVfx == null)
            {
                return;
            }

            var normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : -shotDirection;
            SpawnVfx(targetVfx, hit.point, Quaternion.LookRotation(normal, Vector3.up));
        }

        private bool ShouldRegisterHitOnce(Collider targetCollider, HitZone hitZone)
        {
            if (hitZone != HitZone.Head || targetCollider == null)
            {
                return true;
            }

            var targetKey = ResolveTargetRegistrationKey(targetCollider);
            if (string.IsNullOrEmpty(targetKey))
            {
                return true;
            }

            if (headshotRegistrationUntilByTarget.TryGetValue(targetKey, out var lockUntil) &&
                Time.time < lockUntil)
            {
                return false;
            }

            headshotRegistrationUntilByTarget[targetKey] =
                Time.time + Mathf.Max(0.1f, headshotSingleRegistrationSeconds);
            return true;
        }

        private static string ResolveTargetRegistrationKey(Collider targetCollider)
        {
            var identity = targetCollider.GetComponentInParent<PlayerNetworkIdentity>();
            if (identity != null && !string.IsNullOrWhiteSpace(identity.TicketId))
            {
                return identity.TicketId.Trim();
            }

            return targetCollider.GetInstanceID().ToString();
        }

        private bool IsPlayerHit(Collider targetCollider)
        {
            return PlayerWeaponRaycastFilters.IsPlayerBoneHit(targetCollider, transform);
        }

        private float ResolveDamage(HitZone hitZone)
        {
            switch (hitZone)
            {
                case HitZone.Leg:
                    return Mathf.Max(0f, legDamage);
                case HitZone.Neck:
                    return Mathf.Max(0f, neckDamage);
                case HitZone.Head:
                    return Mathf.Max(0f, headDamage);
                default:
                    return Mathf.Max(0f, bodyDamage);
            }
        }

        private static HitZone ResolveHitZone(Collider targetCollider)
        {
            if (targetCollider == null)
            {
                return HitZone.Body;
            }

            var boneHitbox = targetCollider.GetComponentInParent<PlayerBoneHitbox>(true);
            if (boneHitbox != null)
            {
                switch (boneHitbox.HitZone)
                {
                    case PlayerBoneHitZone.Head:
                        return HitZone.Head;
                    case PlayerBoneHitZone.Neck:
                        return HitZone.Neck;
                    case PlayerBoneHitZone.Leg:
                        return HitZone.Leg;
                    default:
                        return HitZone.Body;
                }
            }

            var current = targetCollider.transform;
            while (current != null)
            {
                var name = current.name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var lower = name.ToLowerInvariant();
                    if (lower.Contains("head"))
                    {
                        return HitZone.Head;
                    }

                    if (lower.Contains("neck"))
                    {
                        return HitZone.Neck;
                    }

                    if (lower.Contains("leg") || lower.Contains("foot"))
                    {
                        return HitZone.Leg;
                    }
                }

                current = current.parent;
            }

            return HitZone.Body;
        }

        private void TryPlayHeadshotAudio(HitZone hitZone)
        {
            if (hitZone != HitZone.Head)
            {
                return;
            }

            hitPlayerSequence++;
            audioController?.PlayHitPlayer(true);
        }

        private QueryTriggerInteraction ResolveHitQueryTriggerInteraction()
        {
            return detectTriggerHitboxes
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;
        }

        private int ResolveEffectiveHitMask()
        {
            return PlayerHitboxLayers.ResolveWeaponRaycastMask(hitMask);
        }

        private bool TryRaycastIgnoringSelf(Ray ray, float distance, out RaycastHit closestHit)
        {
            var mask = ResolveEffectiveHitMask();
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                hitQueryBuffer,
                distance,
                mask,
                ResolveHitQueryTriggerInteraction());
            if (hitCount <= 0)
            {
                closestHit = default;
                return false;
            }

            System.Array.Sort(hitQueryBuffer, 0, hitCount, RaycastHitDistanceComparer.Instance);
            return PlayerWeaponRaycastFilters.TrySelectClosestHit(
                hitQueryBuffer,
                hitCount,
                transform,
                out closestHit);
        }

        private sealed class RaycastHitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly RaycastHitDistanceComparer Instance = new RaycastHitDistanceComparer();
            public int Compare(RaycastHit x, RaycastHit y) => x.distance.CompareTo(y.distance);
        }

        private enum HitZone
        {
            Body = 0,
            Leg = 1,
            Neck = 2,
            Head = 3
        }

        private void TrySendPlayerHitToServer(
            Collider targetCollider,
            Vector3 shotDirection,
            HitZone hitZone,
            Vector3 hitPoint)
        {
            if (targetCollider == null)
            {
                return;
            }

            var identity = targetCollider.GetComponentInParent<PlayerNetworkIdentity>();
            if (identity == null || identity.IsLocalPlayer || string.IsNullOrWhiteSpace(identity.TicketId))
            {
                return;
            }

            if (realtimeClient == null)
            {
                realtimeClient = FindObjectOfType<RealtimeTransportClient>();
            }

            var shotTick = realtimeClient != null ? realtimeClient.LatestServerTick : 0;
            realtimeClient?.SendHit(
                identity.TicketId,
                ResolveDamage(hitZone),
                shotDirection,
                shotSequence,
                shotTick,
                hitPoint,
                hitZone.ToString().ToLowerInvariant());
        }

        private void SpawnVfx(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, position, rotation);
            PlayAllParticleSystems(instance);
            if (vfxAutoDestroySeconds > 0f)
            {
                Destroy(instance, vfxAutoDestroySeconds);
            }
        }

        private static void PlayAllParticleSystems(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];
                if (ps != null)
                {
                    ps.Play(true);
                }
            }
        }

        private bool TryStartReload()
        {
            if (isReloading || currentAmmo >= MagazineSize)
            {
                return false;
            }

            if (reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                reloadCoroutine = null;
            }

            reloadSequence++;
            reloadCoroutine = StartCoroutine(ReloadRoutine());
            return true;
        }

        private bool ShouldBlockFireByWallCollision()
        {
            if (!blockFireWhenWeaponCollides || weaponMount == null)
            {
                return false;
            }

            return weaponMount.CurrentWallAvoidBlend >= Mathf.Clamp01(fireBlockWallAvoidThreshold);
        }

        private bool ShouldBlockFireBySprint()
        {
            if (!blockFireWhenSprinting)
            {
                return false;
            }

            if (fpsController == null)
            {
                fpsController = GetComponent<FpsCharacterController>();
            }

            return fpsController != null && fpsController.IsSprinting;
        }

        private IEnumerator ReloadRoutine()
        {
            isReloading = true;
            var reloadTime = Mathf.Max(0.05f, reloadDuration);
            nextFireTime = Time.time + reloadTime;
            weaponMount?.SetLocalReloading(true);
            weaponMount?.PlayReloadAnimation(reloadTime);
            audioController?.PlayReloadSequence(true, reloadTime, ResolveWeaponAudioOverrides());
            yield return new WaitForSeconds(reloadTime);
            currentAmmo = MagazineSize;
            SyncLoadoutMagAmmo();
            isReloading = false;
            weaponMount?.SetLocalReloading(false);
            reloadCoroutine = null;
        }

        private void SyncLoadoutMagAmmo()
        {
            if (weaponLoadoutController == null)
            {
                weaponLoadoutController = GetComponent<PlayerWeaponLoadoutController>();
            }

            weaponLoadoutController?.SyncActiveSlotMagAmmoFromController();
        }

        private void TryResolveRuntimeMuzzle()
        {
            if (muzzle != null)
            {
                return;
            }

            Transform weaponRoot = null;

            if (weaponMount == null)
            {
                weaponMount = GetComponent<PlayerWeaponMount>();
            }

            if (weaponMount != null)
            {
                weaponRoot = weaponMount.MountedWeaponRoot;
            }

            if (weaponRoot == null)
            {
                var remoteWeapon = GetComponent<RemoteWeaponPresentation>();
                weaponRoot = remoteWeapon != null ? remoteWeapon.WeaponRoot : null;
            }

            if (weaponRoot == null)
            {
                return;
            }

            muzzle = FindChildRecursive(weaponRoot, muzzleName);
            if (muzzle != null)
            {
                return;
            }

            var fallbackNames = new[] { "MuzzlePoint", "MuzzleFlash", "BarrelEnd", "Barrel", "FirePoint" };
            for (var i = 0; i < fallbackNames.Length; i++)
            {
                muzzle = FindChildRecursive(weaponRoot, fallbackNames[i]);
                if (muzzle != null)
                {
                    return;
                }
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t != null && string.Equals(t.name, childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }

        private static bool ReadAimPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton(1);
#endif
        }

        private static bool ReadReloadPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private IEnumerator SpawnTracer(Vector3 from, Vector3 to)
        {
            var tracerObject = new GameObject("ShotTracer");
            var line = tracerObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = tracerWidth;
            line.endWidth = tracerWidth;
            line.startColor = tracerColor;
            line.endColor = tracerColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            line.sortingOrder = 40;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;

            if (tracerMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (shader != null)
                {
                    tracerMaterial = new Material(shader);
                    if (tracerMaterial.HasProperty("_Color"))
                    {
                        tracerMaterial.color = tracerColor;
                    }
                }
            }

            if (tracerMaterial != null)
            {
                line.sharedMaterial = tracerMaterial;
            }

            line.SetPosition(0, from);
            line.SetPosition(1, to);

            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, tracerDuration));
            Destroy(tracerObject);
        }

        private GameObject ResolveMuzzleFlashVfx()
        {
            var profile = weaponMount != null ? weaponMount.ActiveWeaponProfile : null;
            if (profile != null && profile.MuzzleFlashVfx != null)
            {
                return profile.MuzzleFlashVfx;
            }

            return muzzleFlashVfx;
        }

        private WeaponAudioOverrides ResolveWeaponAudioOverrides()
        {
            var profile = weaponMount != null ? weaponMount.ActiveWeaponProfile : null;
            return profile != null ? profile.CreateAudioOverrides() : default;
        }
    }
}
