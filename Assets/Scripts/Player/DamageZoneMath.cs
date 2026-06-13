using UnityEngine;

namespace ShooterPrototype.Player
{
    public readonly struct ZonePhaseState
    {
        public ZonePhaseState(int phase, Vector3 center, float radius)
        {
            Phase = phase;
            Center = center;
            Radius = radius;
        }

        public int Phase { get; }
        public Vector3 Center { get; }
        public float Radius { get; }
    }

    public static class DamageZoneMath
    {
        public static void ComputeTwoPhaseDurations(
            float initialRadius,
            float phase1EndRadius,
            float finalRadius,
            float totalShrinkDurationSeconds,
            out float phase1DurationSeconds,
            out float phase2DurationSeconds)
        {
            totalShrinkDurationSeconds = Mathf.Max(0.001f, totalShrinkDurationSeconds);
            var totalRadiusDelta = Mathf.Max(0.001f, initialRadius - finalRadius);
            var phase1RadiusDelta = Mathf.Max(0f, initialRadius - phase1EndRadius);
            var phase2RadiusDelta = Mathf.Max(0f, phase1EndRadius - finalRadius);

            phase1DurationSeconds = totalShrinkDurationSeconds * (phase1RadiusDelta / totalRadiusDelta);
            phase2DurationSeconds = totalShrinkDurationSeconds * (phase2RadiusDelta / totalRadiusDelta);

            if (phase1DurationSeconds <= 0.001f)
            {
                phase1DurationSeconds = 0.001f;
            }

            if (phase2DurationSeconds <= 0.001f)
            {
                phase2DurationSeconds = 0.001f;
            }
        }

        public static float EvaluatePhaseProgress(float elapsedSeconds, float durationSeconds)
        {
            if (durationSeconds <= 0.001f)
            {
                return 1f;
            }

            return SmoothPhaseProgress(elapsedSeconds / durationSeconds);
        }

        public static float SmoothPhaseProgress(float normalizedTime)
        {
            var t = Mathf.Clamp01(normalizedTime);
            return t * t * (3f - 2f * t);
        }

        public static ZonePhaseState EvaluateZonePhases(
            float elapsedSeconds,
            Vector3 mapCenter,
            Vector3 phase1Center,
            Vector3 phase2Center,
            float initialRadius,
            float phase1EndRadius,
            float finalRadius,
            float totalShrinkDurationSeconds,
            AnimationCurve easingCurve)
        {
            elapsedSeconds = Mathf.Max(0f, elapsedSeconds);
            ComputeTwoPhaseDurations(
                initialRadius,
                phase1EndRadius,
                finalRadius,
                totalShrinkDurationSeconds,
                out var phase1DurationSeconds,
                out var phase2DurationSeconds);

            if (elapsedSeconds <= phase1DurationSeconds)
            {
                var progress = EvaluatePhaseProgress(elapsedSeconds, phase1DurationSeconds);
                if (easingCurve != null && easingCurve.length > 0)
                {
                    progress = Mathf.Clamp01(easingCurve.Evaluate(progress));
                }

                var center = Vector3.Lerp(mapCenter, phase1Center, progress);
                var radius = Mathf.Lerp(initialRadius, phase1EndRadius, progress);
                return new ZonePhaseState(1, center, radius);
            }

            var phase2Elapsed = elapsedSeconds - phase1DurationSeconds;
            var phase2Progress = EvaluatePhaseProgress(phase2Elapsed, phase2DurationSeconds);
            if (easingCurve != null && easingCurve.length > 0)
            {
                phase2Progress = Mathf.Clamp01(easingCurve.Evaluate(phase2Progress));
            }

            var centerPhase2 = Vector3.Lerp(phase1Center, phase2Center, phase2Progress);
            var radiusPhase2 = Mathf.Lerp(phase1EndRadius, finalRadius, phase2Progress);
            return new ZonePhaseState(2, centerPhase2, radiusPhase2);
        }

        public static Vector2 RollPhase1Center(Vector2 mapCenter, float offsetRange)
        {
            var offset = Mathf.Max(0f, offsetRange);
            return new Vector2(
                mapCenter.x + Random.Range(-offset, offset),
                mapCenter.y + Random.Range(-offset, offset));
        }

        public static Vector2 RollPhase2Center(Vector2 mapCenter, float offsetRange)
        {
            var offset = Mathf.Max(0f, offsetRange);
            return new Vector2(
                mapCenter.x + Random.Range(-offset, offset),
                mapCenter.y + Random.Range(-offset, offset));
        }

        public static Vector2 RollPhase2CenterInside(Vector2 phase1Center, float maxRadius)
        {
            var radius = Mathf.Max(0f, maxRadius);
            if (radius <= 0.001f)
            {
                return phase1Center;
            }

            var angle = Random.Range(0f, Mathf.PI * 2f);
            var distance = Mathf.Sqrt(Random.value) * radius;
            return new Vector2(
                phase1Center.x + Mathf.Cos(angle) * distance,
                phase1Center.y + Mathf.Sin(angle) * distance);
        }

        public static float EvaluateDamagePerSecond(
            float minDamagePerSecond,
            float maxDamagePerSecond,
            float elapsedSeconds,
            float damageRampSeconds,
            AnimationCurve damageCurve)
        {
            var progress = damageRampSeconds <= 0.001f
                ? 1f
                : Mathf.Clamp01(elapsedSeconds / damageRampSeconds);
            if (damageCurve != null && damageCurve.length > 0)
            {
                progress = Mathf.Clamp01(damageCurve.Evaluate(progress));
            }

            return Mathf.Lerp(minDamagePerSecond, maxDamagePerSecond, progress);
        }

        public static bool IsOutsideSafeZone(Vector3 worldPosition, Vector3 center, float radius)
        {
            var dx = worldPosition.x - center.x;
            var dz = worldPosition.z - center.z;
            return (dx * dx) + (dz * dz) > radius * radius;
        }

        public static float HorizontalDistance(Vector3 worldPosition, Vector3 center)
        {
            var dx = worldPosition.x - center.x;
            var dz = worldPosition.z - center.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
