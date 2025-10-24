using System;
using UnityEngine;
using UnityEditor;
using __temp.MrPathV2._2.Runtime.Jobs;
using Unity.Collections;

namespace __temp.MrPathV2._2.Editor.Tests
{
    public class MaskThresholdTest : EditorWindow
    {
        [MenuItem("MrPath/Tests/Mask Threshold Test")]
        public static void ShowWindow()
        {
            GetWindow<MaskThresholdTest>("Mask Threshold Test");
        }

        private void OnGUI()
        {
            GUILayout.Label("Mask Threshold Test", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Test SampleMaskAtlas with Threshold"))
            {
                TestSampleMaskAtlasWithThreshold();
            }
            
            if (GUILayout.Button("Test RecipeData MaskThreshold"))
            {
                TestRecipeDataMaskThreshold();
            }
        }

        private void TestSampleMaskAtlasWithThreshold()
        {
            try
            {
                // Create test data
                var atlas = new NativeArray<float>(16, Allocator.Temp);
                for (int i = 0; i < atlas.Length; i++)
                {
                    atlas[i] = i / 15f; // Values from 0 to 1
                }

                // Test with different thresholds
                float result0 = TerrainJobsUtility.SampleMaskAtlas(atlas, 4, 4, 0, 0.5f, 0.5f, 0f);
                float result1 = TerrainJobsUtility.SampleMaskAtlas(atlas, 4, 4, 0, 0.5f, 0.5f, 0.5f);
                float result2 = TerrainJobsUtility.SampleMaskAtlas(atlas, 4, 4, 0, 0.5f, 0.5f, 0.8f);

                Debug.Log($"SampleMaskAtlas Test Results:");
                Debug.Log($"Threshold 0.0: {result0:F3}");
                Debug.Log($"Threshold 0.5: {result1:F3}");
                Debug.Log($"Threshold 0.8: {result2:F3}");

                // Verify threshold behavior
                if (result1 <= result0 && result2 <= result1)
                {
                    Debug.Log("✓ Threshold behavior is correct (higher threshold = lower result)");
                }
                else
                {
                    Debug.LogWarning("✗ Threshold behavior is incorrect");
                }

                atlas.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"Test failed: {e.Message}");
            }
        }

        private void TestRecipeDataMaskThreshold()
        {
            try
            {
                var recipe = new RecipeData();
                Debug.Log($"Default MaskThreshold: {recipe.MaskThreshold}");

                if (Mathf.Approximately(recipe.MaskThreshold, 0f))
                {
                    Debug.Log("✓ RecipeData MaskThreshold defaults to 0");
                }
                else
                {
                    Debug.LogWarning($"✗ RecipeData MaskThreshold should default to 0, got {recipe.MaskThreshold}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Test failed: {e.Message}");
            }
        }
    }
}