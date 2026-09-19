"""Export and validate the approved reconstruction fixture from a live SQLite view."""

from __future__ import annotations

import argparse
import copy
import datetime as dt
import json
import sqlite3
from pathlib import Path
from typing import Any


IDENTITY_KEYS = {
    "puuid",
    "summonerId",
    "summonerName",
    "riotIdGameName",
    "riotIdTagline",
    "accountId",
}
ABSOLUTE_TIME_KEYS = {"gameCreation", "gameStartTimestamp", "gameEndTimestamp", "realTimestamp"}
APPROVED_CHANGED_KEYS = IDENTITY_KEYS | ABSOLUTE_TIME_KEYS | {"matchId", "gameId", "gameName"}
SYNTHETIC_MATCH_ID = "EUW1_1"
SYNTHETIC_GAME_ID = 1
SYNTHETIC_START_MS = int(dt.datetime(2026, 9, 1, tzinfo=dt.timezone.utc).timestamp() * 1000)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("database", type=Path)
    parser.add_argument("source_match_id")
    parser.add_argument("output_directory", type=Path)
    return parser.parse_args()


def load_payloads(database: Path, match_id: str) -> dict[str, dict[str, Any]]:
    connection = sqlite3.connect(database)
    try:
        connection.execute("BEGIN")
        rows = connection.execute(
            "SELECT Kind, RawJson FROM MatchPayloads WHERE MatchId = ? AND State = 'Stored'",
            (match_id,),
        ).fetchall()
        connection.commit()
    finally:
        connection.close()

    payloads = {kind: json.loads(raw_json) for kind, raw_json in rows}
    if set(payloads) != {"Match", "Timeline"}:
        raise RuntimeError("Both stored Match and Timeline payloads are required.")
    return payloads


def replace_recursive(value: Any, key: str, replacement: Any) -> None:
    if isinstance(value, dict):
        for child_key, child_value in value.items():
            if child_key == key:
                value[child_key] = replacement(child_value) if callable(replacement) else replacement
            else:
                replace_recursive(child_value, key, replacement)
    elif isinstance(value, list):
        for child in value:
            replace_recursive(child, key, replacement)


def sanitize(payloads: dict[str, dict[str, Any]], source_match_id: str) -> dict[str, dict[str, Any]]:
    sanitized = copy.deepcopy(payloads)
    match = sanitized["Match"]
    timeline = sanitized["Timeline"]
    original_match = payloads["Match"]
    participants = original_match["info"]["participants"]
    puuid_to_participant = {participant["puuid"]: participant["participantId"] for participant in participants}
    if len(puuid_to_participant) != len(participants):
        raise RuntimeError("Participant PUUIDs are not unique.")

    original_start = original_match["info"]["gameStartTimestamp"]
    absolute_offset = SYNTHETIC_START_MS - original_start
    for payload in sanitized.values():
        replace_recursive(payload, "matchId", SYNTHETIC_MATCH_ID)
        replace_recursive(payload, "gameId", SYNTHETIC_GAME_ID)
        for key in ABSOLUTE_TIME_KEYS:
            replace_recursive(payload, key, lambda value, offset=absolute_offset: value + offset)

    match["info"]["gameName"] = "synthetic-reconstruction-match-1"
    for participant in match["info"]["participants"]:
        participant_id = participant["participantId"]
        participant["puuid"] = f"participant-{participant_id}-puuid"
        if "summonerId" in participant:
            participant["summonerId"] = f"participant-{participant_id}-summoner"
        if "accountId" in participant:
            participant["accountId"] = f"participant-{participant_id}-account"
        if "summonerName" in participant:
            participant["summonerName"] = f"Synthetic Player {participant_id}"
        if "riotIdGameName" in participant:
            participant["riotIdGameName"] = f"Synthetic Player {participant_id}"
        if "riotIdTagline" in participant:
            participant["riotIdTagline"] = f"P{participant_id:02d}"

    for payload in (match, timeline):
        payload["metadata"]["participants"] = [
            f"participant-{puuid_to_participant[puuid]}-puuid"
            for puuid in payload["metadata"]["participants"]
        ]
    for participant in timeline["info"]["participants"]:
        participant_id = participant["participantId"]
        participant["puuid"] = f"participant-{participant_id}-puuid"

    serialized = json.dumps(sanitized, ensure_ascii=False)
    if source_match_id in serialized:
        raise RuntimeError("The original match identifier remains in the sanitized fixture.")
    original_identity_values = collect_values_for_keys(payloads, {"puuid", "summonerId", "accountId"})
    retained_identities = [value for value in original_identity_values if value and value in serialized]
    if retained_identities:
        raise RuntimeError("An original player/account identity remains in the sanitized fixture.")
    return sanitized


