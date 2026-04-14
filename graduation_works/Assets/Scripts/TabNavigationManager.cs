using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class TabNavigationManager : MonoBehaviour
{
    [Tooltip("Tab 순서에 따라 등록된 UI 오브젝트 리스트 (InputField, Button 등)")]
    public List<GameObject> tabSelectables;

    void Update()
    {
        // Tab 키 입력 감지
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            GameObject current = EventSystem.current.currentSelectedGameObject;

            // 현재 포커스된 UI 오브젝트 인덱스 확인
            int currentIndex = tabSelectables.IndexOf(current);

            if (current != null && currentIndex == -1)
            {
                return; 
            }

            // 방향 설정 (Tab: +1 / Shift+Tab: -1)
            int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;

            int nextIndex = 0;

            if (currentIndex != -1)
            {
                nextIndex = (currentIndex + direction + tabSelectables.Count) % tabSelectables.Count;
            }

            // 다음 UI 오브젝트로 포커스 이동
            EventSystem.current.SetSelectedGameObject(tabSelectables[nextIndex]);
        }
    }
}