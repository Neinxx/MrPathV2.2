using UnityEditor;

namespace MrPathV2
{
    public static class PathMenu
    {
        [MenuItem("GameObject/MrPath/Create Path", false, 10)]
        public static void CreatePathObject()
        {

            PathFactory.CreateDefaultPath();
        }
    }
}