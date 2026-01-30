using UnityEngine;
using TMPro;
using System.Collections;

public class Ingame_UI_Message : MonoBehaviour {
    public TextMeshProUGUI textMesh;
    
    public float duration = 1.0f;
    public float fadeTime = 1.0f;

    public void Setup(string message, Vector3 spawnPos) {
        if (textMesh == null) textMesh = GetComponent<TextMeshProUGUI>();
        
        textMesh.text = message;
        
        transform.position = new Vector3(spawnPos.x, spawnPos.y, -5f);
        transform.localScale = new Vector3(0.01f, 0.01f, 1f);
        
        Color c = textMesh.color;
        textMesh.color = new Color(c.r, c.g, c.b, 1);

        StartCoroutine(AnimateProcess());
    }

    IEnumerator AnimateProcess() {
        yield return new WaitForSeconds(duration);

        float timer = 0;
        Color startColor = textMesh.color;
        
        while (timer < fadeTime) {
            float progress = timer / fadeTime;
            float alpha = Mathf.Lerp(1, 0, progress);
            textMesh.color = new Color(startColor.r, startColor.g, startColor.b, alpha);

            timer += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }
}