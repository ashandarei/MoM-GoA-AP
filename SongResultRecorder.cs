using System.Text.Json;
using HarmonyLib;
using Il2CppGame;
using Il2CppGame.Scene.MusicStage;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using StageMain = Il2CppGame.Scene.MusicStage.Main;

namespace MoMController;

/// <summary>Observes native attempt boundaries; never changes gameplay or save data.</summary>
internal sealed class SongResultRecorder
{
    private static SongResultRecorder? instance;
    private readonly MelonLogger.Instance logger;
    private readonly string directory;
    private readonly string sessionId = Guid.NewGuid().ToString();
    private readonly Queue<SongAttemptRecord> pending = new();
    private readonly CompletionExit completionExit = new();
    private readonly HarmonyLib.Harmony harmony = new("ArchipelagoMine.MoM.SongResults");
    private SongAttemptRecord? attempt;
    private float nextFlush;
    private bool reportedWriteError;
    private bool armed;
    private float startSeekTime;
    private float nextObservation;

    internal SongResultRecorder(MelonLogger.Instance logger)
    {
        this.logger = logger;
        directory = Path.Combine(MelonEnvironment.UserDataDirectory, "MoMBridge");
        instance = this;
        // StartGamePlay is inlined by this shipping build. BeginMainLoop is the
        // native coroutine factory used both for first play and in-place retry.
        harmony.Patch(AccessTools.Method(typeof(MusicStageMain), "BeginMainLoop"),
            prefix: new HarmonyMethod(typeof(SongResultRecorder), nameof(OnBeginMainLoop)));
        harmony.Patch(AccessTools.Method(typeof(MusicStageGamePlay), "SetupTriggerData"),
            postfix: new HarmonyMethod(typeof(SongResultRecorder), nameof(OnPrepared)));
        // Capture before the native callback starts a retry or changes scenes,
        // while the completed attempt's score still exists.
        harmony.Patch(AccessTools.Method(typeof(StageMain), "OnFinished"),
            prefix: new HarmonyMethod(typeof(SongResultRecorder), nameof(OnFinished)));
        logger.Msg("Song result recorder ready: " + directory);
    }

    private static void OnBeginMainLoop()
    {
        if (instance == null) return;
        instance.armed = true;
        instance.startSeekTime = 0;
    }

    private static void OnPrepared(float seekTime, bool __result)
    {
        if (instance != null && __result) instance.startSeekTime = seekTime;
    }

    internal void Update()
    {
        // Run on the next Unity frame, outside both the native finish callback
        // and Flush (which is also called by OnApplicationQuit).
        try
        {
            completionExit.Dispatch(eventId => {
                logger.Msg("Completion event published; closing fast session: " + eventId);
                Il2CppGumi.Device.ApplicationQuitManager.Quit(false, false);
            });
        }
        catch (Exception error) { logger.Error("Automatic song exit failed; close the game manually: " + error); }
        if (Time.realtimeSinceStartup >= nextObservation)
        {
            nextObservation = Time.realtimeSinceStartup + 0.1f;
            try
            {
                if (armed && MusicStagePlayData.haveInstance && MusicStagePlayData.state == State.Playing)
                {
                    StartAttempt(startSeekTime);
                    armed = false;
                }
            }
            catch (Exception error) { logger.Error("Could not start song result tracking: " + error); armed = false; }
        }
        Flush();
    }

    private static void OnFinished(MusicStageMain.ExitType exitType)
    {
        try
        {
            if (instance == null) return;
            instance.armed = false;
            instance.logger.Msg("Native song finish: " + exitType);
            instance.FinishAttempt(exitType);
        }
        catch (Exception error) { instance?.logger.Error("Could not capture song result: " + error); }
    }

    private void StartAttempt(float seekTime)
    {
        var setting = MusicStagePlayData.playSetting;
        if (setting?.musicID == null) return;
        if (attempt != null)
        {
            // A new native start cannot turn an unfinished prior attempt into
            // a clear. Preserve it as interrupted if its finish was skipped.
            pending.Enqueue(attempt with { Outcome = "aborted", NativeExit = "Interrupted",
                FinishedAt = DateTime.UtcNow });
        }
        var songId = setting.musicID.valueNumber;
        var difficulty = (int)setting.msDifficulty;
        var context = SongLaunchService.GetResultContext(songId, difficulty);
        attempt = new SongAttemptRecord {
            SessionId = sessionId, EventId = Guid.NewGuid().ToString(),
            RequestId = context.RequestId, SongId = songId, SongName = setting.musicID.GetName(),
            Difficulty = difficulty, DifficultyName = setting.msDifficulty.ToString(),
            SceneId = GameSceneManager.instance.gameSceneID.valueNumber,
            StartedAt = DateTime.UtcNow, Outcome = "playing",
            FastBoot = context.FastBoot, SavesDisabled = context.SavesDisabled,
            PlayMode = setting.simplePlay ? "OneButton" : setting.performerPlay ? "Performer" : "Normal",
            InputDevice = setting.msInputDeviceType.ToString(), DemoPlay = setting.demoPlay,
            AutoPlay = HasAutoPlay(), Assisted = setting.invalidDamage || !setting.enableGameOver,
            StartSeekTime = seekTime
        };
        pending.Enqueue(attempt);
        logger.Msg($"Song attempt started: {attempt.EventId}; {attempt.SongName}; request={attempt.RequestId ?? "none"}.");
    }

