using System.Text.Json;
using HarmonyLib;
using Il2CppGame;
using Il2CppGame.Scene.MusicSelect;
using Il2CppGame.Scene.MusicStage;
using Il2CppGumi.Device;
using Game = Il2CppGame;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using PlaySettingReaction = Il2CppCommon.MSPlaySetting.MSPlaySettingMainBase;

namespace MoMController;

/// <summary>Consumes one cold-start request through direct loading or the normal menus.</summary>
internal sealed partial class SongLaunchService
{
    private static SongLaunchService? activeRequest;
    internal sealed class Request
    {
        public string RequestId { get; set; } = "";
        public uint? SongId { get; set; }
        public int Difficulty { get; set; }
        public bool FastBoot { get; set; }
        public string PlayStyle { get; set; } = "Normal";
        public string StartingItem { get; set; } = "None";
        public string Party { get; set; } = "Classic";
    }

    private readonly Request request;
    private readonly MelonLogger.Instance logger;
    private readonly string resultPath;
    private readonly float started;
    private readonly bool originalRunInBackground;
    private readonly Application.LogCallback unityLog;
    private readonly HashSet<string> unityErrors = new();
    private readonly HarmonyLib.Harmony harmony = new("ArchipelagoMine.MoM.StartupSong");
    private float nextPoll;
    private float lastAction;
    private string previousState = "";
    private string step = "waiting_for_startup";
    private uint? selectedSong;
    private string? selectedName;
    private object? verifiedPlayback;
    private bool submittedTitle;
    private bool submittedMenu;
    private bool submittedSong;
    private bool done;

    internal static (string? RequestId, bool FastBoot, bool SavesDisabled) GetResultContext(uint songId, int difficulty)
    {
        var service = activeRequest;
        if (service == null || !service.submittedSong || service.selectedSong != songId ||
            service.request.Difficulty != difficulty) return (null, false, false);
        return (service.request.RequestId, service.request.FastBoot, service.temporaryProfile);
    }

