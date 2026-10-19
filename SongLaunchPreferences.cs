using Il2CppGame;
using Il2CppGame.Scene.MusicStage;

namespace MoMController;

internal sealed partial class SongLaunchService
{
    private PartyID? selectedParty;

    private MusicStagePlaySetting.PartyInfo CreateParty()
    {
        // Verified against the EGS PartyTable (party-catalog.json). Native IDs
        // differ from menu order: Team 3D is 1, while Team Days is 2.
        uint wanted = request.Party switch {
            "Classic" => 0u, "Days" => 2u, "3D" => 1u, "BBS" => 3u,
            _ => throw new InvalidDataException("Unknown party.")
        };
        foreach (var id in Il2CppTable.PartyTable.GetPartyIDListUserSelectable())
            if (id.valueNumber == wanted) { selectedParty = id; break; }
        if (selectedParty == null)
            throw new InvalidDataException("Party is absent from the installed playable party catalog.");
        return new MusicStagePlaySetting.PartyInfo(selectedParty,
            MusicStagePlaySetting.PartyInfo.FriendChangeType.None,
            request.StartingItem == "SummoningStar", false);
    }

    private bool VerifyParty(MusicStagePlaySetting setting)
    {
        if (selectedParty == null) return false;
        var data = Il2CppTable.PartyTable.GetPartyData(selectedParty);
        var expected = data.characterIDs;
        var actual = setting.partyCharacterIDs;
        var withKing = request.StartingItem == "SummoningStar";
        var matches = actual != null && actual.Count == expected.Length + (withKing ? 1 : 0);
        for (var index = 0; matches && index < expected.Length; index++)
            matches = actual![index].valueNumber == expected[index].valueNumber;
        if (matches && withKing)
            matches = actual![expected.Length].valueNumber == data.kingID.valueNumber;
        if (!matches)
            throw new InvalidDataException("Unexpected party roster: " +
                System.Text.Json.JsonSerializer.Serialize(DescribeParty(setting)));
        return true;
    }

    private static object[] DescribeParty(MusicStagePlaySetting setting)
    {
        var characters = new List<object>();
        if (setting.partyCharacterIDs != null)
            foreach (var character in setting.partyCharacterIDs)
                characters.Add(new { characterId = character.valueNumber, name = character.GetName() });
        return characters.ToArray();
    }

    private Define.MSPlayMode RequestedPlayMode => request.PlayStyle switch {
        "OneButton" => Define.MSPlayMode.Simple,
        "Performer" => Define.MSPlayMode.Performer,
        _ => Define.MSPlayMode.Normal
    };

    // Verified against the installed EGS MSItemTable (item-catalog.json).
    // Native IDs are independent of the game's display language.
    private uint? RequestedItemId => request.StartingItem switch {
        "Potion" => 0u, "MegaPotion" => 1u, "SummoningStar" => 4u, _ => null
    };

    private Il2CppSystem.Collections.Generic.List<MSItemID> CreateStartingItems()
    {
        var items = new Il2CppSystem.Collections.Generic.List<MSItemID>();
        if (RequestedItemId is uint wanted)
        {
            MSItemID? selected = null;
            foreach (var id in Il2CppTable.MSItemTable.GetMSItemIDList())
                if (id.valueNumber == wanted) { selected = id; break; }
            if (selected == null) throw new InvalidDataException("Starting item is absent from the installed item catalog.");
            items.Add(selected);
        }
        return items;
    }

    private bool VerifyStartingItem(MusicStagePlaySetting setting)
    {
        var items = setting.itemInfoList;
        if (setting.IsEnableKing() != (request.StartingItem == "SummoningStar")) return false;
        if (RequestedItemId is not uint wanted) return items == null || items.Count == 0;
        return items != null && items.Count == 1 && items[0].itemID.valueNumber == wanted;
    }

    private static object[] DescribeItems(MusicStagePlaySetting setting)
    {
        var items = new List<object>();
        if (setting.itemInfoList != null)
            foreach (var item in setting.itemInfoList)
                items.Add(new { itemId = item.itemID.valueNumber, name = item.itemID.GetName(),
                    item.used, item.hpBorder, item.hpRecovery });
        return items.ToArray();
    }
}
