using NUnit.Framework;
using Team3.Analytics;
using UnityEngine;

public class CutAnalyticsTests
{
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
        Assert.That(canceledPreview, Is.Not.Null.And.Not.EqualTo(miss));
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
    public void QueuedEventKeepsItsRoomAndTimeAfterSceneChange()
    {
        var session = new AnalyticsSession("alpha-test", true);
        session.EnterScene("Tutorial", 10d);
        session.EnterRoom("tutorial.angle", 12d);
        CutAnalyticsEvent item = session.Capture("first", CutOutcome.Miss, -1f, 17d);
        session.EnterScene("NewLevel", 20d);
        Assert.That(item.scene, Is.EqualTo("Tutorial"));
        Assert.That(item.room, Is.EqualTo("tutorial.angle"));
        Assert.That(item.t_room, Is.EqualTo(5d));
    }

    [Test]
    public void SessionSurvivesRestartButRoomTimerResets()
    {
        var session = new AnalyticsSession("alpha-test", true);
        session.EnterScene("NewLevel", 2d);
        var before = session.Capture("a", CutOutcome.Miss, -1f, 8d);
        session.RestartRoom("manual", 0, 0, 9d);
        session.LeaveScene();
        session.EnterScene("NewLevel", 10d);
        var after = session.Capture("b", CutOutcome.Cancel, -1f, 12d);
        Assert.That(after.session_id, Is.EqualTo(before.session_id));
        Assert.That(after.sequence, Is.GreaterThan(before.sequence));
        Assert.That(after.t_room, Is.EqualTo(2d));
        Assert.That(after.room, Is.EqualTo("NewLevel"));
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
    public void JsonContainsTheDocumentedEventNamesAndValue(CutOutcome outcome, string eventName, float expectedValue)
    {
        var session = new AnalyticsSession("alpha-test", true);
        session.EnterScene("Level3", 1d);
        var item = session.Capture("event-id", outcome, 0.7f, 6d);
        string json = JsonUtility.ToJson(item);
        var restored = JsonUtility.FromJson<CutAnalyticsEvent>(json);
        Assert.That(json, Does.Contain("\"event\":\"" + eventName + "\""));
        Assert.That(restored.value, Is.EqualTo(expectedValue).Within(0.0001f));
        Assert.That(restored.is_editor, Is.True);
        Assert.That(restored.schema_version, Is.EqualTo(2));
    }

    [Test]
    public void ElapsedTimeIncludesThePausedCutMode()
    {
        float previousScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            var session = new AnalyticsSession("alpha-test", true);
            session.EnterScene("Level1", 10d);
            Assert.That(session.Capture("a", CutOutcome.Cancel, -1f, 24d).t_room, Is.EqualTo(14d));
        }
        finally { Time.timeScale = previousScale; }
    }

    [Test]
    public void BoundedQueuePreservesTheInFlightPayloadAndRejectsOverflow()
    {
        var queue = new AnalyticsDelivery(2);
        Assert.That(queue.Enqueue("first"), Is.True);
        Assert.That(queue.Enqueue("second"), Is.True);
        Assert.That(queue.Enqueue("overflow"), Is.False);
        Assert.That(queue.Peek(), Is.EqualTo("first"));
        // Retrying does not dequeue or serialize a new event_id.
        Assert.That(queue.Peek(), Is.EqualTo("first"));
        queue.Remove();
        Assert.That(queue.Peek(), Is.EqualTo("second"));
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
            Assert.That(settings.TryGetEndpoint(out string endpoint), Is.EqualTo(valid));
            if (valid) Assert.That(endpoint, Does.EndWith("/events.json?print=silent"));
        }
        finally { Object.DestroyImmediate(settings); }
    }
}
