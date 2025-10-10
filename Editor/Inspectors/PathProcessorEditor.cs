// PathProcessorEditor.cs
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
namespace MrPathV2
{
    [CustomEditor(typeof(PathProcessor))]
    public class PathProcessorEditor : Editor
    {
        private bool _isApplyingHeight;
        private bool _isApplyingPaint;
        private IHeightProvider _heightProvider;

        private void OnEnable()
        {
            var settings = PathToolSettings.Instance;
            _heightProvider = (settings.heightProviderFactory != null) ? settings.heightProviderFactory.Create() : new TerrainHeightProviderAdapter(new TerrainHeightProvider());
        }

        private void OnDisable()
        {
            _isApplyingHeight = false;
            _isApplyingPaint = false;
            _heightProvider?.Dispose();
            _heightProvider = null;
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var creator = ((PathProcessor)target).GetComponent<PathCreator>();
            // ... 此处应有 GetValidationMessage 的验证逻辑 ...

            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                bool canExecute = !_isApplyingHeight && !_isApplyingPaint;

                using (new EditorGUI.DisabledScope(!canExecute))
                {
                    GUI.backgroundColor = _isApplyingHeight ? Color.yellow : new Color(0.5f, 0.8f, 1f);
                    string flattenText = _isApplyingHeight ? "正在压平..." : "1. 压平地形 (Flatten Terrain)";
                    if (GUILayout.Button(flattenText, GUILayout.Height(35)))
                    {
                        _ = ExecuteCommandAsync(new FlattenTerrainCommand(creator, _heightProvider), b => _isApplyingHeight = b);
                    }

                    GUI.backgroundColor = _isApplyingPaint ? Color.yellow : new Color(1f, 0.6f, 1f);
                    string paintText = _isApplyingPaint ? "正在绘制..." : "2. 绘制纹理 (Paint Textures)";
                    if (GUILayout.Button(paintText, GUILayout.Height(35)))
                    {
                        _ = ExecuteCommandAsync(new PaintTerrainCommand(creator, _heightProvider), b => _isApplyingPaint = b);
                    }
                }
            }
            GUI.backgroundColor = Color.white;
        }

        private async Task ExecuteCommandAsync(TerrainCommandBase command, System.Action<bool> setIsApplying)
        {
            setIsApplying(true);
            Repaint();
            // 統一策略：在命令執行前標記高度快取為髒，確保取樣使用最新資料
            _heightProvider?.MarkAsDirty();
            try
            {
                EditorUtility.DisplayProgressBar("应用路径到地形", $"正在执行: {command.GetCommandName()}...", 0.3f);
                await command.ExecuteAsync();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"执行 {command.GetCommandName()} 失败: {ex.Message}\n{ex.StackTrace}", target);
                EditorUtility.DisplayDialog("执行失败", $"操作 {command.GetCommandName()} 失败，详情请查看控制台日志。", "确定");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                setIsApplying(false);
                Repaint();
            }
        }
    }
}