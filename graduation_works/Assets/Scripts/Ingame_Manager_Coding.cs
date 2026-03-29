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
    public TextMeshProUGUI txtLoopLevelInfo; 
    public TextMeshProUGUI txtConveyorSpeedInfo; // ✨ [추가] 컨베이어 속도 상태 텍스트

    [Header("매니저 연결")]
    public Ingame_Manager_Build buildManager;
    
    private Dictionary<int, string> globalCodes = new Dictionary<int, string>();
    public logic_CodingBase currentLogic;
    private int currentMachineId; 

    private Ingame_Button_Build currentBuildButton;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
        
        // ✨ [수정] 게임 시작 시 반복문과 컨베이어 상태를 한 번에 갱신합니다!
        UpdateSystemStatusText();
    }

    // ✨ [수정] 이름이 변경되었으며 컨베이어 갱신 로직이 추가되었습니다.
    public void UpdateSystemStatusText() {
        if (Ingame_Manager_Quest.Instance == null) return;

        // 1. 반복문 레벨 표시
        if (txtLoopLevelInfo != null) {
            int lvl = Ingame_Manager_Quest.Instance.loopUpgradeLevel;
            if (lvl == 0) txtLoopLevelInfo.text = "시스템: [반복문 사용 불가]";
            else if (lvl == 1) txtLoopLevelInfo.text = "시스템: [for문 최대 10회 가능]";
            else txtLoopLevelInfo.text = "시스템: [무한 루프 사용 가능]";
        }

        // 2. ✨ [핵심 추가] 컨베이어 속도 표시
        if (txtConveyorSpeedInfo != null) {
            bool isUpgraded = Ingame_Manager_Quest.Instance.isConveyorUpgraded;
            if (isUpgraded) txtConveyorSpeedInfo.text = "컨베이어: [고속(fast) 모드 해금됨]";
            else txtConveyorSpeedInfo.text = "컨베이어: [일반(slow) 모드]";
        }
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
        
        // ✨ 코딩창을 열 때도 상태 텍스트 갱신!
        UpdateSystemStatusText();

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

    public int CheckCodeAndApply(string code) {
        if (currentLogic == null) return 0;
        
        string newName = "";
        bool hasName = false;

        Match directMatch = Regex.Match(code, @"name\s*=\s*[""']([^""']+)[""']");
        
        if (directMatch.Success) {
            newName = directMatch.Groups[1].Value;
            hasName = true;
        } 
        else {
            Match varMatch = Regex.Match(code, @"name\s*=\s*([a-zA-Z_][a-zA-Z0-9_]*)");
            if (varMatch.Success) {
                string targetVar = varMatch.Groups[1].Value; 
                Match valueMatch = Regex.Match(code, targetVar + @"\s*=\s*[""']([^""']+)[""']");
                if (valueMatch.Success) {
                    newName = valueMatch.Groups[1].Value;
                    hasName = true;
                }
            }
        }

        string validationCode = code;
        if (hasName) {
            validationCode += $"\nname=\"{newName}\"";
        }

        if (globalCodes.ContainsKey(currentMachineId)) globalCodes[currentMachineId] = code;
        else globalCodes.Add(currentMachineId, code);

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

            logic_CodingBase.CodeState state = currentLogic.ValidateCode(validationCode);

            if (state == logic_CodingBase.CodeState.Valid) {
                SetStatus(Color.green, true); return 2; 
            } else if (state == logic_CodingBase.CodeState.Empty) {
                SetStatus(Color.yellow, false); return 1; 
            } else if (state == logic_CodingBase.CodeState.Error_LoopLocked) {
                SetStatus(Color.red, false); return -1; 
            } else if (state == logic_CodingBase.CodeState.Error_LoopLimit) {
                SetStatus(Color.red, false); return -2; 
            } else if (state == logic_CodingBase.CodeState.Error_InfiniteLocked) {
                SetStatus(Color.red, false); return -3; 
            } else {
                SetStatus(Color.red, false); return 0; 
            }
        } else {
            SetStatus(Color.red, false); 
            return -4; 
        }
    }

    void SetStatus(Color color, bool isAllowed) {
        if (statusLight != null) statusLight.color = color;
        if (buildManager != null) buildManager.SetPlacementPermission(isAllowed);
    }
}