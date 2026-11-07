using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Team3.Analytics
{
    /// <summary>One upload: the full Firebase URL (fixed key, so a retried PUT overwrites itself) and the JSON body.</summary>
    public sealed class AnalyticsPayload
    {
        public readonly string Url;
        public readonly string Json;

        public AnalyticsPayload(string url, string json)
        {
            Url = url;
            Json = json;
        }
    }

    /// <summary>Bounded FIFO of immutable payloads, including the request currently in flight.</summary>
    public sealed class AnalyticsDelivery
    {
        readonly Queue<AnalyticsPayload> payloads = new Queue<AnalyticsPayload>();
        readonly int capacity;
        public int Count => payloads.Count;

        public AnalyticsDelivery(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
        }

        public bool Enqueue(AnalyticsPayload payload)
        {
            if (payload == null || payloads.Count >= capacity) return false;
            payloads.Enqueue(payload);
            return true;
        }

        public AnalyticsPayload Peek() => payloads.Peek();
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

    /// <summary>
    /// Firebase layout v3: /v3/&lt;build&gt;/&lt;player_key&gt;/&lt;session_key&gt;/&lt;event_key&gt;.
    /// Must match docs/analytics/firebase-database.rules.json.
    /// </summary>
    public static class AnalyticsFormat
    {
        public const string RootNode = "v3";
        const string ServerTimestamp = "\"server_time\":{\".sv\":\"timestamp\"}";
        static readonly Regex HexId = new Regex("^[a-f0-9]{32}$");
        static readonly Regex BuildKey = new Regex("^[A-Za-z0-9_-]{1,64}$");
        static readonly Regex EventName = new Regex("^[a-z_]{1,32}$");

        public static bool IsHexId(string value) => value != null && HexId.IsMatch(value);
        public static bool IsValidBuild(string build) => build != null && BuildKey.IsMatch(build);

        /// <summary>p_ + first 8 hex digits of the anonymous player ID.</summary>
        public static string PlayerKey(string playerId) => "p_" + playerId.Substring(0, 8);

        /// <summary>Player's clock (for sorting in the console only) + first 8 hex digits of the session ID.</summary>
        public static string SessionKey(DateTime startedAt, string sessionId)
            => startedAt.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + "-" + sessionId.Substring(0, 8);

        /// <summary>Zero-padded sequence + event name, so events sort in play order in the console.</summary>
        public static string EventKey(int sequence, string eventName)
            => sequence.ToString("D4", CultureInfo.InvariantCulture) + "_" + eventName;

        public static bool TryEventUrl(string databaseRoot, string sessionKey, CutAnalyticsEvent item, out string url)
        {
            url = null;
            if (item == null || string.IsNullOrEmpty(databaseRoot) || string.IsNullOrEmpty(sessionKey)) return false;
            if (!IsValidBuild(item.build) || !IsHexId(item.player_id) || item.sequence < 1
                || item.@event == null || !EventName.IsMatch(item.@event)) return false;
            url = databaseRoot.TrimEnd('/') + "/" + RootNode + "/" + item.build + "/" + PlayerKey(item.player_id)
                + "/" + sessionKey + "/" + EventKey(item.sequence, item.@event) + ".json?print=silent";
            return true;
        }

        /// <summary>Adds Firebase's server timestamp placeholder; JsonUtility cannot serialize it.</summary>
        public static string WithServerTimestamp(string json)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '{') return json;
            return json.Length > 2 && json[1] != '}'
                ? "{" + ServerTimestamp + "," + json.Substring(1)
                : "{" + ServerTimestamp + "}";
        }
    }
}
