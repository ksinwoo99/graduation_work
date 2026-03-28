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
    
    public int resCommon;
    public int resRare;
    public int resSpecial;
    public int resExotic;
}

[System.Serializable]
public class DebuggingResponse
{
    public string output;
    public string status;
    public float execution_time;
}

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
        string actualMachineType = "GENERAL";

        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null)
        {
            logic_CodingBase targetMachine = Ingame_Manager_Build.Instance.codingManager.currentLogic;
            if (targetMachine != null)
            {
                actualMachineType = targetMachine.GetMachineName();
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

        UnityWebRequest www = new UnityWebRequest(url, "POST");
        www.uploadHandler = new UploadHandlerRaw(jsonToSend);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            DebuggingResponse response = JsonUtility.FromJson<DebuggingResponse>(www.downloadHandler.text);
            bool isPythonValid = (response.status == "success");
            bool isMachineValid = false;
            bool isSuccess = false;

            if (isPythonValid)
            {
                int applyResult = TryApplyCodeToMachine(code);
                isMachineValid = (applyResult == 2);
                isSuccess = isPythonValid && isMachineValid;

                string timeMsg = $"\n(실행 시간: {response.execution_time:F3}초)";

                // ✨ [수정] 숫자에 따라 아주 친절한 에러 메시지를 출력합니다!
                if (applyResult == 2) {
                    SetUI("정상 작동 및 적용 완료!\n" + response.output + timeMsg, Color.green, false);
                } else if (applyResult == 1) {
                    SetUI("이름은 저장되었으나, 기계 작동을 위한 필수 함수가 없습니다!" + timeMsg, Color.yellow, true);
                } else if (applyResult == -1) {
                    SetUI("아직 반복문(for)을 사용할 수 있는 시스템 권한이 없습니다!" + timeMsg, Color.red, true);
                } else if (applyResult == -2) {
                    SetUI("현재 시스템에서는 반복문을 최대 10회까지만 사용할 수 있습니다!" + timeMsg, Color.red, true);
                } else if (applyResult == -3) {
                    SetUI("아직 무한 루프(while)를 사용할 수 있는 시스템 권한이 없습니다!" + timeMsg, Color.red, true);
                } else if (applyResult == -4) {
                    SetUI("기계의 이름(name 변수)을 필수로 지정해야 합니다!" + timeMsg, Color.red, true);
                } else {
                    SetUI("문법은 맞았지만, 이 기계가 수행할 수 없는 명령어입니다." + timeMsg, Color.red, true);
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
            SetUI("서버 연결 실패\n" + www.error, Color.red, false);
        }
    }

    IEnumerator SendLogToPythonDB(string userId, string machineType, string code, bool isPyValid, bool isMachValid, bool isSuccess, float execTime, string outputLog)
    {
        string logUrl = "http://13.237.51.219:8001/api/submit_code";

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
            MLResponse mlRes = JsonUtility.FromJson<MLResponse>(www.downloadHandler.text);
            if (resultText != null)
            {
                resultText.text += $"\n\n<color=#FFFF00>[AI 분석 결과]</color> (점수: {mlRes.score}점)\n{mlRes.hint}";
            }
        }
    }

    // ✨ [핵심 수정] bool 반환에서 int(0, 1, 2) 반환으로 변경
    private int TryApplyCodeToMachine(string code)
    {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr != null && buildMgr.codingManager != null)
        {
            logic_CodingBase targetMachine = buildMgr.codingManager.currentLogic;
            if (targetMachine != null)
            {
                return buildMgr.codingManager.CheckCodeAndApply(code);
            }
        }
        return 0;
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