using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Team3.Analytics;
using UnityEngine;

public class CutAnalyticsTests
{
    static string NewId() => Guid.NewGuid().ToString("N");
    static SegmentInfo Tutorial(int segment) => new SegmentInfo("Tutorial", "tutorial", "Tutorial", segment, 4, "Segment " + segment);
    static SegmentInfo Level01() => new SegmentInfo("NewLevel", "level01", "New Level", 1, 1, "New Level");

    [Test]
    public void CancelBeforeDrawingProducesOneResult()
    {
        var tracker = new CutAttemptTracker();
        tracker.BeginMode();
        Assert.That(tracker.CancelMode(), Has.Length.EqualTo(32));
        Assert.That(tracker.CancelMode(), Is.Null);
    }

    [Test]
    public void LeavingAfterFailedLineDoesNotCountAnotherCancellation()
    {
        var tracker = new CutAttemptTracker();
        tracker.BeginMode();
        tracker.SubmitLine();
        Assert.That(tracker.CompleteLine(), Is.Not.Null);
        Assert.That(tracker.CancelMode(), Is.Null);
    }

    [Test]
    public void RetryLineAfterMissIsANewAttempt()
    {
        var tracker = new CutAttemptTracker();
        tracker.BeginMode();
        tracker.SubmitLine();
        string miss = tracker.CompleteLine();
        tracker.SubmitLine();
        string canceledPreview = tracker.CancelMode();
        Assert.That(canceledPreview, Is.Not.Null);
        Assert.That(canceledPreview, Is.Not.EqualTo(miss));
    }

    [Test]
    public void CommittedLineCannotBeRecordedTwice()
    {
        var tracker = new CutAttemptTracker();
        tracker.BeginMode();
        tracker.SubmitLine();
        Assert.That(tracker.CompleteLine(), Is.Not.Null);
        Assert.That(tracker.CompleteLine(), Is.Null);
        tracker.EndMode();
        Assert.That(tracker.CancelMode(), Is.Null);
    }

    [Test]
    public void ModeRestartDoesNotReuseThePreviousAttempt()
    {
        var tracker = new CutAttemptTracker();
        tracker.BeginMode();
        tracker.SubmitLine();
        tracker.EndMode();
        tracker.BeginMode();
        Assert.That(tracker.CancelMode(), Is.Not.Null);
        Assert.That(tracker.CompleteLine(), Is.Null);
    }

    [Test]
    public void OutsideCutModeNoResultIsRecorded()
    {
        var tracker = new CutAttemptTracker();
        tracker.SubmitLine();
        Assert.That(tracker.CompleteLine(), Is.Null);
        Assert.That(tracker.CancelMode(), Is.Null);
    }

    [Test]
    public void QueuedEventKeepsItsSegmentAndTimeAfterSceneChange()
    {
        var session = new AnalyticsSession("beta-test", true);
        session.BeginSegment(Tutorial(3), 2, 2, 12d);
        CutAnalyticsEvent item = session.Capture(NewId(), CutOutcome.Miss, -1f, 17d);
        session.LeaveScene();
        session.BeginSegment(Level01(), 2, 2, 20d);
        Assert.That(item.level, Is.EqualTo("tutorial"));
        Assert.That(item.scene, Is.EqualTo("Tutorial"));
        Assert.That(item.segment, Is.EqualTo(3));
        Assert.That(item.segment_count, Is.EqualTo(4));
        Assert.That(item.t_attempt, Is.EqualTo(5d));
    }

    [Test]
    public void SessionAndPlayerSurviveRestartButAttemptTimerResets()
    {
        string player = NewId();
        var session = new AnalyticsSession("beta-test", true, player);
        session.BeginSegment(Level01(), 2, 2, 2d);
        var before = session.Capture(NewId(), CutOutcome.Miss, -1f, 8d);
        session.Restart("manual", 0, 2, 9d);
        session.LeaveScene();
        session.BeginSegment(Level01(), 2, 2, 10d);
        var after = session.Capture(NewId(), CutOutcome.Cancel, -1f, 12d);
        Assert.That(after.session_id, Is.EqualTo(before.session_id));
        Assert.That(after.player_id, Is.EqualTo(player));
        Assert.That(after.sequence, Is.GreaterThan(before.sequence));
        Assert.That(after.t_attempt, Is.EqualTo(2d));
        Assert.That(after.level, Is.EqualTo("level01"));
    }

