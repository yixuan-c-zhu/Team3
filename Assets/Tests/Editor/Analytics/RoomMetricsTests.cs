using System;
using NUnit.Framework;
using Team3.Analytics;

/// <summary>Level runs, segments and checkpoints (schema v3). The file keeps its old name so its .meta GUID is unchanged.</summary>
public class RoomMetricsTests
{
    static SegmentInfo Tutorial(int segment) => new SegmentInfo("Tutorial", "tutorial", "Tutorial", segment, 4, "Segment " + segment);
    static SegmentInfo Level01() => new SegmentInfo("NewLevel", "level01", "New Level", 1, 1, "New Level");

    [Test]
    public void FirstSegmentStartsALevelRunAndDuplicatesAreIgnored()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var events = s.BeginSegment(Tutorial(1), 3, 3, 10d);
        Assert.That(events.Length, Is.EqualTo(2));
        Assert.That(events[0].@event, Is.EqualTo("level_start"));
        Assert.That(events[1].@event, Is.EqualTo("segment_start"));
        Assert.That(events[0].sequence, Is.EqualTo(1));
        Assert.That(events[1].sequence, Is.EqualTo(2));
        Assert.That(events[0].visit_id, Is.EqualTo(events[1].visit_id));
        Assert.That(events[0].segment, Is.EqualTo(1));
        Assert.That(events[0].cuts_left, Is.EqualTo(3));
        Assert.That(s.BeginSegment(Tutorial(1), 3, 3, 11d).Length, Is.EqualTo(0));
    }

    [Test]
    public void CheckpointContinuesTheRunAndLevelTimeAccumulates()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var first = s.BeginSegment(Tutorial(1), 3, 3, 10d);
        var checkpoint = s.CompleteSegment(false, 1, 3, 25d);
        Assert.That(checkpoint.@event, Is.EqualTo("checkpoint_reached"));
        Assert.That(checkpoint.segment, Is.EqualTo(1));
        Assert.That(checkpoint.t_segment, Is.EqualTo(15d));
        Assert.That(checkpoint.t_level, Is.EqualTo(15d));
        Assert.That(checkpoint.cuts_left, Is.EqualTo(1));
        Assert.That(s.CompleteSegment(false, 1, 3, 26d), Is.Null);

        var second = s.BeginSegment(Tutorial(2), 2, 2, 25d);
        Assert.That(second.Length, Is.EqualTo(1));
        Assert.That(second[0].@event, Is.EqualTo("segment_start"));
        Assert.That(second[0].visit_id, Is.Not.EqualTo(first[1].visit_id));
        Assert.That(second[0].attempt, Is.EqualTo(1));
        Assert.That(second[0].cuts_left, Is.EqualTo(2));

        var use = s.UseMechanic(Mechanic.Cut, "key_c", 30d);
        Assert.That(use.t_segment, Is.EqualTo(5d));
        Assert.That(use.t_level, Is.EqualTo(20d));
    }

    [Test]
    public void LastExitIsLevelCompleteAndIsRecordedOnce()
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginSegment(Tutorial(1), 3, 3, 0d);
        s.CompleteSegment(false, 2, 3, 10d);
        s.BeginSegment(Tutorial(2), 2, 2, 10d);
        s.CompleteSegment(false, 0, 2, 30d);
        s.BeginSegment(Tutorial(3), 2, 2, 30d);
        s.CompleteSegment(false, 1, 2, 45d);
        s.BeginSegment(Tutorial(4), 1, 1, 45d);
        var done = s.CompleteSegment(true, 0, 1, 70d);
        Assert.That(done.@event, Is.EqualTo("level_complete"));
        Assert.That(done.segment, Is.EqualTo(4));
        Assert.That(done.t_segment, Is.EqualTo(25d));
        Assert.That(done.t_level, Is.EqualTo(70d));
        Assert.That(done.level_restarts, Is.EqualTo(0));
        // CheckpointManager.Reach and LevelManager.CompleteLevel both report the final exit.
        Assert.That(s.CompleteSegment(true, 0, 1, 71d), Is.Null);
    }

    [Test]
    public void StartingMidLevelFromTheMenuIsVisibleOnLevelStart()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var events = s.BeginSegment(Tutorial(3), 2, 2, 0d);
        Assert.That(events[0].@event, Is.EqualTo("level_start"));
        Assert.That(events[0].segment, Is.EqualTo(3));
    }

    [Test]
    public void RestartKeepsVisitAndRunAndCountsLevelRestarts()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var start = s.BeginSegment(Tutorial(2), 2, 2, 0d);
        var restart = s.Restart("manual", 0, 2, 10d);
        Assert.That(restart.@event, Is.EqualTo("restart"));
        Assert.That(restart.reason, Is.EqualTo("manual"));
        Assert.That(restart.cuts_left, Is.EqualTo(0));
        Assert.That(restart.level_restarts, Is.EqualTo(1));
        Assert.That(restart.t_level, Is.EqualTo(10d));
        Assert.That(s.Restart("fall", 0, 2, 11d), Is.Null);
        s.LeaveScene();

        var again = s.BeginSegment(Tutorial(2), 2, 2, 12d);
        Assert.That(again.Length, Is.EqualTo(1), "a restart reload is not a new level run");
        Assert.That(again[0].@event, Is.EqualTo("segment_start"));
        Assert.That(again[0].attempt, Is.EqualTo(2));
        Assert.That(again[0].visit_id, Is.EqualTo(start[1].visit_id));

        var checkpoint = s.CompleteSegment(false, 1, 2, 20d);
        Assert.That(checkpoint.t_attempt, Is.EqualTo(8d));
        Assert.That(checkpoint.t_segment, Is.EqualTo(18d));
        Assert.That(checkpoint.t_level, Is.EqualTo(18d));
        Assert.That(checkpoint.level_restarts, Is.EqualTo(1));
    }

    [Test]
    public void MultipleRestartsAccumulateEveryAttempt()
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginSegment(Level01(), 2, 2, 0d);
        s.Restart("manual", 1, 2, 8d);
        s.LeaveScene();
        s.BeginSegment(Level01(), 2, 2, 10d);
        var fall = s.Restart("fall", 2, 2, 14d);
        Assert.That(fall.reason, Is.EqualTo("fall"));
        Assert.That(fall.level_restarts, Is.EqualTo(2));
        s.LeaveScene();
        s.BeginSegment(Level01(), 2, 2, 17d);
        var done = s.CompleteSegment(true, 0, 2, 22d);
        Assert.That(done.@event, Is.EqualTo("level_complete"));
        Assert.That(done.t_segment, Is.EqualTo(17d));
        Assert.That(done.t_level, Is.EqualTo(17d));
        Assert.That(done.attempt, Is.EqualTo(3));
        Assert.That(done.level_restarts, Is.EqualTo(2));
    }

    [Test]
    public void LeavingToTheMenuEndsTheRun()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var first = s.BeginSegment(Tutorial(1), 3, 3, 0d);
        s.CompleteSegment(false, 2, 3, 10d);
        s.BeginSegment(Tutorial(2), 2, 2, 10d);
        s.LeaveScene();
        var back = s.BeginSegment(Tutorial(2), 2, 2, 40d);
        Assert.That(back.Length, Is.EqualTo(2));
        Assert.That(back[0].@event, Is.EqualTo("level_start"));
        Assert.That(back[0].t_level, Is.EqualTo(0d));
        Assert.That(back[1].visit_id, Is.Not.EqualTo(first[1].visit_id));
    }

    [Test]
    public void NewSegmentDoesNotInheritPreviousSegmentCutsOrDiscovery()
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginSegment(Tutorial(1), 2, 2, 10d);
        s.UseMechanic(Mechanic.Cut, "key_c", 11d);
        var checkpoint = s.CompleteSegment(false, 0, 2, 20d);
        var second = s.BeginSegment(Tutorial(2), 3, 3, 21d)[0];
        Assert.That(checkpoint.cuts_left, Is.EqualTo(0));
        Assert.That(second.cuts_left, Is.EqualTo(3));
        Assert.That(second.segment, Is.EqualTo(2));
        Assert.That(s.UseMechanic(Mechanic.Cut, "key_c", 23d).first_use, Is.True);
    }

    [Test]
    public void FirstUsePersistsAcrossRestartAndIncludesPreviousAttemptTime()
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginSegment(Tutorial(2), 3, 3, 0d);
        Assert.That(s.UseMechanic(Mechanic.ViewSwitch, "key_tab", 2d).first_use, Is.True);
        Assert.That(s.UseMechanic(Mechanic.ViewSwitch, "key_tab", 3d).first_use, Is.False);
        s.Restart("manual", 1, 3, 10d);
        s.LeaveScene();
        s.BeginSegment(Tutorial(2), 3, 3, 15d);
        Assert.That(s.UseMechanic(Mechanic.ViewSwitch, "key_tab", 18d).first_use, Is.False);
        var discovered = s.UseMechanic(Mechanic.ColorShift, "key_shift", 20d);
        Assert.That(discovered.first_use, Is.True);
        Assert.That(discovered.t_segment, Is.EqualTo(15d));
    }

    [TestCase(Mechanic.Cut, "use_cut")]
    [TestCase(Mechanic.ViewSwitch, "use_view_switch")]
    [TestCase(Mechanic.ColorShift, "use_color_shift")]
    [TestCase(Mechanic.SwitchPiece, "use_switch_piece")]
    [TestCase(Mechanic.Camera, "use_camera")]
    public void EachMechanicHasItsOwnEvent(Mechanic mechanic, string expected)
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginSegment(Level01(), 2, 2, 0d);
        var item = s.UseMechanic(mechanic, "input", 4d);
        Assert.That(item.@event, Is.EqualTo(expected));
        Assert.That(item.kept_ratio, Is.EqualTo(-1f));
        Assert.That(item.reason, Is.EqualTo("input"));
        Assert.That(item.first_use, Is.True);
        Assert.That(item.t_segment, Is.EqualTo(4d));
    }

    [Test]
    public void CompletedLevelIgnoresGameplayAndRetryIsANewRun()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var original = s.BeginSegment(Level01(), 2, 2, 0d);
        s.Restart("manual", 2, 2, 1d);
        s.LeaveScene();
        s.BeginSegment(Level01(), 2, 2, 2d);
        s.CompleteSegment(true, 1, 2, 4d);
        Assert.That(s.Capture(Guid.NewGuid().ToString("N"), CutOutcome.Miss, -1f, 5d), Is.Null);
        Assert.That(s.UseMechanic(Mechanic.Camera, "key_e", 5d), Is.Null);
        Assert.That(s.Restart("retry_button", 1, 2, 6d), Is.Null);
        s.LeaveScene();
        var replay = s.BeginSegment(Level01(), 2, 2, 7d);
        Assert.That(replay[0].@event, Is.EqualTo("level_start"));
        Assert.That(replay[1].visit_id, Is.Not.EqualTo(original[1].visit_id));
        Assert.That(replay[1].attempt, Is.EqualTo(1));
        Assert.That(replay[1].level_restarts, Is.EqualTo(0));
    }

    [Test]
    public void MenuAndUnloadedScenesProduceNoGameplayEvents()
    {
        var s = new AnalyticsSession("all-metrics", true);
        Assert.That(s.UseMechanic(Mechanic.Camera, "key_e", 0d), Is.Null);
        Assert.That(s.CompleteSegment(true, 0, 0, 0d), Is.Null);
        s.BeginSegment(Level01(), 2, 2, 1d);
        s.LeaveScene();
        Assert.That(s.UseMechanic(Mechanic.Camera, "key_e", 2d), Is.Null);
        Assert.That(s.CompleteSegment(true, 2, 2, 3d), Is.Null);
    }

    [Test]
    public void IncompleteSegmentInfoIsIgnoredAndSegmentNumbersAreClamped()
    {
        var s = new AnalyticsSession("all-metrics", true);
        Assert.That(s.BeginSegment(default(SegmentInfo), 2, 2, 0d).Length, Is.EqualTo(0));
        var info = new SegmentInfo("Tutorial", "tutorial", "Tutorial", 9, 4, "x");
        Assert.That(info.Segment, Is.EqualTo(4));
        Assert.That(new SegmentInfo("A", "a", "A", 0, 0, "x").Segment, Is.EqualTo(1));
    }
}
