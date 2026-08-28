using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cronos;

using Lunarium.Scheduler.Interfaces;

namespace Lunarium.Scheduler.Models;

/// <summary>
/// 已调度的任务信息
/// </summary>
public class ScheduledJob
{
    /// <summary>
    /// 任务类型
    /// </summary>
    public Type JobType { get; }
    
    /// <summary>
    /// Cron 表达式
    /// </summary>
    public CronExpression CronExpression { get; }
    
    /// <summary>
    /// 是否为异步任务
    /// </summary>
    public bool IsAsync { get; }
    
    /// <summary>
    /// 任务名称
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// 下次执行时间
    /// </summary>
    public DateTimeOffset? NextRun { get; set; }
    
    /// <summary>
    /// 上次执行时间
    /// </summary>
    public DateTimeOffset? LastRun { get; set; }
    
    /// <summary>
    /// 任务 Cron 时区
    /// </summary>
    public TimeZoneInfo TimeZone { get; }
    
    /// <summary>
    /// 任务是否正在执行中
    /// </summary>
    public bool IsRunning { get; private set; }
    
    /// <summary>
    /// 用于同步任务执行的锁对象
    /// </summary>
    private readonly object _executionLock = new object();
    
    /// <summary>
    /// 正在运行的任务 (用于追踪异步任务)
    /// </summary>
    private Task? _runningTask;
    
    /// <summary>
    /// 任务执行历史 (保留最近10次)
    /// </summary>
    private readonly Queue<JobExecutionHistory> _history = new Queue<JobExecutionHistory>(10);
    
    /// <summary>
    /// 历史记录的最大容量
    /// </summary>
    private const int MaxHistoryCount = 10;
    
    /// <summary>
    /// 任务调度循环的取消令牌源
    /// </summary>
    internal CancellationTokenSource? LoopCancellationTokenSource { get; set; }
    
    /// <summary>
    /// 任务调度循环的 Task
    /// </summary>
    internal Task? LoopTask { get; set; }

    /// <summary>
    /// 创建一个已调度的任务
    /// </summary>
    /// <param name="jobType">任务类型, 必须实现 IJob 或 IAsyncJob</param>
    /// <param name="cronExpression">Cron 表达式字符串</param>
    /// <param name="name">任务名称(可选)</param>
    /// <param name="timeZone">任务时区(可选), 默认为 UTC</param>
    public ScheduledJob(Type jobType, string cronExpression, string? name = null, TimeZoneInfo? timeZone = null)
    {
        // 验证 jobType 不能为 null
        if (jobType == null)
        {
            throw new ArgumentNullException(nameof(jobType), "任务类型不能为 null");
        }

        // 验证任务类型必须实现 IJob 或 IAsyncJob
        bool isJob = typeof(IJob).IsAssignableFrom(jobType);
        bool isAsyncJob = typeof(IAsyncJob).IsAssignableFrom(jobType);

        if (!isJob && !isAsyncJob)
        {
            throw new ArgumentException(
                $"任务类型 {jobType.Name} 必须实现 IJob 或 IAsyncJob 接口",
                nameof(jobType)
            );
        }

        // 验证并解析 Cron 表达式
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new ArgumentException("Cron 表达式不能为空", nameof(cronExpression));
        }

        CronExpression parsedCron;
        try
        {
            parsedCron = CronExpression.Parse(cronExpression, CronFormat.Standard);
        }
        catch (CronFormatException ex)
        {
            throw new ArgumentException(
                $"无效的 Cron 表达式: {cronExpression}",
                nameof(cronExpression),
                ex
            );
        }

        // 赋值属性
        JobType = jobType;
        CronExpression = parsedCron;
        IsAsync = isAsyncJob;                       // 如果是 IAsyncJob 则为 true
        Name = name ?? jobType.Name;                // 如果没提供名称, 用类名
        TimeZone = timeZone ?? TimeZoneInfo.Utc;    // 如果没提供时区, 默认为 UTC
        IsRunning = false;
    }
    
    /// <summary>
    /// 尝试开始执行(如果任务未在运行)
    /// </summary>
    /// <returns>如果成功开始执行返回 true, 否则返回 false</returns>
    public bool TryBeginExecution()
    {
        lock (_executionLock)
        {
            if (IsRunning)
            {
                return false;
            }
            IsRunning = true;
            return true;
        }
    }
    
    /// <summary>
    /// 标记任务执行结束
    /// </summary>
    public void EndExecution()
    {
        lock (_executionLock)
        {
            IsRunning = false;
            _runningTask = null;
        }
    }
    
    /// <summary>
    /// 设置正在运行的任务(用于追踪)
    /// </summary>
    /// <param name="task">正在执行的任务</param>
    internal void SetRunningTask(Task task)
    {
        lock (_executionLock)
        {
            _runningTask = task;
        }
    }
    
    /// <summary>
    /// 等待当前任务执行完成
    /// </summary>
    public async Task WaitForCompletionAsync()
    {
        Task? taskToWait;
        lock (_executionLock)
        {
            taskToWait = _runningTask;
        }
        
        if (taskToWait != null)
        {
            try
            {
                await taskToWait;
            }
            catch
            {
                // 异常已在执行时记录, 这里忽略
            }
        }
    }
    
    /// <summary>
    /// 记录任务执行历史
    /// </summary>
    /// <param name="history">执行历史记录</param>
    public void RecordExecution(JobExecutionHistory history)
    {
        lock (_history)
        {
            if (_history.Count >= MaxHistoryCount)
            {
                _history.Dequeue();
            }
            _history.Enqueue(history);
        }
    }
    
    /// <summary>
    /// 获取任务执行历史
    /// </summary>
    /// <returns>执行历史列表(最近的在后面)</returns>
    public IReadOnlyCollection<JobExecutionHistory> GetHistory()
    {
        lock (_history)
        {
            return _history.ToArray();
        }
    }
    
    /// <summary>
    /// 获取最近一次执行历史
    /// </summary>
    /// <returns>最近一次执行历史, 如果没有则返回 null</returns>
    public JobExecutionHistory? GetLastHistory()
    {
        lock (_history)
        {
            return _history.LastOrDefault();
        }
    }
    
    /// <summary>
    /// 获取成功执行次数
    /// </summary>
    public int GetSuccessCount()
    {
        lock (_history)
        {
            return _history.Count(h => h.Success);
        }
    }
    
    /// <summary>
    /// 获取失败执行次数
    /// </summary>
    public int GetFailureCount()
    {
        lock (_history)
        {
            return _history.Count(h => !h.Success);
        }
    }
}