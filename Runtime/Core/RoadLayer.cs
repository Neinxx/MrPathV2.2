using System;
using __temp.MrPathV2.Runtime.Core.BlendMasks;
using Sirenix.OdinInspector;
using UnityEngine;
// Editor 相关仅在编辑器下编译
#if UNITY_EDITOR
using UnityEditor;
#endif
// 确保 using 正确
// ... (其他 using) ...

namespace __temp.MrPathV2.Runtime.Core
{
    [Serializable]
    public class RoadLayer
    {
        [HideInInspector]
        public string name = "Layer";

        [HideInInspector]
        public bool enabled = true;

        private const int imageSize = 22;
        // --- 统一的盒子开始了 ---

        /// <summary>
        ///     1. 这是统一盒子的第一个属性。
        ///     - [BoxGroup("LayerContent")] 将它放入一个盒子里。
        ///     - [ShowLabel = false] 隐藏盒子的标题。
        ///     - [HorizontalGroup] 嵌套在 BoxGroup 内部，用于放置混合模式。
        /// </summary>
        [BoxGroup("LayerContent", ShowLabel = false)]
        [HorizontalGroup("LayerContent/BlendSettings", Width = 0.3f)] // 30% 宽度
        [HideLabel]
        public BlendMode blendMode = BlendMode.Normal;

        /// <summary>
        ///     2. 这是同一行 ("BlendSettings") 的第二个属性。
        ///     - 它自动被包含在 "LayerContent" 盒子中，因为它的 HorizontalGroup
        ///     嵌套在 "LayerContent" 之下。
        /// </summary>
        [HorizontalGroup("LayerContent/BlendSettings", Width = 0.7f)] // 70% 宽度
        [LabelText("")]
        [LabelWidth(50)]
        [Range(0f, 1f)]
        public float opacity = 1f;

        /// <summary>
        ///     3. Content Layer 属性。
        ///     - 我们再次使用 [BoxGroup("LayerContent")] 来告诉 Odin
        ///     “继续在刚才那个盒子里绘制”。
        ///     - [Space(5)] 在 "Opacity" 和 "Content Layer" 之间添加了间距。
        /// </summary>
        [BoxGroup("LayerContent")]
        [Space(5)]
        [HideInInspector]
        public TerrainLayer contentLayer;

#if UNITY_EDITOR
        // 现代化的 Content 行布局：左侧缩略图，中间只读名称，右侧定位/清空/选择
        [BoxGroup("LayerContent")]
        [HorizontalGroup("LayerContent/ContentRow", Width = 0.2f)]
        [HideLabel]
        [ShowInInspector]
        [PreviewField(imageSize, ObjectFieldAlignment.Left)]
        [PropertyOrder(0)]
        private Texture2D ContentLayerPreview
        {
            get
            {
                if (!contentLayer) return null;
                var tex = contentLayer.diffuseTexture;
                var preview = tex ? (Texture2D)(AssetPreview.GetAssetPreview(tex) ?? AssetPreview.GetMiniThumbnail(tex))
                                  : (Texture2D)(AssetPreview.GetAssetPreview(contentLayer) ?? AssetPreview.GetMiniThumbnail(contentLayer));
                return preview;
            }
        }

        // 使缩略图可点击：点击直接打开选择窗口
        [BoxGroup("LayerContent")]
        [PropertyOrder(1)]
        [OnInspectorGUI]
        private void MakePreviewClickable()
        {
            var rect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                OpenLayerPicker();
                Event.current.Use();
            }
            // 也提供一个透明按钮覆盖，保证可点击性
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                OpenLayerPicker();
            }
        }

        [HorizontalGroup("LayerContent/ContentRow", Width = 0.8f)]
        [LabelText("")]
        [ShowInInspector]
        [DisplayAsString]
        [PropertyOrder(2)]
        private string ContentLayerDisplay => contentLayer ? contentLayer.name : "未选择";
#endif

        /// <summary>
        ///     4. Layer Mask 属性。
        ///     - 同样，使用 [BoxGroup("LayerContent")] 将其保留在同一个盒子中。
        /// </summary>
        [BoxGroup("LayerContent")]
        [LabelText("Layer Mask")]
        [AssetsOnly]
        [HideInInspector]
        public BlendMaskBase layerMask;

        // --- 统一的盒子结束了 ---

