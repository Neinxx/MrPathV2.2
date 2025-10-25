using UnityEditor;
using UnityEngine;

namespace MrPathV2.Runtime.Core
{
    /// <summary>
    ///     演示如何使用PathCreator.GetPathLength()方法的示例脚本
    /// </summary>
    public class PathLengthUsageExample : MonoBehaviour
    {
        [Header("路径创建器引用")]
        public PathCreator Creator;

        [Header("路径长度信息")]
        [SerializeField] private float roadWorldLength;

        private void Start()
        {
            if (Creator != null)
            {
                UpdatePathLength();
            }
        }

        /// <summary>
        ///     在Scene视图中显示路径长度信息
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (Creator != null)
            {
                var length = Creator.GetPathLength();

                // 在路径起点显示长度信息
                if (Creator.NumPoints > 0)
                {
                    var startPos = Creator.GetPointAt(0f);

#if UNITY_EDITOR
                    Handles.Label(startPos + Vector3.up * 2f,
                        $"路径长度: {length:F2}m");
#endif
                }
            }
        }

        /// <summary>
        ///     在Inspector中显示路径长度
        /// </summary>
        private void OnValidate()
        {
            if (Creator != null && Application.isPlaying)
            {
                roadWorldLength = Creator.GetPathLength();
            }
        }

        /// <summary>
        ///     更新路径长度信息
        /// </summary>
        [ContextMenu("更新路径长度")]
        public void UpdatePathLength()
        {
            if (Creator == null)
            {
                Debug.LogWarning("Creator引用为空，请先分配PathCreator组件");
                return;
            }

            // 获取道路长度
            roadWorldLength = Creator.GetPathLength();

            Debug.Log($"道路总长度: {roadWorldLength:F2} 米");
        }
    }
}
