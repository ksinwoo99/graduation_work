using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions; 

public class Ingame_Manager_Coding : MonoBehaviour {
    public static Ingame_Manager_Coding Instance;

    protected void Awake() {
        // 인스턴스 할당
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

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
    public logic_CodingBase GetCurrentTargetLogic() {
    return currentLogic;
    }
    private int currentMachineId; 

    private Ingame_Button_Build currentBuildButton;

    [Header("동적 난이도 (코드 고장 이벤트)")]
    public bool enableDynamicDifficulty = true; // 이벤트 활성화 여부
    public float breakdownCheckInterval = 180f; // 3분(180초)마다 발생 검사
    [Range(0f, 1f)] public float breakdownProbability = 0.25f; // 발생 확률 (25%)

    public Dictionary<int, int> machineWorkCounts = new Dictionary<int, int>(); // 작업 횟수 추적
    public Dictionary<int, bool> brokenMachines = new Dictionary<int, bool>(); // 현재 고장 상태인지 확인
    public Dictionary<int, string> forbiddenKeywords = new Dictionary<int, string>(); // 금지된 문법 (for, while)

    [Header("복구 및 포기 시스템")]
    public Button btnGiveUp; 
    public float autoFixTime = 90f; 
    private Dictionary<int, string> backupCodes = new Dictionary<int, string>(); 
    private Dictionary<int, float> autoFixTimers = new Dictionary<int, float>(); 

    [Header("상시 고장 알림 UI")]
    public GameObject breakdownStatusPanel; 
    public TextMeshProUGUI txtBreakdownList;

    [Header("시간 연장 시스템")]
    public Button btnExtendTime; // 시간 연장 버튼
    public float extendAmount = 60f; // 한 번 누를 때마다 연장될 시간 (60초)
    public int extendCost = 200; // 연장 비용 (200G)


    void Start() {
        if (codingPanel != null) codingPanel.SetActive(false);
        if (btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
        if (btnThemeToggle != null) btnThemeToggle.onClick.AddListener(ToggleTheme);
        if (btnGiveUp != null) btnGiveUp.onClick.AddListener(OnClick_GiveUp);
        StartCoroutine(DynamicDifficultyRoutine());
        if (btnExtendTime != null) btnExtendTime.onClick.AddListener(OnClick_ExtendTime);
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
        UpdateBreakdownStatusUI();
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
        CheckCodeAndApply(savedCode, false);

        if (btnGiveUp != null) {
            bool isCurrentBroken = brokenMachines.ContainsKey(currentMachineId) && brokenMachines[currentMachineId];
            btnGiveUp.gameObject.SetActive(isCurrentBroken);
        }
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
        CheckCodeAndApply(codeToVerify, true);
    }

    public int CheckCodeAndApply(string code, bool isManualClick = false) {
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
            Ingame_Button_Build[] allButtons = FindObjectsOfType<Ingame_Button_Build>(true);
            foreach (var btn in allButtons) {
                Iteminfo_Base info = btn.GetComponent<Iteminfo_Base>();
                if (info != null && Ingame_System_Save.Instance != null) {
                    string engName = info.machinePrefab != null ? info.machinePrefab.name : info.machineName;
                    int mId = Ingame_System_Save.Instance.GetMachineTypeInt(engName);
                    
                    if (mId != currentMachineId && info.machineName == newName) {
                        SetStatus(Color.red, false); 
                        return -7;
                    }
                }
            }

            if (titleText != null) titleText.text = $"{newName}.py";
            
            if (currentBuildButton != null) {
                if (currentBuildButton.nameText != null) currentBuildButton.nameText.text = newName;
                
                Iteminfo_Base info = currentBuildButton.GetComponent<Iteminfo_Base>();
                if (info != null) {
                    info.machineName = newName;
                    
                    if (buildManager != null && buildManager.machineInfoUI != null && buildManager.machineInfoUI.gameObject.activeSelf) {
                        buildManager.machineInfoUI.ShowInfo(info);
                    }
                }
            }

            if (Ingame_Manager_Quest.Instance != null) {
                if (currentLogic.GetComponent<logic_Miner_Master>() != null) Ingame_Manager_Quest.Instance.isMinerNameChanged = true;
            }

            if (code.Contains("# [ERROR]") || code.Contains("X_ERROR_X")) {
                SetStatus(Color.red, false); 
                return -8;
            }

            if (forbiddenKeywords.ContainsKey(currentMachineId) && !string.IsNullOrEmpty(forbiddenKeywords[currentMachineId])) {
                string banned = forbiddenKeywords[currentMachineId];
                string clean = code.Replace(" ", "").ToLower();
                if (clean.Contains(banned)) {
                    if (buildManager != null) buildManager.ShowFloatingText($"# 에러: '{banned}' 문법은 현재 사용할 수 없습니다!", codingPanel.transform.position);
                    SetStatus(Color.red, false); 
                    return -8;
                }
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
                if (isManualClick && brokenMachines.ContainsKey(currentMachineId) && brokenMachines[currentMachineId]) {
                brokenMachines[currentMachineId] = false;
                forbiddenKeywords[currentMachineId] = ""; 
                if (autoFixTimers.ContainsKey(currentMachineId)) autoFixTimers.Remove(currentMachineId);

                if (Ingame_Manager_Resource.Instance != null) {
                    var resMgr = Ingame_Manager_Resource.Instance;
                    resMgr.resCommon = Mathf.Min(resMgr.resCommon + (resMgr.maxResCommon / 2), resMgr.maxResCommon);
                    resMgr.resRare = Mathf.Min(resMgr.resRare + (resMgr.maxResRare / 2), resMgr.maxResRare);
                    resMgr.resSpecial = Mathf.Min(resMgr.resSpecial + (resMgr.maxResSpecial / 2), resMgr.maxResSpecial);
                    resMgr.resExotic = Mathf.Min(resMgr.resExotic + (resMgr.maxResExotic / 2), resMgr.maxResExotic);
                    
                    if (buildManager != null) {
                        buildManager.ShowFloatingText("과열 해결 완료! (최대 자원의 50% 지급)", codingPanel.transform.position);
                    }
                }
            }

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

    public void OpenCodingPanelByType(int machineType)
    {
        if (codingPanel.activeSelf) codingPanel.SetActive(false);
        
        codingPanel.SetActive(true);
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
    
    // 동적 난이도 조절 시스템 

    private bool IsAnyMachineBroken() {
        foreach (var isBroken in brokenMachines.Values) {
            if (isBroken) return true; 
        }
        return false;
    }

    IEnumerator DynamicDifficultyRoutine() {
        while (true) {
            yield return new WaitForSecondsRealtime(breakdownCheckInterval);

            bool isSafeMode = (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) || Shared_Manager_Session.IsVisiting;
            if (!enableDynamicDifficulty || isSafeMode || IsAnyMachineBroken()) continue;

            float currentProb = breakdownProbability;
            if (buildManager != null && buildManager.expandCount > 0) currentProb += (buildManager.expandCount * 0.05f);

            if (Random.value <= currentProb) TriggerBreakdownOnRandomMachine();
        }
    }

    public void ReportMachineWork(int machineId) {
        if (machineId < 1 || machineId > 8) return;

        bool isSafeMode = (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive) || Shared_Manager_Session.IsVisiting;
        if (!enableDynamicDifficulty || isSafeMode || globalCodes.Count == 0) return;

        if (!machineWorkCounts.ContainsKey(machineId)) machineWorkCounts[machineId] = 0;
        machineWorkCounts[machineId]++;

        if (IsAnyMachineBroken()) return; 

        if (machineWorkCounts[machineId] >= 50) {
            machineWorkCounts[machineId] = 0; 
            if (Random.value <= 0.05f) TriggerBreakdownOnSpecificMachine(machineId);
        }
    }

    private void TriggerBreakdownOnRandomMachine() {
        if (globalCodes.Count == 0) return;
        List<int> activeMachineIds = new List<int>();
        foreach (var kvp in globalCodes) {
            int mId = kvp.Key;
            if (mId >= 1 && mId <= 8 && !string.IsNullOrEmpty(kvp.Value) && kvp.Value.Length > 5) {
                activeMachineIds.Add(mId);
            }
        }
        if (activeMachineIds.Count == 0) return;

        int targetId = activeMachineIds[Random.Range(0, activeMachineIds.Count)];
        TriggerBreakdownOnSpecificMachine(targetId);
    }

    private void TriggerBreakdownOnSpecificMachine(int targetId) {
        if (!globalCodes.ContainsKey(targetId)) return;
        string brokenCode = globalCodes[targetId];
        if (string.IsNullOrEmpty(brokenCode)) return;

        brokenMachines[targetId] = true; 
        backupCodes[targetId] = brokenCode; // 복구용 원본 백업

        int errorType = Random.Range(0, 5); 
        switch (errorType) {
            case 0: brokenCode = brokenCode.Replace(":", ""); break;
            case 1: brokenCode = brokenCode.Replace("()", "("); break;
            case 2: brokenCode = brokenCode.Replace("mining", "minin").Replace("producting", "productig").Replace("move", "mov"); break;
            case 3: brokenCode = brokenCode.Replace("name=", "name=="); break;
            case 4: 
                string banTarget = brokenCode.Contains("for") ? "for" : (brokenCode.Contains("while") ? "while" : "loop");
                if (banTarget != "loop") {
                    forbiddenKeywords[targetId] = banTarget;
                    brokenCode = $"# [ERROR]\n# 과부하로 인해 '{banTarget}'는 사용할 수 없습니다.!\n# 다른 반복문은 사용 가능합니다.\n" + brokenCode.Replace(banTarget, "X_ERROR_X");
                } else {
                    brokenCode = brokenCode.Replace("(", ""); 
                }
                break;
        }

        globalCodes[targetId] = brokenCode;

        logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>();
        foreach (var m in allMachines) {
            Iteminfo_Base info = m.GetComponent<Iteminfo_Base>();
            if (info != null && Ingame_System_Save.Instance != null) {
                int mId = Ingame_System_Save.Instance.GetMachineTypeInt(info.machinePrefab != null ? info.machinePrefab.name : info.machineName);
                if (mId == targetId) {
                    m.ValidateCode(brokenCode); 
                    if (buildManager != null) {
                        string customName = GetMachineCustomName(targetId);
                        buildManager.ShowFloatingText($"{customName} 고장!", m.transform.position);
                        }
                }
            }
        }

        if (codingPanel.activeSelf && currentMachineId == targetId) {
            var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
            if (codeEditor != null) codeEditor.Text = brokenCode;
            else inputField.text = brokenCode;
            if (statusLight != null) statusLight.color = Color.red;
        }

        StartCoroutine(AutoFixRoutine(targetId)); // 타이머 시작
    }

    // 복구 시스템 및 UI
    public void OnClick_GiveUp() {
        int targetId = 0;
        foreach (var kvp in brokenMachines) {
            if (kvp.Value == true) { 
                targetId = kvp.Key;
                break; 
            }
        }

        if (targetId != 0) {
            RestoreMachine(targetId, true); 
        } else {
            // 혹시 모르니 예외 처리 (고장이 아닌데 버튼이 눌린 경우)
            if (buildManager != null) buildManager.ShowFloatingText("고장난 기계가 없습니다.", transform.position);
        }
    }

    IEnumerator AutoFixRoutine(int targetId) {
    autoFixTimers[targetId] = autoFixTime; 

    // ✨ autoFixTimers에 해당 ID가 있는지 먼저 확인하는 조건 추가
    while (brokenMachines.ContainsKey(targetId) && brokenMachines[targetId] && 
           autoFixTimers.ContainsKey(targetId) && autoFixTimers[targetId] > 0) {
        
        yield return new WaitForSecondsRealtime(1f);

        // 기다리는 동안 데이터가 삭제될 수 있으므로 다시 한번 체크
        if (autoFixTimers.ContainsKey(targetId)) {
            autoFixTimers[targetId] -= 1f;
        }
    }
    
    if (brokenMachines.ContainsKey(targetId) && brokenMachines[targetId]) {
        RestoreMachine(targetId, false); 
    }
}

    private void RestoreMachine(int targetId, bool isManualGiveUp) {
        if (!brokenMachines.ContainsKey(targetId) || !brokenMachines[targetId]) return;

        brokenMachines[targetId] = false;
        forbiddenKeywords[targetId] = ""; 
        if (autoFixTimers.ContainsKey(targetId)) autoFixTimers.Remove(targetId);

        if (backupCodes.ContainsKey(targetId)) {
            globalCodes[targetId] = backupCodes[targetId];
        }

        if (Ingame_Manager_Resource.Instance != null) {
            if (isManualGiveUp) {
                int penaltyAmount = Mathf.FloorToInt(Ingame_Manager_Resource.Instance.currentGold * 0.25f);
                Ingame_Manager_Resource.Instance.currentGold -= penaltyAmount; 
                
                if (buildManager != null) {
                    buildManager.ShowFloatingText($"수리 포기 (보유 골드의 25%G): -{penaltyAmount}G", codingPanel.transform.position);
                }
            } else {
                if (buildManager != null) buildManager.ShowFloatingText("시간이 지나 과열이 해결됐습니다.", codingPanel.transform.position);
            }
        }

        logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>();
        foreach (var m in allMachines) {
            Iteminfo_Base info = m.GetComponent<Iteminfo_Base>();
            if (info != null && Ingame_System_Save.Instance != null) {
                int mId = Ingame_System_Save.Instance.GetMachineTypeInt(info.machinePrefab != null ? info.machinePrefab.name : info.machineName);
                if (mId == targetId) m.ValidateCode(globalCodes[targetId]);
            }
        }

        if (codingPanel.activeSelf && currentMachineId == targetId) {
            var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
            if (codeEditor != null) codeEditor.Text = globalCodes[targetId];
            else inputField.text = globalCodes[targetId];
            if (statusLight != null) statusLight.color = Color.green; 
            if (btnGiveUp != null) btnGiveUp.gameObject.SetActive(false);
        }
    }

    private bool isShowingCompleteStatus = false;

    private void UpdateBreakdownStatusUI() {
        if (breakdownStatusPanel == null || txtBreakdownList == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        bool hasAnyBreakdown = false;

        foreach (var kvp in brokenMachines) {
            int mId = kvp.Key;
            bool isBroken = kvp.Value;

            if (isBroken && autoFixTimers.ContainsKey(mId)) {
            hasAnyBreakdown = true;
            string machineName = GetMachineCustomName(mId);
            int timeLeft = Mathf.Max(0, Mathf.CeilToInt(autoFixTimers[mId]));
            sb.AppendLine($"{machineName}: {timeLeft}초 후 복구");
            }
        }

        if (hasAnyBreakdown) {
            isShowingCompleteStatus = false; // 고장이 새로 발견되면 완료 상태 해제
            if (!breakdownStatusPanel.activeSelf) breakdownStatusPanel.SetActive(true);
            if (btnExtendTime != null) btnExtendTime.gameObject.SetActive(true);
            txtBreakdownList.text = sb.ToString();
        } else {
            // 고장난 기계가 없는데 패널이 켜져 있다면 (방금 수리됨)
            if (breakdownStatusPanel.activeSelf && !isShowingCompleteStatus) {
                StartCoroutine(HidePanelAfterDelay(1.5f)); // 1.5초 뒤에 닫기
                if (btnExtendTime != null) btnExtendTime.gameObject.SetActive(false);
            }
        }
    }
    
    private IEnumerator HidePanelAfterDelay(float delay) {
        isShowingCompleteStatus = true;
        txtBreakdownList.text = "모든 시스템 정상 가동 중"; // 완료 메시지 변경
        
        yield return new WaitForSeconds(delay);
        
        if (isShowingCompleteStatus) {
            breakdownStatusPanel.SetActive(false);
            isShowingCompleteStatus = false;
        }
    }

    private string GetMachineDisplayName(int id) {
        if (id >= 1 && id <= 4) return "채굴기";
        if (id >= 5 && id <= 8) return "가공기";
        if (id == 9) return "컨베이어";
        return "기계";
    }

    private string GetMachineCustomName(int mId) {
        string code = GetSavedCode(mId);
        if (!string.IsNullOrEmpty(code)) {
            Match directMatch = Regex.Match(code, @"name\s*=\s*[""']([^""']+)[""']");
            if (directMatch.Success) return directMatch.Groups[1].Value;
            
            Match varMatch = Regex.Match(code, @"name\s*=\s*([a-zA-Z_][a-zA-Z0-9_]*)");
            if (varMatch.Success) {
                string targetVar = varMatch.Groups[1].Value; 
                Match valueMatch = Regex.Match(code, targetVar + @"\s*=\s*[""']([^""']+)[""']");
                if (valueMatch.Success) return valueMatch.Groups[1].Value;
            }
        }
        return GetMachineDisplayName(mId);
    }

    public void OnClick_ExtendTime() {
        int targetId = 0;

        // 현재 고장난 기계 찾기
        foreach (var kvp in brokenMachines) {
            if (kvp.Value) {
                targetId = kvp.Key;
                break;
            }
        }

        if (targetId != 0 && autoFixTimers.ContainsKey(targetId)) {
            // 자원 매니저에서 골드 체크
            if (Ingame_Manager_Resource.Instance != null && Ingame_Manager_Resource.Instance.SpendGold(extendCost)) {
                // 시간 연장!
                autoFixTimers[targetId] += extendAmount;
                
                if (buildManager != null) {
                    buildManager.ShowFloatingText($"⏳ 시간 연장 (+{extendAmount}초): -{extendCost}G", transform.position);
                }
            } else {
                if (buildManager != null) buildManager.ShowFloatingText("골드가 부족합니다!", transform.position);
            }
        }
    }

    public Button btnTestBreakdown;
    public void Test_TriggerForcedBreakdown() {
        Debug.Log("🧪 테스트: 강제로 고장 이벤트를 발생시킵니다.");
        TriggerBreakdownOnRandomMachine();
    }

    public void OnClick_TestBreakdown() {
        if (IsAnyMachineBroken()) {
            if (buildManager != null) buildManager.ShowFloatingText("이미 고장난 기계가 있어 테스트가 취소되었습니다.", transform.position);
            return;
        }
        Test_TriggerForcedBreakdown();
    }

}