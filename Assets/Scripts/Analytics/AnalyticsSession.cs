using System;
using System.Collections.Generic;

namespace Team3.Analytics
{
    public enum CutOutcome { Success, Miss, TooSmall, Cancel }
    public enum Mechanic { Cut, ViewSwitch, ColorShift, SwitchPiece, Camera }

    /// <summary>Where the player is, using the names the player sees in the menu (see docs/analytics/v3-schema-proposal.md).</summary>
    public struct SegmentInfo
    {
        public string Scene;        // Unity scene name, for debugging only
        public string Level;        // tutorial / level01 / level02
        public string LevelTitle;   // menu title
        public int Segment;         // 1-based
        public int SegmentCount;    // segments in this level (Tutorial = 3)
        public string SegmentName;  // player-facing lesson title

        public SegmentInfo(string scene, string level, string levelTitle, int segment, int segmentCount, string segmentName)
        {
            Scene = scene;
            Level = level;
            LevelTitle = levelTitle;
            SegmentCount = Math.Max(1, segmentCount);
            Segment = Math.Max(1, Math.Min(SegmentCount, segment));
            SegmentName = segmentName;
        }

        public bool IsValid => !string.IsNullOrWhiteSpace(Scene) && !string.IsNullOrWhiteSpace(Level)
            && !string.IsNullOrWhiteSpace(LevelTitle) && !string.IsNullOrWhiteSpace(SegmentName);
    }

    /// <summary>Schema v3. Field order is the JSON order. server_time is added at upload (see AnalyticsFormat).</summary>
    [Serializable]
    public sealed class CutAnalyticsEvent
    {
        public int schema_version = 3;
        public string @event;
        public string event_id;
        public string player_id;
        public string session_id;
        public int sequence;
        public string build;
        public bool is_editor;
        public string level;
        public string level_title;
        public string scene;
        public int segment;
        public int segment_count;
        public string segment_name;
        public string visit_id;
        public int attempt;
        public double t_attempt;
        public double t_segment;
        public double t_level;
        public int level_restarts;
        public int cuts_left;
        public int cuts_budget;
        public float kept_ratio = -1f;
        public string reason = "";
        public bool first_use;
    }

    /// <summary>
    /// Context captured at the time of the result, never at upload time.
    /// A level run starts with level_start and lasts until level_complete or until the player leaves the scene
    /// (a restart reload keeps the run). A segment visit lasts until its checkpoint/completion; restarts keep the visit.
    /// </summary>
    public sealed class AnalyticsSession
    {
        readonly string sessionId = Guid.NewGuid().ToString("N");
        readonly string playerId;
        readonly string buildId;
        readonly bool isEditor;
        int sequence;

        SegmentInfo current;
        bool hasSegment;
        string visitId;
        int attempt, cutsLeft, cutsBudget;
        double attemptStartedAt, previousAttemptSeconds;
        bool active, cleared, pendingRestart;

        bool levelActive;          // a level run is in progress
        bool continuingLevel;      // the last segment ended at a checkpoint; the next segment continues the run
        double levelSeconds;       // finished attempt time in this run
        int levelRestarts;

        readonly HashSet<Mechanic> discovered = new HashSet<Mechanic>();

        public string SessionId => sessionId;
        public string PlayerId => playerId;

        public AnalyticsSession(string buildId, bool isEditor, string playerId = null)
        {
            this.buildId = buildId;
            this.isEditor = isEditor;
            this.playerId = AnalyticsFormat.IsHexId(playerId) ? playerId : Guid.NewGuid().ToString("N");
        }

        /// <summary>Starts a segment. Returns level_start (when a new run begins) followed by segment_start, or nothing for a duplicate call.</summary>
        public CutAnalyticsEvent[] BeginSegment(SegmentInfo info, int remaining, int budget, double realtime)
        {
            if (!info.IsValid) return Array.Empty<CutAnalyticsEvent>();
            bool sameSegment = hasSegment && current.Scene == info.Scene && current.Segment == info.Segment;
            if (active && !cleared && sameSegment) return Array.Empty<CutAnalyticsEvent>();

            bool restartReload = pendingRestart && sameSegment;
            bool continueRun = restartReload
                || (levelActive && continuingLevel && hasSegment && current.Scene == info.Scene);
            if (!continueRun)
            {
                levelActive = true;
                levelSeconds = 0d;
                levelRestarts = 0;
            }
            if (restartReload)
            {
                attempt++;
            }
            else
            {
                visitId = Guid.NewGuid().ToString("N");
                attempt = 1;
                previousAttemptSeconds = 0d;
                discovered.Clear();
            }

            current = info;
            hasSegment = true;
            attemptStartedAt = realtime;
            active = true;
            cleared = false;
            pendingRestart = false;
            continuingLevel = false;
            UpdateCuts(remaining, budget);

            if (continueRun) return new[] { Create("segment_start", realtime) };
            var levelStart = Create("level_start", realtime);
            return new[] { levelStart, Create("segment_start", realtime) };
        }

