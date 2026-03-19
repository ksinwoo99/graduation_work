using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using System.Text;
using System;

// 1. 실행 요청용
[System.Serializable]
public class CodeRequest
{
    public string user_id;    
    public string source_code;
    public string machine_type;
    
    // 파이썬 서버로 보낼 현재 자원 보유량 변수
    public int resCommon;
    public int resRare;
    public int resSpecial;
    public int resExotic;
}

// 2. 파이썬 응답받는 용 (★ 실행 시간 추가!)
[System.Serializable]
public class DebuggingResponse
{
    public string output;
    public string status;
    public float execution_time;
}

// 3. ML 수집용
[System.Serializable]
public class MLSubmitRequest
{
    public string user_id;   
    public string machine_type;
    public string source_code;
    public bool is_python_valid;
    public bool is_machine_valid;
    public bool is_success;
    public float execution_time;
    public string output_log;
}
// ML 응답용
[System.Serializable]
public class MLResponse
{
    public string status;
    public float score;
    public string hint;
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

        // 기본값은 GENERAL
        string actualMachineType = "GENERAL";

        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null)
        {
            logic_CodingBase targetMachine = Ingame_Manager_Build.Instance.codingManager.currentLogic;
            if (targetMachine != null)
            {

                actualMachineType = targetMachine.GetMachineName();
                Debug.Log($"[테스트] 현재 기계 이름 받아옴: {actualMachineType}"); // 👈 이 줄 추가
            }
        }

        CodeRequest requestData = new CodeRequest
        {
            user_id = userId,  
            source_code = code,
            machine_type = actualMachineType
        };

        if (Ingame_Manager_Resource.Instance != null) 
        {
            requestData.resCommon = Ingame_Manager_Resource.Instance.resCommon;
            requestData.resRare = Ingame_Manager_Resource.Instance.resRare;
            requestData.resSpecial = Ingame_Manager_Resource.Instance.resSpecial; 
            requestData.resExotic = Ingame_Manager_Resource.Instance.resExotic;
        }

        string json = JsonUtility.ToJson(requestData);
        byte[] jsonToSend = Encoding.UTF8.GetBytes(json);

        // 확인용 로그 출력
        Debug.Log($"[프론트엔드 -> 백엔드] 전송하는 코드 원본:\n{code}\n기계 종류: {actualMachineType}");

        UnityWebRequest www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(jsonToSend);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[백엔드 -> 프론트엔드] 서버 응답 원본:\n{www.downloadHandler.text}");

            DebuggingResponse response = JsonUtility.FromJson<DebuggingResponse>(www.downloadHandler.text);

            bool isPythonValid = (response.status == "success");
            bool isMachineValid = false;
            bool isSuccess = false;

            if (isPythonValid)
            {
                isMachineValid = TryApplyCodeToMachine(code);
                isSuccess = isPythonValid && isMachineValid;

                if (isMachineValid) {
                    string timeMsg = $"\n(실행 시간: {response.execution_time:F3}초)";
                    SetUI("정상 작동 및 적용 완료!\n" + response.output + timeMsg, Color.green, false);
                } else
                {
                    SetUI("서버 문법은 통과했으나, 기계 조건(형식)에 맞지 않습니다.", Color.red, true);
                }
            }
            else
            {
                SetUI(response.output, Color.red, true);
            }

            StartCoroutine(SendLogToPythonDB(userId, actualMachineType, code, isPythonValid, isMachineValid, isSuccess, response.execution_time, response.output));
        }
        else
        {
            Debug.LogError($"[통신 에러] 서버 연결 실패: {www.error}");
            SetUI("서버 연결 실패\n" + www.error, Color.red, false);
        }
    }

    IEnumerator SendLogToPythonDB(string userId, string machineType, string code, bool isPyValid, bool isMachValid, bool isSuccess, float execTime, string outputLog)
    {
        string logUrl = "http://127.0.0.1:8000/api/submit_code";


        MLSubmitRequest mlData = new MLSubmitRequest
        {
            user_id = userId,
            machine_type = machineType,
            source_code = code,
            is_python_valid = isPyValid,
            is_machine_valid = isMachValid,
            is_success = isSuccess,
            execution_time = execTime,
            output_log = outputLog
        };

        string json = JsonUtility.ToJson(mlData);
        byte[] jsonToSend = Encoding.UTF8.GetBytes(json);

        UnityWebRequest www = new UnityWebRequest(logUrl, "POST");
        www.uploadHandler = new UploadHandlerRaw(jsonToSend);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[데이터 수집 완료] AI 피드백 : {www.downloadHandler.text}");

            // 💡 파이썬이 준 JSON 데이터(점수, 힌트)
            MLResponse mlRes = JsonUtility.FromJson<MLResponse>(www.downloadHandler.text);

            // 💡 현재 유니티 화면 텍스트 아래에 AI 힌트 추가
            if (resultText != null)
            {
                // 줄 띄우고 예쁘게 덧붙이기
                resultText.text += $"\n\n<color=#FFFF00>[AI 분석 결과]</color> (점수: {mlRes.score}점)\n{mlRes.hint}";
            }
        }
        else
        {
            Debug.LogError($"[데이터 수집 실패] ML 서버 에러 : {www.error}\n 원인 : {www.downloadHandler.text}");
        }
    }

    private bool TryApplyCodeToMachine(string code)
    {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr != null && buildMgr.codingManager != null)
        {
            logic_CodingBase targetMachine = buildMgr.codingManager.currentLogic;

            if (targetMachine != null)
            {
                logic_CodingBase.CodeState state = targetMachine.ValidateCode(code);

                if (state == logic_CodingBase.CodeState.Valid)
                {
                    buildMgr.codingManager.CheckCodeAndApply(code);
                    return true;
                }
            }
        }
        return false;
    }

    void SetUI(string message, Color Color, bool isError)
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
            resultBackground.color = new Color(Color.r, Color.g, Color.b, bgAlpha);
        }

        resultText.color = UnityEngine.Color.white;
        resultText.text = finalDisplayMessage;

        if (ResultCircle != null) ResultCircle.color = Color;
    }

    public void HideResult()
    {
        if (resultBackground != null) resultBackground.gameObject.SetActive(false);
        if (resultText != null) resultText.text = "";
    }
}