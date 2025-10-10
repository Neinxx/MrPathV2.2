// 建议文件名: RequiredFieldDrawer.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// 标记字段为必填项的特性
    /// </summary>
    public class RequiredFieldAttribute : PropertyAttribute { }

    [CustomPropertyDrawer(typeof(RequiredFieldAttribute))]
    public class RequiredFieldDrawer : PropertyDrawer
    {
        private const float ErrorLabelHeight = 18f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // 【笔法精进】先计算好各个区域，再进行绘制
            Rect propertyRect = position;

            if (property.objectReferenceValue == null)
            {
                // 为属性字段区域留出空间
                propertyRect.y += ErrorLabelHeight;
                propertyRect.height -= ErrorLabelHeight;

                // 定义错误标签的区域
                Rect errorRect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

                // 绘制醒目的错误提示
                EditorGUI.HelpBox(errorRect, $"{label.text} is required!", MessageType.Error);
            }

            // 在计算好的区域内，精准地绘制属性字段
            EditorGUI.PropertyField(propertyRect, property, label, true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUI.GetPropertyHeight(property, label, true);
            if (property.objectReferenceValue == null)
            {
                return baseHeight + ErrorLabelHeight;
            }
            return baseHeight;
        }
    }
}
#endif