using UnityEngine;
using DG.Tweening;

public class Login_Effect_TitleBounce : MonoBehaviour {
    RectTransform rect;

    void Start() {
        rect = GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(0, 700);
        rect.DOAnchorPosY(90f, 1.2f).SetEase(Ease.OutBounce);
    }
}