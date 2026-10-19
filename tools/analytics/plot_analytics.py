"""Generate Alpha charts from an offline Firebase JSON export. Never contacts Firebase."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import Counter, defaultdict
from pathlib import Path
import re
import statistics

CUTS = ("cut_success", "cut_miss", "cut_too_small", "cut_cancel")
MECHANICS = ("use_cut", "use_view_switch", "use_color_shift", "use_switch_piece", "use_camera")
EVENTS = (*CUTS, *MECHANICS, "room_enter", "room_clear", "restart")
BASE_FIELDS = ("schema_version", "event_id", "session_id", "sequence", "build", "scene",
               "room", "event", "value", "t_room", "is_editor")
VISIT_FIELDS = ("room_visit_id", "room_index", "room_attempt", "t_visit", "cuts_left",
                "cuts_budget", "reason", "first_use")
FIELDS = BASE_FIELDS + VISIT_FIELDS
HEX_ID = re.compile(r"[a-f0-9]{32}\Z")


def number(value):
    return type(value) in (int, float) and math.isfinite(value)


def valid_event(row):
    if not isinstance(row, dict) or not all(k in row for k in BASE_FIELDS):
        return False
    version = row["schema_version"]
    if type(version) is not int or version not in (1, 2):
        return False
    if any(not isinstance(row[k], str) or not HEX_ID.fullmatch(row[k])
           for k in ("event_id", "session_id")):
        return False
    for key, limit in (("build", 64), ("scene", 128), ("room", 128)):
        if not isinstance(row[key], str) or not 0 < len(row[key]) <= limit:
            return False
    if type(row["sequence"]) is not int or row["sequence"] < 1 or type(row["is_editor"]) is not bool:
        return False
    if not isinstance(row["event"], str) or row["event"] not in (CUTS if version == 1 else EVENTS):
        return False
    if not number(row["value"]) or not number(row["t_room"]) or row["t_room"] < 0:
        return False
    event, value = row["event"], row["value"]
    if event == "cut_success" and not 0 <= value <= 1:
        return False
    if event in (*CUTS[1:], "room_enter") and value != -1:
        return False
    if event in (*MECHANICS, "restart") and value != 1:
        return False
    if version == 1:
        return True
    if not all(k in row for k in VISIT_FIELDS):
        return False
    if not isinstance(row["room_visit_id"], str) or not HEX_ID.fullmatch(row["room_visit_id"]):
        return False
    for key, minimum in (("room_index", 0), ("room_attempt", 1), ("cuts_left", 0), ("cuts_budget", 0)):
        if type(row[key]) is not int or row[key] < minimum:
            return False
    return (row["cuts_left"] <= row["cuts_budget"]
            and number(row["t_visit"]) and row["t_visit"] >= row["t_room"]
            and isinstance(row["reason"], str) and len(row["reason"]) <= 64
            and type(row["first_use"]) is bool
            and (event != "room_clear" or value == row["cuts_left"]))


def export_rows(payload):
    """Accept a database-root export, /events export, list, or single event."""
    if isinstance(payload, dict) and "events" in payload:
        payload = payload["events"]
    if payload is None:
        return []
    if isinstance(payload, dict) and "event_id" in payload:
        return [payload]
    if isinstance(payload, dict):
        return list(payload.values())
    if isinstance(payload, list):
        return payload
    raise ValueError("Expected a Firebase database/events JSON object or an event list.")


def prepare(payload, build, include_editor=False):
    stats = Counter(input_rows=0, invalid_rows=0, excluded_build=0, excluded_editor=0,
                    duplicate_rows=0, conflicting_event_ids=0, conflicting_sequences=0)
    grouped = defaultdict(list)
    for row in export_rows(payload):
        stats["input_rows"] += 1
        if not valid_event(row):
            stats["invalid_rows"] += 1
            continue
        if row["build"] != build:
            stats["excluded_build"] += 1
            continue
        if row["is_editor"] and not include_editor:
            stats["excluded_editor"] += 1
            continue
        grouped[row["event_id"]].append(row)
    unique = []
    for copies in grouped.values():
        signatures = {json.dumps(row, sort_keys=True, ensure_ascii=False) for row in copies}
        if len(signatures) != 1:
            stats["conflicting_event_ids"] += 1
            continue  # Never arbitrarily choose between contradictory copies.
        stats["duplicate_rows"] += len(copies) - 1
        unique.append(copies[0])
    sequences = Counter((r["session_id"], r["sequence"]) for r in unique)
    stats["conflicting_sequences"] = sum(n > 1 for n in sequences.values())
    unique = [r for r in unique if sequences[r["session_id"], r["sequence"]] == 1]
    unique.sort(key=lambda r: (r["session_id"], r["sequence"]))
    stats["included_events"] = len(unique)
    stats["included_sessions"] = len({r["session_id"] for r in unique})
    stats["included_editor_events"] = sum(r["is_editor"] for r in unique)
    return unique, dict(stats)


def mean(values):
    return statistics.mean(values) if values else None


def median(values):
    return statistics.median(values) if values else None


def analyze(events):
    rooms = sorted({(r["scene"], r["room"]) for r in events},
                   key=lambda k: (k[0] != "Tutorial", k[0],
                                  min(r.get("room_index", 0) for r in events
                                      if (r["scene"], r["room"]) == k), k[1]))
    by_room = defaultdict(list)
    visits = defaultdict(list)
    last = {}
    for row in events:
        by_room[row["scene"], row["room"]].append(row)
        if row["schema_version"] == 2:
            visits[row["session_id"], row["room_visit_id"]].append(row)
    observed = defaultdict(list)
    quality = Counter(visits_without_entry=0, conflicting_visit_contexts=0, clears_without_entry=0)
    for key, records in visits.items():
        records.sort(key=lambda r: r["sequence"])
        if len({(r["scene"], r["room"], r["room_index"]) for r in records}) != 1:
            quality["conflicting_visit_contexts"] += 1
            continue
        entries = [r for r in records if r["event"] == "room_enter"]
        clears = [r for r in records if r["event"] == "room_clear"]
        if not entries:
            quality["visits_without_entry"] += 1
            quality["clears_without_entry"] += bool(clears)
            continue
        clear = next((r for r in clears if r["sequence"] > entries[0]["sequence"]), None)
        observed[entries[0]["scene"], entries[0]["room"]].append(
            {"key": key, "session": key[0], "events": records, "clear": clear})
        for entry in entries:
            prior = last.get(key[0])
            if prior is None or entry["sequence"] > prior["sequence"]:
                last[key[0]] = entry
    tables = {name: [] for name in ("cut_outcomes", "progression", "mechanics", "difficulty", "last_rooms")}
    distributions = {}
    for scene, room in rooms:
        key = scene, room
        base = {"scene": scene, "room": room}
        counts = Counter(r["event"] for r in by_room[key])
        total = sum(counts[c] for c in CUTS)
        ratios = [r["value"] for r in by_room[key] if r["event"] == "cut_success"]
        tables["cut_outcomes"].append(dict(base, **{c: counts[c] for c in CUTS},
                                          attempts=total, success_rate=counts[CUTS[0]] / total if total else None,
                                          median_kept_ratio=median(ratios)))
        room_visits = observed[key]
        entered_sessions = {v["session"] for v in room_visits}
        completed = [v for v in room_visits if v["clear"]]
        cleared_sessions = {v["session"] for v in completed}
        tables["progression"].append(dict(base, entered_sessions=len(entered_sessions),
            cleared_sessions=len(cleared_sessions), observed_visits=len(room_visits),
            completed_visits=len(completed),
            clear_rate=len(cleared_sessions) / len(entered_sessions) if entered_sessions else None))
        for mechanic in MECHANICS:
            uses = [[r for r in v["events"] if r["event"] == mechanic] for v in room_visits]
            first_times = [min(r["t_visit"] for r in rows if r["first_use"])
                           for rows in uses if any(r["first_use"] for r in rows)]
            tables["mechanics"].append(dict(base, mechanic=mechanic, observed_visits=len(uses),
                visits_using=sum(bool(rows) for rows in uses), uses=sum(map(len, uses)),
                usage_rate=sum(bool(rows) for rows in uses) / len(uses) if uses else None,
                first_use_samples=len(first_times), median_first_use_seconds=median(first_times)))
        restart_counts = {reason: [sum(r["event"] == "restart" and r["reason"] == reason
                                      for r in v["events"]) for v in room_visits]
                          for reason in ("manual", "fall")}
        times = [v["clear"]["t_visit"] for v in completed]
        cuts = [v["clear"]["cuts_left"] for v in completed]
        budgets = [v["clear"]["cuts_budget"] for v in completed]
        remaining_fractions = [c / b for c, b in zip(cuts, budgets) if b > 0]
        tables["difficulty"].append(dict(base, observed_visits=len(room_visits), completed_visits=len(completed),
            manual_restarts=sum(restart_counts["manual"]), fall_restarts=sum(restart_counts["fall"]),
            mean_manual_restarts=mean(restart_counts["manual"]), mean_fall_restarts=mean(restart_counts["fall"]),
            median_completion_seconds=median(times), median_cuts_left=median(cuts),
            median_remaining_fraction=median(remaining_fractions)))
        distributions[key] = {"kept_ratio": ratios, "completion_seconds": times, "cuts_left": cuts}
    for session, entry in sorted(last.items()):
        cleared = any(r["event"] == "room_clear" and r["sequence"] > entry["sequence"]
                      for r in visits[session, entry["room_visit_id"]])
        tables["last_rooms"].append({"session_id": session, "scene": entry["scene"], "room": entry["room"],
            "room_visit_id": entry["room_visit_id"], "last_enter_sequence": entry["sequence"],
            "clear_observed_after_last_enter": cleared})
    return tables, distributions, dict(quality)


def write_csv(path, rows, fieldnames=None):
    with path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames or list(rows[0]), extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def plot(tables, distributions, output, build, label):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import numpy as np
    from matplotlib.ticker import MaxNLocator

    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 10,
                         "axes.spines.top": False, "axes.spines.right": False,
                         "axes.titleweight": "bold", "figure.facecolor": "#f7f8fb",
                         "axes.facecolor": "#ffffff", "svg.fonttype": "none"})
    rooms = [(r["scene"], r["room"]) for r in tables["progression"]]
    labels = [f"{s} / {r}" if s != r else s for s, r in rooms]
    y = np.arange(len(rooms))
    height = max(4.8, 2.5 + len(rooms) * .48)

    def finish(fig, name, title, note, bottom=.09):
        fig.suptitle(f"{title}\n{label} | Build: {build}", fontsize=14, fontweight="bold")
        fig.text(.02, .02, note, fontsize=9, color="#4b5563")
        fig.tight_layout(rect=(0, bottom, 1, .88))
        for suffix in ("png", "svg"):
            fig.savefig(output / f"{name}.{suffix}", dpi=200)
        plt.close(fig)

    def room_axis(ax):
        ax.set_yticks(y, labels)
        ax.set_ylim(len(rooms) - .5, -.5)
        ax.grid(axis="x", alpha=.18)
        ax.set_axisbelow(True)

    def boxes(ax, field):
        room_axis(ax)
        positions = [i for i, key in enumerate(rooms) if distributions[key][field]]
        if positions:
            ax.boxplot([distributions[rooms[i]][field] for i in positions], positions=positions,
                       orientation="horizontal", manage_ticks=False, widths=.45)
        ax.set_ylim(len(rooms) - .5, -.5)
        for i, key in enumerate(rooms):
            ax.text(1.01, i, f"n={len(distributions[key][field])}",
                    transform=ax.get_yaxis_transform(), va="center", fontsize=8)

    fig, axes = plt.subplots(1, 2, figsize=(15, height))
    left = np.zeros(len(rooms))
    for event, title, color in zip(CUTS, ("Success", "Miss", "Too small", "Cancel"),
                                   ("#247f72", "#e2963b", "#cc5e5e", "#8a91a6")):
        values = np.array([r[event] for r in tables["cut_outcomes"]])
        axes[0].barh(y, values, left=left, color=color, label=title)
        left += values
    room_axis(axes[0])
    axes[0].xaxis.set_major_locator(MaxNLocator(integer=True))
    axes[0].set_title("Cut outcomes")
    axes[0].set_xlabel("Attempts (including explicit cancellations)")
    handles, legend_labels = axes[0].get_legend_handles_labels()
    fig.legend(handles, legend_labels, loc="lower left", bbox_to_anchor=(.12, .07), ncol=4, frameon=False, fontsize=9)
    boxes(axes[1], "kept_ratio")
    axes[1].set_xlim(0, 1)
    axes[1].set_title("Retained volume on successful cuts")
    axes[1].set_xlabel("Retained / total volume")
    finish(fig, "metric1_cut_outcomes", "Metric 1 - Cut outcomes", "Deduplicated event IDs. Boxplots use successful cuts only; n is the sample count.", bottom=.17)

    fig, axes = plt.subplots(1, 2, figsize=(15, height))
    rows = tables["progression"]
    for delta, field, title, color in ((-.18, "entered_sessions", "Entered", "#8db9d5"),
                                      (.18, "cleared_sessions", "Cleared", "#247f72")):
        axes[0].barh(y + delta, [r[field] for r in rows], height=.32, label=title, color=color)
    room_axis(axes[0])
    axes[0].legend(frameon=False)
    axes[0].set_xlabel("Distinct sessions (not unique people)")
    axes[0].xaxis.set_major_locator(MaxNLocator(integer=True))
    axes[0].set_title("Entered and cleared")
    rates = [100 * r["clear_rate"] if r["clear_rate"] is not None else float("nan") for r in rows]
    axes[1].barh(y, rates, color="#247f72")
    room_axis(axes[1])
    axes[1].set_xlim(0, 115)
    axes[1].set_xlabel("Cleared sessions / entered sessions (%)")
    axes[1].set_title("Observed clear rate")
    for i, r in enumerate(rows):
        text = f'{r["cleared_sessions"]}/{r["entered_sessions"]}' if r["entered_sessions"] else "No entry data"
        axes[1].text(103, i, text, va="center", fontsize=9)
    finish(fig, "metric2_progression", "Metric 2 - Room progression", "Restart entries count once per session/room. Direct level selection prevents assuming a linear funnel.")

    fig, axes = plt.subplots(1, 2, figsize=(15, height))
    mechanic_labels = ("C: Cut", "Tab: View", "Shift: Color", "F: Swap", "Q/E: Rotate")
    for ax, field, title, cmap in ((axes[0], "usage_rate", "Visits using each mechanic (%)", "YlGnBu"),
                                   (axes[1], "median_first_use_seconds", "Median time to first use (s)", "YlOrBr")):
        data = np.array([[next(r[field] for r in tables["mechanics"]
                              if (r["scene"], r["room"]) == key and r["mechanic"] == mechanic)
                          for mechanic in MECHANICS] for key in rooms], dtype=float)
        if field == "usage_rate":
            data *= 100
        im = ax.imshow(np.ma.masked_invalid(data), cmap=cmap, aspect="auto", vmin=0,
                       vmax=100 if field == "usage_rate" else max(1, np.nanmax(data)) if np.isfinite(data).any() else 1)
        ax.set_yticks(y, labels)
        ax.set_xticks(range(5), mechanic_labels, rotation=25, ha="right")
        ax.set_title(title)
        for i in range(len(rooms)):
            for j in range(5):
                value = data[i, j]
                text = "N/A" if not np.isfinite(value) else f"{value:.0f}"
                ax.text(j, i, text, ha="center", va="center", color="white" if np.isfinite(value) and value > im.norm.vmax * .65 else "#172436")
        fig.colorbar(im, ax=ax, shrink=.7)
    finish(fig, "metric3_mechanics", "Metric 3 - Mechanic use and discovery", "Denominator: observed room visits; retries stay within a visit. First-use time includes earlier attempts; non-use is not zero seconds.")

    fig, axes = plt.subplots(1, 3, figsize=(19, height))
    for delta, field, title, color in ((-.18, "mean_manual_restarts", "Manual R", "#e2963b"),
                                      (.18, "mean_fall_restarts", "Fall", "#8db9d5")):
        axes[0].barh(y + delta, [r[field] if r[field] is not None else float("nan") for r in tables["difficulty"]],
                     height=.32, label=title, color=color)
    room_axis(axes[0])
    axes[0].set_title("Restarts per observed visit")
    axes[0].set_xlabel("Mean (includes zero-restart visits)")
    axes[0].legend(frameon=False)
    boxes(axes[1], "completion_seconds")
    axes[1].set_title("Total time until clear")
    axes[1].set_xlabel("Seconds, including failed attempts")
    boxes(axes[2], "cuts_left")
    axes[2].set_title("Cuts left at clear")
    axes[2].set_xlabel("Remaining cuts before refill")
    axes[2].set_xlim(0, max([1] + [c for d in distributions.values() for c in d["cuts_left"]]) + 1)
    axes[2].xaxis.set_major_locator(MaxNLocator(integer=True))
    finish(fig, "metric4_difficulty", "Metric 4 - Room difficulty", "Time and cuts distributions use completed visits only. Time includes pauses. Incomplete visits are reported separately in CSV.")


def demo_data():
    """Deterministic synthetic records; never upload or treat these as a playtest."""
    records = []
    for player in range(1, 9):
        sequence = 0
        for room_index, (scene, room) in enumerate((("Tutorial", "Level1"), ("Tutorial", "Level2"),
                ("Tutorial", "Level3"), ("Tutorial", "ColorShift"), ("DoubleJump", "DoubleJump"), ("NewLevel", "NewLevel"))):
            if room_index > player % 7:
                break
            visit = f"{player * 100 + room_index:032x}"
            attempt, prior, cuts = 1, 0, 2
            last_attempt, last_time = 1, -1
            def add(event, seconds, value=-1, reason="", first=False):
                nonlocal sequence, last_attempt, last_time
                if attempt != last_attempt:
                    last_time = -1
                seconds = max(seconds, last_time + 1)
                last_attempt, last_time = attempt, seconds
                sequence += 1
                records.append(dict(schema_version=2, event_id=f"{player * 10000 + sequence:032x}",
                    session_id=f"{player:032x}", sequence=sequence, build="synthetic-demo", scene=scene,
                    room=room, event=event, value=value, t_room=seconds, is_editor=False,
                    room_visit_id=visit, room_index=room_index if scene == "Tutorial" else 0,
                    room_attempt=attempt, t_visit=prior + seconds, cuts_left=cuts, cuts_budget=2,
                    reason=reason, first_use=first))
            add("room_enter", 0)
            add("use_cut", 3 + player, 1, "key_c", True)
            if player % 2:
                add("cut_miss", 6 + player)
            if player % 3 == 0:
                add("cut_too_small", 8 + player)
            if player % 4 == 0:
                add("cut_cancel", 10 + player)
            if (player + room_index) % 3 == 0:
                add("restart", 20, 1, "manual" if player % 2 else "fall")
                prior, attempt = 20, 2
                add("room_enter", 0)
                add("use_cut", 2, 1, "key_c")
            cuts = 1
            add("cut_success", 10, .35 + player * .04)
            for j, mechanic in enumerate(MECHANICS[1:]):
                if (player + j + room_index) % 3:
                    add(mechanic, 12 + j * 5, 1, ("key_tab", "key_shift", "key_f", "key_e")[j], True)
            if (player + room_index) % 4 or player == 6:
                add("room_clear", 40 + player * 7 + room_index * 8, cuts)
            else:
                break
    return {"events": {f"synthetic-{i}": row for i, row in enumerate(records)}}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--input", type=Path, help="Firebase root or /events JSON export")
    source.add_argument("--demo", action="store_true", help="Generate clearly marked synthetic preview charts")
    parser.add_argument("--build", help="Exact build ID; required with --input to avoid mixing releases")
    parser.add_argument("--include-editor", action="store_true", help="Diagnostic reports only; includes Editor data")
    parser.add_argument("--output", type=Path, help="Output folder (default analytics-output/demo or report)")
    args = parser.parse_args(argv)
    if args.input and not args.build:
        parser.error("--input requires --build (for example alpha2-all-metrics)")
    if args.demo and args.build:
        parser.error("--demo always uses the synthetic-demo build")
    build = "synthetic-demo" if args.demo else args.build
    try:
        raw = args.input.read_bytes() if args.input else None
        payload = json.loads(raw.decode("utf-8-sig")) if raw is not None else demo_data()
        events, stats = prepare(payload, build, args.include_editor)
        if not events:
            raise ValueError("No matching valid events. Check the build ID and export. Editor rows are excluded by default; use --include-editor only for diagnostics.")
        tables, distributions, quality = analyze(events)
    except (OSError, ValueError) as error:
        parser.exit(2, f"Input error: {error}\n")
    synthetic = args.demo or build == "synthetic-demo"
    label = "SYNTHETIC DEMO - NOT PLAYER RESULTS" if synthetic else (
        "EDITOR DATA INCLUDED - DIAGNOSTIC ONLY" if stats["included_editor_events"] else "PLAYER BUILD EVENTS")
    output = args.output or Path("analytics-output") / ("demo" if args.demo else "report")
    output.mkdir(parents=True, exist_ok=True)
    summary = dict(build=build, label=label, synthetic=synthetic, include_editor=args.include_editor,
                   input_sha256=hashlib.sha256(raw).hexdigest() if raw is not None else None,
                   filtering=stats, data_quality=quality)
    write_csv(output / "events_clean.csv", events, FIELDS)
    for name, rows in tables.items():
        if rows:
            write_csv(output / f"{name}.csv", rows)
        elif name == "last_rooms":
            write_csv(output / f"{name}.csv", [], ("session_id", "scene", "room", "room_visit_id",
                                                    "last_enter_sequence", "clear_observed_after_last_enter"))
    (output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    plot(tables, distributions, output, build, label)
    lines = ["# Alpha 数据图表", "", f"**{label}**", "", f"版本：`{build}`。",
             f"纳入 {len(events)} 条事件、{stats['included_sessions']} 个会话（不是独立玩家人数）。", "",
             "每张图同时生成 PNG 和 SVG，汇总表为 UTF-8 CSV。",
             "事件按 event_id 去重；矛盾的重复 ID 或会话序号会被排除，数量见 summary.json。",
             "进度按不同会话统计；机制与难度按房间游玩 ID 统计，重开不增加游玩次数。",
             "首次使用时间只统计有 first_use 记录的样本，未使用不会记成 0 秒。",
             "通关时间包含失败尝试、暂停及离开窗口的时间；只对观测到进入和通关的游玩作图。",
             "没有房间进入事件的记录不计入指标 2–4；旧版 v1 数据仅支持切割图。",
             "最后所在房间见 last_rooms.csv；没有通关事件不能证明退出原因，也可能是数据丢失。", ""]
    for name in ("metric1_cut_outcomes", "metric2_progression", "metric3_mechanics", "metric4_difficulty"):
        lines.extend([f"![{name}]({name}.png)", ""])
    (output / "report.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    print(f"Report: {output.resolve() / 'report.md'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
