using MrPathV2.Runtime.Core;
using UnityEngine;

namespace MrPathV2.Tests
{
    /// <summary>
    ///     简单测试脚本，验证简化后的Recipe SO设计是否正常工作
    /// </summary>
    public class SimpleRecipeTest : MonoBehaviour
    {
        [Header("Test PathCreators")]
        public PathCreator pathCreator1;
        public PathCreator pathCreator2;

        [Header("Test Recipe SOs")]
        public StylizedRoadRecipe recipe1;
        public StylizedRoadRecipe recipe2;

        [Header("Test Controls")]
        public bool testDifferentRecipes;
        public bool testSameRecipe;

        private void Start()
        {
            // 确保有PathCreator实例
            if (pathCreator1 == null)
            {
                var go1 = new GameObject("PathCreator1");
                pathCreator1 = go1.AddComponent<PathCreator>();
            }

            if (pathCreator2 == null)
            {
                var go2 = new GameObject("PathCreator2");
                pathCreator2 = go2.AddComponent<PathCreator>();
            }

            Debug.Log("[SimpleRecipeTest] 测试环境初始化完成");
        }

        private void Update()
        {
            if (testDifferentRecipes)
            {
                testDifferentRecipes = false;
                TestDifferentRecipes();
            }

            if (testSameRecipe)
            {
                testSameRecipe = false;
                TestSameRecipe();
            }
        }

        /// <summary>
        ///     测试两个PathCreator使用不同的Recipe SO
        /// </summary>
        private void TestDifferentRecipes()
        {
            if (!recipe1 || !recipe2)
            {
                Debug.LogError("[SimpleRecipeTest] 请在Inspector中分配两个不同的Recipe SO");
                return;
            }

            // 设置不同的Recipe
            pathCreator1.profile.roadRecipe = recipe1;
            pathCreator2.profile.roadRecipe = recipe2;

            Debug.Log($"[SimpleRecipeTest] PathCreator1 使用 Recipe: {recipe1.name}");
            Debug.Log($"[SimpleRecipeTest] PathCreator2 使用 Recipe: {recipe2.name}");

            // 验证Recipe引用
            VerifyRecipeAssignment();
        }

        /// <summary>
        ///     测试两个PathCreator使用相同的Recipe SO
        /// </summary>
        private void TestSameRecipe()
        {
            if (recipe1 == null)
            {
                Debug.LogError("[SimpleRecipeTest] 请在Inspector中分配Recipe SO");
                return;
            }

            // 设置相同的Recipe
            pathCreator1.profile.roadRecipe = recipe1;
            pathCreator2.profile.roadRecipe = recipe1;

            Debug.Log($"[SimpleRecipeTest] 两个PathCreator都使用相同的Recipe: {recipe1.name}");

            // 验证Recipe引用
            VerifyRecipeAssignment();
        }

        /// <summary>
        ///     验证Recipe分配是否正确
        /// </summary>
        private void VerifyRecipeAssignment()
        {
            var recipe1Ref = pathCreator1.profile.roadRecipe;
            var recipe2Ref = pathCreator2.profile.roadRecipe;

            Debug.Log($"[SimpleRecipeTest] PathCreator1.profile.roadRecipe = {(recipe1Ref ? recipe1Ref.name : "null")}");
            Debug.Log($"[SimpleRecipeTest] PathCreator2.profile.roadRecipe = {(recipe2Ref ? recipe2Ref.name : "null")}");

            // 验证Recipe是否为ScriptableObject
            if (recipe1Ref)
            {
                Debug.Log($"[SimpleRecipeTest] Recipe1 是 ScriptableObject: {recipe1Ref is ScriptableObject}");
            }

            if (recipe2Ref)
            {
                Debug.Log($"[SimpleRecipeTest] Recipe2 是 ScriptableObject: {recipe2Ref is ScriptableObject}");
            }

            Debug.Log("[SimpleRecipeTest] Recipe分配验证完成");
        }

        /// <summary>
        ///     运行时切换Recipe测试
        /// </summary>
        [ContextMenu("Runtime Switch Test")]
        public void RuntimeSwitchTest()
        {
            if (recipe1 == null || recipe2 == null)
            {
                Debug.LogError("[SimpleRecipeTest] 需要两个不同的Recipe SO进行切换测试");
                return;
            }

            // 交换Recipe
            (pathCreator1.profile.roadRecipe, pathCreator2.profile.roadRecipe) = (pathCreator2.profile.roadRecipe, pathCreator1.profile.roadRecipe);

            Debug.Log("[SimpleRecipeTest] 运行时Recipe切换完成");
            VerifyRecipeAssignment();
        }
    }
}
