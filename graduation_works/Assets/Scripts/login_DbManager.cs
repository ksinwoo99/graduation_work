using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;

public class login_DbManager : MonoBehaviour
{
    public static login_DbManager Instance;
    string baseUrl = "http://13.237.51.219:8000"; 

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    [Serializable]
    public class ServerResponse
    {
        public string status;
        public string msg;
        public int user_pk;
        public string user_id;
        public string password;
    }

    public IEnumerator SendJsonRequest(string url, object data, Action<ServerResponse> onResponse)
    {
        string jsonData = JsonUtility.ToJson(data);

        using (UnityWebRequest www = new UnityWebRequest(baseUrl + url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("통신 에러: " + www.error);
                onResponse?.Invoke(null);
            }
            else
            {
                string responseJson = www.downloadHandler.text;
                Debug.Log($"서버 응답 ({url}): " + responseJson);

                try 
                {
                    ServerResponse res = JsonUtility.FromJson<ServerResponse>(responseJson);
                    onResponse?.Invoke(res);
                }
                catch (Exception e)
                {
                    Debug.LogError("JSON 파싱 에러: " + e.Message);
                    onResponse?.Invoke(null);
                }
            }
        }
    }
}