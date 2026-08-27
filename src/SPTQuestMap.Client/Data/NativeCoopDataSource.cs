using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Client.Data;

/// <summary>
/// Fetches the data the in-game Coop views need: the roster of other profiles on this
/// server, and completed-quest snapshots for whichever of them the player selects.
///
/// Deliberately thin compared to <see cref="ServerQuestTopologySource"/>: the client already
/// has the full topology locally, so this only ever asks for what another profile has
/// finished, never the topology itself.
/// </summary>
internal sealed class NativeCoopDataSource
{
    private const string ProfilesRoute = "/questmap/client/coop/profiles";
    private const string StateRoute = "/questmap/client/coop/state";

    public async Task<CoopProfileSummary[]> LoadProfilesAsync()
    {
        var bytes = await RequestHandler.GetDataAsync(ProfilesRoute).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(bytes);
        return JsonConvert.DeserializeObject<CoopProfileSummary[]>(json) ?? [];
    }

    /// <summary>
    /// Ids travel as one comma-separated path segment, matching how
    /// <c>QuestMapClientCoopRouter</c> parses the request server-side. Returns an entry only
    /// for ids the server actually recognised - an id that has since become invalid (profile
    /// deleted, say) is silently absent rather than an error, so a caller should not assume
    /// a 1:1 correspondence with what it asked for.
    /// </summary>
    public async Task<CoopProfileSnapshot[]> LoadSnapshotsAsync(IReadOnlyCollection<string> profileIds)
    {
        if (profileIds.Count == 0) return [];
        var route = $"{StateRoute}/{Uri.EscapeDataString(string.Join(',', profileIds))}";
        var bytes = await RequestHandler.GetDataAsync(route).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(bytes);
        return JsonConvert.DeserializeObject<CoopProfileSnapshot[]>(json) ?? [];
    }
}
