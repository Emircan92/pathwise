using Pathwise.Application.ReviewWindows;
using Pathwise.Domain.Reconstruction;
using Pathwise.Domain.ReviewWindows;

namespace Pathwise.Application.Tests;

public sealed class ReviewPositionSelectorTests
{
    [Theory]
    [InlineData(60_000L, 150_000L, new[] { 1, 2 })]
    [InlineData(75_000L, 150_000L, new[] { 1, 2 })]
    [InlineData(75_000L, 75_000L, new[] { 1 })]
    [InlineData(60_000L, 60_000L, new[] { 1 })]
    public void SelectsOriginalBoundaryAndInPeriodFrames(long start, long end, int[] expected)
    {
        var reconstruction = Reconstruction(EnemyResolutionStatus.Resolved);
        var samples = ReviewPositionSelector.Select(reconstruction, Candidate(reconstruction, start, end));

        Assert.Equal(expected, samples.Select(sample => sample.FrameIndex).ToArray());
        Assert.Equal(expected.Select(index => new long[] { 0, 60_000, 143_500, 210_000 }[index]), samples.Select(sample => sample.TimestampMs));
        Assert.Equal(new Position(11, 21), samples[0].ConfiguredPlayerPosition);
        Assert.Equal(new Position(31, 41), samples[0].EnemyJunglerPosition);
        if (samples.Count > 1)
        {
            Assert.Null(samples[1].ConfiguredPlayerPosition);
            Assert.Null(samples[1].EnemyJunglerPosition);
        }
    }

    [Fact]
    public void UnresolvedEnemyNeverReceivesSubstitutePosition()
    {
        var reconstruction = Reconstruction(EnemyResolutionStatus.Ambiguous);
        var samples = ReviewPositionSelector.Select(reconstruction, Candidate(reconstruction, 75_000, 210_000));

        Assert.Equal([1, 2, 3], samples.Select(sample => sample.FrameIndex).ToArray());
        Assert.All(samples, sample => Assert.Null(sample.EnemyJunglerPosition));
        Assert.Equal(new Position(51, 61), samples[2].ConfiguredPlayerPosition);
    }

    private static ReviewWindowCandidate Candidate(GameReconstruction reconstruction, long start, long end)
    {
        var signal = new ReviewSignal(ReviewSignalKind.GoldDifferenceChange, start, end, [], [], 0, 1_000, 1_000, 1_000, ReviewSignalDirection.Increase, 0);
        return new(1, ReviewSelectionReason.GoldChange, start, end, reconstruction.Participants[0], null,
            reconstruction.EnemyResolution, reconstruction.Changes(start, end), null, [signal], [], [], [], [], []);
    }

    private static GameReconstruction Reconstruction(EnemyResolutionStatus status)
    {
        var player = new Participant(2, 100, 121, "Khazix", "JUNGLE", false, 0, 0, 0);
        var enemy = new Participant(7, 200, 200, "Belveth", "JUNGLE", true, 0, 0, 0);
        var frames = new[]
        {
            Frame(0, 0, new(1, 2), new(3, 4)),
            Frame(1, 60_000, new(11, 21), new(31, 41)),
            Frame(2, 143_500, null, null),
            Frame(3, 210_000, new(51, 61), new(71, 81))
        };
        return new(new("TEST", "1.0", 420, 11, 210, [player, enemy], 2,
            status == EnemyResolutionStatus.Resolved ? new(status, 7) : new(status, null), frames, [], []));
    }

    private static FrameObservation Frame(int index, long timestamp, Position? player, Position? enemy) => new(
        index, timestamp, Observation(2, player), Observation(7, enemy));

    private static PlayerObservation Observation(int id, Position? position) => new(id, position, 1_000, 0, 0, 1, 0, 0, null, null, null, null);
}
