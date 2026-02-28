using NUnit.Framework;
using Unity.Collections;
using MrPathV2.Memory;

namespace MrPathV2.Tests
{
    /// <summary>
    /// 单元测试：验证 UnifiedMemoryManager 的分配、释放与集中清理逻辑。
    /// </summary>
    public class UnifiedMemoryManagerTests
    {
        [Test]
        public void RentNativeArray_Dispose_OwnerReleasesMemory()
        {
            var manager = UnifiedMemory.Instance;
            var owner = manager.RentNativeArray<int>(32, Allocator.Persistent);
            Assert.IsTrue(owner.Collection.IsCreated, "分配后的 NativeArray 应该已创建");

            // 在 Dispose 之前记录长度用于后续断言
            int lengthBeforeDispose = owner.Collection.Length;

            owner.Dispose();
            Assert.IsFalse(owner.Collection.IsCreated, "调用 Dispose 后 NativeArray 应被释放");
            Assert.AreEqual(0, owner.Collection.Length, "Dispose 后长度应为 0 以表明已释放");
        }

        [Test]
        public void ForceCleanup_ReleasesAllTrackedOwners()
        {
            var manager = UnifiedMemory.Instance;
            // 创建两个 Owner，但只手动释放其中一个
            var owner1 = manager.RentNativeArray<float>(16, Allocator.Persistent);
            var owner2 = manager.RentNativeArray<float>(8, Allocator.Persistent);

            owner1.Dispose();
            Assert.IsFalse(owner1.Collection.IsCreated);
            Assert.IsTrue(owner2.Collection.IsCreated);

            manager.ForceCleanup();
            Assert.IsFalse(owner2.Collection.IsCreated, "ForceCleanup 应释放未显式 Dispose 的资源");
        }

        [Test]
        public void AllocationStats_ShouldIncreaseAndResetWithForceCleanup()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var manager = UnifiedMemory.Instance;
            manager.AllocationStats.TryGetValue("NativeArray<int>", out int before);
            var owner = manager.RentNativeArray<int>(4, Allocator.Persistent);
            manager.AllocationStats.TryGetValue("NativeArray<int>", out int afterAlloc);
            
            Assert.AreEqual(before + 1, afterAlloc, "分配后统计应增加");

            manager.ForceCleanup();
            manager.AllocationStats.TryGetValue("NativeArray<int>", out int afterCleanup);
            Assert.AreEqual(afterAlloc, afterCleanup, "ForceCleanup 不会改变已分配计数，但资源应被释放");
            Assert.IsFalse(owner.Collection.IsCreated);
#endif
        }
    }
}