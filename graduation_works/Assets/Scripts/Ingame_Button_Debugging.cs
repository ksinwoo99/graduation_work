using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using System.Text;
using System;

// == 1. 데이터 요청/응답 구조체 (이 부분들이 있어야 CS0246 에러가 사라집니다) ==
[System.Serializable]
public class CodeRequest
{
    public string user_id;
    public string source_code;
    public string machine_type;
}

[System.Serializable]
public class DebuggingResponse // 에러 메시지에 맞춰 이름을 수정했습니다.
{
    public string output;
    public string status;
}

// == 2. 메인 클래스 ==
public class Ingame_Button_Debugging : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField inputField;
    public TMP_Text resultText;
    public Image ResultCircle;
    public Image resultBackground;

    [Header("Transparency Settings")]
    [Range(0f, 1f)]
    public float bgAlpha = 0.2f; // 인스펙터 슬라이더로 연하게 조절 가능

    private void Start()
    {
        // 입력창 클릭 시 결과창 숨기기
        if (inputField != null)
        {
            inputField.onSelect.AddListener(delegate { HideResult(); });
        }
        HideResult();
    }

    public void Debugging()
    {
        string code = inputField.text;
        string currentId = string.IsNullOrEmpty(UserSession.UserId) ? "guest" : UserSession.UserId;
        StartCoroutine(SendToServer(currentId, code));
    }

    IEnumerator SendToServer(string userId, string code)
    {
        string url = "http://13.237.51.219:8000/execute";
        CodeRequest requestData = new CodeRequest { 
            user_id = userId, 
            source_code = code, 
            machine_type = "GENERAL" 
        };

        string json = JsonUtility.ToJson(requestData);
        byte[] jsonToSend = Encoding.UTF8.GetBytes(json);

        UnityWebRequest www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(jsonToSend);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            // 여기서 DebuggingResponse를 사용하여 에러를 해결합니다.
            DebuggingResponse response = JsonUtility.FromJson<DebuggingResponse>(www.downloadHandler.text);
            
            if (response.status == "success") SetUI(response.output, Color.green, false);
            else SetUI(response.output, Color.red, true);
        }
        else
        {
            SetUI("서버 연결 실패\n" + www.error, Color.red, false);
        }
    }

    void SetUI(string message, Color baseColor, bool isError)
    {
        string finalDisplayMessage = message;

        if (isError && !string.IsNullOrEmpty(message))
        {
            string[] lines = message.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0) finalDisplayMessage = lines[lines.Length - 1].Trim();
        }

        if (resultBackground != null)
        {
            resultBackground.gameObject.SetActive(true);
            // 배경색만 지정한 투명도(bgAlpha)로 연하게 적용
            resultBackground.color = new Color(baseColor.r, baseColor.g, baseColor.b, bgAlpha);
        }

        resultText.color = Color.white; 
        resultText.text = finalDisplayMessage;
        
        if (ResultCircle != null) ResultCircle.color = baseColor;
    }

    void HideResult()
    {
        if (resultBackground != null) resultBackground.gameObject.SetActive(false);
        if (resultText != null) resultText.text = "";
    }
}