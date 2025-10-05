using MrPathV2.Commands;
using UnityEditor;
using UnityEngine;

[CustomEditor (typeof (PathProcessor))]
public class PathProcessorEditor : Editor
{
    public override void OnInspectorGUI ()
    {
        base.OnInspectorGUI ();

        if (GUILayout.Button ("Apply Path to World", GUILayout.Height (40)))
        {
            var processor = (PathProcessor) target;
            var creator = processor.GetComponent<PathCreator> ();

            // 创建命令对象
            var command = new ApplyPathToTerrainCommand (creator);

            // 执行命令
            command.ExecuteAsync ().ConfigureAwait (false);
        }
    }
}
