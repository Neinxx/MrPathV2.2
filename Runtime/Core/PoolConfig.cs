using UnityEngine;
using UnityEngine.Serialization;

namespace MrPathV2.Runtime.Core
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "MrPathV2/Pool Config", order = 0)]
    public class PoolConfig : ScriptableObject
    {
        [FormerlySerializedAs("_maxPoolSize")]
        [SerializeField]
        private int maxPoolSize = 64;
        [FormerlySerializedAs("_minArraySize")]
        [SerializeField]
        private int minArraySize = 16;
        [FormerlySerializedAs("_maxArraySize")]
        [SerializeField]
        private int maxArraySize = 1024 * 1024;
        [FormerlySerializedAs("_clearOnReturn")]
        [SerializeField]
        private bool clearOnReturn = true;

        public int MaxPoolSize => maxPoolSize;
        public int MinArraySize => minArraySize;
        public int MaxArraySize => maxArraySize;
        public bool ClearOnReturn => clearOnReturn;
    }

    public interface ITrimable
    {
        void Trim(int maxAgeInSeconds);
    }
}
