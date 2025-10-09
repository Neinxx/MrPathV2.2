

using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using System.Collections.Generic;
namespace MrPathV2
{

    [EditorTool("Path Editor Tool", typeof(PathCreator))]
    public class PathEditorTool : EditorTool
    {
        #region 状态与护法
        private PreviewMeshController _meshController;
        private IHeightProvider _heightProvider;
        private PreviewMaterialManager _materialManager;
        private PathInputHandler _inputHandler;
        private Material _terrainMatTemplate;

        private int _hoveredPointIdx = -1;
        private int _hoveredSegmentIdx = -1;
        private float _hoveredPathT = -1f;
        private bool _isDraggingHandle;
        private bool _isPathDirty = true;
        private PathSpine? _latestPathSpine;
        private PathCreator _subscribedCreator;

        // 【【【 化虚为实 • 核心法宝 】】】
        private GameObject _previewObject;
        private MeshFilter _previewMeshFilter;
        private MeshRenderer _previewMeshRenderer;
        #endregion

        #region 生命周期 (Lifecycle)
        void OnEnable()
        {
            _meshController = new PreviewMeshController();
            _heightProvider = new TerrainHeightProvider();
            _materialManager = new PreviewMaterialManager();
            _inputHandler = new PathInputHandler();
            _terrainMatTemplate = Resources.Load<Material>("PathPreviewMaterial");

            // 步骤 1: 铸造法宝
            CreatePreviewObject();

            Undo.undoRedoPerformed += MarkPathAsDirty;
            // 确保初次激活时，路径会被刷新
            MarkPathAsDirty();
        }

        void OnDisable()
        {
            _meshController?.Dispose();
            _heightProvider?.Dispose();
            _materialManager?.Dispose();
            Undo.undoRedoPerformed -= MarkPathAsDirty;

            // 统一解绑事件，防止泄漏
            if (_subscribedCreator != null)
            {
                _subscribedCreator.PathChanged -= OnPathDataChanged;
                _subscribedCreator = null;
            }

            // 步骤 2: 销毁法宝
            DestroyPreviewObject();
        }
        #endregion

        #region EditorTool 核心
       public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("Animator icon", "路径编辑器");

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView)) return;
            var creator = target as PathCreator;
            if (creator == null || !IsPathValid(creator))
            {
                // 若路径失效，确保预览对象也被隐藏
                if (_previewObject != null) _previewObject.SetActive(false);
                return;
            }
            else
            {
                if (_previewObject != null) _previewObject.SetActive(true);
            }

            // 仅在目标变化时更新订阅，避免每帧重复绑定/解绑
            if (_subscribedCreator != creator)
            {
                if (_subscribedCreator != null)
                    _subscribedCreator.PathChanged -= OnPathDataChanged;
                if (creator != null)
                    creator.PathChanged += OnPathDataChanged;
                _subscribedCreator = creator;
            }

            if (_isPathDirty)
            {
                _latestPathSpine = PathSampler.SamplePath(creator, _heightProvider);
                _meshController.StartMeshGeneration(_latestPathSpine.Value, creator.profile.layers);
                _isPathDirty = false;
            }

            var context = CreateHandleContext(creator);
            PathEditorHandles.Draw(ref context);
            UpdateHoverStateFromContext(context);
            _inputHandler.HandleInputEvents(Event.current, creator, _hoveredPointIdx, _hoveredPathT);

            // 【【【 御使法宝 • 顺天应时 】】】
            // 检查网格是否已炼制完成
            if (_meshController.TryFinalizeMesh())
            {
                // 若已完成，则更新法宝之骨架与皮囊
                UpdatePreviewObject(creator);
            }

            // 确保材质总是最新的 (例如，当用户在Inspector中调整颜色时)
            UpdatePreviewMaterials(creator);

            sceneView.Repaint();
        }
        #endregion

        #region 法宝操控核心 (Preview Object Management)
        private void CreatePreviewObject()
        {
            if (_previewObject == null)
            {
                _previewObject = new GameObject("Path_Preview_Object");
                // 此乃“无形”之真意，让此物存在，却不在场景中可见，亦不被保存
                _previewObject.hideFlags = HideFlags.HideAndDontSave;
                // 关闭碰撞，仅作显示
                var collider = _previewObject.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                }

                _previewMeshFilter = _previewObject.AddComponent<MeshFilter>();
                _previewMeshRenderer = _previewObject.AddComponent<MeshRenderer>();
            }
        }

        private void DestroyPreviewObject()
        {
            if (_previewObject != null)
            {
                DestroyImmediate(_previewObject);
                _previewObject = null;
            }
        }

        /// <summary>
        /// 当网格数据更新时，更新预览对象的骨架 (Mesh)
        /// </summary>
        private void UpdatePreviewObject(PathCreator creator)
        {
            if (_previewObject == null || _meshController == null) return;
            Mesh previewMesh = _meshController.PreviewMesh;
            if (previewMesh == null) return;

            // 赋予骨架
            _previewMeshFilter.sharedMesh = previewMesh;
        }

        /// <summary>
        /// 每一帧都检查并更新预览对象的皮囊 (Materials)
        /// </summary>
        private void UpdatePreviewMaterials(PathCreator creator)
        {
            if (_previewMeshRenderer == null || creator?.profile == null) return;

            // 更新材质池
            _materialManager.UpdateMaterials(creator.profile, _terrainMatTemplate);
            // 获取最新的材质清单
            List<Material> renderMats = _materialManager.GetFrameRenderMaterials();
            // 赋予皮囊
            _previewMeshRenderer.sharedMaterials = renderMats.ToArray();
        }

        

        #endregion

        #region 辅助方法 (Helpers)
        private bool IsPathValid(PathCreator c) => c.Path != null && c.profile != null;
        private PathEditorHandles.HandleDrawContext CreateHandleContext(PathCreator creator) => new()
        {
            creator = creator,
            heightProvider = _heightProvider,
            latestSpine = _latestPathSpine,
            isDragging = _isDraggingHandle,
            hoveredPointIndex = _hoveredPointIdx,
            hoveredSegmentIndex = _hoveredSegmentIdx,
            hoveredPathT = _hoveredPathT
        };
        private void UpdateHoverStateFromContext(PathEditorHandles.HandleDrawContext context)
        {
            _hoveredPointIdx = context.hoveredPointIndex;
            _hoveredSegmentIdx = context.hoveredSegmentIndex;
            _hoveredPathT = context.hoveredPathT;
            _isDraggingHandle = (context.isDragging || _isDraggingHandle) && GUIUtility.hotControl != 0;
        }
        private void MarkPathAsDirty()
        {
            _isPathDirty = true;
            _latestPathSpine = null;
        }
        private void OnPathDataChanged(object s, PathChangedEventArgs e) => MarkPathAsDirty();
        #endregion
    }
}