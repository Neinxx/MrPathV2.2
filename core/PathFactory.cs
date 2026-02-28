using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【最终适配版】路径对象创建工厂类
/// 
/// (大师赞许：创建流程清晰，验证逻辑前置，场景定位智能，
/// 这是一个非常专业的编辑器工具类。适配新架构只需一行关键改动。)
/// </summary>
public static class PathFactory
{
    #region 公共接口

    [MenuItem("GameObject/MrPath/Create Default Path", false, 10)]
    public static void CreateDefaultPath()
    {
        if (!ValidateSettings(out PathToolSettings settings, out string errorMsg))
        {
            Debug.LogError($"创建路径失败：{errorMsg}");
            return;
        }

        GameObject pathObject = CreatePathGameObject(settings);
        PathCreator pathCreator = pathObject.AddComponent<PathCreator>();

        // 关键的初始化流程现在变得更简单了
        InitializePathCreator(pathCreator, settings);

        PlacePathInScene(pathObject);
        CreateDefaultPathSegments(pathCreator, settings);
        FinalizePathCreation(pathObject);
    }

    #endregion

    #region 核心创建流程

    private static bool ValidateSettings(out PathToolSettings settings, out string errorMessage)
    {
        settings = PathToolSettings.Instance;
        if (settings == null)
        {
            errorMessage = "找不到PathToolSettings实例，请确保已创建工具配置文件";
            return false;
        }
        if (settings.defaultPathProfile == null)
        {
            errorMessage = "默认路径配置文件(PathProfile)未设置，请在PathToolSettings中配置";
            return false;
        }
        errorMessage = string.Empty;
        return true;
    }

    private static GameObject CreatePathGameObject(PathToolSettings settings)
    {
        string objectName = string.IsNullOrEmpty(settings.defaultObjectName) ? "New Path" : settings.defaultObjectName;
        return new GameObject(objectName);
    }

    private static void InitializePathCreator(PathCreator creator, PathToolSettings settings)
    {
        // 1. 先赋其“魂” (Profile)
        creator.profile = settings.defaultPathProfile;

        // --- 【【【 核心修改：无为而治 】】】 ---
        // 2. 删除了 creator.EnsurePathImplementationMatchesProfile(); 这一行！
        // 在我们新的架构中，PathCreator 的设计已大道至简。它不再需要一个额外的“初始化”命令。
        // 只要为它赋予了 profile，它就自然而然地知道了该去注册中心查询哪个法则。
        // 它永远处于一个有效的、可工作的状态。
    }

    private static void PlacePathInScene(GameObject pathObject)
    {
        // ... 此方法无需任何修改 ...
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            pathObject.transform.position = Vector3.zero;
            return;
        }
        Vector3 spawnPos = GetSceneViewFocusPosition(sceneView);
        if (Physics.Raycast(sceneView.camera.transform.position, spawnPos - sceneView.camera.transform.position, out RaycastHit hit, 2000f))
        {
            spawnPos = hit.point;
        }
        pathObject.transform.position = spawnPos;
    }

    private static void CreateDefaultPathSegments(PathCreator creator, PathToolSettings settings)
    {
        // (大师批注：在我们的新设计中，creator.pathData 在 PathCreator 被创建时就已经
        // new PathData()，所以它永远不为null。这个检查可以移除，以示对新架构的信心。)
        // if (creator.pathData == null) { ... }

        Vector3 lineDirection = GetDefaultLineDirection();
        Vector3 centerPos = creator.transform.position;
        float halfLength = Mathf.Max(0, settings.defaultLineLength) / 2f;
        Vector3 startPoint = centerPos - lineDirection * halfLength;
        Vector3 endPoint = centerPos + lineDirection * halfLength;
        PathData pathData = creator.pathData;

        var creationCommands = new List<PathChangeCommand>
        {
            new ClearPointsCommand(),
            new AddPointCommand(startPoint),
            new AddPointCommand(endPoint)
        };
        var batchCommand = new BatchCommand(creationCommands);

        // 2. 向执行者下达这道唯一的、完整的创世敕令
        creator.ExecuteCommand(batchCommand);
    }

    private static void FinalizePathCreation(GameObject pathObject)
    {
        // ... 此方法无需任何修改 ...
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