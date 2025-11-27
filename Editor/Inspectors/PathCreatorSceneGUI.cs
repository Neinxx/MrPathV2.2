using MrPathV2.Editor.Tools;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Settings;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEditorTools = UnityEditor.Tools;

namespace MrPathV2.Editor.Inspectors
{
    /// <summary>
    ///     处理PathCreator的Scene GUI逻辑
    /// </summary>
    public class PathCreatorSceneGUI
    {
        private readonly PathEditorContext _ctx;
        private readonly PathCreator _targetCreator;

        public PathCreatorSceneGUI(PathCreator target, PathEditorContext context)
        {
            _targetCreator = target;
            _ctx = context;
        }

        /// <summary>
        ///     在场景中绘制的调度中心
        /// </summary>
        public void OnSceneGUI()
        {
            // 提前返回：检查上下文是否有效
            if (!ContextIsValid())
            {
                HandleInvalidContext();
                return;
            }

            // 提前返回：检查是否有其他工具处于活动状态
            if (IsOtherToolActive())
            {
                ResetPreviewState();
                return;
            }

            var currentEvent = Event.current;

            // 清理上帧的辅助线
            CleanupPreviewLines();

            // 绘制句柄并处理输入
            ProcessSceneHandlesAndInput(currentEvent);

            // 同步拖拽状态到全局多路径预览
            SynchronizeDragState();

            // 处理场景重绘
            HandleSceneRepainting(currentEvent);
        }

        /// <summary>
        ///     处理无效上下文的情况
        /// </summary>
        private void HandleInvalidContext() { }

        /// <summary>
        ///     根据需要激活预览管理器
        /// </summary>
        /// <summary>
        ///     重置预览状态
        /// </summary>
        private static void ResetPreviewState() { }

        /// <summary>
        ///     同步拖拽状态到全局多路径预览
        /// </summary>
        private void SynchronizeDragState()
        {
            if (_targetCreator) { }
        }

        // --- OnSceneGUI 辅助方法 ---

        private bool ContextIsValid() => _targetCreator && _ctx != null && _ctx.IsPathValid();

        private bool IsOtherToolActive() =>
            // [策略] 和平共存
            ToolManager.activeToolType != typeof(PathCreatorTool) && UnityEditorTools.current != Tool.Move;

        private void CleanupPreviewLines()
        {
            var context = _ctx.CreateHandleContext();
            // 检查LineRenderer是否存在，不存在则直接返回
            if (context.LineRenderer == null)
                return;

            // 获取当前路径策略
            var currentStrategy = PathStrategyRegistry.Instance.GetStrategy(_targetCreator.profile.curveType);
            // 确定需要清除的线条类型
            var lineTypeToClear = currentStrategy.GetLineTypeToCleanup();

            // 清除指定类型的预览线
            context.LineRenderer.Clear(lineTypeToClear);
        }

        private void ProcessSceneHandlesAndInput(Event currentEvent)
        {
            var context = _ctx.CreateHandleContext();

            EditorGUI.BeginChangeCheck();
            PathEditorHandles.Draw(ref context);
            if (EditorGUI.EndChangeCheck())
            {
                // 如果句柄被修改，标记路径为脏以触发更新。
                _ctx.MarkDirty();
            }

            _ctx.UpdateHoverState(context);
            _ctx.InputHandler.HandleInputEvents(currentEvent, _targetCreator, context.HoveredPathT, context.HoveredPointIndex);
        }

        private static void HandleSceneRepainting(Event currentEvent)
        {
            // [性能优化] 仅在必要时重绘
            if (currentEvent.type == EventType.MouseMove || currentEvent.type == EventType.MouseDrag)
            {
                HandleUtility.Repaint();
            }
        }
    }
}
