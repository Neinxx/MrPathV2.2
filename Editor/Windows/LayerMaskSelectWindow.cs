#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using __temp.MrPathV2.Runtime.Core;
using __temp.MrPathV2.Runtime.Core.BlendMasks;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace MrPathV2.Editor.Windows
{
    /// <summary>
    /// LayerMaskSelectWindow
    /// 顶部列表（可搜索滚动）+ 底部参数（可拖拽分割调整高度）。
    /// 选中即应用到 RoadLayer.layerMask；遵循提前返回与单一职责原则。
    /// </summary>
    public class LayerMaskSelectWindow : EditorWindow
    {
        private RoadLayer _targetLayer;
        private BlendMaskBase _selected;
        private UnityEditor.Editor _selectedEditor; // 参数区渲染

        private readonly List<BlendMaskBase> _masks = new();
        private readonly Dictionary<int, Texture2D> _iconCache = new();
        private Vector2 _listScroll;
        private Vector2 _paramScroll;
        private string _search = string.Empty;

        // 新建遮罩相关
        private Type[] _availableMaskTypes = Array.Empty<Type>();
        private int _createTypeIndex = 0;
        private string _newMaskName = "NewMask";
        private const string DefaultMaskFolder = "Assets/MrPathV2/Masks";

        // 可拖拽分割条
        private float _listTopHeight = 240f;
        private bool _resizing;
        private const float SplitterHeight = 6f;

        public static void Open(RoadLayer layer, BlendMaskBase current)
        {
            var win = GetWindow<LayerMaskSelectWindow>(true, "Layer Mask Select", true);
            win.minSize = new Vector2(520, 360);
            win.Initialize(layer, current);
            win.Show();
        }

        private void Initialize(RoadLayer layer, BlendMaskBase current)
        {
            _targetLayer = layer;
            _selected = current;
            _availableMaskTypes = FindAvailableMaskTypes();
            if (_availableMaskTypes == null || _availableMaskTypes.Length == 0)
                _availableMaskTypes = new[] { typeof(BlendMaskBase) };
            RebuildList();
            RecreateEditor();
        }

        private void OnDisable()
        {
            if (_selectedEditor)
            {
                DestroyImmediate(_selectedEditor);
                _selectedEditor = null;
            }
        }

        // 点击非窗口区域（失去焦点）自动关闭，符合 Unity 选择器交互
        private void OnLostFocus()
        {
            Close();
        }

        private void OnGUI()
        {
            DrawToolbar();

            var topHeight = Mathf.Clamp(_listTopHeight, 140f, position.height - 180f);
            EditorGUILayout.BeginVertical(GUILayout.Height(topHeight));
            DrawMaskList(topHeight);
            EditorGUILayout.EndVertical();

            DrawHeightSplitter();
            DrawParamsPanel();
            DrawFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            // 移除"遮罩列表"，只保留筛选；输入框水平铺满
            GUILayout.Label("筛选:", GUILayout.Width(40)); // 固定标签宽度
            var newSearch = GUILayout.TextField(_search, EditorStyles.toolbarTextField, GUILayout.ExpandWidth(true)); // 仅文本框扩展
            if (!string.Equals(newSearch, _search))
            {
                _search = newSearch?.Trim() ?? string.Empty;
                Repaint();
            }
            // GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RebuildList();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMaskList(float height)
        {
            if (_masks == null || _masks.Count == 0)
            {
                EditorGUILayout.HelpBox("项目中未找到任何 BlendMask 资产。", MessageType.Info);
                return; // 提前返回
            }

            var query = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim();
            var filtered = (query == null ? _masks.Where(m => m) : _masks.Where(m => m && m.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                            .OrderBy(m => m.name)
                            .ToList();
            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("筛选条件下无匹配遮罩。", MessageType.Info);
                return; // 提前返回
            }

            EditorGUILayout.BeginVertical(GUILayout.Height(height));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            foreach (var m in filtered) DrawMaskRow(m);
            EditorGUILayout.EndScrollView();

            // 底部“新建遮罩”区域（底对齐）
            EditorGUILayout.BeginHorizontal();
            var typeNames = _availableMaskTypes.Select(t => t.Name).ToArray();
            _createTypeIndex = EditorGUILayout.Popup(_createTypeIndex, typeNames, GUILayout.Width(140));
            _newMaskName = EditorGUILayout.TextField(_newMaskName, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("新建遮罩", GUILayout.Width(88)))
            {
                CreateNewMaskAsset(_availableMaskTypes[Mathf.Clamp(_createTypeIndex, 0, _availableMaskTypes.Length - 1)], _newMaskName);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawMaskRow(BlendMaskBase m)
        {
            var rowRect = EditorGUILayout.BeginHorizontal();
            Texture2D icon = null;
            var id = m.GetInstanceID();
            if (!_iconCache.TryGetValue(id, out icon) || !icon)
            {
                icon = AssetPreview.GetMiniThumbnail(m);
                _iconCache[id] = icon;
            }
            var isSelected = _selected == m;
            var isHover = rowRect.Contains(Event.current.mousePosition);
            var bg = isSelected ? new Color(0.2f, 0.6f, 0.2f, 0.15f) : (isHover ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.03f));
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, rowRect.height), bg);

            GUILayout.Space(4);
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            GUILayout.Label(m.name, EditorStyles.label);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 选中即应用
            var e = Event.current;
            if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
            {
                ApplySelection(m);
                e.Use();
            }
        }

        private void DrawParamsPanel()
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("遮罩参数", EditorStyles.boldLabel);
            if (!_selected)
            {
                GUILayout.Label("未选择遮罩", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return; // 提前返回
            }
            _paramScroll = EditorGUILayout.BeginScrollView(_paramScroll, GUILayout.ExpandHeight(true));
            if (_selectedEditor)
            {
                try { _selectedEditor.OnInspectorGUI(); }
                catch (Exception ex) { EditorGUILayout.HelpBox($"渲染遮罩 Inspector 失败: {ex.Message}", MessageType.Error); }
            }
            else
            {
                EditorGUILayout.HelpBox("Inspector 构建失败", MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            // if (GUILayout.Button("关闭", GUILayout.Width(80), GUILayout.Height(24))) Close();
            EditorGUILayout.EndHorizontal();
        }

        private void ApplySelection(BlendMaskBase m)
        {
            if (_targetLayer == null)
            {
                EditorUtility.DisplayDialog("提示", "目标 RoadLayer 为空，无法应用。", "确定");
                return; // 提前返回
            }
            _selected = m;
            _targetLayer.layerMask = m;
            RecreateEditor();
        }

        private void RebuildList()
        {
            _masks.Clear();
            _iconCache.Clear();
            var guids = AssetDatabase.FindAssets("t:BlendMaskBase");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<BlendMaskBase>(path);
                if (m)
                {
                    _masks.Add(m);
                    _iconCache[m.GetInstanceID()] = AssetPreview.GetMiniThumbnail(m);
                }
            }
            _masks.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        }

        private void RecreateEditor()
        {
            if (_selectedEditor) { DestroyImmediate(_selectedEditor); _selectedEditor = null; }
            if (_selected) _selectedEditor = UnityEditor.Editor.CreateEditor(_selected);
        }

        // --- 新建遮罩工具 ---
        private static Type[] FindAvailableMaskTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BlendMaskBase)))
                .ToArray();
        }

        private void CreateNewMaskAsset(Type maskType, string nameHint)
        {
            if (maskType == null || !typeof(BlendMaskBase).IsAssignableFrom(maskType))
            {
                EditorUtility.DisplayDialog("错误", "无效的遮罩类型。", "确定");
                return; // 提前返回
            }

            // 确保目标文件夹存在
            EnsureFolderExists(DefaultMaskFolder);
            var cleanName = string.IsNullOrWhiteSpace(nameHint) ? maskType.Name : nameHint.Trim();
            var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{DefaultMaskFolder}/{cleanName}.asset");

            var instance = ScriptableObject.CreateInstance(maskType) as BlendMaskBase;
            if (!instance)
            {
                EditorUtility.DisplayDialog("错误", "创建遮罩实例失败。", "确定");
                return; // 提前返回
            }

            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 更新列表并应用到目标图层
            _masks.Add(instance);
            _iconCache[instance.GetInstanceID()] = AssetPreview.GetMiniThumbnail(instance);
            ApplySelection(instance);
            Repaint();
        }

        private static void EnsureFolderExists(string fullPath)
        {
            // fullPath 形如 Assets/AAA/BBB
            var parts = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            var current = parts[0]; // 应为 Assets
            for (var i = 1; i < parts.Length; i++)
            {
                var next = parts[i];
                if (!AssetDatabase.IsValidFolder($"{current}/{next}"))
                {
                    AssetDatabase.CreateFolder(current, next);
                }
                current = $"{current}/{next}";
            }
        }

        private void DrawHeightSplitter()
        {
            var y = Mathf.Clamp(_listTopHeight, 140f, position.height - 180f);
            var splitterRect = new Rect(0, y, position.width, SplitterHeight);
            EditorGUI.DrawRect(splitterRect, new Color(0.2f, 0.2f, 0.2f, 0.35f));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (splitterRect.Contains(e.mousePosition)) { _resizing = true; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (_resizing)
                    {
                        _listTopHeight = Mathf.Clamp(e.mousePosition.y, 140f, position.height - 180f);
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_resizing) { _resizing = false; e.Use(); }
                    break;
            }
        }
    }
}
#endif
