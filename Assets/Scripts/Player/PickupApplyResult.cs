namespace ShooterPrototype.Player
{
    internal readonly struct PickupApplyResult
    {
        public PickupApplyResult(bool success, string failureReason = "")
        {
            Success = success;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Success { get; }
        public string FailureReason { get; }

        public static PickupApplyResult Ok => new PickupApplyResult(true);
    }

    /// <summary>
    /// Optional authoritative values from pickup_result (e.g. server medkit inventory).
    /// </summary>
    public readonly struct PickupApplyServerState
    {
        public PickupApplyServerState(int medkitCount, bool hasMedkitCount)
        {
            MedkitCount = medkitCount;
            HasMedkitCount = hasMedkitCount;
        }

        public int MedkitCount { get; }
        public bool HasMedkitCount { get; }

        public static PickupApplyServerState None => new PickupApplyServerState(0, false);
    }
}
