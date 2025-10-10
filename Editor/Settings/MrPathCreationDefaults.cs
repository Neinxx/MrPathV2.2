// 文件路径: neinxx/mrpathv2.2/MrPathV2.2-2.31/Editor/Settings/MrPathCreationDefaults.cs
using UnityEngine;

namespace MrPathV2
{
    /// <summary>
    /// 专门负责新路径创建时的所有默认值。
    /// </summary>
    public class MrPathCreationDefaults : ScriptableObject
    {
        [Header("新对象创建设置")]
        [Tooltip("新创建路径对象的默认名称")]
        public string defaultObjectName = "New Path";

        [Tooltip("新创建路径的初始线段长度（单位：米）")]
        [Min(0.1f)]
        public float defaultLineLength = 10f;
    }
}