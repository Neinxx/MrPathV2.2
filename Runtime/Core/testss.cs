using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;


public class testss : EditorWindow
{
   [MenuItem("Window/My Reorderable Window")]
    public static void ShowWindow()
    {
        GetWindow<testss>("Reorderable List");
         Debug.Log("visualTree");
    }

    public void CreateGUI()
    {
        var root = rootVisualElement;
        var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/__temp/MrPathV2/Runtime/Core/StylizedRoadRecipe.uxml");
       
        VisualElement labelFromUXML = visualTree.Instantiate();
        root.Add(labelFromUXML);
    }
}