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
        static CutAnalytics instance;
        FirebaseAnalyticsSettings settings;
        AnalyticsSession session;
        AnalyticsDelivery delivery;
        string endpoint;
        bool uploadEnabled;
        UnityWebRequest activeRequest;

        public int RecordedCount { get; private set; }
        public int UploadedCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int PendingCount => delivery == null ? 0 : delivery.Count;
        public string LastEventJson { get; private set; }
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

            session = new AnalyticsSession(settings.buildId, Application.isEditor);
            delivery = new AnalyticsDelivery(Mathf.Clamp(settings.queueCapacity, 16, 1024));
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            uploadEnabled = settings.TryGetEndpoint(out endpoint) && (!Application.isEditor || settings.uploadInEditor);
            if (uploadEnabled) StartCoroutine(UploadLoop());
            else if (Application.isEditor)
                Debug.Log("[Analytics] Local event preview only. Configure Team3AnalyticsSettings and enable Upload In Editor to send test data.");
        }

        void OnSceneUnloaded(Scene scene)
        {
            session.LeaveScene();
        }

        static bool Ready => instance != null && instance.session != null && instance.settings.collectionEnabled;

        public static void EnterRoom(string stableRoomId, int cutsLeft, int cutsBudget, int roomIndex = 0)
        {
            if (Ready) instance.Record(instance.session.BeginRoom(SceneManager.GetActiveScene().name,
                stableRoomId, roomIndex, cutsLeft, cutsBudget, Time.realtimeSinceStartupAsDouble));
        }

        public static void CompleteRoom(int cutsLeft, int cutsBudget)
        {
            if (Ready) instance.Record(instance.session.ClearRoom(cutsLeft, cutsBudget, Time.realtimeSinceStartupAsDouble));
        }

        public static void RestartRoom(string reason, int cutsLeft, int cutsBudget)
        {
            if (Ready) instance.Record(instance.session.RestartRoom(reason, cutsLeft, cutsBudget, Time.realtimeSinceStartupAsDouble));
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
            LastEventJson = JsonUtility.ToJson(item);
            RecordedCount++;
            if (Application.isEditor && settings.logEventsInEditor)
                Debug.Log("[Analytics] " + LastEventJson);
            if (uploadEnabled && !delivery.Enqueue(LastEventJson))
            {
                DroppedCount++;
                if (DroppedCount == 1) Debug.LogWarning("[Analytics] Upload queue full; newest events are being dropped.");
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

                // Keep the same JSON/event_id for retries, even if the player changes rooms.
                byte[] body = Encoding.UTF8.GetBytes(delivery.Peek());
                int attempts = 0;
                bool delivered = false;
                while (settings.collectionEnabled)
                {
                    attempts++;
                    long status;
                    using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
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
                            Debug.LogWarning("[Analytics] Firebase rejected writes. Check the database rules, then restart Play Mode. " + LastUploadError);
                        }
                        break;
                    }
                    yield return new WaitForSecondsRealtime(AnalyticsDelivery.RetryDelay(attempts, Random.value));
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
