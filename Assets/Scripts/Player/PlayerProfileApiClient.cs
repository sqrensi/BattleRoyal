using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ShooterPrototype.Player
{
    [Serializable]
    public sealed class PlayerProfileEquippedDto
    {
        public string shirt;
        public string pants;
        public string boots;
        public string gloves;
        public string face;
        public string hair;
    }

    [Serializable]
    public sealed class PlayerProfileDto
    {
        public string playerId;
        public string internalPlayerId;
        public string nickname;
        public string selectedCharacterModel;
        public int currencyBalance;
        public bool starterPackGranted;
        public string[] ownedSkins;
        public PlayerProfileEquippedDto equipped;
    }

    [Serializable]
    public sealed class PlayerProfileEnsureRequest
    {
        public string playerId;
    }

    [Serializable]
    public sealed class PlayerProfileResponse
    {
        public bool ok;
        public string error;
        public string message;
        public PlayerProfileDto profile;
    }

    [Serializable]
    public sealed class PlayerProfilePurchaseRequest
    {
        public string skinId;
    }

    [Serializable]
    public sealed class PlayerProfileEquipRequest
    {
        public string slot;
        public string skinId;
    }

    [Serializable]
    public sealed class PlayerProfileSetNicknameRequest
    {
        public string nickname;
    }

    [Serializable]
    public sealed class PlayerNicknameAvailabilityResponse
    {
        public bool ok;
        public bool available;
        public string error;
        public string message;
        public string nickname;
    }

    public sealed class PlayerProfileApiClient : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "http://127.0.0.1:5050";
        [SerializeField] private float requestTimeoutSeconds = 5f;

        public void Configure(string apiBaseUrl, float timeoutSeconds)
        {
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                baseUrl = apiBaseUrl.TrimEnd('/');
            }

            requestTimeoutSeconds = Mathf.Max(1f, timeoutSeconds);
        }

        public IEnumerator EnsureProfile(string playerId, Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfileEnsureRequest
            {
                playerId = playerId
            };

            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                "/profile/ensure",
                requestBody,
                (ok, json, error) => ParseProfileResponse(ok, json, error, onCompleted));
        }

        public IEnumerator PurchaseSkin(
            string playerId,
            string skinId,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfilePurchaseRequest
            {
                skinId = skinId
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/purchase";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                path,
                requestBody,
                (ok, json, error) => ParseProfileResponse(ok, json, error, onCompleted));
        }

        public IEnumerator SetNickname(
            string playerId,
            string nickname,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfileSetNicknameRequest
            {
                nickname = nickname
            };

            var path = $"/profile/{Uri.EscapeDataString(playerId)}/nickname";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPUT,
                path,
                requestBody,
                (ok, json, error) => ParseProfileResponse(ok, json, error, onCompleted));
        }

        public IEnumerator CheckNicknameAvailable(
            string playerId,
            string nickname,
            Action<bool, bool, string, string> onCompleted)
        {
            var encodedNickname = UnityWebRequest.EscapeURL(nickname ?? string.Empty);
            var encodedPlayerId = UnityWebRequest.EscapeURL(playerId ?? string.Empty);
            var path = $"/profile/nickname/available?nickname={encodedNickname}&playerId={encodedPlayerId}";

            yield return SendRequest(
                UnityWebRequest.kHttpVerbGET,
                path,
                null,
                (ok, json, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, false, error, string.Empty);
                        return;
                    }

                    var response = ParseJson<PlayerNicknameAvailabilityResponse>(json);
                    if (response == null)
                    {
                        onCompleted?.Invoke(false, false, "Invalid nickname check response.", string.Empty);
                        return;
                    }

                    if (!response.ok)
                    {
                        onCompleted?.Invoke(
                            true,
                            false,
                            response.message ?? response.error ?? "Invalid nickname.",
                            response.nickname ?? string.Empty);
                        return;
                    }

                    onCompleted?.Invoke(true, response.available, response.message ?? string.Empty, response.nickname ?? string.Empty);
                });
        }

        public IEnumerator EquipSkin(
            string playerId,
            string slot,
            string skinId,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfileEquipRequest
            {
                slot = slot,
                skinId = skinId
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/equipped";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPUT,
                path,
                requestBody,
                (ok, json, error) => ParseProfileResponse(ok, json, error, onCompleted));
        }

        private static void ParseProfileResponse(
            bool ok,
            string json,
            string error,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            if (!ok)
            {
                onCompleted?.Invoke(false, null, ExtractErrorMessage(json, error));
                return;
            }

            var response = ParseJson<PlayerProfileResponse>(json);
            if (response == null)
            {
                onCompleted?.Invoke(false, null, "Invalid profile response.");
                return;
            }

            if (!response.ok || response.profile == null)
            {
                var message = !string.IsNullOrWhiteSpace(response.message)
                    ? response.message
                    : !string.IsNullOrWhiteSpace(response.error)
                        ? response.error
                        : "Invalid profile response.";
                onCompleted?.Invoke(false, null, message);
                return;
            }

            onCompleted?.Invoke(true, response.profile, string.Empty);
        }

        private IEnumerator SendRequest(
            string method,
            string path,
            object requestBody,
            Action<bool, string, string> onCompleted)
        {
            var url = BuildUrl(path);
            using (var request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, Mathf.RoundToInt(requestTimeoutSeconds));

                if (requestBody != null)
                {
                    var payloadJson = JsonUtility.ToJson(requestBody);
                    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                    request.uploadHandler = new UploadHandlerRaw(payloadBytes);
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                yield return request.SendWebRequest();

                var responseText = request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    var status = request.responseCode > 0 ? $"HTTP {request.responseCode}. " : string.Empty;
                    var error = $"{status}{request.error}".Trim();
                    if (!string.IsNullOrWhiteSpace(responseText))
                    {
                        error = $"{error} {responseText}".Trim();
                    }

                    onCompleted?.Invoke(false, responseText, error);
                    yield break;
                }

                onCompleted?.Invoke(true, responseText, string.Empty);
            }
        }

        private string BuildUrl(string path)
        {
            var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:5050"
                : baseUrl.TrimEnd('/');

            if (string.IsNullOrWhiteSpace(path))
            {
                return normalizedBase;
            }

            return path[0] == '/'
                ? $"{normalizedBase}{path}"
                : $"{normalizedBase}/{path}";
        }

        private static string ExtractErrorMessage(string json, string fallback)
        {
            var response = ParseJson<PlayerProfileResponse>(json);
            if (response != null)
            {
                if (!string.IsNullOrWhiteSpace(response.message))
                {
                    return response.message;
                }

                if (!string.IsNullOrWhiteSpace(response.error))
                {
                    return response.error;
                }
            }

            return fallback ?? string.Empty;
        }

        private static T ParseJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
