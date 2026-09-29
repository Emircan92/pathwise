using Pathwise.Application.Interpretation;

namespace Pathwise.Application.Tests;

public sealed class NarrativeInterpretationValidatorTests
{
    [Fact]
    public void PromptPolicyDefinesRequestedWindowAndProvenanceTimeSemantics()
    {
        Assert.Equal(3, NarrativeInterpretationPromptPolicyV1.Version);
        Assert.Contains(
            "window.requestedStartTimestampMs <= startTimestampMs <= endTimestampMs <= window.requestedEndTimestampMs",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "sampled source-frame timestamps are provenance for reconstructed state and may lie outside those bounds",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "recorded event and encounter timestamps locate recorded evidence",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PromptPolicyPrioritizesConfiguredPlayerSynthesisOverEvidenceInventory()
    {
        Assert.Contains(
            "Lead the overview with the most important development or transition for the configured player",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Prefer synthesis over exhaustive event recitation",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Use quantitative evidence selectively",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Investigation moments do not need to be exhaustive",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Create a concurrent thread only for a materially different story",
            NarrativeInterpretationPromptPolicyV1.Instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsGroundedOutputAndAddsTrustedGenerationMetadata()
    {
        var input = Input();
        var result = NarrativeInterpretationValidatorV1.Validate(input, Provider(Output(input)));

        Assert.Equal(input.InputFingerprint, result.InputFingerprint);
        Assert.Equal(3, result.PromptPolicyVersion);
        Assert.Equal("fake", result.Generation.Provider);
        Assert.Single(result.Uncertainties);
    }

    [Fact]
    public void RejectsFingerprintMismatch()
    {
        var input = Input();
        var output = Output(input) with { InputFingerprint = new string('0', 64) };

        Assert.Throws<NarrativeOutputValidationException>(() =>
            NarrativeInterpretationValidatorV1.Validate(input, Provider(output)));
    }

    [Fact]
    public void RejectsUnknownOrEmptyClaimEvidence()
    {
        var input = Input();
        var unknown = Output(input) with
        {
            Overview = new("Text", "factSummary", ["unknown:evidence"])
        };
        var empty = Output(input) with
        {
            Overview = new("Text", "factSummary", [])
        };

        Assert.Throws<NarrativeOutputValidationException>(() => NarrativeInterpretationValidatorV1.Validate(input, Provider(unknown)));
        Assert.Throws<NarrativeOutputValidationException>(() => NarrativeInterpretationValidatorV1.Validate(input, Provider(empty)));
    }

    [Fact]
    public void RejectsRangesOutsideSelectedWindow()
    {
        var input = Input();
        var output = Output(input) with
        {
            Threads = [new(
                "Outside",
                1_860_547,
                2_100_622,
                new("The lead changed across the sampled metric endpoints.", "factSummary", ["observation:relativeGoldMovement"]),
                [])]
        };

        var exception = Assert.Throws<NarrativeOutputValidationException>(() =>
            NarrativeInterpretationValidatorV1.Validate(input, Provider(output)));

        Assert.Equal(
            "The thread[0] range 1860547–2100622 is outside requested window 1910458–2100622; evidence IDs: [observation:relativeGoldMovement].",
            exception.Message);
    }

    [Fact]
    public void InvestigationRangeDiagnosticIdentifiesIndexBoundsAndEvidenceWithoutProse()
    {
        var input = Input();
        var output = Output(input) with
        {
            MomentsWorthInvestigating = [new(
                "Later death",
                2_080_312,
                2_100_623,
                new("A recorded death precedes later combat events.", "crossEvidenceSynthesis", ["event:35:14"]),
                "What happened before the recorded death?",
                [new("The supplied evidence does not establish why the death occurred.", "notCaptured", ["event:35:14"])])]
        };

        var exception = Assert.Throws<NarrativeOutputValidationException>(() =>
            NarrativeInterpretationValidatorV1.Validate(input, Provider(output)));

        Assert.Equal(
            "The investigationMoment[0] range 2080312–2100623 is outside requested window 1910458–2100622; evidence IDs: [event:35:14].",
            exception.Message);
        Assert.DoesNotContain("A recorded death", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresMeaningfulUncertainty()
    {
        var input = Input();
        var output = Output(input) with { Uncertainties = [] };

        Assert.Throws<NarrativeOutputValidationException>(() =>
            NarrativeInterpretationValidatorV1.Validate(input, Provider(output)));
    }

    [Fact]
    public void FingerprintIsCanonicalAndChangesWithProjectedEvidence()
    {
        var input = Input();

        Assert.Equal(input.InputFingerprint, NarrativeInterpretationFingerprint.Compute(input with { InputFingerprint = "ignored" }));
        Assert.NotEqual(input.InputFingerprint, NarrativeInterpretationFingerprint.Compute(input with
        {
            EvidenceLimitations = [.. input.EvidenceLimitations, "A newly projected limitation."]
        }));
    }

    private static NarrativeInterpretationInputV1 Input()
    {
        var input = new NarrativeInterpretationInputV1(
            1,
            string.Empty,
            new(1, 2, 1, 1, 1, 1),
            2,
            7,
            [new(2, "Khazix", 100, "configuredPlayer"), new(7, "Kayn", 200, "enemyJungler")],
            new(1_910_458, 2_100_622, new(31, 1_860_547), new(35, 2_100_622), "goldAndXpChange", ["goldDifferenceChange", "xpDifferenceChange"], []),
            new(null, "unknownPatch"),
            [
                new NarrativeWindowSelectionEvidenceV1("window:selection", "goldAndXpChange", ["goldDifferenceChange", "xpDifferenceChange"], []),
                new NarrativeMetricObservationEvidenceV1(
                    "observation:relativeGoldMovement",
                    "relativeGoldMovement",
                    new(31, 1_860_547),
                    new(35, 2_100_622),
                    6_926,
                    5_746,
                    -1_180,
                    new(20_608, 22_842),
                    new(13_682, 17_096)),
                new NarrativeCombatEventEvidenceV1("event:35:14", new(35, 14), 2_080_312, 7, 2, [])
            ],
            ["No spatial samples are supplied."]);
        return input with { InputFingerprint = NarrativeInterpretationFingerprint.Compute(input) };
    }

    private static NarrativeInterpretationModelOutputV1 Output(NarrativeInterpretationInputV1 input) => new(
        1,
        input.InputFingerprint,
        new("The selected period contains a material gold change.", "factSummary", ["window:selection"]),
        [],
        [],
        [new("The selection signal does not establish why the change occurred.", "notCaptured", ["window:selection"])]);

    private static NarrativeProviderResultV1 Provider(NarrativeInterpretationModelOutputV1 output) =>
        new(output, "fake", "fake-model", DateTimeOffset.UnixEpoch);
}
