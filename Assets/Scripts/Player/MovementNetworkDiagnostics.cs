using UnityEngine;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Throttled movement/network diagnostics. Filter Unity Console by [MoveDiag].
    /// </summary>
    public static class MovementNetworkDiagnostics
    {
        public const bool Enabled = false;
        private const float LogIntervalSeconds = 1f;

        private static float lastPoseLogAt = -999f;
        private static float lastMoveLogAt = -999f;
        private static float lastReconcileLogAt = -999f;
        private static float lastSnapshotLogAt = -999f;

        private static Vector3 lastMoveSamplePos;
        private static float lastMoveSampleAt = -999f;
        private static bool hasMoveSample;

        public static void LogLocalMovement(
            Vector3 position,
            Vector3 ccVelocity,
            float moveInputX,
            float moveInputZ,
            bool inputBlocked,
            string blockReason,
            bool fpsEnabled,
            bool ccEnabled,
            bool movementLocked,
            bool cursorUnlocked)
        {
            if (!Enabled || Time.unscaledTime - lastMoveLogAt < LogIntervalSeconds)
            {
                return;
            }

            lastMoveLogAt = Time.unscaledTime;

            var horizSpeed = new Vector2(ccVelocity.x, ccVelocity.z).magnitude;
            var inputMag = Mathf.Sqrt(moveInputX * moveInputX + moveInputZ * moveInputZ);
            var localStuck = false;
            if (hasMoveSample && inputMag > 0.12f)
            {
                var moved = Vector3.Distance(
                    new Vector3(position.x, 0f, position.z),
                    new Vector3(lastMoveSamplePos.x, 0f, lastMoveSamplePos.z));
                localStuck = moved < 0.02f &&
                               Time.unscaledTime - lastMoveSampleAt >= LogIntervalSeconds;
            }

            lastMoveSamplePos = position;
            lastMoveSampleAt = Time.unscaledTime;
            hasMoveSample = true;

            Debug.Log(
                "[MoveDiag][local] " +
                $"pos=({position.x:F2},{position.y:F2},{position.z:F2}) " +
                $"ccVel=({ccVelocity.x:F2},{ccVelocity.z:F2}) speed={horizSpeed:F2} " +
                $"input=({moveInputX:F2},{moveInputZ:F2}) mag={inputMag:F2} " +
                $"blocked={(inputBlocked ? 1 : 0)} reason={blockReason} " +
                $"fpsOn={(fpsEnabled ? 1 : 0)} ccOn={(ccEnabled ? 1 : 0)} " +
                $"moveLock={(movementLocked ? 1 : 0)} cursorUnlock={(cursorUnlocked ? 1 : 0)} " +
                $"LOCAL_STUCK={(localStuck ? 1 : 0)}");
        }

        public static void LogPoseSend(
            Vector3 sentPosition,
            float moveInputX,
            float moveInputZ,
            bool inputAuth,
            bool isDead,
            int weaponSlot0,
            int weaponSlot1)
        {
            if (!Enabled || Time.unscaledTime - lastPoseLogAt < LogIntervalSeconds)
            {
                return;
            }

            lastPoseLogAt = Time.unscaledTime;
            Debug.Log(
                "[MoveDiag][pose-send] " +
                $"pos=({sentPosition.x:F2},{sentPosition.y:F2},{sentPosition.z:F2}) " +
                $"inputAuth={(inputAuth ? 1 : 0)} dead={(isDead ? 1 : 0)} " +
                $"move=({moveInputX:F2},{moveInputZ:F2}) " +
                $"slots={weaponSlot0},{weaponSlot1}");
        }

        public static void LogReconcile(
            Vector3 localPos,
            Vector3 serverPos,
            float horizontalError,
            int serverTick,
            string action,
            string skipReason)
        {
            if (!Enabled)
            {
                return;
            }

            if (!string.IsNullOrEmpty(skipReason))
            {
                if (Time.unscaledTime - lastReconcileLogAt < LogIntervalSeconds)
                {
                    return;
                }

                lastReconcileLogAt = Time.unscaledTime;
                Debug.Log(
                    "[MoveDiag][reconcile-skip] " +
                    $"reason={skipReason} tick={serverTick} " +
                    $"local=({localPos.x:F2},{localPos.y:F2},{localPos.z:F2}) " +
                    $"server=({serverPos.x:F2},{serverPos.y:F2},{serverPos.z:F2}) " +
                    $"err={horizontalError:F3}");
                return;
            }

            if (Time.unscaledTime - lastReconcileLogAt < LogIntervalSeconds)
            {
                return;
            }

            lastReconcileLogAt = Time.unscaledTime;
            Debug.Log(
                "[MoveDiag][reconcile] " +
                $"action={action} tick={serverTick} err={horizontalError:F3} " +
                $"local=({localPos.x:F2},{localPos.y:F2},{localPos.z:F2}) " +
                $"server=({serverPos.x:F2},{serverPos.y:F2},{serverPos.z:F2})");
        }

        public static void LogSnapshotSelfAuth(
            bool hasSelfAuth,
            Vector3 localPos,
            Vector3 serverPos,
            int serverTick,
            int binaryVersion,
            int playerCount,
            float secondsSinceLastSnapshot)
        {
            if (!Enabled || Time.unscaledTime - lastSnapshotLogAt < LogIntervalSeconds)
            {
                return;
            }

            lastSnapshotLogAt = Time.unscaledTime;
            var err = hasSelfAuth
                ? Vector2.Distance(
                    new Vector2(localPos.x, localPos.z),
                    new Vector2(serverPos.x, serverPos.z))
                : -1f;

            Debug.Log(
                "[MoveDiag][snapshot] " +
                $"selfAuth={(hasSelfAuth ? 1 : 0)} tick={serverTick} ver={binaryVersion} " +
                $"players={playerCount} snapAge={secondsSinceLastSnapshot:F2}s " +
                $"local=({localPos.x:F2},{localPos.y:F2},{localPos.z:F2}) " +
                $"server=({serverPos.x:F2},{serverPos.y:F2},{serverPos.z:F2}) " +
                $"err={err:F3}");
        }

        private static float lastDecodeLogAt = -999f;

        public static void LogSnapshotDecodeOk(int binaryVersion, int serverTick)
        {
            if (!Enabled || Time.unscaledTime - lastDecodeLogAt < LogIntervalSeconds)
            {
                return;
            }

            lastDecodeLogAt = Time.unscaledTime;
            Debug.Log($"[MoveDiag][snapshot-decode-ok] ver={binaryVersion} tick={serverTick}");
        }

        private static float lastWsLogAt = -999f;

        public static void LogWsState(string state, string ticketId, string detail = "")
        {
            if (!Enabled || Time.unscaledTime - lastWsLogAt < LogIntervalSeconds)
            {
                return;
            }

            lastWsLogAt = Time.unscaledTime;
            Debug.Log(
                $"[MoveDiag][ws] state={state} ticket={(string.IsNullOrWhiteSpace(ticketId) ? "none" : ticketId)} {detail}");
        }
    }
}
