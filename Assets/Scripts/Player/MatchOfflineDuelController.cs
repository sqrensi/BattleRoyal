using System;
using System.Collections;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using UnityEngine;
using UnityEngine.AI;

namespace ShooterPrototype.Player
{
    public sealed class MatchOfflineDuelController : MonoBehaviour
    {
        private const int RoundsToWin = 5;
        private const int DuelWeaponSpareAmmo = 60;
        private const float PrepSeconds = 15f;
        private const float WeaponPickSeconds = 10f;
        private const float RoundSeconds = 60f;
        private const float RoundEndSeconds = 3f;

        private enum RoundWinner
        {
            None = 0,
            Local = 1,
            Opponent = 2,
        }

        private static readonly string[] BotNicknames =
        {
            "Viper", "Ghost", "Blaze", "Raptor", "Nova", "Cipher", "Ace", "Hawk",
        };

        private GameHudController gameHud;
        private CombatHudController combatHud;
        private LocalPlayerMarker localPlayer;
        private FpsCharacterController fpsController;
        private CharacterController characterController;
        private PlayerWeaponLoadoutController weaponLoadoutController;
        private PlayerWeaponController weaponController;
        private PlayerPickupController pickupController;
        private PlayerHealth playerHealth;

        private DuelNavBotController bot;
        private int botDuelRating;
        private int localRoundWins;
        private int botRoundWins;
        private int damageDealtToOpponent;
        private int roundNumber;
        private int localTeamIndex;
        private int localSpawnSlot;
        private int botTeamIndex;
        private int botSpawnSlot;
        private bool sessionStarted;
        private bool matchEnded;
        private bool localWeaponPicked;
        private bool matchOutcomeScheduled;
        private string currentPhase = "prep";
        private int countdownRemainingSeconds;
        private RoundWinner lastRoundWinner = RoundWinner.None;
        private Coroutine flowCoroutine;

        public static MatchOfflineDuelController Active { get; private set; }

        public bool IsSessionActive => sessionStarted && !matchEnded;

        public int LocalRoundWins => localRoundWins;

        public int BotRoundWins => botRoundWins;

        public string OpponentNickname => bot != null ? bot.Nickname : "Соперник";

        public int DamageDealtToOpponent => damageDealtToOpponent;

        public void RecordDamageToOpponent(float damage)
        {
            if (damage <= 0f)
            {
                return;
            }

            var rounded = Mathf.RoundToInt(damage);
            damageDealtToOpponent += rounded;
            MatchScoreboardTracker.AddDamage("offline-bot", rounded);
        }

