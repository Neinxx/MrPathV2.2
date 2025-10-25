using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace MrPathV2._2.Runtime.Core
{
    [CreateAssetMenu(fileName = "StylizedRoadRecipe", menuName = "MrPath/Stylized Road Recipe")]
    public class StylizedRoadRecipe : ScriptableObject
    {
        // =============== Header ===============
        [HorizontalGroup("Header")]
        [LabelText("Master Opacity")]
        [LabelWidth(90)]
        [Range(0f, 1f)]
        [Tooltip("整体配方透明度，可统一控制所有图层的可见度")]
        public float masterOpacity = 1f;

        // =============== Layers ===============
        [Space(16)]
        [Title("Layers Blending Settings")]
        [InfoBox("<size=12><size=20>✨</size> 支持无限层数：系统会自动处理多Control贴图分配和内存优化。</size>")]
        [ListDrawerSettings(
            DraggableItems = true,
            ShowFoldout = true,
            ShowItemCount = true,
            DefaultExpandedState = true,
            CustomAddFunction = nameof(AddNewLayer),
            NumberOfItemsPerPage = 10,
            ListElementLabelName = "name",
            OnBeginListElementGUI = nameof(BeginListElement),
            OnEndListElementGUI = nameof(EndListElement)
        )]
        public List<RoadLayer> layers = new List<RoadLayer>();

        public int ActiveLayerCount => layers.Count(l => l?.enabled == true);

        private void OnValidate()
        {
            layers ??= new List<RoadLayer>();
            RaiseRecipeChanged();
        }

        // =============== Events & Utilities ===============
        public event Action RecipeChanged;

        public void RaiseRecipeChanged()
        {
            RecipeChanged?.Invoke();
        }

        public IReadOnlyList<RoadLayer> GetLayers() => layers;

        // =============== Editor-only Fields ===============
#if UNITY_EDITOR
        [HideInInspector] public float width = 5f;

        private RoadLayer AddNewLayer()
        {
            var newLayer = RoadLayer.CreateDefault(layers.Count + 1);
            RaiseRecipeChanged(); // 统一调用事件
            return newLayer;
        }

        private void BeginListElement(int index)
        {
            if (index < 0 || index >= layers.Count) return;

            var boxRect = SirenixEditorGUI.BeginBox();
            var element = layers[index];

            // 动态计算 toggle 位置：避免硬编码
            const float toggleSize = 18f;
            const float paddingFromRight = 42f; // 给删除按钮留出空间
            var toggleRect = new Rect(
                boxRect.xMax - paddingFromRight,
                boxRect.yMin + 2f,
                toggleSize,
                toggleSize
            );

            var newEnabled = GUI.Toggle(toggleRect, element.enabled, GUIContent.none);
            if (newEnabled != element.enabled)
            {
                Undo.RecordObject(this, "Toggle RoadLayer Enabled");
                element.enabled = newEnabled;
                EditorUtility.SetDirty(this);
                RaiseRecipeChanged();
            }
        }

        private void EndListElement(int index)
        {
            SirenixEditorGUI.EndBox();
        }
#endif
    }
}
