using UnityEditor;
using UnityEngine;

namespace __temp.MrPathV2.Editor.Tests
{
    /// <summary>
    /// UI闪烁测试脚本 - 验证PathCreator选中时的UI稳定性
    /// </summary>
    public class UIFlickerTest : EditorWindow
    {
        private Runtime.Core.PathCreator[] _testPathCreators;
        private int _currentTestIndex = 0;
        private bool _isTestRunning = false;
        private int _selectionCount = 0;
        private const int MAX_SELECTIONS = 10;
        
        [MenuItem("PathCreator/Tests/UI Flicker Test")]
        public static void ShowWindow()
        {
            GetWindow<UIFlickerTest>("UI Flicker Test");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PathCreator UI闪烁测试", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (_testPathCreators == null)
            {
                _testPathCreators = FindObjectsOfType<Runtime.Core.PathCreator>();
            }

            EditorGUILayout.LabelField($"找到 {_testPathCreators?.Length ?? 0} 个PathCreator对象");
            
            if (_testPathCreators == null || _testPathCreators.Length == 0)
            {
                EditorGUILayout.HelpBox("场景中没有找到PathCreator对象。请先创建一些PathCreator对象进行测试。", MessageType.Warning);
                if (GUILayout.Button("刷新搜索"))
                {
                    _testPathCreators = FindObjectsOfType<Runtime.Core.PathCreator>();
                }
                return;
            }

            EditorGUILayout.Space();
            
            if (!_isTestRunning)
            {
                if (GUILayout.Button("开始UI闪烁测试"))
                {
                    StartFlickerTest();
                }
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("手动测试:", EditorStyles.boldLabel);
                
                for (int i = 0; i < _testPathCreators.Length; i++)
                {
                    var pathCreator = _testPathCreators[i];
                    if (pathCreator == null) continue;
                    
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"PathCreator {i + 1}: {pathCreator.name}");
                    
                    if (GUILayout.Button("选中", GUILayout.Width(60)))
                    {
                        Selection.activeGameObject = pathCreator.gameObject;
                        EditorGUIUtility.PingObject(pathCreator.gameObject);
                    }
                    
                    if (GUILayout.Button("取消选中", GUILayout.Width(80)))
                    {
                        Selection.activeGameObject = null;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.LabelField($"正在进行自动测试... ({_selectionCount}/{MAX_SELECTIONS})");
                EditorGUILayout.LabelField($"当前测试对象: {_testPathCreators[_currentTestIndex].name}");
                
                if (GUILayout.Button("停止测试"))
                {
                    StopFlickerTest();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "测试说明:\n" +
                "1. 自动测试会快速切换选中不同的PathCreator对象\n" +
                "2. 观察Inspector面板是否有闪烁现象\n" +
                "3. 手动测试可以单独测试每个PathCreator对象\n" +
                "4. 如果UI没有闪烁，说明修复成功", 
                MessageType.Info);
        }

        private void StartFlickerTest()
        {
            _isTestRunning = true;
            _currentTestIndex = 0;
            _selectionCount = 0;
            
            // 开始协程式的测试
            EditorApplication.update += UpdateFlickerTest;
        }

        private void StopFlickerTest()
        {
            _isTestRunning = false;
            EditorApplication.update -= UpdateFlickerTest;
            Selection.activeGameObject = null;

            Debug.Log($"[UIFlickerTest] 测试完成，共进行了 {_selectionCount} 次选择操作");
        }

        private void UpdateFlickerTest()
        {
            if (!_isTestRunning || _testPathCreators == null || _testPathCreators.Length == 0)
            {
                StopFlickerTest();
                return;
            }

            if (_selectionCount >= MAX_SELECTIONS)
            {
                StopFlickerTest();
                return;
            }

            // 每隔一定时间切换选中对象
            if (EditorApplication.timeSinceStartup % 0.5f < 0.1f) // 大约每0.5秒切换一次
            {
                var pathCreator = _testPathCreators[_currentTestIndex];
                if (pathCreator != null)
                {
                    Selection.activeGameObject = pathCreator.gameObject;
                    Debug.Log($"[UIFlickerTest] 选中: {pathCreator.name}");
                }

                _currentTestIndex = (_currentTestIndex + 1) % _testPathCreators.Length;
                _selectionCount++;
                
                Repaint();
            }
        }

        private void OnDestroy()
        {
            StopFlickerTest();
        }
    }
}
