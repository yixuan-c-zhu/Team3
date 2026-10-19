"""Regression checks for report denominators, filters and cumulative time."""
import copy
import unittest

from plot_analytics import analyze, demo_data, export_rows, prepare, valid_event


def event(sequence, kind, *, session=1, visit=10, attempt=1, time=0, total=None,
          value=None, first=False, reason="", editor=False, build="test"):
    if value is None:
        value = 1 if kind.startswith("use_") or kind == "restart" else -1
        if kind == "room_clear":
            value = 1
        if kind == "cut_success":
            value = .5
    return dict(schema_version=2, event_id=f"{session * 1000 + sequence:032x}",
                session_id=f"{session:032x}", sequence=sequence, build=build, scene="Tutorial",
                room="Level1", event=kind, value=value, t_room=time, is_editor=editor,
                room_visit_id=f"{visit:032x}", room_index=0, room_attempt=attempt,
                t_visit=time if total is None else total, cuts_left=1, cuts_budget=2,
                reason=reason, first_use=first)


class ReportTests(unittest.TestCase):
    def test_filters_and_duplicate_posts(self):
        row = event(1, "cut_success")
        data = [row, copy.deepcopy(row), event(2, "cut_cancel", editor=True),
                event(3, "cut_miss", build="other"), {"garbage": True}]
        result, stats = prepare({"events": {str(i): r for i, r in enumerate(data)}}, "test")
        self.assertEqual(result, [row])
        self.assertEqual([stats[k] for k in ("duplicate_rows", "excluded_editor", "excluded_build", "invalid_rows")], [1, 1, 1, 1])

    def test_conflicting_ids_are_not_arbitrarily_selected(self):
        row = event(1, "cut_success")
        conflict = dict(row, value=.9)
        result, stats = prepare([row, conflict], "test")
        self.assertEqual(result, [])
        self.assertEqual(stats["conflicting_event_ids"], 1)

    def test_conflicting_sequences_are_excluded(self):
        row = event(1, "cut_success")
        conflict = dict(row, event_id="f" * 32, event="cut_miss", value=-1)
        result, stats = prepare([row, conflict], "test")
        self.assertEqual(result, [])
        self.assertEqual(stats["conflicting_sequences"], 1)

    def test_retries_do_not_inflate_visits_or_session_funnel(self):
        rows = [event(1, "room_enter"), event(2, "use_cut", time=5, first=True),
                event(3, "restart", time=10, reason="manual"),
                event(4, "room_enter", attempt=2, total=10),
                event(5, "use_cut", attempt=2, time=2, total=12),
                event(6, "room_clear", attempt=2, time=10, total=20),
                event(7, "room_enter", visit=11),  # Replay, same session, new visit.
                event(1, "room_enter", session=2, visit=20)]  # No clear or use.
        tables, distributions, _ = analyze(prepare(rows, "test")[0])
        progress = tables["progression"][0]
        self.assertEqual((progress["entered_sessions"], progress["cleared_sessions"], progress["observed_visits"]), (2, 1, 3))
        self.assertEqual(progress["clear_rate"], .5)
        discovery = tables["mechanics"][0]
        self.assertEqual(discovery["uses"], 2)
        self.assertAlmostEqual(discovery["usage_rate"], 1 / 3)
        self.assertEqual(discovery["median_first_use_seconds"], 5)
        difficulty = tables["difficulty"][0]
        self.assertAlmostEqual(difficulty["mean_manual_restarts"], 1 / 3)
        self.assertEqual(difficulty["mean_fall_restarts"], 0)
        self.assertEqual(distributions["Tutorial", "Level1"]["completion_seconds"], [20])
        self.assertFalse(tables["last_rooms"][0]["clear_observed_after_last_enter"])

    def test_never_used_is_missing_latency_not_zero(self):
        tables, _, _ = analyze([event(1, "room_enter")])
        mechanic = tables["mechanics"][0]
        self.assertEqual(mechanic["usage_rate"], 0)
        self.assertIsNone(mechanic["median_first_use_seconds"])

    def test_orphan_clear_cannot_inflate_clear_rate(self):
        rows = [event(1, "room_enter"), event(2, "room_clear", session=2, visit=20)]
        tables, _, quality = analyze(rows)
        self.assertEqual(tables["progression"][0]["cleared_sessions"], 0)
        self.assertEqual(quality["clears_without_entry"], 1)

    def test_same_room_names_in_different_scenes_remain_separate(self):
        rows = [event(1, "room_enter"), dict(event(2, "room_enter", visit=11), scene="NewLevel")]
        tables, _, _ = analyze(rows)
        self.assertEqual(len(tables["progression"]), 2)

    def test_invalid_numeric_and_editor_fields_are_rejected(self):
        row = event(1, "room_enter")
        for change in ({"is_editor": "false"}, {"t_room": float("nan")},
                       {"sequence": True}, {"cuts_left": 3}, {"t_visit": -1}):
            with self.subTest(change=change):
                self.assertFalse(valid_event(dict(row, **change)))

    def test_legacy_cut_export_does_not_fabricate_progress(self):
        row = event(1, "cut_success")
        row["schema_version"] = 1
        for key in ("room_visit_id", "room_index", "room_attempt", "t_visit", "cuts_left", "cuts_budget", "reason", "first_use"):
            del row[key]
        events, _ = prepare(row, "test")
        tables, _, _ = analyze(events)
        self.assertEqual(tables["cut_outcomes"][0]["cut_success"], 1)
        self.assertEqual(tables["progression"][0]["entered_sessions"], 0)
        self.assertIsNone(tables["mechanics"][0]["usage_rate"])

    def test_conflicting_visit_context_is_excluded_from_visit_metrics(self):
        rows = [event(1, "room_enter"), dict(event(2, "room_clear"), scene="WrongScene")]
        tables, _, quality = analyze(rows)
        self.assertEqual(quality["conflicting_visit_contexts"], 1)
        self.assertTrue(all(r["entered_sessions"] == 0 for r in tables["progression"]))
        self.assertEqual(tables["last_rooms"], [])

    def test_demo_is_deterministic_and_valid(self):
        data = demo_data()
        self.assertEqual(data, demo_data())
        rows, stats = prepare(data, "synthetic-demo")
        self.assertEqual(stats["invalid_rows"], 0)
        self.assertEqual(stats["conflicting_sequences"], 0)
        self.assertEqual({r["event"] for r in rows}, {"room_enter", "room_clear", "restart",
            "cut_success", "cut_miss", "cut_too_small", "cut_cancel", "use_cut", "use_view_switch",
            "use_color_shift", "use_switch_piece", "use_camera"})
        self.assertEqual(export_rows({"events": None}), [])


if __name__ == "__main__":
    unittest.main()
