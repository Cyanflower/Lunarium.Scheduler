using System;

namespace Lunarium.Scheduler.Interfaces;

/// <summary>
/// 异步定时任务接口
/// </summary>
public interface IAsyncJob
{
    /// <summary>
    /// 异步执行任务
    /// </summary>
    Task ExecuteAsync();
}
