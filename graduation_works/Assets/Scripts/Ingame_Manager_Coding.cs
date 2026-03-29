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
    public TextMeshProUGUI txtConveyorSpeedInfo;

    [Header("폰트 줌(확대/축소) 설정")] 
    public float minFontSize = 10f;   
    public float maxFontSize = 60f;   
    public float fontZoomSpeed = 3f;  

    [Header("매니저 연결")]
    public Ingame_Manager_Build buildManager;
    
    private Dictionary<int, string> globalCodes = new Dictionary<int, string>();
    public logic_CodingBase currentLogic;
    private int currentMachineId; 

    private Ingame_Button_Build currentBuildButton;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
        
        UpdateSystemStatusText();
    }

    void Update() {
        if (codingPanel != null && codingPanel.activeSelf) {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) {
                float scroll = Input.mouseScrollDelta.y;
                
                if (scroll != 0) {
                    var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
                    
                    if (codeEditor != null) {
                        TMP_Text[] allTexts = codeEditor.GetComponentsInChildren<TMP_Text>(true);
                        
                        if (allTexts.Length > 0) {
                            float currentSize = allTexts[0].fontSize;
                            float newSize = Mathf.Clamp(currentSize + (scroll * fontZoomSpeed), minFontSize, maxFontSize);
                            
                            // 1. 모든 텍스트 크기 조절
                            foreach (var txt in allTexts) {
                                txt.fontSize = newSize;
                            }

                            // ✨ 2. [추가] 회색 하이라이트 바(LineHighlight)의 높이도 폰트 비율에 맞춰 키워줍니다!
                            Transform highlight = codeEditor.transform.Find("LineHighlight");
                            if (highlight != null) {
                                RectTransform rt = highlight.GetComponent<RectTransform>();
                                if (rt != null) {
                                    // 기본 폰트 14일 때 높이가 21이므로, 1.5배 비율을 적용합니다.
                                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, newSize * 1.5f);
                                }
                            }
                        }
                    } 
                    else if (inputField != null && inputField.textComponent != null) {
                        float currentSize = inputField.textComponent.fontSize;
                        float newSize = Mathf.Clamp(currentSize + (scroll * fontZoomSpeed), minFontSize, maxFontSize);
                        
                        inputField.textComponent.fontSize = newSize;
                        if (inputField.placeholder != null) {
                            TMP_Text placeholderText = inputField.placeholder.GetComponent<TMP_Text>();
                            if (placeholderText != null) placeholderText.fontSize = newSize;
                        }
                    }
                }
            }
        }
    }

    public void UpdateSystemStatusText() {
        if (Ingame_Manager_Quest.Instance == null) return;

        if (txtLoopLevelInfo != null) {
            int lvl = Ingame_Manager_Quest.Instance.loopUpgradeLevel;
            if (lvl == 0) txtLoopLevelInfo.text = "사용 불가";
            else if (lvl == 1) txtLoopLevelInfo.text = "최대 10회";
            else txtLoopLevelInfo.text = "무한 루프 가능";
        }

        if (txtConveyorSpeedInfo != null) {
            int convLevel = Ingame_Manager_Quest.Instance.conveyorUpgradeLevel;
            if (convLevel == 0) txtConveyorSpeedInfo.text = "사용 불가";
            else if (convLevel == 1) txtConveyorSpeedInfo.text = "일반(slow) 모드";
            else txtConveyorSpeedInfo.text = "고속(fast) 모드";
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
                string clean = code.Replace(" ", "").ToLower();
                if (clean.Contains("for") || clean.Contains("while") || clean.Contains("loop:")) {
                    if (Ingame_Manager_Quest.Instance != null) {
                        Ingame_Manager_Quest.Instance.AddLoopUsageProgress();
                    }
                }
                SetStatus(Color.green, true); return 2; 
            } else if (state == logic_CodingBase.CodeState.Empty) {
                SetStatus(Color.yellow, false); return 1; 
            } else if (state == logic_CodingBase.CodeState.Error_LoopLocked) {
                SetStatus(Color.red, false); return -1; 
            } else if (state == logic_CodingBase.CodeState.Error_LoopLimit) {
                SetStatus(Color.red, false); return -2;
            } else if (state == logic_CodingBase.CodeState.Error_InfiniteLocked) {
                SetStatus(Color.red, false); return -3; 
            } else if (state == logic_CodingBase.CodeState.Error_ConveyorLocked) {
                SetStatus(Color.red, false); return -5; 
            } else if (state == logic_CodingBase.CodeState.Error_ConveyorFastLocked) {
                SetStatus(Color.red, false); return -6; 
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