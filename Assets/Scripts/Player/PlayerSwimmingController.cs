using UnityEngine;

namespace ShooterPrototype.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerSwimmingController : MonoBehaviour
    {
        private FpsCharacterController fpsController;
        private PlayerWeaponHolsterController holsterController;
        private PlayerHealth playerHealth;
        private int waterOverlapCount;
        private bool wasHolsteredBeforeWater;
        private bool restoreDrawAfterWater;

        public bool IsSwimming => waterOverlapCount > 0;

        private void Awake()
        {
            fpsController = GetComponent<FpsCharacterController>();
            holsterController = GetComponent<PlayerWeaponHolsterController>();
            playerHealth = GetComponent<PlayerHealth>();
        }

        private void OnDisable()
        {
            if (waterOverlapCount > 0)
            {
                waterOverlapCount = 0;
                ExitWater();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!WaterLayers.IsWaterCollider(other))
            {
                return;
            }

            if (waterOverlapCount == 0)
            {
                EnterWater();
            }

            waterOverlapCount++;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!WaterLayers.IsWaterCollider(other))
            {
                return;
            }

            waterOverlapCount = Mathf.Max(0, waterOverlapCount - 1);
            if (waterOverlapCount == 0)
            {
                ExitWater();
            }
        }

        private void EnterWater()
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                return;
            }

            if (holsterController != null)
            {
                wasHolsteredBeforeWater = holsterController.IsHolstered;
                restoreDrawAfterWater = holsterController.IsWeaponReady;
                holsterController.BeginHolsterAllImmediate();
            }

            fpsController?.SetSwimmingMode(true);
        }

        private void ExitWater()
        {
            fpsController?.SetSwimmingMode(false);

            if (holsterController == null || wasHolsteredBeforeWater)
            {
                wasHolsteredBeforeWater = false;
                restoreDrawAfterWater = false;
                return;
            }

            if (restoreDrawAfterWater)
            {
                holsterController.BeginDrawEquippedWeapon();
            }

            wasHolsteredBeforeWater = false;
            restoreDrawAfterWater = false;
        }

        public void ForceExitWaterState()
        {
            waterOverlapCount = 0;
            wasHolsteredBeforeWater = false;
            restoreDrawAfterWater = false;
            fpsController?.SetSwimmingMode(false);
        }
    }
}
