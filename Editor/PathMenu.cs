using UnityEditor;
namespace MrPathV2
{
    /// <summary>
    /// V1.8 (Factory Pattern):
    /// 创建逻辑已完全移至PathFactory。本类只负责调用工厂，实现完全解耦。
    /// </summary>
    public static class PathMenu
    {
        [MenuItem("GameObject/MrPath/Create Path", false, 10)]
        public static void CreatePathObject()
        {
            // 只需向工厂发出一个简单的请求
            PathFactory.CreateDefaultPath();
        }
    }
}