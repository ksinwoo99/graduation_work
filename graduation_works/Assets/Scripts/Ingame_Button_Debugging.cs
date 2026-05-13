using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using System.Text;
using System;

// ──────────────────────────────────────────────────────────
// DTO: Server A(/execute) 코드 실행 요청
// ──────────────────────────────────────────────────────────
[System.Serializable]
public class CodeRequest
{
    public string user_id;
    public string source_code;
    public string machine_type;

    // 자원 보유량 
    public int resCommon;
    public int resRare;
    public int resSpecial;
    public int resExotic;
}

// ──────────────────────────────────────────────────────────
// DTO: Server A(/execute) 코드 실행 응답
// ──────────────────────────────────────────────────────────
[System.Serializable]
public class DebuggingResponse
{
    public string output;          // 실행 결과 또는 에러 메시지
    public string status;          // "success" | "error"
    public float  execution_time;  // 실행 소요 시간 (초)
}

// ──────────────────────────────────────────────────────────
// DTO: Server B(/api/submit_code) ML 로그 전송 요청
// ──────────────────────────────────────────────────────────
[System.Serializable]
public class MLSubmitRequest
{
    public string user_id;
    public string machine_type;
    public string source_code;

    public bool   is_python_valid;   // Server A 실행 통과 여부
    public bool   is_machine_valid;  // 클라이언트 기계 조건 통과 여부
    public bool   is_success;        // 최종 성공 여부 (python_valid AND machine_valid)
    public float  execution_time;    // 코드 실행 소요 시간 (초)
    public string output_log;        // 실행 결과 또는 에러 메시지

    // 자원/골드 보유량 
    public int res_common;
    public int res_rare;
    public int res_special;
    public int res_exotic;
    public int gold;
}

// ──────────────────────────────────────────────────────────
// DTO: Server B(/api/submit_code) ML 로그 전송 응답
// ──────────────────────────────────────────────────────────
[System.Serializable]
public class MLResponse
{
    public string status;  // "success" | "error"
    public float  score;   // 서버 계산 점수 (0~100)
    public string hint;    // AI 생성 힌트 메시지

    // ── 루프 균형 신호 (서버 _compute_loop_balance 결과) ──
    public bool   should_break_machine;  // true → 임밸런스 고장 트리거 (8:2 초과)
    public bool   is_balance_fixed;      // true → 임밸런스 고장 해제 트리거 (6.5:3.5 이하)
    public string consumed_part_type;    // "for" | "while" — 소모된 부품 종류
    public float  imbalance_score;       // 0.0(균형) ~ 1.0(완전 편향)
}

/// <summary>
/// 인게임 코드 에디터의 실행(F5) 버튼을 담당합니다.
///
/// 실행 흐름:
///   1. Server A(/execute) → 파이썬 코드 실행 및 문법 검증
///   2. 클라이언트 로컬    → TryApplyCodeToMachine() 으로 기계 조건 검증
///   3. Server B(/api/submit_code) → 결과 로그 저장 및 AI 힌트 수신
/// </summary>
public class Ingame_Button_Debugging : MonoBehaviour
{
    [Header("UI 연결")]
    public TMP_InputField inputField;
    public TMP_Text       resultText;
    public Image          ResultCircle;
    public Image          resultBackground;

    [Header("결과 배경 투명도")]
    [Range(0f, 1f)]
    public float bgAlpha = 0.2f;

    // ──────────────────────────────────────────────────────
    // 서버 B 주소 설정
    // 로컬 테스트와 AWS 배포 중 사용할 줄의 주석을 해제하세요.
    // ──────────────────────────────────────────────────────

    // // 로컬 테스트용
    // private const string ML_SERVER_URL = "http://127.0.0.1:8001/api/submit_code";

    // AWS 배포용
    private const string ML_SERVER_URL = "http://13.237.51.219:8001/api/submit_code";

    // Server A 주소 (게임 실행 서버, 별도 관리 — 항상 AWS)
    private const string EXECUTE_SERVER_URL = "http://13.237.51.219:8000/execute";

    // ──────────────────────────────────────────────────────

    private void Start()
    {
        if (inputField != null)
        {
            // 입력 필드를 클릭하면 이전 결과 UI를 숨깁니다.
            inputField.onSelect.AddListener(delegate { HideResult(); });
        }
        HideResult();
    }

    private void Update()
    {
        // F5 단축키로 실행 (코드 에디터 패널이 활성화된 경우에만)
        if (Input.GetKeyDown(KeyCode.F5))
        {
            if (inputField != null && inputField.gameObject.activeInHierarchy)
            {
                Debugging();
            }
        }
    }

    /// <summary>실행 버튼 또는 F5 단축키에 의해 호출됩니다.</summary>
    public void Debugging()
    {
        // 버튼 클릭 시 포커스 해제 (이중 입력 방지)
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        string code      = inputField.text;
        string currentId = string.IsNullOrEmpty(Shared_Manager_Session.CurrentUserId)
                         ? "guest"
                         : Shared_Manager_Session.CurrentUserId;

        StartCoroutine(SendToServer(currentId, code));
    }