    private static bool HasAutoPlay()
    {
        var values = MusicStagePlayData.autoPlay;
        if (values == null) return false;
        for (var index = 0; index < values.Length; index++) if (values[index]) return true;
        return false;
    }

    private void FinishAttempt(MusicStageMain.ExitType exitType)
    {
        if (attempt == null) return;
        var setting = MusicStagePlayData.playSetting;
        var scores = MusicStagePlayData.playScores;
        var score = scores != null && scores.Length > 0 ? scores[0] : null;
        var identityMatches = setting != null && setting.musicID?.valueNumber == attempt.SongId &&
            (int)setting.msDifficulty == attempt.Difficulty;
        var outcome = !identityMatches ? "aborted" : exitType switch {
            MusicStageMain.ExitType.Clear => "cleared",
            MusicStageMain.ExitType.GameOver => "failed",
            _ => "aborted"
        };
        var result = attempt with {
            Outcome = outcome, NativeExit = exitType.ToString(), FinishedAt = DateTime.UtcNow,
            MusicTime = MusicStagePlayData.currentMusicTime,
            MusicLength = MusicStagePlayData.currentMusicLength,
            Score = score?.score, MaxCombo = score?.maxComboCount,
            Rank = outcome == "cleared" ? score?.msPlayRank.ToString() : null,
            ClearMark = outcome == "cleared" ? score?.msPlayResult.ToString() : null,
            AutoPlay = attempt.AutoPlay || HasAutoPlay(),
            DemoPlay = attempt.DemoPlay || setting?.demoPlay == true,
            Assisted = attempt.Assisted || setting?.invalidDamage == true || setting?.enableGameOver == false,
        };
        pending.Enqueue(result);
        attempt = null;
        logger.Msg($"Song attempt {outcome}: {result.EventId}; {result.SongName}; score={result.Score}; eligible={result.EligibleForChallenge}.");
    }

    internal void Flush(bool force = false)
    {
        if (!force && Time.realtimeSinceStartup < nextFlush) return;
        nextFlush = Time.realtimeSinceStartup + 1;
        while (pending.TryPeek(out var record))
        {
            try
            {
                var context = SongLaunchService.GetResultContext(record.SongId, record.Difficulty);
                var autoClose = record.EligibleForChallenge && record.FastBoot && record.SavesDisabled &&
                    context.FastBoot && context.SavesDisabled && context.RequestId == record.RequestId;
                completionExit.Publish(record.EventId, autoClose, () => {
                    var json = JsonSerializer.Serialize(record, new JsonSerializerOptions {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
                    if (record.Outcome != "playing")
                    {
                        // Commit the durable event before updating convenience
                        // snapshots. A client can catch up after being closed.
                        WriteAtomic(Path.Combine(directory, "results", record.EventId + ".json"), json);
                        WriteAtomic(Path.Combine(directory, "latest-result.json"), json);
                    }
                    WriteAtomic(Path.Combine(directory, "current-attempt.json"), json);
                });
                pending.Dequeue();
                reportedWriteError = false;
            }
            catch (Exception error)
            {
                if (!reportedWriteError) logger.Error("Song result write failed; will retry: " + error.Message);
                reportedWriteError = true;
                break;
            }
        }
    }

    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
    }
}

internal sealed record SongAttemptRecord
{
    public int SchemaVersion { get; init; } = 1;
    public string SessionId { get; init; } = "";
    public string EventId { get; init; } = "";
    public string? RequestId { get; init; }
    public uint SongId { get; init; }
    public string SongName { get; init; } = "";
    public int Difficulty { get; init; }
    public string DifficultyName { get; init; } = "";
    public uint SceneId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string Outcome { get; init; } = "";
    public string? NativeExit { get; init; }
    public int? Score { get; init; }
    public int? MaxCombo { get; init; }
    public string? Rank { get; init; }
    public string? ClearMark { get; init; }
    public float? MusicTime { get; init; }
    public float? MusicLength { get; init; }
    public float StartSeekTime { get; init; }
    public string PlayMode { get; init; } = "";
    public string InputDevice { get; init; } = "";
    public bool DemoPlay { get; init; }
    public bool AutoPlay { get; init; }
    public bool Assisted { get; init; }
    public bool FastBoot { get; init; }
    public bool SavesDisabled { get; init; }
    public bool Cleared => Outcome == "cleared";
    public bool EligibleForChallenge => Cleared && RequestId != null &&
        PlayMode is ("Normal" or "OneButton" or "Performer") &&
        !DemoPlay && !AutoPlay && !Assisted && StartSeekTime == 0 &&
        SceneId == Define.GameScene.MusicSelectMusicStage.valueNumber;
}
