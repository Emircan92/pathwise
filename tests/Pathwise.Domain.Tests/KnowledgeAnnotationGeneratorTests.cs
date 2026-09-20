using Pathwise.Domain.FactualObservations;
using Pathwise.Domain.Knowledge;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Domain.Tests;

public sealed class KnowledgeAnnotationGeneratorTests
{
    private static readonly KnowledgeSource Source = new("Riot source", new("https://example.test/riot"));

    [Theory]
    [InlineData("16.18.817.5716")]
    [InlineData("16.18.999999")]
    [InlineData("16.18")]
    [InlineData("26.18")]
    [InlineData("26.18.1")]
    public void ResolverRecognizesApprovedFamiliesAndNumericSuffixes(string raw)
    {
        var result = new PublicPatchResolver().Resolve(raw);

        Assert.Equal(raw, result.RawGameVersion);
        Assert.Equal(new PublicPatch(26, 18), result.Patch);
        Assert.NotEmpty(result.Basis);
    }

    [Theory]
    [InlineData("")]
    [InlineData("16")]
    [InlineData("16.18.")]
    [InlineData("16.18.build")]
    [InlineData(" 16.18")]
    [InlineData("16.19")]
    [InlineData("36.18")]
    public void ResolverLeavesMalformedAndUnknownFamiliesUnresolved(string raw)
    {
        var result = new PublicPatchResolver().Resolve(raw);

        Assert.Null(result.Patch);
        Assert.Equal(raw, result.RawGameVersion);
    }

    [Fact]
    public void CoverageDistinguishesUnknownPatchMissingPackAndUnsupportedMatch()
    {
        var reconstruction = Reconstruction("16.19", mapId: 12);
        var observations = Observations(reconstruction, Window(reconstruction, 0, 100));
        var generator = new KnowledgeAnnotationGenerator();

        var unknown = generator.Generate(
            reconstruction,
            observations,
            new PublicPatchResolver().Resolve(reconstruction.Patch),
            null);
        Assert.Equal(KnowledgeCoverage.UnknownPatch, unknown.Coverage);

        reconstruction = Reconstruction("26.19", mapId: 12);
        observations = Observations(reconstruction, Window(reconstruction, 0, 100));
        var noPack = generator.Generate(
            reconstruction,
            observations,
            new(reconstruction.Patch, new(26, 19), "test"),
            null);
        Assert.Equal(KnowledgeCoverage.NoPackForPatch, noPack.Coverage);

        reconstruction = Reconstruction("26.18", mapId: 12);
        observations = Observations(reconstruction, Window(reconstruction, 0, 100));
        var unsupported = generator.Generate(reconstruction, observations, Resolution(reconstruction), Pack());
        Assert.Equal(KnowledgeCoverage.UnsupportedMatch, unsupported.Coverage);
        Assert.Empty(unsupported.Annotations);

        reconstruction = Reconstruction("26.18", queueId: 440);
        observations = Observations(reconstruction, Window(reconstruction, 0, 100));
        unsupported = generator.Generate(reconstruction, observations, Resolution(reconstruction), Pack());
        Assert.Equal(KnowledgeCoverage.UnsupportedMatch, unsupported.Coverage);
        Assert.Empty(unsupported.Annotations);
    }

    [Fact]
    public void AvailableCoverageMayContainNoAnnotationsAndRetainsProvenance()
    {
        var reconstruction = Reconstruction("16.18.817.5716");
        var result = Generate(reconstruction, Window(reconstruction, 700_000, 800_000));

        Assert.Equal(KnowledgeCoverage.Available, result.Coverage);
        Assert.Empty(result.Annotations);
        Assert.Equal(1, result.GeneratorVersion);
        Assert.Equal(60_000, result.InitialSpawnContextRadiusMs);
        Assert.Equal("16.18.817.5716", result.PatchResolution.RawGameVersion);
        Assert.Equal(new PublicPatch(26, 18), result.PatchResolution.Patch);
        Assert.Equal(1, result.Pack!.Version);
    }

