using System;

namespace Lunarium.Scheduler.Interfaces;

/// <summary>
/// 定时任务接口
/// </summary>
public interface IJob
{
        /// <summary>
        /// 执行任务
        /// </summary>
        void Execute();
}
