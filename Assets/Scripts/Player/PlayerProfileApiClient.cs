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
        public string weaponAssault;
        public string weaponSniper;
        public string weaponPistol;
        public string weaponMp7;
    }

    [Serializable]
    public sealed class SkinQuantityEntry
    {
        public string skinId;
        public int quantity;
    }

    [Serializable]
    public sealed class CaseQuantityEntry
    {
        public string caseId;
        public int quantity;
    }

    [Serializable]
    public sealed class PlayerAchievementEntry
    {
        public string achievementId;
        public string code;
        public string title;
        public string description;
        public int progress;
        public int target;
        public bool completed;
        public long completedAt;
        public string rewardType;
        public int rewardAmount;
        public string rewardCaseId;
    }

    [Serializable]
    public sealed class AchievementCompletedEntry
    {
        public string achievementId;
        public string title;
        public string description;
    }

    [Serializable]
    public sealed class PlayerMatchStatsDto
    {
        public int matchCount;
        public int totalKills;
        public int totalDeaths;
        public int totalWins;
        public float avgPlacement;
        public float avgDamage;
        public float kdRatio;
    }

    [Serializable]
    public sealed class PlayerProfileDto
    {
        public string playerId;
        public string internalPlayerId;
        public string nickname;
        public string selectedCharacterModel;
        public int currencyBalance;
        public int rating;
        public bool starterPackGranted;
        public string[] ownedSkins;
        public SkinQuantityEntry[] ownedSkinQuantities;
        public CaseQuantityEntry[] ownedCaseQuantities;
        public PlayerProfileEquippedDto equipped;
        public PlayerAchievementEntry[] achievements;
        public PlayerMatchStatsDto stats;
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
    public sealed class PlayerProfilePurchaseCaseRequest
    {
        public string caseId;
    }

    [Serializable]
    public sealed class PlayerProfileCaseOpenResponse
    {
        public bool ok;
        public string error;
        public string message;
        public string rolledSkinId;
        public PlayerProfileDto profile;
    }

    [Serializable]
    public sealed class PlayerProfileAchievementEventRequest
    {
        public string eventType;
        public int amount;
    }

    [Serializable]
    public sealed class PlayerProfileAchievementEventResponse
    {
        public bool ok;
        public string error;
        public string message;
        public AchievementCompletedEntry[] newlyCompleted;
        public PlayerProfileDto profile;
    }

    [Serializable]
    public sealed class PlayerProfileClaimAchievementRequest
    {
        public string achievementId;
    }

    [Serializable]
    public sealed class PlayerProfileEquipRequest
    {
        public string slot;
        public string skinId;
    }

    [Serializable]
    public sealed class PlayerProfileMatchRewardRequest
    {
        public int amount;
        public string sourceId;
    }

    [Serializable]
    public sealed class PlayerProfileMatchStatsResponse
    {
        public bool ok;
        public string error;
        public string message;
        public bool alreadyReported;
        public int ratingDelta;
        public PlayerProfileDto profile;
    }

    [Serializable]
    public sealed class PlayerProfileMatchStatsRequest
    {
        public string sourceId;
        public int kills;
        public int deaths;
        public int placement;
        public bool won;
        public int damageDealt;
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

    [Serializable]
    public sealed class LeaderboardEntryDto
    {
        public int rank;
        public string nickname;
        public int rating;
        public string playerId;
    }

    [Serializable]
    public sealed class LeaderboardResponse
    {
        public bool ok;
        public string error;
        public string message;
        public LeaderboardEntryDto[] entries;
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

        public IEnumerator FetchLeaderboard(
            int limit,
            Action<bool, LeaderboardEntryDto[], string> onCompleted)
        {
            var normalizedLimit = Mathf.Clamp(limit, 1, 25);
            var path = $"/profile/leaderboard?limit={normalizedLimit}";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbGET,
                path,
                null,
                (ok, json, error) => ParseLeaderboardResponse(ok, json, error, onCompleted));
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

        public IEnumerator PurchaseCase(
            string playerId,
            string caseId,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfilePurchaseCaseRequest
            {
                caseId = caseId
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/purchase-case";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                path,
                requestBody,
                (ok, json, error) => ParseProfileResponse(ok, json, error, onCompleted));
        }

        public IEnumerator OpenCase(
            string playerId,
            string caseId,
            Action<bool, PlayerProfileDto, string, string> onCompleted)
        {
            var requestBody = new PlayerProfilePurchaseCaseRequest
            {
                caseId = caseId
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/open-case";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                path,
                requestBody,
                (ok, json, error) => ParseCaseOpenResponse(ok, json, error, onCompleted));
        }

        public IEnumerator ReportAchievementEvent(
            string playerId,
            string eventType,
            int amount,
            Action<bool, PlayerProfileDto, AchievementCompletedEntry[], string> onCompleted)
        {
            var requestBody = new PlayerProfileAchievementEventRequest
            {
                eventType = eventType,
                amount = amount
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/achievement-event";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                path,
                requestBody,
                (ok, json, error) =>
                {
                    if (!ok)
                    {
                        onCompleted?.Invoke(false, null, null, ExtractErrorMessage(json, error));
                        return;
                    }

                    var response = ParseJson<PlayerProfileAchievementEventResponse>(json);
                    if (response == null || !response.ok || response.profile == null)
                    {
                        var message = response != null && !string.IsNullOrWhiteSpace(response.message)
                            ? response.message
                            : response != null && !string.IsNullOrWhiteSpace(response.error)
                                ? response.error
                                : "Achievement event failed.";
                        onCompleted?.Invoke(false, null, null, message);
                        return;
                    }

                    onCompleted?.Invoke(
                        true,
                        response.profile,
                        response.newlyCompleted,
                        string.Empty);
                });
        }

        public IEnumerator ClaimAchievement(
            string playerId,
            string achievementId,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfileClaimAchievementRequest
            {
                achievementId = achievementId
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/claim-achievement";
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

        public IEnumerator GrantMatchReward(
            string playerId,
            int amount,
            string sourceId,
            Action<bool, PlayerProfileDto, string> onCompleted)
        {
            var requestBody = new PlayerProfileMatchRewardRequest
            {
                amount = amount,
                sourceId = sourceId ?? string.Empty
            };

            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/match-reward";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                path,
                requestBody,
                (ok, json, error) => ParseProfileResponse(ok, json, error, onCompleted));
        }

        public IEnumerator RecordMatchStats(
            string playerId,
            PlayerProfileMatchStatsRequest request,
            Action<bool, int, PlayerProfileDto, string> onCompleted)
        {
            var path = $"/profile/{UnityWebRequest.EscapeURL(playerId)}/match-stats";
            yield return SendRequest(
                UnityWebRequest.kHttpVerbPOST,
                path,
                request,
                (ok, json, error) => ParseMatchStatsResponse(ok, json, error, onCompleted));
        }

        private static void ParseMatchStatsResponse(
            bool ok,
            string json,
            string error,
            Action<bool, int, PlayerProfileDto, string> onCompleted)
        {
            if (!ok)
            {
                onCompleted?.Invoke(false, 0, null, ExtractErrorMessage(json, error));
                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                onCompleted?.Invoke(false, 0, null, "Empty match stats response.");
                return;
            }

            PlayerProfileMatchStatsResponse response;
            try
            {
                response = ParseJson<PlayerProfileMatchStatsResponse>(json);
            }
            catch (Exception exception)
            {
                onCompleted?.Invoke(false, 0, null, exception.Message);
                return;
            }

            if (response == null || !response.ok)
            {
                onCompleted?.Invoke(
                    false,
                    0,
                    null,
                    response != null && !string.IsNullOrWhiteSpace(response.message)
                        ? response.message
                        : "Match stats request failed.");
                return;
            }

            var profile = response.profile;
            if (profile == null)
            {
                var fallback = ParseJson<PlayerProfileResponse>(json);
                profile = fallback?.profile;
            }

            if (profile == null)
            {
                onCompleted?.Invoke(false, 0, null, "Match stats response missing profile.");
                return;
            }

            onCompleted?.Invoke(true, response.ratingDelta, profile, string.Empty);
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

        private static void ParseLeaderboardResponse(
            bool ok,
            string json,
            string error,
            Action<bool, LeaderboardEntryDto[], string> onCompleted)
        {
            if (!ok)
            {
                onCompleted?.Invoke(false, null, ExtractErrorMessage(json, error));
                return;
            }

            var response = ParseJson<LeaderboardResponse>(json);
            if (response == null)
            {
                onCompleted?.Invoke(false, null, "Invalid leaderboard response.");
                return;
            }

            if (!response.ok)
            {
                var message = !string.IsNullOrWhiteSpace(response.message)
                    ? response.message
                    : !string.IsNullOrWhiteSpace(response.error)
                        ? response.error
                        : "Invalid leaderboard response.";
                onCompleted?.Invoke(false, null, message);
                return;
            }

            onCompleted?.Invoke(true, response.entries ?? System.Array.Empty<LeaderboardEntryDto>(), string.Empty);
        }

        private static void ParseCaseOpenResponse(
            bool ok,
            string json,
            string error,
            Action<bool, PlayerProfileDto, string, string> onCompleted)
        {
            if (!ok)
            {
                onCompleted?.Invoke(false, null, ExtractErrorMessage(json, error), string.Empty);
                return;
            }

            var response = ParseJson<PlayerProfileCaseOpenResponse>(json);
            if (response == null)
            {
                onCompleted?.Invoke(false, null, "Invalid case open response.", string.Empty);
                return;
            }

            if (!response.ok || response.profile == null)
            {
                var message = !string.IsNullOrWhiteSpace(response.message)
                    ? response.message
                    : !string.IsNullOrWhiteSpace(response.error)
                        ? response.error
                        : "Case purchase failed.";
                onCompleted?.Invoke(false, null, message, string.Empty);
                return;
            }

            onCompleted?.Invoke(
                true,
                response.profile,
                string.Empty,
                response.rolledSkinId ?? string.Empty);
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
