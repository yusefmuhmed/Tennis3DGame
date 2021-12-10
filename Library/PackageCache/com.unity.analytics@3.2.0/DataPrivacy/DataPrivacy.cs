#if ENABLE_CLOUD_SERVICES_ANALYTICS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace UnityEngine.Analytics
{
    public class DataPrivacy
    {
        [Serializable]
        internal struct UserPostData
        {
            public string appid;
            public string userid;
            public long sessionid;
            public string platform;
            public UInt32 platformid;
            public string sdk_ver;
            public bool debug_device;
            public string deviceid;
            public string plugin_ver;
        }

        // Nested structs must be Serializable for JsonUtility
        [Serializable]
        internal struct OptOutStatus
        {
            public bool optOut;
            public bool analyticsEnabled;
            public bool deviceStatsEnabled;
            public bool limitUserTracking;
            public bool performanceReportingEnabled;
        }

        [Serializable]
        internal struct RequestData
        {
            public string date;
        }

        [Serializable]
        internal struct OptOutResponse
        {
            public RequestData request;
            public OptOutStatus status;
        }

        [Serializable]
        internal struct TokenData
        {
            public string url;
            public string token;
        }

        const string kVersion = "2.0.1";
        const string kVersionString = "DataPrivacyPackage/" + kVersion;

        internal const string kBaseUrl = "https://data-optout-service.uca.cloud.unity3d.com";
        const string kOptOutUrl = kBaseUrl + "/player/opt_out";
        const string kTokenUrl = kBaseUrl + "/token";

        const string kPrefAnalyticsEnabled = "data.analyticsEnabled";
        const string kPrefDeviceStatsEnabled = "data.deviceStatsEnabled";
        const string kPrefLimitUserTracking = "data.limitUserTracking";
        const string kPrefPerformanceReportingEnabled = "data.performanceReportingEnabled";
        const string kOptOut = "data.optOut";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            // Skip initialization if literally all analytics are disabled.
            if (Analytics.enabled || Analytics.deviceStatsEnabled || PerformanceReporting.enabled)
            {
                FetchOptOutStatus();
            }
        }

        static OptOutStatus LoadFromPlayerPrefs()
        {
            OptOutStatus optOutStatus = new OptOutStatus();

            optOutStatus.analyticsEnabled = PlayerPrefs.GetInt(kPrefAnalyticsEnabled, 1) == 1;
            optOutStatus.deviceStatsEnabled = PlayerPrefs.GetInt(kPrefDeviceStatsEnabled, 1) == 1;
            optOutStatus.limitUserTracking = PlayerPrefs.GetInt(kPrefLimitUserTracking, 0) == 1;
            optOutStatus.performanceReportingEnabled = PlayerPrefs.GetInt(kPrefPerformanceReportingEnabled, 1) == 1;
            optOutStatus.optOut = PlayerPrefs.GetInt(kOptOut, 0) == 1;
            return optOutStatus;
        }

        static void SaveToPlayerPrefs(OptOutStatus optOutStatus)
        {
            PlayerPrefs.SetInt(kPrefAnalyticsEnabled, optOutStatus.analyticsEnabled ? 1 : 0);
            PlayerPrefs.SetInt(kPrefDeviceStatsEnabled, optOutStatus.deviceStatsEnabled ? 1 : 0);
            PlayerPrefs.SetInt(kPrefLimitUserTracking, optOutStatus.limitUserTracking ? 1 : 0);
            PlayerPrefs.SetInt(kPrefPerformanceReportingEnabled, optOutStatus.performanceReportingEnabled ? 1 : 0);
            PlayerPrefs.SetInt(kOptOut, optOutStatus.optOut ? 1 : 0);
        }

        internal static void SetOptOutStatus(DataPrivacy.OptOutStatus optOutStatus)
        {
            // Set each flag based on the settings passed in, but prioritize more restrictive
            // settings, so if a developer explicitly disables something in code, we don't
            // enable it with the default values passed back from the server.
            try
            {
                Analytics.enabled = Analytics.enabled && optOutStatus.analyticsEnabled;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            try
            {
                Analytics.deviceStatsEnabled = Analytics.deviceStatsEnabled && optOutStatus.deviceStatsEnabled;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            try
            {
                Analytics.limitUserTracking = Analytics.limitUserTracking || optOutStatus.limitUserTracking;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
#if UNITY_ANALYTICS
            try
            {
                PerformanceReporting.enabled = PerformanceReporting.enabled && optOutStatus.performanceReportingEnabled;
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
#endif
        }

        internal static UserPostData GetUserData()
        {
            var postData = new UserPostData
            {
                appid = Application.cloudProjectId,
                userid = AnalyticsSessionInfo.userId,
                sessionid = AnalyticsSessionInfo.sessionId,
                platform = Application.platform.ToString(),
                platformid = (UInt32)Application.platform,
                sdk_ver = Application.unityVersion,
                debug_device = Debug.isDebugBuild,
                deviceid = SystemInfo.deviceUniqueIdentifier,
                plugin_ver = kVersionString
            };

            return postData;
        }

        static string GetUserAgent()
        {
            var message = "UnityPlayer/{0} ({1}/{2}{3} {4})";
            return String.Format(message,
                Application.unityVersion,
                Application.platform.ToString(),
                (UInt32)Application.platform,
                Debug.isDebugBuild ? "-dev" : "",
                kVersionString);
        }

        static String getErrorString(UnityWebRequest www)
        {
            var json = www.downloadHandler.text;
            var error = www.error;
            if (String.IsNullOrEmpty(error))
            {
                // 5.5 sometimes fails to parse an error response, and the only clue will be
                // in www.responseHeadersString, which isn't accessible.
                error = "Empty response";
            }

            if (!String.IsNullOrEmpty(json))
            {
                error += ": " + json;
            }

            return error;
        }

        public static void FetchOptOutStatus(Action<bool> optOutAction = null)
        {
            // Load from player prefs
            var localOptOutStatus = LoadFromPlayerPrefs();
            SetOptOutStatus(localOptOutStatus);

            var userData = GetUserData();

            if (string.IsNullOrEmpty(userData.appid))
            {
                Debug.LogError("Could not find AppID for the project. Create a new Unity Project ID or link to an existing ID in the Services window.");
            }

            if (string.IsNullOrEmpty(userData.userid))
            {
                Debug.LogError("Could not find UserID!");
            }

            if (string.IsNullOrEmpty(userData.deviceid))
            {
                Debug.LogError("Could not find DeviceID!");
            }

            var query = string.Format(kOptOutUrl + "?appid={0}&userid={1}&deviceid={2}", userData.appid, userData.userid, userData.deviceid);
            var baseUri = new Uri(kBaseUrl);
            var uri = new Uri(baseUri, query);

            var www = UnityWebRequest.Get(uri.ToString());
#if !UNITY_WEBGL
            www.SetRequestHeader("User-Agent", GetUserAgent());
#endif
            var async = www.SendWebRequest();
            async.completed += (AsyncOperation async2) =>
                {
                    var json = www.downloadHandler.text;
                    if (!String.IsNullOrEmpty(www.error) || String.IsNullOrEmpty(json))
                    {
                        var error = getErrorString(www);
                        Debug.LogWarning(String.Format("Failed to load data opt-opt status from {0}: {1}", www.url, error));

                        if (optOutAction != null)
                        {
                            optOutAction(localOptOutStatus.optOut);
                        }
                        return;
                    }

                    OptOutStatus optOutStatus;
                    try
                    {
                        OptOutResponse response = JsonUtility.FromJson<OptOutResponse>(json);
                        optOutStatus = response.status;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(String.Format("Failed to load data opt-opt status from {0}: {1}", www.url, e.ToString()));
                        if (optOutAction != null)
                        {
                            optOutAction(localOptOutStatus.optOut);
                        }
                        return;
                    }

                    SetOptOutStatus(optOutStatus);
                    SaveToPlayerPrefs(optOutStatus);

                    if (optOutAction != null)
                    {
                        optOutAction(optOutStatus.optOut);
                    }
                };
        }

        public static void FetchPrivacyUrl(Action<string> success, Action<string> failure = null)
        {
            string postJson = JsonUtility.ToJson(GetUserData());
            byte[] bytes = Encoding.UTF8.GetBytes(postJson);
            var uploadHandler = new UploadHandlerRaw(bytes);
            uploadHandler.contentType = "application/json";

            var www = UnityWebRequest.Post(kTokenUrl, "");
            www.uploadHandler = uploadHandler;
#if !UNITY_WEBGL
            www.SetRequestHeader("User-Agent", GetUserAgent());
#endif
            var async = www.SendWebRequest();

            async.completed += (AsyncOperation async2) =>
                {
                    var json = www.downloadHandler.text;
                    if (!String.IsNullOrEmpty(www.error) || String.IsNullOrEmpty(json))
                    {
                        var error = getErrorString(www);
                        if (failure != null)
                        {
                            failure(error);
                        }
                    }
                    else
                    {
                        TokenData tokenData;
                        tokenData.url = ""; // Just to quell "possibly unassigned" error
                        try
                        {
                            tokenData = JsonUtility.FromJson<TokenData>(json);
                        }
                        catch (Exception e)
                        {
                            failure(e.ToString());
                        }

                        success(tokenData.url);
                    }
                };
        }
    }
}
#endif //ENABLE_CLOUD_SERVICES_ANALYTICS
