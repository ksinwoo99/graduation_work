using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions; 
using UnityEngine.EventSystems;

public class ingame_CodingManager : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject codingPanel;
    public TextMeshProUGUI titleText;
    public TMP_InputField inputField;
    public Button btnVerify;
    public Image statusLight;
    
    [Header("매니저 연결")]
    public Ingame_BuildManager buildManager; 
    public UnityEngine.Tilemaps.TileBase minerTile; // (혹시 안 쓰더라도 에러 방지용)

    private Dictionary<string, string> globalCodes = new Dictionary<string, string>();
    private string currentMachineName; 

    void Start()
    {
        if(codingPanel != null) codingPanel.SetActive(false);
        if(btnVerify != null) btnVerify.onClick.AddListener(OnClick_Verify);
    }

    // ==========================================
    // 🌍 외부(BuildManager)에서 호출하는 함수
    // ==========================================
    public void OpenFromExternal(string machineName, UnityEngine.Tilemaps.TileBase tile, Image btnImage)
    {
        // 토글: 이미 켜져 있고 같은 기계면 닫기
        if (codingPanel.activeSelf && currentMachineName == machineName)
        {
            OnClick_Close();
            return;
        }

        OpenAndStartBuild(machineName, tile, btnImage);
    }

    // (기존 버튼 호환용 - 없어도 되지만 에러 방지용으로 남김)
    public void OnClick_OpenMiner()
    {
        // 현재 선택된 버튼 찾기
        GameObject clickedObj = EventSystem.current.currentSelectedGameObject;
        Image btnImage = (clickedObj != null) ? clickedObj.GetComponent<Image>() : null;
        
        OpenFromExternal("Miner", minerTile, btnImage);
    }

    void OpenAndStartBuild(string machineName, UnityEngine.Tilemaps.TileBase tile, Image btnImage)
    {
        currentMachineName = machineName;
        codingPanel.SetActive(true);
        if(titleText != null) titleText.text = $"{machineName}.py";

        string savedCode = "";
        if (globalCodes.ContainsKey(machineName)) savedCode = globalCodes[machineName];
        inputField.text = savedCode;

        // BuildManager에게 전달
        if(buildManager != null) buildManager.StartBuildMode(tile, btnImage);

        CheckCodeAndApply(savedCode);
    }

    void OnClick_Verify()
    {
        string code = inputField.text.Trim();
        CheckCodeAndApply(code);
    }

    void CheckCodeAndApply(string code)
    {
        bool isValid = IsCodeValid(code);

        if(buildManager != null) buildManager.SetPlacementPermission(isValid);
        if (statusLight != null) statusLight.color = isValid ? Color.green : Color.red; 

        if (isValid)
        {
            if (globalCodes.ContainsKey(currentMachineName))
                globalCodes[currentMachineName] = code;
            else
                globalCodes.Add(currentMachineName, code);
        }
    }

    bool IsCodeValid(string code)
    {
        if (string.IsNullOrEmpty(code)) return false;
        // mining(숫자) 체크 정규식
        return Regex.IsMatch(code, @"mining\s*\(\s*\d*\s*\)");
    }

    public void OnClick_Close()
    {
        codingPanel.SetActive(false);
        if(buildManager != null) buildManager.CancelBuildMode();
    }

    public void CloseWindowOnly()
    {
        codingPanel.SetActive(false);
    }
}