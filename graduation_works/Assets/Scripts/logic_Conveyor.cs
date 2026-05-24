using UnityEngine;
using System.Collections;
using System.Text.RegularExpressions;

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Conveyor : logic_CodingBase { 
    
    [Header("컨베이어 설정")]
    public float animSpeed = 0.4f; 
    public Sprite[] animSprites;   

    [Header("상태 (자동할당)")]
    public BuildDirection myDirection = BuildDirection.Down;
    private SpriteRenderer spriteRenderer;
    
    // ✨ [수정] 처음 지었을 때는 코드가 없으니 일단 정지 상태(false)로 둡니다!
    public bool isWorking = false; 

    public float itemMoveDuration = 2.0f; 

    private int currentFrame = 0; 

    protected override void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (GetComponent<BoxCollider2D>() == null) gameObject.AddComponent<BoxCollider2D>();
        
        base.Awake();
    }

    void Start() {
        if (spriteRenderer != null && animSprites != null && animSprites.Length > 0) {
            spriteRenderer.sprite = animSprites[0];
        }
        StartCoroutine(ConveyorAnimRoutine());
    }

    // ✨ [핵심 1] 컨베이어를 지었을 때 기본적으로 코딩 창이 비어있게 만듭니다.
    public override string GetDefaultCode() {
        return ""; 
    }

    // ✨ [핵심 2] 코드가 없으면 멈추고, moving()이 확인되면 즉시 가동합니다!
    public override CodeState ValidateCode(string code) {
        string noTags = Regex.Replace(code, "<.*?>", string.Empty);
        string cleanCode = Regex.Replace(noTags, @"\s+", "").ToLower();

        if (string.IsNullOrEmpty(cleanCode)) {
            isWorking = false; // 코드가 지워지면 멈춤
            return CodeState.Empty;
        }

        if (cleanCode.Contains("moving()")) {
            isWorking = true; // 정상 코드면 작동 시작!
            ApplyCurrentSpeed(); 
            return CodeState.Valid;
        }

        isWorking = false; // 오타가 나면 멈춤
        return CodeState.Error;
    }

    // ✨ [핵심 3] 글로벌 업그레이드 레벨에 따라 3단계 속도를 자동으로 세팅해줍니다.
    public void ApplyCurrentSpeed() {
        int level = 0;
        if (Ingame_Manager_Quest.Instance != null) {
            level = Ingame_Manager_Quest.Instance.conveyorUpgradeLevel;
        }

        if (level == 0) { // 0단계 (Slow)
            itemMoveDuration = 2.0f;
            animSpeed = 0.4f;
        } else if (level == 1) { // 1단계 (Normal)
            itemMoveDuration = 1.0f;
            animSpeed = 0.2f;
        } else { // 2단계 (Fast)
            itemMoveDuration = 0.5f;
            animSpeed = 0.1f;
        }
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

            // ✨ 실시간으로 업그레이드 버튼의 상태를 체크해서 속도를 갱신합니다.
            ApplyCurrentSpeed();

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