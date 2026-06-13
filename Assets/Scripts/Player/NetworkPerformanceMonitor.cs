using ShooterPrototype.Network;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShooterPrototype.Player
{
    /// <summary>
    /// Lightweight runtime overlay for FPS, ping, and network send stats. Toggle with F3.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkPerformanceMonitor : MonoBehaviour
    {
        public static readonly ProfilerMarker SendLocalPoseMarker = new ProfilerMarker("Match.SendLocalPose");
        public static readonly ProfilerMarker PollSnapshotMarker = new ProfilerMarker("Match.PollSnapshot");
        public static readonly ProfilerMarker RefreshHiddenBodyMarker = new ProfilerMarker("Clothing.RefreshHiddenBody");

        [SerializeField] private bool showOverlay = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;
        [SerializeField] private RealtimeTransportClient transportClient;

        private float fpsSmoothed;
        private float nextTransportLookupAt;

        public static NetworkPerformanceMonitor Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            var unscaledDelta = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, 1f / unscaledDelta, 0.08f);

            if (WasTogglePressed())
            {
                showOverlay = !showOverlay;
            }

            if (transportClient == null && Time.unscaledTime >= nextTransportLookupAt)
            {
                nextTransportLookupAt = Time.unscaledTime + 1f;
                transportClient = RealtimeTransportClient.Active != null
                    ? RealtimeTransportClient.Active
                    : FindFirstObjectByType<RealtimeTransportClient>();
            }
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            var stats = transportClient != null ? transportClient.GetNetworkStats() : default;
            var conn = transportClient == null
                ? "no client"
                : transportClient.IsReady
                    ? "ready"
                    : transportClient.IsConnected
                        ? "ws open"
                        : transportClient.IsConnecting
                            ? "connecting"
                            : "offline";
            var lines = new[]
            {
                $"FPS {fpsSmoothed:0}",
                $"RT {conn}  ping {Mathf.Max(0, stats.SmoothedRoundTripMs)} ms",
                $"Pose sent {stats.PosesSentPerSecond:0.0}/s  skipped {stats.PosesSkippedPerSecond:0.0}/s",
                $"Pose mode {(stats.UseBinaryPoses ? "binary" : "json")}  hb {stats.PoseHeartbeatSeconds:0.00}s",
                $"Snapshots {stats.SnapshotsPerSecond:0.0}/s  tick {stats.LatestServerTick}",
                "F3 toggle overlay"
            };

            const float width = 400f;
            const float height = 132f;
            var x = Screen.width - width - 8f;
            var y = Screen.height - height - 8f;
            GUI.Box(new Rect(x, y, width, height), "Network Performance");
            for (var i = 0; i < lines.Length; i++)
            {
                GUI.Label(new Rect(x + 8f, y + 20f + i * 16f, width - 16f, 16f), lines[i]);
            }
        }

        private bool WasTogglePressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            return toggleKey switch
            {
                KeyCode.F1 => keyboard.f1Key.wasPressedThisFrame,
                KeyCode.F2 => keyboard.f2Key.wasPressedThisFrame,
                KeyCode.F3 => keyboard.f3Key.wasPressedThisFrame,
                KeyCode.F4 => keyboard.f4Key.wasPressedThisFrame,
                KeyCode.F5 => keyboard.f5Key.wasPressedThisFrame,
                KeyCode.F6 => keyboard.f6Key.wasPressedThisFrame,
                KeyCode.F7 => keyboard.f7Key.wasPressedThisFrame,
                KeyCode.F8 => keyboard.f8Key.wasPressedThisFrame,
                KeyCode.F9 => keyboard.f9Key.wasPressedThisFrame,
                KeyCode.F10 => keyboard.f10Key.wasPressedThisFrame,
                KeyCode.F11 => keyboard.f11Key.wasPressedThisFrame,
                KeyCode.F12 => keyboard.f12Key.wasPressedThisFrame,
                _ => false
            };
        }
    }
}
