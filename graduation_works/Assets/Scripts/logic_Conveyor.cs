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
    public override CodeState ValidateCode(string code) {
        // 1. move() 괄호 안의 모든 내용을 가져옵니다.
        Match match = Regex.Match(code, @"move\(\s*([^)]*)\s*\)");
        
        if (!match.Success) {
            return CodeState.Error; // move() 자체가 없으면 에러!
        }

        // 2. 괄호 안의 내용에서 양옆 공백과 따옴표(", ')를 모두 제거하고 소문자로 변환합니다.
        // 이렇게 하면 move("fast"), move('fast'), move(fast) 모두 똑같이 "fast"로 인식합니다!
        string arg = match.Groups[1].Value.Trim().Replace("\"", "").Replace("'", "").ToLower();

        // 3. 인자가 없거나 "slow"인 경우 (기본 속도)
        if (string.IsNullOrEmpty(arg) || arg == "slow") {
            itemMoveDuration = 2.0f; // 느린 속도
            animSpeed = 0.4f;        
            return CodeState.Valid;
        }

        // 4. "fast"인 경우
        if (arg == "fast") {
            bool isUpgraded = false;
            if (Ingame_Manager_Quest.Instance != null) {
                isUpgraded = Ingame_Manager_Quest.Instance.isConveyorUpgraded; 
            }

            if (!isUpgraded) {
                // 아직 퀘스트를 안 깼다면 빨간불(에러)!
                return CodeState.Error; 
            }

            // 해금되었다면 빠른 속도 적용
            itemMoveDuration = 1.0f; // 빠른 속도
            animSpeed = 0.2f;        
            return CodeState.Valid;
        }

        // 5. "slow", "fast", 빈칸 외에 이상한 값(예: move(123), move("hello"))을 넣은 경우
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