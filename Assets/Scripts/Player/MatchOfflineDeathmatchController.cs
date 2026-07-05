using System.Collections;
using System.Collections.Generic;
using ShooterPrototype.Matchmaking;
using ShooterPrototype.UI;
using UnityEngine;
using UnityEngine.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShooterPrototype.Player
{
    public sealed class MatchOfflineDeathmatchController : MonoBehaviour
    {
        public const float MatchDurationSeconds = 300f;
        private const float PrepSeconds = 10f;

        private const int BotCount = 3;
        private const int DmWeaponSpareAmmo = 999;
        private const float RespawnDelaySeconds = 3f;

        private static readonly string[] BotNicknamePool =
        {
            "Viper", "Ghost", "Blaze", "Raptor", "Nova", "Cipher", "Ace", "Hawk",
            "Storm", "Rogue", "Falcon", "Hex", "Rex", "Bolt", "Shade",
        };

        private readonly List<DmBotEntry> bots = new List<DmBotEntry>(BotCount);
        private readonly List<PlayerHealth> combatants = new List<PlayerHealth>(BotCount + 1);
        private readonly List<int> botSpawnSlotScratch = new List<int>(BotCount);
        private int localPlayerSpawnSlot = -1;

        private GameHudController gameHud;
        private CombatHudController combatHud;
        private GameKillFeedController killFeed;
        private LocalPlayerMarker localPlayer;
        private FpsCharacterController fpsController;
        private PlayerWeaponLoadoutController weaponLoadoutController;
        private PlayerWeaponController weaponController;
        private PlayerPickupController pickupController;
        private PlayerHealth playerHealth;

        private float remainingSeconds = MatchDurationSeconds;
        private int localKillCount;
        private int localDeathCount;
        private bool sessionStarted;
        private bool sessionEnded;
        private bool combatPhaseActive;
        private bool initialWeaponPickDone;
        private bool manualWeaponPickOpen;
        private Coroutine localPlayerRespawnRoutine;
        private readonly Dictionary<DuelNavBotController, Coroutine> botRespawnRoutines =
            new Dictionary<DuelNavBotController, Coroutine>(BotCount);

        public static MatchOfflineDeathmatchController Active { get; private set; }

        public bool IsSessionActive => sessionStarted && !sessionEnded && combatPhaseActive;

        public int LocalKillCount => localKillCount;

        public int LocalDeathCount => localDeathCount;

        private struct DmBotEntry
        {
            public DuelNavBotController Bot;
            public string TicketId;
            public string Nickname;
            public int SpawnSlot;
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
            CancelAllRespawnRoutines();
            CloseWeaponPickUi();
        }

        public void PrepareForNewMatch()
        {
            sessionStarted = false;
            sessionEnded = false;
            combatPhaseActive = false;
            localKillCount = 0;
            localDeathCount = 0;
            remainingSeconds = MatchDurationSeconds;
            initialWeaponPickDone = false;
            manualWeaponPickOpen = false;
            localPlayerSpawnSlot = -1;
            combatants.Clear();
            ClearBots();
        }

        public void OnLocalPlayerSpawned(LocalPlayerMarker marker)
        {
            if (marker != null)
            {
                localPlayer = marker;
            }
        }

        public bool TryFindNearestEnemy(
            Vector3 origin,
            PlayerHealth exclude,
            float maxDistance,
            out Transform target)
        {
            target = null;
            var bestDistance = maxDistance;
            for (var i = 0; i < combatants.Count; i++)
            {
                var candidate = combatants[i];
                if (candidate == null || candidate == exclude || candidate.IsDead)
                {
                    continue;
                }

                var distance = Vector3.Distance(origin, candidate.transform.position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                target = candidate.transform;
            }

            return target != null;
        }

        private void Update()
        {
            if (!sessionStarted || sessionEnded || !combatPhaseActive)
            {
                return;
            }

            TryOpenManualWeaponPick();
            if (manualWeaponPickOpen)
            {
                fpsController?.MaintainWeaponPickCursor();
            }
        }

        public IEnumerator StartSessionRoutine()
        {
            if (sessionStarted)
            {
                yield break;
            }

            sessionStarted = true;
            sessionEnded = false;
            CacheReferences();
            yield return WaitForLocalPlayerRoutine();
            if (localPlayer == null)
            {
                sessionStarted = false;
                Debug.LogError("[MatchOfflineDeathmatch] Local player not found.");
                yield break;
            }

            localPlayerSpawnSlot = -1;
            BindLocalPlayer();
            playerHealth?.ConfigureOfflineDmLocalPlayer();
            RegisterCombatant(playerHealth);
            TeleportLocalPlayerToUniqueSpawn();
            yield return EnsureNavMeshReadyRoutine();
            WeaponBlockUtility.RemoveSceneBotOccluderProxies(gameObject.scene);
            PrepareLocalPlayerForPrep();
            yield return SpawnBotsRoutine();
            yield return EnsureHudReadyRoutine();
            gameHud?.SetScoreboardLocalTicket("offline-local");
            MatchScoreboardTracker.Reset("offline-local");
            EnsureScoreboardParticipants();
            yield return RunPrepPhaseRoutine();
            EnableCombatPhase();
            StartCoroutine(SessionTimerRoutine());
            SyncHudFromTracker();
        }

        private void PrepareLocalPlayerForPrep()
        {
            fpsController?.SetMovementLocked(false);
            fpsController?.SetGameOverMode(false);
            fpsController?.SetServerReconciliationSuspended(true);
            weaponController?.SetDuelFireBlocked(true);
            initialWeaponPickDone = false;
            manualWeaponPickOpen = false;
            CloseWeaponPickUi();
        }

        private IEnumerator RunPrepPhaseRoutine()
        {
            gameHud?.ShowModeIntroBanner(
                MainMenuGameModeUtility.GetModeIntroDescription(MainMenuGameMode.Deathmatch),
                5f);

            var deadline = Time.unscaledTime + PrepSeconds;
            while (Time.unscaledTime < deadline)
            {
                yield return null;
            }

            gameHud?.SetMatchStatusMessage(string.Empty);
        }

        private void EnableCombatPhase()
        {
            combatPhaseActive = true;
            EnableLocalPlayerCombat();
            GameplayAudioPrewarm.PrewarmCombatClips();

            if (bots.Count > 0 && bots[0].Bot != null)
            {
                DuelBotLineOfSight.PrepareScene(bots[0].Bot.gameObject.scene);
            }

            for (var i = 0; i < bots.Count; i++)
            {
                bots[i].Bot?.SetCombatEnabled(true);
            }
        }

        public void NotifyEntityDied(PlayerHealth victimHealth)
        {
            if (!sessionStarted || sessionEnded || victimHealth == null || !combatPhaseActive)
            {
                return;
            }

            var victimTicket = ResolveTicketId(victimHealth);
            var victimNick = ResolveNickname(victimHealth);
            var killerTicket = victimHealth.LastAttackerTicketId ?? string.Empty;
            var killerNick = ResolveKillerNickname(victimHealth, killerTicket);

            MatchScoreboardTracker.RecordKill(killerTicket, victimTicket, killerNick, victimNick);
            PushKillFeed(killerTicket, killerNick, victimTicket, victimNick);

            if (victimHealth.GetComponent<LocalPlayerMarker>() != null)
            {
                combatHud?.ShowDuelDeathBanner(killerNick);
                StartLocalPlayerRespawn();
            }
            else if (string.Equals(killerTicket, "offline-local", System.StringComparison.Ordinal))
            {
                combatHud?.ShowDuelKillBanner(victimNick);
                MatchAchievementReporter.ReportEvent(this, "kill_player", 1);
            }

            SyncHudFromTracker();

            if (victimHealth.GetComponent<DuelNavBotController>() is DuelNavBotController victimBot)
            {
                StartBotRespawn(victimBot);
            }
        }

        private void StartLocalPlayerRespawn()
        {
            if (localPlayerRespawnRoutine != null)
            {
                StopCoroutine(localPlayerRespawnRoutine);
            }

            localPlayerRespawnRoutine = StartCoroutine(RespawnLocalPlayerRoutine());
        }

        private void StartBotRespawn(DuelNavBotController bot)
        {
            if (bot == null)
            {
                return;
            }

            if (botRespawnRoutines.TryGetValue(bot, out var existing) && existing != null)
            {
                StopCoroutine(existing);
            }

            botRespawnRoutines[bot] = StartCoroutine(RespawnBotRoutine(bot));
        }

        private void CancelAllRespawnRoutines()
        {
            if (localPlayerRespawnRoutine != null)
            {
                StopCoroutine(localPlayerRespawnRoutine);
                localPlayerRespawnRoutine = null;
            }

            foreach (var pair in botRespawnRoutines)
            {
                if (pair.Value != null)
                {
                    StopCoroutine(pair.Value);
                }
            }

            botRespawnRoutines.Clear();
        }

        public void EnsureScoreboardParticipants()
        {
            MatchScoreboardTracker.Upsert("offline-local", PlayerProfileService.LocalDisplayNickname);
            for (var i = 0; i < bots.Count; i++)
            {
                var bot = bots[i];
                if (string.IsNullOrWhiteSpace(bot.TicketId))
                {
                    continue;
                }

                MatchScoreboardTracker.Upsert(bot.TicketId, bot.Nickname);
            }
        }

        private IEnumerator RespawnLocalPlayerRoutine()
        {
            yield return new WaitForSeconds(RespawnDelaySeconds);
            if (!sessionStarted || sessionEnded || localPlayer == null || playerHealth == null)
            {
                yield break;
            }

            if (!DmSpawnUtility.TryResolveSpawnPose(ResolveLocalRespawnSlot(), out var position, out var rotation))
            {
                position = localPlayer.transform.position;
                rotation = localPlayer.transform.rotation;
            }

            playerHealth.ForceReviveAt(position, rotation);
            fpsController?.NotifyLocalRespawned(1.5f);
            weaponController?.RestoreAfterRespawn();
            weaponController?.SetDuelFireBlocked(false);
            manualWeaponPickOpen = false;
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
            localPlayerRespawnRoutine = null;
        }

        private IEnumerator RespawnBotRoutine(DuelNavBotController bot)
        {
            if (bot == null)
            {
                yield break;
            }

            bot.SetCombatEnabled(false);
            yield return new WaitForSeconds(RespawnDelaySeconds);
            if (!sessionStarted || sessionEnded || bot == null)
            {
                yield break;
            }

            var spawnSlot = 0;
            botSpawnSlotScratch.Clear();
            CollectOccupiedSpawnSlots(botSpawnSlotScratch, excludeHealth: bot.GetComponent<PlayerHealth>());
            spawnSlot = DmSpawnUtility.RollRandomSpawnSlot(-1, botSpawnSlotScratch);
            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i].Bot == bot)
                {
                    var entry = bots[i];
                    entry.SpawnSlot = spawnSlot;
                    bots[i] = entry;
                    break;
                }
            }

            if (!DmSpawnUtility.TryResolveSpawnPose(spawnSlot, out var position, out var rotation))
            {
                position = bot.transform.position;
                rotation = bot.transform.rotation;
            }

            bot.PrepareCombatRound();
            bot.WarpTo(position, rotation);
            bot.SetFreeTargetMode(true);
            bot.GetComponent<RemoteLeftHandIkBinder>()?.SetHandIkEnabled(true);
            bot.SetCombatEnabled(true);
            botRespawnRoutines.Remove(bot);
        }

        private IEnumerator SpawnBotsRoutine()
        {
            ClearBots();

            var nicknames = PickUniqueBotNicknames(BotCount);
            var reservedSlots = new List<int>(BotCount + 1);
            CollectOccupiedSpawnSlots(reservedSlots);
            botSpawnSlotScratch.Clear();
            DmSpawnUtility.TryRollUniqueSpawnSlots(BotCount, botSpawnSlotScratch, reservedSlots);
            for (var i = 0; i < BotCount; i++)
            {
                var spawnSlot = i < botSpawnSlotScratch.Count ? botSpawnSlotScratch[i] : DmSpawnUtility.RollRandomSpawnSlot(-1, botSpawnSlotScratch);
                if (!DmSpawnUtility.TryResolveSpawnPose(spawnSlot, out var position, out var rotation))
                {
                    position = Vector3.zero;
                    rotation = Quaternion.identity;
                }

                var nickname = nicknames[i];
                var skill = Random.Range(0.35f, 0.68f);
                var ticketId = $"offline-dm-bot-{i + 1}";
                var bot = TrainingBotFactory.CreateDuelBot(position, rotation, nickname, skill, offlineDmMode: true);
                bot.WarpTo(position, rotation);
                bot.SetScoreboardTicketId(ticketId);
                bot.SetFreeTargetMode(true);
                bot.PrepareCombatRound();
                bot.RefreshVisualPresentationDeferred();
                bot.SetCombatEnabled(false);
                RegisterCombatant(bot.GetComponent<PlayerHealth>());

                bots.Add(new DmBotEntry
                {
                    Bot = bot,
                    TicketId = ticketId,
                    Nickname = nickname,
                    SpawnSlot = spawnSlot,
                });

                yield return null;
            }

            EnsureScoreboardParticipants();
        }

        private void ClearBots()
        {
            CancelAllRespawnRoutines();

            for (var i = 0; i < bots.Count; i++)
            {
                if (bots[i].Bot != null)
                {
                    var health = bots[i].Bot.GetComponent<PlayerHealth>();
                    if (health != null)
                    {
                        combatants.Remove(health);
                    }

                    Destroy(bots[i].Bot.gameObject);
                }
            }

            bots.Clear();
        }

        private IEnumerator SessionTimerRoutine()
        {
            var lastDisplayedSeconds = -1;
            while (remainingSeconds > 0f && !sessionEnded)
            {
                remainingSeconds -= Time.deltaTime;
                var displaySeconds = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
                if (displaySeconds != lastDisplayedSeconds)
                {
                    lastDisplayedSeconds = displaySeconds;
                    gameHud?.SetTrainingTimerSeconds(displaySeconds);
                }

                yield return null;
            }

            EndSession();
        }

        private void EndSession()
        {
            if (sessionEnded)
            {
                return;
            }

            sessionEnded = true;
            for (var i = 0; i < bots.Count; i++)
            {
                bots[i].Bot?.SetCombatEnabled(false);
            }

            weaponController?.SetDuelFireBlocked(true);
            gameHud?.ScheduleTrainingGameOver(localKillCount);
        }

        private void SyncHudFromTracker()
        {
            if (MatchScoreboardTracker.TryGetEntry("offline-local", out var localRow))
            {
                localKillCount = localRow.Kills;
                localDeathCount = localRow.Deaths;
            }

            RefreshHud();
        }

        private void RefreshHud()
        {
            gameHud?.SetKillCount(localKillCount);
            gameHud?.SetMatchCornerStats(localKillCount, bots.Count);
            gameHud?.SetTrainingTimerSeconds(Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds)));
        }

        private void RegisterCombatant(PlayerHealth health)
        {
            if (health == null || combatants.Contains(health))
            {
                return;
            }

            combatants.Add(health);
        }

        private static List<string> PickUniqueBotNicknames(int count)
        {
            var pool = new List<string>(BotNicknamePool);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var swapIndex = Random.Range(0, i + 1);
                (pool[i], pool[swapIndex]) = (pool[swapIndex], pool[i]);
            }

            var result = new List<string>(count);
            var pickCount = Mathf.Min(count, pool.Count);
            for (var i = 0; i < pickCount; i++)
            {
                result.Add(pool[i]);
            }

            for (var i = pickCount; i < count; i++)
            {
                result.Add($"Bot-{i + 1}");
            }

            return result;
        }

        private void EnableLocalPlayerCombat()
        {
            fpsController?.SetMovementLocked(false);
            fpsController?.SetGameOverMode(false);
            fpsController?.SetServerReconciliationSuspended(true);
            weaponController?.SetDuelFireBlocked(false);
            weaponLoadoutController?.ApplyDuelRoundWeaponPick(WeaponKind.AssaultRifle, DmWeaponSpareAmmo);
            weaponLoadoutController?.EnsureActiveWeaponEquipped(drawWeapon: true);
            weaponLoadoutController?.ConfigureTrainingInfiniteReserveAmmo(true);
            pickupController?.RefreshWeaponAvailability();
            weaponController?.RefreshWeaponAvailability();
            initialWeaponPickDone = true;
        }

        private void TryOpenManualWeaponPick()
        {
            if (manualWeaponPickOpen ||
                !combatPhaseActive ||
                !initialWeaponPickDone ||
                !IsLocalPlayerAlive() ||
                fpsController == null ||
                gameHud == null ||
                GameHudController.IsPauseMenuOpen)
            {
                return;
            }

            if (!ReadWeaponChangeKeyDown())
            {
                return;
            }

            manualWeaponPickOpen = true;
            fpsController.SetWeaponPickUiMode(true, allowMovementWhileOpen: true);
            weaponController?.SetDuelFireBlocked(true);
            gameHud.ShowDuelWeaponPickPanel(HandleManualWeaponPicked);
        }

        private void HandleManualWeaponPicked(WeaponKind kind)
        {
            if (!manualWeaponPickOpen)
            {
                return;
            }

            if (!ApplyWeaponSelection(kind))
            {
                return;
            }

            manualWeaponPickOpen = false;
            HideWeaponPickUi();
            weaponController?.SetDuelFireBlocked(false);
        }

        private bool ApplyWeaponSelection(WeaponKind kind)
        {
            if (weaponLoadoutController == null)
            {
                return false;
            }

            if (!weaponLoadoutController.ApplyDuelRoundWeaponPick(kind, DmWeaponSpareAmmo))
            {
                return false;
            }

            weaponLoadoutController.EnsureActiveWeaponEquipped(drawWeapon: true);
            localPlayer?.GetComponent<PlayerWeaponHolsterController>()?.ForceArmedState();
            pickupController?.RefreshWeaponAvailability();
            weaponController?.RefreshWeaponAvailability();
            return true;
        }

        private void HideWeaponPickUi()
        {
            fpsController?.SetWeaponPickUiMode(false);
            gameHud?.HideDuelWeaponPickPanel();
        }

        private void CloseWeaponPickUi()
        {
            manualWeaponPickOpen = false;
            HideWeaponPickUi();
        }

        private static bool ReadWeaponChangeKeyDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.B);
