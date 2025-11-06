// using System.Linq;
// using UnityEngine;
// using UnityEngine.UIElements;

// namespace __temp.MrPathV2.Editor.UI
// {
//     public class DropContainer : VisualElement
//     {
//         // --- UXML/工厂定义 ---
//         public new class UxmlFactory : UxmlFactory<DropContainer, UxmlTraits> { }
//         public new class UxmlTraits : VisualElement.UxmlTraits { }

//         // --- 实例字段 ---
//         private VisualElement m_MarginTarget;    // 当前被施加 margin 的元素
//         private int m_InsertionIndex = -1;       // 预定的插入索引
//         private bool m_IsMarginAbove = false;    // margin 是在上方还是下方

//         // 动画时长 (必须与 USS 中的 transition-duration 匹配!)
//         private const int c_TransitionTimeMs = 300;

//         // --- 构造函数 ---
//         public DropContainer()
//         {
//             AddToClassList("drag-container");
//         }

//         // --- 布局计算 (单一职责) ---

//         /// <summary>
//         /// 当拖动项在容器上移动时，由拖动项调用，计算并更新插入点视觉反馈。
//         /// </summary>
//         public void UpdateDropTarget(DraggableInsertItem draggedItem, Vector2 pointerPosition)
//         {
//             VisualElement newMarginTarget = null;
//             int newInsertionIndex = -1;
//             bool newIsMarginAbove = false;
//             float spaceSize = draggedItem.FullHeight;

//             // 1. 查找最接近的子项 (忽略自身)
//             var visibleChildren = this.Children().Where(c => c != draggedItem).ToList();

//             VisualElement closestChild = null;
//             float closestDist = float.MaxValue;

//             foreach (var child in visibleChildren)
//             {
//                 float dist = Mathf.Abs(pointerPosition.y - child.worldBound.center.y);
//                 if (dist < closestDist)
//                 {
//                     closestDist = dist;
//                     closestChild = child;
//                 }
//             }

//             // 2. 确定插入索引和目标
//             if (closestChild != null)
//             {
//                 // 在子项上方或下方
//                 float childMidY = closestChild.worldBound.yMin + closestChild.worldBound.height / 2f;
//                 if (pointerPosition.y < childMidY)
//                 {
//                     newInsertionIndex = IndexOf(closestChild);
//                     newMarginTarget = closestChild;
//                     newIsMarginAbove = true;
//                 }
//                 else
//                 {
//                     newInsertionIndex = IndexOf(closestChild) + 1;
//                     newMarginTarget = closestChild;
//                     newIsMarginAbove = false;
//                 }
//             }
//             else if (visibleChildren.Count > 0)
//             {
//                 // 容器内有元素，但鼠标在顶部或底部边缘
//                 var firstChild = visibleChildren[0];
//                 var lastChild = visibleChildren[visibleChildren.Count - 1];

//                 if (pointerPosition.y < firstChild.worldBound.yMin)
//                 {
//                     newInsertionIndex = 0;
//                     newMarginTarget = firstChild;
//                     newIsMarginAbove = true;
//                 }
//                 else if (pointerPosition.y > lastChild.worldBound.yMax)
//                 {
//                     newInsertionIndex = childCount;
//                     newMarginTarget = lastChild;
//                     newIsMarginAbove = false;
//                 }
//             }
//             else
//             {
//                 // 容器是空的
//                 newInsertionIndex = 0;
//                 newMarginTarget = null;
//             }

//             // 3. 应用或清除 margin 视觉反馈
//             ApplyOrClearMargin(newMarginTarget, newIsMarginAbove, newInsertionIndex, spaceSize);
//         }

//         private void ApplyOrClearMargin(VisualElement newMarginTarget, bool newIsMarginAbove, int newInsertionIndex, float spaceSize)
//         {
//             // 提前返回：插入点没有变化
//             if (m_MarginTarget == newMarginTarget && m_IsMarginAbove == newIsMarginAbove) return;

//             // 清除旧的 margin
//             ClearDropTarget();

