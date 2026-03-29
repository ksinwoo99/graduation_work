using UnityEngine;
using DG.Tweening;

public class Login_Effect_Background : MonoBehaviour
{
    [Header("DOTween 배경 이동 설정")]
    public float moveDistance = 50f;  // ↔️ 좌우로 움직일 최대 거리 (픽셀)
    public float duration = 5f;       // ⏱️ 한쪽 끝에서 반대쪽 끝까지 가는 시간 (초)

    private RectTransform rectTransform;
    private Tween moveTween;

    // ✨ [핵심] 씬이 넘어가도 파괴되지 않고 진행 시간을 기억하는 공유(static) 변수입니다.
    public static float savedTime = 0f;

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        
        float targetX = rectTransform.anchoredPosition.x + moveDistance;

        moveTween = rectTransform.DOAnchorPosX(targetX, duration)
            .SetLoops(-1, LoopType.Yoyo) 
            .SetEase(Ease.InOutSine);    

        // ✨ 2. 기억해둔 시간이 있다면, 애니메이션을 처음부터 시작하지 않고 그 시간으로 즉시 타임워프!
        if (savedTime > 0f) {
            moveTween.Goto(savedTime, true);
        }
    }

    void OnDestroy()
    {
        if (moveTween != null && moveTween.IsActive()) {
            // ✨ 1. 씬이 종료될 때(파괴될 때), 현재까지 애니메이션이 진행된 시간을 저장합니다.
            // 무한 루프이므로 1회 왕복 시간(duration * 2)으로 나눈 나머지를 저장하여 완벽한 주기를 맞춥니다.
            savedTime = moveTween.Elapsed(true) % (duration * 2);
            
            moveTween.Kill(); // 메모리 누수 방지를 위해 트윈 종료
        }
    }
}