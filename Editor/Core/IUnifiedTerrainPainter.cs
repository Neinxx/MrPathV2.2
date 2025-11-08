using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Core;
using MrPathV2.Editor.Terrain;
using MrPathV2.Editor.Settings;
using UnityEngine;
// CPU-only 模式下移除 GPU 依赖与引用，保留统一工厂与接口

namespace MrPathV2.Editor.Core
{
    // 统一地形画笔接口，支持 using 释放与异步/同步绘制
    public interface IUnifiedTerrainPainter : IDisposable
    {

        bool IsSupported { get; }
        PainterType Type { get; }
        Task<TerrainPaintResult> PaintAsync(
            PathCreator pathCreator,
            bool isPreview = false,
            CancellationToken cancellationToken = default);

        TerrainPaintResult Paint(
            PathCreator pathCreator,
            bool isPreview = false);
    }

    /// <summary>
    ///     缂傚倸鍊烽悞锕傛晪闂佺硶鏅?鐎规洘濞婃俊姝??闂備焦鎮堕崝?闂佽?绻愬ú锔剧矙?
    /// </summary>
    public enum PainterType
    {
        CPU = 0,
        GPU = 1
    }

    /// <summary>
    ///     闂備線娼婚梽鍕?濮椔閹虫粓骞?绾惧吋淇?閹?鎮￠幋鐘电＝濞达綀?鐏忎即鏌?
    /// </summary>
    public struct TerrainPaintResult
    {
        public bool IsSuccess;
        public string ErrorMessage;
        public Texture2D PreviewTexture;
        public float ExecutionTimeMs;
        public PainterType UsedPainter;
        public int ProcessedTerrainCount;
        public int SuccessfulTerrainCount;
        public int FailedTerrainCount;

        public static TerrainPaintResult CreateSuccess(PainterType painterType, float executionTime, Texture2D previewTexture = null) => new TerrainPaintResult
        {
            IsSuccess = true,
            UsedPainter = painterType,
            ExecutionTimeMs = executionTime,
            PreviewTexture = previewTexture,
            ProcessedTerrainCount = 1,
            SuccessfulTerrainCount = 1,
            FailedTerrainCount = 0
        };

        public static TerrainPaintResult CreateFailure(string errorMessage, PainterType painterType) => new TerrainPaintResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            UsedPainter = painterType,
            ProcessedTerrainCount = 1,
            SuccessfulTerrainCount = 0,
            FailedTerrainCount = 1
        };
    }

    /// <summary>
    ///     缂傚倸鍊烽悞锕傛晪闂佺硶鏅?鐎规洘濞婃俊鎼佸Ψ閵堝洨鐓戦梻?-
    ///     闂備礁鎼?粔?閻庤?鎸烽懗鍫曞礌閺?鍤掗柕鍫濇搐閻撴岸姊绘担瑙勭凡缂佸?鐖奸幃鍧楀幢濞戞ɑ銇濋柣蹇曞仱閻?浠氶悷婊勫灴瀹曟??鑱欐俊?娴囨禒姘?攽?閹虫捇宕愰懗?绮存繝銏ｆ硾??闂備礁鎼?悡?浠氬┑鐐舵彧缁茬晫澹?閸?鐎规洘姘ㄩ幑鍕?倻濡?厧缍?
    /// </summary>
    public static class UnifiedPainterFactory
    {
        /// <summary>
        ///     闂備礁鎲＄敮妤冪矙閹寸姷纾介柟鎹?鐎氬?鎳曞▎寰濆綊鏁愰崨?绱炵紓?鐎规洘姘ㄩ幑鍕?倻濡?厧缍旈梺璇查?濠閬嶅磻濡?吋??
        /// </summary>
        /// <param name="preferredType">
        ///     濠?缁查箖鎮ч悩杞扮泊婵犮垼娉涢惉鑲╂暜濞戙垺鐓曢柟鐑樻礃绾?偓绻涢崨?绗ч柣姘?⒔閹风娀鎳?鑱?/param>
        ///     <param name="terrain">
        ///         闂?闂佺粯鐗?紞浣哥暦閻戣棄绾ч柤?閻?/param>
        ///         <param name="pathBounds">
        ///             闂?缂備礁澧庨崑鐐?韫囨稑鎹?婵炲懏娲熼弻銊モ槈濡?厧鈪遍梺杞伴檷閸婃牜绮嬪?鍡樺劅闁抽攱?鑱欓悷婊勫灴瀹曟?螣閸忕厧鐝伴梺?鐎?鍏橀弻?/param>
        ///             <returns>缂傚倸鍊烽悞锕傛晪闂佺硶鏅?鐎规洘濞婃俊鎼佸Ψ瑜庡?鏍ㄧ箾?/returns>
        public static IUnifiedTerrainPainter CreatePainter(
            PainterType preferredType = PainterType.CPU,
            UnityEngine.Terrain terrain = null,
            Bounds? pathBounds = null)
        {
            // CPU-only 模式：遵循“提前返回”与“依赖接口”的原则，避免对具体 GPU 实现的直接依赖。
            // 无论调用方偏好或硬件支持与否，统一返回 CPU 绘制器以保证一致性与可维护性。
            return new UnifiedCpuTerrainPainter();
        }

        /// <summary>
        ///     闂備礁鍚嬮崕鎶藉床閼艰翰浜归柛?缁犮儵鎳曞▎鎾寸厸闁?閻熸粎澧楅悡锟犲箖娴犲?惟鐟滃酣鎮炬潏鈺冪＝濞达絽??瀹曞搫螖閸?妫?
        /// </summary>
        /// <returns>闂?闂佽桨闄嶉崐妤冩?閹烘嚦鐔煎传閸?骞闂備線娼荤徊濠氬礉瀹鍕?瀭?闁?/returns>
        public static IUnifiedTerrainPainter[] GetAvailablePainters()
        {
            // 仅暴露 CPU 绘制器，避免在选择器层面出现 GPU 选项，符合单一职责与统一策略。
            return new IUnifiedTerrainPainter[]
            {
                new UnifiedCpuTerrainPainter()
            };
        }

        // CPU-only 策略下不再需要 GPU 选择逻辑；保留空实现避免外部引用编译错误。
        private static bool ShouldUseGpuPainter(PainterType preferredType, UnityEngine.Terrain terrain, Bounds? pathBounds) => false;
    }
}
