#if UNITY_EDITOR
using System;
using MrPathV2.Runtime.Core;
using MrPathV2.Editor.GPU;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrPathV2.Editor.Inspectors
{
    [CustomEditor(typeof(PathCreator))]
    public class PathCreatorEditor : UnityEditor.Editor
    {

        #region 场景 GUI (OnSceneGUI)

        private void OnSceneGUI()
        {
            _sceneGUI?.OnSceneGUI();
        }

        #endregion

        #region GUI 绘制 (UI Toolkit)

        public override VisualElement CreateInspectorGUI() => _inspectorUI?.CreateInspectorGUI(serializedObject);

        #endregion
        #region 字段

        private PathCreator _targetCreator;

        // --- 序列化属性缓存 ---
        private SerializedProperty _profileProperty;

        // --- 核心上下文 ---
        private PathEditorContext _ctx;

        // --- UI 处理器 ---
        private PathCreatorInspectorUI _inspectorUI;
        private PathCreatorSceneGUI _sceneGUI;

        // --- Profile 事件订阅跟踪 ---
        private PathProfile _lastSubscribedProfile;

        public PathCreatorEditor(PathCreator targetCreator)
        {
            _targetCreator = targetCreator;
        }

        #endregion

        #region 生命周期 (OnEnable / OnDisable)

        private void OnEnable()
        {
            _targetCreator = target as PathCreator;
            if (!_targetCreator) return;

            // 缓存 SerializedProperty
            _profileProperty = serializedObject.FindProperty(nameof(PathCreator.profile));
            serializedObject.FindProperty(nameof(PathCreator.pathData));

            // 初始化上下文
            _ctx = new PathEditorContext(_targetCreator);
            _ctx.Initialize(_targetCreator);

            // 初始化UI处理器
            _inspectorUI = new PathCreatorInspectorUI(this, _targetCreator);
            _sceneGUI = new PathCreatorSceneGUI(_targetCreator, _ctx);

            // 订阅核心事件
            Undo.undoRedoPerformed += OnUndoRedo;
            _targetCreator.CurveDefinitionChanged += OnCurveDefinitionChanged;
            _targetCreator.AppearanceChanged += OnAppearanceChanged;

            // 订阅初始 Profile 的修改事件
            SubscribeToProfile(_targetCreator.profile);

            // 初始刷新延后至 Inspector 构建完成后由 UI 触发，避免与 UI 初始化竞争

            // 预热 GPU 资源与计算着色器，避免首次拖动卡顿
            // 使用 delayCall 避免阻塞 Inspector 初始化
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var _ = GpuTerrainPainterV2.Instance;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PathCreatorEditor] GPU 预热失败: {e.Message}");
                }
            };
        }

        private void OnDisable()
        {
            // 1. 清理UI资源
            _inspectorUI?.OnDestroy();

            // 2. 清理上下文引用
            if (_ctx != null)
            {
                _ctx.Dispose();
                _ctx = null;
            }

            // 3. 取消撤销/重做回调
            try
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"Undo callback removal failed: {e.Message}");
            }

            // 4. 清理目标创建器事件
            if (_targetCreator != null)
            {
                try
                {
                    _targetCreator.CurveDefinitionChanged -= OnCurveDefinitionChanged;
                    _targetCreator.AppearanceChanged -= OnAppearanceChanged;
                }
                catch (NullReferenceException e)
                {
                    Debug.LogError($"Event unsubscription failed: {e.Message}");
                }
            }

            // 5. 取消订阅Profile事件
            try
            {
                UnsubscribeFromLastProfile();
            }
            catch (Exception e)
            {
                Debug.LogError($"Profile cleanup failed: {e}");
            }
        }

        // 优化版：高效销毁编辑器的方法
        public void SafeDestroyEditor(ref UnityEditor.Editor editor)
        {
            // 只保留必要的null检查，移除try-catch以提高性能
            // Unity的DestroyImmediate在传入null时是安全的，不会抛出异常
            if (editor)
            {
                DestroyImmediate(editor);
                editor = null;
            }
        }

        #endregion

        #region 事件处理器

        private void OnCurveDefinitionChanged()
        {
            // 曲线定义变化：只需刷新脊线(隐含网格重建)，避免不必要的材质重建
            _ctx?.RequestSpineRefresh(true);
            _ctx?.RequestSceneViewRefresh(true);
        }

        private void OnAppearanceChanged()
        {
            // 外观参数变化：仅刷新材质，避免脊线/网格重复重算
            _ctx?.RequestMaterialsRefresh(true);
            _ctx?.RequestSceneViewRefresh(true);
        }

        private void OnUndoRedo() => MarkPathAsDirty();

        /// <summary>
        ///     当 Profile 资产本身被修改时调用
        /// </summary>
        private void OnProfileModified()
        {
            if (!_targetCreator.profile) return;

            // 刷新界面
            Repaint();

            // 预览和场景刷新保持不变
            _ctx?.RequestSceneViewRefresh(true);
            MarkPathAsDirty();
        }

        #endregion

        #region 逻辑与辅助方法

        private void MarkPathAsDirty()
        {
            _ctx?.MarkDirty();
        }

        // 新增：防抖版本，避免加载与切换时的即时重计算
        public void MarkPathAsDirtyDebounced()
        {
            _ctx?.MarkDirty(false);
        }

        public void SubscribeToProfile(PathProfile profile)
        {
            if (!profile) return;

            _lastSubscribedProfile = profile;
            _lastSubscribedProfile.ProfileModified += OnProfileModified;
        }

        public void UnsubscribeFromLastProfile()
        {
            if (_lastSubscribedProfile)
            {
                _lastSubscribedProfile.ProfileModified -= OnProfileModified;
                _lastSubscribedProfile = null;
            }
        }

        /// <summary>
        ///     当在Inspector中创建新的Profile时调用
        /// </summary>
        /// <param name="newProfile">新创建的Profile</param>
        public void OnProfileCreated(PathProfile newProfile)
        {
            _profileProperty.objectReferenceValue = newProfile;
            serializedObject.ApplyModifiedProperties();
        }

        #endregion
    }
}
#endif