    [Theory]
    [InlineData(239_999, 239_999, false)]
    [InlineData(239_999, 240_000, true)]
    [InlineData(359_999, 360_000, true)]
    [InlineData(360_000, 360_001, false)]
    public void NearSpawnUsesApprovedExactIntersectionBoundaries(long start, long end, bool expected)
    {
        var reconstruction = Reconstruction();
        var result = Generate(reconstruction, Window(reconstruction, start, end), DragonOnlyPack());

        Assert.Equal(expected, result.Annotations.Count == 1);
        if (expected)
            Assert.Equal(KnowledgeAnnotationKind.NearInitialSpawn, result.Annotations[0].Kind);
    }

    [Theory]
    [InlineData("AIR_DRAGON")]
    [InlineData("EARTH_DRAGON")]
    [InlineData("FIRE_DRAGON")]
    [InlineData("WATER_DRAGON")]
    [InlineData("HEXTECH_DRAGON")]
    [InlineData("CHEMTECH_DRAGON")]
    public void RecognizedElementalDragonsAttachToExistingObservation(string subtype)
    {
        var @event = Objective(450_000, 4, 5, "DRAGON", subtype);
        var reconstruction = Reconstruction(events: [@event]);
        var window = Window(reconstruction, 400_000, 500_000, [@event]);

        var annotation = Assert.Single(Generate(reconstruction, window, DragonOnlyPack()).Annotations);
        Assert.Equal(KnowledgeAnnotationKind.RecordedObjectiveContext, annotation.Kind);
        Assert.Equal(new SourceEventReference(4, 5), annotation.Target.Event);
        Assert.NotNull(annotation.Target.Observation);
        Assert.Equal(450_000, annotation.RecordedKillTimestampMs);
    }

    [Theory]
    [InlineData("ELDER_DRAGON")]
    [InlineData("UNKNOWN_DRAGON")]
    [InlineData(null)]
    public void ElderAndUnknownDragonSubtypesAreExcluded(string? subtype)
    {
        var @event = Objective(450_000, 4, 5, "DRAGON", subtype);
        var reconstruction = Reconstruction(events: [@event]);
        var window = Window(reconstruction, 400_000, 500_000, [@event]);

        Assert.Empty(Generate(reconstruction, window, DragonOnlyPack()).Annotations);
    }

    [Fact]
    public void BaronMatchesAndPreSpawnKillProducesNoAnnotationOrWindowFallback()
    {
        var early = Objective(1_199_999, 1, 1, "BARON_NASHOR", null);
        var reconstruction = Reconstruction(events: [early]);
        var earlyWindow = Window(reconstruction, 1_140_000, 1_260_000, [early]);
        Assert.Empty(Generate(reconstruction, earlyWindow, BaronOnlyPack()).Annotations);

        var kill = Objective(1_200_000, 1, 2, "BARON_NASHOR", null);
        reconstruction = Reconstruction(events: [kill]);
        var result = Generate(reconstruction, Window(reconstruction, 1_199_999, 1_200_000, [kill]), BaronOnlyPack());
        Assert.Equal(KnowledgeAnnotationKind.RecordedObjectiveContext, Assert.Single(result.Annotations).Kind);
    }

    [Fact]
    public void OrderingDeduplicationDistinctEventsAndOverlappingWindowsAreDeterministic()
    {
        var dragon1 = Objective(400_000, 7, 3, "DRAGON", "FIRE_DRAGON");
        var dragon2 = Objective(400_000, 7, 4, "DRAGON", "WATER_DRAGON");
        var reconstruction = Reconstruction(events: [dragon2, dragon1]);
        var later = Window(reconstruction, 350_000, 450_000, [dragon2, dragon1, dragon1]);
        var earlier = Window(reconstruction, 300_000, 410_000, [dragon2, dragon1]);
        var observations = Observations(reconstruction, later, earlier);

        var result = new KnowledgeAnnotationGenerator().Generate(
            reconstruction,
            observations,
            Resolution(reconstruction),
            DragonOnlyPack());

        Assert.Equal(4, result.Annotations.Count);
        Assert.Equal(new[] { 300_000L, 300_000L, 350_000L, 350_000L },
            result.Annotations.Select(x => x.Target.Window.RequestedStartTimestampMs));
        Assert.Equal(new[] { 3, 4, 3, 4 },
            result.Annotations.Select(x => x.Target.Event!.EventIndex));
    }

