namespace __temp.MrPathV2.Runtime.Memory
{
    /// <summary>
    ///     静态访问点，便于在代码中快速访问统一的内存管理器。
    /// </summary>
    public static class UnifiedMemory
    {
        /// <summary>
        ///     全局单例实例。
        /// </summary>
        public static UnifiedMemoryManager Instance => UnifiedMemoryManager.Instance;
    }
}