def collect_values_for_keys(value: Any, keys: set[str]) -> set[str]:
    result: set[str] = set()
    if isinstance(value, dict):
        for key, child in value.items():
            if key in keys and isinstance(child, str):
                result.add(child)
            result.update(collect_values_for_keys(child, keys))
    elif isinstance(value, list):
        for child in value:
            result.update(collect_values_for_keys(child, keys))
    return result


def differences(before: Any, after: Any, path: tuple[str, ...] = ()) -> list[tuple[tuple[str, ...], Any, Any]]:
    if type(before) is not type(after):
        return [(path, before, after)]
    if isinstance(before, dict):
        if before.keys() != after.keys():
            return [(path, before, after)]
        result: list[tuple[tuple[str, ...], Any, Any]] = []
        for key in before:
            result.extend(differences(before[key], after[key], (*path, key)))
        return result
    if isinstance(before, list):
        if len(before) != len(after):
            return [(path, before, after)]
        result = []
        for index, (before_item, after_item) in enumerate(zip(before, after, strict=True)):
            result.extend(differences(before_item, after_item, (*path, str(index))))
        return result
    return [] if before == after else [(path, before, after)]


def validate(original: dict[str, dict[str, Any]], sanitized: dict[str, dict[str, Any]]) -> list[tuple[tuple[str, ...], Any, Any]]:
    changes = differences(original, sanitized)
    unexpected = [change for change in changes if not is_approved_change(change[0])]
    if unexpected:
        raise RuntimeError(f"Unexpected sanitized fields changed: {[change[0] for change in unexpected[:10]]}")

    match = sanitized["Match"]
    timeline = sanitized["Timeline"]
    if match["metadata"]["matchId"] != SYNTHETIC_MATCH_ID or timeline["metadata"]["matchId"] != SYNTHETIC_MATCH_ID:
        raise RuntimeError("Synthetic match linkage is invalid.")
    if match["info"]["gameStartTimestamp"] != SYNTHETIC_START_MS:
        raise RuntimeError("Synthetic game start is invalid.")
    expected_puuids = [participant["puuid"] for participant in match["info"]["participants"]]
    if match["metadata"]["participants"] != expected_puuids or timeline["metadata"]["participants"] != expected_puuids:
        raise RuntimeError("Metadata participant linkage is invalid.")
    if [participant["puuid"] for participant in timeline["info"]["participants"]] != expected_puuids:
        raise RuntimeError("Timeline participant linkage is invalid.")

    original_frames = original["Timeline"]["info"]["frames"]
    sanitized_frames = timeline["info"]["frames"]
    if [frame["timestamp"] for frame in original_frames] != [frame["timestamp"] for frame in sanitized_frames]:
        raise RuntimeError("Relative frame timing changed.")
    original_events = [(event["timestamp"], event.get("actualStartTime")) for frame in original_frames for event in frame["events"]]
    sanitized_events = [(event["timestamp"], event.get("actualStartTime")) for frame in sanitized_frames for event in frame["events"]]
    if original_events != sanitized_events:
        raise RuntimeError("Relative event or scheduled objective timing changed.")
    return changes


def is_approved_change(path: tuple[str, ...]) -> bool:
    if path[-1] in APPROVED_CHANGED_KEYS:
        return True
    return len(path) >= 3 and path[-1].isdigit() and path[-2] == "participants" and path[-3] == "metadata"


def main() -> None:
    args = parse_args()
    original = load_payloads(args.database, args.source_match_id)
    sanitized = sanitize(original, args.source_match_id)
    changes = validate(original, sanitized)
    args.output_directory.mkdir(parents=True, exist_ok=True)
    (args.output_directory / "match.json").write_text(json.dumps(sanitized["Match"], indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (args.output_directory / "timeline.json").write_text(json.dumps(sanitized["Timeline"], indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Exported validated fixture with {len(changes)} approved field changes.")


if __name__ == "__main__":
    main()
