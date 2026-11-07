"""Regression checks for v3 parsing, Alpha (v2) conversion, level runs, funnels and report output."""
import copy
import csv
import json
import tempfile
import unittest
from pathlib import Path

from plot_analytics import (analyze, demo_data, export_rows, main, prepare, push_key_ms, valid_v2, valid_v3)

PLAYER = "7f3a9c21" + "a" * 24
SESSION = "a1b2c3d4" + "b" * 24
TUTORIAL = ("Cut to fit", "Leave a little behind", "First bridge", "Building Bridge")


class Recorder:
    """Builds v3 events the way AnalyticsSession does, for one player/session."""

    def __init__(self, player=PLAYER, session=SESSION, build="test", editor=False):
        self.player, self.session, self.build, self.editor = player, session, build, editor
        self.sequence, self.rows, self.visit = 0, [], 0

    def add(self, event, level="tutorial", segment=1, count=4, *, attempt=1, t=0.0, t_segment=None, t_level=None,
            restarts=0, cuts=1, budget=2, kept=-1, reason="", first=False, new_visit=False):
        if new_visit:
            self.visit += 1
        self.sequence += 1
        titles = {"tutorial": "Tutorial", "level01": "New Level", "level02": "Glass Paradox"}
        row = dict(schema_version=3, event=event, event_id=f"{self.session[:8]}{self.sequence:024x}",
                   player_id=self.player, session_id=self.session, sequence=self.sequence, build=self.build,
                   server_time=1793000000000 + self.sequence * 1000, is_editor=self.editor, level=level,
                   level_title=titles[level], scene=titles[level].replace(" ", ""), segment=segment,
                   segment_count=count, segment_name=TUTORIAL[segment - 1] if level == "tutorial" else titles[level],
                   visit_id=f"{self.session[:8]}{self.visit:024x}", attempt=attempt, t_attempt=t,
                   t_segment=t if t_segment is None else t_segment, t_level=t if t_level is None else t_level,
                   level_restarts=restarts, cuts_left=cuts, cuts_budget=budget, kept_ratio=kept, reason=reason,
                   first_use=first)
        self.rows.append(row)
        return row

    def nested(self):
        """Shape of a Firebase export of /v3: build -> player -> session -> event key -> row."""
        tree = {}
        for row in self.rows:
            tree.setdefault("v3", {}).setdefault(row["build"], {}).setdefault(f"p_{row['player_id'][:8]}", {}) \
                .setdefault(f"20261110-1432-{row['session_id'][:8]}", {})[f"{row['sequence']:04d}_{row['event']}"] = row
        return tree


def full_tutorial(rec, stop_after=None, start=1):
    """A run through the 4-segment tutorial; stop_after = last checkpoint reached before leaving."""
    t_level = 0.0
    for segment in range(start, 5):
        if segment == start:
            rec.add("level_start", segment=segment, new_visit=True, t_level=t_level)
            rec.add("segment_start", segment=segment, t_level=t_level)
        else:
            rec.add("segment_start", segment=segment, new_visit=True, t_level=t_level)
        rec.add("use_cut", segment=segment, t=2, t_level=t_level + 2, reason="key_c", first=True)
        rec.add("cut_success", segment=segment, t=5, t_level=t_level + 5, kept=.5)
        if stop_after is not None and segment > stop_after:
            return
        event = "level_complete" if segment == 4 else "checkpoint_reached"
        rec.add(event, segment=segment, t=10, t_level=t_level + 10, cuts=0)
        t_level += 10


def v2_row(sequence, kind, *, scene="Tutorial", room="Level1", index=0, visit=10, attempt=1, t=0, total=None,
           session=1, build="alpha", cuts=1, budget=2):
    value = 1 if kind.startswith("use_") or kind == "restart" else -1
    if kind == "room_clear":
        value = cuts
    if kind == "cut_success":
        value = .5
    return dict(schema_version=2, event_id=f"{session * 1000 + sequence:032x}", session_id=f"{session:032x}",
                sequence=sequence, build=build, scene=scene, room=room, event=kind, value=value, t_room=t,
                is_editor=False, room_visit_id=f"{visit:032x}", room_index=index, room_attempt=attempt,
                t_visit=t if total is None else total, cuts_left=cuts, cuts_budget=budget,
                reason="manual" if kind == "restart" else "", first_use=False)


