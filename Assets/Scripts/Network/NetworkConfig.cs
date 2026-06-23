using UnityEngine;

namespace ShooterPrototype.Network
{
    [CreateAssetMenu(
        fileName = "NetworkConfig",
        menuName = "Shooter Prototype/Network Config",
        order = 1)]
    public sealed class NetworkConfig : ScriptableObject
    {
        [Header("Connection")]
        [SerializeField] private string serverAddress = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private float connectTimeoutSeconds = 5f;

        [Header("Queue API")]
        [SerializeField] private string queueApiBaseUrl = "http://127.0.0.1:5050";
        [Tooltip("Used in WebGL when the game page is HTTPS (Yandex Games). Required there — browsers block HTTP API calls.")]
        [SerializeField] private string queueApiBaseUrlSecure = string.Empty;
        [SerializeField] private string realtimeWsUrl = "ws://127.0.0.1:5051";
        [Tooltip("Secure WebSocket URL for WebGL on HTTPS pages.")]
        [SerializeField] private string realtimeWsUrlSecure = string.Empty;
        [SerializeField] private float queueRequestTimeoutSeconds = 5f;
        [SerializeField] private float queuePollIntervalSeconds = 0.5f;

        [Header("Mock Dedicated Server")]
        [SerializeField] private bool autoStartMockServerInBatchMode = true;
        [SerializeField] private bool allowMockServerInEditor = false;

        public string ServerAddress => serverAddress;
        public int ServerPort => serverPort;
        public float ConnectTimeoutSeconds => connectTimeoutSeconds;
        public string QueueApiBaseUrl => queueApiBaseUrl;
        public string QueueApiBaseUrlSecure => queueApiBaseUrlSecure;
        public string RealtimeWsUrl => realtimeWsUrl;
        public string RealtimeWsUrlSecure => realtimeWsUrlSecure;

        public string ResolveQueueApiBaseUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsBrowserPageSecure && !string.IsNullOrWhiteSpace(queueApiBaseUrlSecure))
            {
                return queueApiBaseUrlSecure.TrimEnd('/');
            }
#endif
            return string.IsNullOrWhiteSpace(queueApiBaseUrl)
                ? "http://127.0.0.1:5050"
                : queueApiBaseUrl.TrimEnd('/');
        }

        public string ResolveRealtimeWsUrl()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (IsBrowserPageSecure && !string.IsNullOrWhiteSpace(realtimeWsUrlSecure))
            {
                return realtimeWsUrlSecure.TrimEnd('/');
            }
#endif
            return string.IsNullOrWhiteSpace(realtimeWsUrl)
                ? "ws://127.0.0.1:5051"
                : realtimeWsUrl.TrimEnd('/');
        }

        public bool IsQueueApiMixedContentBlocked()
        {
            return IsBrowserPageSecure &&
                   ResolveQueueApiBaseUrl().StartsWith("http://", System.StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBrowserPageSecure
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var pageUrl = Application.absoluteURL;
                return !string.IsNullOrEmpty(pageUrl) &&
                       pageUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);
#else
                return false;
#endif
            }
        }
        public float QueueRequestTimeoutSeconds => queueRequestTimeoutSeconds;
        public float QueuePollIntervalSeconds => queuePollIntervalSeconds;
        public bool AutoStartMockServerInBatchMode => autoStartMockServerInBatchMode;
        public bool AllowMockServerInEditor => allowMockServerInEditor;
    }
}
