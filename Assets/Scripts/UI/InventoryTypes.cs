using ShooterPrototype.Player;

namespace ShooterPrototype.UI
{
    public enum InventorySlotKind
    {
        FloorItem = 0,
        FloorArea = 1,
        InventoryItem = 2,
        InventoryArea = 3,
        WeaponSlot = 4,
        WeaponArea = 5
    }

    public enum InventoryDragSource
    {
        None = 0,
        WeaponSlot = 1,
        InventoryItem = 2,
        FloorItem = 3
    }

    public struct InventoryDragPayload
    {
        public InventoryDragSource Source;
        public int WeaponSlotIndex;
        public string ItemId;
        public string SpawnId;
        public int Amount;
        public PickupKind PickupKind;

        public bool HasDragData =>
            (Source == InventoryDragSource.WeaponSlot && WeaponSlotIndex >= 0 && WeaponSlotIndex <= 1) ||
            (Source == InventoryDragSource.InventoryItem && !string.IsNullOrWhiteSpace(ItemId)) ||
            (Source == InventoryDragSource.FloorItem && !string.IsNullOrWhiteSpace(SpawnId));

        public bool IsFloorPickup => Source == InventoryDragSource.FloorItem;
        public bool IsInventoryOwned =>
            Source == InventoryDragSource.WeaponSlot || Source == InventoryDragSource.InventoryItem;
    }
}
