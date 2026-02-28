using UnityEngine;

namespace MrPathV2
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "MrPathV2/Pool Config", order = 0)]
    public class PoolConfig : ScriptableObject
    {
        [SerializeField]
        private int _maxPoolSize = 64;
        [SerializeField]
        private int _minArraySize = 16;
        [SerializeField]
        private int _maxArraySize = 1024 * 1024;
        [SerializeField]
        private bool _clearOnReturn = true;

        public int MaxPoolSize => _maxPoolSize;
        public int MinArraySize => _minArraySize;
        public int MaxArraySize => _maxArraySize;
        public bool ClearOnReturn => _clearOnReturn;
    }

    public interface ITrimable
    {
        void Trim(int maxAgeInSeconds);
    }
}