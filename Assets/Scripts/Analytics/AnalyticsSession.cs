using System;
using System.Collections.Generic;

namespace Team3.Analytics
{
    public enum CutOutcome { Success, Miss, TooSmall, Cancel }
    public enum Mechanic { Cut, ViewSwitch, ColorShift, SwitchPiece, Camera }

    [Serializable]
    public sealed class CutAnalyticsEvent
    {
        public int schema_version = 2;
        public string event_id;
        public string session_id;
        public int sequence;
        public string build;
        public string scene;
        public string room;
        public string @event;
        public float value;
        public double t_room;
        public bool is_editor;
        public string room_visit_id;
        public int room_index;
        public int room_attempt;
        public double t_visit;
        public int cuts_left;
        public int cuts_budget;
        public string reason;
        public bool first_use;
    }

    /// <summary>Context captured at the time of the result, never at upload time.</summary>
    public sealed class AnalyticsSession
    {
        readonly string sessionId = Guid.NewGuid().ToString("N");
        readonly string buildId;
        readonly bool isEditor;
        string scene;
        string room;
        double roomStartedAt;
        int sequence;
        string visitId;
        int roomIndex, attempt, cutsLeft, cutsBudget;
        double previousAttemptSeconds;
        bool active, cleared, pendingRestart;
        readonly HashSet<Mechanic> discovered = new HashSet<Mechanic>();

        public string SessionId => sessionId;

        public AnalyticsSession(string buildId, bool isEditor)
        {
            this.buildId = buildId;
            this.isEditor = isEditor;
        }

        public void EnterScene(string sceneName, double realtime)
        {
            BeginRoom(sceneName, sceneName, 0, 0, 0, realtime);
        }

        public void EnterRoom(string roomId, double realtime)
        {
            BeginRoom(scene, roomId, 0, cutsLeft, cutsBudget, realtime);
        }

        public CutAnalyticsEvent BeginRoom(string sceneName, string roomId, int index, int remaining, int budget, double realtime)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(roomId)) return null;
            bool sameRoom = scene == sceneName && room == roomId;
            if (active && !cleared && sameRoom) return null;
            if (pendingRestart && sameRoom)
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
            scene = sceneName;
            room = roomId;
            roomIndex = Math.Max(0, index);
            roomStartedAt = realtime;
            active = true;
            cleared = false;
            pendingRestart = false;
            UpdateCuts(remaining, budget);
            return Create("room_enter", -1f, realtime);
        }

        public void LeaveScene()
        {
            active = false;
            // A restart carries its visit and accumulated time across the scene reload.
            if (!pendingRestart) cleared = false;
        }

        public void UpdateCuts(int remaining, int budget)
        {
            cutsBudget = Math.Max(0, budget);
            cutsLeft = Math.Max(0, Math.Min(cutsBudget, remaining));
        }

        public CutAnalyticsEvent ClearRoom(int remaining, int budget, double realtime)
        {
            if (!active || cleared) return null;
            UpdateCuts(remaining, budget);
            var item = Create("room_clear", cutsLeft, realtime);
            cleared = true;
            return item;
        }

        public CutAnalyticsEvent RestartRoom(string reason, int remaining, int budget, double realtime)
        {
            // A replay from the completion screen is a new visit, not a struggle restart.
            if (!active || cleared || pendingRestart) return null;
            UpdateCuts(remaining, budget);
            var item = Create("restart", 1f, realtime, reason);
            previousAttemptSeconds = item.t_visit;
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
            var item = Create(eventName, 1f, realtime, reason);
            item.first_use = discovered.Add(mechanic);
            return item;
        }

        public CutAnalyticsEvent Capture(string attemptId, CutOutcome outcome, float keptRatio, double realtime)
        {
            if (string.IsNullOrEmpty(attemptId) || !active || cleared) return null;
            string eventName;
            switch (outcome)
            {
                case CutOutcome.Success: eventName = "cut_success"; break;
                case CutOutcome.Miss: eventName = "cut_miss"; break;
                case CutOutcome.TooSmall: eventName = "cut_too_small"; break;
                case CutOutcome.Cancel: eventName = "cut_cancel"; break;
                default: return null;
            }
            var item = Create(eventName, outcome == CutOutcome.Success ? Math.Max(0f, Math.Min(1f, keptRatio)) : -1f, realtime);
            item.event_id = attemptId;
            return item;
        }

        CutAnalyticsEvent Create(string eventName, float value, double realtime, string reason = "")
        {
            double elapsed = Math.Max(0d, realtime - roomStartedAt);
            return new CutAnalyticsEvent
            {
                event_id = Guid.NewGuid().ToString("N"),
                session_id = sessionId,
                sequence = ++sequence,
                build = buildId,
                scene = scene,
                room = room,
                @event = eventName,
                value = value,
                t_room = elapsed,
                is_editor = isEditor,
                room_visit_id = visitId,
                room_index = roomIndex,
                room_attempt = attempt,
                t_visit = previousAttemptSeconds + elapsed,
                cuts_left = cutsLeft,
                cuts_budget = cutsBudget,
                reason = reason,
                first_use = false
            };
        }
    }
}
