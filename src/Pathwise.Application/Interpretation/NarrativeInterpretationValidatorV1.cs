namespace Pathwise.Application.Interpretation;

public static class NarrativeInterpretationValidatorV1
{
    private static readonly HashSet<string> ClaimBases = new(StringComparer.Ordinal)
    {
        "factSummary",
        "crossEvidenceSynthesis"
    };

    private static readonly HashSet<string> UncertaintyReasons = new(StringComparer.Ordinal)
    {
        "notCaptured",
        "missingSource",
        "ambiguousAttribution",
        "samplingGap",
        "conflictingSources",
        "outsideNarrativeScope"
    };

    public static NarrativeInterpretationV1 Validate(
        NarrativeInterpretationInputV1 input,
        NarrativeProviderResultV1 providerResult)
    {
        var output = providerResult.Output ?? throw new NarrativeOutputValidationException("The provider returned no narrative output.");
        if (output.Version != NarrativeInterpretationVersions.Output)
            throw new NarrativeOutputValidationException("The narrative output version is not supported.");
        if (!string.Equals(output.InputFingerprint, input.InputFingerprint, StringComparison.Ordinal))
            throw new NarrativeOutputValidationException("The narrative output fingerprint does not match the authoritative input.");
        if (!string.Equals(NarrativeInterpretationFingerprint.Compute(input), input.InputFingerprint, StringComparison.Ordinal))
            throw new NarrativeOutputValidationException("The authoritative narrative input fingerprint is invalid.");

        var evidenceIds = input.Evidence.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        ValidateClaim(output.Overview, "overview", evidenceIds);
        ValidateCount(output.Threads, 0, 6, "threads");
        ValidateCount(output.MomentsWorthInvestigating, 0, 6, "moments worth investigating");
        ValidateCount(output.Uncertainties, 1, 8, "uncertainties");

        for (var index = 0; index < output.Threads.Count; index++)
        {
            var thread = output.Threads[index];
            Text(thread.Title, 1, 160, "thread title");
            Range(thread.StartTimestampMs, thread.EndTimestampMs, input, $"thread[{index}]", ThreadEvidenceIds(thread, evidenceIds));
            ValidateClaim(thread.Summary, "thread summary", evidenceIds);
            ValidateCount(thread.SignificantDevelopments, 0, 6, "thread developments");
            foreach (var claim in thread.SignificantDevelopments)
                ValidateClaim(claim, "significant development", evidenceIds);
        }

        for (var index = 0; index < output.MomentsWorthInvestigating.Count; index++)
        {
            var moment = output.MomentsWorthInvestigating[index];
            Text(moment.Title, 1, 160, "investigation title");
            Range(moment.StartTimestampMs, moment.EndTimestampMs, input, $"investigationMoment[{index}]", MomentEvidenceIds(moment, evidenceIds));
            ValidateClaim(moment.WhyItStandsOut, "investigation basis", evidenceIds);
            Text(moment.Question, 1, 500, "investigation question");
            ValidateCount(moment.Uncertainties, 1, 6, "investigation uncertainties");
            foreach (var uncertainty in moment.Uncertainties)
                ValidateUncertainty(uncertainty, evidenceIds);
        }

        foreach (var uncertainty in output.Uncertainties)
            ValidateUncertainty(uncertainty, evidenceIds);

        Text(providerResult.Provider, 1, 80, "provider name");
        Text(providerResult.Model, 1, 160, "model name");
        return new(
            NarrativeInterpretationVersions.Output,
            input.InputFingerprint,
            input.UpstreamVersions,
            NarrativeInterpretationVersions.PromptPolicy,
            new(providerResult.Provider, providerResult.Model, providerResult.GeneratedAtUtc),
            input.Window,
            output.Overview,
            output.Threads,
            output.MomentsWorthInvestigating,
            output.Uncertainties);
    }

    private static void ValidateClaim(NarrativeClaimV1 value, string name, IReadOnlySet<string> evidenceIds)
    {
        if (value is null) throw new NarrativeOutputValidationException($"The {name} is missing.");
        Text(value.Text, 1, 1_000, name);
        if (!ClaimBases.Contains(value.Basis))
            throw new NarrativeOutputValidationException($"The {name} has an unsupported basis.");
        ValidateCount(value.EvidenceIds, 1, 16, $"{name} evidence references");
        References(value.EvidenceIds, evidenceIds, name);
    }

    private static void ValidateUncertainty(NarrativeUncertaintyV1 value, IReadOnlySet<string> evidenceIds)
    {
        if (value is null) throw new NarrativeOutputValidationException("A narrative uncertainty is missing.");
        Text(value.Statement, 1, 700, "uncertainty statement");
        if (!UncertaintyReasons.Contains(value.Reason))
            throw new NarrativeOutputValidationException("A narrative uncertainty has an unsupported reason.");
        ValidateCount(value.RelatedEvidenceIds, 0, 16, "uncertainty evidence references");
        References(value.RelatedEvidenceIds, evidenceIds, "uncertainty");
    }

    private static void Range(
        long start,
        long end,
        NarrativeInterpretationInputV1 input,
        string name,
        IReadOnlyCollection<string> referencedEvidenceIds)
    {
        if (start > end || start < input.Window.RequestedStartTimestampMs || end > input.Window.RequestedEndTimestampMs)
            throw new NarrativeOutputValidationException(
                $"The {name} range {start}–{end} is outside requested window " +
                $"{input.Window.RequestedStartTimestampMs}–{input.Window.RequestedEndTimestampMs}; " +
                $"evidence IDs: [{string.Join(", ", referencedEvidenceIds)}].");
    }

    private static IReadOnlyCollection<string> ThreadEvidenceIds(
        NarrativeThreadV1 thread,
        IReadOnlySet<string> knownEvidenceIds)
    {
        var claims = new List<NarrativeClaimV1?> { thread.Summary };
        if (thread.SignificantDevelopments is not null) claims.AddRange(thread.SignificantDevelopments);
        return ClaimsEvidenceIds(claims, knownEvidenceIds);
    }

    private static IReadOnlyCollection<string> MomentEvidenceIds(
        NarrativeInvestigationMomentV1 moment,
        IReadOnlySet<string> knownEvidenceIds) =>
        (moment.WhyItStandsOut?.EvidenceIds ?? [])
            .Concat(moment.Uncertainties?.SelectMany(value => value?.RelatedEvidenceIds ?? []) ?? [])
            .Where(knownEvidenceIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyCollection<string> ClaimsEvidenceIds(
        IEnumerable<NarrativeClaimV1?> claims,
        IReadOnlySet<string> knownEvidenceIds) => claims
        .Where(value => value is not null)
        .SelectMany(value => value!.EvidenceIds ?? [])
        .Where(knownEvidenceIds.Contains)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static void References(IEnumerable<string> values, IReadOnlySet<string> evidenceIds, string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !evidenceIds.Contains(value))
                throw new NarrativeOutputValidationException($"The {name} references unknown evidence '{value}'.");
            if (!seen.Add(value))
                throw new NarrativeOutputValidationException($"The {name} repeats evidence reference '{value}'.");
        }
    }

    private static void Text(string value, int minimum, int maximum, string name)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < minimum || length > maximum)
            throw new NarrativeOutputValidationException($"The {name} must contain between {minimum} and {maximum} characters.");
    }

    private static void ValidateCount<T>(IReadOnlyCollection<T>? values, int minimum, int maximum, string name)
    {
        var count = values?.Count ?? -1;
        if (count < minimum || count > maximum)
            throw new NarrativeOutputValidationException($"The narrative must contain between {minimum} and {maximum} {name}.");
    }
}
