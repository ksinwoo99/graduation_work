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
    private logic_CodingBase currentLogic;
    private string currentMachineName;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);

        // 들어오는 문자 체크
        if (inputField != null) {
            inputField.onValidateInput += ValidateTab; 
        }
    }

    // 들어오는 문자가 탭(\t)이면 널 문자(\0)를 반환해서 입력 무시
    private char ValidateTab(string text, int charIndex, char addedChar) {
        if (addedChar == '\t') {
            return '\0'; 
        }
        return addedChar; // 탭이 아니면 정상적으로 입력 허용
    }

    // 매 프레임 탭 키 입력을 감시 (우리가 원하는 동작 수행)
    void Update() {
        if (codingPanel.activeSelf && inputField != null && inputField.isFocused) {
            if (Input.GetKeyDown(KeyCode.Tab)) {
                InsertFourSpaces();
            }
        }
    }

    // 스페이스바 4번 강제 주입 함수
    void InsertFourSpaces() {
        if (inputField == null) return;

        string tabString = "    "; // 공백 4칸
        
        int caretPos = inputField.caretPosition;
        
        inputField.text = inputField.text.Insert(caretPos, tabString);
        inputField.caretPosition = caretPos + tabString.Length;
        inputField.ForceLabelUpdate();
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

        inputField.text = savedCode;

        if (buildManager != null) buildManager.StartBuildMode(tile, btnImage);

        CheckCodeAndApply(savedCode);
    }

    void OnClick_Verify() {
        CheckCodeAndApply(inputField.text);
    }

    void CheckCodeAndApply(string code) {
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