        private void Awake()
        {
            Active = this;
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        private void OnDisable()
        {
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            gameHud?.ClearDuelRoundBanner();
            gameHud?.ClearDuelRoundEndCountdown();
        }

        private void Update()
        {
            if (!sessionStarted || matchEnded)
            {
                return;
            }

            if (string.Equals(currentPhase, "round", StringComparison.Ordinal))
            {
                if (countdownRemainingSeconds > 0 && countdownRemainingSeconds <= 5)
                {
                    gameHud?.SetDuelRoundEndCountdown(countdownRemainingSeconds);
                }
                else
                {
                    gameHud?.ClearDuelRoundEndCountdown();
                }
            }
            else
            {
                gameHud?.ClearDuelRoundEndCountdown();
            }
        }

        public void PrepareForNewMatch()
        {
            sessionStarted = false;
            matchEnded = false;
            matchOutcomeScheduled = false;
            localRoundWins = 0;
            botRoundWins = 0;
            damageDealtToOpponent = 0;
            roundNumber = 0;
            localWeaponPicked = false;
            botDuelRating = 0;
            currentPhase = "prep";
            lastRoundWinner = RoundWinner.None;
            if (flowCoroutine != null)
            {
                StopCoroutine(flowCoroutine);
                flowCoroutine = null;
            }

            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();

            if (bot != null)
            {
                Destroy(bot.gameObject);
                bot = null;
            }
        }

        public void OnLocalPlayerSpawned(LocalPlayerMarker marker)
        {
            if (marker != null)
            {
                localPlayer = marker;
            }

            if (bot != null)
            {
                TrainingBotFactory.RefreshDuelBotPresentation(bot.gameObject);
            }
        }

        public IEnumerator StartSessionRoutine()
        {
            if (sessionStarted)
            {
                yield break;
            }

            sessionStarted = true;
            matchEnded = false;
            CacheReferences();
            yield return WaitForLocalPlayerRoutine();
            if (localPlayer == null)
            {
                sessionStarted = false;
                Debug.LogError("[MatchOfflineDuel] Local player not found.");
                yield break;
            }

            BindLocalPlayer();
            yield return EnsureNavMeshReadyRoutine();
            WeaponBlockUtility.RemoveSceneBotOccluderProxies(gameObject.scene);
            RollSpawns();
            SpawnBot();
            EnableLocalPlayerForDuel();
            yield return EnsureHudReadyRoutine();
            gameHud?.SetScoreboardLocalTicket("offline-local");
            ClearLocalLoadout();
            flowCoroutine = StartCoroutine(MatchFlowRoutine());
        }

        private IEnumerator EnsureHudReadyRoutine()
        {
            for (var i = 0; i < 30; i++)
            {
                CacheReferences();
                gameHud?.WarmUpMatchUi();
                if (gameHud != null)
                {
                    break;
                }

                yield return null;
            }

            countdownRemainingSeconds = Mathf.CeilToInt(PrepSeconds);
            UpdateHud();
            yield return null;
        }

        private IEnumerator MatchFlowRoutine()
        {
            yield return RunPrepPhase();

            while (!matchEnded)
            {
                yield return RunRoundPickPhase();
                yield return RunRoundPhase();
                yield return RunRoundEndPhase();

                if (localRoundWins >= RoundsToWin || botRoundWins >= RoundsToWin)
                {
                    matchEnded = true;
                    break;
                }

                roundNumber += 1;
                RollSpawns();
            }

            currentPhase = "ending";
            SetMovementLocked(true);
            SetLocalCombatEnabled(false);
            bot?.SetCombatEnabled(false);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            gameHud?.SetMatchStatusMessage(string.Empty);
            UpdateHud();

            var localWon = localRoundWins > botRoundWins;
            if (!matchOutcomeScheduled)
            {
                matchOutcomeScheduled = true;
                var summary = MatchOutcomeSummary.CreateDuel(localWon, localRoundWins, 0);
                gameHud?.ScheduleGameOver(localWon, summary);
            }
        }

        private IEnumerator RunPrepPhase()
        {
            currentPhase = "prep";
            EnsureHudReady();
            TeleportLocalToSpawn(revive: true);
            bot?.WarpTo(GetSpawnPosition(botTeamIndex, botSpawnSlot), GetSpawnRotation(botTeamIndex, botSpawnSlot));
            bot?.SetCombatEnabled(false);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            ClearLocalLoadout();
            SetMovementLocked(false);
            SetLocalCombatEnabled(false);

            var deadline = Time.unscaledTime + PrepSeconds;
            while (Time.unscaledTime < deadline)
            {
                countdownRemainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, deadline - Time.unscaledTime));
                UpdateHud();
                yield return null;
            }
        }

