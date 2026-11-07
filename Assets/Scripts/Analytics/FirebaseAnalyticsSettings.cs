using System;
using UnityEngine;

namespace Team3.Analytics
{
    [CreateAssetMenu(fileName = "Team3AnalyticsSettings", menuName = "Team3/Analytics Settings")]
    public sealed class FirebaseAnalyticsSettings : ScriptableObject
    {
        public const string ResourceName = "Team3AnalyticsSettings";

        [Tooltip("Disable to stop both event collection and uploads.")]
        public bool collectionEnabled = true;
        [Tooltip("Root HTTPS URL copied from Firebase Realtime Database. Empty means local preview only.")]
        public string databaseUrl = "";
        [Tooltip("Change for each playtest build so results land in their own folder under /v3. Letters, digits, - and _ only.")]
        public string buildId = "beta1-all-metrics";
        [Tooltip("Editor events always carry is_editor=true. Keep off for normal development.")]
        public bool uploadInEditor = false;
        public bool logEventsInEditor = true;
        [Range(16, 1024)] public int queueCapacity = 256;
        [Range(1, 6)] public int maxAttempts = 4;
        [Range(5, 60)] public int requestTimeoutSeconds = 10;

        /// <summary>The database root (https://host) when the URL and build ID are usable; events go under /v3/&lt;build&gt;/...</summary>
        public bool TryGetDatabaseRoot(out string root)
        {
            root = null;
            if (!AnalyticsFormat.IsValidBuild(buildId)) return false;
            if (!Uri.TryCreate((databaseUrl ?? "").Trim(), UriKind.Absolute, out Uri uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || uri.AbsolutePath != "/") return false;
            bool firebaseHost = uri.Host.EndsWith(".firebaseio.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".firebasedatabase.app", StringComparison.OrdinalIgnoreCase);
            if (!firebaseHost) return false;
            root = uri.GetLeftPart(UriPartial.Authority);
            return true;
        }
    }
}
