// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Factories/PathFactory.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    public static class PathFactory
    {
        [MenuItem("GameObject/MrPath/Create Default Path", false, 10)]
        public static void CreateDefaultPath()
        {
            // 现在从新的配置系统获取设置
            if (!ValidateSettings(out var creationDefaults, out var appearanceDefaults, out string errorMsg))
            {
                Debug.LogError($"创建路径失败：{errorMsg}");
                return;
            }

            GameObject pathObject = CreatePathGameObject(creationDefaults);
            PathCreator pathCreator = pathObject.AddComponent<PathCreator>();

            InitializePathCreator(pathCreator, appearanceDefaults);

            PlacePathInScene(pathObject);
            CreateDefaultPathSegments(pathCreator, creationDefaults);
            FinalizePathCreation(pathObject);
        }

        private static bool ValidateSettings(out MrPathCreationDefaults creationDefaults, out MrPathAppearanceDefaults appearanceDefaults, out string errorMessage)
        {
            var settings = MrPathProjectSettings.GetOrCreateSettings();
            creationDefaults = settings.creationDefaults;
            appearanceDefaults = settings.appearanceDefaults;

            if (creationDefaults == null || appearanceDefaults == null)
            {
                errorMessage = "核心配置资产丢失，请通过 Project Settings -> MrPath 修复。";
                return false;
            }
            if (appearanceDefaults.defaultPathProfile == null)
            {
                errorMessage = "默认路径配置文件(PathProfile)未设置，请在 'MrPath_AppearanceDefaults' 资产中配置。";
                return false;
            }
            errorMessage = string.Empty;
            return true;
        }

        private static GameObject CreatePathGameObject(MrPathCreationDefaults settings)
        {
            string objectName = string.IsNullOrEmpty(settings.defaultObjectName) ? "New Path" : settings.defaultObjectName;
            return new GameObject(objectName);
        }

        private static void InitializePathCreator(PathCreator creator, MrPathAppearanceDefaults settings)
        {
            // 赋其“魂” (Profile)
            creator.profile = settings.defaultPathProfile;
        }

        private static void CreateDefaultPathSegments(PathCreator creator, MrPathCreationDefaults settings)
        {
            Vector3 lineDirection = GetDefaultLineDirection();
            Vector3 centerPos = creator.transform.position;
            float halfLength = Mathf.Max(0, settings.defaultLineLength) / 2f;
            Vector3 startPoint = centerPos - lineDirection * halfLength;
            Vector3 endPoint = centerPos + lineDirection * halfLength;

            var creationCommands = new List<PathChangeCommand>
            {
                new ClearPointsCommand(),
                new AddPointCommand(startPoint),
                new AddPointCommand(endPoint)
            };
            var batchCommand = new BatchCommand(creationCommands);
            creator.ExecuteCommand(batchCommand);
        }

        // --- 以下辅助方法无需修改 ---
        private static void PlacePathInScene(GameObject pathObject)
        {
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

        private static void FinalizePathCreation(GameObject pathObject)
        {
            Undo.RegisterCreatedObjectUndo(pathObject, $"Create {pathObject.name}");
            Selection.activeGameObject = pathObject;
            EditorGUIUtility.PingObject(pathObject);
        }

        private static Vector3 GetSceneViewFocusPosition(SceneView sceneView)
        {
            return sceneView.pivot.sqrMagnitude > 0.01f
                ? sceneView.pivot
                : sceneView.camera.transform.position + sceneView.camera.transform.forward * 10f;
        }

        private static Vector3 GetDefaultLineDirection()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            return sceneView != null
                ? sceneView.camera.transform.right
                : Vector3.right;
        }
    }
}