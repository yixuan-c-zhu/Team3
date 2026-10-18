using NUnit.Framework;
using Team3.Analytics;

public class RoomMetricsTests
{
    [Test]
    public void RoomEntryAndCompletionAreNotDuplicated()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var enter = s.BeginRoom("Tutorial", "Level1", 0, 2, 2, 10d);
        Assert.That(enter.@event, Is.EqualTo("room_enter"));
        Assert.That(s.BeginRoom("Tutorial", "Level1", 0, 2, 2, 11d), Is.Null);
        var clear = s.ClearRoom(1, 2, 25d);
        Assert.That(clear.@event, Is.EqualTo("room_clear"));
        Assert.That(clear.t_visit, Is.EqualTo(15d));
        Assert.That(clear.cuts_left, Is.EqualTo(1));
        Assert.That(clear.value, Is.EqualTo(1f));
        Assert.That(s.ClearRoom(1, 2, 26d), Is.Null);
    }

    [Test]
    public void RestartPreservesVisitAndTotalTimeButResetsAttemptTime()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var enter = s.BeginRoom("Tutorial", "Level2", 1, 3, 3, 10d);
        var restart = s.RestartRoom("manual", 0, 3, 30d);
        Assert.That(restart.reason, Is.EqualTo("manual"));
        Assert.That(restart.cuts_left, Is.Zero);
        Assert.That(s.RestartRoom("fall", 0, 3, 31d), Is.Null);
        s.LeaveScene();
        var reenter = s.BeginRoom("Tutorial", "Level2", 1, 3, 3, 35d);
        var clear = s.ClearRoom(2, 3, 45d);
        Assert.That(reenter.room_visit_id, Is.EqualTo(enter.room_visit_id));
        Assert.That(reenter.room_attempt, Is.EqualTo(2));
        Assert.That(clear.t_room, Is.EqualTo(10d));
        Assert.That(clear.t_visit, Is.EqualTo(30d));
        Assert.That(clear.cuts_left, Is.EqualTo(2));
    }

    [Test]
    public void MultipleRestartsAccumulateEveryAttempt()
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 0d);
        s.RestartRoom("manual", 1, 2, 8d);
        s.LeaveScene();
        s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 10d);
        var fall = s.RestartRoom("fall", 2, 2, 14d);
        Assert.That(fall.reason, Is.EqualTo("fall"));
        s.LeaveScene();
        s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 17d);
        var clear = s.ClearRoom(0, 2, 22d);
        Assert.That(clear.t_visit, Is.EqualTo(17d));
        Assert.That(clear.room_attempt, Is.EqualTo(3));
    }

    [Test]
    public void NewRoomDoesNotInheritPreviousRoomCutsOrDiscovery()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var first = s.BeginRoom("Tutorial", "Level1", 0, 2, 2, 10d);
        s.UseMechanic(Mechanic.Cut, "key_c", 11d);
        var clear = s.ClearRoom(0, 2, 20d);
        var second = s.BeginRoom("Tutorial", "Level2", 1, 3, 3, 21d);
        Assert.That(clear.cuts_left, Is.Zero);
        Assert.That(second.cuts_left, Is.EqualTo(3));
        Assert.That(second.room_index, Is.EqualTo(1));
        Assert.That(second.room_visit_id, Is.Not.EqualTo(first.room_visit_id));
        Assert.That(s.UseMechanic(Mechanic.Cut, "key_c", 23d).first_use, Is.True);
    }

    [Test]
    public void FirstUsePersistsAcrossRestartAndIncludesPreviousAttemptTime()
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginRoom("Tutorial", "Level2", 1, 3, 3, 0d);
        Assert.That(s.UseMechanic(Mechanic.ViewSwitch, "key_tab", 2d).first_use, Is.True);
        Assert.That(s.UseMechanic(Mechanic.ViewSwitch, "key_tab", 3d).first_use, Is.False);
        s.RestartRoom("manual", 1, 3, 10d);
        s.LeaveScene();
        s.BeginRoom("Tutorial", "Level2", 1, 3, 3, 15d);
        Assert.That(s.UseMechanic(Mechanic.ViewSwitch, "key_tab", 18d).first_use, Is.False);
        var discovered = s.UseMechanic(Mechanic.ColorShift, "key_shift", 20d);
        Assert.That(discovered.first_use, Is.True);
        Assert.That(discovered.t_visit, Is.EqualTo(15d));
    }

    [TestCase(Mechanic.Cut, "use_cut")]
    [TestCase(Mechanic.ViewSwitch, "use_view_switch")]
    [TestCase(Mechanic.ColorShift, "use_color_shift")]
    [TestCase(Mechanic.SwitchPiece, "use_switch_piece")]
    [TestCase(Mechanic.Camera, "use_camera")]
    public void EachMechanicHasItsOwnDiscoveryEvent(Mechanic mechanic, string expected)
    {
        var s = new AnalyticsSession("all-metrics", true);
        s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 0d);
        var item = s.UseMechanic(mechanic, "input", 4d);
        Assert.That(item.@event, Is.EqualTo(expected));
        Assert.That(item.value, Is.EqualTo(1f));
        Assert.That(item.first_use, Is.True);
        Assert.That(item.t_visit, Is.EqualTo(4d));
    }

    [Test]
    public void ClearedRoomIgnoresGameplayAndRetryIsANewVisit()
    {
        var s = new AnalyticsSession("all-metrics", true);
        var original = s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 0d);
        s.ClearRoom(1, 2, 4d);
        Assert.That(s.Capture("x", CutOutcome.Miss, -1f, 5d), Is.Null);
        Assert.That(s.UseMechanic(Mechanic.Camera, "key_e", 5d), Is.Null);
        Assert.That(s.RestartRoom("retry_button", 1, 2, 6d), Is.Null);
        s.LeaveScene();
        var replay = s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 7d);
        Assert.That(replay.room_visit_id, Is.Not.EqualTo(original.room_visit_id));
        Assert.That(replay.room_attempt, Is.EqualTo(1));
    }

    [Test]
    public void MenuAndUnloadedScenesProduceNoGameplayEvents()
    {
        var s = new AnalyticsSession("all-metrics", true);
        Assert.That(s.UseMechanic(Mechanic.Camera, "key_e", 0d), Is.Null);
        s.BeginRoom("NewLevel", "NewLevel", 0, 2, 2, 1d);
        s.LeaveScene();
        Assert.That(s.UseMechanic(Mechanic.Camera, "key_e", 2d), Is.Null);
        Assert.That(s.ClearRoom(2, 2, 3d), Is.Null);
    }
}
