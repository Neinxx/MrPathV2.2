using UnityEngine;

namespace __temp.MrPathV2._2.Runtime.Core.BlendMasks
{
    /// <summary>
    /// 路径长度集成测试脚本
    /// 用于验证pathLength参数在整个遮罩系统中的正确传递和使用
    /// </summary>
    public class PathLengthIntegrationTest : MonoBehaviour
    {
        [Header("测试参数")]
        public float testPathLength = 100f;
        public float testWorldWidth = 10f;
        public float testHorizontalPosition = 0f;
        
        [Header("测试遮罩")]
        public NoiseMask noiseMask;
        public PerlinNoiseMask perlinNoiseMask;
        public GradientMask gradientMask;
        
        [Header("测试结果")]
        [SerializeField] private float noiseResult;
        [SerializeField] private float perlinResult;
        [SerializeField] private float gradientResult;
        
        void Start()
        {
            TestMaskEvaluation();
        }
        
        [ContextMenu("测试遮罩评估")]
        public void TestMaskEvaluation()
        {
            Debug.Log("开始路径长度集成测试...");
            
            // 测试NoiseMask
            if (noiseMask != null)
            {
                noiseResult = noiseMask.Evaluate(testHorizontalPosition, testWorldWidth, testPathLength);
                Debug.Log($"NoiseMask 结果: {noiseResult} (pathLength: {testPathLength})");
            }
            
            // 测试PerlinNoiseMask
            if (perlinNoiseMask != null)
            {
                perlinResult = perlinNoiseMask.Evaluate(testHorizontalPosition, testWorldWidth, testPathLength);
                Debug.Log($"PerlinNoiseMask 结果: {perlinResult} (pathLength: {testPathLength})");
            }
            
            // 测试GradientMask
            if (gradientMask != null)
            {
                gradientResult = gradientMask.Evaluate(testHorizontalPosition, testWorldWidth, testPathLength);
                Debug.Log($"GradientMask 结果: {gradientResult} (pathLength: {testPathLength})");
            }
            
            Debug.Log("路径长度集成测试完成！");
        }
        
        [ContextMenu("测试不同路径长度")]
        public void TestDifferentPathLengths()
        {
            if (noiseMask == null) return;
            
            Debug.Log("测试不同路径长度对噪声遮罩的影响:");
            
            float[] testLengths = { 50f, 100f, 200f, 500f };
            
            foreach (float length in testLengths)
            {
                float result = noiseMask.Evaluate(testHorizontalPosition, testWorldWidth, length);
                Debug.Log($"路径长度 {length}m: 噪声值 = {result}");
            }
        }
    }
}