using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
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
public class DebuggingResponse 
{
    public string output;
    public string status;
}

public class Ingame_Button_Debugging : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField inputField;
    public TMP_Text resultText;
    public Image ResultCircle;
    public Image resultBackground;

    [Header("Transparency Settings")]
    [Range(0f, 1f)]
    public float bgAlpha = 0.2f; 

    private void Start()
    {
        if (inputField != null)
        {
            inputField.onSelect.AddListener(delegate { HideResult(); });
        }
        HideResult();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            if (inputField != null && inputField.gameObject.activeInHierarchy)
            {
                Debugging();
            }
        }
    }

    public void Debugging()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        string code = inputField.text;
        string currentId = string.IsNullOrEmpty(Shared_Manager_Session.CurrentUserId) ? "guest" : Shared_Manager_Session.CurrentUserId;
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

        Debug.Log($"[프론트엔드 -> 백엔드] 전송하는 코드 원본:\n{code}");

        UnityWebRequest www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(jsonToSend);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[백엔드 -> 프론트엔드] 서버 응답 원본:\n{www.downloadHandler.text}");

            DebuggingResponse response = JsonUtility.FromJson<DebuggingResponse>(www.downloadHandler.text);
            
            if (response.status == "success") 
            {
                bool isLogicValid = TryApplyCodeToMachine(code);

                if (isLogicValid) {
                    SetUI("정상 작동 및 적용 완료!\n" + response.output, Color.green, false);
                } else {
                    SetUI("서버 문법은 통과했으나, 기계 조건(형식)에 맞지 않습니다.", Color.red, true);
                }
            }
            else 
            {
                SetUI(response.output, Color.red, true);
            }
        }
        else
        {
            Debug.LogError($"[통신 에러] 서버 연결 실패: {www.error}");
            SetUI("서버 연결 실패\n" + www.error, Color.red, false);
        }
    }

    private bool TryApplyCodeToMachine(string code)
    {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr != null && buildMgr.codingManager != null)
        {
            // 🔥 [수정] targetLogic을 currentLogic으로 변경
            logic_CodingBase targetMachine = buildMgr.codingManager.currentLogic; 

            if (targetMachine != null)
            {
                // 🔥 [수정] CodeState의 풀네임(logic_CodingBase.CodeState)으로 변경
                logic_CodingBase.CodeState state = targetMachine.ValidateCode(code);
                
                if (state == logic_CodingBase.CodeState.Valid)
                {
                    // 🔥 [추가] 유니티 기계 검사도 통과했다면, 
                    // 코딩창의 초록불을 켜고 설치를 허용하도록 매니저에게 명령!
                    buildMgr.codingManager.CheckCodeAndApply(code);
                    return true;
                }
            }
        }
        return false;
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
            resultBackground.color = new Color(baseColor.r, baseColor.g, baseColor.b, bgAlpha);
        }

        resultText.color = Color.white; 
        resultText.text = finalDisplayMessage;
        
        if (ResultCircle != null) ResultCircle.color = baseColor;
    }

    public void HideResult()
    {
        if (resultBackground != null) resultBackground.gameObject.SetActive(false);
        if (resultText != null) resultText.text = "";
    }
}