    [Fact]
    public void InvalidPackAndInconsistentEventWindowFailExplicitly()
    {
        var @event = Objective(300_000, 1, 1, "DRAGON", "AIR_DRAGON");
        var reconstruction = Reconstruction(events: [@event]);
        var invalidWindow = Window(reconstruction, 300_000, 400_000, [@event]);
        Assert.Throws<ArgumentException>(() => Generate(reconstruction, invalidWindow, DragonOnlyPack()));

        var duplicateFacts = new KnowledgePack("pack", 1, new(26, 18), 11, 420, "review", [
            Fact("same", KnowledgeObjective.ElementalDragon, 300_000),
            Fact("same", KnowledgeObjective.BaronNashor, 1_200_000)]);
        Assert.Throws<ArgumentException>(() => Generate(
            reconstruction,
            Window(reconstruction, 200_000, 400_000, [@event]),
            duplicateFacts));
    }

    private static KnowledgeAnnotationResult Generate(
        GameReconstruction reconstruction,
        WindowFactualObservations window,
        KnowledgePack? pack = null) =>
        new KnowledgeAnnotationGenerator().Generate(
            reconstruction,
            Observations(reconstruction, window),
            Resolution(reconstruction),
            pack ?? Pack());

    private static PatchResolution Resolution(GameReconstruction reconstruction) =>
        new PublicPatchResolver().Resolve(reconstruction.Patch);

    private static FactualObservationResult Observations(
        GameReconstruction reconstruction,
        params WindowFactualObservations[] windows) =>
        new(FactualObservationGenerator.CurrentVersion, FactualObservationGenerator.Policy, windows);

    private static WindowFactualObservations Window(
        GameReconstruction reconstruction,
        long start,
        long end,
        IReadOnlyList<EliteMonsterKillEvent>? events = null)
    {
        var window = new SelectedReviewWindowKey(
            reconstruction.MatchId,
            reconstruction.ConfiguredParticipantId,
            reconstruction.EnemyResolution.ParticipantId,
            start,
            end,
            reconstruction.ReconstructionVersion,
            ReviewWindowDetector.CurrentVersion);
        FactualObservation[] observations = events is null
            ? []
            : [new EliteObjectiveContextObservation(
                new(window, FactualObservationGenerator.CurrentVersion, FactualObservationKind.EliteObjectiveContext),
                events)];
        return new(window, reconstruction.EnemyResolution, observations, [], []);
    }

    private static GameReconstruction Reconstruction(
        string patch = "26.18",
        int mapId = 11,
        int queueId = 420,
        IReadOnlyList<ReconstructionEvent>? events = null)
    {
        var participant = new Participant(1, 100, 1, "Champion", "JUNGLE", true, 0, 0, 0);
        var observation = new PlayerObservation(1, null, 0, 0, 0, 1, 0, 0, null, null, null, null);
        return new(new(
            "MATCH",
            patch,
            queueId,
            mapId,
            2_000,
            [participant],
            1,
            new(EnemyResolutionStatus.Missing, null),
            [new(0, 0, observation, null), new(1, 2_000_000, observation, null)],
            events ?? [],
            []));
    }

    private static EliteMonsterKillEvent Objective(
        long timestamp,
        int frame,
        int index,
        string type,
        string? subtype) =>
        new(timestamp, new(frame, index), type, subtype, null,
            new(TeamAttributionKind.Unknown, null, null, null), [], null, null);

    private static ObjectiveInitialSpawnFact Fact(string id, KnowledgeObjective objective, long spawn) =>
        new(id, objective, spawn, [Source]);

    private static KnowledgePack Pack() => new(
        "pack", 1, new(26, 18), 11, 420, "reviewed through 26.18",
        [Fact("elemental-dragon.initial-spawn", KnowledgeObjective.ElementalDragon, 300_000),
         Fact("baron-nashor.initial-spawn", KnowledgeObjective.BaronNashor, 1_200_000)]);

    private static KnowledgePack DragonOnlyPack() => new(
        "pack", 1, new(26, 18), 11, 420, "reviewed through 26.18",
        [Fact("elemental-dragon.initial-spawn", KnowledgeObjective.ElementalDragon, 300_000)]);

    private static KnowledgePack BaronOnlyPack() => new(
        "pack", 1, new(26, 18), 11, 420, "reviewed through 26.18",
        [Fact("baron-nashor.initial-spawn", KnowledgeObjective.BaronNashor, 1_200_000)]);
}
