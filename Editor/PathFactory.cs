using UnityEditor;
using UnityEngine;

/// <summary>
/// 路径对象创建工厂类
/// 负责规范化路径对象的创建流程，确保初始化状态一致
/// </summary>
public static class PathFactory
{
    #region 公共接口

    /// <summary>
    /// 创建一个基于默认设置的新路径对象
    /// </summary>
    public static void CreateDefaultPath()
    {
        // 1. 验证核心配置可用性（提前失败原则）
        if (!ValidateSettings(out PathToolSettings settings, out string errorMsg))
        {
            Debug.LogError($"创建路径失败：{errorMsg}");
            return;
        }

        // 2. 创建基础对象与组件
        GameObject pathObject = CreatePathGameObject(settings);
        PathCreator pathCreator = pathObject.AddComponent<PathCreator>();

        // 3. 应用配置初始化
        InitializePathCreator(pathCreator, settings);

        // 4. 定位到场景合适位置
        PlacePathInScene(pathObject);

        // 5. 创建默认路径形状
        CreateDefaultPathSegments(pathCreator, settings);

        // 6. 完成创建流程（Undo+选中）
        FinalizePathCreation(pathObject);
    }

    #endregion

    #region 核心创建流程

    /// <summary>
    /// 验证设置是否有效
    /// </summary>
    private static bool ValidateSettings(out PathToolSettings settings, out string errorMessage)
    {
        settings = PathToolSettings.Instance;

        // 检查设置实例是否存在
        if (settings == null)
        {
            errorMessage = "找不到PathToolSettings实例，请确保已创建工具配置文件";
            return false;
        }

        // 检查默认配置文件是否存在
        if (settings.defaultPathProfile == null)
        {
            errorMessage = "默认路径配置文件(PathProfile)未设置，请在PathToolSettings中配置";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// 创建路径基础游戏对象
    /// </summary>
    private static GameObject CreatePathGameObject(PathToolSettings settings)
    {
        // 使用合理的默认名称（避免空字符串）
        string objectName = string.IsNullOrEmpty(settings.defaultObjectName)
            ? "New Path"
            : settings.defaultObjectName;

        return new GameObject(objectName);
    }

    /// <summary>
    /// 初始化路径创建器组件
    /// </summary>
    private static void InitializePathCreator(PathCreator creator, PathToolSettings settings)
    {
        // 1. 先赋其“魂” (Profile)
        creator.profile = settings.defaultPathProfile;

        // 2. 【人定胜天】显式召唤！
        //    由于我们是通过代码赋值，OnValidate 不会自动触发，
        //    所以我们必须手动调用 EnsurePathExists 来创建 Path 对象。
        creator.EnsurePathImplementationMatchesProfile();
    }

    /// <summary>
    /// 将路径对象放置在场景合适位置
    /// </summary>
    private static void PlacePathInScene(GameObject pathObject)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            // 无场景视图时，放置在世界原点
            pathObject.transform.position = Vector3.zero;
            return;
        }

        // 计算场景视图焦点位置
        Vector3 spawnPos = GetSceneViewFocusPosition(sceneView);

        // 尝试投射到地面
        if (Physics.Raycast(sceneView.camera.transform.position,
                           spawnPos - sceneView.camera.transform.position,
                           out RaycastHit hit,
                           2000f))
        {
            spawnPos = hit.point;
        }

        pathObject.transform.position = spawnPos;
    }

    /// <summary>
    /// 创建默认的路径线段（直线）
    /// </summary>
    // PathFactory.cs
    private static void CreateDefaultPathSegments(PathCreator creator, PathToolSettings settings)
    {
        // OnValidate/Awake 已经确保 Path 对象存在，现在可以安全地添加点了
        if (creator.Path == null)
        {
            Debug.LogError("[PathFactory] Path 对象未能成功初始化，无法添加默认点！");
            return;
        }

        Vector3 lineDirection = GetDefaultLineDirection();
        Vector3 centerPos = creator.transform.position;
        float halfLength = Mathf.Max(0, settings.defaultLineLength) / 2f;
        Vector3 startPoint = centerPos - lineDirection * halfLength;
        Vector3 endPoint = centerPos + lineDirection * halfLength;

        creator.ClearSegments();
        creator.AddSegment(startPoint);
        creator.AddSegment(endPoint);
    }

    /// <summary>
    /// 完成路径创建的收尾工作
    /// </summary>
    private static void FinalizePathCreation(GameObject pathObject)
    {
        Undo.RegisterCreatedObjectUndo(pathObject, $"Create {pathObject.name}");
        Selection.activeGameObject = pathObject;
        EditorGUIUtility.PingObject(pathObject);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 获取场景视图的焦点位置
    /// </summary>
    private static Vector3 GetSceneViewFocusPosition(SceneView sceneView)
    {
        // 场景视图焦点位置可能未初始化，使用摄像机前方位置作为备选
        return sceneView.pivot.sqrMagnitude > 0.01f
            ? sceneView.pivot
            : sceneView.camera.transform.position + sceneView.camera.transform.forward * 10f;
    }

    /// <summary>
    /// 获取默认的线段方向
    /// </summary>
    private static Vector3 GetDefaultLineDirection()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        return sceneView != null
            ? sceneView.camera.transform.right
            : Vector3.right; // 无场景视图时使用世界右方向
    }

    #endregion
}