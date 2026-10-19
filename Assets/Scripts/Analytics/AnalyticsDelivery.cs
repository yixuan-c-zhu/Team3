using System;
using System.Collections.Generic;

namespace Team3.Analytics
{
    /// <summary>Bounded FIFO of immutable JSON, including the request currently in flight.</summary>
    public sealed class AnalyticsDelivery
    {
        readonly Queue<string> payloads = new Queue<string>();
        readonly int capacity;
        public int Count => payloads.Count;

        public AnalyticsDelivery(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
        }

        public bool Enqueue(string json)
        {
            if (payloads.Count >= capacity) return false;
            payloads.Enqueue(json);
            return true;
        }

        public string Peek() => payloads.Peek();
        public void Remove() => payloads.Dequeue();
        public void Clear() => payloads.Clear();

        public static bool ShouldRetry(long responseCode, int attemptsMade, int maxAttempts)
        {
            if (attemptsMade >= maxAttempts) return false;
            return responseCode == 0 || responseCode == 408 || responseCode == 429 || responseCode >= 500;
        }

        public static float RetryDelay(int attemptsMade, float jitter)
        {
            return (float)Math.Min(30d, Math.Pow(2d, Math.Max(0, attemptsMade - 1)))
                + Math.Max(0f, Math.Min(1f, jitter));
        }
    }
}