    [Test]
    public void InvalidPlayerIdIsReplacedWithARandomOne()
    {
        var session = new AnalyticsSession("beta-test", true, "not-a-hex-id");
        Assert.That(AnalyticsFormat.IsHexId(session.PlayerId), Is.True);
    }

    [Test]
    public void NewPlaySessionGetsANewSessionId()
    {
        Assert.That(new AnalyticsSession("a", true).SessionId,
            Is.Not.EqualTo(new AnalyticsSession("a", true).SessionId));
    }

    [TestCase(CutOutcome.Success, "cut_success", 0.7f)]
    [TestCase(CutOutcome.Miss, "cut_miss", -1f)]
    [TestCase(CutOutcome.TooSmall, "cut_too_small", -1f)]
    [TestCase(CutOutcome.Cancel, "cut_cancel", -1f)]
    public void CutEventsCarryTheDocumentedNameAndKeptRatio(CutOutcome outcome, string eventName, float expectedRatio)
    {
        var session = new AnalyticsSession("beta-test", true);
        session.BeginSegment(Tutorial(1), 3, 3, 1d);
        string id = NewId();
        var item = session.Capture(id, outcome, 0.7f, 6d);
        string json = JsonUtility.ToJson(item);
        StringAssert.Contains("\"event\":\"" + eventName + "\"", json);
        Assert.That(item.event_id, Is.EqualTo(id));
        Assert.That(item.kept_ratio, Is.EqualTo(expectedRatio).Within(0.0001f));
        Assert.That(item.is_editor, Is.True);
        Assert.That(item.schema_version, Is.EqualTo(3));
    }

    [Test]
    public void CutWithoutAValidAttemptIdIsIgnored()
    {
        var session = new AnalyticsSession("beta-test", true);
        session.BeginSegment(Tutorial(1), 3, 3, 1d);
        Assert.That(session.Capture("first", CutOutcome.Miss, -1f, 2d), Is.Null);
    }

