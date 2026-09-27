using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Progression;

public sealed class ProgressionEvidenceProjector
{
    public const int CurrentVersion = 1;
    public const int RiftHeraldItemId = 3513;

    public ProgressionEvidenceResult Project(
        GameReconstruction reconstruction,
        ReviewWindowDetectionResult windows)
    {
        if (windows.ReconstructionVersion != reconstruction.ReconstructionVersion)
            throw new InvalidOperationException("Review windows and reconstruction versions do not match.");

        var events = reconstruction.Events
            .Select(ProjectEvent)
            .OfType<ProgressionEvent>()
            .OrderBy(value => value.TimestampMs)
            .ThenBy(value => value.Source.FrameIndex)
            .ThenBy(value => value.Source.EventIndex)
            .ToArray();

        var projectedWindows = windows.Candidates.Select(window =>
        {
            var slice = events.Where(value =>
                value.TimestampMs > window.RequestedStartTimestampMs &&
                value.TimestampMs <= window.RequestedEndTimestampMs).ToArray();
            return new WindowProgressionEvidence(
                window,
                slice.Any(value => value is GameEndedProgressionEvent),
                slice);
        }).ToArray();

        return new(
            CurrentVersion,
            reconstruction.ReconstructionVersion,
            Outcome(reconstruction, events.OfType<GameEndedProgressionEvent>().ToArray()),
            events,
            projectedWindows);
    }

    private static ProgressionEvent? ProjectEvent(ReconstructionEvent value) => value switch
    {
        StructureKillEvent { IsPlate: false } structure => new BuildingDestroyedProgressionEvent(
            structure.TimestampMs,
            structure.Source,
            structure.StructureType,
            structure.TowerType,
            structure.Lane,
            structure.OwningTeam,
            structure.KillerParticipantId,
            structure.AssistingParticipantIds,
            structure.Position),
        EliteMonsterKillEvent herald when string.Equals(herald.MonsterType, "RIFTHERALD", StringComparison.Ordinal) =>
            new RiftHeraldKilledProgressionEvent(
                herald.TimestampMs,
                herald.Source,
                herald.KillerParticipantId,
                herald.TeamAttribution,
                herald.AssistingParticipantIds,
                herald.Position),
        ItemTransactionEvent { Action: "destroyed", ItemId: RiftHeraldItemId } item =>
            new ItemDestroyedProgressionEvent(item.TimestampMs, item.Source, item.ParticipantId, RiftHeraldItemId),
        GameEndEvent gameEnd => new GameEndedProgressionEvent(
            gameEnd.TimestampMs,
            gameEnd.Source,
            gameEnd.WinningTeam),
        _ => null
    };

    private static ProgressionOutcomeEvidence Outcome(
        GameReconstruction reconstruction,
        IReadOnlyList<GameEndedProgressionEvent> gameEnds)
    {
        var configured = reconstruction.Participants.Single(value =>
            value.ParticipantId == reconstruction.ConfiguredParticipantId);
        var summary = reconstruction.MatchSummary;
        var configuredResult = summary?.ParticipantResults.SingleOrDefault(value =>
            value.ParticipantId == reconstruction.ConfiguredParticipantId);

        var winnerClaims = new List<int>();
        winnerClaims.AddRange(gameEnds
            .Select(value => value.WinningTeam.ResolvedTeamId)
            .OfType<int>());
        if (summary is not null)
        {
            winnerClaims.AddRange(summary.TeamResults.Where(value => value.Won.Value).Select(value => value.TeamId));
            winnerClaims.AddRange(summary.ParticipantResults.Where(value => value.Won.Value).Select(value => value.TeamId));
        }

        var distinctWinners = winnerClaims.Distinct().ToArray();
        var duration = summary?.ReportedDurationSeconds
            ?? new MatchFieldFact<int>(
                reconstruction.ReportedDurationSeconds,
                new MatchFieldReference("match.info.gameDuration"));

        return new(
            reconstruction.ConfiguredParticipantId,
            configured.TeamId,
            configuredResult?.Won,
            distinctWinners.Length == 1 ? distinctWinners[0] : null,
            duration,
            summary?.GameEndTimestampMs,
            summary?.EndOfGameResult,
            summary?.TeamResults ?? [],
            summary?.ParticipantResults ?? [],
            gameEnds.Count == 1 ? gameEnds[0] : null);
    }
}
