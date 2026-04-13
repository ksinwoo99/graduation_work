using UnityEngine;
using UnityEngine.EventSystems;

public class UI_Tutorial_ClickCopy : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (Ingame_UI_Tutorial.Instance == null) return;

        if (eventData.clickCount == 2)
        {
            Ingame_UI_Tutorial.Instance.HandleTutorialCodeAction(false);
        }
        else if (eventData.clickCount == 1)
        {
            Ingame_UI_Tutorial.Instance.HandleTutorialCodeAction(true);
        }
    }
}