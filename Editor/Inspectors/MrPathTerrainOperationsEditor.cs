using System.Collections.Generic;
using System.IO;
using System.Linq;
using __temp.MrPathV2.Editor.Operations;
using __temp.MrPathV2.Editor.Settings;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Inspectors
{
    [CustomEditor(typeof(MrPathTerrainOperations))]
    public class MrPathTerrainOperationsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // 绘制默认 Inspector
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            if (GUILayout.Button("创建默认地形操作资产并设置"))
            {
                CreateAndAssignDefaultAsset();
            }
        }

        private void CreateAndAssignDefaultAsset()
        {
            var targetObject = (MrPathTerrainOperations)target;
            var settingsPath = GetSettingsPath();
            var path = settingsPath + "/TerrainOperations/DefaultTerrainOperation.asset";

            // 检查是否已经有资产
            if (targetObject == null)
            {
                Debug.LogError("目标对象未初始化！");
                return;
            }

            var existingAsset = AssetDatabase.LoadAssetAtPath<MrPathTerrainOperations>(path);
            if (existingAsset == null)
            {
                EnsureFolderExists(Path.GetDirectoryName(path));
                existingAsset = CreateInstance<MrPathTerrainOperations>();
                AssetDatabase.CreateAsset(existingAsset, path);
                Debug.Log("默认地形操作资产已创建: " + path);
            }

            // 收集所有 PathTerrainOperation 资产并填充到列表
            // 先尝试通过反射查找所有派生自 PathTerrainOperation 的具体类型（非抽象）
            var concreteTypes = TypeCache.GetTypesDerivedFrom<PathTerrainOperation>()
                .Where(t => t is { IsAbstract: false, IsClass: true }).ToList();

            // 用于存放最终结果的列表
            var foundOpsList = new List<PathTerrainOperation>();

            // 目标文件夹：将所有操作资产集中放在 TerrainOperations 子文件夹下
            var opsFolder = settingsPath + "/TerrainOperations/Operations";
            EnsureFolderExists(opsFolder);

            foreach (var type in concreteTypes)
            {
                // 先尝试查找已经存在的资产
                var guids = AssetDatabase.FindAssets($"t:{type.Name}");
                PathTerrainOperation opAsset = null;
                if (guids.Length > 0)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    opAsset = AssetDatabase.LoadAssetAtPath(assetPath, type) as PathTerrainOperation;
                }

                // 如果仍未找到，则创建一个新的资产
                if (opAsset == null)
                {
                    opAsset = CreateInstance(type) as PathTerrainOperation;
                    var assetPath = $"{opsFolder}/{type.Name}.asset";
                    AssetDatabase.CreateAsset(opAsset, assetPath);
                    Debug.Log($"已创建缺失的 PathTerrainOperation 资产: {assetPath}");
                }

                if (opAsset != null) foundOpsList.Add(opAsset);
            }

            var foundOps = foundOpsList.OrderBy(op => op.order).ToArray();

            if (foundOps.Length == 0)
            {
                Debug.LogWarning("未找到任何 PathTerrainOperation 资产，无法填充默认地形操作。");
            }
            existingAsset.operations = foundOps;
            EditorUtility.SetDirty(existingAsset);
            AssetDatabase.SaveAssets();

            // 将填充后的 operations 赋值给当前目标对象
            targetObject.operations = existingAsset.operations;
            EditorUtility.SetDirty(targetObject);
        }

        private string GetSettingsPath() => MrPathProjectSettings.GetSettingsRootFolder();

        /// <summary>
        ///     确保指定文件夹存在（递归创建）。
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parent = Path.GetDirectoryName(folderPath);
            var folderName = Path.GetFileName(folderPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
