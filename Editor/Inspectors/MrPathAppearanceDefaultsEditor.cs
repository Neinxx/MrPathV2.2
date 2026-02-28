using UnityEditor;
using UnityEngine;

namespace MrPathV2
{
    [CustomEditor(typeof(MrPathAppearanceDefaults))]
    public class MrPathAppearanceDefaultsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 绘制默认 Inspector
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            if (GUILayout.Button("创建并设置默认外观配置"))
            {
                CreateAndAssignDefaultAssets();
            }
        }

        private void CreateAndAssignDefaultAssets()
        {
            var targetObject = (MrPathAppearanceDefaults)target;
            string settingsPath = GetSettingsPath();

            // 创建并设置 defaultPathProfile
            string pathProfilePath = settingsPath + "/AppearanceDefaults/DefaultPathProfile.asset";
            var existingPathProfile = AssetDatabase.LoadAssetAtPath<PathProfile>(pathProfilePath);
            if (existingPathProfile == null)
            {
                EnsureFolderExists(System.IO.Path.GetDirectoryName(pathProfilePath));
                var defaultPathProfile = CreateInstance<PathProfile>();
                AssetDatabase.CreateAsset(defaultPathProfile, pathProfilePath);
                AssetDatabase.SaveAssets();
                Debug.Log("默认路径配置文件已创建: " + pathProfilePath);
                existingPathProfile = defaultPathProfile;
            }
            targetObject.defaultPathProfile = existingPathProfile;

            // 创建并设置 previewMaterialTemplate
            string materialTemplatePath = settingsPath + "/AppearanceDefaults/DefaultPreviewMaterialTemplate.mat";
            var existingMaterialTemplate = AssetDatabase.LoadAssetAtPath<Material>(materialTemplatePath);
            if (existingMaterialTemplate == null)
            {
                EnsureFolderExists(System.IO.Path.GetDirectoryName(materialTemplatePath));
                var defaultMaterialTemplate = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(defaultMaterialTemplate, materialTemplatePath);
                AssetDatabase.SaveAssets();
                Debug.Log("默认预览材质模板已创建: " + materialTemplatePath);
                existingMaterialTemplate = defaultMaterialTemplate;
            }
            targetObject.previewMaterialTemplate = existingMaterialTemplate;

            EditorUtility.SetDirty(targetObject);
        }

        private string GetSettingsPath()
        {
            string[] guids = AssetDatabase.FindAssets("MrPathV2.2");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("MrPathV2.2"))
                {
                    return path + "/Settings";
                }
            }
            Debug.LogError("未找到 MrPathV2.2 文件夹路径！");
            return "Assets/MrPathV2.2/Settings"; // 回退到默认路径
        }

        /// <summary>
        /// 确保指定文件夹存在（递归创建）。
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = System.IO.Path.GetDirectoryName(folderPath);
            string folderName = System.IO.Path.GetFileName(folderPath);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}