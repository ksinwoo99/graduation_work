using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions; // 🔥 [추가] 텍스트 속에서 특정 패턴(name="")을 찾기 위해 필요!

public class Ingame_Manager_Coding : MonoBehaviour {
    [Header("UI 연결")]
    public GameObject codingPanel;
    public TextMeshProUGUI titleText;
    public TMP_InputField inputField;
    public Button btnVerify;
    public Image statusLight;

    [Header("매니저 연결")]
    public Ingame_Manager_Build buildManager;
    
    // ✨ [숫자 기반] 기계 ID를 Key로 사용
    private Dictionary<int, string> globalCodes = new Dictionary<int, string>();
    public logic_CodingBase currentLogic;
    private int currentMachineId; 

    // ✨ [추가] 현재 열린 코딩창과 연결된 건설 버튼을 기억해둘 변수
    private Ingame_Button_Build currentBuildButton;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
    }

    public void OpenFromExternal(int machineId, string displayName, UnityEngine.Tilemaps.TileBase tile, Image btnImage, logic_CodingBase logicScript) {
        Debug.Log($"[코딩창 열기] ID: {machineId} | UI명: {displayName}");

        if (codingPanel.activeSelf && currentMachineId == machineId) {
            CloseWindow();
            return;
        }

        if (codingPanel.activeSelf) SaveCurrentInput();

        currentMachineId = machineId;
        currentLogic = logicScript;
        
        // ✨ [추가] 코딩창을 열 때 누른 버튼을 기억해둡니다!
        if (btnImage != null) {
            currentBuildButton = btnImage.GetComponent<Ingame_Button_Build>();
        }

        codingPanel.SetActive(true);
        if (titleText != null) titleText.text = $"{displayName}.py";

        string savedCode = "";
        if (globalCodes.ContainsKey(machineId)) savedCode = globalCodes[machineId];
        else if (currentLogic != null) savedCode = currentLogic.GetDefaultCode();

        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        if (codeEditor != null) codeEditor.Text = savedCode;
        else inputField.text = savedCode;

        if (buildManager != null) buildManager.StartBuildMode(tile, btnImage);
        
        // 코드 적용 및 이름 텍스트 업데이트
        CheckCodeAndApply(savedCode);
    }

    public void SaveCurrentInput() {
        if (currentMachineId == 0) return;
        if (codingPanel != null && !codingPanel.activeSelf) return;

        string currentText = "";
        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        currentText = (codeEditor != null) ? codeEditor.Text : inputField.text;

        if (globalCodes.ContainsKey(currentMachineId)) globalCodes[currentMachineId] = currentText;
        else globalCodes.Add(currentMachineId, currentText);

        Debug.Log($"[메모리 저장] ID: {currentMachineId} | {currentText.Length}자 저장됨");
    }

    public void CloseWindow() {
        SaveCurrentInput(); 
        codingPanel.SetActive(false);
        if (buildManager != null) buildManager.CancelBuildMode();
    }

    public void CloseWindowOnly() {
        SaveCurrentInput();
        codingPanel.SetActive(false);
    }

    // 서버 저장 시 호출
    public string GetSavedCode(int machineId) {
        if (globalCodes.ContainsKey(machineId)) return globalCodes[machineId];
        return "";
    }

    // 서버 로드 시 호출
    public void SetSavedCode(int machineId, string code) {
        if (globalCodes.ContainsKey(machineId)) globalCodes[machineId] = code;
        else globalCodes.Add(machineId, code);
        Debug.Log($"[코드 주입] ID: {machineId} | {code.Length}자");
    }

    void OnClick_Verify() {
        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        string codeToVerify = (codeEditor != null) ? codeEditor.Text : inputField.text;
        CheckCodeAndApply(codeToVerify);
    }

    public void CheckCodeAndApply(string code) {
        if (currentLogic == null) return;
        
        if (currentLogic.ValidateCode(code) == logic_CodingBase.CodeState.Valid) {
            SetStatus(Color.green, true);
            if (globalCodes.ContainsKey(currentMachineId)) globalCodes[currentMachineId] = code;
            else globalCodes.Add(currentMachineId, code);

            // ==============================================================
            // ✨ [핵심 추가] 파이썬 코드 안에서 name="이름" 부분 찾아내기!
            // name = "어쩌구" / name='어쩌구' / name="어쩌구" 등 띄어쓰기와 따옴표를 모두 인식합니다.
            Match match = Regex.Match(code, @"name\s*=\s*[""']([^""']+)[""']");
            if (match.Success) {
                string newName = match.Groups[1].Value; // 따옴표 안에 적힌 진짜 이름만 빼오기
                
                // 1. 코딩창 상단 타이틀 바꾸기 (예: 채굴기.py)
                if (titleText != null) titleText.text = $"{newName}.py";
                
                // 2. 하단 건축 버튼의 TMP 글자 바꾸기
                if (currentBuildButton != null && currentBuildButton.nameText != null) {
                    currentBuildButton.nameText.text = newName;
                }
            }
            // ==============================================================

        } else {
            SetStatus(Color.red, false);
        }
    }

    void SetStatus(Color color, bool isAllowed) {
        if (statusLight != null) statusLight.color = color;
        if (buildManager != null) buildManager.SetPlacementPermission(isAllowed);
    }
}