#endif
        }

        private bool IsLocalPlayerAlive()
        {
            return playerHealth != null && !playerHealth.IsDead;
        }

        private void PushKillFeed(
            string killerTicket,
            string killerNick,
            string victimTicket,
            string victimNick)
        {
            if (killFeed == null ||
                string.IsNullOrWhiteSpace(killerTicket) ||
                string.Equals(killerTicket, "world", System.StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(victimTicket))
            {
                return;
            }

            var killerName = FormatKillFeedName(killerTicket, killerNick);
            var victimName = FormatKillFeedName(victimTicket, victimNick);
            killFeed.PushLocalPlayerKill(killerName, victimName);
        }

        private static string FormatKillFeedName(string ticketId, string nickname)
        {
            if (string.Equals(ticketId, "offline-local", System.StringComparison.Ordinal))
            {
                var prefix = PlayerProfileService.NicknamePrefix;
                return string.IsNullOrEmpty(prefix) ? "Вы" : $"[{prefix}] Вы";
            }

            return string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
        }

        private void BindLocalPlayer()
        {
            fpsController = localPlayer.GetComponent<FpsCharacterController>();
            weaponLoadoutController = localPlayer.GetComponent<PlayerWeaponLoadoutController>();
            weaponController = localPlayer.GetComponent<PlayerWeaponController>();
            pickupController = localPlayer.GetComponent<PlayerPickupController>();
            playerHealth = localPlayer.GetComponent<PlayerHealth>();
        }

        private void TeleportLocalPlayerToUniqueSpawn()
        {
            if (localPlayer == null)
            {
                return;
            }

            var occupied = new List<int>(BotCount + 1);
            CollectOccupiedSpawnSlots(occupied);
            localPlayerSpawnSlot = DmSpawnUtility.RollRandomSpawnSlot(-1, occupied);
            if (!DmSpawnUtility.TryResolveSpawnPose(localPlayerSpawnSlot, out var position, out var rotation))
            {
                return;
            }

            var characterController = localPlayer.GetComponent<CharacterController>();
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
        }

        private int ResolveLocalRespawnSlot()
        {
            var occupied = new List<int>(BotCount + 1);
            CollectOccupiedSpawnSlots(occupied, excludeHealth: playerHealth);
            localPlayerSpawnSlot = DmSpawnUtility.RollRandomSpawnSlot(localPlayerSpawnSlot, occupied);
            return localPlayerSpawnSlot;
        }

        private void CollectOccupiedSpawnSlots(List<int> occupied, PlayerHealth excludeHealth = null)
        {
            occupied.Clear();
            if (localPlayerSpawnSlot >= 0 &&
                (excludeHealth == null || excludeHealth != playerHealth))
            {
                occupied.Add(localPlayerSpawnSlot);
            }

            for (var i = 0; i < bots.Count; i++)
            {
                var bot = bots[i].Bot;
                if (bot == null)
                {
                    continue;
                }

                var health = bot.GetComponent<PlayerHealth>();
                if (health != null && health.IsDead)
                {
                    continue;
                }

                if (excludeHealth != null && health == excludeHealth)
                {
                    continue;
                }

                occupied.Add(bots[i].SpawnSlot);
            }
        }

        private void CacheReferences()
        {
            gameHud = FindFirstObjectByType<GameHudController>();
            combatHud = FindFirstObjectByType<CombatHudController>();
            killFeed = FindFirstObjectByType<GameKillFeedController>();
            localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
        }

        private IEnumerator WaitForLocalPlayerRoutine()
        {
            for (var i = 0; i < 120; i++)
            {
                localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
                if (localPlayer != null)
                {
                    yield break;
                }

                yield return null;
            }
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

            SyncHudFromTracker();
            yield return null;
        }

        private IEnumerator EnsureNavMeshReadyRoutine()
        {
            for (var i = 0; i < 20; i++)
            {
                if (localPlayer == null)
                {
                    localPlayer = FindFirstObjectByType<LocalPlayerMarker>();
                }

                if (localPlayer == null)
                {
                    yield break;
                }

                if (NavMesh.SamplePosition(localPlayer.transform.position, out _, 4f, NavMesh.AllAreas))
                {
                    yield break;
                }

                yield return new WaitForSeconds(0.1f);
            }

            if (localPlayer != null)
            {
                Debug.LogWarning(
                    "[MatchOfflineDeathmatch] NavMesh not found near spawn. Bake NavMesh on the DM scene.");
            }
        }

        private string ResolveKillerNickname(PlayerHealth victimHealth, string killerTicket)
        {
            if (victimHealth != null && !string.IsNullOrWhiteSpace(victimHealth.LastAttackerNickname))
            {
                return victimHealth.LastAttackerNickname.Trim();
            }

            return ResolveNicknameForTicket(killerTicket);
        }

        private string ResolveNicknameForTicket(string ticketId)
        {
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                return string.Empty;
            }

            if (string.Equals(ticketId, "offline-local", System.StringComparison.Ordinal))
            {
                return PlayerProfileService.LocalDisplayNickname;
            }

            for (var i = 0; i < bots.Count; i++)
            {
                if (string.Equals(bots[i].TicketId, ticketId, System.StringComparison.Ordinal))
                {
                    return bots[i].Nickname;
                }
            }

            if (MatchScoreboardTracker.TryGetEntry(ticketId, out var entry) &&
                !string.IsNullOrWhiteSpace(entry.Nickname))
            {
                return entry.Nickname;
            }

            return string.Empty;
        }

        private static string ResolveTicketId(PlayerHealth health)
        {
            if (health.GetComponent<LocalPlayerMarker>() != null)
            {
                return "offline-local";
            }

            var bot = health.GetComponent<DuelNavBotController>();
            if (bot != null && !string.IsNullOrWhiteSpace(bot.ScoreboardTicketId))
            {
                return bot.ScoreboardTicketId;
            }

            return "offline-bot";
        }

        private static string ResolveNickname(PlayerHealth health)
        {
            if (health.GetComponent<LocalPlayerMarker>() != null)
            {
                return PlayerProfileService.LocalDisplayNickname;
            }

            var bot = health.GetComponent<DuelNavBotController>();
            return bot != null ? bot.Nickname : "Бот";
        }
    }
}
