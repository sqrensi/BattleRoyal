namespace ShooterPrototype.Player
{
    /// <summary>
    /// Project-wide gameplay presentation toggles that do not affect match rules.
    /// </summary>
    public static class GameplayPerformanceOptions
    {
        public const bool UseProceduralRemoteLocomotion = false;
        public const int MaxActiveShotTracers = 24;
        public const float CornerStatsTextRefreshSeconds = 0.2f;
    }
}
