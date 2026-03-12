using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Shared_Data_Save {
    public float totalPlayTime;
    public int gold;
    public int resCommon;
    public int resRare;
    public int resSpecial;
    public int resExotic;
    public List<BuildingSaveData> buildings = new List<BuildingSaveData>();
}

[System.Serializable]
public class BuildingSaveData {
    public string prefabName;
    public Vector3Int position;
    public int remainingCount;
}