using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;

public class login_DbManager : MonoBehaviour
{
    public static login_DbManager Instance;
    string baseUrl = "http://localhost:3000"; 

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    public IEnumerator SendPostRequest(string url, WWWForm form, Action<string> onResponse)
    {
        using (UnityWebRequest www = UnityWebRequest.Post(baseUrl + url, form))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("통신 에러: " + www.error);
                onResponse?.Invoke("ERROR");
            }
            else
            {
                string response = www.downloadHandler.text;
                Debug.Log($"서버 응답 ({url}): " + response);
                onResponse?.Invoke(response);
            }
        }
    }
}