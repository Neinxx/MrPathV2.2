using UnityEditor;
using UnityEditorInternal;
using UnityEngine; // 引入ReorderableList所需的命名空间

[CustomEditor (typeof (PathProfile))]
public class PathProfileEditor : Editor
{
    private ReorderableList layerList;

    private void OnEnable ()
    {
        // 1. 找到我们要操作的目标属性："layers"
        var layersProp = serializedObject.FindProperty ("layers");

        // 2. 创建 ReorderableList 实例并进行配置
        layerList = new ReorderableList (serializedObject, layersProp,
            true, true, true, true); // 可拖拽, 显示头部, 显示添加/删除按钮

        // 3. 定义如何绘制列表的头部
        layerList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField (rect, "路径渲染图层 (Path Layers)");
        };

        // 4. 定义如何绘制列表中的每一个元素 (核心)
        layerList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var element = layersProp.GetArrayElementAtIndex (index);

            // 为了美观，给每个元素上下留出一点边距
            rect.y += 2;

            // 使用这一行代码，让Unity为我们自动绘制整个属性，包括所有子属性
            // 第三个参数 'true' 意味着 "includeChildren" (包含子项)
            EditorGUI.PropertyField (
                new Rect (rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                element,
                true
            );
        };

        // 5. 定义如何计算每个元素的高度 (关键)
        layerList.elementHeightCallback = (index) =>
        {
            var element = layersProp.GetArrayElementAtIndex (index);
            // 使用 GetPropertyHeight 来获取该属性在当前状态下（展开或折叠）所需的总高度
            float propertyHeight = EditorGUI.GetPropertyHeight (element, true);

            // 增加一些额外的边距，让列表不那么拥挤
            float spacing = 8f;

            return propertyHeight + spacing;
        };
    }

    public override void OnInspectorGUI ()
    {
        // 更新序列化对象，这是每个自定义Inspector的标配
        serializedObject.Update ();

        // 绘制 "minVertexSpacing" 属性
        EditorGUILayout.PropertyField (serializedObject.FindProperty ("minVertexSpacing"));
        EditorGUILayout.Space ();

        // 只需一行代码，即可绘制出我们精心配置的、功能完备的列表！
        layerList.DoLayoutList ();

        // 应用所有修改
        serializedObject.ApplyModifiedProperties ();
    }
}
