using System.Threading.Tasks;
using UnityEngine;

// 使用命名空间组织代码
namespace MrPathV2.Commands
{
    /// <summary>
    /// 命令接口，定义了可执行命令的基本结构
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// 异步执行命令
        /// </summary>
        /// <returns>任务</returns>
        Task ExecuteAsync ();
    }

    /// <summary>
    /// 将路径应用到地形的命令实现
    /// </summary>
    public class ApplyPathToTerrainCommand : ICommand
    {
        private readonly PathCreator _creator;

        /// <summary>
        /// 构造函数，使用只读字段确保不可变性
        /// </summary>
        /// <param name="creator">路径创建器实例</param>
        public ApplyPathToTerrainCommand (PathCreator creator)
        {
            _creator = creator;
        }

        /// <summary>
        /// 异步执行应用路径到地形的操作
        /// </summary>
        /// <returns>任务</returns>
        public async Task ExecuteAsync ()
        {
            Debug.Log ("开始执行'应用到地形'命令...");

            // 模拟异步操作，实际项目中可以是耗时的地形处理任务
            await Task.Run (() =>
            {
                // 1. 调用 PathSampler 生成高精度骨架
                // var spine = PathSampler.SamplePath(_creator, 0.1f);
                Debug.Log ("1.生成路径骨架...");

                // 2. 找到所有受影响的地形
                // var terrains = FindAffectedTerrains();
                Debug.Log ("2.查找受影响的地形...");
                // 3. 准备并调度一个或多个地形修改的 Job
                // ...
                Debug.Log ("3.准备并调度 Job...");
                // 4. Job完成后，将结果写回地形
                Debug.Log ("4.将结果写回地形...");
                // ...
            });

            Debug.Log ("命令执行完毕！");
        }

        // 同步版本（如果需要的话）
        public void Execute ()
        {
            ExecuteAsync ().Wait ();
        }
    }
}
