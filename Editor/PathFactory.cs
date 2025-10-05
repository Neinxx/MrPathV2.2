using UnityEditor;
using UnityEngine;

/// <summary>
/// 【重排版】一个静态工厂类，负责创建和初始化新的路径对象。
/// </summary>
public static class PathFactory
{
    #region 公共接口 (Public API)

    /// <summary>
    /// 根据 PathToolSettings 中的默认设置，创建一个新的路径对象。
    /// </summary>
    public static void CreateDefaultPath ()
    {
        // 1. 从设置资产中加载配置
        PathToolSettings settings = PathToolSettings.Instance;

        // 2. 创建和配置 GameObject 与核心组件
        GameObject pathObject = new GameObject (settings.defaultObjectName);
        PathCreator creator = pathObject.AddComponent<PathCreator> ();

        // -- 应用默认设置 --
        creator.curveType = settings.defaultCurveType;

        // --- 【点睛之笔】 ---
        // 在创建后，立刻赋予其默认的灵魂(Profile)，确保其立即可见
        creator.profile = settings.defaultPathProfile;
        // --------------------

        // (确保Path实例被创建)
        // creator.EnsurePathExists(); // PathCreator的Awake或OnValidate会处理，但显式调用更保险

        // 3. 放置到场景视图的焦点位置
        PlaceInSceneView (pathObject);

        // 4. 生成默认的路径形状 (一条直线)
        SceneView sceneView = SceneView.lastActiveSceneView;
        Vector3 lineDirection = sceneView != null ? sceneView.camera.transform.right : Vector3.right;

        Vector3 worldCenter = pathObject.transform.position;
        float defaultLength = settings.defaultLineLength;
        Vector3 pointA = worldCenter - lineDirection * defaultLength / 2;
        Vector3 pointB = worldCenter + lineDirection * defaultLength / 2;

        creator.AddSegment (pointA);
        creator.AddSegment (pointB);

        // 5. 注册Undo，并将其设为当前选中对象
        Undo.RegisterCreatedObjectUndo (pathObject, "Create " + settings.defaultObjectName);
        Selection.activeGameObject = pathObject;
        EditorGUIUtility.PingObject (pathObject);
    }

    #endregion

    #region 内部辅助方法 (Internal Helpers)

    /// <summary>
    /// 辅助方法：将一个GameObject放置在场景视图的中心，并尝试投射到下方的物体表面。
    /// </summary>
    private static void PlaceInSceneView (GameObject obj)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null) return;

        // 将对象放置在场景视图摄像机当前注视的焦点上
        Vector3 spawnPos = sceneView.pivot;

        // 从摄像机向焦点发射一条射线，尝试找到一个表面
        Ray ray = new Ray (sceneView.camera.transform.position, spawnPos - sceneView.camera.transform.position);
        if (Physics.Raycast (ray, out RaycastHit hit, 2000f))
        {
            spawnPos = hit.point;
        }

        obj.transform.position = spawnPos;
    }

    #endregion
}
