using System.Diagnostics;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Services;

namespace SPTQuestMap.Routers;

/// <summary>
/// Read-only feed for the in-game Coop views: the roster of other profiles on this server,
/// and per-profile completed-quest snapshots for whichever of them the player selects.
///
/// Deliberately minimal, unlike <see cref="QuestMapClientStaticRouter"/>'s full topology feed:
/// only two id lists per profile are exposed - what they have finished, and what they can act
/// on right now. The client already has the full topology locally, so those two facts are
/// enough for it to compute chain progress, catch-up and map suggestions itself. Anything
/// beyond them would be someone else's profile data leaving the server for no reason.
/// </summary>
[Injectable]
public sealed class QuestMapClientCoopRouter(
    JsonUtil jsonUtil,
    QuestMapDataService dataService,
    ISptLogger<QuestMapDataService> logger)
    : DynamicRouter(
        jsonUtil,
        [
            new RouteAction<EmptyRequestData>(
                ProfilesRoute,
                (_, _, sessionId, _) =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var currentProfileId = sessionId.ToString();
                    var profiles = dataService.GetProfiles()
                        .Where(profile => !string.Equals(profile.Id, currentProfileId, StringComparison.Ordinal))
                        .Select(profile => new CoopProfileSummary(profile.Id, profile.Nickname, profile.Side, profile.Level))
                        .ToArray();
                    var response = jsonUtil.Serialize(profiles)
                        ?? throw new InvalidOperationException("QuestMap coop profiles serialization returned null.");
                    logger.Info(
                        "QUESTMAP_M09_SERVER_COOP_PROFILES_ROUTE " +
                        $"profiles={profiles.Length}; chars={response.Length}; totalMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
                    return new ValueTask<string>(response);
                }),
            new RouteAction<EmptyRequestData>(
                StateRoute,
                (url, _, _, _) =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var requestedIds = GetRequestedIds(url);
                    var snapshots = requestedIds
                        .Select(id => (Id: id, State: dataService.GetProfileState(id)))
                        .Where(pair => pair.State is not null)
                        .Select(pair => new CoopProfileSnapshot(
                            pair.Id,
                            pair.State!.Quests
                                .Where(quest => quest.DisplayState == "Completed")
                                .Select(quest => quest.QuestId)
                                .Distinct(StringComparer.Ordinal)
                                .ToArray(),
                            // What this profile could actually take into a raid today. Without
                            // it the client can only ask "have they finished it", which counts
                            // every level- and prerequisite-locked quest as outstanding.
                            pair.State!.Quests
                                .Where(quest => quest.DisplayState is "Available" or "InProgress" or "ReadyToFinish")
                                .Select(quest => quest.QuestId)
                                .Distinct(StringComparer.Ordinal)
                                .ToArray(),
                            // Which legs of a multi-map quest this profile has already done.
                            // Without it a quest spanning four maps counts against all four
                            // forever, even once three of them are finished.
                            pair.State!.Quests
                                .SelectMany(quest => quest.Objectives)
                                .Where(objective => objective.Complete)
                                .Select(objective => objective.ObjectiveId)
                                .Distinct(StringComparer.Ordinal)
                                .ToArray()))
                        .ToArray();
                    var response = jsonUtil.Serialize(snapshots)
                        ?? throw new InvalidOperationException("QuestMap coop state serialization returned null.");
                    logger.Info(
                        "QUESTMAP_M09_SERVER_COOP_STATE_ROUTE " +
                        $"requested={requestedIds.Length}; returned={snapshots.Length}; chars={response.Length}; " +
                        $"totalMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
                    return new ValueTask<string>(response);
                })
        ])
{
    public const string ProfilesRoute = "/questmap/client/coop/profiles";
    public const string StateRoute = "/questmap/client/coop/state";

    /// <summary>
    /// Ids travel as one comma-separated path segment after <see cref="StateRoute"/>, the
    /// same suffix-encoding convention <see cref="QuestMapClientStaticRouter.GetRequestedLanguage"/>
    /// uses for language - this project's routes carry extra data in the path, not a query
    /// string, so both routers parse the same way.
    /// </summary>
    private static string[] GetRequestedIds(string url)
    {
        var prefix = StateRoute + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal)) return [];

        var encoded = url[prefix.Length..];
        if (encoded.Length == 0) return [];

        return Uri.UnescapeDataString(encoded)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
