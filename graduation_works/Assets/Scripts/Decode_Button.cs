using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using System.Text;
using System;

[System.Serializable]
public class CodeRequest
{
    public string user_id;
    public string source_code;
    public string machine_type;
}

[System.Serializable]
public class DecodeResponse
{
    public string output;
    public string status;
}

public class Decode_Button : MonoBehaviour
{
    public TMP_InputField inputField;
    public TMP_Text resultText;
    public Image ResultCircle;

    public void Decode()
    {
        string code = inputField.text;
        
        // (테스트) UserId가 비어있으면 "guest"(1)로 설정
        string currentId = string.IsNullOrEmpty(UserSession.UserId) ? "guest" : UserSession.UserId;
        
        StartCoroutine(SendToServer(currentId, code));
    }

    IEnumerator SendToServer(string userId, string code)
    {
        string url = "http://13.237.51.219:8000/execute";

        CodeRequest requestData = new CodeRequest
        {
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
            DecodeResponse response = JsonUtility.FromJson<DecodeResponse>(www.downloadHandler.text);
            
            // 성공 시에는 전체 결과값 출력, 실패(에러) 시에는 마지막 줄만 추출하도록 분기
            if (response.status == "success")
            {
                SetUI(response.output, Color.green, false);
            }
            else
            {
                SetUI(response.output, Color.red, true);
            }
        }
        else
        {
            SetUI("서버 연결 실패\n" + www.error, Color.red, false);
        }
    }

    void SetUI(string message, Color color, bool isError)
    {
        string finalDisplayMessage = message;

        // 에러인 경우에만 마지막 줄(SyntaxError 내용) 추출 로직 실행
        if (isError && !string.IsNullOrEmpty(message))
        {
            // 1. 문자열을 줄바꿈 기준으로 쪼갬 (빈 줄은 무시)
            string[] lines = message.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (lines.Length > 0)
            {
                // 2. 가장 마지막 줄을 선택하여 양끝 공백 제거
                finalDisplayMessage = lines[lines.Length - 1].Trim();
            }
        }

        resultText.color = color;
        resultText.text = finalDisplayMessage;
        
        if (ResultCircle != null) 
            ResultCircle.color = color;
    }
}