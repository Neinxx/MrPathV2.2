#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Runtime.Components
{
    /// <summary>
    ///     标记字段为必填项的特性，为空时会在Inspector中显示错误提示
    /// </summary>
    public class RequiredFieldAttribute : PropertyAttribute
    {

        /// <summary>
        ///     标记字段为必填项
        /// </summary>
        public RequiredFieldAttribute() { }

        /// <summary>
        ///     标记字段为必填项并指定自定义错误消息
        /// </summary>
        /// <param name="errorMessage">为空时显示的错误消息</param>
        public RequiredFieldAttribute(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        /// <summary>
        ///     标记字段为必填项并指定检查行为
        /// </summary>
        /// <param name="errorMessage">为空时显示的错误消息</param>
        /// <param name="forceCheckInEditMode">是否在编辑模式下强制检查</param>
        public RequiredFieldAttribute(string errorMessage, bool forceCheckInEditMode)
        {
            ErrorMessage = errorMessage;
            ForceCheckInEditMode = forceCheckInEditMode;
        }

        /// <summary>
        ///     自定义错误消息（留空则使用默认消息）
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        ///     是否在编辑模式下强制检查（即使对象未被选中）
        /// </summary>
        public bool ForceCheckInEditMode { get; set; }
    }


    [CustomPropertyDrawer(typeof(RequiredFieldAttribute))]
    public class RequiredFieldDrawer : PropertyDrawer
    {
        // ====================================================================
        // 在这里修改你想要的字体大小和高度
        // ====================================================================

        /// <summary>
        ///     错误框的固定高度
        /// </summary>
        private const float ErrorBoxHeight = 36f; // 你可以改成 30f, 24f 等

        /// <summary>
        ///     错误消息的字体大小
        /// </summary>
        private const int ErrorFontSize = 12; // 你可以改成 11, 13, 14 等
        private const float BorderWidth = 1f;

        // ====================================================================

        // 错误框的背景和边框颜色 (比你原版的更柔和)
        private static readonly Color ErrorBackgroundColor = new Color(0.5f, 0.2f, 0.2f, 0.4f);
        private static readonly Color ErrorBorderColor = new Color(0.8f, 0.3f, 0.3f, 0.8f);

        /// <summary>
        ///     缓存 Unity 的原生错误图标
        /// </summary>
        private GUIContent _errorIconContent;

        /// <summary>
        ///     缓存自定义的 GUI 样式
        /// </summary>
        private GUIStyle _errorTextStyle;


        /// <summary>
        ///     初始化我们的自定义样式
        /// </summary>
        private void InitializeStyles()
        {
            if (_errorTextStyle == null)
            {
                // 基于原生 Label 创建新样式
                _errorTextStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = ErrorFontSize, // <--- 应用自定义字体大小
                    richText = true,
                    alignment = TextAnchor.MiddleLeft,
                    // 调整内边距以适应图标
                    padding = new RectOffset(2, 2, 2, 2)
                };
            }

            if (_errorIconContent == null)
            {
                // 获取 Unity 内置的错误图标
                _errorIconContent = EditorGUIUtility.IconContent("console.erroricon");
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // 确保样式已初始化
            InitializeStyles();

            var requiredAttribute = (RequiredFieldAttribute)attribute;
            var isNull = IsPropertyNull(property);

            if (!isNull)
            {
                // 字段有效，正常绘制
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            // --- 字段为空，开始自定义绘制 ---

            // 1. 定义错误框区域
            var errorRect = new Rect(
                position.x,
                position.y,
                position.width,
                ErrorBoxHeight // <--- 应用自定义高度
            );

            // 2. 绘制背景和边框 (使用你之前的方法)
            DrawErrorBackground(errorRect);

            // 3. 绘制图标
            var iconRect = new Rect(
                errorRect.x + 5f,
                errorRect.y + (errorRect.height - 20f) / 2f, // 垂直居中
                20f,
                20f
            );
            GUI.Label(iconRect, _errorIconContent, GUIStyle.none);

            // 4. 绘制文本
            var labelRect = new Rect(
                iconRect.xMax + 5f, // 图标右侧
                errorRect.y,
                errorRect.width - iconRect.width - 10f, // 减去图标和边距
                errorRect.height
            );

            var errorMessage = GetErrorMessage(requiredAttribute, label);
            // 添加红色富文本 (可选)
            var richErrorMessage = $"<color=#{ColorUtility.ToHtmlStringRGB(Color.white)}><b>!</b> {errorMessage}</color>";

            EditorGUI.LabelField(labelRect, richErrorMessage, _errorTextStyle);

            // 5. 定义属性字段区域 (在错误框下方)
            var propertyRect = new Rect(
                position.x,
                errorRect.yMax + EditorGUIUtility.standardVerticalSpacing,
                position.width,
                EditorGUI.GetPropertyHeight(property, label, true)
            );

            // 6. 绘制属性字段
            using (new EditorGUI.DisabledScope(requiredAttribute.ForceCheckInEditMode))
            {
                EditorGUI.PropertyField(propertyRect, property, label, true);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var baseHeight = EditorGUI.GetPropertyHeight(property, label, true);

            if (IsPropertyNull(property))
            {
                // 返回: 属性高度 + 错误框高度 + 间距
                return baseHeight + ErrorBoxHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            return baseHeight;
        }

        /// <summary>
        ///     帮助方法：获取要显示的错误消息
        /// </summary>
        private string GetErrorMessage(RequiredFieldAttribute attribute, GUIContent label)
        {
            if (!string.IsNullOrEmpty(attribute.ErrorMessage))
            {
                return attribute.ErrorMessage;
            }
            return $"{label.text} 是必填项";
        }

        /// <summary>
        ///     检查属性是否为空（仅检查引用和字符串）
        /// </summary>
        private bool IsPropertyNull(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null;

                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(property.stringValue);

                default:
                    return false;
            }
        }

        /// <summary>
        ///     绘制错误提示的背景色
        ///     (来自你之前的代码，颜色已调整)
        /// </summary>
        private void DrawErrorBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, ErrorBackgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, BorderWidth), ErrorBorderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - BorderWidth, rect.width, BorderWidth), ErrorBorderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, BorderWidth, rect.height), ErrorBorderColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - BorderWidth, rect.y, BorderWidth, rect.height), ErrorBorderColor);
        }

        // 移除了 DrawWarningIcon，因为它被 Unity 原生图标取代了
    }
}
#endif
