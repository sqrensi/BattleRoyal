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
        public const int WallCheckIntervalFrames = 6;
        public const int WallRaycastHitBufferSize = 12;
        public const int WallCapsuleCastHitBufferSize = 12;
        public const int WallOverlapCapsuleBufferSize = 16;

        public const float DmBotThinkIntervalSeconds = 0.55f;
        /// <summary>
        /// Full pose solve interval for bots and online remotes (IK, weapon attach, spine pitch).
        /// Cached transforms are still applied every visible frame.
        /// </summary>
        public const int BotPresentationSolveIntervalFrames = 5;

        public const int EnemyHandIkPositionSolveIterations = 4;
        public const bool DmBotShotTracersEnabled = false;
        /// <summary>
        /// Chance to spawn muzzle/hit particles for bot shots. Audio still plays every shot.
        /// </summary>
        public const float DmBotShotVfxChance = 0.35f;

        public const float EnemyPresentationMaxDistance = 40f;
        public const int EnemyVisibilityCheckIntervalFrames = 6;
    }
}