        private IEnumerator RunRoundPickPhase()
        {
            currentPhase = "round_pick";
            EnsureHudReady();
            if (roundNumber <= 0)
            {
                roundNumber = 1;
            }

            TeleportLocalToSpawn(revive: true);
            PrepareLocalPlayerForRoundStart();
            bot?.WarpTo(GetSpawnPosition(botTeamIndex, botSpawnSlot), GetSpawnRotation(botTeamIndex, botSpawnSlot));
            bot?.SetCombatEnabled(false);

            localWeaponPicked = false;
            ClearLocalLoadout();
            fpsController?.SetWeaponPickUiMode(true);
            gameHud?.ShowDuelWeaponPickPanel(HandleLocalWeaponPicked);
            SetMovementLocked(true);
            SetLocalCombatEnabled(false);
            gameHud?.ClearDuelRoundBanner();

            var deadline = Time.unscaledTime + WeaponPickSeconds;
            while (Time.unscaledTime < deadline)
            {
                countdownRemainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, deadline - Time.unscaledTime));
                UpdateHud();
                yield return null;
            }

            if (!localWeaponPicked)
            {
                ApplyLocalWeaponKind(WeaponKind.AssaultRifle);
            }

            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
        }

        private IEnumerator RunRoundPhase()
        {
            currentPhase = "round";
            EnsureHudReady();
            PrepareLocalPlayerForRoundStart();
            bot?.WarpTo(GetSpawnPosition(botTeamIndex, botSpawnSlot), GetSpawnRotation(botTeamIndex, botSpawnSlot));
            bot?.PrepareCombatRound();
            bot?.SetCombatTarget(localPlayer.transform);
            bot?.SetCombatEnabled(true);

            SetMovementLocked(false);
            SetLocalCombatEnabled(true);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            gameHud?.ClearDuelRoundBanner();

            lastRoundWinner = RoundWinner.None;
            var deadline = Time.unscaledTime + RoundSeconds;

            while (Time.unscaledTime < deadline)
            {
                countdownRemainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, deadline - Time.unscaledTime));
                UpdateHud();

                if (playerHealth != null && playerHealth.IsDead)
                {
                    botRoundWins++;
                    lastRoundWinner = RoundWinner.Opponent;
                    combatHud?.ShowDuelDeathBanner(bot != null ? bot.Nickname : "Соперник");
                    yield break;
                }

                if (bot != null && bot.GetComponent<PlayerHealth>() is { IsDead: true })
                {
                    localRoundWins++;
                    lastRoundWinner = RoundWinner.Local;
                    combatHud?.ShowDuelKillBanner(bot.Nickname);
                    yield break;
                }

                yield return null;
            }

            if (playerHealth != null && bot != null)
            {
                var localHp = playerHealth.CurrentHealth;
                var botHp = bot.GetComponent<PlayerHealth>()?.CurrentHealth ?? 0f;
                if (localHp > botHp)
                {
                    localRoundWins++;
                    lastRoundWinner = RoundWinner.Local;
                    combatHud?.ShowDuelKillBanner(bot.Nickname);
                }
                else if (botHp > localHp)
                {
                    botRoundWins++;
                    lastRoundWinner = RoundWinner.Opponent;
                    combatHud?.ShowDuelDeathBanner(bot.Nickname);
                }
            }
        }

        private IEnumerator RunRoundEndPhase()
        {
            currentPhase = "round_end";
            bot?.SetCombatEnabled(false);
            SetMovementLocked(false);
            SetLocalCombatEnabled(true);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();

            var deadline = Time.unscaledTime + RoundEndSeconds;
            while (Time.unscaledTime < deadline)
            {
                countdownRemainingSeconds = Mathf.CeilToInt(Mathf.Max(0f, deadline - Time.unscaledTime));
                UpdateHud();
                yield return null;
            }
        }

        private void HandleLocalWeaponPicked(WeaponKind kind)
        {
            if (!string.Equals(currentPhase, "round_pick", StringComparison.Ordinal) || localWeaponPicked)
            {
                return;
            }

            localWeaponPicked = true;
            ApplyLocalWeaponKind(kind);
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
        }

        private void PrepareLocalPlayerForRoundStart()
        {
            if (localPlayer == null || playerHealth == null)
            {
                return;
            }

            if (playerHealth.IsDead)
            {
                var position = GetSpawnPosition(localTeamIndex, localSpawnSlot);
                var rotation = GetSpawnRotation(localTeamIndex, localSpawnSlot);
                playerHealth.ForceReviveAt(position, rotation);
                fpsController?.NotifyLocalRespawned(1.5f);
            }
            else
            {
                playerHealth.RestoreFullHealthForRoundStart();
            }
        }

        private void ApplyLocalWeaponKind(WeaponKind kind)
        {
            if (weaponLoadoutController == null)
            {
                return;
            }

            if (!weaponLoadoutController.ApplyDuelRoundWeaponPick(kind, DuelWeaponSpareAmmo))
            {
                return;
            }

            weaponLoadoutController.EnsureActiveWeaponEquipped(drawWeapon: true);
            localPlayer.GetComponent<PlayerWeaponHolsterController>()?.ForceArmedState();
            weaponController?.CancelActiveReload();
            weaponController?.RefreshWeaponAvailability();
            pickupController?.RefreshWeaponAvailability();
        }

        private void ClearLocalLoadout()
        {
            if (localPlayer == null)
            {
                return;
            }

            weaponLoadoutController ??= localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            weaponLoadoutController?.Loadout?.ClearForSpawn();

            var weaponMount = localPlayer.GetComponent<PlayerWeaponMount>();
            if (weaponMount != null && weaponMount.HasMountedWeapon)
            {
                weaponMount.UnequipWeapon();
            }

            if (weaponController != null)
            {
                weaponController.CancelActiveReload();
                weaponController.SetCurrentAmmo(0);
                weaponController.RefreshWeaponAvailability();
            }

            localPlayer.GetComponent<PlayerWeaponHolsterController>()?.ForceHolsteredIdleState();
            pickupController?.RefreshWeaponAvailability();
        }

        private void EnsureHudReady()
        {
            CacheReferences();
            gameHud?.WarmUpMatchUi();
        }

        private void SpawnBot()
        {
            var nickname = BotNicknames[UnityEngine.Random.Range(0, BotNicknames.Length)];
            var skill = UnityEngine.Random.Range(0.38f, 0.72f);
            var pose = GetSpawnPosition(botTeamIndex, botSpawnSlot);
            var rotation = GetSpawnRotation(botTeamIndex, botSpawnSlot);
            bot = TrainingBotFactory.CreateDuelBot(pose, rotation, nickname, skill);
            botDuelRating = MatchRatingUtility.RollNearbyDuelRating(PlayerProfileService.DuelRating);
            bot.SetCombatTarget(localPlayer.transform);
            bot.SetCombatEnabled(false);
        }

        private void RollSpawns()
        {
            localTeamIndex = 0;
            botTeamIndex = 1;
            localSpawnSlot = UnityEngine.Random.Range(0, 3);
            botSpawnSlot = UnityEngine.Random.Range(0, 3);
        }

        private void TeleportLocalToSpawn(bool revive)
        {
            var position = GetSpawnPosition(localTeamIndex, localSpawnSlot);
            var rotation = GetSpawnRotation(localTeamIndex, localSpawnSlot);
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            localPlayer.transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();

            if (characterController != null)
            {
                characterController.enabled = true;
            }

            if (revive)
            {
                playerHealth?.ForceReviveAt(position, rotation);
                fpsController?.NotifyLocalRespawned(1.5f);
            }
        }

        private static Vector3 GetSpawnPosition(int teamIndex, int slotIndex)
        {
            return DuelSpawnUtility.TryResolveSpawnPose(teamIndex, slotIndex, out var position, out _)
                ? position
                : Vector3.zero;
        }

        private static Quaternion GetSpawnRotation(int teamIndex, int slotIndex)
        {
            return DuelSpawnUtility.TryResolveSpawnPose(teamIndex, slotIndex, out _, out var rotation)
                ? rotation
                : Quaternion.identity;
        }

        private void EnableLocalPlayerForDuel()
        {
            fpsController?.SetMovementLocked(false);
            fpsController?.SetServerReconciliationSuspended(true);
            fpsController?.SetGameOverMode(false);
            playerHealth?.SetEliminationMode(true);
            playerHealth?.SetNetworkMode(false);
            if (pickupController != null)
            {
                pickupController.enabled = true;
            }

            if (weaponController != null)
            {
                weaponController.enabled = true;
            }

            weaponController?.SetDuelFireBlocked(true);
        }

        private void SetMovementLocked(bool locked)
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                locked = true;
            }

            fpsController?.SetMovementLocked(locked);
        }

        private void SetLocalCombatEnabled(bool enabled)
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                enabled = false;
            }

            weaponController?.SetDuelFireBlocked(!enabled);
            if (weaponController != null && enabled)
            {
                weaponController.RefreshWeaponAvailability();
            }

            if (pickupController != null)
            {
                pickupController.enabled = enabled;
            }
        }

        private void BindLocalPlayer()
        {
            fpsController = localPlayer.GetComponent<FpsCharacterController>();
            characterController = localPlayer.GetComponent<CharacterController>();
            weaponLoadoutController = localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            weaponController = localPlayer.GetComponent<PlayerWeaponController>();
            pickupController = localPlayer.GetComponent<PlayerPickupController>();
            playerHealth = localPlayer.GetComponent<PlayerHealth>();
        }

        private void CacheReferences()
        {
            gameHud = FindFirstObjectByType<GameHudController>();
            combatHud = FindFirstObjectByType<CombatHudController>();
            localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
        }

        private IEnumerator WaitForLocalPlayerRoutine()
        {
            var deadline = Time.unscaledTime + 10f;
            while (localPlayer == null && Time.unscaledTime < deadline)
            {
                localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
                yield return null;
            }
        }

        private IEnumerator EnsureNavMeshReadyRoutine()
        {
            var sampleOrigin = GetSpawnPosition(localTeamIndex, localSpawnSlot);
            if (NavMesh.SamplePosition(sampleOrigin, out _, 48f, NavMesh.AllAreas))
            {
                yield break;
            }

            Debug.LogWarning(
                "[MatchOfflineDuel] NavMesh not found on 1x1. Bake NavMesh Surface on walkable floor.");
            yield return null;
        }

        private void UpdateHud()
        {
            gameHud?.SetDuelMatchHud(
                localRoundWins,
                botRoundWins,
                roundNumber,
                RoundsToWin,
                countdownRemainingSeconds,
                BuildDuelPhaseLabel());
            RefreshDuelPlayersPanel();
            gameHud?.SetMatchStatusMessage(string.Empty);
        }

        private void RefreshDuelPlayersPanel()
        {
            if (gameHud == null)
            {
                return;
            }

            var opponentNick = bot != null ? bot.Nickname : "Соперник";
            var opponentRating = botDuelRating > 0
                ? botDuelRating
                : MatchRatingUtility.DefaultRating;

            gameHud.SetDuelPlayersPanel(
                PlayerProfileService.Nickname,
                PlayerProfileService.DuelRating,
                opponentNick,
                opponentRating);
        }

        private string BuildDuelPhaseLabel()
        {
            var countdown = Mathf.Max(0, countdownRemainingSeconds);
            switch (currentPhase)
            {
                case "prep":
                    return $"Подготовка — {countdown} сек.";
                case "round_pick":
                    return $"Выбор оружия — {countdown} сек.";
                case "round":
                    return $"Раунд {Mathf.Max(1, roundNumber)} — {countdown} сек.";
                case "round_end":
                    if (lastRoundWinner == RoundWinner.Local)
                    {
                        return $"Раунд за тобой — {countdown} сек.";
                    }

                    if (lastRoundWinner == RoundWinner.Opponent)
                    {
                        return $"Раунд за соперником — {countdown} сек.";
                    }

                    return $"Перерыв — {countdown} сек.";
                case "ending":
                    return localRoundWins > botRoundWins ? "Победа!" : "Поражение";
                default:
                    return string.Empty;
            }
        }
    }
}
