using System.Text.Json;
using Il2CppGame;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace MoMController;

internal static class SongCatalogExporter
{
    private static bool complete;
    private static float nextTry;

    internal static void Update(MelonLogger.Instance logger)
    {
        if (complete || Time.realtimeSinceStartup < nextTry || !GameSceneManager.haveInstance) return;
        nextTry = Time.realtimeSinceStartup + 5;
        if (GameSceneManager.instance.gameSceneID == null ||
            GameSceneManager.instance.GetGameSceneMain(GameSceneManager.instance.gameSceneID) == null) return;
        try
        {
            var ids = Il2CppTable.MusicTable.GetMusicSelectMusicIDList();
            if (ids == null || ids.Length == 0) return;
            var songs = new List<object>();
            foreach (var id in ids)
            {
                var data = Il2CppTable.MusicTable.GetMusicData(id);
                songs.Add(new { songId = id.valueNumber, name = id.GetName(),
                    stageType = data.msType.ToString(), series = data.seriesID.GetName(),
                    lengthSeconds = data.musicLength });
            }
            var directory = Path.Combine(MelonEnvironment.UserDataDirectory, "MoMBridge");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "song-catalog.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { schemaVersion = 1, songs },
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path, true);
            var items = new List<object>();
            foreach (var id in Il2CppTable.MSItemTable.GetMSItemIDList())
            {
                var item = new Il2CppGame.Scene.MusicStage.MusicStagePlaySetting.ItemInfo(id);
                items.Add(new { itemId = id.valueNumber, name = id.GetName(),
                    timing = item.useTiming.ToString(), item.hpBorder, item.hpRecovery,
                    item.dropUpRate, item.expUpRate });
            }
            File.WriteAllText(Path.Combine(directory, "item-catalog.json"),
                JsonSerializer.Serialize(new { schemaVersion = 1, items },
                    new JsonSerializerOptions { WriteIndented = true }));
            var parties = new List<object>();
            foreach (var id in Il2CppTable.PartyTable.GetPartyIDListUserSelectable())
            {
                var data = Il2CppTable.PartyTable.GetPartyData(id);
                var characters = new List<object>();
                foreach (var character in data.characterIDs)
                    characters.Add(new { characterId = character.valueNumber, name = character.GetName() });
                parties.Add(new { partyId = id.valueNumber, name = id.GetName(), data.sortOrder, characters });
            }
            File.WriteAllText(Path.Combine(directory, "party-catalog.json"),
                JsonSerializer.Serialize(new { schemaVersion = 1, parties },
                    new JsonSerializerOptions { WriteIndented = true }));
            complete = true;
            logger.Msg($"Exported {songs.Count} installed Track Selection songs to {path}.");
        }
        catch (Exception error) { logger.Warning("Song catalog export will retry: " + error.Message); }
    }
}