        public void LeaveScene()
        {
            active = false;
            // A restart carries its visit, run and accumulated time across the scene reload.
            if (pendingRestart) return;
            cleared = false;
            levelActive = false;
            continuingLevel = false;
        }

        public void UpdateCuts(int remaining, int budget)
        {
            cutsBudget = Math.Max(0, budget);
            cutsLeft = Math.Max(0, Math.Min(cutsBudget, remaining));
        }

        /// <summary>checkpoint_reached when another segment follows, level_complete at the level's exit.</summary>
        public CutAnalyticsEvent CompleteSegment(bool levelComplete, int remaining, int budget, double realtime)
        {
            if (!active || cleared) return null;
            UpdateCuts(remaining, budget);
            var item = Create(levelComplete ? "level_complete" : "checkpoint_reached", realtime);
            cleared = true;
            if (levelComplete)
            {
                levelActive = false;
                continuingLevel = false;
            }
            else
            {
                levelSeconds += item.t_attempt;
                continuingLevel = true;
            }
            return item;
        }

        public CutAnalyticsEvent Restart(string reason, int remaining, int budget, double realtime)
        {
            // A replay from the completion screen is a new run, not a struggle restart.
            if (!active || cleared || pendingRestart) return null;
            UpdateCuts(remaining, budget);
            levelRestarts++;
            var item = Create("restart", realtime, reason);
            previousAttemptSeconds = item.t_segment;
            levelSeconds += item.t_attempt;
            pendingRestart = true;
            active = false;
            return item;
        }

        public CutAnalyticsEvent UseMechanic(Mechanic mechanic, string reason, double realtime)
        {
            if (!active || cleared) return null;
            string eventName;
            switch (mechanic)
            {
                case Mechanic.Cut: eventName = "use_cut"; break;
                case Mechanic.ViewSwitch: eventName = "use_view_switch"; break;
                case Mechanic.ColorShift: eventName = "use_color_shift"; break;
                case Mechanic.SwitchPiece: eventName = "use_switch_piece"; break;
                case Mechanic.Camera: eventName = "use_camera"; break;
                default: return null;
            }
            var item = Create(eventName, realtime, reason);
            item.first_use = discovered.Add(mechanic);
            return item;
        }

        public CutAnalyticsEvent Capture(string attemptId, CutOutcome outcome, float keptRatio, double realtime)
        {
            if (!AnalyticsFormat.IsHexId(attemptId) || !active || cleared) return null;
            string eventName;
            switch (outcome)
            {
                case CutOutcome.Success: eventName = "cut_success"; break;
                case CutOutcome.Miss: eventName = "cut_miss"; break;
                case CutOutcome.TooSmall: eventName = "cut_too_small"; break;
                case CutOutcome.Cancel: eventName = "cut_cancel"; break;
                default: return null;
            }
            var item = Create(eventName, realtime);
            item.event_id = attemptId;
            if (outcome == CutOutcome.Success) item.kept_ratio = Math.Max(0f, Math.Min(1f, keptRatio));
            return item;
        }

        CutAnalyticsEvent Create(string eventName, double realtime, string reason = "")
        {
            double elapsed = Math.Max(0d, realtime - attemptStartedAt);
            return new CutAnalyticsEvent
            {
                @event = eventName,
                event_id = Guid.NewGuid().ToString("N"),
                player_id = playerId,
                session_id = sessionId,
                sequence = ++sequence,
                build = buildId,
                is_editor = isEditor,
                level = current.Level,
                level_title = current.LevelTitle,
                scene = current.Scene,
                segment = current.Segment,
                segment_count = current.SegmentCount,
                segment_name = current.SegmentName,
                visit_id = visitId,
                attempt = attempt,
                t_attempt = elapsed,
                t_segment = previousAttemptSeconds + elapsed,
                t_level = levelSeconds + elapsed,
                level_restarts = levelRestarts,
                cuts_left = cutsLeft,
                cuts_budget = cutsBudget,
                kept_ratio = -1f,
                reason = reason ?? "",
                first_use = false
            };
        }
    }
}
