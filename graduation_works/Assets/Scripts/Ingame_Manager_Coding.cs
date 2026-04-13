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

    [Header("테마 설정 (다크/라이트 모드)")]
    public Button btnThemeToggle; 
    public InGameCodeEditor.CodeEditorTheme darkTheme;
    public InGameCodeEditor.CodeEditorTheme lightTheme;
    public bool isDarkMode = true;

    [Header("폰트 줌(확대/축소) 설정")] 
    public float minFontSize = 10f;   
    public float maxFontSize = 60f;   
    public float fontZoomSpeed = 3f;  

    [Header("매니저 연결")]
    public Ingame_Manager_Build buildManager;
    
    public Dictionary<int, string> globalCodes = new Dictionary<int, string>();
    public logic_CodingBase currentLogic;
    private int currentMachineId; 

    private Ingame_Button_Build currentBuildButton;

    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
        if (btnThemeToggle != null) btnThemeToggle.onClick.AddListener(ToggleTheme);
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
                            foreach (var txt in allTexts) txt.fontSize = newSize;

                            Transform highlight = codeEditor.transform.Find("LineHighlight");
                            if (highlight != null) {
                                RectTransform rt = highlight.GetComponent<RectTransform>();
                                if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, newSize * 1.5f);
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

                    Canvas.ForceUpdateCanvases(); // UI 레이아웃을 즉시 갱신
                    if (inputField != null && inputField.verticalScrollbar != null) {
                        inputField.verticalScrollbar.value = 1f; // 1은 맨 위, 0은 맨 아래
                    }

                    if (codeEditor != null) {
                        ScrollRect sr = codeEditor.GetComponentInChildren<ScrollRect>();
                        if (sr != null) sr.verticalNormalizedPosition = 1f;
                    }
                }
            }
        }
    }

    public void OpenFromExternal(int machineId, string displayName, UnityEngine.Tilemaps.TileBase tile, Image btnImage, logic_CodingBase logicScript) {
        if (codingPanel.activeSelf && currentMachineId == machineId) { CloseWindow(); return; }
        if (codingPanel.activeSelf) SaveCurrentInput();

        bool isMachineChanged = (currentMachineId != machineId);

        currentMachineId = machineId;
        currentLogic = logicScript;
        if (btnImage != null) currentBuildButton = btnImage.GetComponent<Ingame_Button_Build>();

        codingPanel.SetActive(true);
        
        if (isMachineChanged) {
            Ingame_Button_Debugging debugger = codingPanel.GetComponentInChildren<Ingame_Button_Debugging>(true);
            if (debugger != null) debugger.HideResult();
        }

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

    public void CloseWindowOnly() { SaveCurrentInput(); codingPanel.SetActive(false); }

    public string GetSavedCode(int machineId) { return globalCodes.ContainsKey(machineId) ? globalCodes[machineId] : ""; }

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
            newName = directMatch.Groups[1].Value; hasName = true;
        } else {
            Match varMatch = Regex.Match(code, @"name\s*=\s*([a-zA-Z_][a-zA-Z0-9_]*)");
            if (varMatch.Success) {
                string targetVar = varMatch.Groups[1].Value; 
                Match valueMatch = Regex.Match(code, targetVar + @"\s*=\s*[""']([^""']+)[""']");
                if (valueMatch.Success) { newName = valueMatch.Groups[1].Value; hasName = true; }
            }
        }

        string validationCode = code;
        if (hasName) validationCode += $"\nname=\"{newName}\"";

        if (globalCodes.ContainsKey(currentMachineId)) globalCodes[currentMachineId] = code;
        else globalCodes.Add(currentMachineId, code);

        if (hasName && !string.IsNullOrEmpty(newName)) {
            if (titleText != null) titleText.text = $"{newName}.py";
            
            if (currentBuildButton != null) {
                if (currentBuildButton.nameText != null) currentBuildButton.nameText.text = newName;
                
                // ✨ [핵심 추가 1] Iteminfo_Base의 기본 이름도 코딩한 이름으로 덮어씌웁니다!
                Iteminfo_Base info = currentBuildButton.GetComponent<Iteminfo_Base>();
                if (info != null) {
                    info.machineName = newName;
                    
                    // 만약 정보창(Info)이 이미 켜져 있다면, 즉시 바뀐 이름으로 새로고침합니다.
                    if (buildManager != null && buildManager.machineInfoUI != null && buildManager.machineInfoUI.gameObject.activeSelf) {
                        buildManager.machineInfoUI.ShowInfo(info);
                    }
                }
            }

            if (Ingame_Manager_Quest.Instance != null) {
                if (currentLogic.GetComponent<logic_Miner_Master>() != null) Ingame_Manager_Quest.Instance.isMinerNameChanged = true;
            }

            logic_CodingBase.CodeState state = currentLogic.ValidateCode(validationCode);

            logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>();
            foreach(var m in allMachines) {
                if (m.GetMachineName() == currentLogic.GetMachineName()) {
                    m.ValidateCode(validationCode);
                    
                    if (m is logic_Miner_Master miner) miner.InitializeMiner(miner.miningCount);
                    else if (m is logic_Productor_Master productor) productor.InitializeProductor(productor.processingCount);
                }
            }

            if (state == logic_CodingBase.CodeState.Valid) {
                string clean = code.Replace(" ", "").ToLower();
                if (clean.Contains("for") || clean.Contains("while") || clean.Contains("loop:")) {
                    if (Ingame_Manager_Quest.Instance != null) {
                        if (currentLogic is logic_Miner_Master) 
                            Ingame_Manager_Quest.Instance.isMinerLoopUsed = true;
                        else if (currentLogic is logic_Productor_Master) 
                            Ingame_Manager_Quest.Instance.isProductorLoopUsed = true;
                    }
                }
                SetStatus(Color.green, true); return 2; 
            } else if (state == logic_CodingBase.CodeState.Empty) { SetStatus(Color.yellow, false); return 1; 
            } else if (state == logic_CodingBase.CodeState.Error_LoopLocked) { SetStatus(Color.red, false); return -1; 
            } else if (state == logic_CodingBase.CodeState.Error_LoopLimit) { SetStatus(Color.red, false); return -2;
            } else if (state == logic_CodingBase.CodeState.Error_InfiniteLocked) { SetStatus(Color.red, false); return -3; 
            } else if (state == logic_CodingBase.CodeState.Error_ConveyorLocked) { SetStatus(Color.red, false); return -5; 
            } else if (state == logic_CodingBase.CodeState.Error_ConveyorFastLocked) { SetStatus(Color.red, false); return -6; 
            } else { SetStatus(Color.red, false); return 0; }
        } else {
            SetStatus(Color.red, false); return -4; 
        }
    }

    void SetStatus(Color color, bool isAllowed) {
        if (statusLight != null) statusLight.color = color;
        if (buildManager != null) buildManager.SetPlacementPermission(isAllowed);

        if (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) {
            bool isError = (color == Color.red);
            Ingame_UI_Tutorial.Instance.TriggerCompileResult(isError);
        }
    }

    public void ToggleTheme() {
        isDarkMode = !isDarkMode;
        
        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        if (codeEditor != null) {
            codeEditor.EditorTheme = isDarkMode ? darkTheme : lightTheme;
        }
        
        if (btnThemeToggle != null) {
            TextMeshProUGUI btnText = btnThemeToggle.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) {
                btnText.text = isDarkMode ? "라이트 모드" : "다크 모드";
            }
        }
    }

    public void SyncAllButtonNames() {
        Ingame_Button_Build[] allButtons = FindObjectsOfType<Ingame_Button_Build>(true);
        foreach (var btn in allButtons) {
            Iteminfo_Base info = btn.GetComponent<Iteminfo_Base>();
            if (info != null && Ingame_System_Save.Instance != null) {
                string engName = info.machinePrefab != null ? info.machinePrefab.name : info.machineName;
                int mId = Ingame_System_Save.Instance.GetMachineTypeInt(engName);
                string code = GetSavedCode(mId);

                if (!string.IsNullOrEmpty(code)) {
                    string newName = "";
                    Match directMatch = Regex.Match(code, @"name\s*=\s*[""']([^""']+)[""']");
                    if (directMatch.Success) newName = directMatch.Groups[1].Value;
                    else {
                        Match varMatch = Regex.Match(code, @"name\s*=\s*([a-zA-Z_][a-zA-Z0-9_]*)");
                        if (varMatch.Success) {
                            string targetVar = varMatch.Groups[1].Value; 
                            Match valueMatch = Regex.Match(code, targetVar + @"\s*=\s*[""']([^""']+)[""']");
                            if (valueMatch.Success) newName = valueMatch.Groups[1].Value;
                        }
                    }

                    if (!string.IsNullOrEmpty(newName)) {
                        if (btn.nameText != null) btn.nameText.text = newName;
                        // ✨ [핵심 추가 2] 세이브를 불러왔을 때도 Info 창 이름이 정상 반영되도록 동기화!
                        info.machineName = newName; 
                    }
                }
            }
        }
    }
}