class ParsingTests(unittest.TestCase):
    def test_nested_v3_export_is_flattened_at_any_depth(self):
        rec = Recorder()
        full_tutorial(rec)
        tree = rec.nested()
        for payload in (tree, tree["v3"], tree["v3"]["test"], {"events": {}, **tree}):
            events, stats = prepare(payload, "test")
            self.assertEqual(stats["included_events"], len(rec.rows))
            self.assertEqual(stats["invalid_rows"], 0)
        self.assertEqual(len(export_rows(tree)), len(rec.rows))

    def test_v3_validation_matches_the_rules(self):
        rec = Recorder()
        good = rec.add("cut_success", kept=.4)
        self.assertTrue(valid_v3(good))
        for change in ({"kept_ratio": 2}, {"event": "room_clear"}, {"cuts_left": 3}, {"segment": 5},
                       {"t_segment": -1}, {"level": "Tutorial"}, {"build": "beta 1"}, {"is_editor": "false"},
                       {"extra_field": 1}, {"server_time": None}, {"player_id": "x"}):
            with self.subTest(change=change):
                self.assertFalse(valid_v3(dict(good, **change)))
        self.assertFalse(valid_v3(dict(rec.add("use_cut"), kept_ratio=.5)))

    def test_filters_and_duplicate_uploads(self):
        rec = Recorder()
        row = rec.add("level_start", new_visit=True)
        retry = dict(row, server_time=row["server_time"] + 500)  # same event re-sent: only server_time differs
        editor = Recorder(session="c" * 32, editor=True).add("level_start")
        other = Recorder(session="d" * 32, build="other").add("level_start")
        events, stats = prepare([row, retry, editor, other, {"event": "x", "event_id": "y"}], "test")
        self.assertEqual(len(events), 1)
        self.assertEqual(events[0]["server_time"], row["server_time"])
        self.assertEqual([stats[k] for k in ("duplicate_rows", "excluded_editor", "excluded_build", "invalid_rows")], [1, 1, 1, 1])

    def test_conflicting_copies_and_sequences_are_excluded(self):
        rec = Recorder()
        row = rec.add("cut_success", kept=.5)
        events, stats = prepare([row, dict(row, kept_ratio=.9)], "test")
        self.assertEqual((events, stats["conflicting_event_ids"]), ([], 1))
        events, stats = prepare([row, dict(row, event_id="f" * 32, event="cut_miss", kept_ratio=-1)], "test")
        self.assertEqual((events, stats["conflicting_sequences"]), ([], 1))

    def test_push_key_time_is_recovered_for_alpha_rows(self):
        self.assertEqual(push_key_ms("-P4Fw-Bq8eI3k0vmfg3l"), 1792359269174)
        self.assertIsNone(push_key_ms("0001_level_start"))
        events, stats = prepare({"events": {"-P4Fw-Bq8eI3k0vmfg3l": v2_row(1, "room_enter")}}, "alpha")
        self.assertEqual(events[0]["date_utc"], "2026-10-18 21:34")
        self.assertEqual(stats["first_event_utc"], "2026-10-18 21:34")


