using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using MrPathV2.Memory;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MrPathV2
{
    /// <summary>
    /// 预览线条渲染器 - 统一管理所有预览相关的线条绘制
    /// 应用最佳实践：职责分离、性能优化、可扩展设计
    /// </summary>
    public class PreviewLineRenderer : IDisposable
    {
        #region 线条类型定义
        
        public enum LineType
        {
            PathCurve,          // 路径曲线
            ControlLine,        // 控制线（贝塞尔）
            WireframeEdge,      // 网格边框线
            DebugLine,          // 调试线条
            HandleConnection    // 控制点连接线
        }

        [System.Serializable]
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
            public Vector3 start;
            public Vector3 end;
            public LineStyle style;
            public LineType type;
            public int priority; // 渲染优先级
        }

        #endregion

        #region 私有字段

        private readonly List<LineSegment> _lineSegments;
        private readonly Dictionary<LineType, LineStyle> _defaultStyles;
        private readonly NativeList<float3> _tempPoints;
        private readonly MrPathV2.Memory.MemoryOwner<NativeList<float3>> _tempPointsOwner;
        
        // 性能优化相关
        private Camera _currentCamera;
        private Plane[] _frustumPlanes;
        private bool _enableFrustumCulling = true;
        private bool _enableDistanceCulling = true;
        private float _maxRenderDistance = 1000f;
        
        // 批量渲染优化
        private readonly Dictionary<LineType, List<LineSegment>> _batchedLines;
        private bool _isDirty = true;

        #endregion

        #region 构造函数和初始化

        public PreviewLineRenderer(int initialCapacity = 256)
        {
            _lineSegments = new List<LineSegment>(initialCapacity);
            _defaultStyles = new Dictionary<LineType, LineStyle>();
            _tempPointsOwner = MrPathV2.Memory.UnifiedMemory.Instance.RentNativeList<float3>(64, Allocator.Persistent);
            _tempPoints = _tempPointsOwner.Collection;
            _batchedLines = new Dictionary<LineType, List<LineSegment>>();
            
            InitializeDefaultStyles();
            InitializeBatchedLists();
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
            foreach (LineType type in System.Enum.GetValues(typeof(LineType)))
            {
                _batchedLines[type] = new List<LineSegment>();
            }
        }

        #endregion

        #region 公共API

        /// <summary>
        /// 设置当前渲染相机（用于视锥体剔除）
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
        /// 添加单条线段
        /// </summary>
        public void AddLine(Vector3 start, Vector3 end, LineType type, LineStyle? customStyle = null, int priority = 0)
        {
            var style = customStyle ?? GetDefaultStyle(type);
            
            _lineSegments.Add(new LineSegment
            {
                start = start,
                end = end,
                style = style,
                type = type,
                priority = priority
            });
            
            _isDirty = true;
        }

        /// <summary>
        /// 添加多段连续线条（如曲线）
        /// </summary>
        public void AddPolyLine(Vector3[] points, LineType type, LineStyle? customStyle = null, int priority = 0)
        {
            if (points == null || points.Length < 2) return;
            
            var style = customStyle ?? GetDefaultStyle(type);
            
            for (int i = 0; i < points.Length - 1; i++)
            {
                _lineSegments.Add(new LineSegment
                {
                    start = points[i],
                    end = points[i + 1],
                    style = style,
                    type = type,
                    priority = priority
                });
            }
            
            _isDirty = true;
        }

        /// <summary>
        /// 添加贝塞尔曲线
        /// </summary>
        public void AddBezierCurve(Vector3 start, Vector3 end, Vector3 control1, Vector3 control2, 
            LineType type, int resolution = 32, LineStyle? customStyle = null, int priority = 0)
        {
            var points = GenerateBezierPoints(start, end, control1, control2, resolution);
            AddPolyLine(points, type, customStyle, priority);
        }

        /// <summary>
        /// 添加Catmull-Rom样条曲线
        /// </summary>
        public void AddCatmullRomSpline(Vector3[] controlPoints, LineType type, int resolution = 16, 
            LineStyle? customStyle = null, int priority = 0)
        {
            if (controlPoints == null || controlPoints.Length < 4) return;
            
            var points = GenerateCatmullRomPoints(controlPoints, resolution);
            AddPolyLine(points, type, customStyle, priority);
        }

        /// <summary>
        /// 清除所有线条
        /// </summary>
        public void Clear()
        {
            _lineSegments.Clear();
            foreach (var batch in _batchedLines.Values)
            {
                batch.Clear();
            }
            _isDirty = true;
        }

        /// <summary>
        /// 清除指定类型的线条
        /// </summary>
        public void Clear(LineType type)
        {
            _lineSegments.RemoveAll(line => line.type == type);
            _batchedLines[type].Clear();
            _isDirty = true;
        }

        /// <summary>
        /// 渲染所有线条
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
            foreach (LineType type in System.Enum.GetValues(typeof(LineType)))
            {
                RenderBatch(type);
            }
#endif
        }

        /// <summary>
        /// 设置默认样式
        /// </summary>
        public void SetDefaultStyle(LineType type, LineStyle style)
        {
            _defaultStyles[type] = style;
        }

        /// <summary>
        /// 获取默认样式
        /// </summary>
        public LineStyle GetDefaultStyle(LineType type)
        {
            return _defaultStyles.TryGetValue(type, out var style) ? style : LineStyle.Default;
        }

        #endregion

        #region 私有方法

        private void UpdateBatches()
        {
            // 清空批次
            foreach (var batch in _batchedLines.Values)
            {
                batch.Clear();
            }
            
            // 按类型分组并排序
            foreach (var line in _lineSegments)
            {
                if (ShouldRenderLine(line))
                {
                    _batchedLines[line.type].Add(line);
                }
            }
            
            // 按优先级排序每个批次
            foreach (var batch in _batchedLines.Values)
            {
                batch.Sort((a, b) => a.priority.CompareTo(b.priority));
            }
        }

        private bool ShouldRenderLine(LineSegment line)
        {
            // 距离剔除
            if (_enableDistanceCulling && _currentCamera != null)
            {
                var center = (line.start + line.end) * 0.5f;
                var distance = Vector3.Distance(_currentCamera.transform.position, center);
                if (distance > _maxRenderDistance) return false;
            }
            
            // 视锥体剔除
            if (_enableFrustumCulling && _currentCamera != null && _frustumPlanes != null)
            {
                var bounds = new Bounds((line.start + line.end) * 0.5f, 
                    Vector3.one * Vector3.Distance(line.start, line.end));
                if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds)) return false;
            }
            
            return true;
        }

        private void RenderBatch(LineType type)
        {
#if UNITY_EDITOR
            var batch = _batchedLines[type];
            if (batch.Count == 0) return;
            
            foreach (var line in batch)
            {
                RenderSingleLine(line);
            }
#endif
        }

        private void RenderSingleLine(LineSegment line)
        {
#if UNITY_EDITOR
            var oldColor = Handles.color;
            Handles.color = line.style.color;
            
            try
            {
                if (line.style.dashed)
                {
                    Handles.DrawDottedLine(line.start, line.end, line.style.dashSize);
                }
                else if (line.style.antiAliased)
                {
                    Handles.DrawAAPolyLine(line.style.thickness, line.start, line.end);
                }
                else
                {
                    Handles.DrawLine(line.start, line.end);
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
            var points = new Vector3[resolution + 1];
            
            for (int i = 0; i <= resolution; i++)
            {
                float t = (float)i / resolution;
                points[i] = CalculateBezierPoint(start, control1, control2, end, t);
            }
            
            return points;
        }

        private Vector3 CalculateBezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;
            
            return uuu * p0 + 3 * uu * t * p1 + 3 * u * tt * p2 + ttt * p3;
        }

        private Vector3[] GenerateCatmullRomPoints(Vector3[] controlPoints, int resolution)
        {
            var points = new List<Vector3>();
            
            for (int i = 0; i < controlPoints.Length - 3; i++)
            {
                for (int j = 0; j < resolution; j++)
                {
                    float t = (float)j / resolution;
                    var point = CalculateCatmullRomPoint(
                        controlPoints[i], controlPoints[i + 1], 
                        controlPoints[i + 2], controlPoints[i + 3], t);
                    points.Add(point);
                }
            }
            
            // 添加最后一个点
            points.Add(controlPoints[controlPoints.Length - 2]);
            
            return points.ToArray();
        }

        private Vector3 CalculateCatmullRomPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float tt = t * t;
            float ttt = tt * t;
            
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * tt +
                (-p0 + 3f * p1 - 3f * p2 + p3) * ttt
            );
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _tempPointsOwner?.Dispose();
            
            _lineSegments?.Clear();
            
            foreach (var batch in _batchedLines.Values)
            {
                batch?.Clear();
            }
        }

        #endregion

        #region 性能配置

        /// <summary>
        /// 启用/禁用视锥体剔除
        /// </summary>
        public void SetFrustumCulling(bool enabled)
        {
            _enableFrustumCulling = enabled;
        }

        /// <summary>
        /// 启用/禁用距离剔除
        /// </summary>
        public void SetDistanceCulling(bool enabled, float maxDistance = 1000f)
        {
            _enableDistanceCulling = enabled;
            _maxRenderDistance = maxDistance;
        }

        /// <summary>
        /// 获取当前线条统计信息
        /// </summary>
        public (int total, int rendered) GetRenderStats()
        {
            int rendered = 0;
            foreach (var line in _lineSegments)
            {
                if (ShouldRenderLine(line)) rendered++;
            }
            
            return (_lineSegments.Count, rendered);
        }

        #endregion
    }
}