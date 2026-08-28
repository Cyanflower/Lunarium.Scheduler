using System;

namespace Lunarium.Scheduler.Models;

/// <summary>
/// 任务执行历史记录
/// </summary>
public class JobExecutionHistory
{
    /// <summary>
    /// 执行时间
    /// </summary>
    public DateTimeOffset ExecutedAt { get; set; }
    
    /// <summary>
    /// 执行耗时
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// 是否执行成功
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// 错误信息 (如果失败)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 异常堆栈 (如果失败)
    /// </summary>
    public string? StackTrace { get; set; }
}