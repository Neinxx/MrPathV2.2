using System;
using System.Collections.Generic;
using MrPathV2.Runtime.Core;
using MrPathV2.Runtime.Memory;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
// 仅在编辑器下使用 Handles 与 Editor API
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MrPathV2.Runtime.Preview
{
    /// <summary>
    ///     预览线条渲染器 - 统一管理所有预览相关的线条绘制
    ///     应用最佳实践：职责分离、性能 优化、可扩展设计
    /// </summary>
    public class PreviewLineRenderer : IDisposable
    {

        #region IDisposable

        public void Dispose()
        {
            _tempPointsOwner?.Dispose();

            _lineSegments?.Clear();

            foreach (var batch in _batchedLines.Values)
            {
                batch?.Clear();
            }

            // 确保释放GPU相关资源，避免ComputeBuffer/材质泄漏
            ReleaseGpuResources();
        }

        #endregion
        #region 线条类型定义

        public enum LineType
        {
            PathCurve, // 路径曲线
            ControlLine, // 控制线（贝塞尔）
            WireframeEdge, // 网格边框线
            DebugLine, // 调试线条
            HandleConnection // 控制点连接线
        }

        [Serializable]
        public struct LineStyle
        {
            public Color color;
            public float thickness;
            public bool dashed;
            public float dashSize;
            public bool antiAliased;

            public static LineStyle Default => new LineStyle
            {
                color = Color.white,
                thickness = 2f,
                dashed = false,
                dashSize = 4f,
                antiAliased = true
            };
        }

        private struct LineSegment
        {
            public Vector3 Start;
            public Vector3 End;
            public LineStyle Style;
            public LineType Type;
            public int Priority; // 渲染优先级
        }

        #endregion

        #region 私有字段

        private readonly List<LineSegment> _lineSegments;
        private readonly Dictionary<LineType, LineStyle> _defaultStyles;
        private readonly MemoryOwner<NativeList<float3>> _tempPointsOwner;

        // 性能优化相关
        private Camera _currentCamera;
        private Plane[] _frustumPlanes;
        private bool _enableFrustumCulling = true;
        private bool _enableDistanceCulling = true;
        private float _maxRenderDistance = 1000f;

        // 批量渲染优化
        private readonly Dictionary<LineType, List<LineSegment>> _batchedLines;
        private bool _isDirty = true;

        // GPU 渲染相关
        private bool _useGpu = false;
        private Material _gpuMat;
        private ComputeBuffer _segmentBuffer;
        private Mesh _unitQuad;
        private static readonly int _SegmentsId = Shader.PropertyToID("_Segments");
        private Bounds _gpuDrawBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        // 默认虚线长度（像素），作为样式未设置时的兜底值
        private float _defaultDashPixels = 10f;
        private bool _fastDashed;
        private float _collinearCosThreshold = 0.998f;
        private int _maxPolylinePoints = 4096;

        private readonly Dictionary<Color, List<LineSegment>> _simpleGroupsCache = new Dictionary<Color, List<LineSegment>>();
        private readonly Dictionary<(Color color, float thickness), List<LineSegment>> _aaGroupsCache = new Dictionary<(Color color, float thickness), List<LineSegment>>();
        private readonly Dictionary<(Color color, float dashSize), List<LineSegment>> _dashedGroupsCache = new Dictionary<(Color color, float dashSize), List<LineSegment>>();
        private Vector3[] _pointsBuffer;
        private Vector3[] _pointsExact;
        private List<Vector3> _polyBuffer;
        private Vector3[] _polyExact;

        #endregion

        #region 构造函数和初始化

        public PreviewLineRenderer(int initialCapacity = 256)
        {
            _lineSegments = new List<LineSegment>(initialCapacity);
            _defaultStyles = new Dictionary<LineType, LineStyle>();
            _tempPointsOwner = UnifiedMemory.Instance.RentNativeList<float3>(64, Allocator.Persistent);
            _batchedLines = new Dictionary<LineType, List<LineSegment>>();

            InitializeDefaultStyles();
            InitializeBatchedLists();
            InitializeGpuResources();
        }

        private void InitializeDefaultStyles()
        {
            _defaultStyles[LineType.PathCurve] = new LineStyle
            {
                color = new Color(0.2f, 0.8f, 1f, 0.8f),
                thickness = 3f,
                antiAliased = true
            };

            _defaultStyles[LineType.ControlLine] = new LineStyle
            {
                color = new Color(1f, 1f, 1f, 0.4f),
                thickness = 1f,
                dashed = true,
                dashSize = 4f
            };

            _defaultStyles[LineType.WireframeEdge] = new LineStyle
            {
                color = new Color(0.5f, 0.5f, 0.5f, 0.6f),
                thickness = 1f,
                antiAliased = false
            };

            _defaultStyles[LineType.DebugLine] = new LineStyle
            {
                color = Color.red,
                thickness = 2f,
                antiAliased = true
            };

            _defaultStyles[LineType.HandleConnection] = new LineStyle
            {
                color = new Color(1f, 0.8f, 0.2f, 0.7f),
                thickness = 2f,
                antiAliased = true
            };
        }

        private void InitializeBatchedLists()
        {
            foreach (LineType type in Enum.GetValues(typeof(LineType)))
            {
                _batchedLines[type] = new List<LineSegment>();
            }
        }

        #endregion

        #region 公共API

        /// <summary>
        ///     设置当前渲染相机（用于视锥体剔除）
        /// </summary>
        public void SetCamera(Camera camera)
        {
            if (_currentCamera != camera)
            {
                _currentCamera = camera;
                if (camera != null)
                {
                    _frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                }
            }
        }

        /// <summary>
        ///     启用或禁用GPU绘制模式（失败自动回退到Handles）
        /// </summary>
        public void SetUseGpu(bool enabled)
        {
            // 统一禁用GPU：无论输入如何，均保持CPU渲染
            _useGpu = false;
        }

        /// <summary>
        ///     添加单条线段
        /// </summary>
        public void AddLine(Vector3 start, Vector3 end, LineType type, LineStyle? customStyle = null, int priority = 0)
        {
            var style = customStyle ?? GetDefaultStyle(type);

            _lineSegments.Add(new LineSegment
            {
                Start = start,
                End = end,
                Style = style,
                Type = type,
                Priority = priority
            });

            _isDirty = true;
        }

        /// <summary>
        ///     添加多段连续线条（如曲线）
        /// </summary>
        private void AddPolyLine(Vector3[] points, LineType type, LineStyle? customStyle = null, int priority = 0)
        {
            if (points == null || points.Length < 2) return;

            var style = customStyle ?? GetDefaultStyle(type);

            for (var i = 0; i < points.Length - 1; i++)
            {
                _lineSegments.Add(new LineSegment
                {
                    Start = points[i],
                    End = points[i + 1],
                    Style = style,
                    Type = type,
                    Priority = priority
                });
            }

            _isDirty = true;
        }

        /// <summary>
        ///     添加贝塞尔曲线
        /// </summary>
        public void AddBezierCurve(Vector3 start, Vector3 end, Vector3 control1, Vector3 control2,
            LineType type, int resolution = 32, LineStyle? customStyle = null, int priority = 0)
        {
            var points = GenerateBezierPoints(start, end, control1, control2, resolution);
            AddPolyLine(points, type, customStyle, priority);
        }

        /// <summary>
        ///     添加贝塞尔曲线（屏幕自适应采样）。根据当前相机与像素步长估算分辨率。
        /// </summary>
        public void AddBezierCurveAdaptive(Vector3 start, Vector3 end, Vector3 control1, Vector3 control2,
            LineType type, float maxPixelStep = 6f, LineStyle? customStyle = null, int priority = 0,
            int minResolution = 8, int maxResolution = 2048)
        {
            // 提前返回：输入校验
            if (maxPixelStep <= 0f)
            {
                AddBezierCurve(start, end, control1, control2, type, 32, customStyle, priority);
                return;
            }

            // 如相机未设置，回退到固定分辨率，避免阻塞交互
            var resolution = 32;
            if (_currentCamera != null)
            {
                resolution = EstimateBezierResolution(start, end, control1, control2, _currentCamera, maxPixelStep, minResolution, maxResolution);
            }

            var points = GenerateBezierPoints(start, end, control1, control2, resolution);
            AddPolyLine(points, type, customStyle, priority);
        }

        /// <summary>
        ///     添加Catmull-Rom样条曲线
        /// </summary>
        public void AddCatmullRomSpline(Vector3[] controlPoints, LineType type, int resolution = 16,
            LineStyle? customStyle = null, int priority = 0)
        {
            if (controlPoints == null || controlPoints.Length < 4) return;

            var points = GenerateCatmullRomPoints(controlPoints, resolution);
            AddPolyLine(points, type, customStyle, priority);
        }

        /// <summary>
        ///     添加Catmull-Rom样条曲线（屏幕自适应采样）。根据当前相机与像素步长估算分辨率。
        /// </summary>
        public void AddCatmullRomSplineAdaptive(Vector3[] controlPoints, LineType type, float maxPixelStep = 6f,
            LineStyle? customStyle = null, int priority = 0, int minResolution = 8, int maxResolution = 1024)
        {
            // 提前返回：输入与参数校验
            if (controlPoints == null || controlPoints.Length < 4)
                return;

            if (maxPixelStep <= 0f)
            {
                AddCatmullRomSpline(controlPoints, type, 16, customStyle, priority);
                return;
            }

            var resolution = 16;
            if (_currentCamera != null)
            {
                resolution = EstimateCatmullResolution(controlPoints, _currentCamera, maxPixelStep, minResolution, maxResolution);
            }

            var points = GenerateCatmullRomPoints(controlPoints, resolution);
            AddPolyLine(points, type, customStyle, priority);
        }

        /// <summary>
        ///     清除所有线条
        /// </summary>
        public void Clear()
        {
            _lineSegments.Clear();
            foreach (var batch in _batchedLines.Values)
            {
                batch.Clear();
            }
            _isDirty = true;
            // 不在此处释放GPU缓冲，避免每帧重复分配；按需在渲染时扩容
        }

        /// <summary>
        ///     清除指定类型的线条
        /// </summary>
        public void Clear(LineType type)
        {
            _lineSegments.RemoveAll(line => line.Type == type);
            _batchedLines[type].Clear();
            _isDirty = true;
        }

        /// <summary>
        ///     渲染所有线条
        /// </summary>
        public void Render()
        {
#if UNITY_EDITOR
            if (_lineSegments.Count == 0) return;

            // 更新批次（如果需要）
            if (_isDirty)
            {
                UpdateBatches();
                _isDirty = false;
            }

            // 按优先级和类型渲染
            foreach (LineType type in Enum.GetValues(typeof(LineType)))
            {
                if (_useGpu)
                {
                    RenderBatchGpu(type);
                }
                else
                {
                    RenderBatch(type);
                }
            }
#endif
        }

        /// <summary>
        ///     设置默认样式
        /// </summary>
        public void SetDefaultStyle(LineType type, LineStyle style)
        {
            _defaultStyles[type] = style;
        }

        /// <summary>
        ///     通过依赖注入方式设置默认样式提供器。
        ///     提供器按 <see cref="LineType"/> 返回对应的 <see cref="LineStyle"/>。
        ///     若提供器为 null 则提前返回，保持现有默认样式。
        /// </summary>
        public void UseStyleProvider(Func<LineType, LineStyle> provider)
        {
            if (provider == null) return; // 提前返回：空提供器不生效

            foreach (LineType type in Enum.GetValues(typeof(LineType)))
            {
                try
                {
                    var style = provider(type);
                    _defaultStyles[type] = style;
                }
                catch
                {
                    // 防御性：单个类型获取失败不影响其他类型；保留旧值
                }
            }
        }

        /// <summary>
        ///     获取默认样式
        /// </summary>
        private LineStyle GetDefaultStyle(LineType type) => _defaultStyles.TryGetValue(type, out var style) ? style : LineStyle.Default;

        #endregion

        #region 私有方法

        private void UpdateBatches()
        {
            using (ProfilingMarkers.PreviewLineRendererUpdate.Auto())
            {
                // 清空批次
                foreach (var batch in _batchedLines.Values)
                {
                    batch.Clear();
                }

                // 按类型分组并排序
                for (var i = 0; i < _lineSegments.Count; i++)
                {
                    var line = _lineSegments[i];
                    if (ShouldRenderLine(line)) _batchedLines[line.Type].Add(line);
                }

                // 按优先级排序每个批次
                foreach (var batch in _batchedLines.Values)
                {
                    batch.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                }
            }
        }

        private bool ShouldRenderLine(LineSegment line)
        {
            // 距离剔除
            if (_enableDistanceCulling && _currentCamera != null)
            {
                var center = (line.Start + line.End) * 0.5f;
                var distance = Vector3.Distance(_currentCamera.transform.position, center);
                if (distance > _maxRenderDistance) return false;
            }

            // 视锥体剔除
            if (_enableFrustumCulling && _currentCamera != null && _frustumPlanes != null)
            {
                var bounds = new Bounds((line.Start + line.End) * 0.5f,
                    Vector3.one * Vector3.Distance(line.Start, line.End));
                if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds)) return false;
            }

            return true;
        }

        private void RenderBatch(LineType type)
        {
#if UNITY_EDITOR
            var batch = _batchedLines[type];
            if (batch.Count == 0) return; // 提前返回
            foreach (var kv in _simpleGroupsCache) kv.Value.Clear();
            foreach (var kv in _aaGroupsCache) kv.Value.Clear();
            foreach (var kv in _dashedGroupsCache) kv.Value.Clear();

            for (var idx = 0; idx < batch.Count; idx++)
            {
                var line = batch[idx];
                var style = line.Style;
                if (style.dashed)
                {
                    var dash = Mathf.Max(1f, style.dashSize > 0 ? style.dashSize : _defaultDashPixels);
                    var key = (style.color, dash);
                    if (!_dashedGroupsCache.TryGetValue(key, out var list))
                    {
                        list = new List<LineSegment>(64);
                        _dashedGroupsCache[key] = list;
                    }
                    list.Add(line);
                    continue;
                }

                if (style.antiAliased)
                {
                    var key = (style.color, Mathf.Max(0.5f, style.thickness));
                    if (!_aaGroupsCache.TryGetValue(key, out var list))
                    {
                        list = new List<LineSegment>(64);
                        _aaGroupsCache[key] = list;
                    }
                    list.Add(line);
                    continue;
                }

                // 普通线（DrawLines 可批量）
                if (!_simpleGroupsCache.TryGetValue(style.color, out var slist))
                {
                    slist = new List<LineSegment>(128);
                    _simpleGroupsCache[style.color] = slist;
                }
                slist.Add(line);
            }

            // 绘制普通线：一次性批量调用 Handles.DrawLines
            foreach (var kv in _simpleGroupsCache)
            {
                var color = kv.Key;
                var lines = kv.Value;
                if (lines == null || lines.Count == 0) continue;

                using (new Handles.DrawingScope(color))
                {
                    var needed = lines.Count * 2;
                    if (_pointsBuffer == null || _pointsBuffer.Length < needed)
                        _pointsBuffer = new Vector3[Mathf.NextPowerOfTwo(needed)];
                    if (_pointsExact == null || _pointsExact.Length != needed)
                        _pointsExact = new Vector3[needed];
                    var pi = 0;
                    for (var i = 0; i < lines.Count; i++)
                    {
                        _pointsBuffer[pi++] = lines[i].Start;
                        _pointsBuffer[pi++] = lines[i].End;
                    }
                    Array.Copy(_pointsBuffer, 0, _pointsExact, 0, needed);
                    Handles.DrawLines(_pointsExact);
                }
            }

            // 绘制抗锯齿线：按组构造连续折线，减少 DrawAAPolyLine 调用
            const float eps = 1e-4f;
            foreach (var kv in _aaGroupsCache)
            {
                var (color, width) = kv.Key;
                var lines = kv.Value;
                if (lines == null || lines.Count == 0) continue;

                using (new Handles.DrawingScope(color))
                {
                    _polyBuffer ??= new List<Vector3>(8);
                    var poly = _polyBuffer;
                    Vector3 lastEnd = default;
                    var hasPoly = false;
                    for (var i = 0; i < lines.Count; i++)
                    {
                        var seg = lines[i];
                        if (!hasPoly)
                        {
                            poly.Clear();
                            poly.Add(seg.Start);
                            poly.Add(seg.End);
                            lastEnd = seg.End;
                            hasPoly = true;
                            continue;
                        }

                        // 与上一段连续，则扩展为一条折线；否则先绘制，再开启新折线
                        if (Vector3.Distance(lastEnd, seg.Start) < eps)
                        {
                            poly.Add(seg.End);
                            lastEnd = seg.End;
                        }
                        else
                        {
                            if (poly.Count >= 2)
                            {
                                if (_polyExact == null || _polyExact.Length != poly.Count)
                                    _polyExact = new Vector3[poly.Count];
                                for (var k = 0; k < poly.Count; k++) _polyExact[k] = poly[k];
                                Handles.DrawAAPolyLine(width, _polyExact);
                            }
                            poly.Clear();
                            poly.Add(seg.Start);
                            poly.Add(seg.End);
                            lastEnd = seg.End;
                        }
                    }
                    if (hasPoly && poly != null && poly.Count >= 2)
                    {
                        if (_polyExact == null || _polyExact.Length != poly.Count)
                            _polyExact = new Vector3[poly.Count];
                        for (var k = 0; k < poly.Count; k++) _polyExact[k] = poly[k];
                        Handles.DrawAAPolyLine(width, _polyExact);
                    }
                }
            }

            // 绘制虚线：按组设置颜色与虚线长度，逐段调用（API不支持批量）
            foreach (var kv in _dashedGroupsCache)
            {
                var (color, dash) = kv.Key;
                var lines = kv.Value;
                if (lines == null || lines.Count == 0) continue;
                using (new Handles.DrawingScope(color))
                {
                    if (_fastDashed)
                    {
                        _polyBuffer ??= new List<Vector3>(lines.Count + 1);
                        _polyBuffer.Clear();
                        _polyBuffer.Add(lines[0].Start);
                        for (var i = 0; i < lines.Count; i++) _polyBuffer.Add(lines[i].End);
                        if (_polyExact == null || _polyExact.Length != _polyBuffer.Count) _polyExact = new Vector3[_polyBuffer.Count];
                        for (var i = 0; i < _polyBuffer.Count; i++) _polyExact[i] = _polyBuffer[i];
                        Handles.DrawAAPolyLine(1f, _polyExact);
                    }
                    else
                    {
                        for (var i = 0; i < lines.Count; i++) Handles.DrawDottedLine(lines[i].Start, lines[i].End, dash);
                    }
                }
            }
#endif
        }

        private struct SegmentData
        {
            public Vector3 start;
            public Vector3 end;
            public Color color;
            public float thickness;
            public float dashSize;
            public uint flags; // bit0: dashed, bit1: aa(保留), bit2: 起点连接, bit3: 终点连接
        }

        private void RenderBatchGpu(LineType type)
        {
#if UNITY_EDITOR
            if (_gpuMat == null || _unitQuad == null)
            {
                // GPU资源不可用时回退
                RenderBatch(type);
                return;
            }

            var batch = _batchedLines[type];
            if (batch.Count == 0) return;

            EnsureSegmentBufferSize(batch.Count);

            var segments = new SegmentData[batch.Count];
            for (var i = 0; i < batch.Count; i++)
            {
                var line = batch[i];
                uint flags = 0u;
                if (line.Style.dashed) flags |= 1u;
                if (line.Style.antiAliased) flags |= 2u; // 保留位

                // 邻接段接缝标记：起点/终点连接
                const float eps = 1e-4f;
                var startJoined = i > 0 && Vector3.Distance(batch[i - 1].End, line.Start) < eps;
                var endJoined = i < batch.Count - 1 && Vector3.Distance(line.End, batch[i + 1].Start) < eps;
                if (startJoined) flags |= 4u;
                if (endJoined) flags |= 8u;

                segments[i] = new SegmentData
                {
                    start = line.Start,
                    end = line.End,
                    color = line.Style.color,
                    thickness = Mathf.Max(0.0001f, line.Style.thickness), // 像素单位
                    dashSize = Mathf.Max(0.0001f, line.Style.dashSize > 0 ? line.Style.dashSize : _defaultDashPixels),   // 像素单位（支持全局默认）
                    flags = flags
                };
            }

            _segmentBuffer.SetData(segments);
            _gpuMat.SetBuffer(_SegmentsId, _segmentBuffer);

            // 使用较大的包围盒以避免被剔除；实际剔除通过CPU侧ShouldRenderLine处理
            Graphics.DrawMeshInstancedProcedural(_unitQuad, 0, _gpuMat, _gpuDrawBounds, batch.Count);
#endif
        }

        private void RenderSingleLine(LineSegment line)
        {
#if UNITY_EDITOR
            var oldColor = Handles.color;
            Handles.color = line.Style.color;

            try
            {
                if (line.Style.dashed)
                {
                    Handles.DrawDottedLine(line.Start, line.End, line.Style.dashSize);
                }
                else if (line.Style.antiAliased)
                {
                    Handles.DrawAAPolyLine(line.Style.thickness, line.Start, line.End);
                }
                else
                {
                    Handles.DrawLine(line.Start, line.End);
                }
            }
            finally
            {
                Handles.color = oldColor;
            }
#endif
        }

        private Vector3[] GenerateBezierPoints(Vector3 start, Vector3 end, Vector3 control1, Vector3 control2, int resolution)
        {
            // 提前返回：分辨率校验
            if (resolution <= 0)
            {
                return new[] { start, end };
            }

            NativeArray<float3> nativePoints = default;
            try
            {
                // 使用Job并行生成Bezier点（resolution+1个点）
                nativePoints = new NativeArray<float3>(resolution + 1, Allocator.TempJob);

                var job = new MrPathV2.Runtime.Jobs.PreviewLineJobs.GenerateBezierPointsJob
                {
                    P0 = new float3(start.x, start.y, start.z),
                    P1 = new float3(control1.x, control1.y, control1.z),
                    P2 = new float3(control2.x, control2.y, control2.z),
                    P3 = new float3(end.x, end.y, end.z),
                    Resolution = resolution,
                    Points = nativePoints
                };

                var handle = job.Schedule(nativePoints.Length, 64);
                handle.Complete();

                // 转换为 Vector3
                var points = new Vector3[nativePoints.Length];
                for (var i = 0; i < nativePoints.Length; i++)
                {
                    var p = nativePoints[i];
                    points[i] = new Vector3(p.x, p.y, p.z);
                }
                return points;
            }
            finally
            {
                if (nativePoints.IsCreated) nativePoints.Dispose();
            }
        }

        private Vector3 CalculateBezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1 - t;
            var tt = t * t;
            var uu = u * u;
            var uuu = uu * u;
            var ttt = tt * t;

            return uuu * p0 + 3 * uu * t * p1 + 3 * u * tt * p2 + ttt * p3;
        }

        /// <summary>
        ///     估算屏幕分辨率（Bezier）：粗采样测量屏幕空间长度，按像素步长换算分辨率。
        /// </summary>
        private int EstimateBezierResolution(Vector3 p0, Vector3 p3, Vector3 c0, Vector3 c1, Camera cam,
            float maxPixelStep, int minRes, int maxRes)
        {
            const int coarse = 64; // 粗采样点数（包含终点）
            var last = p0;
            var screenLen = 0f;
            for (int i = 1; i <= coarse; i++)
            {
                var t = i / (float)coarse;
                var cur = CalculateBezierPoint(p0, c0, c1, p3, t);
                var a = cam.WorldToScreenPoint(last);
                var b = cam.WorldToScreenPoint(cur);
                screenLen += Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));
                last = cur;
            }

            var samples = Mathf.Clamp(Mathf.CeilToInt(screenLen / Mathf.Max(1f, maxPixelStep)), minRes, maxRes);
            return samples;
        }

        private Vector3[] GenerateCatmullRomPoints(Vector3[] controlPoints, int resolution)
        {
            // 提前返回：输入与分辨率校验
            if (controlPoints == null || controlPoints.Length < 4 || resolution <= 0)
            {
                return Array.Empty<Vector3>();
            }

            var segments = controlPoints.Length - 3;
            var totalPoints = segments * resolution; // 不包含各段终点

            NativeArray<float3> nativeCp = default;
            NativeArray<float3> nativePoints = default;
            try
            {
                // 准备Native数据
                nativeCp = new NativeArray<float3>(controlPoints.Length, Allocator.TempJob);
                for (var i = 0; i < controlPoints.Length; i++)
                {
                    var cp = controlPoints[i];
                    nativeCp[i] = new float3(cp.x, cp.y, cp.z);
                }

                nativePoints = new NativeArray<float3>(totalPoints, Allocator.TempJob);

                var job = new MrPathV2.Runtime.Jobs.PreviewLineJobs.GenerateCatmullRomPointsJob
                {
                    ControlPoints = nativeCp,
                    Resolution = resolution,
                    Points = nativePoints
                };

                var handle = job.Schedule(nativePoints.Length, 64);
                handle.Complete();

                // 转换结果并按原语义补最后一个点（controlPoints[^2]）
                var result = new List<Vector3>(nativePoints.Length + 1);
                for (var i = 0; i < nativePoints.Length; i++)
                {
                    var p = nativePoints[i];
                    result.Add(new Vector3(p.x, p.y, p.z));
                }
                result.Add(controlPoints[^2]);
                return result.ToArray();
            }
            finally
            {
                if (nativePoints.IsCreated) nativePoints.Dispose();
                if (nativeCp.IsCreated) nativeCp.Dispose();
            }
        }

        private static Vector3 CalculateCatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var tt = t * t;
            var ttt = tt * t;

            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * tt +
                (-p0 + 3f * p1 - 3f * p2 + p3) * ttt
                );
        }

        /// <summary>
        ///     估算屏幕分辨率（Catmull-Rom）：对每段做粗采样测量屏幕空间长度。
        /// </summary>
        private int EstimateCatmullResolution(Vector3[] cps, Camera cam, float maxPixelStep, int minRes, int maxRes)
        {
            // cps: 至少4个点，段数 = cps.Length - 3
            const int coarsePerSegment = 16;
            var totalScreenLen = 0f;
            var segments = cps.Length - 3;
            for (int seg = 0; seg < segments; seg++)
            {
                var p0 = cps[seg];
                var p1 = cps[seg + 1];
                var p2 = cps[seg + 2];
                var p3 = cps[seg + 3];
                var last = p1; // Catmull-Rom通常以p1为段起点
                for (int i = 1; i <= coarsePerSegment; i++)
                {
                    var t = i / (float)coarsePerSegment;
                    var cur = CalculateCatmullRomPoint(p0, p1, p2, p3, t);
                    var a = cam.WorldToScreenPoint(last);
                    var b = cam.WorldToScreenPoint(cur);
                    totalScreenLen += Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));
                    last = cur;
                }
            }

            var samplesPerSegment = Mathf.CeilToInt(totalScreenLen / Mathf.Max(1f, maxPixelStep));
            return Mathf.Clamp(samplesPerSegment, minRes, maxRes);
        }

        #endregion

        #region 性能配置

        /// <summary>
        ///     启用/禁用视锥体剔除
        /// </summary>
        public void SetFrustumCulling(bool enabled)
        {
            _enableFrustumCulling = enabled;
        }

        /// <summary>
        ///     启用/禁用距离剔除
        /// </summary>
        public void SetDistanceCulling(bool enabled, float maxDistance = 1000f)
        {
            _enableDistanceCulling = enabled;
            _maxRenderDistance = maxDistance;
        }

        /// <summary>
        ///     获取当前线条统计信息
        /// </summary>
        public (int total, int rendered) GetRenderStats()
        {
            var rendered = 0;
            foreach (var line in _lineSegments)
            {
                if (ShouldRenderLine(line)) rendered++;
            }

            return (_lineSegments.Count, rendered);
        }

        #endregion

        #region GPU 初始化与资源管理
        private void InitializeGpuResources()
        {
#if UNITY_EDITOR
            // CPU-only 模式：不创建任何GPU资源，确保渲染路径简洁稳定
            _gpuMat = null;
            _unitQuad = null;
            _segmentBuffer = null;
#endif
        }

        /// <summary>
        ///     应用来自编辑器侧的预览参数配置（依赖注入）。
        ///     仅更新材质常量与默认虚线长度，不创建或销毁资源。
        /// </summary>
        public void ApplyPreviewConfig(PreviewLineConfig cfg)
        {
            // 提前返回：空或无效配置不生效
            if (!cfg.IsValid) return;

            _defaultDashPixels = Mathf.Max(1f, cfg.DefaultDashPixels);
            _fastDashed = cfg.FastDashed;

#if UNITY_EDITOR
            if (_gpuMat)
            {
                _gpuMat.SetFloat("_AAWidthPx", Mathf.Max(0.5f, cfg.AAWidthPx));
                _gpuMat.SetFloat("_CapAAWidthPx", Mathf.Max(0.5f, cfg.CapAAWidthPx));
                _gpuMat.SetFloat("_SeamScale", Mathf.Max(0.25f, cfg.SeamScale));
                _gpuMat.SetInt("_CapType", Mathf.Clamp(cfg.CapType, 0, 2));
            }
#endif
        }

        private Mesh BuildUnitQuad()
        {
            var m = new Mesh { name = "MrPath_GpuLine_UnitQuad" };
            var verts = new[]
            {
                new Vector3(0f, -0.5f, 0f),
                new Vector3(1f, -0.5f, 0f),
                new Vector3(0f,  0.5f, 0f),
                new Vector3(1f,  0.5f, 0f)
            };
            var uvs = new[]
            {
                new Vector2(0f, -0.5f),
                new Vector2(1f, -0.5f),
                new Vector2(0f,  0.5f),
                new Vector2(1f,  0.5f)
            };
            var tris = new[] { 0, 2, 1, 1, 2, 3 };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.hideFlags = HideFlags.HideAndDontSave;
            return m;
        }

        private void EnsureSegmentBufferSize(int count)
        {
            // start(3) + end(3) + color(4) + thickness + dashSize + flags(uint)
            var stride = sizeof(float) * (3 + 3 + 4 + 1 + 1) + sizeof(int);
            if (_segmentBuffer == null || _segmentBuffer.count < count)
            {
                _segmentBuffer?.Dispose();
                _segmentBuffer = new ComputeBuffer(Mathf.NextPowerOfTwo(count), stride);
            }
        }

        public void ReleaseGpuResources()
        {
            try
            {
                _segmentBuffer?.Dispose();
                _segmentBuffer = null;
                if (_gpuMat != null)
                {
                    if (Application.isEditor) UnityEngine.Object.DestroyImmediate(_gpuMat);
                    else UnityEngine.Object.Destroy(_gpuMat);
                    _gpuMat = null;
                }
                if (_unitQuad != null)
                {
                    if (Application.isEditor) UnityEngine.Object.DestroyImmediate(_unitQuad);
                    else UnityEngine.Object.Destroy(_unitQuad);
                    _unitQuad = null;
                }
            }
            catch { /* ignore */ }
        }
        #endregion
    }
}
