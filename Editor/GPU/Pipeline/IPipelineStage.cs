using System;
using UnityEngine;
using __temp.MrPathV2.Editor.GPU.Core;

namespace __temp.MrPathV2.Editor.GPU.Pipeline
{
    /// <summary>
    /// 管线阶段接口（单一职责）：每个阶段只处理自身逻辑并暴露明确的输入输出。
    /// 现代风格：采用提前返回与防御式编程，避免隐藏副作用。
    /// </summary>
    public interface IPipelineStage<in TIn, out TOut>
    {
        /// <summary>
        /// 执行阶段逻辑。
        /// 必须在失败时提前返回并记录错误，不得抛出未处理异常。
        /// </summary>
        /// <param name="input">阶段输入</param>
        /// <param name="error">错误信息（如失败）</param>
        /// <returns>阶段输出或默认值（失败时）</returns>
        TOut Execute(TIn input, out string error);
    }
}