    /// <summary>
    /// [Step 1] Server A 에 코드를 전송하여 파이썬 실행 결과를 받습니다.
    /// 성공 시 클라이언트 기계 검증 → [Step 2] ML 로그 전송으로 이어집니다.
    /// </summary>
    IEnumerator SendToServer(string userId, string code)
    {
        // 현재 열린 기계의 타입을 가져옵니다. 기계가 없으면 "GENERAL" 로 처리합니다.
        string actualMachineType = "GENERAL";
        if (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.codingManager != null)
        {
            logic_CodingBase targetMachine = Ingame_Manager_Build.Instance.codingManager.currentLogic;
            if (targetMachine != null)
                actualMachineType = targetMachine.GetMachineName();
        }

        CodeRequest requestData = new CodeRequest
        {
            user_id      = userId,
            source_code  = code,
            machine_type = actualMachineType
        };

        if (Ingame_Manager_Resource.Instance != null)
        {
            requestData.resCommon  = Ingame_Manager_Resource.Instance.resCommon;
            requestData.resRare    = Ingame_Manager_Resource.Instance.resRare;
            requestData.resSpecial = Ingame_Manager_Resource.Instance.resSpecial;
            requestData.resExotic  = Ingame_Manager_Resource.Instance.resExotic;
        }

        string json      = JsonUtility.ToJson(requestData);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

        UnityWebRequest www = new UnityWebRequest(EXECUTE_SERVER_URL, "POST");
        www.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            SetUI("서버 연결 실패\n" + www.error, Color.red, false);
            yield break;
        }

        DebuggingResponse response   = JsonUtility.FromJson<DebuggingResponse>(www.downloadHandler.text);
        bool              isPyValid  = (response.status == "success");
        bool              isMachValid = false;
        bool              isSuccess  = false;

        if (isPyValid)
        {
            // 파이썬 문법이 유효한 경우, 클라이언트 측에서 기계 조건을 검증합니다.
            int  applyResult = TryApplyCodeToMachine(code);
            isMachValid = (applyResult == 2);
            isSuccess   = isMachValid;   // is_python_valid 는 이미 true

            string timeMsg = $"  (실행 시간: {response.execution_time:F3}초)";

            // applyResult 코드별 피드백 메시지
            // 값 정의는 Ingame_Manager_Coding.CheckCodeAndApply() 참고
            switch (applyResult)
            {
                case  2: SetUI("정상 작동 및 적용 완료!\n" + response.output + timeMsg, Color.green,  false); break;
                case  1: SetUI("이름은 저장되었으나, 기계 작동을 위한 필수 함수가 없습니다!" + timeMsg, Color.yellow, true); break;
                case -1: SetUI("아직 반복문(for)을 사용할 수 있는 시스템 권한이 없습니다!" + timeMsg, Color.red, true); break;
                case -2: SetUI("현재 시스템에서는 반복문을 최대 10회까지만 사용할 수 있습니다!" + timeMsg, Color.red, true); break;
                case -3: SetUI("아직 무한 루프(while)를 사용할 수 있는 시스템 권한이 없습니다!" + timeMsg, Color.red, true); break;
                case -4: SetUI("기계의 이름(name 변수)을 필수로 지정해야 합니다!" + timeMsg, Color.red, true); break;
                case -5: SetUI("시스템 권한 부족: 아직 컨베이어 벨트를 가동할 수 없습니다!" + timeMsg, Color.red, true); break;
                case -6: SetUI("시스템 권한 부족: 아직 컨베이어 벨트 고속(fast) 모드를 사용할 수 없습니다!" + timeMsg, Color.red, true); break;
                case -7: SetUI("이미 다른 기계가 사용 중인 이름은 사용할 수 없습니다!" + timeMsg, Color.red, true); break;
                case -8: SetUI("과열로 인해 해당 문법을 사용할 수 없습니다. 우회 코드를 사용하세요." + timeMsg, Color.red, true); break;
                case -9: SetUI("[부품 부족] 사용 가능한 부품이 소진되었습니다. 반대 부품으로 균형을 맞춰 보충하세요!" + timeMsg, Color.red, true); break;
                default: SetUI("문법은 맞았지만, 이 기계가 수행할 수 없는 명령어입니다." + timeMsg, Color.red, true); break;
            }
        }
        else
        {
            // 파이썬 실행 자체가 실패한 경우 에러 로그를 표시합니다.
            SetUI(response.output, Color.red, true);
        }

