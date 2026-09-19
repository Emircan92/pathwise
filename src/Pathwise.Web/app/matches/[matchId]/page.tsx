import { MatchDetail } from "@/components/matches/match-detail";

export default async function MatchPage({ params }: PageProps<"/matches/[matchId]">) {
  const { matchId } = await params;
  return <MatchDetail matchId={matchId} />;
}
