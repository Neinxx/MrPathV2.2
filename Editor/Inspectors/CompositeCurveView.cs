using System;
using MrPathV2.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace MrPathV2.Editor.Inspectors
{
    /// <summary>
    ///     复合曲线视图：在一个视图中并排展示 CrossSection 与 FalloffShape。
    ///     - 左侧面板：CrossSection（域 [-1,1]，值范围随曲线）
    ///     - 右侧面板：FalloffShape（域 [0,1]，值 [0,1]）
    ///     不再包含 falloffWidth 的滑动杆，宽度由外部字段控制。
    /// </summary>
    public class CompositeCurveView : VisualElement
    {
        private const int SampleCount = 128;
        private const float HandleBaseRadius = 3f; // 更小的句柄半径
        private const float PickRadius = 3f; // 更小的拾取半径
        // 编辑与联动控制
        private bool _autoLinkStartTangent; // 起点切线自动适配 CrossSection 边缘（默认关闭）
        private bool _draggingFalloff;
        private int _dragKeyIndexFalloff = -1;
        // 删除 Align Right Edge，固定右缘对齐
        private bool _editFalloffInView = true; // 在视图上直接编辑 FalloffShape
        // 交互可见性与可点击性（缩小）
        private int _hoverKeyIndexFalloff = -1;
        private float _lastAppliedStartTangent = float.NaN;
        private bool _linkEndpoints; // 开启视图偏移对齐（默认关闭）
        private PathProfile _profile;
        // 新增：左侧编辑状态
        private bool _draggingCross;
        private int _dragKeyIndexCross = -1;
        private int _hoverKeyIndexCross = -1;
        // 新增：显示模式与范围控制
        private bool _fitPerPanel = true; // 每个面板自适应纵向范围以便编辑
        private bool _normalizedView; // 归一化显示比例信息（0..1）
        private float _edgeHeightForView; // 当前右缘高度（供归一化与绘制）
        private float _terrainBaseForView; // 当前基准高度（通常为 0）
        
        // --- UI Toolkit 新增：工具列與畫布 ---
        private readonly IMGUIContainer _canvas;
        
        public CompositeCurveView()
        {
            // 套用樣式（若存在同名 USS）
            AddToClassList("composite-curve-view");
            var uss = UIResourceLoader.LoadUss(typeof(CompositeCurveView));
            if (uss) styleSheets.Add(uss);

            // 建立 Toolbar
            var toolbar = new Toolbar
            {
                style =
                {
                    marginBottom = 4,
                    flexGrow = 1,
                    flexWrap = Wrap.Wrap // 讓按鈕自動換行，避免溢出
                }
            };

            var autoLinkStartTangentToggle = new ToolbarToggle
            {
                text = "Auto Tangent",
                value = _autoLinkStartTangent,
                tooltip = "起點切線自動匹配 CrossSection 右緣斜率"
            };
            var editFalloffToggle = new ToolbarToggle
            {
                text = "Edit Falloff",
                value = _editFalloffInView,
                tooltip = "允許直接在視圖中編輯 Falloff 曲線"
            };
            var linkEndpointsToggle = new ToolbarToggle
            {
                text = "Link Ends",
                value = _linkEndpoints,
                tooltip = "對齊 CrossSection 右緣與 Falloff 起點高度"
            };
            var fitPerPanelToggle = new ToolbarToggle
            {
                text = "Fit View",
                value = _fitPerPanel,
                tooltip = "每個面板自適應顯示範圍，便於編輯"
            };
            var normalizedViewToggle = new ToolbarToggle
            {
                text = "Normalize",
                value = _normalizedView,
                tooltip = "以 0..1 歸一化顯示（比較直觀）"
            };

            autoLinkStartTangentToggle.RegisterValueChangedCallback(evt => { _autoLinkStartTangent = evt.newValue; _canvas?.MarkDirtyRepaint(); });
            editFalloffToggle.RegisterValueChangedCallback(evt => { _editFalloffInView = evt.newValue; _canvas?.MarkDirtyRepaint(); });
            linkEndpointsToggle.RegisterValueChangedCallback(evt => { _linkEndpoints = evt.newValue; _canvas?.MarkDirtyRepaint(); });
            fitPerPanelToggle.RegisterValueChangedCallback(evt => { _fitPerPanel = evt.newValue; _canvas?.MarkDirtyRepaint(); });
            normalizedViewToggle.RegisterValueChangedCallback(evt => { _normalizedView = evt.newValue; _canvas?.MarkDirtyRepaint(); });

            toolbar.Add(autoLinkStartTangentToggle);
            toolbar.Add(editFalloffToggle);
            toolbar.Add(linkEndpointsToggle);
            toolbar.Add(fitPerPanelToggle);
            toolbar.Add(normalizedViewToggle);

            // 建立 IMGUI 畫布
            _canvas = new IMGUIContainer(OnCanvasGUI);
            _canvas.AddToClassList("composite-curve-canvas");
            _canvas.style.minHeight = 180;
            _canvas.style.marginTop = 4;

            // 佈局：垂直堆疊（Toolbar + Canvas）
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1;

            Add(toolbar);
            Add(_canvas);
        }

        public void SetProfile(PathProfile profile)
        {
            _profile = profile;
        }

        private void OnCanvasGUI()
        {
            if (!_profile) return;

            var rect = GUILayoutUtility.GetRect(10, 220, GUILayout.ExpandWidth(true));
            DrawBackground(rect);

            const float contentMargin = 8f;
            const float panelsSpacing = 8f;
            var contentHeight = rect.height - contentMargin * 2f;
            var panelHeight = Mathf.Floor((contentHeight - panelsSpacing) * 0.5f);

            var topRect = new Rect(rect.x + contentMargin, rect.y + contentMargin, rect.width - contentMargin * 2f, panelHeight);
            var bottomRect = new Rect(rect.x + contentMargin, topRect.yMax + panelsSpacing, rect.width - contentMargin * 2f, panelHeight);

            // 计算高度与范围
            var csRangeData = GetCrossSectionYRange();
            var edgeHeight = _profile.crossSection?.Evaluate(1f) ?? 0f;
            const float terrainBase = 0f;
            _edgeHeightForView = edgeHeight;
            _terrainBaseForView = terrainBase;

            Vector2 topRange;
            if (_normalizedView)
            {
                topRange = new Vector2(0f, 1f);
            }
            else
            {
                topRange = _fitPerPanel ? csRangeData : new Vector2(-1f, 1f);
            }
            var bottomRange = new Vector2(0f, 1f);

            // 上方 CrossSection
            DrawGrid(topRect, topRange);
            DrawCrossSection(topRect, topRange);
            DrawCrossSectionHandles(topRect, topRange);
            HandleCrossSectionEditing(topRect, topRange);

            // 下方 Falloff（按原始权重 0..1）
            DrawGrid(bottomRect, bottomRange);
            DrawFalloff(bottomRect, bottomRange);

            // 交互编辑 Falloff
            HandleFalloffEditing(bottomRect, bottomRange);

            // 接縫提示（改用各自面板的映射以準確對齊）
            DrawSeamEndpointMarkers(topRect, bottomRect, topRange, bottomRange);

            AutoLinkFalloffStartTangent();
            AutoLinkEndpointsValue();
        }

        private static void DrawBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 0.5f));
        }

        private Vector2 GetCrossSectionYRange()
        {
            // 动态估算可视范围：按采样值范围扩展 10%
            var min = float.MaxValue;
            var max = float.MinValue;
            var curve = _profile.crossSection;
            if (curve == null || curve.length == 0) return new Vector2(-1f, 1f);
            for (var i = 0; i <= SampleCount; i++)
            {
                var t = Mathf.Lerp(-1f, 1f, i / (float)SampleCount);
                var v = curve.Evaluate(t);
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
            var pad = Mathf.Max(0.01f, (max - min) * 0.1f);
            return new Vector2(min - pad, max + pad);
        }

        private static void DrawGrid(Rect rect, Vector2 yRange)
        {
            // 背景网格与坐标轴（移除顶部 Domain/Range 标签）
            Handles.color = new Color(1f, 1f, 1f, 0.06f);
            for (var i = 0; i <= 10; i++)
            {
                var x = Mathf.Lerp(rect.xMin, rect.xMax, i / 10f);
                Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));
            }
            for (var j = 0; j <= 6; j++)
            {
                var y = Mathf.Lerp(rect.yMin, rect.yMax, j / 6f);
                Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));
            }

            // 0 线与边界提示
            if (!(yRange.x < 0f) || !(yRange.y > 0f)) return;
            var y0 = Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, 0f));
            Handles.color = new Color(1f, 1f, 1f, 0.12f);
            Handles.DrawLine(new Vector3(rect.xMin, y0), new Vector3(rect.xMax, y0));
            // 移除 Domain/Range 标签输出
        }

        private void DrawCrossSection(Rect rect, Vector2 yRange)
        {
            var curve = _profile.crossSection;
            if (curve == null || curve.length == 0) return;

            Handles.color = new Color(0.35f, 0.78f, 1f, 0.95f);
            Vector3? last = null;
            for (var i = 0; i <= SampleCount; i++)
            {
                var x = Mathf.Lerp(-1f, 1f, i / (float)SampleCount);
                var raw = curve.Evaluate(x);
                var vView = _normalizedView ? Mathf.Clamp01(Mathf.InverseLerp(_terrainBaseForView, _edgeHeightForView, raw)) : Mathf.Clamp(raw, yRange.x, yRange.y);
                var px = Mathf.Lerp(rect.xMin, rect.xMax, (x + 1f) * 0.5f);
                var py = Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, vView));
                var p = new Vector3(px, py);
                if (last.HasValue) Handles.DrawLine(last.Value, p);
                last = p;
            }
        }

        private void DrawFalloff(Rect rect, Vector2 yRange)
{
    var curve = _profile.falloffShape;
    if (curve == null || curve.length == 0) return;

    Handles.color = new Color(1f, 0.65f, 0.25f, 0.95f);
    Vector3? last = null;
    for (var i = 0; i <= SampleCount; i++)
    {
        var t = i / (float)SampleCount;
        var w = Mathf.Clamp01(curve.Evaluate(t));
        var vView = Mathf.Clamp01(w);
        var px = Mathf.Lerp(rect.xMin, rect.xMax, t);
        var py = Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, vView));
        var p = new Vector3(px, py);
        if (last.HasValue) Handles.DrawLine(last.Value, p);
        last = p;
    }

    Handles.color = new Color(1f, 1f, 1f, 0.18f);
    Handles.DrawLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));
    Handles.DrawLine(new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMax, rect.yMin));

    DrawFalloffHandles(rect, yRange);
}

        // 绘制 Falloff 的关键点圆形句柄，并对悬停/拖动进行高亮
        private void DrawFalloffHandles(Rect rect, Vector2 yRange)
{
    var fo = _profile.falloffShape;
    if (fo == null) return;

    var dpiScale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
    var baseRadius = HandleBaseRadius * dpiScale;

    for (var i = 0; i < fo.length; i++)
    {
        var k = fo[i];
        var px = Mathf.Lerp(rect.xMin, rect.xMax, k.time);
        var vView = Mathf.Clamp01(k.value);
         var py = Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, vView));

        var isEndpoint = k.time is <= 0.0005f or >= 0.9995f;
        if (isEndpoint) continue;
        var r = baseRadius * (i == _hoverKeyIndexFalloff || _draggingFalloff && i == _dragKeyIndexFalloff ? 1.3f : 1f);

        Handles.color = new Color(1f, 0.55f, 0.1f, 0.95f);
        Handles.DrawSolidDisc(new Vector3(px, py, 0f), Vector3.forward, r);
        Handles.color = new Color(0f, 0f, 0f, 0.9f);
        Handles.DrawWireDisc(new Vector3(px, py, 0f), Vector3.forward, r);
    }
}

        // 交互：直接编辑 FalloffShape（右侧面板）
        /// <summary>
        /// 处理Falloff编辑的主要方法
        /// </summary>
        private void HandleFalloffEditing(Rect rect, Vector2 yRange)
{
    if (!_editFalloffInView) return;
    var e = Event.current;
    if (e == null) return;

    var fo = _profile.falloffShape;
    if (fo == null) return;

    Func<float, float> toPixelYFromWeight = w =>
     {
         var v = Mathf.Clamp01(w);
         return Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, v));
     };

    var pickRadius = Mathf.Max(PickRadius, 5f) * Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);

    switch (e.type)
    {
        case EventType.MouseDown:
        {
            if (!rect.Contains(e.mousePosition)) return;
            var nearest = FindNearestKeyIndex(fo,
                t => Mathf.Lerp(rect.xMin, rect.xMax, t),
                toPixelYFromWeight,
                e.mousePosition,
                pickRadius);

            switch (e.button)
            {
                case 0 when nearest >= 0:
                    _draggingFalloff = true;
                    _dragKeyIndexFalloff = nearest;
                    e.Use();
                    return;
                case 0 when e.clickCount == 2:
                {
                    var t = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, e.mousePosition.x));
                    var w = Mathf.Clamp01(Mathf.InverseLerp(rect.yMax, rect.yMin, e.mousePosition.y));
                    if (t is < 0.02f or > 0.98f) return;
                    Undo.RecordObject(_profile, "Add Falloff Key");
                    fo.AddKey(new Keyframe(t, w));
                    EditorUtility.SetDirty(_profile);
                    MarkDirtyRepaint();
                    e.Use();
                    return;
                }
                case 1 when nearest >= 0:
                {
                    var k = fo[nearest];
                    if (k.time is > 0.0005f and < 0.9995f)
                    {
                        Undo.RecordObject(_profile, "Delete Falloff Key");
                        fo.RemoveKey(nearest);
                        EditorUtility.SetDirty(_profile);
                        MarkDirtyRepaint();
                    }
                    e.Use();
                    break;
                }
            }

            return;
        }
        case EventType.MouseDrag when _draggingFalloff:
        {
            var t = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, e.mousePosition.x));
            var w = Mathf.Clamp01(Mathf.InverseLerp(rect.yMax, rect.yMin, e.mousePosition.y));
            var k = fo[_dragKeyIndexFalloff];
            if (k.time is <= 0.0005f or >= 0.9995f)
            {
                _draggingFalloff = false;
                _dragKeyIndexFalloff = -1;
                return;
            }
            Undo.RecordObject(_profile, "Move Falloff Key");
            var newKey = new Keyframe(t, w) { inTangent = k.inTangent, outTangent = k.outTangent };
            fo.MoveKey(_dragKeyIndexFalloff, newKey);
            EditorUtility.SetDirty(_profile);
            MarkDirtyRepaint();
            e.Use();
            return;
        }
        case EventType.MouseUp when _draggingFalloff:
            _draggingFalloff = false;
            _dragKeyIndexFalloff = -1;
            e.Use();
            return;
        case EventType.MouseMove:
        {
            var nearest = FindNearestKeyIndex(fo,
                t => Mathf.Lerp(rect.xMin, rect.xMax, t),
                toPixelYFromWeight,
                e.mousePosition,
                pickRadius);
            if (nearest >= 0)
            {
                var kNearest = fo[nearest];
                if (kNearest.time <= 0.0005f || kNearest.time >= 0.9995f)
                    nearest = -1;
            }
            if (_hoverKeyIndexFalloff != nearest)
            {
                _hoverKeyIndexFalloff = nearest;
                MarkDirtyRepaint();
            }
            return;
        }
    }
}


        private static int FindNearestKeyIndex(AnimationCurve curve,
            Func<float, float> toPixelX, Func<float, float> toPixelY,
            Vector2 mouse, float maxDist)
        {
            var bestIdx = -1;
            var bestDist = float.MaxValue;
            for (var i = 0; i < curve.length; i++)
            {
                var k = curve[i];
                var px = toPixelX(k.time);
                var py = toPixelY(k.value);
                var d = Vector2.Distance(new Vector2(px, py), mouse);
                if (d < maxDist && d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private void AutoLinkFalloffStartTangent()
        {
            if (!_autoLinkStartTangent) return;
            var fo = _profile.falloffShape;
            var cs = _profile.crossSection;
            if (fo == null || fo.length == 0 || cs == null || cs.length == 0) return;

            // 找到起点 key（time ~ 0）
            var startIdx = -1;
            for (var i = 0; i < fo.length; i++)
            {
                if (Mathf.Abs(fo[i].time - 0f) <= 0.0005f)
                {
                    startIdx = i;
                    break;
                }
            }
            if (startIdx < 0) return;

            // 估算 CrossSection 在 x=1 处的斜率
            var eps = 0.01f;
            var v1 = cs.Evaluate(1f);
            var v0 = cs.Evaluate(1f - eps);
            var slopeCs = (v1 - v0) / eps; // 值/域

            // 映射为 Falloff 起点的切线（保证向下，限制范围）
            var desiredTangent = -Mathf.Max(0.02f, Mathf.Min(8f, Mathf.Abs(slopeCs)));
            if (!float.IsNaN(_lastAppliedStartTangent) && Mathf.Approximately(desiredTangent, _lastAppliedStartTangent)) return;

            var k = fo[startIdx];
            // 保持起点值不变（通常应为 1）
            var newKey = new Keyframe(k.time, k.value)
            {
                inTangent = desiredTangent,
                outTangent = desiredTangent
            };

            Undo.RecordObject(_profile, "Auto Link Falloff Start Tangent");
            fo.MoveKey(startIdx, newKey);
            EditorUtility.SetDirty(_profile);
            _lastAppliedStartTangent = desiredTangent;
        }

        private void DrawSeamEndpointMarkers(Rect leftRect, Rect rightRect, Vector2 topRange, Vector2 bottomRange)
        {
            // 以各自面板的實際映射來標記端點，確保視覺對齊
            if (!_profile) return;
            var cs = _profile.crossSection;
            var fo = _profile.falloffShape;
            if (cs == null || cs.length == 0 || fo == null || fo.length == 0) return;

            // CrossSection 右緣（x=1）的像素 Y
            var csValRight = cs.Evaluate(1f);
            var csView = _normalizedView
                ? Mathf.Clamp01(Mathf.InverseLerp(_terrainBaseForView, _edgeHeightForView, csValRight))
                : Mathf.Clamp(csValRight, topRange.x, topRange.y);
            var leftY = Mathf.Lerp(leftRect.yMax, leftRect.yMin, Mathf.InverseLerp(topRange.x, topRange.y, csView));

            // Falloff 起點（t=0）的像素 Y（以權重 0..1 顯示）
            var foStartW = Mathf.Clamp01(fo.Evaluate(0f));
            var foView = Mathf.Clamp01(foStartW);
            var rightY = Mathf.Lerp(rightRect.yMax, rightRect.yMin, Mathf.InverseLerp(bottomRange.x, bottomRange.y, foView));

            var pxLeft = leftRect.xMax;
            var pxRight = rightRect.xMin;

            Handles.color = new Color(1f, 1f, 1f, 0.25f);
            Handles.DrawLine(new Vector3(pxLeft, leftY), new Vector3(pxRight, rightY));

            var r = HandleBaseRadius * Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint) * 1.2f;
            Handles.color = new Color(0.9f, 0.9f, 0.9f, 0.75f);
            Handles.DrawSolidDisc(new Vector3(pxLeft, leftY, 0f), Vector3.forward, r);
            Handles.DrawSolidDisc(new Vector3(pxRight, rightY, 0f), Vector3.forward, r);
            Handles.color = new Color(0f, 0f, 0f, 0.9f);
            Handles.DrawWireDisc(new Vector3(pxLeft, leftY, 0f), Vector3.forward, r);
            Handles.DrawWireDisc(new Vector3(pxRight, rightY, 0f), Vector3.forward, r);
        }

        private void AutoLinkEndpointsValue()
        {
            if (!_linkEndpoints) return;
            var fo = _profile.falloffShape;
            if (fo == null || fo.length == 0) return;

            // 端点权重应为 1，保证接缝高度一致
            var startIdx = -1;
            for (var i = 0; i < fo.length; i++)
            {
                if (Mathf.Abs(fo[i].time - 0f) <= 0.0005f) { startIdx = i; break; }
            }
            if (startIdx < 0) return;

            var k = fo[startIdx];
            var desiredStartWeight = 1f;
            if (Mathf.Approximately(k.value, desiredStartWeight)) return;

            Undo.RecordObject(_profile, "Link Endpoints Value");
            var newKey = new Keyframe(k.time, desiredStartWeight)
            {
                inTangent = k.inTangent,
                outTangent = k.outTangent
            };
            fo.MoveKey(startIdx, newKey);
            EditorUtility.SetDirty(_profile);
            MarkDirtyRepaint();
        }



// 新增：左侧 CrossSection 关键点句柄绘制（跳过端点）
        private void DrawCrossSectionHandles(Rect rect, Vector2 yRange)
        {
            var cs = _profile?.crossSection;
            if (cs == null || cs.length == 0) return;

            var dpiScale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            var baseRadius = HandleBaseRadius * dpiScale;

            for (var i = 0; i < cs.length; i++)
            {
                var k = cs[i];
                var isEndpoint = Mathf.Abs(k.time - (-1f)) <= 0.0005f || Mathf.Abs(k.time - 1f) <= 0.0005f;
                if (isEndpoint) continue; // 端点不可拖动，不绘制

                var px = Mathf.Lerp(rect.xMin, rect.xMax, (k.time + 1f) * 0.5f);
                var v = Mathf.Clamp(k.value, yRange.x, yRange.y);
                var py = Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, v));
                var r = baseRadius * ((_draggingCross && i == _dragKeyIndexCross) || i == _hoverKeyIndexCross ? 1.3f : 1f);

                Handles.color = new Color(0.35f, 0.78f, 1f, 0.95f);
                Handles.DrawSolidDisc(new Vector3(px, py, 0f), Vector3.forward, r);
                Handles.color = new Color(0f, 0f, 0f, 0.9f);
                Handles.DrawWireDisc(new Vector3(px, py, 0f), Vector3.forward, r);
            }
        }
        // Falloff 在上下布局中按原始权重展示时的范围
        // 使用 0..1 的标准范围，适配像素映射统一处理
        // 新增：左侧 CrossSection 交互编辑（添加/删除/拖动非端点）
        private void HandleCrossSectionEditing(Rect rect, Vector2 yRange)
        {
            var e = Event.current;
            var cs = _profile?.crossSection;
            if (e == null || cs == null) return;

            // 映射函数
            Func<float, float> toPixelX = t => Mathf.Lerp(rect.xMin, rect.xMax, (t + 1f) * 0.5f);
            Func<float, float> toPixelY = v =>
            {
                var vAdj = Mathf.Clamp(v, yRange.x, yRange.y);
                return Mathf.Lerp(rect.yMax, rect.yMin, Mathf.InverseLerp(yRange.x, yRange.y, vAdj));
            };

            var pickRadius = Mathf.Max(PickRadius, 10f) * Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);

            switch (e.type)
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                {
                    var nearest = FindNearestKeyIndex(cs, toPixelX, toPixelY, e.mousePosition, pickRadius);
                    // 忽略端点
                    if (nearest >= 0)
                    {
                        var kNearest = cs[nearest];
                        if (Mathf.Abs(kNearest.time - (-1f)) <= 0.0005f || Mathf.Abs(kNearest.time - 1f) <= 0.0005f)
                            nearest = -1;
                    }
                    if (e.button == 0 && nearest >= 0)
                    {
                        _draggingCross = true;
                        _dragKeyIndexCross = nearest;
                        e.Use();
                    }
                    else if (e.button == 0)
                    {
                        if (e.clickCount == 2)
                        {
                            var t = Mathf.Clamp(Time(e.mousePosition.x), -1f, 1f);
                            var v = Value(e.mousePosition.y);
                            // 避免在端点附近添加
                            if (Mathf.Abs(t - (-1f)) <= 0.02f || Mathf.Abs(t - 1f) <= 0.02f) return;
                            Undo.RecordObject(_profile, "Add CrossSection Key");
                            cs.AddKey(new Keyframe(t, v));
                            EditorUtility.SetDirty(_profile);
                            MarkDirtyRepaint();
                            e.Use();
                        }
                    }
                    else if (e.button == 1 && nearest >= 0)
                    {
                        var k = cs[nearest];
                        if (Mathf.Abs(k.time - (-1f)) > 0.0005f && Mathf.Abs(k.time - 1f) > 0.0005f)
                        {
                            Undo.RecordObject(_profile, "Delete CrossSection Key");
                            cs.RemoveKey(nearest);
                            EditorUtility.SetDirty(_profile);
                            MarkDirtyRepaint();
                        }
                        e.Use();
                    }
                    break;
                }
                case EventType.MouseDrag when _draggingCross:
                {
                    var t = Mathf.Clamp(Time(e.mousePosition.x), -1f, 1f);
                    var v = Value(e.mousePosition.y);
                    var k = cs[_dragKeyIndexCross];
                    // 端点不允许拖动
                    if (Mathf.Abs(k.time - (-1f)) <= 0.0005f || Mathf.Abs(k.time - 1f) <= 0.0005f)
                    {
                        _draggingCross = false;
                        _dragKeyIndexCross = -1;
                        return;
                    }
                    Undo.RecordObject(_profile, "Move CrossSection Key");
                    var newKey = new Keyframe(t, v)
                    {
                        inTangent = k.inTangent,
                        outTangent = k.outTangent
                    };
                    cs.MoveKey(_dragKeyIndexCross, newKey);
                    EditorUtility.SetDirty(_profile);
                    MarkDirtyRepaint();
                    e.Use();
                    break;
                }
                case EventType.MouseUp when _draggingCross:
                    _draggingCross = false;
                    _dragKeyIndexCross = -1;
                    e.Use();
                    break;
                case EventType.MouseMove:
                {
                    var nearest = FindNearestKeyIndex(cs, toPixelX, toPixelY, e.mousePosition, pickRadius);
                    if (nearest >= 0)
                    {
                        var kNearest = cs[nearest];
                        if (Mathf.Abs(kNearest.time - (-1f)) <= 0.0005f || Mathf.Abs(kNearest.time - 1f) <= 0.0005f)
                            nearest = -1;
                    }
                    if (_hoverKeyIndexCross != nearest)
                    {
                        _hoverKeyIndexCross = nearest;
                        MarkDirtyRepaint();
                    }
                    break;
                }
            }
            return;

            float Value(float py) => Mathf.Lerp(yRange.x, yRange.y, Mathf.InverseLerp(rect.yMax, rect.yMin, py));

            float Time(float px) => Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(rect.xMin, rect.xMax, px));
        }
    }
}
