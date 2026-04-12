using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[RequireComponent(typeof(Button))]
public class UI_ButtonSound : MonoBehaviour
{
    [Header("연결할 사운드 에셋")]
    public AudioClip clickSound;
    
    private AudioSource audioSource;

    void Awake()
    {
        // 1. 사운드 재생을 위한 오디오 소스 설정
        audioSource = gameObject.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        
        audioSource.playOnAwake = false;

        // 2. 씬에 존재하는 '모든' 버튼을 싹 다 긁어모읍니다. (비활성화된 버튼 포함)
        Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();

        foreach (Button btn in allButtons)
        {
            // 프리팹(Assets 폴더에 있는 것)은 제외하고, 실제 씬(Scene)에 있는 것만 골라냅니다.
            if (btn.gameObject.scene.name == null) continue;

            // 버튼이 클릭될 때 소리가 나도록 리스너를 강제로 심어버립니다.
            btn.onClick.AddListener(() => PlayClickSound());
        }
    }

    private void PlayClickSound()
    {
        if (clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
    }
}