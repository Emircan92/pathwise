using Pathwise.Api;
using Pathwise.Application.Knowledge;
using Pathwise.Application.Reconstruction;
using Pathwise.Application.ReviewWindows;
using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Knowledge;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Api.Tests;

public sealed class MatchReviewMapperTests
{
    [Theory]
    [InlineData(KnowledgeCoverage.Available, "available", 26, 18, "26.18")]
    [InlineData(KnowledgeCoverage.UnknownPatch, "unknownPatch", null, null, null)]
    [InlineData(KnowledgeCoverage.NoPackForPatch, "noPackForPatch", 26, 19, "26.19")]
    [InlineData(KnowledgeCoverage.UnsupportedMatch, "unsupportedMatch", 26, 18, "26.18")]
    public void MapsEveryKnowledgeCoverageState(
        KnowledgeCoverage coverage,
        string expectedCoverage,
        int? major,
        int? minor,
        string? expectedPatch)
    {
        var reconstruction = Reconstruction(EnemyResolutionStatus.Missing, null);
        var detection = Detection(reconstruction, []);
        var observations = new FactualObservationResult(FactualObservationGenerator.CurrentVersion, FactualObservationGenerator.Policy, []);
        var patch = major is null ? null : new PublicPatch(major.Value, minor!.Value);
        var knowledge = new KnowledgeAnnotationResult(
            KnowledgeAnnotationGenerator.CurrentVersion,
            new(reconstruction.Patch, patch, "test"),
            coverage,
            coverage == KnowledgeCoverage.Available ? KnowledgePacks.SummonersRiftObjectiveInitialSpawns : null,
            KnowledgeAnnotationGenerator.InitialSpawnContextRadiusMs,
            []);

        var response = MatchReviewApiMapper.Map(Result(reconstruction, new(detection, observations, knowledge)));

        Assert.Equal(expectedCoverage, response.Knowledge.Coverage);
        Assert.Equal(expectedPatch, response.Knowledge.PublicPatch);
        Assert.Empty(response.Windows);
        Assert.Equal("missing", response.EnemyResolution.Status);
        Assert.Null(response.EnemyResolution.ParticipantId);
    }

    [Fact]
    public void MapsMetricOmissionWithoutInventingObservation()
    {
        var reconstruction = Reconstruction(EnemyResolutionStatus.Resolved, 7);
        var changes = reconstruction.Changes(0, 180_000);
        var participant = reconstruction.Participants[0];
        var enemy = reconstruction.Participants[1];
        var signal = new ReviewSignal(ReviewSignalKind.GoldDifferenceChange, 0, 180_000, [], [], 0, 1_000, 1_000, 1_000, ReviewSignalDirection.Increase, 0);
        var candidate = new ReviewWindowCandidate(1, ReviewSelectionReason.GoldChange, 0, 180_000, participant, enemy, reconstruction.EnemyResolution, changes, null, [signal], [], [], [], [], []);
        var detection = Detection(reconstruction, [candidate]);
        var key = new SelectedReviewWindowKey("TEST", 2, 7, 0, 180_000, 1, 2);
        var omission = new MetricObservationOmission(
            FactualObservationKind.RelativeGoldMovement,
            new(2, new(0, 0), new(1, 60_000), 1_000, 900));
        var observations = new FactualObservationResult(
            FactualObservationGenerator.CurrentVersion,
            FactualObservationGenerator.Policy,
            [new(key, reconstruction.EnemyResolution, [], [omission], [])]);
        var knowledge = new KnowledgeAnnotationResult(1, new(reconstruction.Patch, null, "test"), KnowledgeCoverage.UnknownPatch, null, 60_000, []);

        var response = MatchReviewApiMapper.Map(Result(reconstruction, new(detection, observations, knowledge)));

        var window = Assert.Single(response.Windows);
        Assert.Empty(window.Observations);
        var mapped = Assert.Single(window.MetricOmissions);
        Assert.Equal("relativeGoldMovement", mapped.Kind);
        Assert.Equal("counterRegression", mapped.Reason);
    }

