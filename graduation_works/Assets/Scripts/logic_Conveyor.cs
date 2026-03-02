using UnityEngine;
using System.Collections;

[RequireComponent(typeof(SpriteRenderer))]
public class logic_Conveyor : logic_CodingBase { 
    
    [Header("컨베이어 설정")]
    public float animSpeed = 0.2f; // 속도를 살짝 빠르게 
    // 🔥 [수정] A, B 두 개 대신 여러 이미지를 넣을 수 있는 배열로 변경!
    public Sprite[] animSprites;   

    [Header("상태 (자동할당)")]
    public BuildDirection myDirection = BuildDirection.Down;
    private SpriteRenderer spriteRenderer;
    public bool isWorking = true; 

    private int currentFrame = 0; // 현재 재생 중인 이미지 번호

    void Awake() {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void Start() {
        if (spriteRenderer != null && animSprites != null && animSprites.Length > 0) {
            spriteRenderer.sprite = animSprites[0];
        }
        StartCoroutine(ConveyorAnimRoutine());
    }

    public override CodeState ValidateCode(string code) {
        return CodeState.Valid; 
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

            // 배열에 이미지가 제대로 들어있을 때만 무한 반복 재생
            if (!isBuildMode && !isPaused && isWorking && animSprites != null && animSprites.Length > 0) {
                timer += Time.deltaTime;
                if (timer >= animSpeed) {
                    timer = 0f;
                    currentFrame = (currentFrame + 1) % animSprites.Length;
                    
                    if (spriteRenderer != null) {
                        spriteRenderer.sprite = animSprites[currentFrame];
                    }
                }
            }
            yield return null; 
        }
    }
}