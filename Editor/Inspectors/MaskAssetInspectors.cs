// using UnityEditor intentionally avoided to prevent namespace collision with MrPathV2.Editor
using MrPathV2.Runtime.Core.BlendMasks;
using MrPathV2.Runtime.Core.BlendMasks.Composition;

namespace MrPathV2.Editor.Inspectors
{
    [UnityEditor.CustomEditor(typeof(NoiseMask))]
    public class NoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                var mask = (NoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [UnityEditor.CustomEditor(typeof(ShoulderPerlinNoiseMask))]
    public class ShoulderPerlinNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                var mask = (ShoulderPerlinNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [UnityEditor.CustomEditor(typeof(EdgePerlinNoiseMask))]
    public class EdgePerlinNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                var mask = (EdgePerlinNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [UnityEditor.CustomEditor(typeof(StripeNoiseMask))]
    public class StripeNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                var mask = (StripeNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [UnityEditor.CustomEditor(typeof(WorleyNoiseMask))]
    public class WorleyNoiseMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                var mask = (WorleyNoiseMask)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }

    [UnityEditor.CustomEditor(typeof(ComposedMaskSO))]
    public class ComposedMaskInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUI.BeginChangeCheck();
            var so = serializedObject;
            so.Update();
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("region"));
            var regionObj = ((ComposedMaskSO)target).region;
            if (regionObj != null)
            {
                var regEd = UnityEditor.Editor.CreateEditor(regionObj);
                regEd.OnInspectorGUI();
            }
            UnityEditor.EditorGUILayout.Space();
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("noise"));
            var noiseObj = ((ComposedMaskSO)target).noise;
            if (noiseObj != null)
            {
                var nEd = UnityEditor.Editor.CreateEditor(noiseObj);
                nEd.OnInspectorGUI();
            }
            UnityEditor.EditorGUILayout.Space();
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("noiseScale"));
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("uniformScale"));
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("rotationDeg"));
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("modulators"), true);
            UnityEditor.EditorGUILayout.Space();
            UnityEditor.EditorGUILayout.PropertyField(so.FindProperty("specials"), true);
            var specials = ((ComposedMaskSO)target).specials;
            if (specials != null)
            {
                for (int i = 0; i < specials.Length; i++)
                {
                    var v = specials[i].variant;
                    if (v != null)
                    {
                        var sEd = UnityEditor.Editor.CreateEditor(v);
                        sEd.OnInspectorGUI();
                    }
                }
            }
            so.ApplyModifiedProperties();
            if (UnityEditor.EditorGUI.EndChangeCheck())
            {
                var mask = (ComposedMaskSO)target;
                MaskChangeEvents.RaiseChanged(mask);
            }
        }
    }
}
