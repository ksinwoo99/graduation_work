using UnityEngine;
using System.Collections;
using System.Text.RegularExpressions;

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Conveyor : logic_CodingBase { 
    
    [Header("컨베이어 설정")]
    public float animSpeed = 0.4f; // 기본은 느리게 시작
    public Sprite[] animSprites;   

    [Header("상태 (자동할당)")]
    public BuildDirection myDirection = BuildDirection.Down;
    private SpriteRenderer spriteRenderer;
    public bool isWorking = true; 

    // 실제 아이템 이동에 걸리는 시간
    public float itemMoveDuration = 2.0f; 

    private int currentFrame = 0; 

    protected override void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        base.Awake(); // 부모의 Awake(상태 아이콘 찾기) 실행!
    }

    void Start() {
        if (spriteRenderer != null && animSprites != null && animSprites.Length > 0) {
            spriteRenderer.sprite = animSprites[0];
        }
        StartCoroutine(ConveyorAnimRoutine());
    }

    public override string GetDefaultCode() { 
        return "name = \"컨베이어 벨트\"\nmove()"; 
    }

    // ✨ [핵심 로직] 파이썬 코드를 검사하고 문자에 따라 속도를 적용하는 함수
    // ValidateCode 함수 전체를 덮어씌워 주세요.
    public override CodeState ValidateCode(string code) {
        Match match = Regex.Match(code, @"move\(\s*([^)]*)\s*\)");
        
        if (!match.Success) return CodeState.Error;

        string arg = match.Groups[1].Value.Trim().Replace("\"", "").Replace("'", "").ToLower();

        // ✨ 퀘스트 매니저에서 현재 컨베이어 해금 레벨(0, 1, 2)을 가져옵니다.
        int convLevel = 0;
        if (Ingame_Manager_Quest.Instance != null) {
            convLevel = Ingame_Manager_Quest.Instance.conveyorUpgradeLevel; 
        }

        // 🚨 0단계: 아예 사용 불가!
        if (convLevel == 0) return CodeState.Error_ConveyorLocked;

        // 🟢 1단계: 일반 속도 (slow) 통과
        if (string.IsNullOrEmpty(arg) || arg == "slow") {
            itemMoveDuration = 2.0f; 
            animSpeed = 0.4f;        
            return CodeState.Valid;
        }

        // 🟢 2단계: 빠른 속도 (fast) 검사
        if (arg == "fast") {
            if (convLevel < 2) {
                // 아직 고속 모드가 해금되지 않았을 때!
                return CodeState.Error_ConveyorFastLocked; 
            }

            itemMoveDuration = 1.0f; 
            animSpeed = 0.2f;        
            return CodeState.Valid;
        }

        return CodeState.Error;
    }

    public void SetDirection(int dirIndex) {
        myDirection = (BuildDirection)dirIndex;
        float angle = -dirIndex * 90f;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    public Vector3Int GetPushDirection() {
        if (!isWorking) return Vector3Int.zero; 
        switch (myDirection) {
            case BuildDirection.Down: return new Vector3Int(0, -1, 0);
            case BuildDirection.Left: return new Vector3Int(-1, 0, 0);
            case BuildDirection.Up: return new Vector3Int(0, 1, 0);
            case BuildDirection.Right: return new Vector3Int(1, 0, 0);
            default: return new Vector3Int(0, -1, 0);
        }
    }

    IEnumerator ConveyorAnimRoutine() {
        float timer = 0f;

        while (true) {
            bool isBuildMode = (Ingame_Manager_Build.Instance != null && Ingame_Manager_Build.Instance.isBuildMode);
            bool isPaused = (Ingame_Manager_Time.Instance != null && Ingame_Manager_Time.Instance.isPaused);

            if (!isBuildMode && !isPaused && isWorking && animSprites != null && animSprites.Length > 0) {
                timer += Time.deltaTime;
                if (timer >= animSpeed) {
                    timer = 0f;
                    currentFrame = (currentFrame + 1) % animSprites.Length;
                    if (spriteRenderer != null) spriteRenderer.sprite = animSprites[currentFrame];
                }
            }
            yield return null; 
        }
    }
}