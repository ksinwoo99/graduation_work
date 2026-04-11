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
    public InGameCodeEditor.CodeEditorTheme darkTheme;  // 다크 테마 에셋 드래그 앤 드롭
    public InGameCodeEditor.CodeEditorTheme lightTheme; // 라이트 테마 에셋 드래그 앤 드롭
    private bool isDarkMode = true; // 기본 상태 (다크모드)

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
                }
            }
        }
    }

    public void OpenFromExternal(int machineId, string displayName, UnityEngine.Tilemaps.TileBase tile, Image btnImage, logic_CodingBase logicScript) {
        if (codingPanel.activeSelf && currentMachineId == machineId) { CloseWindow(); return; }
        if (codingPanel.activeSelf) SaveCurrentInput();

        // 같은 기계인지 확인
        bool isMachineChanged = (currentMachineId != machineId);

        currentMachineId = machineId;
        currentLogic = logicScript;
        if (btnImage != null) currentBuildButton = btnImage.GetComponent<Ingame_Button_Build>();

        codingPanel.SetActive(true);
        
        // 기계가 바뀌었을 때 결과창 끄기
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
            if (currentBuildButton != null && currentBuildButton.nameText != null) currentBuildButton.nameText.text = newName;
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

        // ✨ [핵심 추가] 코딩 검사 결과가 '빨간불(Color.red)'인지 확인하여 튜토리얼 매니저로 즉시 전달!
        // (노란불이나 초록불이라면 에러가 아닌 것으로 판정하여 튜토리얼이 통과됩니다)
        if (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) {
            bool isError = (color == Color.red);
            Ingame_UI_Tutorial.Instance.TriggerCompileResult(isError);
        }
    }

    public void ToggleTheme() {
        // 1. 모드 상태 뒤집기 (true -> false -> true)
        isDarkMode = !isDarkMode;
        
        // 2. CodeEditor 컴포넌트를 찾아서 테마 갈아끼우기
        var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
        if (codeEditor != null) {
            codeEditor.EditorTheme = isDarkMode ? darkTheme : lightTheme;
        }
        
        // 3. (선택) 버튼의 글씨도 '라이트 모드' / '다크 모드'로 바꿔주기
        if (btnThemeToggle != null) {
            TextMeshProUGUI btnText = btnThemeToggle.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) {
                btnText.text = isDarkMode ? "라이트 모드" : "다크 모드";
            }
        }
    }

    // ✨ [추가] 로드 직후 모든 하단 UI 버튼의 이름을 코드에서 읽어와 강제 동기화하는 함수
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

                    // 코드를 읽어서 알아낸 이름이 있다면 버튼 텍스트에 덮어씌움!
                    if (!string.IsNullOrEmpty(newName) && btn.nameText != null) {
                        btn.nameText.text = newName;
                    }
                }
            }
        }
    }
}