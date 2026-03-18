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
    private Dictionary<string, string> globalCodes = new Dictionary<string, string>();
    public logic_CodingBase currentLogic;
    private string currentMachineName;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
    }

    public void OpenFromExternal(string machineName, UnityEngine.Tilemaps.TileBase tile, Image btnImage, logic_CodingBase logicScript) {
        if (codingPanel.activeSelf && currentMachineName == machineName) {
            CloseWindow();
            return;
        }

        // ✨ [안전장치] 다른 기계 창이 열려있는 상태에서 새 기계를 눌렀다면, 이전 기계 코드 먼저 자동 저장!
        if (codingPanel.activeSelf) {
            SaveCurrentInput();
        }

        currentMachineName = machineName;
        currentLogic = logicScript;
        
        codingPanel.SetActive(true);
        if (titleText != null) titleText.text = $"{machineName}.py";

        string savedCode = "";
        if (globalCodes.ContainsKey(machineName)) 
            savedCode = globalCodes[machineName];
        else if (currentLogic != null)
            savedCode = currentLogic.GetDefaultCode();

        InGameCodeEditor.CodeEditor codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        
        if (codeEditor != null) {
            codeEditor.Text = savedCode;
        } else {
            inputField.text = savedCode;
        }

        Ingame_Button_Debugging debugger = codingPanel.GetComponentInChildren<Ingame_Button_Debugging>(true);
        if (debugger != null) {
            debugger.HideResult();
        }

        if (buildManager != null) buildManager.StartBuildMode(tile, btnImage);

        // 창을 열 때, 불러온 코드가 올바른 문법인지 상태등 색상 바로 업데이트
        CheckCodeAndApply(savedCode);
    }

    void OnClick_Verify() {
        InGameCodeEditor.CodeEditor codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        string codeToVerify = codeEditor != null ? codeEditor.Text : inputField.text;
        CheckCodeAndApply(codeToVerify);
    }

    public void CheckCodeAndApply(string code) {
        if (currentLogic == null) return;
        logic_CodingBase.CodeState state = currentLogic.ValidateCode(code);

        switch (state) {
            case logic_CodingBase.CodeState.Empty:
                SetStatus(Color.yellow, false);
                break;
            case logic_CodingBase.CodeState.Error:
                SetStatus(Color.red, false);
                break;
            case logic_CodingBase.CodeState.Valid:
                SetStatus(Color.green, true);
                SaveCode(code); // 정상일 때 공식 저장
                break;
        }
    }

    void SetStatus(Color color, bool isAllowed) {
        if (statusLight != null) statusLight.color = color;
        if (buildManager != null) buildManager.SetPlacementPermission(isAllowed);
    }

    void SaveCode(string code) {
        if (globalCodes.ContainsKey(currentMachineName))
            globalCodes[currentMachineName] = code;
        else
            globalCodes.Add(currentMachineName, code);
    }

    // ==========================================
    // ✨ [핵심 추가] 창이 닫힐 때 무조건 현재 텍스트를 임시 저장하는 기능
    // ==========================================
    public void SaveCurrentInput() {
        if (string.IsNullOrEmpty(currentMachineName)) return;

        string currentText = "";
        InGameCodeEditor.CodeEditor codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        if (codeEditor != null) {
            currentText = codeEditor.Text;
        } else {
            currentText = inputField.text;
        }

        if (globalCodes.ContainsKey(currentMachineName))
            globalCodes[currentMachineName] = currentText;
        else
            globalCodes.Add(currentMachineName, currentText);
    }

    public void CloseWindow() {
        SaveCurrentInput(); // ✨ 창이 닫히기 직전에 자동 저장!
        codingPanel.SetActive(false);
        if (buildManager != null) buildManager.CancelBuildMode();
    }
    
    public void CloseWindowOnly() {
        SaveCurrentInput(); // ✨ 창이 닫히기 직전에 자동 저장!
        codingPanel.SetActive(false);
    }

    public void SetSavedCode(string machineName, string code) {
        if (globalCodes.ContainsKey(machineName)) globalCodes[machineName] = code;
        else globalCodes.Add(machineName, code);
    }

    public string GetSavedCode(string machineName) {
        if (globalCodes.ContainsKey(machineName)) return globalCodes[machineName];
        return "";
    }
}