using System.Collections;
using ShooterPrototype.Network;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerMedkitController : MonoBehaviour
    {
        [SerializeField] private float useDurationSeconds = 8f;
        [SerializeField] private float healAmount = 70f;

        private PlayerInventory inventory;
        private Coroutine useRoutine;
        private bool cancelRequested;
        private PlayerHealth health;
        private FpsCharacterController fpsController;
        private PlayerWeaponMount weaponMount;
        private PlayerWeaponController weaponController;
        private PlayerWeaponHolsterController weaponHolster;
        private PlayerPickupController pickupController;
        private RealtimeTransportClient transportClient;
        private bool useNetworkAuthority;
        private int lastAppliedMedkitSeq = -1;

        public int MedkitCount => inventory != null ? inventory.GetCount(InventoryItemIds.Medkit) : 0;
        public bool IsUsingMedkit => useRoutine != null;
        public float RemainingUseSeconds { get; private set; }

        private void Awake()
        {
            if (GetComponent<RemoteThirdPersonPlayerBootstrap>() != null)
            {
                enabled = false;
                return;
            }

            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<PlayerHealth>();
            fpsController = GetComponent<FpsCharacterController>();
            weaponMount = GetComponent<PlayerWeaponMount>();
            weaponController = GetComponent<PlayerWeaponController>();
            weaponHolster = GetComponent<PlayerWeaponHolsterController>();
            pickupController = GetComponent<PlayerPickupController>();
        }

        private void OnEnable()
        {
            TryBindTransport();
        }

        private void OnDisable()
        {
            UnbindTransport();
            StopActiveUse(applyHeal: false, refundMedkit: false);
        }

        private void Update()
        {
            if (!ReadUseMedkitPressed() || IsUsingMedkit)
            {
                return;
            }

            TryStartUseMedkit();
        }

        public void SetMedkitCount(int count)
        {
            inventory?.SetCount(InventoryItemIds.Medkit, count);
        }

        public bool CanAddFromPickup(int amount)
        {
            if (amount <= 0 || inventory == null || health != null && health.IsDead)
            {
                return false;
            }

            return inventory.CanAdd(InventoryItemIds.Medkit, amount);
        }

        public bool TryAddMedkit(int amount)
        {
            return inventory != null && inventory.TryAdd(InventoryItemIds.Medkit, amount);
        }

        public bool CanUseMedkit()
        {
            if (IsUsingMedkit || MedkitCount <= 0 || health == null || health.IsDead)
            {
                return false;
            }

            return health.CurrentHealth < health.MaxHealth - 0.001f;
        }

        public bool TryStartUseMedkit()
        {
            if (!CanUseMedkit())
            {
                return false;
            }

            if (useNetworkAuthority && transportClient != null && transportClient.IsConnected)
            {
                transportClient.SendMedkitUse();
                return true;
            }

            if (!inventory.TryRemove(InventoryItemIds.Medkit, 1))
            {
                return false;
            }

            BeginLocalUseRoutine(useDurationSeconds, applyHealLocally: true);
            return true;
        }

        public void ApplyNetworkMedkitResult(RealtimeTransportClient.MedkitResultMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (!message.success)
            {
                if (message.medkitCount >= 0)
                {
                    SetMedkitCount(message.medkitCount);
                }

                if (string.Equals(message.reason, "cancelled", System.StringComparison.Ordinal))
                {
                    StopActiveUse(applyHeal: false, refundMedkit: false);
                }

                if (!string.IsNullOrWhiteSpace(message.reason) &&
                    !string.Equals(message.reason, "cancelled", System.StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[PlayerMedkitController] medkit_use rejected: {message.reason}");
                }

                return;
            }

            SetMedkitCount(message.medkitCount);
            if (IsUsingMedkit)
            {
                return;
            }

            var duration = message.durationSeconds > 0.01f ? message.durationSeconds : useDurationSeconds;
            BeginLocalUseRoutine(duration, applyHealLocally: false);
        }

        public void ApplyNetworkHeal(RealtimeTransportClient.HealMessage message)
        {
            if (message == null || message.amount <= 0f)
            {
                return;
            }

            if (message.medkitSeq > 0 && message.medkitSeq <= lastAppliedMedkitSeq)
            {
                return;
            }

            if (message.medkitSeq > 0)
            {
                lastAppliedMedkitSeq = message.medkitSeq;
            }

            health?.TryHeal(message.amount > 0f ? message.amount : healAmount);
        }

        public void SyncFromSnapshot(RealtimeTransportClient.RealtimePlayerState state, bool authoritativeMedkitCount)
        {
            if (state == null)
            {
                return;
            }

            if (authoritativeMedkitCount)
            {
                SetMedkitCount(state.medkitCount);
            }

            if (!state.isUsingMedkit)
            {
                if (IsUsingMedkit && !cancelRequested)
                {
                    StopActiveUse(applyHeal: false, refundMedkit: false);
                }

                return;
            }

            if (IsUsingMedkit)
            {
                RemainingUseSeconds = Mathf.Max(RemainingUseSeconds, state.medkitRemainingSeconds);
                return;
            }

            var duration = state.medkitRemainingSeconds > 0.01f
                ? state.medkitRemainingSeconds
                : useDurationSeconds;
            BeginLocalUseRoutine(duration, applyHealLocally: false);
        }

        private void BeginLocalUseRoutine(float durationSeconds, bool applyHealLocally)
        {
            cancelRequested = false;
            useRoutine = StartCoroutine(UseMedkitRoutine(durationSeconds, applyHealLocally));
        }

        private IEnumerator UseMedkitRoutine(float durationSeconds, bool applyHealLocally)
        {
            RemainingUseSeconds = Mathf.Max(0.01f, durationSeconds);

            weaponController?.CancelActiveReload();
            if (weaponController != null)
            {
                weaponController.enabled = false;
            }

            fpsController?.SetMedkitUseMovementMode(true, CancelMedkitFromMovement);
            weaponHolster?.BeginMedkitUsePresentation();

            while (RemainingUseSeconds > 0f)
            {
                if (cancelRequested)
                {
                    yield break;
                }

                RemainingUseSeconds -= Time.deltaTime;
                yield return null;
            }

            RemainingUseSeconds = 0f;
            var completed = !cancelRequested;
            StopActiveUse(applyHeal: completed && applyHealLocally, refundMedkit: false);
        }

        private void CancelMedkitFromMovement()
        {
            if (!IsUsingMedkit || cancelRequested)
            {
                return;
            }

            cancelRequested = true;

            if (useNetworkAuthority && transportClient != null && transportClient.IsConnected)
            {
                transportClient.SendMedkitCancel();
            }
            else
            {
                inventory?.TryAdd(InventoryItemIds.Medkit, 1);
            }

            StopActiveUse(applyHeal: false, refundMedkit: false);
        }

        private void StopActiveUse(bool applyHeal, bool refundMedkit)
        {
            if (useRoutine != null)
            {
                StopCoroutine(useRoutine);
                useRoutine = null;
            }

            cancelRequested = false;
            RemainingUseSeconds = 0f;

            fpsController?.SetMedkitUseMovementMode(false);
            weaponHolster?.RestoreAfterMedkitUse();

            if (weaponController != null)
            {
                weaponController.enabled = weaponMount != null && weaponMount.HasMountedWeapon;
            }

            pickupController?.RefreshWeaponAvailability();

            if (applyHeal)
            {
                health?.TryHeal(healAmount);
            }
            else if (refundMedkit)
            {
                inventory?.TryAdd(InventoryItemIds.Medkit, 1);
            }
        }

        private void TryBindTransport()
        {
            if (transportClient != null)
            {
                return;
            }

            transportClient = FindObjectOfType<RealtimeTransportClient>();
            if (transportClient == null)
            {
                useNetworkAuthority = false;
                return;
            }

            useNetworkAuthority = true;
            transportClient.MedkitResultReceived += HandleMedkitResult;
            transportClient.HealReceived += HandleHeal;
        }

        private void UnbindTransport()
        {
            if (transportClient == null)
            {
                return;
            }

            transportClient.MedkitResultReceived -= HandleMedkitResult;
            transportClient.HealReceived -= HandleHeal;
        }

        private void HandleMedkitResult(RealtimeTransportClient.MedkitResultMessage message)
        {
            ApplyNetworkMedkitResult(message);
        }

        private void HandleHeal(RealtimeTransportClient.HealMessage message)
        {
            ApplyNetworkHeal(message);
        }

        private static bool ReadUseMedkitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
            {
                return false;
            }

            return Keyboard.current.digit8Key.wasPressedThisFrame ||
                   Keyboard.current.numpad8Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Alpha8);
#endif
        }
    }
}
