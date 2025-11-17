using UnityEditor;
using MrPathV2.Runtime.Core.BlendMasks;
using MrPathV2.Runtime.Core.BlendMasks.Composition;

namespace MrPathV2.Editor.Inspectors
{
    [CustomEditor(typeof(NoiseMask))]
    public class NoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var mask = (NoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [CustomEditor(typeof(ShoulderPerlinNoiseMask))]
    public class ShoulderPerlinNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var mask = (ShoulderPerlinNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [CustomEditor(typeof(EdgePerlinNoiseMask))]
    public class EdgePerlinNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var mask = (EdgePerlinNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [CustomEditor(typeof(StripeNoiseMask))]
    public class StripeNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var mask = (StripeNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [CustomEditor(typeof(WorleyNoiseMask))]
    public class WorleyNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var mask = (WorleyNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [CustomEditor(typeof(ComposedMaskSO))]
    public class ComposedMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var mask = (ComposedMaskSO)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }
}
