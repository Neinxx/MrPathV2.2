using __temp.MrPathV2._2.Editor.Settings;
using __temp.MrPathV2._2.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2._2.Editor.Inspectors
{
    [CustomEditor(typeof(MrPathAppearanceDefaults))]
    public class MrPathAppearanceDefaultsEditor : UnityEditor.Editor
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
                var defaultMaterialTemplate = new Material(Shader.Find("MrPath/PathPreviewSplatMulti"));
                AssetDatabase.CreateAsset(defaultMaterialTemplate, materialTemplatePath);
                AssetDatabase.SaveAssets();
                Debug.Log("默认预览材质模板已创建(多层预览 Shader): " + materialTemplatePath);
                existingMaterialTemplate = defaultMaterialTemplate;
            }
            targetObject.previewMaterialTemplate = existingMaterialTemplate;

            // 如果已存在的模板不是多层预览 Shader，自动升级以适配新版管线
            var multiShader = Shader.Find("MrPath/PathPreviewSplatMulti");
            if (existingMaterialTemplate != null && multiShader != null && existingMaterialTemplate.shader != multiShader)
            {
                existingMaterialTemplate.shader = multiShader;
                EditorUtility.SetDirty(existingMaterialTemplate);
                Debug.Log("已将现有预览材质模板升级为多层预览 Shader");
            }

            EditorUtility.SetDirty(targetObject);
        }

        private string GetSettingsPath()
        {
            return MrPathProjectSettings.GetSettingsRootFolder();
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