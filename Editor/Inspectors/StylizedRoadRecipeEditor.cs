using System;
using System.IO;
using System.Linq;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Core.BlendMasks;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Inspectors
{
    [CustomEditor(typeof(StylizedRoadRecipe))]
    public class StylizedRoadRecipeEditor : OdinEditor
    {
        private static readonly Type[] MaskTypes = FindAvailableMaskTypes();
        private int _lastRecipeHash;

        private StylizedRoadRecipe _recipe;

        private Type _selectedMaskType;
        public Action OnDataChanged;

        protected override void OnEnable()
        {
            base.OnEnable();
            _recipe = target as StylizedRoadRecipe;
            if (_recipe == null) return;

            _lastRecipeHash = ComputeRecipeHash(_recipe);
            Tree.OnPropertyValueChanged += HandlePropertyValueChanged;

            if (MaskTypes.Length > 0)
                _selectedMaskType = MaskTypes[0];
        }

        protected override void OnDisable()
        {
            if (Tree != null)
            {
                Tree.OnPropertyValueChanged -= HandlePropertyValueChanged;
                Tree.Dispose();
            }
            base.OnDisable();
        }

        private void HandlePropertyValueChanged(InspectorProperty _, int __)
        {
            OnDataChanged?.Invoke();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GUILayout.Space(8);

            SirenixEditorGUI.BeginBox();
            {
                GUILayout.Space(2);

                // 使用 Odin 的 Toolbar 布局：自动对齐、无缩进、现代风格
                SirenixEditorGUI.BeginHorizontalToolbar();
                {
                    if (MaskTypes.Length > 0)
                    {
                        var currentIndex = Array.IndexOf(MaskTypes, _selectedMaskType);
                        var typeNames = MaskTypes.Select(GetMaskTypeDisplayName).ToArray();

                        // 使用标准 EditorGUILayout.Popup，但放在 Toolbar 中自动对齐
                        var newIndex = EditorGUILayout.Popup(currentIndex, typeNames, EditorStyles.popup, GUILayout.ExpandWidth(true));
                        if (newIndex != currentIndex)
                            _selectedMaskType = MaskTypes[newIndex];
                    }

                    if (GUILayout.Button("新建遮罩", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        CreateMaskAsset();
                    }
                }
                SirenixEditorGUI.EndHorizontalToolbar();

                GUILayout.Space(2);
            }
            SirenixEditorGUI.EndBox();

            CheckForChanges();
        }

        private void CheckForChanges()
        {
            if (!_recipe) return;
            var currentHash = ComputeRecipeHash(_recipe);
            if (currentHash == _lastRecipeHash) return;

            _lastRecipeHash = currentHash;
            _recipe.RaiseRecipeChanged();
        }

        #region Hash & Change Detection

        private static int ComputeRecipeHash(StylizedRoadRecipe recipe)
        {
            if (!recipe) return 0;

            unchecked
            {
                var hash = 17;
                foreach (var layer in recipe.GetLayers())
                {
                    if (layer == null) continue;

                    hash = hash * 23 + layer.enabled.GetHashCode();
                    hash = hash * 23 + layer.opacity.GetHashCode();
                    hash = hash * 23 + layer.blendMode.GetHashCode();
                    hash = hash * 23 + (layer.contentLayer ? layer.contentLayer.GetInstanceID() : 0);

                    if (layer.layerMask)
                    {
                        var maskJson = JsonUtility.ToJson(layer.layerMask);
                        hash = hash * 23 + maskJson.GetHashCode();
                    }
                }
                return hash;
            }
        }

        #endregion

        #region Mask Creation

        private void CreateMaskAsset()
        {
            if (_selectedMaskType == null) return;
            CreateSpecificMaskAsset(_selectedMaskType, GetMaskTypeDisplayName(_selectedMaskType));
        }

        private void CreateSpecificMaskAsset(Type maskType, string displayName)
        {
            if (maskType == null || !maskType.IsSubclassOf(typeof(BlendMaskBase)))
            {
                ErrorHandler.LogError($"无效的遮罩类型: {maskType?.Name}");
                return;
            }

            var newMask = CreateInstance(maskType);
            var recipePath = AssetDatabase.GetAssetPath(_recipe);
            var folder = Path.GetDirectoryName(recipePath) ?? "Assets";
            var masksFolder = Path.Combine(folder, "Masks");

            if (!AssetDatabase.IsValidFolder(masksFolder))
                AssetDatabase.CreateFolder(folder, "Masks");

            var assetPath = Path.Combine(masksFolder, $"{_recipe.name}_{maskType.Name}.asset").Replace("\\", "/");
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(newMask, uniquePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(newMask);
            ErrorHandler.LogInfo($"MrPath: 已创建新的{displayName}资产: {uniquePath}");
        }

        private static string GetMaskTypeDisplayName(Type type)
        {
            return type switch
            {
                _ when type == typeof(ShoulderMask) => "路肩遮罩",
                _ when type == typeof(RoadSurfaceMask) => "路面遮罩",
                _ when type == typeof(GradientMask) => "渐变遮罩",
                _ when type == typeof(NoiseMask) => "噪声遮罩",
                _ => type.Name
            };
        }

        private static Type[] FindAvailableMaskTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BlendMaskBase)))
                .ToArray();
        }

        #endregion
    }
}