        // 성공/실패 여부에 관계없이 항상 ML 서버에 로그를 전송합니다.
        StartCoroutine(SendLogToMLServer(
            userId, actualMachineType, code,
            isPyValid, isMachValid, isSuccess,
            response.execution_time, response.output
        ));
    }

    /// <summary>
    /// [Step 2] Server B 에 코드 실행 로그를 전송하고 AI 힌트를 수신합니다.
    /// 결과 UI 하단에 AI 분석 결과가 추가됩니다.
    /// </summary>
    IEnumerator SendLogToMLServer(
        string userId, string machineType, string code,
        bool   isPyValid, bool isMachValid, bool isSuccess,
        float  execTime, string outputLog)
    {
        MLSubmitRequest mlData = new MLSubmitRequest
        {
            user_id          = userId,
            machine_type     = machineType,
            source_code      = code,
            is_python_valid  = isPyValid,
            is_machine_valid = isMachValid,
            is_success       = isSuccess,
            execution_time   = execTime,
            output_log       = outputLog
        };

        if (Ingame_Manager_Resource.Instance != null)
        {
            mlData.res_common  = Ingame_Manager_Resource.Instance.resCommon;
            mlData.res_rare    = Ingame_Manager_Resource.Instance.resRare;
            mlData.res_special = Ingame_Manager_Resource.Instance.resSpecial;
            mlData.res_exotic  = Ingame_Manager_Resource.Instance.resExotic;
            mlData.gold        = Ingame_Manager_Resource.Instance.currentGold;
        }

        string json      = JsonUtility.ToJson(mlData);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

        UnityWebRequest www = new UnityWebRequest(ML_SERVER_URL, "POST");
        www.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            MLResponse mlRes = JsonUtility.FromJson<MLResponse>(www.downloadHandler.text);

            // ── 임밸런스 고장 / 복구 신호 처리 (AI 힌트보다 먼저 적용) ──
            var buildMgrForBalance = Ingame_Manager_Build.Instance;
            if (buildMgrForBalance != null && buildMgrForBalance.codingManager != null)
            {
                buildMgrForBalance.codingManager.HandleLoopImbalance(
                    mlRes.should_break_machine,
                    mlRes.is_balance_fixed,
                    mlRes.consumed_part_type,
                    mlRes.imbalance_score
                );
            }

            if (resultText != null)
            {
                // ✨ 1. 현재 테마 모드 확인
                bool isDark = true;
                var buildMgr = Ingame_Manager_Build.Instance;
                if (buildMgr != null && buildMgr.codingManager != null) {
                    isDark = buildMgr.codingManager.isDarkMode;
                }

                // ✨ 2. 노란색의 보색 적용! 
                // 다크모드: 밝은 노란색(#FFFF00) / 라이트모드: 보색인 보라색(#800080)으로 설정합니다.
                string highlightColor = isDark ? "#FFFF00" : "#800080"; 

                resultText.text += $"\n\n<color={highlightColor}>[AI 분석 결과]</color> (점수: {mlRes.score}점)\n{mlRes.hint}";
            }
        }
    }

    /// <summary>
    /// 현재 열린 기계에 코드를 적용하고 결과 코드를 반환합니다.
    /// 반환값 정의는 Ingame_Manager_Coding.CheckCodeAndApply() 참고.
    /// </summary>
    private int TryApplyCodeToMachine(string code)
    {
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr != null && buildMgr.codingManager != null)
        {
            logic_CodingBase targetMachine = buildMgr.codingManager.currentLogic;
            if (targetMachine != null)
                return buildMgr.codingManager.CheckCodeAndApply(code, true); 
        }
        return 0;
    }

    /// <summary>
    /// 결과 UI(텍스트, 배경, 상태 원)를 업데이트합니다.
    /// isError 가 true 이면 멀티라인 에러 로그에서 마지막 줄만 표시합니다.
    /// </summary>
    private void SetUI(string message, Color tint, bool isError)
    {
        string displayMessage = message;

        if (isError && !string.IsNullOrEmpty(message))
        {
            // 에러 로그의 가장 마지막 줄(핵심 메시지)만 표시
            string[] lines = message.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
                displayMessage = lines[lines.Length - 1].Trim();
        }

        // 1. 현재 다크/라이트 모드 상태 가져오기
        bool isDark = true;
        var buildMgr = Ingame_Manager_Build.Instance;
        if (buildMgr != null && buildMgr.codingManager != null) {
            isDark = buildMgr.codingManager.isDarkMode;
        }

        // 2. 배경창 켜기 및 투명도(Alpha) 조절
        if (resultBackground != null)
        {
            resultBackground.gameObject.SetActive(true);
            
            // ✨ [핵심 수정] 라이트 모드일 때는 투명도를 0.5f(50%)로 올려서 배경색을 더 진하게 만듭니다.
            // 다크 모드일 때는 기존에 인스펙터에 설정한 bgAlpha(기본 0.2f) 값을 씁니다.
            float currentAlpha = isDark ? bgAlpha : 0.5f; 
            
            resultBackground.color = new Color(tint.r, tint.g, tint.b, currentAlpha);
        }

        // 3. 텍스트 색상 적용 (다크: 흰색 / 라이트: 완전 검은색)
        resultText.color = isDark ? UnityEngine.Color.white : UnityEngine.Color.black;
        resultText.text  = displayMessage;

        // 4. 상태 원 색상 적용
        if (ResultCircle != null)
            ResultCircle.color = tint;
    }

    /// <summary>결과 UI를 모두 숨깁니다.</summary>
    public void HideResult()
    {
        if (resultBackground != null) resultBackground.gameObject.SetActive(false);
        if (resultText != null)       resultText.text = "";
    }
}
