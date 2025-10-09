using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace MrPathV2
{
    // 自定义编辑器，用于PathProfile资产


    [CustomEditor(typeof(PathProfile))]
    public class PathProfileEditor : Editor
    {
        private ReorderableList _layerList;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 使用 DrawPropertiesExcluding 自动绘制所有未被手动处理的属性，此乃最强“无为而治”之法
            DrawPropertiesExcluding(serializedObject, "m_Script", "layers");

            EditorGUILayout.Space();

            // 懒加载并绘制图层列表
            if (_layerList == null) InitializeLayerList();
            _layerList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();
        }

        private void InitializeLayerList()
        {
            var layersProp = serializedObject.FindProperty("layers");
            _layerList = new ReorderableList(serializedObject, layersProp, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "路径渲染图层 (Layers)"),
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = layersProp.GetArrayElementAtIndex(index);
                    rect.y += 2;
                    EditorGUI.PropertyField(rect, element, true);
                },
                elementHeightCallback = index => EditorGUI.GetPropertyHeight(layersProp.GetArrayElementAtIndex(index), true) + 8
            };
        }
    }
}