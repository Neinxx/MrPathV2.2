// 命名空间桥接：为旧代码提供 __temp.MrPathV2.Editor.Windows 的类型别名
#if UNITY_EDITOR
namespace __temp.MrPathV2.Editor.Windows
{
    // 将新命名空间中的窗口类型映射到旧命名空间，消除编译错误
    using SelectTerrainLayerWindow = global::MrPathV2.Editor.Windows.SelectTerrainLayerWindow;
    using LayerMaskSelectWindow = global::MrPathV2.Editor.Windows.LayerMaskSelectWindow;
}
#endif

