// PathEditorHandles.cs

#if UNITY_EDITOR
using MrPathV2.Runtime.Interfaces;
using MrPathV2.Runtime.Preview;
using MrPathV2.Runtime.Settings;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    public static class PathEditorHandles
    {
        public static void Draw(ref HandleDrawContext context)
        {
            var creator = context.Creator;
            if (creator == null || creator.profile == null || creator.pathData.KnotCount == 0) return;

            var camera = SceneView.currentDrawingSceneView.camera;
            if (camera == null) return;

            var strategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
            if (strategy == null) return;

            UpdateHoverState(ref context, strategy);
            strategy.DrawHandles(ref context);
            DrawInsertionPreviewHandle(ref context, camera, strategy.drawingStyle);
        }

        public static void DrawHandle(Vector3 localPos, int flatIndex, HandleStyle style, ref HandleDrawContext context, Camera camera)
        {
            var creator = context.Creator;
            var worldPos = creator.transform.TransformPoint(localPos);

            var isHovered = flatIndex == context.HoveredPointIndex;

            var currentStrategy = PathStrategyRegistry.Instance.GetStrategy(creator.profile.curveType);
            if (!currentStrategy || currentStrategy.drawingStyle == null)
            {
                Handles.color = Color.red;
                Handles.SphereHandleCap(0, worldPos, Quaternion.identity, HandleUtility.GetHandleSize(worldPos) * 0.1f, EventType.Repaint);
                return;
            }
            var hoverStyle = currentStrategy.drawingStyle.hoverStyle;

            var finalStyle = isHovered ? hoverStyle : style;
            var size = isHovered ? finalStyle.size * 1.2f : finalStyle.size;

            var handleSize = HandleUtility.GetHandleSize(worldPos);
            Handles.color = finalStyle.fillColor;
            Handles.DrawSolidDisc(worldPos, camera.transform.forward, handleSize * size);
            Handles.color = finalStyle.borderColor;
            Handles.DrawWireDisc(worldPos, camera.transform.forward, handleSize * size, 2f);

            Handles.color = Color.clear;
            EditorGUI.BeginChangeCheck();
            var newWorldPos = Handles.FreeMoveHandle(worldPos, Quaternion.identity, handleSize * size * 1.2f, Vector3.zero, Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(creator, "Move Path Point");

                var finalPos = newWorldPos;
                if (creator.profile && creator.profile.snapToTerrain && context.HeightProvider != null)
                {
                    finalPos.y = context.HeightProvider.GetHeight(finalPos);
                }

                creator.ExecuteCommand(new MovePointCommand(flatIndex, finalPos));
            }
        }

        public struct HandleDrawContext
        {
            public PathCreator Creator;
            public IHeightProvider HeightProvider;
            public PathSpine? LatestSpine;
            public bool IsDragging;
            public int HoveredPointIndex;
            public int HoveredSegmentIndex;
            public float HoveredPathT;
            public PreviewLineRenderer LineRenderer;
            public float PreviewMaxPixelStep;
            public bool UseSplinesStyle;
            public bool DrawActiveSegmentOnly;
            public int DragNeighborRange;
            public bool IsCameraMoving;
            public AdaptivePolylineCache PolylineCache;
        }

        private static void UpdateHoverState(ref HandleDrawContext context, PathStrategy strategy)
        {
            if (context.IsDragging) return;
            context.HoveredPointIndex = -1;
            strategy.UpdatePointHover(ref context);
            if (context.HoveredPointIndex == -1)
            {
                UpdatePathHover(ref context);
            }
            else
            {
                context.HoveredSegmentIndex = -1;
                context.HoveredPathT = -1;
            }
        }

        private static void UpdatePathHover(ref HandleDrawContext context)
        {
            var creator = context.Creator;
            if (creator == null) return;

            context.HoveredSegmentIndex = -1;
            context.HoveredPathT = -1;

            var resolution = (context.IsDragging || context.IsCameraMoving) ? 12 : 40;
            const float pickThreshold = 12f;
            var pickThresholdSqr = pickThreshold * pickThreshold;

            var currentEvent = Event.current;
            if (currentEvent == null) return;
            var mousePos = currentEvent.mousePosition;

            int startSeg = 0, endSeg = creator.NumSegments;
            if (context.DrawActiveSegmentOnly && context.HoveredSegmentIndex >= 0)
            {
                startSeg = Mathf.Max(0, context.HoveredSegmentIndex - context.DragNeighborRange);
                endSeg = Mathf.Min(creator.NumSegments, context.HoveredSegmentIndex + context.DragNeighborRange + 1);
            }

            for (var seg = startSeg; seg < endSeg; seg++)
            {
                for (var j = 0; j < resolution; j++)
                {
                    var t = seg + (float)j / resolution;
                    var worldPoint = creator.GetPointAt(t);
                    var guiPoint = HandleUtility.WorldToGUIPoint(worldPoint);

                    if ((guiPoint - mousePos).sqrMagnitude < pickThresholdSqr)
                    {
                        context.HoveredSegmentIndex = seg;
                        context.HoveredPathT = t;
                        return;
                    }
                }
            }
        }

        private static void DrawInsertionPreviewHandle(ref HandleDrawContext context, Camera camera, PathDrawingStyle style)
        {
            var e = Event.current;
            if (e.shift && !e.control && context.HoveredPathT > -1)
            {
                var previewPos = context.Creator.GetPointAt(context.HoveredPathT);
                var handleSize = HandleUtility.GetHandleSize(previewPos);
                var previewStyle = style.insertionPreviewStyle;

                Handles.color = previewStyle.fillColor;
                Handles.DrawSolidDisc(previewPos, camera.transform.forward, handleSize * previewStyle.size);
                Handles.color = previewStyle.borderColor;
                Handles.DrawWireDisc(previewPos, camera.transform.forward, handleSize * previewStyle.size, 1.5f);
            }
        }
    }
}
#endif