    [Fact]
    public void PreservesDistinctSourceIdentityForSameTimestampEvents()
    {
        var reconstruction = Reconstruction(EnemyResolutionStatus.Resolved, 7);
        var candidate = Candidate(reconstruction);
        var detection = Detection(reconstruction, [candidate]);
        var key = new SelectedReviewWindowKey("TEST", 2, 7, 0, 180_000, 1, 2);
        var observationKey = new FactualObservationKey(key, 1, FactualObservationKind.EliteObjectiveContext);
        var timestamp = 120_000L;
        var first = Objective(timestamp, 4, 1);
        var second = Objective(timestamp, 4, 2);
        var observations = new FactualObservationResult(1, FactualObservationGenerator.Policy,
            [new(key, reconstruction.EnemyResolution, [new EliteObjectiveContextObservation(observationKey, [first, second])], [], [])]);
        var fact = KnowledgePacks.SummonersRiftObjectiveInitialSpawns.Facts.Single(value => value.Objective == KnowledgeObjective.ElementalDragon);
        var knowledge = new KnowledgeAnnotationResult(1, new(reconstruction.Patch, new(26, 18), "test"), KnowledgeCoverage.Available, KnowledgePacks.SummonersRiftObjectiveInitialSpawns, 60_000,
            [
                new(fact.Id, new(key, observationKey, first.Source), KnowledgeAnnotationKind.RecordedObjectiveContext, timestamp),
                new(fact.Id, new(key, observationKey, second.Source), KnowledgeAnnotationKind.RecordedObjectiveContext, timestamp)
            ]);

        var response = MatchReviewApiMapper.Map(Result(reconstruction, new(detection, observations, knowledge)));

        var window = Assert.Single(response.Windows);
        var objective = Assert.IsType<EliteObjectiveContextDto>(Assert.Single(window.Observations));
        Assert.Equal([(4, 1), (4, 2)], objective.Events.Select(value => (value.Source.FrameIndex, value.Source.EventIndex)).ToArray());
        Assert.Equal([(4, 1), (4, 2)], window.KnowledgeAnnotations.Select(value => (value.Target.Event!.FrameIndex, value.Target.Event.EventIndex)).ToArray());
    }

    private static ReconstructionResult<MatchReview> Result(GameReconstruction reconstruction, MatchReview review) => new(
        reconstruction,
        new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "v5", "v5"),
        review);

    private static ReviewWindowDetectionResult Detection(GameReconstruction reconstruction, IReadOnlyList<ReviewWindowCandidate> candidates) => new(
        candidates,
        ReviewWindowDetector.CurrentVersion,
        reconstruction.ReconstructionVersion,
        ReviewWindowOptions.Default,
        reconstruction.EnemyResolution,
        [], [], [], []);

    private static ReviewWindowCandidate Candidate(GameReconstruction reconstruction)
    {
        var signal = new ReviewSignal(ReviewSignalKind.GoldDifferenceChange, 0, 180_000, [], [], 0, 1_000, 1_000, 1_000, ReviewSignalDirection.Increase, 0);
        return new(1, ReviewSelectionReason.GoldChange, 0, 180_000, reconstruction.Participants[0], reconstruction.Participants[1], reconstruction.EnemyResolution, reconstruction.Changes(0, 180_000), null, [signal], [], [], [], [], []);
    }

    private static EliteMonsterKillEvent Objective(long timestamp, int frame, int index) => new(
        timestamp,
        new(frame, index),
        "DRAGON",
        "HEXTECH_DRAGON",
        7,
        new(TeamAttributionKind.KnownTeam, 200, 200, null),
        [], null, null);

    private static GameReconstruction Reconstruction(EnemyResolutionStatus status, int? enemyId)
    {
        var configured = new Participant(2, 100, 121, "Khazix", "JUNGLE", false, 0, 0, 0);
        var enemy = new Participant(7, 200, 200, "Belveth", "JUNGLE", true, 0, 0, 0);
        var frames = new[]
        {
            new FrameObservation(0, 0, Observation(2, 1_000), enemyId is null ? null : Observation(7, 1_000)),
            new FrameObservation(1, 60_000, Observation(2, 900), enemyId is null ? null : Observation(7, 1_000)),
            new FrameObservation(2, 180_000, Observation(2, 2_000), enemyId is null ? null : Observation(7, 1_000))
        };
        return new(new("TEST", "1.0", 420, 11, 180, enemyId is null ? [configured] : [configured, enemy], 2, new(status, enemyId), frames, [], []));
    }

    private static PlayerObservation Observation(int participantId, long gold) => new(participantId, null, gold, 0, 0, 1, 0, 0, null, null, null, null);
}
