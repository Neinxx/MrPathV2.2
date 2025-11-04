#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Editor.Settings;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Interfaces;
using __temp.MrPathV2.Runtime.Providers;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Preview
{
    /// <summary>
    /// Global multi-path preview renderer.
    /// Renders preview meshes for all PathCreator instances concurrently in SceneView.
    /// Each PathCreator uses its own PathProfile and materials.
    /// </summary>
    [InitializeOnLoad]
    public static class MultiPathPreviewRenderer
    {
        // 控制全局多路径预览是否启用，避免与单对象编辑器预览重复绘制
        private static bool IsEnabled => true;


        // 当前正在编辑（拖拽句柄）的对象 ID；拖拽中由编辑器设置

        private static readonly Dictionary<int, PathPreviewManager> Managers = new Dictionary<int, PathPreviewManager>();
        private static readonly Dictionary<int, TransformSnapshot> LastTransforms = new Dictionary<int, TransformSnapshot>();
        private static IHeightProvider s_MHeightProvider;
        private static Material s_MTemplate;
        private static bool s_MInitialized;
        private static List<PathCreator> s_MCachedCreators = new List<PathCreator>();
        private static bool s_MCreatorsDirty = true;

        private struct TransformSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        static MultiPathPreviewRenderer()
        {
            EditorApplication.delayCall += EnsureInitialized;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.hierarchyChanged += () =>
            {
                s_MCreatorsDirty = true;
            };
        }

        private static void EnsureInitialized()
        {
            if (s_MInitialized) return;
            try
            {
                s_MHeightProvider ??= new TerrainHeightProvider();

                var settings = MrPathProjectSettings.GetOrCreateSettings();
                var appearance = settings?.appearanceDefaults;
                s_MTemplate = appearance?.previewMaterialTemplate;

                // Ensure multi-layer shader template exists
                var multiShader = Shader.Find("MrPath/PathPreviewSplatMulti");
                if (multiShader)
                {
                    if (!s_MTemplate || !s_MTemplate.shader || !s_MTemplate.shader.name.Contains("PathPreviewSplatMulti"))
                    {
                        s_MTemplate = new Material(multiShader)
                        {
                            name = "DefaultPreviewMaterialTemplate"
                        };
                    }
                }

                s_MInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiPathPreviewRenderer] Initialization failed: {ex.Message}");
            }
        }

        // 确保并返回某 PathCreator 的全局预览管理器
        private static PathPreviewManager EnsureManager(int id)
        {
            if (Managers.TryGetValue(id, out var mgr) && mgr != null) return mgr;
            var generator = new DefaultPreviewGenerator();
            var matMgr = new PreviewMaterialManager();
            mgr = new PathPreviewManager(generator, matMgr, s_MTemplate, alpha: 1f);
            Managers[id] = mgr;
            return mgr;
        }

        // 供编辑器调用：在全局渲染模式下标记指定 PathCreator 的预览为脏
        public static void MarkCreatorDirty(PathCreator creator, bool spine = true, bool mesh = true, bool materials = true)
        {
            if (!creator) return;
            if (!s_MInitialized) EnsureInitialized();
            var id = creator.GetInstanceID();
            var mgr = EnsureManager(id);
            if (spine) mgr.MarkSpineDirty();
            if (mesh) mgr.MarkMeshDirty();
            if (materials) mgr.MarkMaterialsDirty();
            try
            {
                SceneView.RepaintAll();
            }
            catch
            { /* ignore */
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                CleanupAll();
            }
        }

        private static IEnumerable<PathCreator> GetCreators()
        {
            if (!s_MCreatorsDirty && s_MCachedCreators != null) return s_MCachedCreators ?? Array.Empty<PathCreator>().ToList();
            try
            {
                s_MCachedCreators = new List<PathCreator>(UnityEngine.Object.FindObjectsOfType<PathCreator>());
            }
            catch
            { /* ignore */
            }
            s_MCreatorsDirty = false;
            return s_MCachedCreators ?? Array.Empty<PathCreator>().ToList();
        }

        private static void OnSceneGUI(SceneView sv)
        {
            if (!IsEnabled) return;
            if (!s_MInitialized) EnsureInitialized();

            DetectFallbackActiveId();
            var creators = GetCreators();
            var alive = new HashSet<int>();

            ProcessAllCreators(creators, alive);
            CleanupRemovedCreators(alive);
        }

        private static void DetectFallbackActiveId()
        {
            try
            {
                var selectedGo = Selection.activeGameObject;
                var selectedCreator = selectedGo ? selectedGo.GetComponent<PathCreator>() : null;
                if (selectedCreator && GUIUtility.hotControl != 0)
                {
                    selectedCreator.GetInstanceID();
                }
            }
            catch
            {
                /* ignore */
            }
        }

        private static void ProcessAllCreators(IEnumerable<PathCreator> creators, HashSet<int> alive)
        {
            foreach (var creator in creators)
            {
                ProcessSingleCreator(creator, alive);
            }
        }

        private static void ProcessSingleCreator(PathCreator creator, HashSet<int> alive)
        {
            if (!ShouldProcessCreator(creator)) return;

            var id = creator.GetInstanceID();
            alive.Add(id);

            var mgr = EnsureManager(id);

            if (ShouldSkipCreatorRendering())
            {
                DisableManagerForEditing(mgr);
                return;
            }

            HandleTransformChanges(creator, mgr);
            UpdateManagerPreview(creator, mgr);
        }

        private static bool ShouldProcessCreator(PathCreator creator)
        {
            if (!creator || !creator.profile) return false;
            if (!creator.profile.showPreviewMesh) return false;
            return true;
        }

        private static bool ShouldSkipCreatorRendering()
        {
            // 不再在编辑或变换期间跳过渲染，始终保持可见
            return false;
        }

        private static void DisableManagerForEditing(PathPreviewManager mgr)
        {
            try
            {
                mgr.SetActive(false);
            }
            catch
            {
                /* ignore */
            }
        }

        private static void HandleTransformChanges(PathCreator creator, PathPreviewManager mgr)
        {
            var tr = creator.transform;
            if (!tr) return;

            var id = creator.GetInstanceID();
            var changed = true;

            if (LastTransforms.TryGetValue(id, out var snap))
            {
                changed = snap.Position != tr.position ||
                          snap.Rotation != tr.rotation ||
                          snap.Scale != tr.localScale;
            }

            if (changed)
            {
                LastTransforms[id] = new TransformSnapshot
                {
                    Position = tr.position,
                    Rotation = tr.rotation,
                    Scale = tr.localScale
                };
                mgr.MarkSpineDirty();
            }
        }

        private static void UpdateManagerPreview(PathCreator creator, PathPreviewManager mgr)
        {
            try
            {
                mgr.SetActive(true);
                mgr.Update(creator, s_MHeightProvider);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiPathPreviewRenderer] Update error for {creator.name}: {ex.Message}");
            }
        }

        private static void CleanupRemovedCreators(HashSet<int> alive)
        {
            var toRemove = new List<int>();

            foreach (var kv in Managers.Where(kv => !alive.Contains(kv.Key)))
            {
                try
                {
                    kv.Value?.Dispose();
                }
                catch
                {
                    /* ignore */
                }
                toRemove.Add(kv.Key);
            }

            foreach (var id in toRemove)
            {
                Managers.Remove(id);
                LastTransforms.Remove(id);
            }
        }


        private static void CleanupAll()
        {
            foreach (var kv in Managers)
            {
                try
                {
                    kv.Value?.Dispose();
                }
                catch
                { /* ignore */
                }
            }
            Managers.Clear();
            LastTransforms.Clear();
            s_MCachedCreators?.Clear();
            s_MCreatorsDirty = true;
        }
    }
}
#endif
