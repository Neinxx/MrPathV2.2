using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MrPathV2.Runtime.Core;
using MrPathV2.Editor.GPU;
using UnityEngine;
// Use V2 GPU painter

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
            // 闂備礁鎼?粔?閻庤?鎸烽懗鍫曞礌閺?鍤掗柕鍫濇搐閻撴岸姊洪悡搴ｆ瀮婵?瀚?幈銊ㄧ疀濞戞ɑ銇濋柣蹇曞仱閻?浠氶悷婊勫灴瀹曟??鑱欐俊?娴囨禒姘?攽?閹虫捇宕愰懗?绮存繝銏ｆ硾??缂傚倸鍊烽悞锕傛晪闂佺硶鏅?鐎?
            var shouldUseGpu = ShouldUseGpuPainter(preferredType, terrain, pathBounds);

            if (shouldUseGpu && SystemInfo.supportsComputeShaders)
            {
                return GpuTerrainPainterV2.Instance;
            }
            return new UnifiedCpuTerrainPainter();
        }

        /// <summary>
        ///     闂備礁鍚嬮崕鎶藉床閼艰翰浜归柛?缁犮儵鎳曞▎鎾寸厸闁?閻熸粎澧楅悡锟犲箖娴犲?惟鐟滃酣鎮炬潏鈺冪＝濞达絽??瀹曞搫螖閸?妫?
        /// </summary>
        /// <returns>闂?闂佽桨闄嶉崐妤冩?閹烘嚦鐔煎传閸?骞闂備線娼荤徊濠氬礉瀹鍕?瀭?闁?/returns>
        public static IUnifiedTerrainPainter[] GetAvailablePainters()
        {
            var painters = new List<IUnifiedTerrainPainter>();

            // CPU缂傚倸鍊烽悞锕傛晪闂佺硶鏅?鐎规洘濞婃俊绋跨啲閼辨瑧绱撴担鑲℃垹鐟ч梻?闂?
            painters.Add(new UnifiedCpuTerrainPainter());

            // GPU缂傚倸鍊烽悞锕傛晪闂佺硶鏅?鐎规洘濞婃俊鎼佸煛閸屾瑧甯涢梺鑽ゅ枑濞叉垵霉?缂傚倷鑳舵慨?濠电偟鍘ч崯鎾?箠濠靛?绠甸柟鐑樻尵瑜版粓姊洪柈婢浡?闁?
            if (SystemInfo.supportsComputeShaders)
            {
                painters.Add(GpuTerrainPainterV2.Instance);
            }

            return painters.ToArray();
        }

        private static bool ShouldUseGpuPainter(PainterType preferredType, UnityEngine.Terrain terrain, Bounds? pathBounds)
        {
            // 濠电姷?鑱欓崘鐚溌瑜版帗鍊跺?鑸靛姇閸欏﹪骞栨潏鍓у矝閼辨瑥?鑱欐?鐐村姍瀹曟儼?闁哄棛妾窹U闂備焦瀵х粙鎴炵附閺冨倻绠旈柡?鐎垫煡鏌ゆ慨鎰?妤呭春閻樼粯鐓涢懕?闁诲骸鐏氬Λ鍐?极瀹ュ洣娌?柦妯?娴犙勭箾鏉堝墽绉?繛澶嬫礋瀵?PU
            if (preferredType == PainterType.GPU && SystemInfo.supportsComputeShaders)
                return true;

            // 濠电姷?鑱欓崘鐚溌瑜版帗鍊跺?鑸靛姇閸欏﹪骞栨潏鍓у矝閼辨瑥?鑱欐?鐐村姍瀹曟儼?闁哄棛妾睵U闂備焦瀵х粙鎴︽嚐?瀹?闁瑰嘲鎳愰幑鍕?Ω閿旇姤?CPU
            if (preferredType == PainterType.CPU)
                return false;

            // 闂?婵?閺呯娀寮婚崨?绶為悗?闂備焦瀵х粙鎺楁嚌妤ｅ啯鍋勯懕娆愮箾鐎涙?鐭婂Δ鐘?閿曘垻澹曠ｃ劋姹楅梺闈涚箳婵?増绂嶉敐澶嬪堕煫鍥ㄦ尵缁犱即鏌熼柨瀣?闁?缂備礁澧庨崑鐐?閸?闁糕剝?閸掍即鏌?
            if (terrain != null && pathBounds.HasValue)
            {
                var terrainData = terrain.terrainData;
                var terrainPixels = terrainData.alphamapWidth * terrainData.alphamapHeight;

                // 闂佽崵濮崇欢銈囨?閺囥垺鍋╅柤濮愬栧畷?缂備礁澧庨崑鐔煎箟娴兼潙骞㈡繛鍡樺姉閿涙劙姊哄Ч鍥у?閻?婵＄敻宕?閸ゅ﹪鏌℃径瀣?仴闁告捁鍋愮槐鎺撴綇閳轰絿锝夋煛?
                var pathArea = pathBounds.Value.size.x * pathBounds.Value.size.z;
                var terrainArea = terrainData.size.x * terrainData.size.z;
                var coverageRatio = pathArea / terrainArea;
                var estimatedPixels = (int)(terrainPixels * coverageRatio);

                // 濠电姷?鑱欓崘鐚溌瑜版帗鍊跺?璺烘湰閸熸椽鏌涢埄鍐?噭缁惧彞鍗抽弻娑?閸??缂備籍聙鐎孤閸?鐎?梺鐟板槻閻?厧鐡?梺鍝?閺呮稑煤?鍗遍柛?绾惧湱鐥??缂伮婢跺⊕褰掓晲閸?喓銆婇梺?PU
                const int gpuThreshold = 50000; // 闂?闂佸憡菧閸婃?妲?濠电偛鐗婇崹鍨?婵傜?閱囨繝銏℃犺仚?
                return estimatedPixels > gpuThreshold && SystemInfo.supportsComputeShaders;
            }

            // 濠?甯楃粙??濠电偠鎻?紞聙婵炲?娲熷??PU
            return false;
        }
    }
}