class AlphaConversionTests(unittest.TestCase):
    def test_v2_rows_get_menu_names_and_level_events(self):
        rows = [v2_row(1, "room_enter"), v2_row(2, "room_clear", t=10),
                v2_row(3, "room_enter", room="Level2", index=1, visit=11),
                v2_row(4, "restart", room="Level2", index=1, visit=11, t=5),
                v2_row(5, "room_enter", room="Level2", index=1, visit=11, attempt=2, total=5),
                v2_row(6, "room_clear", room="Level2", index=1, visit=11, attempt=2, t=4, total=9),
                v2_row(1, "room_enter", scene="NewLevel", room="NewLevel", session=2, visit=20),
                v2_row(2, "room_clear", scene="NewLevel", room="NewLevel", session=2, visit=20, t=30)]
        events, stats = prepare(rows, "alpha")
        self.assertTrue(all(valid_v2(r) for r in rows))
        self.assertEqual(stats["derived_level_starts"], 2)
        tutorial = [r for r in events if r["session_id"] == f"{1:032x}"]
        self.assertEqual([r["event"] for r in tutorial], ["level_start", "segment_start", "checkpoint_reached",
                                                          "segment_start", "restart", "segment_start", "level_complete"])
        last = tutorial[-1]
        self.assertEqual((last["level"], last["segment"], last["segment_count"], last["segment_name"]),
                         ("tutorial", 2, 2, "Leave a little behind"))
        self.assertEqual((last["t_level"], last["level_restarts"]), (19, 1))
        level01 = [r for r in events if r["level"] == "level01"]
        self.assertEqual([r["event"] for r in level01], ["level_start", "segment_start", "level_complete"])
        self.assertTrue(stats["players_are_sessions"])

    def test_replay_after_menu_is_a_new_run_but_restart_is_not(self):
        rows = [v2_row(1, "room_enter"), v2_row(2, "restart", t=3), v2_row(3, "room_enter", attempt=2, total=3),
                v2_row(4, "room_enter", visit=11)]  # chose the level again from the menu
        events, _ = prepare(rows, "alpha")
        self.assertEqual(sum(r["event"] == "level_start" for r in events), 2)


class AnalysisTests(unittest.TestCase):
    def test_funnel_counts_players_by_their_best_run(self):
        a = Recorder()
        full_tutorial(a, stop_after=1)        # first try: stops after checkpoint 1
        full_tutorial(a)                      # second try: completes
        b = Recorder(player="b" * 32, session="e" * 32)
        full_tutorial(b, stop_after=2)
        c = Recorder(player="c" * 32, session="f" * 32)
        full_tutorial(c, start=3)             # menu start at segment 3: not in the funnel
        events, _ = prepare(a.rows + b.rows + c.rows, "test")
        tables, quality = analyze(events)
        funnel = {r["step"]: r for r in tables["funnel"]}
        self.assertEqual([funnel[s]["players"] for s in range(5)], [2, 2, 2, 1, 1])
        self.assertEqual([funnel[s]["runs"] for s in range(5)], [3, 3, 2, 1, 1])
        self.assertEqual(funnel[2]["players_stopped_here"], 1)
        self.assertEqual(funnel[4]["label"], "Complete")
        self.assertEqual(funnel[1]["label"], "Checkpoint 1 (Cut to fit)")
        summary = tables["level_summary"][0]
        self.assertEqual((summary["players_started"], summary["players_completed"], summary["runs_started_mid_level"]), (2, 1, 1))
        self.assertEqual(summary["median_seconds_to_complete"], 40)
        self.assertEqual(quality["events_outside_a_run"], 0)
        players = {r["player_id"]: r for r in tables["players"]}
        self.assertTrue(players[PLAYER]["completed"])
        self.assertEqual(players["b" * 32]["furthest"], "Checkpoint 2 (Leave a little behind)")
        self.assertEqual(players["c" * 32]["furthest"], "(only mid-level starts)")

    def test_segment_difficulty_counts_restarts_inside_one_visit(self):
        rec = Recorder()
        rec.add("level_start", "level01", 1, 1, new_visit=True)
        rec.add("segment_start", "level01", 1, 1)
        rec.add("cut_miss", "level01", 1, 1, t=3)
        rec.add("restart", "level01", 1, 1, t=8, restarts=1, reason="fall")
        rec.add("segment_start", "level01", 1, 1, attempt=2, t_segment=8, t_level=8, restarts=1)
        rec.add("restart", "level01", 1, 1, attempt=2, t=2, t_segment=10, t_level=10, restarts=2, reason="manual")
        rec.add("segment_start", "level01", 1, 1, attempt=3, t_segment=10, t_level=10, restarts=2)
        rec.add("level_complete", "level01", 1, 1, attempt=3, t=5, t_segment=15, t_level=15, restarts=2, cuts=0)
        tables, _ = analyze(prepare(rec.rows, "test")[0])
        seg = tables["segment_difficulty"][0]
        self.assertEqual((seg["visits"], seg["visits_exited"], seg["manual_restarts"], seg["fall_restarts"]), (1, 1, 1, 1))
        self.assertEqual((seg["restarts_per_visit"], seg["median_seconds_to_exit"], seg["cut_failures"]), (2, 15, 1))
        summary = tables["level_summary"][0]
        self.assertEqual((summary["players_completed"], summary["mean_restarts_per_run"], summary["median_seconds_to_complete"]), (1, 2, 15))
        self.assertEqual(tables["funnel"], [])  # single-segment levels have no funnel

    def test_mechanics_mark_taught_cells_and_missing_first_use_is_not_zero(self):
        rec = Recorder()
        full_tutorial(rec, stop_after=0)
        tables, _ = analyze(prepare(rec.rows, "test")[0])
        rows = {(r["segment"], r["mechanic"]): r for r in tables["mechanics"]}
        self.assertTrue(rows[1, "use_cut"]["taught"])
        self.assertTrue(rows[1, "use_view_switch"]["taught"])
        self.assertFalse(rows["all", "use_cut"]["taught"])
        self.assertEqual(rows[1, "use_view_switch"]["usage_rate"], 0)
        self.assertIsNone(rows[1, "use_view_switch"]["median_seconds_to_first_use"])
        self.assertEqual(rows["all", "use_cut"]["median_seconds_to_first_use"], 2)

    def test_hardest_segment_is_flagged(self):
        a = Recorder()
        full_tutorial(a, stop_after=2)
        b = Recorder(player="b" * 32, session="e" * 32)
        full_tutorial(b)
        tables, _ = analyze(prepare(a.rows + b.rows, "test")[0])
        hardest = [r for r in tables["segment_difficulty"] if r["hardest"]]
        self.assertEqual([r["segment"] for r in hardest], [3])


