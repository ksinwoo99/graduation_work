using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// ✨ [데이터 구조 변경] 인스펙터 레시피 세팅이 A/B 타입 방식으로 변경되었습니다!
[System.Serializable]
public class ProductorRecipe {
    [Header("코딩 조건 (예: Common, A)")]
    public string targetTier = "Common";   // Common, Advanced, Hightech, Superior
    public string targetType = "A";        // A 또는 B (코딩과 매칭될 텍스트)
    
    [Header("실제 소모될 자원 및 갯수")]
    public ResourceType resourceType;      // 이 레시피가 먹을 자원
    public int consumeAmount = 100;        // 실제 깎이는 자원량 (밸런스 패치용)
    
    [Header("결과물")]
    public GameObject resultPrefab;        // 생성될 아이템 프리팹
}

// [내부 엔진] 유저 코드 저장용
public class ProcessRule {
    public bool isElse = false;
    public string condRes = "";    
    public string condOp = "";     
    public int condVal = 0;        
    public string actionTier = ""; 
    public string actionType = ""; // ✨ 기존 actionAmount(숫자)가 actionType(A/B)으로 변경!
    public bool hasAction = false; 
}

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Productor_Master : logic_CodingBase
{
    [Header("다중 가공 레시피 설정 (A/B 타입)")]
    public List<ProductorRecipe> multiRecipes = new List<ProductorRecipe>();

    [Header("가공기 기본 설정")]
    public float checkInterval = 1.0f; 
    public float processingTime = 5.0f; 

    [Header("고품질(대박) 아이템 설정")]
    [Range(0f, 100f)] public float highQualityChance = 15.0f; 

    [Header("애니메이션 설정")]
    public Sprite spriteIdle;   
    public Sprite spriteActive; 

    private SpriteRenderer spriteRenderer;
    public int processingCount = 0; 
    private Coroutine processingCoroutine;
    private bool isCurrentlyProcessing = false;
    private ProductorRecipe currentProcessingRecipe = null;

    private List<ProcessRule> parsedRules = new List<ProcessRule>();

    protected override void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (GetComponent<BoxCollider2D>() == null) gameObject.AddComponent<BoxCollider2D>();
        base.Awake(); 
    }

    void Start() {
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        UpdateStatusUI();
    }

    public override void ToggleOperation() {
        if (processingCount == 0) {
            if (Ingame_Manager_Build.Instance != null) {
                Vector3 pos = transform.position; pos.z = -5f;
                Ingame_Manager_Build.Instance.ShowFloatingText("명령어가 없거나 오류가 있습니다.", pos);
            }
            return;
        }

        if (isOperating) {
            isOperating = false;
            isStopping = true;
            UpdateStatusUI(); 
        } else {
            isOperating = true;
            isStopping = false;
            UpdateStatusUI(); 
            InitializeProductor(this.processingCount); 
        }
    }

    public void InitializeProductor(int count) {
        this.processingCount = count;
        if (processingCoroutine != null) {
            StopCoroutine(processingCoroutine);
            
            // ✨ [추가 2] 애니메이션 도중에 코루틴이 끊겼다면 먹었던 자원 환불!
            if (isCurrentlyProcessing && currentProcessingRecipe != null && Ingame_Manager_Resource.Instance != null) {
                Ingame_Manager_Resource.Instance.AddResource(currentProcessingRecipe.resourceType, currentProcessingRecipe.consumeAmount);
                isCurrentlyProcessing = false;
                currentProcessingRecipe = null;
            }
        }
        if (processingCount != 0) processingCoroutine = StartCoroutine(MasterRoutine());
    }

    public override CodeState ValidateCode(string code) {
        parsedRules.Clear();
        string noTags = Regex.Replace(code, "<.*?>", string.Empty);
        string[] lines = noTags.Split('\n', '\r');
        ProcessRule currentRule = null;
        bool codeIsValid = false;

        foreach (string rawLine in lines) {
            string line = rawLine.Replace(" ", "").ToLower();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("if")) {
                Match m = Regex.Match(line, @"if(rescommon|resrare|resspecial|resexotic)(>=|<=|>|<|==)([0-9]+):");
                if (m.Success) {
                    currentRule = new ProcessRule { condRes = m.Groups[1].Value, condOp = m.Groups[2].Value, condVal = int.Parse(m.Groups[3].Value) };
                    parsedRules.Add(currentRule);
                }
            } 
            else if (line.StartsWith("elif")) {
                Match m = Regex.Match(line, @"elif(rescommon|resrare|resspecial|resexotic)(>=|<=|>|<|==)([0-9]+):");
                if (m.Success) {
                    currentRule = new ProcessRule { condRes = m.Groups[1].Value, condOp = m.Groups[2].Value, condVal = int.Parse(m.Groups[3].Value) };
                    parsedRules.Add(currentRule);
                }
            } 
            else if (line.StartsWith("else:")) {
                currentRule = new ProcessRule { isElse = true };
                parsedRules.Add(currentRule);
            } 
            // ✨ [핵심 파서 변경] producting(Common, A) 또는 따옴표가 있는 producting(Common, "A") 형태를 허용합니다!
            else if (line.StartsWith("producting(")) {
                Match m = Regex.Match(line, @"producting\((common|advanced|hightech|superior|rare|special|exotic),['""]?(a|b)['""]?\)");
                if (m.Success) {
                    if (currentRule != null) {
                        currentRule.actionTier = m.Groups[1].Value;
                        currentRule.actionType = m.Groups[2].Value; // a 또는 b 로 저장됨
                        currentRule.hasAction = true;
                        codeIsValid = true;
                    } else {
                        currentRule = new ProcessRule { isElse = true, actionTier = m.Groups[1].Value, actionType = m.Groups[2].Value, hasAction = true };
                        parsedRules.Add(currentRule);
                        codeIsValid = true;
                    }
                }
            }
        }

        if (!codeIsValid) { processingCount = 0; return CodeState.Error; }

        string cleanCodeAll = Regex.Replace(noTags, @"\s+", "").ToLower();
        int loopLevel = 0;
        if (Ingame_Manager_Quest.Instance != null) loopLevel = Ingame_Manager_Quest.Instance.loopUpgradeLevel;

        if (cleanCodeAll.Contains("whiletrue:") || cleanCodeAll.Contains("while(true)") || cleanCodeAll.Contains("loop:")) {
            if (loopLevel < 2) { processingCount = 0; return CodeState.Error_InfiniteLocked; }
            processingCount = -1; return CodeState.Valid;
        }

        Match whileMatch = Regex.Match(cleanCodeAll, @"while.*?([0-9]+).*?:");
        if (whileMatch.Success && !cleanCodeAll.Contains("whiletrue")) {
            if (loopLevel < 1) { processingCount = 0; return CodeState.Error_LoopLocked; }
            int count = int.Parse(whileMatch.Groups[1].Value);
            if (loopLevel == 1 && count > 10) { processingCount = 0; return CodeState.Error_LoopLimit; }
            processingCount = count; return CodeState.Valid;
        }

        if (cleanCodeAll.Contains("for") && cleanCodeAll.Contains("range(")) {
            if (loopLevel < 1) { processingCount = 0; return CodeState.Error_LoopLocked; }
            int start = cleanCodeAll.IndexOf("range(") + 6;
            int end = cleanCodeAll.IndexOf(")", start);
            int count = int.Parse(cleanCodeAll.Substring(start, end - start));
            if (loopLevel == 1 && count > 10) { processingCount = 0; return CodeState.Error_LoopLimit; }
            processingCount = count; return CodeState.Valid;
        }

        processingCount = 1; return CodeState.Valid;
    }

    private int GetResourceAmount(string resName) {
        var resMgr = Ingame_Manager_Resource.Instance;
        if (resMgr == null) return 0;
        switch (resName) {
            case "rescommon": return resMgr.resCommon;
            case "resrare": return resMgr.resRare;
            case "resspecial": return resMgr.resSpecial;
            case "resexotic": return resMgr.resExotic;
            default: return 0;
        }
    }

    IEnumerator MasterRoutine() {
        int currentCount = 0;
        var resMgr = Ingame_Manager_Resource.Instance;

        isOperating = true; 
        isStopping = false;
        UpdateStatusUI(); 

        while (processingCount == -1 || currentCount < processingCount) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

            if (!isBuildMode && !isPaused && resMgr != null) {
                ProcessRule activeRule = null;
                foreach (var rule in parsedRules) {
                    if (!rule.hasAction) continue;
                    if (rule.isElse) { activeRule = rule; break; }
                    int currentAmount = GetResourceAmount(rule.condRes);
                    bool conditionMet = false;
                    switch (rule.condOp) {
                        case ">=": conditionMet = currentAmount >= rule.condVal; break;
                        case "<=": conditionMet = currentAmount <= rule.condVal; break;
                        case ">": conditionMet = currentAmount > rule.condVal; break;
                        case "<": conditionMet = currentAmount < rule.condVal; break;
                        case "==": conditionMet = currentAmount == rule.condVal; break;
                    }
                    if (conditionMet) { activeRule = rule; break; }
                }

                if (activeRule != null) {
                    ProductorRecipe recipe = multiRecipes.Find(r => r.targetTier.ToLower() == activeRule.actionTier && r.targetType.ToLower() == activeRule.actionType);
                    
                    if (recipe != null) {
                        if (resMgr.HasEnoughResource(recipe.resourceType, recipe.consumeAmount)) {
                            resMgr.ConsumeResource(recipe.resourceType, recipe.consumeAmount);
                            
                            isCurrentlyProcessing = true;
                            currentProcessingRecipe = recipe;

                            yield return StartCoroutine(ProcessingAnimationRoutine());

                            isCurrentlyProcessing = false;
                            currentProcessingRecipe = null;

                            float roll = Random.Range(0f, 100f);
                            bool isJackpot = roll <= highQualityChance; 

                            SpawnProduct(recipe.resultPrefab, isJackpot);
                            
                            if (processingCount != -1) currentCount++;
                        } else { yield return new WaitForSeconds(checkInterval); }
                    } else { yield return new WaitForSeconds(checkInterval); }
                } else { yield return new WaitForSeconds(checkInterval); }
                if (isStopping) break; 
            } else { yield return null; }
        }
        
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
        isOperating = false; isStopping = false; UpdateStatusUI(); processingCoroutine = null;
    }

    IEnumerator ProcessingAnimationRoutine() {
        float timer = 0f; float animTimer = 0f; bool isSpriteA = true;
        while (timer < processingTime) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);
            if (!isBuildMode && !isPaused) {
                timer += Time.deltaTime; animTimer += Time.deltaTime;
                if (animTimer >= 0.5f) {
                    animTimer = 0f; isSpriteA = !isSpriteA;
                    if (spriteRenderer != null) spriteRenderer.sprite = isSpriteA ? spriteIdle : spriteActive;
                }
            }
            yield return null;
        }
        if (spriteRenderer != null && spriteIdle != null) spriteRenderer.sprite = spriteIdle;
    }

    void SpawnProduct(GameObject prefabToDrop, bool isHighQuality) {
        if (prefabToDrop == null || Ingame_Manager_Build.Instance == null) return;
        var buildMgr = Ingame_Manager_Build.Instance;
        Vector3Int myCell = buildMgr.tilemapInstallations.WorldToCell(transform.position);
        Vector3 targetDropPos = buildMgr.GetDropPosition(myCell);

        Vector3 spawnPos = transform.position; spawnPos.y -= 0.5f; spawnPos.z = -1f;
        GameObject productObj = Instantiate(prefabToDrop, spawnPos, Quaternion.identity);

        Ingame_Item_Dropped itemScript = productObj.GetComponent<Ingame_Item_Dropped>();
        if (itemScript != null) {
            itemScript.SetDropTarget(targetDropPos); 
            if (isHighQuality) {
                itemScript.SetHighQuality();
            }
        }
    }

    void OnDisable() {
        if (isCurrentlyProcessing && currentProcessingRecipe != null && Ingame_Manager_Resource.Instance != null) {
            Ingame_Manager_Resource.Instance.AddResource(currentProcessingRecipe.resourceType, currentProcessingRecipe.consumeAmount);
            isCurrentlyProcessing = false;
            currentProcessingRecipe = null;
        }
    }
}