    private SongLaunchService(Request request, MelonLogger.Instance logger)
    {
        this.request = request;
        this.logger = logger;
        started = Time.realtimeSinceStartup;
        originalRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        resultPath = Path.Combine(MelonEnvironment.UserDataDirectory, "MoMSongLaunch-result.json");
        unityLog = (Action<string, string, LogType>)OnUnityLog;
        Application.add_logMessageReceived(unityLog);
        activeRequest = this;
        if (request.FastBoot) InstallFastBoot();
        harmony.Patch(AccessTools.Method(typeof(Game.Scene.MusicSelect.MainBase), "InitializeSetting"),
            prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(PrepareInitialSong)));
        foreach (var method in new[] { "NoMaskListUpdate", "SetNoMaskListItem", "LateUpdate" })
            harmony.Patch(AccessTools.Method(typeof(MusicList), method),
                prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(CanUpdateSongPreview)));
        Report("pending", "Accepted startup song request.");
    }

    public static SongLaunchService? FromPendingRequest(MelonLogger.Instance logger)
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, "MoMSongLaunch-request.json");
        if (!File.Exists(path)) return null;
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty request.");
            if (!Guid.TryParse(request.RequestId, out _) || request.Difficulty is < 0 or > 2)
                throw new InvalidDataException("RequestId must be a GUID and Difficulty must be 0, 1 or 2.");
            if (request.PlayStyle is not ("Normal" or "OneButton" or "Performer") ||
                request.StartingItem is not ("None" or "Potion" or "MegaPotion" or "SummoningStar") ||
                request.Party is not ("Classic" or "Days" or "3D" or "BBS"))
                throw new InvalidDataException("Unknown play style, starting item or party.");
            if (!request.FastBoot && (request.StartingItem != "None" || request.Party != "Classic"))
                throw new InvalidDataException("Starting item and party overrides require a temporary FastBoot session.");
            // Claim it before doing anything in the game; later launches must
            // not repeat the request after a crash or normal game restart.
            File.Move(path, Path.Combine(MelonEnvironment.UserDataDirectory,
                "MoMSongLaunch-active-" + request.RequestId + ".json"));
            return new SongLaunchService(request, logger);
        }
        catch (Exception error)
        {
            logger.Error("Could not accept song request: " + error);
            return null;
        }
    }

    public void Update()
    {
        if (done || Time.realtimeSinceStartup < nextPoll) return;
        nextPoll = Time.realtimeSinceStartup + 0.25f;
        try
        {
            if (Time.realtimeSinceStartup - started > 180)
            {
                Finish("failed", "Timed out while " + step + ".");
                return;
            }
            if (!GameSceneManager.haveInstance || !GameSceneFlowManager.haveInstance) return;
            var manager = GameSceneManager.instance;
            var flow = GameSceneFlowManager.instance;
            var sceneId = manager.gameSceneID;
            if (sceneId == null) return;
            var main = manager.GetGameSceneMain(sceneId);
            var state = $"scene={sceneId.valueNumber} main={main?.GetIl2CppType().FullName} idle={flow.isIdle} flow={flow._state} manager={manager._state} next={flow._nextGameSceneLoadData?.gameSceneID?.valueNumber} system={manager.systemScene} initialized={main?.IsInitialized()}";
            if (main?.TryCast<Game.Scene.Title.Main>() is { } titleMain)
            {
                var logoState = titleMain._logoSceneParts;
                state += $" title={logoState?._state} submit={logoState?._currentSubmitButtonType} latentEnabled={logoState?._latentPressAnyButton?.IsUIEnable()} startEnabled={logoState?._startGameMenuFocusUI?._focusUIObject?.IsUIEnable()}";
            }
            if (main?.TryCast<Game.Scene.MainMenu.Main>() is { } menuMain)
                state += $" menu={menuMain._state}";
            if (state != previousState)
            {
                previousState = state;
                logger.Msg("Song launch: " + state);
                Report("pending", state);
            }

            if (sceneId.value == Define.GameScene.MusicSelectMusicStage.value && submittedSong)
            {
                var setting = MusicStagePlayData.playSetting;
                if (MusicStagePlayData.state == State.Playing && MusicStagePlayData.currentMusicTime > 1 &&
                    setting.musicID.valueNumber == selectedSong && (int)setting.msDifficulty == request.Difficulty &&
                    !setting.demoPlay && setting.simplePlay == (request.PlayStyle == "OneButton") &&
                    setting.performerPlay == (request.PlayStyle == "Performer") &&
                    (!request.FastBoot || (VerifyStartingItem(setting) && VerifyParty(setting))) &&
                    setting.msInputDeviceType == InputManager.PlayingInputDeviceType.Gamepad)
                {
                    verifiedPlayback = new { state = MusicStagePlayData.state.ToString(),
                        musicTime = MusicStagePlayData.currentMusicTime, setting.demoPlay,
                        setting.simplePlay, setting.performerPlay, kingEnabled = setting.IsEnableKing(),
                        items = DescribeItems(setting), party = DescribeParty(setting),
                        inputDevice = setting.msInputDeviceType.ToString() };
                    var difficultyName = new[] { "Beginner", "Standard", "Proud" }[request.Difficulty];
                    Finish("ready", $"Requested song is playing on {difficultyName} in {request.PlayStyle} style with {request.StartingItem}.");
                }
                return;
            }
            if (!flow.isIdle || main == null || manager.systemScene || !main.IsInitialized()) return;
            if (Time.realtimeSinceStartup - lastAction < 1) return;

            if (submittedMenu && !submittedSong && main.TryCast<Il2CppCommon.Window.HelpMain>() is { } help)
            {
                if (!help.IsOpenEnd()) return;
                var buttons = help._helpSceneParts._latentButtons;
                var button = buttons[0].IsUIEnable() ? buttons[0] : buttons[1];
                if (!button.IsUIEnable()) return;
                lastAction = Time.realtimeSinceStartup;
                logger.Msg("Song launch: advancing the Track Selection help window.");
                button.OnSubmit();
                return;
            }

            if (sceneId.value == Define.GameScene.Title.value && !submittedTitle)
            {
                var title = main.TryCast<Game.Scene.Title.Main>();
                var logo = title?._logoSceneParts;
                if (logo == null || logo._state != Game.Scene.Title.Logo.State.Wait ||
                    logo._currentSubmitButtonType != Game.Scene.Title.Logo.SubmitButtonType.PlayStartEffect) return;
                var button = logo._startGameMenuFocusUI?._focusUIObject;
                if (button == null || !button.IsUIEnable()) return;
                submittedTitle = true;
                step = "loading_profile";
                lastAction = Time.realtimeSinceStartup;
                logger.Msg("Song launch: submitting the normal title Start action.");
                button.OnSubmit();
            }
            else if (sceneId.value == Define.GameScene.MainMenu.value && !submittedMenu)
            {
                var menu = main.TryCast<Game.Scene.MainMenu.Main>();
                if (menu == null || menu._state != Game.Scene.MainMenu.Main.State.Main) return;
                // The game's MainMenu reaction table assigns MusicSelect to 1.
                const int trackSelectionReaction = 1;
                var nextScene = Il2CppTable.GameSceneFlowTable.GetNextGameSceneID(sceneId, trackSelectionReaction);
                if (nextScene == null || nextScene.value != Define.GameScene.MusicSelect.value)
                {
                    Finish("failed", $"Main-menu Track Selection edge resolved to {nextScene?.valueNumber}, expected {Define.GameScene.MusicSelect.valueNumber}.");
                    return;
                }
                var songs = Il2CppTable.MusicTable.GetMusicSelectMusicIDList();
                MusicID? initialSong = null;
                MusicID? fallbackSong = null;
                for (var index = 0; index < songs.Length; index++)
                {
                    var id = songs[index];
                    if (request.SongId.HasValue && id.valueNumber != request.SongId.Value) continue;
                    var entry = new ListItemMS(id, (Define.MSDifficulty)request.Difficulty);
                    fallbackSong ??= id;
                    if (entry.playable) { initialSong = id; break; }
                }
                initialSong ??= fallbackSong;
                if (initialSong == null) { Finish("failed", "The requested song is not in the installed Track Selection catalog."); return; }
                selectedSong = initialSong.valueNumber;
                selectedName = initialSong.GetName();
                var selectionInfo = Game.Backup.Backup.musicSelectInfo;
                logger.Msg($"Song launch: preparing selection {selectedName} ({selectedSong}); previous selection {selectionInfo.selectedMusicIDValue}.");
                selectionInfo.selectedMusicID = initialSong;
                selectionInfo.selectedDifficulty = (Define.MSDifficulty)request.Difficulty;
                selectionInfo.playMode = RequestedPlayMode;
                submittedMenu = true;
                step = "opening_track_selection";
                lastAction = Time.realtimeSinceStartup;
                // Enter through the existing scene-flow edge. The menu button
                // can be locked on early profiles; no saved unlock flags change.
                logger.Msg("Song launch: entering Track Selection through the normal scene flow.");
                menu._decideScene = trackSelectionReaction;
                menu.OnFadeOutComplete();
            }
            else if (sceneId.value == Define.GameScene.MusicSelect.value && !submittedSong)
            {
                var selection = main.TryCast<Game.Scene.MusicSelect.Main>();
                if (selection?._musicListSceneMain == null) return;
                if (!selection._musicListSceneMain.IsInitialized()) return;
                ListItemMS? chosen = null;
                var items = selection._musicListSceneMain.listItems;
                for (var index = 0; index < items.Length; index++)
                {
                    var item = items[index].TryCast<ListItemMS>();
                    if (item == null || item.musicID.valueNumber != selectedSong) continue;
                    chosen = item;
                    break;
                }
                if (chosen == null) { Finish("failed", "The requested song is not present in Track Selection."); return; }
                selectedSong = chosen.musicID.valueNumber;
                selectedName = chosen.musicID.GetName();
                selection.SelectMusic(chosen.musicID, (Define.MSDifficulty)request.Difficulty, ListItemViewMS.PlayableType.Playable);
                selection.SetMSPlayMode(RequestedPlayMode);
                selectedSong = selection._musicID.valueNumber;
                submittedSong = true;
                step = "loading_song";
                lastAction = Time.realtimeSinceStartup;
                logger.Msg($"Song launch: starting {selectedName} ({selectedSong}), difficulty {request.Difficulty}.");
                selection.OnMSPlaySettingSubmitAction(new PlaySettingReaction.ReactionData {
                    playingInputDeviceType = InputManager.PlayingInputDeviceType.Gamepad
                });
                Report("pending", "Song submitted to the normal loading transition.");
            }
        }
        catch (Exception error)
        {
            logger.Error("Song launch failed: " + error);
            Finish("failed", error.ToString());
        }
    }

    private void Finish(string status, string message)
    {
        done = true;
        Application.runInBackground = originalRunInBackground;
        // Keep error reporting for the temporary session, including returning
        // from the song. The generated remove wrapper cannot Cast a null last
        // delegate, so ordinary sessions assign the native result safely.
        if (!temporaryProfile)
        {
            var remaining = Il2CppSystem.Delegate.Remove(Application.s_LogCallbackHandler, unityLog);
            Application.s_LogCallbackHandler = remaining == null ? null : remaining.Cast<Application.LogCallback>();
        }
        // Keep the early-profile menu fixes for this session so returning from
        // a failed/finished song also has a valid selection. Startup stays done.
        if (status != "ready" && !temporaryProfile)
        {
            activeRequest = null;
            harmony.UnpatchSelf();
        }
        step = status;
        Report(status, message);
        logger.Msg($"Song launch {status}: {message}");
    }

    private void OnUnityLog(string message, string trace, LogType type)
    {
        if (type is not (LogType.Error or LogType.Exception or LogType.Assert) ||
            unityErrors.Count >= 10 || !unityErrors.Add(message + trace)) return;
        logger.Error($"Unity during song launch: {type}: {message}\n{trace}");
    }

    private static void PrepareInitialSong(Game.Scene.MusicSelect.MainBase __instance)
    {
        var service = activeRequest;
        if (service == null || !service.submittedMenu || !service.selectedSong.HasValue) return;
        // Early profiles have no selectable unlocked row to fire the usual
        // focus callback. Seed the scene's selection before it draws records.
        __instance._musicID = Game.Backup.Backup.musicSelectInfo.selectedMusicID;
        __instance._msDifficulty = (Define.MSDifficulty)service.request.Difficulty;
        __instance._msPlayMode = service.RequestedPlayMode;
        __instance._playableType = ListItemViewMS.PlayableType.Playable;
        var items = __instance._musicListSceneMain.listItems;
        for (var index = 0; index < items.Length; index++)
        {
            var entry = items[index].TryCast<ListItemMS>();
            if (entry == null || entry.musicID.valueNumber != service.selectedSong.Value) continue;
            // Enable this transient UI row so normal focus/preview setup can
            // run even on an early profile. This does not unlock its save record.
            entry.defaultPlayableType = ListItemViewMS.PlayableType.Playable;
            entry.SetPlayEnable(ListItemViewMS.PlayableType.Playable);
        }
        __instance._musicListSceneMain.SelectMusic(__instance._musicID, __instance._msDifficulty);
        service.logger.Msg("Song launch: seeded the initial MusicSelect scene selection.");
    }

    private static bool CanUpdateSongPreview(MusicList __instance)
    {
        if (activeRequest == null) return true;
        // The preview assumes the selected row has a rendered view, which is
        // not true during direct entry on a profile with no unlocked tracks.
        var item = __instance._list?.selectedItem?.TryCast<ListItemMS>();
        return item != null && item.listItemView.Length > 0 &&
            item.GetListItemMSRectTransform() != null && __instance._noMaskListView != null;
    }

    private void Report(string status, string message)
    {
        var data = new { request.RequestId, status, step, message, songId = selectedSong, requestedSongId = request.SongId,
            songName = selectedName, difficulty = request.Difficulty, utc = DateTime.UtcNow,
            elapsedSeconds = Time.realtimeSinceStartup - started, fastBoot = request.FastBoot,
            playStyle = request.PlayStyle, startingItem = request.StartingItem, party = request.Party,
            savesDisabled = temporaryProfile, playback = verifiedPlayback };
        File.WriteAllText(resultPath + ".tmp", JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(resultPath + ".tmp", resultPath, true);
    }
}
