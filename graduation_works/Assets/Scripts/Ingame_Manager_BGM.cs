using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class Ingame_Manager_BGM : MonoBehaviour
{
    public static Ingame_Manager_BGM Instance;

    [Header("BGM 설정")]
    public AudioClip bgmClip;           // 🔥 재생할 BGM 파일 (인스펙터에서 드래그)
    [Range(0f, 1f)] public float defaultVolume = 0.5f; // 기본 볼륨 

    [Header("UI 연결")]
    public Button btnBgmToggle;         // 🔥 On/Off 할 버튼
    public Slider volumeSlider;         // 🔥 볼륨 조절 슬라이더
    
    [Header("버튼 이미지 (옵션)")]
    public Image toggleBtnImage;        // 버튼의 아이콘을 바꾸고 싶다면 연결 (안 해도 무방)
    public Sprite iconBgmOn;
    public Sprite iconBgmOff;

    private AudioSource audioSource;
    private bool isBgmOn = true;

    void Awake()
    {
        // 싱글톤 설정 (필요 시)
        if (Instance == null) Instance = this;
        
        audioSource = GetComponent<AudioSource>();
        
        // 1. 오디오 소스 기본 설정 (반복 재생 등)
        audioSource.loop = true;
        audioSource.playOnAwake = false; 
        audioSource.volume = defaultVolume;

        if (bgmClip != null)
        {
            audioSource.clip = bgmClip;
        }
    }

    void Start()
    {
        // 2. UI 이벤트 연결
        if (btnBgmToggle != null)
        {
            btnBgmToggle.onClick.AddListener(ToggleBGM);
        }

        if (volumeSlider != null)
        {
            volumeSlider.value = defaultVolume;
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        }

        // 3. 인게임 씬에서만 시작할 때 재생
        if (SceneManager.GetActiveScene().name == "Ingame_Scene")
        {
            PlayBGM();
        }
        
        UpdateButtonIcon();
    }

    public void PlayBGM()
    {
        if (audioSource.clip != null && isBgmOn)
        {
            audioSource.Play();
        }
    }

    // ✨ 한 개의 버튼으로 On/Off 조작
    public void ToggleBGM()
    {
        isBgmOn = !isBgmOn;

        if (isBgmOn)
        {
            audioSource.Play();
        }
        else
        {
            audioSource.Pause(); // Stop() 대신 Pause()를 쓰면 껐다 켰을 때 이어서 재생됩니다.
        }

        UpdateButtonIcon();
    }

    // ✨ 볼륨 조절
    public void OnVolumeChanged(float value)
    {
        audioSource.volume = value;
    }

    // (옵션) 켜짐/꺼짐에 따라 버튼 아이콘 변경
    private void UpdateButtonIcon()
    {
        if (toggleBtnImage != null && iconBgmOn != null && iconBgmOff != null)
        {
            toggleBtnImage.sprite = isBgmOn ? iconBgmOn : iconBgmOff;
        }
    }

    // 혹시라도 씬이 넘어갈 때 꺼야 한다면 (OnDestroy 활용)
    void OnDestroy()
    {
        if (btnBgmToggle != null) btnBgmToggle.onClick.RemoveListener(ToggleBGM);
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
    }
}