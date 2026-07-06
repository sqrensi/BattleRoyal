namespace ShooterPrototype.Platform
{
    /// <summary>
    /// Holds the latest Yandex signed player token for server-side verification.
    /// </summary>
    public static class YandexPlayerAuthService
    {
        public static string CurrentSignature { get; private set; } = string.Empty;

        public static bool HasSignature => !string.IsNullOrWhiteSpace(CurrentSignature);

        public static void UpdateSignature(string signature)
        {
            CurrentSignature = string.IsNullOrWhiteSpace(signature) ? string.Empty : signature.Trim();
        }

        public static void Clear()
        {
            CurrentSignature = string.Empty;
        }
    }
}
