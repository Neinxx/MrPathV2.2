using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using __temp.MrPathV2.Editor; // UIResourceLoader

namespace __temp.MrPathV2.Editor.UI
{
    public class Testss : EditorWindow
    {
        // 常量定义 - 集中管理路径，便于维护
        private const string MAIN_UXML_PATH = "Assets\\__temp\\MrPathV2\\Editor\\UI\\StylizedRoadRecipe.uxml";
        private const string ROAD_LAYER_UXML_PATH = "Assets\\__temp\\MrPathV2\\Editor\\UI\\RoadLayer.uxml";
        private const string DRAPABLE_ROOT_NAME = "drapableRoot";
        private const string ADD_BUTTON_NAME = "addLayerButton";
        private const string X_BUTTON_NAME = "XButton";

        private ReorderableContainerV2 m_Container;
        private VisualTreeAsset roadLayerUI;
        private List<ReorderableItem> m_Items = new List<ReorderableItem>();

        [MenuItem("Window/My Reorderable Window")]
        public static void ShowWindow()
        {
            GetWindow<Testss>("Reorderable List");
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
        
            // 加载主UI并添加错误处理
            var visualTree = LoadUxmlAsset<VisualTreeAsset>(MAIN_UXML_PATH);
            if (visualTree == null)
            {
                root.Add(new Label($"Error: Could not load UXML at {MAIN_UXML_PATH}"));
                return;
            }

            // 实例化主UI
            VisualElement labelFromUXML = visualTree.Instantiate();
            root.Add(labelFromUXML);

            // 显式附加 Reorderable 样式，确保 V2 的类名样式生效
            var reorderUss = UIResourceLoader.LoadUssByName("ReorderableStyles");
            if (reorderUss != null && !root.styleSheets.Contains(reorderUss))
            {
                root.styleSheets.Add(reorderUss);
            }

            // 加载子UI资源
            roadLayerUI = LoadUxmlAsset<VisualTreeAsset>(ROAD_LAYER_UXML_PATH);

            // 获取UI元素并验证
            var drapableRoot = root.Q<VisualElement>(DRAPABLE_ROOT_NAME);
            var addButton = root.Q<Button>(ADD_BUTTON_NAME);
        
            if (drapableRoot == null)
            {
                Debug.LogError($"Could not find VisualElement with name: {DRAPABLE_ROOT_NAME}");
                return;
            }

            if (addButton == null)
            {
                Debug.LogError($"Could not find Button with name: {ADD_BUTTON_NAME}");
            }

            // 初始化容器
            InitializeContainer(drapableRoot);

            // 注册事件
            if (addButton != null)
            {
                addButton.clicked += OnAddButtonClick;
            }
        }

        /// <summary>
        /// 初始化可重排容器
        /// </summary>
        private void InitializeContainer(VisualElement parent)
        {
            m_Container = new ReorderableContainerV2();
            m_Container.style.flexGrow = 1;
            m_Container.style.flexShrink = 1;
        
            // 添加已存在的项
            foreach (var item in m_Items)
            {
                // 为每个现有项注册删除按钮事件
                RegisterRemoveButtonEvent(item);
                m_Container.Add(item);
            }
        
            parent.Add(m_Container);
        }

        /// <summary>
        /// 添加新项按钮点击事件
        /// </summary>
        private void OnAddButtonClick()
        {
            if (roadLayerUI == null)
            {
                Debug.LogError("Road layer UXML asset is not loaded!");
                return;
            }

            // 创建新项
            var roadLayerItem = roadLayerUI.Instantiate();
            var reorderableItem = new ReorderableItem();
            reorderableItem.Add(roadLayerItem);
        
            // 注册删除按钮事件
            RegisterRemoveButtonEvent(reorderableItem);
        
            // 添加到容器和列表
            m_Container.Add(reorderableItem);
            m_Items.Add(reorderableItem);
        }

        /// <summary>
        /// 为项注册删除按钮事件
        /// </summary>
        private void RegisterRemoveButtonEvent(ReorderableItem item)
        {
            var xButton = item.Q<Button>(X_BUTTON_NAME);
            if (xButton != null)
            {
                // 使用弱引用避免内存泄漏
                xButton.clicked -= () => OnRemoveButtonClick(item);
                xButton.clicked += () => OnRemoveButtonClick(item);
            }
            else
            {
                Debug.LogWarning($"Remove button not found in item: {item.name}");
            }
        }

        /// <summary>
        /// 删除项按钮点击事件
        /// </summary>
        private void OnRemoveButtonClick(ReorderableItem item)
        {
            m_Container.Remove(item);
            m_Items.Remove(item);
            // 清理事件避免内存泄漏
            var xButton = item.Q<Button>(X_BUTTON_NAME);
            if (xButton != null)
            {
                xButton.clicked -= () => OnRemoveButtonClick(item);
            }
        }

        /// <summary>
        /// 加载UXML资源的通用方法
        /// </summary>
        private T LoadUxmlAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogError($"Failed to load asset at path: {path}");
            }
            return asset;
        }

        // 清理事件避免内存泄漏
        private void OnDestroy()
        {
            foreach (var item in m_Items)
            {
                var xButton = item.Q<Button>(X_BUTTON_NAME);
                if (xButton != null)
                {
                    xButton.clicked -= () => OnRemoveButtonClick(item);
                }
            }
        }
    }
}
