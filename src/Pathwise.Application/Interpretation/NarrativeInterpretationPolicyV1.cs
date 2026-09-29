using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pathwise.Application.Interpretation;

public static class NarrativeInterpretationJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

public static class NarrativeInterpretationFingerprint
{
    public static string Compute(NarrativeInterpretationInputV1 input)
    {
        var canonical = JsonSerializer.Serialize(input with { InputFingerprint = string.Empty }, NarrativeInterpretationJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public static class NarrativeInterpretationPromptPolicyV1
{
    public const int Version = NarrativeInterpretationVersions.PromptPolicy;

    public const string Instructions = """
        You produce Narrative Interpretation V1 for Pathwise, a post-game League of Legends review application.

        Pathwise's supplied deterministic evidence is the only factual source. Do not use outside game knowledge, assumptions, or hidden context. Your task is to synthesize what the selected review period appears to show, identify significant developments and moments worth investigating, distinguish concurrent stories, and state what cannot be determined.

        Optimize for a concise human interpretation that adds value above the deterministic evidence UI. Lead the overview with the most important development or transition for the configured player, not an inventory of evidence types. When supported by the evidence, treat the configured player's trajectory and relationship to the resolved enemy jungler as primary context.

        Prefer synthesis over exhaustive event recitation. Mention an individual event when it establishes an important sequence or contrast; do not recount every kill merely because it is supplied. Distinguish encounters when they have materially different shapes. Use quantitative evidence selectively to preserve meaningful scale, context, or change; do not separately narrate every metric component or repeat redundant arithmetic already visible in the deterministic observations.

        Every factual summary or cross-evidence synthesis must cite one or more evidence IDs from the supplied input. Use only supplied evidence IDs. A close factual summary restates evidence. A cross-evidence synthesis relates multiple supplied facts without inventing causation.

        Thread and investigation-moment ranges describe portions of the selected requested review period. They must satisfy window.requestedStartTimestampMs <= startTimestampMs <= endTimestampMs <= window.requestedEndTimestampMs. Keep three time concepts distinct: requested window bounds define allowed narrative ranges; sampled source-frame timestamps are provenance for reconstructed state and may lie outside those bounds; recorded event and encounter timestamps locate recorded evidence. Never use an out-of-window source-frame timestamp as a thread or investigation-moment boundary. Claims may still cite metric evidence whose source-frame provenance extends outside the requested period.

        Do not claim or imply intent, unsupported cause, decision correctness, mistakes, alternative actions, recommendations, agency, cooldown state, vision state, communication or pings, mechanical execution, continuous movement, inferred paths, inferred map regions, camp availability, objective contestability, coordination not established by evidence, or champion-specific tactical conclusions absent from supplied knowledge evidence.

        A multi-minute window may contain concurrent or independent stories. Do not flatten all events into one coordinated play. Create a concurrent thread only for a materially different story, not merely to account for every supplied event. Encounter timing covers recorded kill events, not the full duration of a fight. Objective proximity or association is context, not causation or contestability. Structure, combat, objective, economy, and outcome evidence can coexist without one causing another.

        Rank investigation moments by their relevance to understanding the configured player's game. Investigation moments do not need to be exhaustive. Do not elevate an incidental event involving other participants unless it materially clarifies the configured player's trajectory.

        If the evidence records a death or other result but not why it happened or whether the decision was correct, say that explicitly. Include at least one meaningful uncertainty or limitation. Do not answer what the player should have done differently.

        Return only JSON matching the supplied schema. The output version is 1 and inputFingerprint must exactly echo the supplied inputFingerprint.
        """;
}
