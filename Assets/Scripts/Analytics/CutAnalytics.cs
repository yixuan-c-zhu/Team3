using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Team3.Analytics
{
    /// <summary>Automatically installed once per play session; no scene/prefab wiring required.</summary>
    public sealed class CutAnalytics : MonoBehaviour
    {
        const string PlayerIdKey = "Team3.Analytics.PlayerId";
        const string EditorPlayerIdKey = "Team3.Analytics.EditorPlayerId";

        static CutAnalytics instance;
        FirebaseAnalyticsSettings settings;
        AnalyticsSession session;
        AnalyticsDelivery delivery;
        string databaseRoot;
        string sessionKey;
        bool uploadEnabled;
        UnityWebRequest activeRequest;

        public int RecordedCount { get; private set; }
        public int UploadedCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int PendingCount => delivery == null ? 0 : delivery.Count;
        public string LastEventJson { get; private set; }
        public string LastEventUrl { get; private set; }
        public string LastUploadError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (instance == null) new GameObject("[Analytics]").AddComponent<CutAnalytics>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            settings = Resources.Load<FirebaseAnalyticsSettings>(FirebaseAnalyticsSettings.ResourceName);
            if (settings == null || !settings.collectionEnabled) return;

            session = new AnalyticsSession(settings.buildId, Application.isEditor, LoadPlayerId());
            sessionKey = AnalyticsFormat.SessionKey(DateTime.Now, session.SessionId);
            delivery = new AnalyticsDelivery(Mathf.Clamp(settings.queueCapacity, 16, 1024));
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            uploadEnabled = settings.TryGetDatabaseRoot(out databaseRoot) && (!Application.isEditor || settings.uploadInEditor);
            if (uploadEnabled) StartCoroutine(UploadLoop());
            else if (Application.isEditor)
                Debug.Log("[Analytics] Local event preview only. Configure Team3AnalyticsSettings and enable Upload In Editor to send test data.");
        }

        /// <summary>Anonymous random ID kept in PlayerPrefs (browser storage on WebGL). No personal data.</summary>
        static string LoadPlayerId()
        {
            string key = Application.isEditor ? EditorPlayerIdKey : PlayerIdKey;
            string id = PlayerPrefs.GetString(key, "");
            if (AnalyticsFormat.IsHexId(id)) return id;
            id = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(key, id);
            PlayerPrefs.Save();
            return id;
        }

        void OnSceneUnloaded(Scene scene)
        {
            session.LeaveScene();
        }

        static bool Ready => instance != null && instance.session != null && instance.settings.collectionEnabled;

        /// <summary>Call when a segment starts: level start, after a checkpoint, and after a restart reload.</summary>
        public static void BeginSegment(SegmentInfo info, int cutsLeft, int cutsBudget)
        {
            if (!Ready) return;
            foreach (var item in instance.session.BeginSegment(info, cutsLeft, cutsBudget, Time.realtimeSinceStartupAsDouble))
                instance.Record(item);
        }

        /// <summary>levelComplete = true at the level's exit (level_complete); false at a checkpoint (checkpoint_reached).</summary>
        public static void CompleteSegment(bool levelComplete, int cutsLeft, int cutsBudget)
        {
            if (Ready) instance.Record(instance.session.CompleteSegment(levelComplete, cutsLeft, cutsBudget, Time.realtimeSinceStartupAsDouble));
        }

        public static void Restart(string reason, int cutsLeft, int cutsBudget)
        {
            if (Ready) instance.Record(instance.session.Restart(reason, cutsLeft, cutsBudget, Time.realtimeSinceStartupAsDouble));
        }

        public static void UpdateCuts(int cutsLeft, int cutsBudget)
        {
            if (Ready) instance.session.UpdateCuts(cutsLeft, cutsBudget);
        }

        public static void RecordMechanic(Mechanic mechanic, string reason = "")
        {
            if (Ready) instance.Record(instance.session.UseMechanic(mechanic, reason, Time.realtimeSinceStartupAsDouble));
        }

        public static void RecordCut(string attemptId, CutOutcome outcome, float keptRatio = -1f)
        {
            if (!Ready || string.IsNullOrEmpty(attemptId)) return;
            instance.Record(instance.session.Capture(attemptId, outcome, keptRatio, Time.realtimeSinceStartupAsDouble));
        }

        void Record(CutAnalyticsEvent item)
        {
            if (!settings.collectionEnabled) return;
            if (item == null) return;
            LastEventJson = AnalyticsFormat.WithServerTimestamp(JsonUtility.ToJson(item));
            RecordedCount++;
            bool hasUrl = AnalyticsFormat.TryEventUrl(databaseRoot ?? "https://preview.invalid", sessionKey, item, out string url);
            LastEventUrl = url;
            if (Application.isEditor && settings.logEventsInEditor)
                Debug.Log("[Analytics] " + url + "\n" + LastEventJson);
            if (!uploadEnabled) return;
            if (!hasUrl || !delivery.Enqueue(new AnalyticsPayload(url, LastEventJson)))
            {
                DroppedCount++;
                if (DroppedCount == 1) Debug.LogWarning("[Analytics] Event dropped (queue full or invalid build/player ID).");
            }
        }

        IEnumerator UploadLoop()
        {
            while (uploadEnabled)
            {
                if (!settings.collectionEnabled)
                {
                    DroppedCount += delivery.Count;
                    delivery.Clear();
                    yield return new WaitForSecondsRealtime(0.25f);
                    continue;
                }
                if (delivery.Count == 0)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                    continue;
                }

                // Same URL and body for every retry: a repeated PUT overwrites the same node instead of duplicating it.
                AnalyticsPayload payload = delivery.Peek();
                byte[] body = Encoding.UTF8.GetBytes(payload.Json);
                int attempts = 0;
                bool delivered = false;
                while (settings.collectionEnabled)
                {
                    attempts++;
                    long status;
                    using (var request = new UnityWebRequest(payload.Url, UnityWebRequest.kHttpVerbPUT))
                    {
                        activeRequest = request;
                        request.uploadHandler = new UploadHandlerRaw(body);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.timeout = Mathf.Clamp(settings.requestTimeoutSeconds, 5, 60);
                        yield return request.SendWebRequest();
                        status = request.responseCode;
                        delivered = request.result == UnityWebRequest.Result.Success && status >= 200 && status < 300;
                        LastUploadError = delivered ? null : "HTTP " + status + ": " + request.error;
                        activeRequest = null;
                    }
                    if (delivered || !AnalyticsDelivery.ShouldRetry(status, attempts, Mathf.Clamp(settings.maxAttempts, 1, 6)))
                    {
                        if (status == 401 || status == 403)
                        {
                            uploadEnabled = false;
                            Debug.LogWarning("[Analytics] Firebase rejected writes. Check that the v3 database rules are published, then restart Play Mode. " + LastUploadError);
                        }
                        break;
                    }
                    yield return new WaitForSecondsRealtime(AnalyticsDelivery.RetryDelay(attempts, UnityEngine.Random.value));
                }
                delivery.Remove();
                if (delivered) UploadedCount++;
                else
                {
                    DroppedCount++;
                    Debug.LogWarning("[Analytics] Event could not be uploaded after " + attempts + " attempt(s). " + LastUploadError);
                }
                if (!uploadEnabled)
                {
                    DroppedCount += delivery.Count;
                    delivery.Clear();
                }
            }
        }

        void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (activeRequest != null)
            {
                activeRequest.Abort();
                activeRequest.Dispose();
                activeRequest = null;
            }
            instance = null;
        }
    }
}
