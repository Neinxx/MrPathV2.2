using UnityEditor;
using UnityEngine;

/// <summary>
/// 【临时诊断法宝】为 PathCreator 提供一个自定义编辑器，
/// 用于添加一个“打印数据”的调试按钮。
/// </summary>
[CustomEditor (typeof (PathCreator))]
public class PathCreatorEditor : Editor
{
    public override void OnInspectorGUI ()
    {
        // 先绘制默认的 Inspector 界面
        DrawDefaultInspector ();

        // 获取当前正在编辑的 PathCreator 实例
        var creator = (PathCreator) target;

        // 添加一个醒目的诊断按钮
        EditorGUILayout.Space (10);
        GUI.backgroundColor = Color.red; // 让按钮变红，以示重要
        if (GUILayout.Button ("神识内窥 (Debug: Print Point Data)", GUILayout.Height (30)))
        {
            if (creator.Path == null)
            {
                Debug.LogError ("Path 数据 (Path) 为 null!");
            }
            else if (creator.Path.Points == null)
            {
                Debug.LogError ("点列表 (Path.Points) 为 null!");
            }
            else
            {
                Debug.Log ($"--- 正在检视 '{creator.name}' 的路径数据 ---");
                Debug.Log ($"共有点: {creator.NumPoints}");
                for (int i = 0; i < creator.NumPoints; i++)
                {
                    // 打印出每个点的局部坐标
                    Debug.Log ($"点 {i}: {creator.Path.Points[i]}");
                }
                Debug.Log ("--- 检视完毕 ---");
            }
        }
        GUI.backgroundColor = Color.white; // 恢复默认颜色
    }
}
