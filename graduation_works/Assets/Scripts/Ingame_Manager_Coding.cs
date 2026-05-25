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
    private Dictionary<int, string> timedBreakdownReasons = new Dictionary<int, string>();

    // 루프 빈도 불균형으로 인한 고장 (시간으로 풀리지 않음. 균형 회복 시에만 복구)
    // 기존 random 고장 시스템과 분리하기 위한 마커 집합.
    private HashSet<int> imbalanceBrokenMachines = new HashSet<int>();
    private Dictionary<int, float> machineImbalanceScores = new Dictionary<int, float>();
    private Dictionary<int, float> smoothedImbalanceScores = new Dictionary<int, float>();
    private int debugGraceCount = 0;
    private const int GRACE_PERIOD = 5;

    [Header("복구 및 포기 시스템")]
    public Button btnGiveUp; 
    public float autoFixTime = 90f; 
    private Dictionary<int, string> backupCodes = new Dictionary<int, string>(); 
    private Dictionary<int, float> autoFixTimers = new Dictionary<int, float>(); 

    [Header("상시 고장 알림 UI")]
    public GameObject breakdownStatusPanel; 
    public TextMeshProUGUI txtBreakdownList;
    public GameObject txtImbalanceNotice;

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
            // 임밸런스 고장은 포기로 풀 수 없음
            bool canGiveUp = isCurrentBroken && !imbalanceBrokenMachines.Contains(currentMachineId);
            btnGiveUp.gameObject.SetActive(canGiveUp);
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

    // 코드창 UI 전체 강제 갱신
    public void RefreshCodingPanelUI(string newCode, Color lightColor, bool showGiveUp) {
        if (inputField != null) {
            var codeEditor = inputField.GetComponentInParent<InGameCodeEditor.CodeEditor>();
            if (codeEditor != null) codeEditor.Text = newCode;
            inputField.text = newCode;
            inputField.ForceLabelUpdate(); 
        }
        
        if (statusLight != null) statusLight.color = lightColor;
        if (btnGiveUp != null) btnGiveUp.gameObject.SetActive(showGiveUp);
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
        CheckCodeAndApply(codeToVerify, false);
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
                // 긴 이름 축약 적용 (원본 newName은 info에 저장하고, UI 텍스트만 줄입니다)
                if (currentBuildButton.nameText != null) 
                    currentBuildButton.nameText.text = GetTruncatedName(newName, 5); 
                
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

                // 주석(#)·문자열 리터럴 안의 'for'/'while' 은 키워드 사용이 아니므로 제외.
                // (고장 시 주입한 안내 주석의 'for'/'while' 이 오탐되는 문제 방지)
                string sanitized = StripCommentsAndStringLiterals(code);
                bool isBannedUsed = Regex.IsMatch(
                    sanitized, $@"\b{Regex.Escape(banned)}\b",
                    RegexOptions.IgnoreCase);

                if (isBannedUsed) {
                    // 임밸런스 고장이면 "부품 부족" 힌트를 우선 표시 (-9), 그 외 과열 고장은 기존 -8
                    bool isImbalance = imbalanceBrokenMachines.Contains(currentMachineId);
                    string opposite = banned == "for" ? "while" : "for";
                    string msg = isImbalance
                        ? $"# [부품 부족] '{banned}' 부품이 모두 소진되었습니다! '{opposite}' 부품을 사용해 균형을 맞춰주세요."
                        : $"# 에러: '{banned}' 문법은 현재 사용할 수 없습니다!";
                    if (buildManager != null) buildManager.ShowFloatingText(msg, codingPanel.transform.position);
                    SetStatus(Color.red, false);
                    return isImbalance ? -9 : -8;
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
                    
                    if (!imbalanceBrokenMachines.Contains(currentMachineId)) {
                        brokenMachines[currentMachineId] = false;
                        forbiddenKeywords[currentMachineId] = ""; 
                        if (autoFixTimers.ContainsKey(currentMachineId)) autoFixTimers.Remove(currentMachineId);

                        if (Ingame_Manager_Resource.Instance != null) {
                            var resMgr = Ingame_Manager_Resource.Instance;
                            resMgr.AddResource(ResourceType.Common, resMgr.maxResCommon / 2);
                            resMgr.AddResource(ResourceType.Rare, resMgr.maxResRare / 2);
                            resMgr.AddResource(ResourceType.Special, resMgr.maxResSpecial / 2);
                            resMgr.AddResource(ResourceType.Exotic, resMgr.maxResExotic / 2);
                            resMgr.EarnGold(resMgr.maxGold / 2);
                            
                            if (buildManager != null) {
                                Vector3 textPos = Camera.main.transform.position + new Vector3(0, 2.5f, 0);
                                buildManager.ShowFloatingText("과열 수리 완료! (최대 자원의 50% 지급)", textPos);
                            }
                        }
                    }
                }

                // 주석/문자열 내 'for'/'while' 오탐 방지 + count() 무한 루프도 감지.
                string clean = StripCommentsAndStringLiterals(code).ToLower();
                bool usedLoop =
                    Regex.IsMatch(clean, @"\bfor\b")
                    || Regex.IsMatch(clean, @"\bwhile\b")
                    || Regex.IsMatch(clean, @"\bcount\s*\(");
                if (usedLoop) {
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
            } else if (state == logic_CodingBase.CodeState.Error_WrongMachineSyntax) {
                string tierHint = GetWrongMachineSyntaxHint();
                if (buildManager != null)
                    buildManager.ShowFloatingText(tierHint, codingPanel.transform.position);
                SetStatus(Color.red, false);
                return -10;
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
                        // 세이브를 로드할 때도 긴 이름 축약 적용
                        if (btn.nameText != null) btn.nameText.text = GetTruncatedName(newName, 5);
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

        string breakdownReason = "";
        int errorType = Random.Range(0, 4); 
        switch (errorType) {
            case 0: 
                brokenCode = System.Text.RegularExpressions.Regex.Replace(brokenCode, @"^.*(mining|producting)\(.*\).*$\r?\n?", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                breakdownReason = "핵심 명령어가 \n 지워졌습니다!";
                break;
            case 1: 
                brokenCode = System.Text.RegularExpressions.Regex.Replace(brokenCode, @"^.*name\s*=.*$\r?\n?", "", System.Text.RegularExpressions.RegexOptions.Multiline);
                breakdownReason = "기계의 이름표가 \n 지워졌습니다!";
                break;
            case 2: 
                string banTarget = brokenCode.Contains("for") ? "for" : (brokenCode.Contains("while") ? "while" : "loop");
                if (banTarget != "loop") {
                    forbiddenKeywords[targetId] = banTarget;
                    brokenCode = $"# [ERROR]\n# 과부하로 인해 '{banTarget}'는 사용할 수 없습니다.!\n# 다른 반복문은 사용 가능합니다.\n" + brokenCode;
                    breakdownReason = $"과부하로 인해 '{banTarget}' \n 문법을 사용할 수 없습니다!";
                } else {
                    brokenCode = brokenCode.Replace("(", ""); 
                    breakdownReason = "코드가 파손되었습니다!";
                }
                break;
            case 3: 
                string[] lines = brokenCode.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 1) {
                    int lineToRemove = Random.Range(1, lines.Length);
                    List<string> lineList = new List<string>(lines);
                    lineList.RemoveAt(lineToRemove);
                    lineList.Insert(lineToRemove, "    # [DATA LOST] 시스템 과부하로 코드가 소실되었습니다.");
                    brokenCode = string.Join("\n", lineList);
                    breakdownReason = "시스템 과부하로 \n 코드의 일부가 날아갔습니다!";
                } else {
                    brokenCode = "# [FATAL ERROR] 데이터 완전 소실"; 
                    breakdownReason = "코드가 완전히 \n 소실되었습니다!";
                }
                break;
        }
        timedBreakdownReasons[targetId] = breakdownReason;
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

                        // ✨ 팝업창 연동
                        if (Ingame_UI_Tutorial.Instance != null) {
                            Ingame_UI_Tutorial.Instance.ShowMessagePanel($"<color=#FF5A5A>[ {customName} 고장 발생! ]</color>\n{breakdownReason}");
                        }
                    }
                }
            }
        }

        if (codingPanel.activeSelf && currentMachineId == targetId) {
            RefreshCodingPanelUI(brokenCode, Color.red, true);

            Ingame_Button_Debugging debugger = codingPanel.GetComponentInChildren<Ingame_Button_Debugging>(true);
            if (debugger != null) debugger.HideResult();
        }
        StartCoroutine(AutoFixRoutine(targetId));
    }

    // 복구 시스템 및 UI
    public void OnClick_GiveUp() {
        int targetId = 0;
        foreach (var kvp in brokenMachines) {
            // 임밸런스 고장은 골드로 포기 불가 — 균형 회복으로만 풀림
            if (kvp.Value == true && !imbalanceBrokenMachines.Contains(kvp.Key)) {
                targetId = kvp.Key;
                break;
            }
        }

        if (targetId != 0) {
            RestoreMachine(targetId, true); 
        } else {
            // 일반 고장이 없으면 임밸런스 고장 안내 / 아무것도 없으면 기본 메시지
            string msg = imbalanceBrokenMachines.Count > 0
                ? "부품 부족 고장은 반대 부품을 사용해서 균형을 맞춰야 풀립니다."
                : "고장난 기계가 없습니다.";
            if (buildManager != null) buildManager.ShowFloatingText(msg, transform.position);
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

        bool wasImbalance = imbalanceBrokenMachines.Contains(targetId);
        if (wasImbalance) imbalanceBrokenMachines.Remove(targetId);

        brokenMachines[targetId] = false;
        forbiddenKeywords[targetId] = ""; 
        if (autoFixTimers.ContainsKey(targetId)) autoFixTimers.Remove(targetId);

        if (wasImbalance) {
            // 임밸런스 회복: 백업으로 덮어쓰지 않고 현재 코드에서 헤더 블록만 제거
            // → 잠금 중 작성한 반대 부품 솔루션이 그대로 보존됩니다.
            if (globalCodes.ContainsKey(targetId)) {
                globalCodes[targetId] = StripImbalanceHeader(globalCodes[targetId]);
            }
        } else if (backupCodes.ContainsKey(targetId)) {
            // 랜덤 고장(코드 깨짐) 복구: 백업으로 통째 복원
            globalCodes[targetId] = backupCodes[targetId];
        }
        if (Ingame_Manager_Resource.Instance != null) {
            var resMgr = Ingame_Manager_Resource.Instance;
            
            if (wasImbalance) {
                resMgr.AddResource(ResourceType.Common, resMgr.maxResCommon / 2);
                resMgr.AddResource(ResourceType.Rare, resMgr.maxResRare / 2);
                resMgr.AddResource(ResourceType.Special, resMgr.maxResSpecial / 2);
                resMgr.AddResource(ResourceType.Exotic, resMgr.maxResExotic / 2);
                resMgr.EarnGold(resMgr.maxGold / 2);
                
                if (buildManager != null) {
                    Vector3 textPos = Camera.main.transform.position + new Vector3(0, 2.5f, 0);
                    buildManager.ShowFloatingText("부품 균형 회복! (최대 자원의 50% 지급)", textPos);
                }
            } else if (isManualGiveUp) {
                int penaltyAmount = Mathf.FloorToInt(resMgr.currentGold * 0.25f);
                resMgr.SpendGold(penaltyAmount); 
                
                if (buildManager != null) {
                    Vector3 textPos = Camera.main.transform.position + new Vector3(0, 2.5f, 0);
                    buildManager.ShowFloatingText($"수리 포기 (보유 골드의 25%G): -{penaltyAmount}G", textPos);
                }
            } else {
                if (buildManager != null) {
                    Vector3 textPos = Camera.main.transform.position + new Vector3(0, 2.5f, 0);
                    buildManager.ShowFloatingText("시간이 지나 과열이 해결됐습니다.", textPos);
                }
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
        bool hasTimedBreakdown = false; 
        bool hasGiveUpBreakdown = false; 
        bool hasImbalanceBreakdown = false; 

        foreach (var kvp in brokenMachines) {
            int mId = kvp.Key;
            bool isBroken = kvp.Value;
            if (!isBroken) continue;

            string machineName = GetMachineCustomName(mId);

            if (imbalanceBrokenMachines.Contains(mId)) {
                hasAnyBreakdown = true;
                hasImbalanceBreakdown = true;
                string banned = forbiddenKeywords.ContainsKey(mId) ? forbiddenKeywords[mId] : "?";
                string opposite = banned == "for" ? "while" : (banned == "while" ? "for" : "다른");
                
                // 1. 서버가 로그 기반으로 계산한 정직한 목표 점수 (0.0 ~ 1.0)
                float targetScore = machineImbalanceScores.ContainsKey(mId) ? machineImbalanceScores[mId] : 0.6f;
                if (!smoothedImbalanceScores.ContainsKey(mId)) smoothedImbalanceScores[mId] = targetScore;
                smoothedImbalanceScores[mId] = Mathf.MoveTowards(smoothedImbalanceScores[mId], targetScore, Time.deltaTime * 3f);
                float realPercent = (smoothedImbalanceScores[mId] / 2f + 0.5f) * 100f;
                float remainBias = Mathf.Max(0f, realPercent - 65f);
                sb.AppendLine($"{machineName}: 부품 부족 ('{banned}' 소진)\n<color=#FFDD00>[남은 편향도: {remainBias:F1}]</color>\n'{opposite}' 사용으로 균형 회복 필요");
            }
            // 2) 일반 과열 고장일 때
            else {
                hasAnyBreakdown = true;
                hasTimedBreakdown = true;
                hasGiveUpBreakdown = true; 

                string standardReason = timedBreakdownReasons.ContainsKey(mId) ? timedBreakdownReasons[mId] : "코드 파손됨";

                // ✨ [핵심 추가] 이름표가 소실된 케이스라면, 백업 코드에서 원래 이름을 역추적해 옵니다!
                if (standardReason.Contains("이름표") && backupCodes.ContainsKey(mId)) {
                    string originalCode = backupCodes[mId];
                    // 원본 코드에서 name = "값" 또는 name = '값' 패턴을 정규식으로 매칭
                    var match = System.Text.RegularExpressions.Regex.Match(originalCode, @"name\s*=\s*['""](.*?)['""]");
                    
                    if (match.Success) {
                        // 추출 성공 시 지워지기 전 원래 이름으로 덮어씌우기
                        machineName = match.Groups[1].Value;
                    }
                }

                int timeLeft = autoFixTimers.ContainsKey(mId) ? Mathf.Max(0, Mathf.CeilToInt(autoFixTimers[mId])) : 0;
                sb.AppendLine($"{machineName} : <color=#FFDD00>[{standardReason}]</color><br>  - 자동 복구까지: {timeLeft}초");
            }
        }

        if (hasAnyBreakdown) {
            isShowingCompleteStatus = false; 
            if (!breakdownStatusPanel.activeSelf) breakdownStatusPanel.SetActive(true);
            if (btnExtendTime != null) btnExtendTime.gameObject.SetActive(hasTimedBreakdown);
            if (btnGiveUp != null) btnGiveUp.gameObject.SetActive(hasGiveUpBreakdown); 
            
            if (txtImbalanceNotice != null) txtImbalanceNotice.SetActive(hasImbalanceBreakdown);
            
            txtBreakdownList.text = sb.ToString();
        } else {
            if (breakdownStatusPanel.activeSelf && !isShowingCompleteStatus) {
                StartCoroutine(HidePanelAfterDelay(1.5f)); 
                if (btnExtendTime != null) btnExtendTime.gameObject.SetActive(false);
                if (btnGiveUp != null) btnGiveUp.gameObject.SetActive(false);
                
                if (txtImbalanceNotice != null) txtImbalanceNotice.SetActive(false);
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

        // 현재 고장난 기계 찾기 (임밸런스 고장은 타이머가 없으므로 제외)
        foreach (var kvp in brokenMachines) {
            if (kvp.Value && !imbalanceBrokenMachines.Contains(kvp.Key)) {
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

    // 임밸런스 고장 시 상단에 주입되는 안내 블록의 시작/끝 마커.
    // 복구 시 이 블록만 정확히 제거하기 위해 unique 한 식별 문자열을 사용합니다.
    private const string ImbalanceHeaderStart = "# <<< BALANCE_LOCK_START >>>";
    private const string ImbalanceHeaderEnd   = "# <<< BALANCE_LOCK_END >>>";

    // 주어진 코드에서 임밸런스 헤더 블록(시작 마커 ~ 끝 마커, 두 마커 포함)을 제거합니다.
    // 마커가 없으면 원본을 그대로 반환합니다.
    string GetWrongMachineSyntaxHint() {
        if (currentLogic is logic_Miner_Master miner)
            return $"이 채굴기는 {miner.requiredSyntax} 만 사용할 수 있어요.";
        if (currentLogic is logic_Productor_Master productor) {
            string tier = string.IsNullOrEmpty(productor.allowedProductingTierDisplay)
                ? GameCodeValidator.GetProductingTierForMachine(currentLogic.GetMachineName())
                : productor.allowedProductingTierDisplay;
            if (!string.IsNullOrEmpty(tier))
                tier = char.ToUpper(tier[0]) + tier.Substring(1);
            return $"이 가공기는 producting({tier}, 'A' 또는 'B') 만 사용할 수 있어요.";
        }
        return "이 기계 등급에 맞는 함수 인자만 사용할 수 있어요.";
    }

    private static string StripImbalanceHeader(string code) {
        if (string.IsNullOrEmpty(code)) return code;
        int startIdx = code.IndexOf(ImbalanceHeaderStart);
        if (startIdx < 0) return code;
        int endIdx = code.IndexOf(ImbalanceHeaderEnd, startIdx);
        if (endIdx < 0) return code;

        // 끝 마커가 포함된 줄 전체를 제거 (개행 포함)
        int endLineEnd = code.IndexOf('\n', endIdx);
        if (endLineEnd < 0) endLineEnd = code.Length;
        else endLineEnd += 1;

        return code.Substring(0, startIdx) + code.Substring(endLineEnd);
    }

    // 주석(#) 과 문자열 리터럴(' " 둘 다) 내부를 공백으로 치환해 반환합니다.
    // 금지 키워드 검사(`forbiddenKeywords`) 가 안내 주석/문자열 내부의
    // 'for'/'while' 단어를 오탐하지 않도록 하기 위한 전처리.
    //
    // 단순 라인 단위 파서 — Python 의 삼중따옴표 / 이스케이프는 처리하지 않지만,
    // 본 게임의 짧은 한 줄짜리 코드 스타일에는 충분합니다.
    private static string StripCommentsAndStringLiterals(string code) {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        var sb = new System.Text.StringBuilder(code.Length);
        foreach (string line in code.Split('\n')) {
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < line.Length; i++) {
                char c = line[i];

                if (!inSingle && !inDouble && c == '#') break; // 라인 나머지는 주석

                if (!inDouble && c == '\'') { inSingle = !inSingle; sb.Append(' '); continue; }
                if (!inSingle && c == '"')  { inDouble = !inDouble; sb.Append(' '); continue; }

                sb.Append((inSingle || inDouble) ? ' ' : c);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────
    // 루프 임밸런스 고장 — 서버 /api/submit_code 응답으로 트리거/복구
    //
    // 동작 흐름:
    //   - shouldBreak  → 현재 디버그 중인 기계를 'consumedPart'(for|while) 사용 불가 상태로 고장
    //   - isFixed      → 모든 임밸런스 고장 기계를 일괄 복구
    //   - 중간 영역(0.3 < imbalance < 0.5) → 둘 다 false, 기존 상태 유지
    //   - 편향도는 서버가 installed_machines(채굴기·가공기 8종) 저장 코드로 계산
    //
    // 기존 random 고장 시스템과 분리: imbalanceBrokenMachines 마커로 구분.
    // ──────────────────────────────────────────────────────────
    public void HandleLoopImbalance(bool shouldBreak, bool isFixed,
                                    string consumedPart, float imbalanceScore) {
        bool isSafeMode = (Ingame_UI_Tutorial.Instance != null && Ingame_UI_Tutorial.Instance.isTutorialActive)
                          || Shared_Manager_Session.IsVisiting;
        if (!enableDynamicDifficulty || isSafeMode) return;

        // 현재 디버깅 중인 기계의 실시간 편향도 수치를 기록합니다.
        int targetId = currentMachineId;
        if (targetId >= 1 && targetId <= 8) {
            machineImbalanceScores[targetId] = imbalanceScore;
        }

        debugGraceCount++;
        if (debugGraceCount <= GRACE_PERIOD) return; 

        // 1) 균형 회복 신호 → 임밸런스 고장 모두 해제
        if (isFixed && imbalanceBrokenMachines.Count > 0) {
            // 순회 중 수정 방지를 위해 사본
            int[] ids = new int[imbalanceBrokenMachines.Count];
            imbalanceBrokenMachines.CopyTo(ids);
            foreach (int id in ids) RestoreMachine(id, false);
            return;
        }

        // 2) 편향 신호 → 현재 디버그 중인 기계를 임밸런스 고장으로 전환
        if (shouldBreak) {
            // 부품 종류 검증 (예상 외 값이면 무시)
            if (consumedPart != "for" && consumedPart != "while") return;

            // 이미 임밸런스 고장이 활성화 중이면 중복 트리거 방지
            if (imbalanceBrokenMachines.Count > 0) return;

            // targetId 재검증 후 고장 발생
            if (targetId < 1 || targetId > 8 || !globalCodes.ContainsKey(targetId) || string.IsNullOrEmpty(globalCodes[targetId])) {
                foreach (var kvp in globalCodes) {
                    if (kvp.Key >= 1 && kvp.Key <= 8 && !string.IsNullOrEmpty(kvp.Value) && kvp.Value.Length > 5) {
                        targetId = kvp.Key;
                        break;
                    }
                }
            }
            if (targetId < 1 || targetId > 8) return;

            // 일반 고장과 중복되면 우선권은 일반 고장 (이미 코드가 깨져 있음)
            if (brokenMachines.ContainsKey(targetId) && brokenMachines[targetId]) return;

            TriggerImbalanceBreakdownOn(targetId, consumedPart);
        }
    }

    private string GetTruncatedName(string name, int maxLength = 5) {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Length > maxLength) return name.Substring(0, maxLength) + "...";
        return name;
    }

    private void TriggerImbalanceBreakdownOn(int targetId, string consumedPart) {
        if (!globalCodes.ContainsKey(targetId)) return;
        string srcCode = globalCodes[targetId];
        if (string.IsNullOrEmpty(srcCode)) return;

        // 혹시 이전에 남아 있을 수 있는 헤더 블록 제거 (중첩 방지)
        srcCode = StripImbalanceHeader(srcCode);

        brokenMachines[targetId]      = true;
        backupCodes[targetId]         = srcCode;     // 헤더 제거 후 백업 (랜덤 고장 호환용)
        forbiddenKeywords[targetId]   = consumedPart;
        imbalanceBrokenMachines.Add(targetId);

        // 안내 블록을 시작/끝 마커로 감싸 prepend. X_ERROR_X 치환은 하지 않습니다 —
        // 키워드 차단은 CheckCodeAndApply 의 `\b{banned}\b` 검사가 이미 담당하고,
        // 코드를 깨뜨리지 않아야 회복 시 사용자가 작성한 솔루션을 그대로 보존할 수 있습니다.
        string opposite   = consumedPart == "for" ? "while" : "for";
        string brokenCode =
            $"{ImbalanceHeaderStart}\n"
            + $"# [부품 부족] '{consumedPart}' 을 너무 자주 사용하여 부품이 소진되었습니다!\n"
            + $"# '{opposite}' 부품을 사용하는 코드로 디버깅해 균형을 맞춰주세요.\n"
            + $"{ImbalanceHeaderEnd}\n"
            + srcCode;

        globalCodes[targetId] = brokenCode;

        logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>();
        foreach (var m in allMachines) {
            Iteminfo_Base info = m.GetComponent<Iteminfo_Base>();
            if (info == null || Ingame_System_Save.Instance == null) continue;
            int mId = Ingame_System_Save.Instance.GetMachineTypeInt(
                info.machinePrefab != null ? info.machinePrefab.name : info.machineName);
            if (mId == targetId) {
                m.ValidateCode(brokenCode);
                if (buildManager != null) {
                    string customName = GetMachineCustomName(targetId);
                    buildManager.ShowFloatingText(
                        $"{customName} 부품 부족! '{consumedPart}' 소진 — '{opposite}' 로 균형 회복 필요",
                        m.transform.position);
                }
            }
        }

        // 현재 패널이 해당 기계를 보고 있으면 에디터에도 반영
        if (codingPanel.activeSelf && currentMachineId == targetId) {
            RefreshCodingPanelUI(brokenCode, Color.red, false);
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

    public void OnClick_BreakdownStatusPanel() {
        int targetId = 0;

        // 1. 고장 난 기계 ID 찾기
        foreach (var kvp in brokenMachines) {
            if (kvp.Value == true) {
                targetId = kvp.Key;
                break;
            }
        }

        if (targetId == 0) return;

        // 2. 씬에 있는 모든 기계 찾기
        logic_CodingBase[] allMachines = FindObjectsOfType<logic_CodingBase>(true); 

        foreach (var m in allMachines) {
            // ✨ 핵심: Iteminfo_Base를 찾지 않고, 오브젝트 이름에서 "(Clone)"을 지워서 프리팹 이름을 알아냅니다.
            string mName = m.gameObject.name.Replace("(Clone)", "").Trim();
            
            int mId = 0;
            if (Ingame_System_Save.Instance != null) {
                mId = Ingame_System_Save.Instance.GetMachineTypeInt(mName);
            }
            
            // 3. 찾는 ID와 일치하면 코딩창 띄우기!
            if (mId == targetId) {
                string customName = GetMachineCustomName(targetId);
                OpenFromExternal(targetId, customName, null, null, m);
                return; // 성공했으니 함수 종료
            }
        }
    }

}