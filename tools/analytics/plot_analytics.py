"""Charts and tables for Cross Section analytics from an offline Firebase JSON export.

Reads schema v3 (/v3/<build>/<player>/<session>/<event>) and the Alpha schema v2 (/events/<push key>),
which is converted to v3 names. Never contacts Firebase. See docs/analytics/v3-schema-proposal.md.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
import statistics
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path

CUTS = ("cut_success", "cut_miss", "cut_too_small", "cut_cancel")
MECHANICS = ("use_cut", "use_view_switch", "use_color_shift", "use_switch_piece", "use_camera")
FLOW = ("level_start", "segment_start", "checkpoint_reached", "level_complete", "restart")
EVENTS = (*FLOW, *CUTS, *MECHANICS)
FIELDS = ("schema_version", "event", "event_id", "player_id", "session_id", "sequence", "build",
          "server_time", "is_editor", "level", "level_title", "scene", "segment", "segment_count",
          "segment_name", "visit_id", "attempt", "t_attempt", "t_segment", "t_level", "level_restarts",
          "cuts_left", "cuts_budget", "kept_ratio", "reason", "first_use")
CSV_FIELDS = (*FIELDS, "date_utc", "source_schema", "derived")
HEX_ID = re.compile(r"[a-f0-9]{32}\Z")
LEVEL_ID = re.compile(r"[a-z0-9_]{1,32}\Z")
BUILD_ID = re.compile(r"[A-Za-z0-9_-]{1,64}\Z")
PUSH_CHARS = "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz"

LEVEL_ORDER = ("tutorial", "level01", "level02")
MECHANIC_LABELS = {"use_cut": "C: Cut", "use_view_switch": "Tab: 2D/3D", "use_color_shift": "Shift: Color",
                   "use_switch_piece": "F: Swap piece", "use_camera": "Q/E: Turn"}
CUT_LABELS = {"cut_success": "Success", "cut_miss": "Miss", "cut_too_small": "Too small", "cut_cancel": "Cancel"}

# Alpha (schema v2) used Unity scene names and LevelCatalog save IDs. Map them to the menu names.
V2_LEVELS = {"Tutorial": ("tutorial", "Tutorial"), "NewLevel": ("level01", "New Level"),
             "GlassParadox": ("level02", "Glass Paradox")}
V2_SEGMENT_TITLES = {"Level1": "Cut to fit", "Level2": "Leave a little behind", "Level3": "A different angle",
                     "ColorShift": "Shift your color", "FirstBridge": "First bridge",
                     "BuildingBridge": "Building Bridge", "NewLevel": "New Level", "GlassParadox": "Glass Paradox"}

# What each tutorial segment is meant to teach, from the LevelCatalog mechanic tags. Edit when lessons change.
TAUGHT = {
    "Cut to fit": ("use_cut", "use_view_switch"),               # CUTTING + PERSPECTIVE
    "Leave a little behind": ("use_cut", "use_switch_piece"),   # WEIGHT + GLASS + SWITCHING
    "First bridge": ("use_cut", "use_color_shift"),             # AIM + COLOR
    "Building Bridge": ("use_cut", "use_view_switch"),          # HIDDEN PATHS + CUTTING + AIM
    "A different angle": ("use_cut", "use_view_switch", "use_camera"),  # Alpha only
    "Shift your color": ("use_color_shift",),                   # Alpha only
}

# Alpha -> Beta tutorial segments. Only "same" rows may be compared segment by segment.
SEGMENT_MAPPING = (
    ("1 Cut to fit", "1 Cut to fit", "same title, content changed"),
    ("2 Leave a little behind", "2 Leave a little behind", "same title, content changed"),
    ("3 A different angle", "-", "Alpha only"),
    ("4 Shift your color", "-", "Alpha only"),
    ("-", "3 First bridge", "Beta only"),
    ("5 Building Bridge", "4 Building Bridge", "to be confirmed"),
)

# Reference palette (dataviz skill, light surface). Categorical slots in fixed order; blue ordinal ramp.
SURFACE, INK, INK_2, GRID = "#fcfcfb", "#0b0b0b", "#52514e", "#e6e5e0"
SERIES = ("#2a78d6", "#eb6834", "#1baf7a", "#eda100")
BLUE_RAMP = ("#86b6ef", "#6da7ec", "#5598e7", "#3987e5", "#2a78d6", "#256abf", "#1c5cab", "#184f95")
HEAT = ("#f4f7fb", "#cde2fb", "#9ec5f4", "#6da7ec", "#3987e5", "#256abf", "#184f95", "#0d366b")


# ---------------------------------------------------------------- reading

def number(value):
    return type(value) in (int, float) and math.isfinite(value)


def integer(value, minimum=0):
    return type(value) is int and value >= minimum


def short_text(value, limit=128, minimum=1):
    return isinstance(value, str) and minimum <= len(value) <= limit


def push_key_ms(key):
    """Firebase push keys start with an 8-character server timestamp (ms)."""
    if not isinstance(key, str) or len(key) != 20 or any(c not in PUSH_CHARS for c in key[:8]):
        return None
    value = 0
    for char in key[:8]:
        value = value * 64 + PUSH_CHARS.index(char)
    return value


def date_utc(ms):
    return datetime.fromtimestamp(ms / 1000, timezone.utc).strftime("%Y-%m-%d %H:%M") if number(ms) else ""


def walk_events(payload, path=()):
    """Yield (path, row) for every event-like dict in a root, /v3, /v3/<build>, /events or list export."""
    if isinstance(payload, dict):
        if "event" in payload and "event_id" in payload:
            yield path, payload
            return
        for key, child in payload.items():
            yield from walk_events(child, (*path, str(key)))
    elif isinstance(payload, list):
        for index, child in enumerate(payload):
            yield from walk_events(child, (*path, str(index)))


def export_rows(payload):
    return [row for _, row in walk_events(payload)]


def valid_v3(row):
    if not all(k in row for k in FIELDS) or len(row) != len(FIELDS):
        return False
    if row["schema_version"] != 3 or row["event"] not in EVENTS:
        return False
    if not all(isinstance(row[k], str) and HEX_ID.fullmatch(row[k])
               for k in ("event_id", "player_id", "session_id", "visit_id")):
        return False
    if not (isinstance(row["build"], str) and BUILD_ID.fullmatch(row["build"])
            and isinstance(row["level"], str) and LEVEL_ID.fullmatch(row["level"])
            and all(short_text(row[k]) for k in ("level_title", "scene", "segment_name"))
            and short_text(row["reason"], 64, 0)):
        return False
    if not (integer(row["sequence"], 1) and integer(row["segment_count"], 1) and integer(row["segment"], 1)
            and row["segment"] <= row["segment_count"] and integer(row["attempt"], 1)
            and integer(row["level_restarts"]) and integer(row["cuts_budget"])
            and integer(row["cuts_left"]) and row["cuts_left"] <= row["cuts_budget"]):
        return False
    if type(row["is_editor"]) is not bool or type(row["first_use"]) is not bool or not number(row["server_time"]):
        return False
    t = row["t_attempt"]
    if not (number(t) and t >= 0 and number(row["t_segment"]) and row["t_segment"] >= t
            and number(row["t_level"]) and row["t_level"] >= t and number(row["kept_ratio"])):
        return False
    if row["event"] == "cut_success":
        return 0 <= row["kept_ratio"] <= 1
    return row["kept_ratio"] == -1


V2_BASE = ("schema_version", "event_id", "session_id", "sequence", "build", "scene", "room", "event",
           "value", "t_room", "is_editor", "room_visit_id", "room_index", "room_attempt", "t_visit",
           "cuts_left", "cuts_budget", "reason", "first_use")
V2_EVENTS = (*CUTS, *MECHANICS, "room_enter", "room_clear", "restart")


def valid_v2(row):
    if not all(k in row for k in V2_BASE) or row["schema_version"] != 2 or row["event"] not in V2_EVENTS:
        return False
    if not all(isinstance(row[k], str) and HEX_ID.fullmatch(row[k]) for k in ("event_id", "session_id", "room_visit_id")):
        return False
    if not (short_text(row["build"], 64) and short_text(row["scene"]) and short_text(row["room"])
            and short_text(row["reason"], 64, 0)):
        return False
    if not (integer(row["sequence"], 1) and integer(row["room_index"]) and integer(row["room_attempt"], 1)
            and integer(row["cuts_budget"]) and integer(row["cuts_left"]) and row["cuts_left"] <= row["cuts_budget"]):
        return False
    if type(row["is_editor"]) is not bool or type(row["first_use"]) is not bool:
        return False
    if not (number(row["value"]) and number(row["t_room"]) and row["t_room"] >= 0
            and number(row["t_visit"]) and row["t_visit"] >= row["t_room"]):
        return False
    event, value = row["event"], row["value"]
    if event == "cut_success":
        return 0 <= value <= 1
    if event in (*MECHANICS, "restart"):
        return value == 1
    if event == "room_clear":
        return value == row["cuts_left"]
    return value == -1


def from_v2(row, key):
    """Rename a v2 row to v3 fields. segment_count, level_start and t_level are filled in later per build."""
    level, title = V2_LEVELS.get(row["scene"], (row["scene"].lower(), row["scene"]))
    event = {"room_enter": "segment_start", "room_clear": "_segment_exit"}.get(row["event"], row["event"])
    return dict(schema_version=3, event=event, event_id=row["event_id"], player_id=row["session_id"],
                session_id=row["session_id"], sequence=row["sequence"], build=row["build"],
                server_time=push_key_ms(key), is_editor=row["is_editor"], level=level, level_title=title,
                scene=row["scene"], segment=row["room_index"] + 1, segment_count=None,
                segment_name=V2_SEGMENT_TITLES.get(row["room"], row["room"]), visit_id=row["room_visit_id"],
                attempt=row["room_attempt"], t_attempt=row["t_room"], t_segment=row["t_visit"], t_level=None,
                level_restarts=None, cuts_left=row["cuts_left"], cuts_budget=row["cuts_budget"],
                kept_ratio=row["value"] if row["event"] == "cut_success" else -1,
                reason=row["reason"], first_use=row["first_use"], source_schema=2, derived=False)


def complete_v2(rows, stats):
    """v2 has no level events: infer segment_count, level_start, checkpoint/complete, t_level, level_restarts."""
    counts = defaultdict(int)
    for row in rows:
        counts[row["level"]] = max(counts[row["level"]], row["segment"])
    for row in rows:
        row["segment_count"] = counts[row["level"]]
        if row["event"] == "_segment_exit":
            row["event"] = "level_complete" if row["segment"] == row["segment_count"] else "checkpoint_reached"
    output = []
    by_session = defaultdict(list)
    for row in rows:
        by_session[row["session_id"]].append(row)
    for session_rows in by_session.values():
        session_rows.sort(key=lambda r: r["sequence"])
        level, seconds, restarts, continuing = None, 0.0, 0, False
        for row in session_rows:
            if row["event"] == "segment_start" and row["attempt"] == 1 and not (continuing and row["level"] == level):
                level, seconds, restarts = row["level"], 0.0, 0
                output.append(dict(row, event="level_start", event_id="derived-" + row["event_id"], derived=True,
                                   t_level=0.0, level_restarts=0))
                stats["derived_level_starts"] += 1
            if row["event"] == "segment_start":
                continuing = False
            if row["event"] == "restart":
                restarts += 1
            row["t_level"] = seconds + row["t_attempt"]
            row["level_restarts"] = restarts
            if row["event"] in ("restart", "checkpoint_reached"):
                seconds += row["t_attempt"]
            if row["event"] == "checkpoint_reached":
                continuing = True
            output.append(row)
    return output


def prepare(payload, build, include_editor=False):
    stats = Counter(input_rows=0, invalid_rows=0, excluded_build=0, excluded_editor=0, duplicate_rows=0,
                    conflicting_event_ids=0, conflicting_sequences=0, v2_rows=0, derived_level_starts=0)
    grouped = defaultdict(list)
    for path, row in walk_events(payload):
        stats["input_rows"] += 1
        version = row.get("schema_version") if isinstance(row, dict) else None
        if version == 3 and valid_v3(row):
            record = dict(row, source_schema=3, derived=False)
        elif version == 2 and valid_v2(row):
            record = from_v2(row, path[-1] if path else None)
        else:
            stats["invalid_rows"] += 1
            continue
        if record["build"] != build:
            stats["excluded_build"] += 1
            continue
        if record["is_editor"] and not include_editor:
            stats["excluded_editor"] += 1
            continue
        signature = json.dumps({k: v for k, v in row.items() if k != "server_time"}, sort_keys=True)
        grouped[record["event_id"]].append((signature, record))
    unique = []
    for copies in grouped.values():
        if len({signature for signature, _ in copies}) != 1:
            stats["conflicting_event_ids"] += 1
            continue  # Never arbitrarily choose between contradictory copies.
        stats["duplicate_rows"] += len(copies) - 1
        times = [r["server_time"] for _, r in copies if number(r["server_time"])]
        unique.append(dict(copies[0][1], server_time=min(times) if times else None))
    sequences = Counter((r["session_id"], r["sequence"]) for r in unique)
    stats["conflicting_sequences"] = sum(n > 1 for n in sequences.values())
    unique = [r for r in unique if sequences[r["session_id"], r["sequence"]] == 1]
    v2 = [r for r in unique if r["source_schema"] == 2]
    stats["v2_rows"] = len(v2)
    unique = [r for r in unique if r["source_schema"] == 3] + (complete_v2(v2, stats) if v2 else [])
    unique.sort(key=lambda r: (r["session_id"], r["sequence"], r["event"] != "level_start"))
    for row in unique:
        row["date_utc"] = date_utc(row["server_time"])
    stats["included_events"] = len(unique)
    stats["included_sessions"] = len({r["session_id"] for r in unique})
    stats["included_players"] = len({r["player_id"] for r in unique})
    stats["included_editor_events"] = sum(r["is_editor"] for r in unique)
    stats["players_are_sessions"] = bool(v2) and len(v2) == len([r for r in unique if not r["derived"]])
    times = [r["server_time"] for r in unique if number(r["server_time"])]
    stats["first_event_utc"] = date_utc(min(times)) if times else ""
    stats["last_event_utc"] = date_utc(max(times)) if times else ""
    return unique, dict(stats)


# ---------------------------------------------------------------- analysis

def mean(values):
    return statistics.mean(values) if values else None


def median(values):
    return statistics.median(values) if values else None


def ratio(part, whole):
    return part / whole if whole else None


def level_sort_key(level):
    return (LEVEL_ORDER.index(level) if level in LEVEL_ORDER else len(LEVEL_ORDER), level)


def build_runs(events, quality):
    """A run = one level_start and everything after it in the session until the next level_start."""
    runs, current = [], {}
    for row in events:
        session = row["session_id"]
        if row["event"] == "level_start":
            current[session] = dict(player=row["player_id"], session=session, level=row["level"],
                                    level_title=row["level_title"], segment_count=row["segment_count"],
                                    start_segment=row["segment"], checkpoints=set(), complete=None,
                                    restarts=Counter(), events=[], last_t_level=0.0)
            runs.append(current[session])
            continue
        run = current.get(session)
        if run is None or run["level"] != row["level"]:
            quality["events_outside_a_run"] += 1
            continue
        run["events"].append(row)
        run["last_t_level"] = max(run["last_t_level"], row["t_level"])
        if row["event"] == "checkpoint_reached":
            run["checkpoints"].add(row["segment"])
        elif row["event"] == "level_complete" and run["complete"] is None:
            run["complete"] = row
        elif row["event"] == "restart":
            run["restarts"][row["reason"] or "other"] += 1
    for run in runs:
        exits = [*run["checkpoints"], *( [run["segment_count"]] if run["complete"] else [])]
        run["furthest"] = max(exits) if exits else 0  # 0 = no checkpoint reached
    return runs


def step_label(step, count, names):
    if step == 0:
        return "Start"
    if step >= count:
        return "Complete"
    return f"Checkpoint {step} ({names.get(step, f'segment {step}')})"


def analyze(events):
    quality = Counter(events_outside_a_run=0, visits_without_start=0)
    runs = build_runs(events, quality)
    levels = sorted({r["level"] for r in events}, key=level_sort_key)
    names = defaultdict(dict)   # level -> segment -> name
    titles, counts = {}, {}
    for row in events:
        names[row["level"]][row["segment"]] = row["segment_name"]
        titles[row["level"]], counts[row["level"]] = row["level_title"], row["segment_count"]

    visits = defaultdict(list)
    for row in events:
        if row["event"] != "level_start":
            visits[row["session_id"], row["visit_id"]].append(row)
    visit_rows = []
    for records in visits.values():
        start = next((r for r in records if r["event"] == "segment_start"), None)
        if start is None:
            quality["visits_without_start"] += 1
            continue
        exit_row = next((r for r in records if r["event"] in ("checkpoint_reached", "level_complete")), None)
        visit_rows.append(dict(level=start["level"], segment=start["segment"], player=start["player_id"],
                               events=records, exit=exit_row))

    tables = {k: [] for k in ("level_summary", "funnel", "segment_difficulty", "cut_outcomes", "mechanics", "players")}

    for level in levels:
        level_runs = [r for r in runs if r["level"] == level]
        from_start = [r for r in level_runs if r["start_segment"] == 1]
        done = [r for r in from_start if r["complete"]]
        players_started = {r["player"] for r in from_start}
        players_done = {r["player"] for r in done}
        restarts = [sum(r["restarts"].values()) for r in from_start]
        tables["level_summary"].append(dict(
            level=level, level_title=titles[level], segment_count=counts[level],
            players_started=len(players_started), players_completed=len(players_done),
            player_completion_rate=ratio(len(players_done), len(players_started)),
            runs_from_start=len(from_start), runs_completed=len(done),
            run_completion_rate=ratio(len(done), len(from_start)),
            runs_started_mid_level=len(level_runs) - len(from_start),
            median_seconds_to_complete=median([r["complete"]["t_level"] for r in done]),
            mean_restarts_per_run=mean(restarts), median_restarts_per_run=median(restarts),
            manual_restarts=sum(r["restarts"]["manual"] for r in from_start),
            fall_restarts=sum(r["restarts"]["fall"] for r in from_start),
            median_cuts_left_at_complete=median([r["complete"]["cuts_left"] for r in done])))

        if counts[level] > 1:
            best = defaultdict(lambda: -1)
            for r in from_start:
                best[r["player"]] = max(best[r["player"]], r["furthest"])
            for step in range(counts[level] + 1):
                reached_runs = sum(r["furthest"] >= step for r in from_start)
                reached_players = sum(v >= step for v in best.values())
                stopped = sum(v == step for v in best.values()) if step < counts[level] else 0
                tables["funnel"].append(dict(level=level, step=step, label=step_label(step, counts[level], names[level]),
                                             players=reached_players, runs=reached_runs,
                                             players_share=ratio(reached_players, len(best)),
                                             players_stopped_here=stopped))

        for segment in sorted(names[level]):
            seg_visits = [v for v in visit_rows if v["level"] == level and v["segment"] == segment]
            seg_events = [r for v in seg_visits for r in v["events"]]
            exited = [v for v in seg_visits if v["exit"]]
            restarts_by = {reason: [sum(r["event"] == "restart" and r["reason"] == reason for r in v["events"])
                                    for v in seg_visits] for reason in ("manual", "fall")}
            outcomes = Counter(r["event"] for r in seg_events if r["event"] in CUTS)
            tables["segment_difficulty"].append(dict(
                level=level, segment=segment, segment_name=names[level][segment],
                visits=len(seg_visits), players=len({v["player"] for v in seg_visits}),
                visits_exited=len(exited), exit_rate=ratio(len(exited), len(seg_visits)),
                manual_restarts=sum(restarts_by["manual"]), fall_restarts=sum(restarts_by["fall"]),
                restarts_per_visit=mean([m + f for m, f in zip(restarts_by["manual"], restarts_by["fall"])]),
                median_seconds_to_exit=median([v["exit"]["t_segment"] for v in exited]),
                median_cuts_left_at_exit=median([v["exit"]["cuts_left"] for v in exited]),
                cut_attempts=sum(outcomes.values()),
                cut_failures=outcomes["cut_miss"] + outcomes["cut_too_small"] + outcomes["cut_cancel"],
                hardest=False))

        for segment in ["all", *sorted(names[level])]:
            selected = [r for r in events if r["level"] == level and (segment == "all" or r["segment"] == segment)]
            outcomes = Counter(r["event"] for r in selected if r["event"] in CUTS)
            total = sum(outcomes.values())
            ratios = [r["kept_ratio"] for r in selected if r["event"] == "cut_success"]
            tables["cut_outcomes"].append(dict(
                level=level, segment=segment, segment_name="(whole level)" if segment == "all" else names[level][segment],
                **{c: outcomes[c] for c in CUTS}, attempts=total, success_rate=ratio(outcomes["cut_success"], total),
                median_kept_ratio=median(ratios)))

        # Mechanics: per segment visit, and per run for the whole level (time from the start of the run).
        for segment in ["all", *sorted(names[level])]:
            if segment == "all":
                units = [r["events"] for r in level_runs]
                clock = "t_level"
            else:
                units = [v["events"] for v in visit_rows if v["level"] == level and v["segment"] == segment]
                clock = "t_segment"
            name = "(whole level)" if segment == "all" else names[level][segment]
            for mechanic in MECHANICS:
                uses = [[r for r in unit if r["event"] == mechanic] for unit in units]
                firsts = [min(r[clock] for r in rows) for rows in uses if rows]
                tables["mechanics"].append(dict(
                    level=level, segment=segment, segment_name=name, mechanic=mechanic,
                    taught=segment != "all" and counts[level] > 1 and mechanic in TAUGHT.get(name, ()),
                    units=len(units), unit="run" if segment == "all" else "segment visit",
                    units_using=sum(bool(rows) for rows in uses), uses=sum(map(len, uses)),
                    usage_rate=ratio(sum(bool(rows) for rows in uses), len(units)),
                    median_seconds_to_first_use=median(firsts)))

    multi = [r for r in tables["segment_difficulty"] if counts[r["level"]] > 1 and r["visits"]]
    for level in {r["level"] for r in multi}:
        candidates = [r for r in multi if r["level"] == level]
        hardest = max(candidates, key=lambda r: (1 - (r["exit_rate"] or 0), r["restarts_per_visit"] or 0))
        hardest["hardest"] = True

    by_player = defaultdict(list)
    for run in runs:
        by_player[run["player"], run["level"]].append(run)
    first_seen = {}
    for row in events:
        if number(row["server_time"]):
            first_seen[row["player_id"]] = min(first_seen.get(row["player_id"], row["server_time"]), row["server_time"])
    for (player, level), player_runs in sorted(by_player.items(), key=lambda kv: (kv[0][0], level_sort_key(kv[0][1]))):
        from_start = [r for r in player_runs if r["start_segment"] == 1]
        done = [r for r in from_start if r["complete"]]
        furthest = max((r["furthest"] for r in from_start), default=None)
        tables["players"].append(dict(
            player_id=player, level=level, sessions=len({r["session"] for r in player_runs}),
            runs=len(player_runs), runs_from_start=len(from_start),
            furthest=step_label(furthest, counts[level], names[level]) if furthest is not None else "(only mid-level starts)",
            completed=bool(done), best_seconds_to_complete=min((r["complete"]["t_level"] for r in done), default=None),
            restarts=sum(sum(r["restarts"].values()) for r in player_runs),
            first_seen_utc=date_utc(first_seen.get(player))))
    return tables, dict(quality)


# ---------------------------------------------------------------- output

def write_csv(path, rows, fieldnames=None):
    fieldnames = fieldnames or (list(rows[0]) if rows else [])
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def pct(value):
    return "N/A" if value is None else f"{100 * value:.0f}%"


def seconds(value):
    return "N/A" if value is None else f"{value:.0f}s"


def plot(tables, output, build, label):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import numpy as np
    from matplotlib.colors import ListedColormap
    from matplotlib.ticker import MaxNLocator, PercentFormatter

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 10, "text.color": INK,
                         "axes.labelcolor": INK_2, "xtick.color": INK_2, "ytick.color": INK,
                         "axes.edgecolor": GRID, "axes.spines.top": False, "axes.spines.right": False,
                         "axes.titleweight": "bold", "axes.titlesize": 11, "axes.titlelocation": "left",
                         "figure.facecolor": SURFACE, "axes.facecolor": SURFACE, "svg.fonttype": "none"})

    summary = {r["level"]: r for r in tables["level_summary"]}
    levels = [r["level"] for r in tables["level_summary"]]
    level_names = [f'{summary[l]["level_title"]}' for l in levels]
    segments = [r for r in tables["segment_difficulty"]]
    seg_names = [f'{summary[r["level"]]["level_title"]} · {r["segment"]} {r["segment_name"]}'
                 if summary[r["level"]]["segment_count"] > 1 else summary[r["level"]]["level_title"]
                 for r in segments]

    def finish(fig, name, title, note):
        fig.suptitle(f"{title}\n{label}  |  build: {build}", x=.01, ha="left", fontsize=13, fontweight="bold")
        fig.text(.01, .012, note, fontsize=8.5, color=INK_2)
        height = fig.get_size_inches()[1]  # keep a fixed ~1 inch for the title block whatever the figure height
        fig.tight_layout(rect=(0, .4 / height, 1, 1 - .95 / height))
        for suffix in ("png", "svg"):
            fig.savefig(output / f"{name}.{suffix}", dpi=200)
        plt.close(fig)

    def hbar_axis(ax, names):
        ax.set_yticks(range(len(names)), names)
        ax.set_ylim(len(names) - .5, -.5)
        ax.grid(axis="x", color=GRID, linewidth=.8)
        ax.set_axisbelow(True)
        ax.tick_params(axis="y", length=0)

    def stacked_cuts(ax, rows, names, title):
        left = np.zeros(len(rows))
        for color, event in zip(SERIES, CUTS):
            values = np.array([r[event] for r in rows], dtype=float)
            ax.barh(range(len(rows)), values, left=left, height=.55, color=color, label=CUT_LABELS[event],
                    edgecolor=SURFACE, linewidth=2)
            for i, (v, l) in enumerate(zip(values, left)):
                if v >= max(2, .08 * max(1, max(r["attempts"] for r in rows))):
                    ax.text(l + v / 2, i, f"{v:.0f}", ha="center", va="center", fontsize=8, color=INK)
            left += values
        for i, r in enumerate(rows):
            ax.text(left[i], i, f'  n={r["attempts"]} · {pct(r["success_rate"])} success', va="center", fontsize=8, color=INK_2)
        hbar_axis(ax, names)
        ax.set_xlim(0, max(1, left.max()) * 1.45)
        ax.xaxis.set_major_locator(MaxNLocator(integer=True))
        ax.set_xlabel("Cut attempts")
        ax.set_title(title)

    # Metric 1: cut outcomes, whole levels then segments of multi-segment levels.
    whole = [r for r in tables["cut_outcomes"] if r["segment"] == "all"]
    parts = [r for r in tables["cut_outcomes"] if r["segment"] != "all" and summary[r["level"]]["segment_count"] > 1]
    fig, axes = plt.subplots(1, 2 if parts else 1, figsize=(15 if parts else 8, max(4.2, 2.4 + .5 * max(len(whole), len(parts)))),
                             squeeze=False)
    stacked_cuts(axes[0][0], whole, [summary[r["level"]]["level_title"] for r in whole], "By level")
    if parts:
        stacked_cuts(axes[0][1], parts, [f'{r["segment"]} {r["segment_name"]}' for r in parts], "Tutorial by segment")
    handles, legend_labels = axes[0][0].get_legend_handles_labels()
    fig.legend(handles, legend_labels, loc="upper right", ncol=4, frameon=False, fontsize=9, bbox_to_anchor=(.99, .93))
    finish(fig, "metric1_cut_outcomes", "Metric 1 - Cut outcomes",
           "One attempt = one submitted cut line, or leaving cut mode before drawing (cancel). Counts by event_id after de-duplication.")

    # Metric 2: who finishes each level, and where tutorial players stop.
    funnel = [r for r in tables["funnel"] if r["level"] == "tutorial"] or tables["funnel"]
    fig, axes = plt.subplots(1, 2 if funnel else 1, figsize=(15 if funnel else 8, 4.8), squeeze=False)
    ax = axes[0][0]
    y = np.arange(len(levels))
    started = [summary[l]["players_started"] for l in levels]
    completed = [summary[l]["players_completed"] for l in levels]
    ax.barh(y, started, height=.6, color=BLUE_RAMP[0], label="Started from the beginning", edgecolor=SURFACE, linewidth=2)
    ax.barh(y, completed, height=.6, color=BLUE_RAMP[5], label="Completed", edgecolor=SURFACE, linewidth=2)
    for i, l in enumerate(levels):
        ax.text(started[i], i, f'  {completed[i]}/{started[i]} completed ({pct(summary[l]["player_completion_rate"])})',
                va="center", fontsize=9, color=INK)
    hbar_axis(ax, level_names)
    ax.set_xlim(0, max([1, *started]) * 1.6)
    ax.xaxis.set_major_locator(MaxNLocator(integer=True))
    ax.set_xlabel("Players (anonymous browser ID; Alpha data: sessions)")
    ax.set_title("Players who start and finish each level")
    ax.legend(frameon=False, loc="lower right", fontsize=8.5)
    if funnel:
        ax = axes[0][1]
        values = [r["players"] for r in funnel]
        colors = [BLUE_RAMP[min(len(BLUE_RAMP) - 1, 1 + i)] for i in range(len(funnel))]
        ax.barh(range(len(funnel)), values, height=.6, color=colors, edgecolor=SURFACE, linewidth=2)
        for i, r in enumerate(funnel):
            stop = f' · {r["players_stopped_here"]} stopped here' if r["players_stopped_here"] else ""
            ax.text(r["players"], i, f'  {r["players"]} ({pct(r["players_share"])}){stop}', va="center", fontsize=9, color=INK)
        hbar_axis(ax, [r["label"] for r in funnel])
        ax.set_xlim(0, max([1, *values]) * 1.7)
        ax.xaxis.set_major_locator(MaxNLocator(integer=True))
        ax.set_xlabel("Players who reached this point (best run that started at segment 1)")
        ax.set_title(f'{summary[funnel[0]["level"]]["level_title"]} funnel: where players stop')
    finish(fig, "metric2_progression", "Metric 2 - Level completion and the tutorial funnel",
           "Only runs that start at the beginning of a level count; runs started mid-level from the menu are listed in level_summary.csv.")

    # Metric 3: mechanic use per level (per run) and per tutorial segment (per visit). Outlined = taught there.
    rows = [r for r in tables["mechanics"] if r["segment"] == "all" or summary[r["level"]]["segment_count"] > 1]
    keys = list(dict.fromkeys((r["level"], r["segment"]) for r in rows))
    data = np.full((len(keys), len(MECHANICS)), np.nan)
    taught = np.zeros_like(data, dtype=bool)
    for r in rows:
        i, j = keys.index((r["level"], r["segment"])), MECHANICS.index(r["mechanic"])
        data[i, j] = np.nan if r["usage_rate"] is None else 100 * r["usage_rate"]
        taught[i, j] = r["taught"]
    row_names = [f'{summary[l]["level_title"]} (whole level)' if s == "all" else f'   {s} {next(r["segment_name"] for r in rows if r["level"] == l and r["segment"] == s)}'
                 for l, s in keys]
    fig, ax = plt.subplots(figsize=(11, max(4, 1.6 + .5 * len(keys))))
    im = ax.imshow(np.ma.masked_invalid(data), cmap=ListedColormap(HEAT), vmin=0, vmax=100, aspect="auto")
    ax.set_xticks(range(len(MECHANICS)), [MECHANIC_LABELS[m] for m in MECHANICS])
    ax.set_yticks(range(len(keys)), row_names)
    ax.tick_params(length=0)
    for side in ax.spines.values():
        side.set_visible(False)
    for i in range(len(keys)):
        for j in range(len(MECHANICS)):
            v = data[i, j]
            ax.text(j, i, "N/A" if not np.isfinite(v) else f"{v:.0f}%", ha="center", va="center", fontsize=9,
                    color=SURFACE if np.isfinite(v) and v >= 55 else INK)
            if taught[i, j]:
                ax.add_patch(plt.Rectangle((j - .47, i - .45), .94, .9, fill=False, edgecolor=INK, linewidth=1.6))
    bar = fig.colorbar(im, ax=ax, shrink=.75, format=PercentFormatter())
    bar.outline.set_visible(False)
    ax.set_title("Share that used each mechanic (outlined = what the segment teaches)")
    finish(fig, "metric3_mechanics", "Metric 3 - Mechanic use",
           "Whole-level rows: share of runs. Segment rows: share of segment visits; restarts stay inside the same visit. "
           "Time to first use is in mechanics.csv (never-used is missing, not 0 s).")

    # Metric 4: where players get stuck.
    fig, axes = plt.subplots(2, 2, figsize=(16, max(7.5, 3.5 + .9 * len(segments))))
    ax = axes[0][0]
    exit_rates = [100 * r["exit_rate"] if r["exit_rate"] is not None else np.nan for r in segments]
    ax.barh(range(len(segments)), exit_rates, height=.55, color=SERIES[0], edgecolor=SURFACE, linewidth=2)
    for i, r in enumerate(segments):
        tag = "  << hardest" if r["hardest"] else ""
        ax.text(min(exit_rates[i], 100) if np.isfinite(exit_rates[i]) else 0, i,
                f'  {r["visits_exited"]}/{r["visits"]} visits{tag}', va="center", fontsize=8.5, color=INK)
    hbar_axis(ax, seg_names)
    ax.set_xlim(0, 150)
    ax.set_xticks([0, 25, 50, 75, 100])
    ax.xaxis.set_major_formatter(PercentFormatter())
    ax.set_title("Visits that reached the segment's exit")
    ax = axes[0][1]
    manual = [r["manual_restarts"] for r in segments]
    fall = [r["fall_restarts"] for r in segments]
    ax.barh(range(len(segments)), manual, height=.55, color=SERIES[0], label="R (manual)", edgecolor=SURFACE, linewidth=2)
    ax.barh(range(len(segments)), fall, left=manual, height=.55, color=SERIES[1], label="Fell off", edgecolor=SURFACE, linewidth=2)
    for i, r in enumerate(segments):
        ax.text(manual[i] + fall[i], i, f'  {manual[i]} R + {fall[i]} fall · {r["restarts_per_visit"] or 0:.2f}/visit',
                va="center", fontsize=8.5, color=INK)
    hbar_axis(ax, seg_names)
    ax.set_xlim(0, max([1, *[m + f for m, f in zip(manual, fall)]]) * 1.8)
    ax.xaxis.set_major_locator(MaxNLocator(integer=True))
    ax.legend(frameon=False, loc="lower right", fontsize=8.5)
    ax.set_title("Restarts before reaching the exit")
    ax = axes[1][0]
    times = [r["median_seconds_to_exit"] if r["median_seconds_to_exit"] is not None else np.nan for r in segments]
    ax.barh(range(len(segments)), times, height=.55, color=SERIES[0], edgecolor=SURFACE, linewidth=2)
    for i, r in enumerate(segments):
        ax.text(times[i] if np.isfinite(times[i]) else 0, i, f'  {seconds(r["median_seconds_to_exit"])} · cuts left {r["median_cuts_left_at_exit"] if r["median_cuts_left_at_exit"] is not None else "N/A"}',
                va="center", fontsize=8.5, color=INK)
    hbar_axis(ax, seg_names)
    ax.set_xlim(0, max([1, *[t for t in times if np.isfinite(t)]]) * 1.6)
    ax.set_xlabel("Median seconds to the exit, including failed attempts (visits that reached it)")
    ax.set_title("Time per segment and cuts left at its exit")
    ax = axes[1][1]
    totals = [summary[l]["median_seconds_to_complete"] if summary[l]["median_seconds_to_complete"] is not None else np.nan for l in levels]
    ax.barh(range(len(levels)), totals, height=.55, color=SERIES[0], edgecolor=SURFACE, linewidth=2)
    for i, l in enumerate(levels):
        s = summary[l]
        ax.text(totals[i] if np.isfinite(totals[i]) else 0, i,
                f'  {seconds(s["median_seconds_to_complete"])} · {s["mean_restarts_per_run"] or 0:.1f} restarts/run · n={s["runs_completed"]}',
                va="center", fontsize=8.5, color=INK)
    hbar_axis(ax, level_names)
    ax.set_xlim(0, max([1, *[t for t in totals if np.isfinite(t)]]) * 1.7)
    ax.set_xlabel("Median seconds from level start to completion (completed runs from the beginning)")
    ax.set_title("Whole level: time to complete and restarts per run")
    finish(fig, "metric4_difficulty", "Metric 4 - Where players get stuck",
           "Times include cut-mode pauses, help panels and time away from the window. 'Hardest' = lowest exit rate, ties broken by restarts per visit.")


def plot_comparison(rows, output, current, baseline):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import numpy as np

    metrics = (("player_completion_rate", "Players who completed (from the beginning)", 100, "%"),
               ("median_seconds_to_complete", "Median seconds to complete", 1, "s"),
               ("mean_restarts_per_run", "Restarts per run", 1, ""))
    levels = list(dict.fromkeys(r["level_title"] for r in rows))
    fig, axes = plt.subplots(1, 3, figsize=(16, 4.6))
    y = np.arange(len(levels))
    for ax, (field, title, scale, unit) in zip(axes, metrics):
        for k, (build, color, offset) in enumerate(((baseline, SERIES[1], -.18), (current, SERIES[0], .18))):
            values = []
            for name in levels:
                row = next((r for r in rows if r["level_title"] == name and r["build"] == build), None)
                values.append(np.nan if row is None or row[field] is None else row[field] * scale)
            ax.barh(y + offset, values, height=.34, color=color, label=build, edgecolor=SURFACE, linewidth=2)
            for i, v in enumerate(values):
                ax.text(v if np.isfinite(v) else 0, i + offset, "  N/A" if not np.isfinite(v) else f"  {v:.0f}{unit}" if unit else f"  {v:.1f}",
                        va="center", fontsize=8.5, color=INK)
        ax.set_yticks(y, levels)
        ax.tick_params(axis="y", length=0)
        ax.set_ylim(len(levels) - .5, -.5)
        ax.grid(axis="x", color=GRID)
        ax.set_axisbelow(True)
        ax.set_title(title)
        ax.margins(x=.3)
    handles, labels = axes[0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="upper right", ncol=2, frameon=False, fontsize=9, bbox_to_anchor=(.99, .97))
    fig.suptitle(f"Level-level comparison: {baseline} vs {current}", x=.01, ha="left", fontsize=13, fontweight="bold")
    fig.text(.01, .015, "Tutorial content changed between builds: compare the tutorial as a whole; per-segment comparison uses segment_mapping.csv.",
             fontsize=8.5, color=INK_2)
    fig.tight_layout(rect=(0, .05, 1, .9))
    for suffix in ("png", "svg"):
        fig.savefig(output / f"comparison_levels.{suffix}", dpi=200)
    plt.close(fig)


# ---------------------------------------------------------------- demo

def demo_data():
    """Deterministic synthetic v3 export; never upload it or present it as playtest results."""
    build = "synthetic-demo"
    tutorial = ("Cut to fit", "Leave a little behind", "First bridge", "Building Bridge")
    budgets = (3, 2, 2, 1)
    tree = {}
    for player in range(1, 13):
        player_id = f"{player:08x}" + "a" * 24
        session_id = f"{player:08x}" + "b" * 24
        sequence = 0
        nodes = tree.setdefault("v3", {}).setdefault(build, {}).setdefault(f"p_{player_id[:8]}", {}) \
            .setdefault(f"20261110-{10 + player:02d}00-{session_id[:8]}", {})
        state = dict(visit=0, attempt=1, t0=0.0, prior=0.0, level_seconds=0.0, restarts=0)

        def add(event, level, title, scene, segment, count, name, clock, cuts, budget, kept=-1, reason="", first=False):
            nonlocal sequence
            sequence += 1
            t_attempt = clock - state["t0"]
            row = dict(schema_version=3, event=event, event_id=f"{player:08x}{sequence:024x}", player_id=player_id,
                       session_id=session_id, sequence=sequence, build=build, server_time=1793000000000 + player * 3600000 + int(clock * 1000),
                       is_editor=False, level=level, level_title=title, scene=scene, segment=segment, segment_count=count,
                       segment_name=name, visit_id=f"{player:08x}{state['visit']:024x}", attempt=state["attempt"],
                       t_attempt=round(t_attempt, 3), t_segment=round(state["prior"] + t_attempt, 3),
                       t_level=round(state["level_seconds"] + t_attempt, 3), level_restarts=state["restarts"],
                       cuts_left=cuts, cuts_budget=budget, kept_ratio=kept, reason=reason, first_use=first)
            nodes[f"{sequence:04d}_{event}"] = row

        def play(level, title, scene, segs, start_clock, stop_after=None):
            clock = start_clock
            state.update(level_seconds=0.0, restarts=0)
            for index, (name, budget) in enumerate(segs):
                segment, count = index + 1, len(segs)
                state.update(visit=state["visit"] + 1, attempt=1, t0=clock, prior=0.0)
                if index == 0:
                    add("level_start", level, title, scene, segment, count, name, clock, budget, budget)
                add("segment_start", level, title, scene, segment, count, name, clock, budget, budget)
                add("use_cut", level, title, scene, segment, count, name, clock + 3 + player % 4, budget, budget, reason="key_c", first=True)
                if (player + index) % 3 == 0:
                    add("cut_miss", level, title, scene, segment, count, name, clock + 6, budget, budget)
                if (player + index) % 2 == 0 and index != 2:
                    add("use_view_switch", level, title, scene, segment, count, name, clock + 7, budget, budget, reason="key_tab", first=True)
                if index in (2, 3) or level != "tutorial":
                    if player % 4:
                        add("use_color_shift", level, title, scene, segment, count, name, clock + 8, budget, budget, reason="key_shift", first=True)
                if index == 1 and player % 3:
                    add("use_switch_piece", level, title, scene, segment, count, name, clock + 12, budget - 1, budget, reason="key_f", first=True)
                stuck = (index == 3 and player % 3 == 0) or (index == 2 and player % 5 == 0)
                tries = 1 + (player + index) % 3 if index >= 2 else 1
                for attempt in range(tries):
                    if attempt:
                        state.update(attempt=state["attempt"] + 1, t0=clock)
                        add("segment_start", level, title, scene, segment, count, name, clock, budget, budget)
                    add("cut_success", level, title, scene, segment, count, name, clock + 10, budget - 1, budget, kept=round(.3 + .05 * player % .5, 3))
                    clock += 15 + 4 * player % 7 + 10 * index
                    if attempt < tries - 1 or stuck:
                        state["restarts"] += 1
                        add("restart", level, title, scene, segment, count, name, clock, budget - 1, budget,
                            reason="fall" if (player + attempt) % 2 else "manual")
                        state["prior"] += clock - state["t0"]
                        state["level_seconds"] += clock - state["t0"]
                if stuck:
                    return clock
                event = "level_complete" if segment == count else "checkpoint_reached"
                add(event, level, title, scene, segment, count, name, clock, budget - 1, budget)
                state["level_seconds"] += clock - state["t0"]
                if event == "level_complete" or segment == stop_after:
                    return clock  # stop_after: the player leaves after this checkpoint
            return clock

        clock = play("tutorial", "Tutorial", "Tutorial", list(zip(tutorial, budgets)), 0.0, stop_after=1 if player == 7 else None)
        if player % 2:
            clock = play("level01", "New Level", "NewLevel", [("New Level", 2)], clock + 5)
        if player % 4 == 1:
            play("level02", "Glass Paradox", "GlassParadox", [("Glass Paradox", 3)], clock + 5)
    return tree


# ---------------------------------------------------------------- report

def load(path):
    raw = path.read_bytes()
    return raw, json.loads(raw.decode("utf-8-sig"))


def report_lines(label, build, events, stats, tables, quality, baseline):
    summary = tables["level_summary"]
    lines = ["# Cross Section 数据报告", "", f"**{label}**", "", f"版本：`{build}`。",
             f"纳入 {len(events)} 条事件、{stats['included_players']} 位玩家、{stats['included_sessions']} 个 session"
             + ("（Alpha 数据没有玩家 ID，按 session 计）" if stats.get("players_are_sessions") else "（玩家 = 浏览器里的匿名 ID）") + "。",
             f"服务器时间范围（UTC）：{stats.get('first_event_utc') or 'N/A'} ~ {stats.get('last_event_utc') or 'N/A'}。", "",
             "## 关键结果", ""]
    for row in summary:
        lines.append(f"- **{row['level_title']}**：从头开始 {row['players_started']} 人，通关 {row['players_completed']} 人"
                     f"（{pct(row['player_completion_rate'])}）；通关用时中位数 {seconds(row['median_seconds_to_complete'])}，"
                     f"每次游玩平均重开 {row['mean_restarts_per_run'] or 0:.1f} 次。")
    for row in tables["segment_difficulty"]:
        if row["hardest"]:
            lines.append(f"- 最难的一段：**{row['level']} 第 {row['segment']} 段「{row['segment_name']}」**，"
                         f"到达出口 {row['visits_exited']}/{row['visits']} 次（{pct(row['exit_rate'])}），"
                         f"重开 {row['manual_restarts']} 次 R + {row['fall_restarts']} 次掉落。")
    stops = [r for r in tables["funnel"] if r["level"] == "tutorial" and r["players_stopped_here"]]
    if stops:
        worst = max(stops, key=lambda r: r["players_stopped_here"])
        lines.append(f"- Tutorial 流失最多的位置：停在「{worst['label']}」之后的有 {worst['players_stopped_here']} 人。")
    lines += ["", "## 图表", ""]
    for name, title in (("metric1_cut_outcomes", "指标 1：切割结果"), ("metric2_progression", "指标 2：通关与 Tutorial 漏斗"),
                        ("metric3_mechanics", "指标 3：机制使用"), ("metric4_difficulty", "指标 4：卡关信号")):
        lines += [f"### {title}", "", f"![{name}]({name}.png)", ""]
    if baseline:
        lines += ["### 与基线版本对比（关卡层面）", "", "![comparison_levels](comparison_levels.png)", "",
                  "Tutorial 内容在两个版本间有改动，只按整关比较；分段对应关系见 `segment_mapping.csv`：", "",
                  "| Alpha 段 | Beta 段 | 状态 |", "|---|---|---|"]
        lines += [f"| {a} | {b} | {s} |" for a, b, s in SEGMENT_MAPPING]
        lines.append("")
    lines += ["## 统计口径", "",
              "- 一次游玩（run）= 一个 `level_start` 到通关或离开该关；重开仍属同一次游玩。",
              "- 通关率只看从第 1 段开始的游玩；从菜单直接选中间某段开始的，单独计入 `runs_started_mid_level`。",
              "- 漏斗按玩家取最好的一次游玩；每一段按段游玩（visit）统计，重开不增加 visit 数。",
              "- 时间包含切割暂停、帮助面板和离开窗口的时间；未通关的游玩不计入用时。",
              "- 首次使用时间：整关按 `t_level`，分段按 `t_segment`；从未使用不记为 0 秒。",
              "- 「该段要教的机制」来自 LevelCatalog 的机制标签，定义在脚本的 `TAUGHT` 中。",
              "- 没有出口事件不能证明玩家放弃，也可能是关闭页面时数据丢失。",
              f"- 数据质量：{json.dumps(quality, ensure_ascii=False)}；过滤统计见 `summary.json`。", "",
              "## 文件", "",
              "`level_summary.csv`、`funnel.csv`、`segment_difficulty.csv`、`cut_outcomes.csv`、`mechanics.csv`、"
              "`players.csv`（每位玩家每关一行）、`events_clean.csv`（清洗后的全部事件）。", ""]
    return lines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--input", type=Path, help="Firebase export: database root, /v3, /v3/<build> or /events")
    source.add_argument("--demo", action="store_true", help="Generate clearly marked synthetic preview charts")
    parser.add_argument("--build", help="Exact build ID; required with --input to avoid mixing releases")
    parser.add_argument("--include-editor", action="store_true", help="Diagnostic reports only; includes Editor data")
    parser.add_argument("--baseline-input", type=Path, help="Export with an earlier build to compare level results against")
    parser.add_argument("--baseline-build", help="Build ID inside --baseline-input (for example alpha2-all-metrics)")
    parser.add_argument("--output", type=Path, help="Output folder (default analytics-output/demo or report)")
    args = parser.parse_args(argv)
    if args.input and not args.build:
        parser.error("--input requires --build (for example beta1-all-metrics)")
    if args.demo and args.build:
        parser.error("--demo always uses the synthetic-demo build")
    if bool(args.baseline_input) != bool(args.baseline_build):
        parser.error("--baseline-input and --baseline-build go together")
    build = "synthetic-demo" if args.demo else args.build
    try:
        raw, payload = load(args.input) if args.input else (None, demo_data())
        events, stats = prepare(payload, build, args.include_editor)
        if not events:
            raise ValueError("No matching valid events. Check the build ID and export. Editor rows are excluded by default; use --include-editor only for diagnostics.")
        tables, quality = analyze(events)
        baseline_rows = None
        if args.baseline_input:
            base_raw, base_payload = load(args.baseline_input)
            base_events, base_stats = prepare(base_payload, args.baseline_build, False)
            if not base_events:
                raise ValueError(f"No valid events for baseline build {args.baseline_build}.")
            base_tables, _ = analyze(base_events)
            baseline_rows = [dict(r, build=args.baseline_build) for r in base_tables["level_summary"]] + \
                            [dict(r, build=build) for r in tables["level_summary"]]
    except (OSError, ValueError) as error:
        parser.exit(2, f"Input error: {error}\n")
    synthetic = args.demo or build == "synthetic-demo"
    label = "SYNTHETIC DEMO - NOT PLAYER RESULTS" if synthetic else (
        "EDITOR DATA INCLUDED - DIAGNOSTIC ONLY" if stats["included_editor_events"] else "PLAYER BUILD EVENTS")
    output = args.output or Path("analytics-output") / ("demo" if args.demo else "report")
    output.mkdir(parents=True, exist_ok=True)
    summary = dict(build=build, label=label, synthetic=synthetic, include_editor=args.include_editor,
                   input_sha256=hashlib.sha256(raw).hexdigest() if raw is not None else None,
                   baseline_build=args.baseline_build, filtering=stats, data_quality=quality)
    write_csv(output / "events_clean.csv", events, CSV_FIELDS)
    for name, rows in tables.items():
        write_csv(output / f"{name}.csv", rows)
    plot(tables, output, build, label)
    if baseline_rows:
        write_csv(output / "comparison_levels.csv", baseline_rows,
                  ["build", "level", "level_title", "players_started", "players_completed", "player_completion_rate",
                   "runs_from_start", "runs_completed", "median_seconds_to_complete", "mean_restarts_per_run"])
        write_csv(output / "segment_mapping.csv", [dict(alpha=a, beta=b, status=s) for a, b, s in SEGMENT_MAPPING])
        plot_comparison(baseline_rows, output, build, args.baseline_build)
    (output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (output / "report.md").write_text("\n".join(report_lines(label, build, events, stats, tables, quality, bool(baseline_rows))),
                                      encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    print(f"Report: {output.resolve() / 'report.md'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
