using UnityEditor;
using UnityEngine;
namespace MrPathV2
{
    /// <summary>
    /// MrPath 工具的项目设置（资产集中到 Settings）。
    /// 采用 ScriptableObject + AssetDatabase，统一到 Assets/__temp/MrPathV2.2/Settings。
    /// </summary>
    public class PathToolSettings : ScriptableObject
    {
        #region 静态实例管理
        private const string AssetPath = "Assets/__temp/MrPathV2.2/Settings/MrPathSettings.asset";

        private static PathToolSettings _instance;

        public static PathToolSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

                // 优先按类型查找已存在资产
                var guids = AssetDatabase.FindAssets($"t:{nameof(PathToolSettings)}");
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    // 若存在于非标准路径，则移动到标准路径
                    if (path != AssetPath)
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));
                        AssetDatabase.MoveAsset(path, AssetPath);
                        path = AssetPath;
                    }
                    _instance = AssetDatabase.LoadAssetAtPath<PathToolSettings>(path);
                }

                // 若不存在则在集中目录创建
                if (_instance == null)
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));
                    _instance = CreateInstance<PathToolSettings>();
                    AssetDatabase.CreateAsset(_instance, AssetPath);
#if UNITY_2020_3_OR_NEWER
                    AssetDatabase.SaveAssetIfDirty(_instance);
#else
                    AssetDatabase.SaveAssets();
#endif
                }

                return _instance;
            }
        }
        #endregion

        #region 配置字段

        [Header("默认创建设置")]
        [Tooltip("新创建路径对象的默认名称")]
        public string defaultObjectName = "New Path";

        [Tooltip("新创建路径的初始线段长度（单位：米）")]
        [Min(0.1f)]
        public float defaultLineLength = 10f;

        [Header("默认外观配置")]
        [Tooltip("新创建路径将使用的默认外观配置文件")]
        public PathProfile defaultPathProfile;

        [Header("预览设置")]
        [Tooltip("预览使用的材质模板 (URP Shader)")]
        public Material previewMaterialTemplate;
        [Range(0, 1)]
        [Tooltip("预览透明度，避免遮挡场景交互")]
        public float previewAlpha = 0.5f;

        [Header("依赖工厂设置")]
        [Tooltip("用于创建预览生成器(IPreviewGenerator)的工厂")]
        public PreviewGeneratorFactory previewGeneratorFactory;
        [Tooltip("用于创建高度提供者(IHeightProvider)的工厂")]
        public HeightProviderFactory heightProviderFactory;
        [Tooltip("用于创建预览材质管理器(PreviewMaterialManager)的工厂")]
        public PreviewMaterialManagerFactory previewMaterialManagerFactory;

        [Header("策略映射覆盖")]
        [Tooltip("覆盖 Bezier 曲线的策略资产（可为空则使用注册表）")]
        public PathStrategy bezierStrategy;
        [Tooltip("覆盖 Catmull-Rom 曲线的策略资产（可为空则使用注册表）")]
        public PathStrategy catmullRomStrategy;

        [Header("场景UI设置")]
        [Tooltip("工具窗口宽度")]
        public float sceneUiWindowWidth = 180f;
        [Tooltip("工具窗口高度")]
        public float sceneUiWindowHeight = 110f;
        [Tooltip("Scene视图右侧边距")]
        public float sceneUiRightMargin = 15f;
        [Tooltip("Scene视图底部边距")]
        public float sceneUiBottomMargin = 40f;

        [Header("数据驱动操作")]
        [Tooltip("在场景工具窗口中显示的操作列表（按 order 排序）")]
        public PathTerrainOperation[] operations;

        [Header("快捷键设置")]
        [Tooltip("是否启用默认快捷键 Ctrl+W/Ctrl+P（若自定义操作，可关闭）")]
        public bool enableDefaultShortcuts = true;

        #endregion

        #region 初始化与校验

        private void OnValidate()
        {
            // OnValidate 是校验和修正数据的最佳场所
            if (string.IsNullOrEmpty(defaultObjectName))
            {
                defaultObjectName = "New Path";
            }
            if (defaultLineLength < 0.1f)
            {
                defaultLineLength = 0.1f;
            }

            // 避免每次 OnValidate 都触发保存，引发导入循环。
            // 由 SettingsProvider 在检测到变更时统一保存。
            EditorUtility.SetDirty(this);
        }

        #endregion
    }
}