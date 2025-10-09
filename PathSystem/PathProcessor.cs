using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 【大师重构版】路径处理器组件。
    /// 负责路径的编辑和管理逻辑，配合PathCreator使用。
    /// </summary>
    [RequireComponent(typeof(PathCreator))]
    public class PathProcessor : MonoBehaviour
    {
        // 这个组件可能不需要很多字段，它的逻辑主要在Editor脚本中
    }
}