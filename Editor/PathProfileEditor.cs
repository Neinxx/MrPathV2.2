using UnityEditor;
using UnityEngine;

/// <summary>
/// 为PathProfile ScriptableObject 提供一个强大的自定义编辑器界面。
/// </summary>
[CustomEditor (typeof (PathProfile))]
public class PathProfileEditor : Editor
{
    public override void OnInspectorGUI ()
    {
        serializedObject.Update ();

        EditorGUILayout.PropertyField (serializedObject.FindProperty ("minVertexSpacing"));

        EditorGUILayout.Space ();
        EditorGUILayout.LabelField ("路径分段", EditorStyles.boldLabel);

        // 绘制分段列表
        SerializedProperty segmentsProp = serializedObject.FindProperty ("segments");
        for (int i = 0; i < segmentsProp.arraySize; i++)
        {
            SerializedProperty segmentProp = segmentsProp.GetArrayElementAtIndex (i);
            DrawSegment (segmentProp);
        }

        // 添加/删除分段的按钮
        if (GUILayout.Button ("添加分段"))
        {
            segmentsProp.arraySize++;
        }
        if (segmentsProp.arraySize > 0 && GUILayout.Button ("删除最后一个分段"))
        {
            segmentsProp.arraySize--;
        }

        serializedObject.ApplyModifiedProperties ();
    }

    private void DrawSegment (SerializedProperty segmentProp)
    {
        EditorGUILayout.BeginVertical ("box");

        EditorGUILayout.PropertyField (segmentProp.FindPropertyRelative ("name"));
        EditorGUILayout.PropertyField (segmentProp.FindPropertyRelative ("outputMode"));

        EditorGUILayout.PropertyField (segmentProp.FindPropertyRelative ("width"));
        EditorGUILayout.PropertyField (segmentProp.FindPropertyRelative ("horizontalOffset"));
        EditorGUILayout.PropertyField (segmentProp.FindPropertyRelative ("verticalOffset"));

        SegmentOutputMode mode = (SegmentOutputMode) segmentProp.FindPropertyRelative ("outputMode").enumValueIndex;

        if (mode == SegmentOutputMode.StandaloneMesh)
        {
            EditorGUILayout.PropertyField (segmentProp.FindPropertyRelative ("standaloneMeshMaterial"));
        }
        else // 地形绘制模式
        {
            DrawLayerBlendRecipe (segmentProp.FindPropertyRelative ("terrainPaintingRecipe"));
        }

        EditorGUILayout.EndVertical ();
        EditorGUILayout.Space ();
    }

    private void DrawLayerBlendRecipe (SerializedProperty recipeProp)
    {
        EditorGUILayout.LabelField ("图层混合配方", EditorStyles.boldLabel);

        SerializedProperty layersProp = recipeProp.FindPropertyRelative ("blendLayers");
        for (int i = 0; i < layersProp.arraySize; i++)
        {
            SerializedProperty layerProp = layersProp.GetArrayElementAtIndex (i);
            EditorGUILayout.BeginVertical ("box");
            EditorGUILayout.PropertyField (layerProp.FindPropertyRelative ("terrainLayer"));
            DrawBlendMask (layerProp.FindPropertyRelative ("blendMask"));
            EditorGUILayout.EndVertical ();
        }

        if (GUILayout.Button ("添加混合图层"))
        {
            layersProp.arraySize++;
        }
        if (layersProp.arraySize > 0 && GUILayout.Button ("删除最后一个混合图层"))
        {
            layersProp.arraySize--;
        }
    }

    private void DrawBlendMask (SerializedProperty maskProp)
    {
        SerializedProperty maskTypeProp = maskProp.FindPropertyRelative ("maskType");
        EditorGUILayout.PropertyField (maskTypeProp, new GUIContent ("混合遮罩类型"));

        BlendMaskType maskType = (BlendMaskType) maskTypeProp.enumValueIndex;

        switch (maskType)
        {
            case BlendMaskType.ProceduralNoise:
                EditorGUILayout.PropertyField (maskProp.FindPropertyRelative ("noiseScale"));
                EditorGUILayout.PropertyField (maskProp.FindPropertyRelative ("noiseStrength"));
                break;
            case BlendMaskType.PositionalGradient:
                EditorGUILayout.PropertyField (maskProp.FindPropertyRelative ("gradient"));
                break;
            case BlendMaskType.CustomTexture:
                EditorGUILayout.PropertyField (maskProp.FindPropertyRelative ("customTexture"));
                break;
        }
    }
}
