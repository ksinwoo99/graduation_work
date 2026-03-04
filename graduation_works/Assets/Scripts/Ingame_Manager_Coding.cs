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

        CheckCodeAndApply(savedCode);
    }

    void OnClick_Verify() {
        CheckCodeAndApply(inputField.text);
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
                SaveCode(code);
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

    public void CloseWindow() {
        codingPanel.SetActive(false);
        if (buildManager != null) buildManager.CancelBuildMode();
    }
    
    public void CloseWindowOnly() {
        codingPanel.SetActive(false);
    }
}