//             // 应用新的 margin
//             m_MarginTarget = newMarginTarget;
//             m_IsMarginAbove = newIsMarginAbove;
//             m_InsertionIndex = newInsertionIndex;

//             if (m_MarginTarget != null)
//             {
//                 if (m_IsMarginAbove)
//                 {
//                     m_MarginTarget.style.marginTop = new Length(spaceSize, LengthUnit.Pixel);
//                 }
//                 else
//                 {
//                     m_MarginTarget.style.marginBottom = new Length(spaceSize, LengthUnit.Pixel);
//                 }
//                 // USS Transition 会自动平滑过渡
//             }
//         }

//         /// <summary>
//         /// 清除所有“撑开”的 margin。
//         /// </summary>
//         public void ClearDropTarget()
//         {
//             // 提前返回：没有 margin target
//             if (m_MarginTarget == null) return;

//             // 恢复 margin 为默认 (Null)
//             m_MarginTarget.style.marginTop = StyleKeyword.Null;
//             m_MarginTarget.style.marginBottom = StyleKeyword.Null;

//             m_MarginTarget = null;
//             m_InsertionIndex = -1;
//         }

//         // --- 放置与排序 (单一职责) ---

//         /// <summary>
//         /// 当拖动项被释放时调用，处理飞入动画和最终的 Hierarchy 排序。
//         /// </summary>
//         public bool HandleDrop(DraggableInsertItem droppedItem, float itemFullHeight)
//         {
//             // 提前返回：没有找到有效的插入点
//             if (m_InsertionIndex < 0) return false;

//             // 1. 计算 A 应该“飞”向的最终布局位置 (targetPos)
//             Vector2 targetLayoutPos;

//             if (m_MarginTarget != null)
//             {
//                 // 计算 B 元素在撑开 margin 后，A 应该对齐的布局Y坐标。
//                 if (m_IsMarginAbove)
//                 {
//                     // A 飞向 B 原始位置的 Y 坐标 (B 当前 layout.y - B.margin-top)
//                     float originalY = m_MarginTarget.layout.yMin - m_MarginTarget.resolvedStyle.marginTop;
//                     targetLayoutPos = new Vector2(m_MarginTarget.layout.xMin, originalY);
//                 }
//                 else
//                 {
//                     // A 飞向 B 原始底部的 Y 坐标 (B 当前 layout.yMax - B.margin-bottom)
//                     float originalY = m_MarginTarget.layout.yMax - m_MarginTarget.resolvedStyle.marginBottom;
//                     targetLayoutPos = new Vector2(m_MarginTarget.layout.xMin, originalY);
//                 }
//             }
//             else
//             {
//                 // 容器是空的，飞向容器的 content 顶部 (0,0)
//                 targetLayoutPos = Vector2.zero;
//             }

//             // 2. 命令 A “飞”过去 (动画)
//             // ⚠️ 注意：这要求 DraggableInsertItem 的 translate 坐标系与容器的 layout 坐标系一致
//             droppedItem.style.translate = new Translate(targetLayoutPos.x, targetLayoutPos.y, 0); 

//             // 3. 安排一个任务，在动画 *结束* 后，才真正修改 Hierarchy
//             droppedItem.schedule.Execute(() =>
//             {
//                 // 1. 清除 margin 动画 (通常在 HandleDrop 外清理，这里是保险)
//                 ClearDropTarget();

//                 // 2. 执行最终插入 (元素从未被 Remove)

//                 // 提前返回检查：修正 m_InsertionIndex 防止越界
//                 int maxValidIndex = childCount; 
//                 if (m_InsertionIndex < 0) m_InsertionIndex = 0;
//                 else if (m_InsertionIndex > maxValidIndex) m_InsertionIndex = maxValidIndex;

//                 // E. 执行插入 (移动元素在 Hierarchy 中的位置)
//                 this.Insert(m_InsertionIndex, droppedItem); 

//                 // 3. 重置 A 的样式 (恢复 display:flex, position:relative, translate:None)
//                 droppedItem.ResetStylesAfterDrop(); 

//             }).StartingIn(c_TransitionTimeMs);

//             return true;
//         }
//     }
// }