using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions; 

public class Ingame_Manager_Coding : MonoBehaviour {
    [Header("UI 연결")]
    public GameObject codingPanel;
    public TextMeshProUGUI titleText;
    public TMP_InputField inputField;
    public Button btnVerify;
    public Image statusLight;

    [Header("매니저 연결")]
    public Ingame_Manager_Build buildManager;
    
    private Dictionary<int, string> globalCodes = new Dictionary<int, string>();
    public logic_CodingBase currentLogic;
    private int currentMachineId; 

    private Ingame_Button_Build currentBuildButton;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
    }

    public void OpenFromExternal(int machineId, string displayName, UnityEngine.Tilemaps.TileBase tile, Image btnImage, logic_CodingBase logicScript) {
        if (codingPanel.activeSelf && currentMachineId == machineId) {
            CloseWindow();
            return;
        }

        if (codingPanel.activeSelf) SaveCurrentInput();

        currentMachineId = machineId;
        currentLogic = logicScript;
        
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

    public string GetSavedCode(int machineId) {
        if (globalCodes.ContainsKey(machineId)) return globalCodes[machineId];
        return "";
    }

    public void SetSavedCode(int machineId, string code) {
        if (globalCodes.ContainsKey(machineId)) globalCodes[machineId] = code;
        else globalCodes.Add(machineId, code);
    }

    void OnClick_Verify() {
        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        string codeToVerify = (codeEditor != null) ? codeEditor.Text : inputField.text;
        CheckCodeAndApply(codeToVerify);
    }

    // ✨ [핵심 변경] void에서 int로 반환형을 변경하여 상세한 상태를 알려줍니다.
    public int CheckCodeAndApply(string code) {
        if (currentLogic == null) return 0;
        
        // ==============================================================
        // ✨ [업그레이드된 이름 탐지기] 직접 할당 및 변수 참조 모두 감지!
        string newName = "";
        bool hasName = false;

        // 1단계: name = "이름" (직접 할당) 방식 찾기
        Match directMatch = Regex.Match(code, @"name\s*=\s*[""']([^""']+)[""']");
        
        if (directMatch.Success) {
            newName = directMatch.Groups[1].Value;
            hasName = true;
        } 
        else {
            // 2단계: name = a 처럼 따옴표 없는 변수를 넣었을 경우 찾기
            Match varMatch = Regex.Match(code, @"name\s*=\s*([a-zA-Z_][a-zA-Z0-9_]*)");
            if (varMatch.Success) {
                string targetVar = varMatch.Groups[1].Value; // 'a' 라는 변수명 획득
                
                // 3단계: 위쪽에 a = "이름" 이라고 정의된 부분이 있는지 역추적!
                Match valueMatch = Regex.Match(code, targetVar + @"\s*=\s*[""']([^""']+)[""']");
                if (valueMatch.Success) {
                    newName = valueMatch.Groups[1].Value;
                    hasName = true;
                }
            }
        }
        // ==============================================================

        // 🚨 [새로 추가된 트릭] 기계 부품(logic)은 옛날 방식(name="...")만 고집하므로,
        // 검사를 통과시키기 위해 코드 맨 밑에 몰래 따옴표 형식을 붙여서 검사받게 속입니다!
        string validationCode = code;
        if (hasName) {
            validationCode += $"\nname=\"{newName}\"";
        }

        // 🔥 code 대신 몰래 조작한 validationCode를 검사기에 넣습니다!
        bool isLogicValid = (currentLogic.ValidateCode(validationCode) == logic_CodingBase.CodeState.Valid);

        // 🔥 코드를 검사할 때마다 무조건 딕셔너리에 코드를 저장합니다! (원본 그대로 저장)
        if (globalCodes.ContainsKey(currentMachineId)) globalCodes[currentMachineId] = code;
        else globalCodes.Add(currentMachineId, code);

        // 찾은 이름이 있을 경우 UI와 퀘스트를 업데이트합니다.
        if (hasName && !string.IsNullOrEmpty(newName)) {
            if (titleText != null) titleText.text = $"{newName}.py";
            if (currentBuildButton != null && currentBuildButton.nameText != null) {
                currentBuildButton.nameText.text = newName;
            }

            if (Ingame_Manager_Quest.Instance != null) {
                if (currentLogic.GetComponent<logic_Miner_Master>() != null) {
                    Ingame_Manager_Quest.Instance.isMinerNameChanged = true;
                }
            }

            if (isLogicValid) {
                // 1. 이름도 있고 함수도 완벽할 때 (초록불, 설치 허용)
                SetStatus(Color.green, true);
                return 2; 
            } else {
                // 2. 이름은 바꿨지만 함수가 없을 때 (노란불, 설치 불가!)
                SetStatus(Color.yellow, false); 
                return 1; 
            }
        } else {
            // 3. 이름 변수가 아예 없을 때 (빨간불, 설치 불가!)
            SetStatus(Color.red, false); 
            return 0; 
        }
    }

    void SetStatus(Color color, bool isAllowed) {
        if (statusLight != null) statusLight.color = color;
        if (buildManager != null) buildManager.SetPlacementPermission(isAllowed);
    }
}