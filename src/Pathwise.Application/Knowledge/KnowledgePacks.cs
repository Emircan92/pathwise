using Pathwise.Domain.Knowledge;

namespace Pathwise.Application.Knowledge;

public static class KnowledgePacks
{
    public static KnowledgePack SummonersRiftObjectiveInitialSpawns { get; } = new(
        "summoners-rift-objective-initial-spawns",
        1,
        new(26, 18),
        11,
        420,
        "The objective initial-spawn mechanics were reviewed through public patch 26.18 using the accepted source audit and direct Riot mechanic documentation.",
        [
            new(
                "elemental-dragon.initial-spawn",
                KnowledgeObjective.ElementalDragon,
                300_000,
                [new("Patch 9.23 notes", new("https://www.leagueoflegends.com/en-us/news/game-updates/patch-9-23-notes/"))]),
            new(
                "baron-nashor.initial-spawn",
                KnowledgeObjective.BaronNashor,
                1_200_000,
                [new("Patch 26.1 notes", new("https://www.leagueoflegends.com/en-us/news/game-updates/patch-26-1-notes/"))])
        ]);

    public static KnowledgePack? For(PublicPatch? patch) =>
        patch == SummonersRiftObjectiveInitialSpawns.Patch
            ? SummonersRiftObjectiveInitialSpawns
            : null;
}