#if UNITY_EDITOR
        // Mask 行：左侧图标 + 右侧只读名称；整行可点击打开 LayerMaskSelectWindow
        [BoxGroup("LayerContent")]
        [HorizontalGroup("LayerContent/MaskRow", Width = 0.2f)]
        [HideLabel]
        [ShowInInspector]
        [PreviewField(imageSize, ObjectFieldAlignment.Left)]
        [PropertyOrder(3)]
        private Texture2D LayerMaskIcon
        {
            get
            {
                if (!layerMask) return null;
                return AssetPreview.GetMiniThumbnail(layerMask) as Texture2D;
            }
        }

        [BoxGroup("LayerContent")]
        [PropertyOrder(4)]
        [OnInspectorGUI]
        private void MakeMaskRowClickable()
        {
            var rect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                OpenLayerMaskSelect();
                Event.current.Use();
            }
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                OpenLayerMaskSelect();
            }
        }

        [HorizontalGroup("LayerContent/MaskRow", Width = 0.8f)]
        [LabelText("")]
        [ShowInInspector]
        [DisplayAsString]
        [PropertyOrder(5)]
        private string LayerMaskDisplay => layerMask ? GetNameSuffix(layerMask.name, imageSize) : "未选择";

        // 超长名称仅显示后缀，避免布局挤占；例如显示 …VeryLongSuffix
        private static string GetNameSuffix(string name, int maxSuffixLen = 16)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (name.Length <= maxSuffixLen) return name;
            var start = Mathf.Max(0, name.Length - maxSuffixLen);
            return "…" + name.Substring(start);
        }

        private void OpenLayerPicker()
        {
            // 通过反射调用 Editor 窗口，避免 runtime 对 Editor 程序集的编译期依赖
            // 优先尝试新的选择窗口
            var type = System.Type.GetType("MrPathV2.Editor.Windows.SelectTerrainLayerWindow, Assembly-CSharp-Editor");
            if (type == null)
            {
                // 回退到旧类型（如果存在）
                type = System.Type.GetType("MrPathV2.Editor.Windows.TerrainLayerPickerWindow, Assembly-CSharp-Editor");
                if (type == null)
                {
                    Debug.LogWarning("SelectTerrainLayerWindow/TerrainLayerPickerWindow 类型均未找到 (Assembly-CSharp-Editor)。");
                    return; // 早退
                }
            }
            var method = type.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                Debug.LogWarning("选择窗口 Open 方法未找到。");
                return; // 早退
            }
            try
            {
                // 尝试获取当前选中的PathCreator作为上下文
                PathCreator contextPathCreator = null;
                var selectionType = System.Type.GetType("UnityEditor.Selection, UnityEditor");
                if (selectionType != null)
                {
                    var activeGameObjectProperty = selectionType.GetProperty("activeGameObject");
                    if (activeGameObjectProperty != null)
                    {
                        var activeGameObject = activeGameObjectProperty.GetValue(null) as UnityEngine.GameObject;
                        if (activeGameObject != null)
                        {
                            contextPathCreator = activeGameObject.GetComponent<PathCreator>();
                        }
                    }
                }

                method.Invoke(null, new object[] { this, contentLayer, contextPathCreator });
            }
            catch (Exception e)
            {
                Debug.LogError($"打开 TerrainLayerPickerWindow 失败: {e.Message}");
            }
        }

        private void PingContentLayer()
        {
            if (!contentLayer) return;
            EditorGUIUtility.PingObject(contentLayer);
        }

        private void ClearContentLayer()
        {
            contentLayer = null;
        }

        private void OpenLayerMaskSelect()
        {
            var type = System.Type.GetType("MrPathV2.Editor.Windows.LayerMaskSelectWindow, Assembly-CSharp-Editor");
            if (type == null)
            {
                Debug.LogWarning("LayerMaskSelectWindow 类型未找到 (Assembly-CSharp-Editor)。");
                return; // 早退
            }
            var method = type.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                Debug.LogWarning("LayerMaskSelectWindow.Open 方法未找到。");
                return; // 早退
            }
            try
            {
                method.Invoke(null, new object[] { this, layerMask });
            }
            catch (Exception e)
            {
                Debug.LogError($"打开 LayerMaskSelectWindow 失败: {e.Message}");
            }
        }
#endif

        /// <summary>
        ///     创建新图层时的默认值 (保持不变)
        /// </summary>
        public static RoadLayer CreateDefault(int index) => new RoadLayer
        {
            name = $"Layer {index}",
            enabled = true,
            opacity = 1f,
            blendMode = BlendMode.Normal,
            contentLayer = null,
            layerMask = null
        };
    }
}
