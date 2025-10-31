namespace __temp.MrPathV2.Runtime.Core.BlendMasks
{
    /// <summary>
    /// 标识噪声算法类型，与 HLSL / Compute 着色器约定保持一致。
    /// 0 = Perlin 噪声
    /// 1 = Simple (快速) 噪声
    /// </summary>
    public enum NoiseAlgorithmId
    {
        Perlin = 0,
        Simple = 1
    }
}