    [Test]
    public void ElapsedTimeIncludesThePausedCutMode()
    {
        float previousScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            var session = new AnalyticsSession("beta-test", true);
            session.BeginSegment(Tutorial(1), 3, 3, 10d);
            Assert.That(session.Capture(NewId(), CutOutcome.Cancel, -1f, 24d).t_attempt, Is.EqualTo(14d));
        }
        finally { Time.timeScale = previousScale; }
    }

    [Test]
    public void BoundedQueuePreservesTheInFlightPayloadAndRejectsOverflow()
    {
        var queue = new AnalyticsDelivery(2);
        var first = new AnalyticsPayload("u1", "first");
        Assert.That(queue.Enqueue(first), Is.True);
        Assert.That(queue.Enqueue(new AnalyticsPayload("u2", "second")), Is.True);
        Assert.That(queue.Enqueue(new AnalyticsPayload("u3", "overflow")), Is.False);
        Assert.That(queue.Enqueue(null), Is.False);
        // Retrying does not dequeue or rebuild the URL/body.
        Assert.That(queue.Peek(), Is.SameAs(first));
        Assert.That(queue.Peek(), Is.SameAs(first));
        queue.Remove();
        Assert.That(queue.Peek().Json, Is.EqualTo("second"));
    }

    [TestCase(0, true)]
    [TestCase(408, true)]
    [TestCase(429, true)]
    [TestCase(503, true)]
    [TestCase(400, false)]
    [TestCase(401, false)]
    [TestCase(403, false)]
    public void OnlyTransientErrorsAreRetried(long status, bool expected)
    {
        Assert.That(AnalyticsDelivery.ShouldRetry(status, 1, 4), Is.EqualTo(expected));
    }

    [Test]
    public void RetriesHaveAFiniteBudgetAndIncreasingDelay()
    {
        Assert.That(AnalyticsDelivery.ShouldRetry(503, 4, 4), Is.False);
        Assert.That(AnalyticsDelivery.RetryDelay(1, 0f), Is.EqualTo(1f));
        Assert.That(AnalyticsDelivery.RetryDelay(3, 0.5f), Is.EqualTo(4.5f));
    }

    [Test]
    public void FirebaseKeysAreReadableAndSortable()
    {
        Assert.That(AnalyticsFormat.PlayerKey("7f3a9c21aaaaaaaaaaaaaaaaaaaaaaaa"), Is.EqualTo("p_7f3a9c21"));
        Assert.That(AnalyticsFormat.SessionKey(new DateTime(2026, 11, 10, 14, 32, 59), "a1b2c3d4bbbbbbbbbbbbbbbbbbbbbbbb"),
            Is.EqualTo("20261110-1432-a1b2c3d4"));
        Assert.That(AnalyticsFormat.EventKey(5, "checkpoint_reached"), Is.EqualTo("0005_checkpoint_reached"));
        Assert.That(AnalyticsFormat.EventKey(12345, "use_cut"), Is.EqualTo("12345_use_cut"));
    }

    [Test]
    public void EventUrlFollowsTheV3Layout()
    {
        string player = "7f3a9c21" + new string('a', 24);
        var session = new AnalyticsSession("beta1-all-metrics", false, player);
        var events = session.BeginSegment(Tutorial(1), 3, 3, 0d);
        Assert.That(AnalyticsFormat.TryEventUrl("https://x-default-rtdb.firebaseio.com/", "20261110-1432-a1b2c3d4", events[0], out string url), Is.True);
        Assert.That(url, Is.EqualTo("https://x-default-rtdb.firebaseio.com/v3/beta1-all-metrics/p_7f3a9c21/20261110-1432-a1b2c3d4/0001_level_start.json?print=silent"));
    }

    [TestCase("beta 1")]
    [TestCase("beta/1")]
    [TestCase("beta.1")]
    [TestCase("")]
    public void EventUrlRejectsBuildIdsThatAreNotFirebaseKeys(string build)
    {
        var session = new AnalyticsSession(build, true);
        var item = session.BeginSegment(Tutorial(1), 3, 3, 0d)[0];
        Assert.That(AnalyticsFormat.TryEventUrl("https://x.firebaseio.com", "20261110-1432-a1b2c3d4", item, out string url), Is.False);
        Assert.That(url, Is.Null);
    }

    [Test]
    public void UploadedJsonHasExactlyTheFieldsTheRulesAccept()
    {
        // Keep in step with docs/analytics/firebase-database.rules.json (v3 branch).
        string[] expected =
        {
            "server_time", "schema_version", "event", "event_id", "player_id", "session_id", "sequence", "build",
            "is_editor", "level", "level_title", "scene", "segment", "segment_count", "segment_name", "visit_id",
            "attempt", "t_attempt", "t_segment", "t_level", "level_restarts", "cuts_left", "cuts_budget",
            "kept_ratio", "reason", "first_use"
        };
        var fields = new[] { "server_time" }.Concat(typeof(CutAnalyticsEvent)
            .GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name)).ToArray();
        Assert.That(fields, Is.EquivalentTo(expected));

        var session = new AnalyticsSession("beta-test", true);
        string json = AnalyticsFormat.WithServerTimestamp(JsonUtility.ToJson(session.BeginSegment(Tutorial(1), 3, 3, 0d)[0]));
        StringAssert.StartsWith("{\"server_time\":{\".sv\":\"timestamp\"},\"schema_version\":3,", json);
        Assert.That(AnalyticsFormat.WithServerTimestamp("{}"), Is.EqualTo("{\"server_time\":{\".sv\":\"timestamp\"}}"));
    }

    [TestCase("https://team3-game-37d24-default-rtdb.firebaseio.com/", true)]
    [TestCase("https://example-default-rtdb.europe-west1.firebasedatabase.app", true)]
    [TestCase("https://console.firebase.google.com/project/test", false)]
    [TestCase("https://example.firebaseio.com/events.json", false)]
    [TestCase("https://example.firebaseio.com/?auth=secret", false)]
    [TestCase("https://example.firebaseio.com.attacker.com/", false)]
    [TestCase("http://example.firebaseio.com/", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void ConfigurationAcceptsOnlyTheDatabaseRoot(string url, bool valid)
    {
        var settings = ScriptableObject.CreateInstance<FirebaseAnalyticsSettings>();
        try
        {
            settings.databaseUrl = url;
            Assert.That(settings.TryGetDatabaseRoot(out string root), Is.EqualTo(valid));
            if (valid)
            {
                StringAssert.StartsWith("https://", root);
                Assert.That(root.EndsWith("/"), Is.False);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }

    [Test]
    public void ConfigurationRejectsABuildIdThatCannotBeAFolder()
    {
        var settings = ScriptableObject.CreateInstance<FirebaseAnalyticsSettings>();
        try
        {
            settings.databaseUrl = "https://team3-game-37d24-default-rtdb.firebaseio.com/";
            Assert.That(settings.buildId, Is.EqualTo("beta1-all-metrics"));
            settings.buildId = "beta 1";
            Assert.That(settings.TryGetDatabaseRoot(out string root), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }
}
