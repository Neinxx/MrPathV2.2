using UnityEditor.UIElements;
using UnityEngine.UIElements;

// 为 SliderInt 派生 UxmlFactory，让 UXML 能识别并创建它
public class SliderIntUxmlFactory : UxmlFactory<SliderInt, SliderIntUxmlTraits> {}

// 可选：定义 UXML 特性（如 min-value、max-value 的解析）
public class SliderIntUxmlTraits : VisualElement.UxmlTraits
{
    // 定义 UXML 中的 min-value 属性
    private UxmlIntAttributeDescription m_MinValue = new UxmlIntAttributeDescription { name = "min-value" };
    // 定义 UXML 中的 max-value 属性
    private UxmlIntAttributeDescription m_MaxValue = new UxmlIntAttributeDescription { name = "max-value" };

    public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
    {
        base.Init(ve, bag, cc);
        SliderInt sliderInt = ve as SliderInt;
        if (sliderInt != null)
        {
            // 从 UXML 属性中读取 min/max 值并设置到 SliderInt
            sliderInt.lowValue = m_MinValue.GetValueFromBag(bag, cc);
            sliderInt.highValue = m_MaxValue.GetValueFromBag(bag, cc);
        }
    }
}