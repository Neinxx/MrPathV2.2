using UnityEditor;
using UnityEngine;

public static class PathFactory
{
    public static void CreateDefaultPath ()
    {
        // 1. 从设置中加载配置
        PathToolSettings settings = PathToolSettings.Instance;

        // 2. 创建和配置
        GameObject pathObject = new GameObject (settings.defaultObjectName);
        PathCreator creator = pathObject.AddComponent<PathCreator> ();

        // 使用配置的默认曲线类型
        creator.curveType = settings.defaultCurveType;
        if (settings.defaultCurveType == PathCreator.CurveType.Bezier)
        {
            creator.Path = new BezierPath ();
        }
        else if (settings.defaultCurveType == PathCreator.CurveType.CatmullRom)
        {
            creator.Path = new CatmullRomPath ();
        }

        // 3. 放置
        PlaceInSceneView (pathObject);

        // 4. 生成默认形状 (使用配置的长度)
        SceneView sceneView = SceneView.lastActiveSceneView;
        Vector3 lineDirection = sceneView != null ? sceneView.camera.transform.right : Vector3.right;

        Vector3 worldCenter = pathObject.transform.position;
        float defaultLength = settings.defaultLineLength; // 使用配置的长度
        Vector3 pointA = worldCenter - lineDirection * defaultLength / 2;
        Vector3 pointB = worldCenter + lineDirection * defaultLength / 2;

        creator.AddSegment (pointA);
        creator.AddSegment (pointB);

        // 5. 完成 & 选中
        Undo.RegisterCreatedObjectUndo (pathObject, "Create " + settings.defaultObjectName);
        Selection.activeGameObject = pathObject;
        EditorGUIUtility.PingObject (pathObject);
    }

    /// <summary>
    /// 辅助方法：将对象放置在场景视图中心。
    /// </summary>
    private static void PlaceInSceneView (GameObject obj)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null) return;

        Camera sceneCam = sceneView.camera;
        Vector3 spawnPos = sceneView.pivot;

        Ray ray = new Ray (sceneCam.transform.position, spawnPos - sceneCam.transform.position);
        if (Physics.Raycast (ray, out RaycastHit hit, 2000f))
        {
            spawnPos = hit.point;
        }

        obj.transform.position = spawnPos;
    }
}