class EndToEndTests(unittest.TestCase):
    def test_demo_is_deterministic_and_valid(self):
        data = demo_data()
        self.assertEqual(data, demo_data())
        events, stats = prepare(data, "synthetic-demo")
        self.assertEqual((stats["invalid_rows"], stats["conflicting_sequences"]), (0, 0))
        self.assertEqual({r["event"] for r in events}, {"level_start", "segment_start", "checkpoint_reached",
            "level_complete", "restart", "cut_success", "cut_miss", "use_cut", "use_view_switch",
            "use_color_shift", "use_switch_piece"})

    def test_cli_writes_report_tables_and_comparison(self):
        rec = Recorder(build="beta-test")
        full_tutorial(rec)
        alpha = {"events": {f"-P4Fw-Bq8eI3k0vmf{i:03d}": r for i, r in enumerate(
            [v2_row(1, "room_enter"), v2_row(2, "room_clear", t=12)])}}
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            (tmp / "beta.json").write_text(json.dumps(rec.nested()), encoding="utf-8")
            (tmp / "alpha.json").write_text(json.dumps(alpha), encoding="utf-8")
            out = tmp / "out"
            code = main(["--input", str(tmp / "beta.json"), "--build", "beta-test", "--output", str(out),
                         "--baseline-input", str(tmp / "alpha.json"), "--baseline-build", "alpha"])
            self.assertEqual(code, 0)
            for name in ("report.md", "summary.json", "level_summary.csv", "funnel.csv", "segment_difficulty.csv",
                         "cut_outcomes.csv", "mechanics.csv", "players.csv", "events_clean.csv",
                         "comparison_levels.csv", "segment_mapping.csv", "metric1_cut_outcomes.png",
                         "metric2_progression.svg", "metric3_mechanics.png", "metric4_difficulty.png", "comparison_levels.png"):
                self.assertTrue((out / name).exists(), name)
            with (out / "comparison_levels.csv").open(encoding="utf-8-sig") as stream:
                builds = {r["build"] for r in csv.DictReader(stream)}
            self.assertEqual(builds, {"alpha", "beta-test"})
            self.assertIn("Tutorial", (out / "report.md").read_text(encoding="utf-8"))

    def test_cli_refuses_missing_build_or_empty_result(self):
        with self.assertRaises(SystemExit):
            main(["--input", "x.json"])
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "empty.json"
            path.write_text(json.dumps(copy.deepcopy(Recorder().nested())), encoding="utf-8")
            with self.assertRaises(SystemExit) as raised:
                main(["--input", str(path), "--build", "nothing-here", "--output", str(Path(tmp) / "o")])
            self.assertEqual(raised.exception.code, 2)


if __name__ == "__main__":
    unittest.main()
