using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 路径创建工厂，负责创建配置完整的路径对象
    /// </summary>
    public static class PathFactory
    {
        [MenuItem("GameObject/MrPathV2/Create Path", false, 10)]
        public static void CreateDefaultPath()
        {
            // 获取项目设置
            var projectSettings = MrPathProjectSettings.GetOrCreateSettings();
            if (projectSettings == null)
            {
                Debug.LogError("[PathFactory] 无法获取项目设置，路径创建失败");
                return;
            }

            // 获取创建默认设置
            var creationDefaults = projectSettings.creationDefaults;
            var appearanceDefaults = projectSettings.appearanceDefaults;

            // 确定对象名称
            string objectName = creationDefaults?.defaultObjectName ?? "New MrPath";
            float defaultLength = creationDefaults?.defaultLineLength ?? 10f;

            // 创建游戏对象并注册撤销
            var go = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(go, "Create Path");

            // 添加PathCreator组件
            var creator = go.AddComponent<PathCreator>();

            // 初始化PathData并添加默认的两个节点
            creator.pathData = new PathData();
            
            // 添加起始节点和结束节点，形成一条直线
            Vector3 startPos = Vector3.zero;
            Vector3 endPos = Vector3.forward * defaultLength;
            
            creator.pathData.AddKnot(startPos, Vector3.zero, Vector3.zero);
            creator.pathData.AddKnot(endPos, Vector3.zero, Vector3.zero);

            // 分配默认配置文件
            if (appearanceDefaults?.defaultPathProfile != null)
            {
                creator.profile = appearanceDefaults.defaultPathProfile;
            }
            else
            {
                Debug.LogWarning("[PathFactory] 未找到默认路径配置文件，请在项目设置中配置");
            }

            // 将对象放置在场景视图中心（如果有场景视图的话）
            if (SceneView.lastActiveSceneView != null)
            {
                var sceneView = SceneView.lastActiveSceneView;
                Vector3 spawnPos = sceneView.pivot;
                
                // 如果有地形，尝试将路径放置在地形表面
                var terrain = Terrain.activeTerrain;
                if (terrain != null)
                {
                    float terrainHeight = terrain.SampleHeight(spawnPos);
                    spawnPos.y = terrainHeight;
                }
                
                go.transform.position = spawnPos;
            }

            // 选中新创建的对象
            Selection.activeGameObject = go;
            
            // 确保场景视图聚焦到新对象
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            Debug.Log($"[PathFactory] 成功创建路径: {objectName}");
        }
    }
}