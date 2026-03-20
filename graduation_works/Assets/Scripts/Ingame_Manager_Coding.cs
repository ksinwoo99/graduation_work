using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
    }

    // 1. 코딩 창 열기 (첫 번째 인자가 int machineId 임을 확인!)
    public void OpenFromExternal(int machineId, string displayName, UnityEngine.Tilemaps.TileBase tile, Image btnImage, logic_CodingBase logicScript) {
        Debug.Log($"[코딩창 열기] ID: {machineId} | UI명: {displayName}");

        if (codingPanel.activeSelf && currentMachineId == machineId) {
            CloseWindow();
            return;
        }

        if (codingPanel.activeSelf) SaveCurrentInput();

        currentMachineId = machineId;
        currentLogic = logicScript;
        codingPanel.SetActive(true);
        if (titleText != null) titleText.text = $"{displayName}.py";

        string savedCode = "";
        if (globalCodes.ContainsKey(machineId)) savedCode = globalCodes[machineId];
        else if (currentLogic != null) savedCode = currentLogic.GetDefaultCode();

        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        if (codeEditor != null) codeEditor.Text = savedCode;
        else inputField.text = savedCode;

        if (buildManager != null) buildManager.StartBuildMode(tile, btnImage);
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
        } else {
            SetStatus(Color.red, false);
        }
    }

    void SetStatus(Color color, bool isAllowed) {
        if (statusLight != null) statusLight.color = color;
        if (buildManager != null) buildManager.SetPlacementPermission(isAllowed);
    }
}