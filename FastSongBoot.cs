using HarmonyLib;
using Il2CppGame;
using Il2CppGame.Scene.MusicStage;
using Il2CppGumi.Device;
using Game = Il2CppGame;

namespace MoMController;

internal sealed partial class SongLaunchService
{
    private bool temporaryProfile;
    private int suppressedSaves;
    private bool completingTemporaryQuit;

    private void InstallFastBoot()
    {
        // These patches remain installed for the entire temporary session,
        // including after a launch error or a return to the game's menus.
        foreach (var name in new[] { "SaveAsync", "SaveInter", "SaveSystemBackupAsync",
            "SaveSystemBackupInter", "ReserveSave", "ReserveSystemSave" })
            harmony.Patch(AccessTools.Method(typeof(Game.Backup.Backup), name),
                prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(AllowProfileSave)));
        foreach (var name in new[] { "EnableSave", "ReserveSave", "ReserveSystemSave" })
            harmony.Patch(AccessTools.Method(typeof(Game.Backup.AutoBackup), name),
                prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(AllowProfileSave)));
        // AutoBackup checks the file system before it checks enableSave. A
        // temporary profile deliberately has no file system to poll.
        harmony.Patch(AccessTools.Method(typeof(Game.Backup.AutoBackup), "LateUpdate"),
            prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(AllowAutoBackupUpdate)));
        // Confirmed quitting polls both backup file systems. Fast boot never
        // creates them, and the native getters dereference those null objects.
        // No task can be active while all writes are suppressed in this mode.
        foreach (var name in new[] { "saving", "savingSystemBackup" })
            harmony.Patch(AccessTools.PropertyGetter(typeof(Game.Backup.Backup), name),
                prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(ReadTemporarySaveActivity)));
        harmony.Patch(AccessTools.Method(typeof(ApplicationQuitManager), "OnApplicationQuit"),
            prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(SkipTemporaryQuitDialog)));
        harmony.Patch(AccessTools.Method(typeof(ApplicationQuitManager), "ExecuteCompleteProcess"),
            prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(BeginTemporaryQuitCleanup)),
            finalizer: new HarmonyMethod(typeof(SongLaunchService), nameof(EndTemporaryQuitCleanup)));
        harmony.Patch(AccessTools.Method(typeof(Il2CppGumi.Sound.SoundManager), "Release"),
            prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(ReleaseTemporaryQuitAudio)));
        harmony.Patch(AccessTools.Method(typeof(GameSceneFlowManager), "GetFirstGameSceneLoadData"),
            prefix: new HarmonyMethod(typeof(SongLaunchService), nameof(ChooseFirstSongScene)));
    }

    private static bool AllowAutoBackupUpdate() => activeRequest?.temporaryProfile != true;

    private static void SkipTemporaryQuitDialog()
    {
        if (activeRequest?.temporaryProfile == true)
        {
            WindowFocusReturn.AllowKh3(activeRequest.logger);
            ApplicationQuitManager._displayMessageBox = false;
        }
    }

    private static void BeginTemporaryQuitCleanup()
    {
        if (activeRequest?.temporaryProfile == true)
            activeRequest.completingTemporaryQuit = true;
    }

    private static void EndTemporaryQuitCleanup()
    {
        if (activeRequest != null) activeRequest.completingTemporaryQuit = false;
    }

    private static bool ReleaseTemporaryQuitAudio()
    {
        var service = activeRequest;
        if (service?.temporaryProfile != true || !service.completingTemporaryQuit) return true;
        try
        {
            // Only during final process shutdown: finalize the audio backend
            // once, stopping its workers/device. Avoid serial synchronous bank
            // destruction (~4.5s); the exiting process reclaims bank storage.
            // Scene changes and normal saved sessions retain full bank cleanup.
            Il2CppGumi.Sound.SoundManager.instance.ReleaseSeadInitializer();
            service.logger.Msg("Fast session exit: audio engine finalized; skipped per-bank unloading.");
            return false;
        }
        catch (Exception error)
        {
            service.logger.Warning("Fast audio shutdown failed; using normal cleanup: " + error.Message);
            return true;
        }
    }

    private static bool ReadTemporarySaveActivity(ref bool __result)
    {
        if (activeRequest?.temporaryProfile != true) return true;
        __result = false;
        return false;
    }

    private static bool AllowProfileSave()
    {
        var service = activeRequest;
        if (service == null || !service.temporaryProfile) return true;
        if (service.suppressedSaves++ < 5)
            service.logger.Msg("Fast song boot: suppressed a save request for the temporary profile.");
        return false;
    }

    private static bool ChooseFirstSongScene(GameSceneFlowManager __instance,
        ref GameSceneFlowManager.LoadData __result)
    {
        var service = activeRequest;
        if (service == null || !service.request.FastBoot || service.done) return true;
        try
        {
            var songs = Il2CppTable.MusicTable.GetMusicSelectMusicIDList();
            MusicID? song = null;
            for (var index = 0; index < songs.Length; index++)
                if (!service.request.SongId.HasValue || songs[index].valueNumber == service.request.SongId.Value)
                { song = songs[index]; break; }
            if (song == null) throw new InvalidDataException("The requested song is not in the installed Track Selection catalog.");

            service.temporaryProfile = true;
            Game.Backup.AutoBackup.DisableSave();
            Game.Backup.Backup.doNotSaveFlag = true;
            // Create the same in-memory defaults used for a new profile, without
            // opening a save file or asking the title scene to load one.
            Game.Backup.Backup._backup = new Game.Backup.BackupData();
            Game.Backup.Backup.InitializeData();
            if (Game.Backup.Backup._systemBackupData == null)
            {
                Game.Backup.Backup._systemBackupData = new Game.Backup.SystemBackupData();
                Game.Backup.Backup.InitializeSystemBackupData();
            }

            var difficulty = (Define.MSDifficulty)service.request.Difficulty;
            var selection = Game.Backup.Backup.musicSelectInfo;
            selection.selectedMusicID = song;
            selection.selectedDifficulty = difficulty;
            selection.playMode = service.RequestedPlayMode;
            var party = service.CreateParty();
            var setting = MusicStagePlayData.playSetting;
            setting.SetPlaySetting(song, difficulty, false,
                service.request.PlayStyle == "OneButton", service.request.PlayStyle == "Performer", false,
                party, service.CreateStartingItems(), InputManager.PlayingInputDeviceType.Gamepad);
            Game.PlayData.BeforePlayData.SetMSData(song, difficulty, service.RequestedPlayMode);
            Game.Logic.MSSetting.SetControlType(service.RequestedPlayMode, song);
            Game.Logic.MSSetting.SetKeyAssignType(song, setting.memoryDiveType);
            __instance._flowData.gameMode = Define.GameMode.MusicSelect;
            __result = __instance.GetGameSceneLoadData(Define.GameScene.Loading);
            service.selectedSong = song.valueNumber;
            service.selectedName = song.GetName();
            service.submittedMenu = true;
            service.submittedSong = true;
            service.step = "loading_song_directly";
            service.logger.Msg($"Fast song boot: first scene Loading, song {service.selectedName} ({service.selectedSong}); temporary profile, saves disabled.");
            service.Report("pending", "Loading the requested song directly with a temporary profile.");
            return false;
        }
        catch (Exception error)
        {
            service.logger.Error("Fast song boot failed: " + error);
            service.Finish("failed", error.ToString());
            return true;
        }
    }
}
