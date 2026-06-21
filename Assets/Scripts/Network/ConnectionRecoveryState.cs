namespace ShooterPrototype.Network
{
    /// <summary>
    /// Passes a lost-connection signal from the game scene to the main-menu reconnect gate.
    /// </summary>
    public static class ConnectionRecoveryState
    {
        private static bool pending;
        private static string message = string.Empty;

        public static void MarkPending(string recoveryMessage)
        {
            pending = true;
            message = string.IsNullOrWhiteSpace(recoveryMessage)
                ? "Соединение с сервером потеряно"
                : recoveryMessage.Trim();
        }

        public static bool TryConsumePending(out string recoveryMessage)
        {
            if (!pending)
            {
                recoveryMessage = string.Empty;
                return false;
            }

            recoveryMessage = message;
            pending = false;
            message = string.Empty;
            return true;
        }
    }
}
