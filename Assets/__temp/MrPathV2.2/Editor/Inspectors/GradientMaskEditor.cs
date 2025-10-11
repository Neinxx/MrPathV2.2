using UnityEditor;
using UnityEngine;
using MrPathV2;

[CustomEditor(typeof(GradientMask))]
public class GradientMaskEditor : Editor
{
    private Texture2D _preview;
    private const int kWidth = 256;
    private const int kHeight = 32;

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var mask = target as GradientMask;
        if (mask == null) return;

        if (_preview == null)
        {
            _preview = new Texture2D(kWidth, kHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        }

        for (int x = 0; x < kWidth; x++)
        {
            float t = Mathf.Lerp(-1f, 1f, x / (float)(kWidth - 1));
            float v = Mathf.Clamp01(mask.Evaluate(t));
            var c = new Color(v, v, v, 1f);
            for (int y = 0; y < kHeight; y++) _preview.SetPixel(x, y, c);
        }
        _preview.Apply(false);

        GUILayout.Label("一维预览", EditorStyles.boldLabel);
        GUILayout.Box(_preview, GUILayout.Width(kWidth), GUILayout.Height(kHeight));
    }
}