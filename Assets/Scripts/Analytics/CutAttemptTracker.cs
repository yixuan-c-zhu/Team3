using System;

namespace Team3.Analytics
{
    /// <summary>One terminal result per submitted line, or one early mode cancellation.</summary>
    public sealed class CutAttemptTracker
    {
        bool inCutMode;
        bool submittedLine;
        string pendingId;

        public void BeginMode()
        {
            inCutMode = true;
            submittedLine = false;
            pendingId = null;
        }

        // Call only after the drag and cutting plane have passed their validity checks.
        public void SubmitLine()
        {
            if (!inCutMode) return;
            submittedLine = true;
            pendingId = Guid.NewGuid().ToString("N");
        }

        public string CompleteLine()
        {
            string id = pendingId;
            pendingId = null;
            return id;
        }

        public string CancelMode()
        {
            if (!inCutMode) return null;
            // Leaving after an already recorded miss/too-small is not a second outcome.
            string id = pendingId ?? (!submittedLine ? Guid.NewGuid().ToString("N") : null);
            EndMode();
            return id;
        }

        public void EndMode()
        {
            inCutMode = false;
            submittedLine = false;
            pendingId = null;
        }
    }
}
