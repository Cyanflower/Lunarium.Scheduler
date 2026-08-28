using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Lunarium.Scheduler.Models;
using Lunarium.Scheduler.Interfaces;

namespace Lunarium.Scheduler;

/// <summary>
/// 基于 Cron 表达式的轻量级任务调度器，支持 DI 容器集成、同步/异步任务、时区感知与并发保护。
/// </summary>
public class LunariumScheduler : IAsyncDisposable
{
    private readonly List<ScheduledJob> _jobs = new();
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private bool _isRunning = false;
    private readonly object _lock = new();
    private CancellationTokenSource? _globalCancellationTokenSource;

    #region 构造函数

    /// <summary>
    /// 创建调度器实例
    /// </summary>
    /// <param name="serviceProvider">服务提供者(DI容器)</param>
    /// <param name="logger">日志记录器(可选)。如果为 null, 将使用空日志记录器。</param>
    public LunariumScheduler(IServiceProvider serviceProvider, ILogger? logger = null)
    {
        // 验证参数
        if (serviceProvider == null)
        {
            throw new ArgumentNullException(
                nameof(serviceProvider),
                "服务提供者(DI容器)不能为 null"
            );
        }

        // 初始化字段
        _serviceProvider = serviceProvider;
        _jobs = new List<ScheduledJob>();
        _lock = new object();
        _isRunning = false;

        // 如果没有提供日志记录器, 使用 NullLogger
        _logger = logger ?? NullLogger.Instance;
    }

    #endregion

    #region 公共 API - 注册与移除任务

    /// <summary>
    /// 注册一个同步定时任务
    /// </summary>
    /// <typeparam name="TJob">任务类型, 必须实现 IJob</typeparam>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="name">任务名称(可选)</param>
    /// <param name="timeZone">任务时区(可选)</param>
    public void Schedule<TJob>(string cronExpression, string? name = null, TimeZoneInfo? timeZone = null)
        where TJob : IJob
    {
        // 创建任务信息对象
        var scheduledJob = new ScheduledJob(
            typeof(TJob),
            cronExpression,
            name,
            timeZone
        );

        // 线程安全地检查重复注册并添加
        lock (_lock)
        {
            // 同类型任务不允许重复注册(契约明确, 立即报错)
            if (_jobs.Any(j => j.JobType == typeof(TJob)))
            {
                throw new ArgumentException(
                    $"任务类型 {typeof(TJob).Name} 已注册, 不允许重复注册"
                );
            }

            // 同名任务(不同类型)记录警告并忽略
            if (_jobs.Any(j => j.Name == scheduledJob.Name))
            {
                _logger.LogWarning(
                    "任务名称 {JobName} 已存在, 忽略本次注册",
                    scheduledJob.Name
                );
                return;
            }

            _jobs.Add(scheduledJob);
        }

        _logger.LogInformation(
            "已注册任务: {JobName} (类型: {JobType}, Cron: {CronExpression}, 时区: {TimeZoneId})",
            scheduledJob.Name,
            typeof(TJob).Name,
            cronExpression,
            scheduledJob.TimeZone.Id
        );

        // 如果调度器已经启动, 立即为新任务创建调度
        if (_isRunning && _globalCancellationTokenSource != null)
        {
            StartJobLoop(scheduledJob, _globalCancellationTokenSource.Token);
            _logger.LogDebug(
                "调度器正在运行, 立即为任务 {JobName} 创建调度",
                scheduledJob.Name
            );
        }
    }

    /// <summary>
    /// 注册一个异步定时任务
    /// </summary>
    /// <typeparam name="TJob">任务类型, 必须实现 IAsyncJob</typeparam>
    /// <param name="cronExpression">Cron 表达式</param>
    /// <param name="name">任务名称(可选)</param>
    /// <param name="timeZone">任务时区(可选)</param>
    public void ScheduleAsync<TJob>(string cronExpression, string? name = null, TimeZoneInfo? timeZone = null)
        where TJob : IAsyncJob
    {
        // 创建任务信息对象
        var scheduledJob = new ScheduledJob(
            typeof(TJob),
            cronExpression,
            name,
            timeZone
        );

        // 线程安全地检查重复注册并添加
        lock (_lock)
        {
            // 同类型任务不允许重复注册(契约明确, 立即报错)
            if (_jobs.Any(j => j.JobType == typeof(TJob)))
            {
                throw new ArgumentException(
                    $"任务类型 {typeof(TJob).Name} 已注册, 不允许重复注册"
                );
            }

            // 同名任务(不同类型)记录警告并忽略
            if (_jobs.Any(j => j.Name == scheduledJob.Name))
            {
                _logger.LogWarning(
                    "任务名称 {JobName} 已存在, 忽略本次注册",
                    scheduledJob.Name
                );
                return;
            }

            _jobs.Add(scheduledJob);
        }

        _logger.LogInformation(
            "已注册异步任务: {JobName} (类型: {JobType}, Cron: {CronExpression}, 时区: {TimeZoneId})",
            scheduledJob.Name,
            typeof(TJob).Name,
            cronExpression,
            scheduledJob.TimeZone.Id
        );

        // 如果调度器已经启动, 立即为新任务创建调度
        if (_isRunning && _globalCancellationTokenSource != null)
        {
            StartJobLoop(scheduledJob, _globalCancellationTokenSource.Token);
        }
    }

    /// <summary>
    /// 移除指定类型的定时任务
    /// </summary>
    /// <typeparam name="TJob">要移除的任务类型</typeparam>
    /// <returns>如果找到并移除返回 true, 否则返回 false</returns>
    public bool Remove<TJob>() where TJob : IJob
    {
        lock (_lock)
        {
            // 查找任务
            var jobToRemove = _jobs.FirstOrDefault(j => j.JobType == typeof(TJob));

            if (jobToRemove == null)
            {
                _logger.LogWarning(
                    "尝试移除不存在的任务: {JobType}",
                    typeof(TJob).Name
                );
                return false;
            }

            // 取消任务调度循环
            jobToRemove.LoopCancellationTokenSource?.Cancel();
            jobToRemove.LoopCancellationTokenSource?.Dispose();
            jobToRemove.LoopCancellationTokenSource = null;

            // 从列表移除
            _jobs.Remove(jobToRemove);

            _logger.LogInformation(
                "已移除任务: {JobName} (类型: {JobType})",
                jobToRemove.Name,
                typeof(TJob).Name
            );

            return true;
        }
    }

    /// <summary>
    /// 移除指定名称的定时任务
    /// </summary>
    /// <param name="name">任务名称</param>
    /// <returns>如果找到并移除返回 true, 否则返回 false</returns>
    public bool Remove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        lock (_lock)
        {
            var jobToRemove = _jobs.FirstOrDefault(j => j.Name == name);

            if (jobToRemove == null)
            {
                _logger.LogWarning(
                    "尝试移除不存在的任务: {JobName}",
                    name
                );
                return false;
            }

            // 取消任务调度循环
            jobToRemove.LoopCancellationTokenSource?.Cancel();
            jobToRemove.LoopCancellationTokenSource?.Dispose();
            jobToRemove.LoopCancellationTokenSource = null;

            _jobs.Remove(jobToRemove);

            _logger.LogInformation(
                "已移除任务: {JobName} (类型: {JobType})",
                jobToRemove.Name,
                jobToRemove.JobType.Name
            );

            return true;
        }
    }

    #endregion

    #region 公共 API - 调度器的生命周期控制

    /// <summary>
    /// 启动调度器
    /// </summary>
    public void Start()
    {
        // 线程安全地检查和设置状态
        lock (_lock)
        {
            // 如果已经在运行, 直接返回
            if (_isRunning)
            {
                _logger.LogWarning("调度器已经在运行, 忽略重复启动");
                return;
            }

            // 创建全局取消令牌
            _globalCancellationTokenSource = new CancellationTokenSource();

            // 标记为运行状态
            _isRunning = true;
            _logger.LogInformation("调度器启动, 共有 {JobCount} 个任务", _jobs.Count);
        }

        // 为所有已注册的任务创建调度循环
        lock (_lock)
        {
            foreach (var job in _jobs)
            {
                StartJobLoop(job, _globalCancellationTokenSource!.Token);
                _logger.LogInformation(
                    "为任务 {JobName} 创建调度, 下次执行: {NextRun}",
                    job.Name,
                    job.NextRun?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "未计算"
                );
            }
        }

        _logger.LogInformation("调度器启动完成");
    }

    /// <summary>
    /// 停止调度器
    /// </summary>
    public void Stop()
    {
        // 线程安全地检查和设置状态
        lock (_lock)
        {
            // 如果没有运行, 直接返回
            if (!_isRunning)
            {
                _logger.LogWarning("调度器未运行, 忽略停止操作");
                return;
            }

            // 标记为停止状态
            _isRunning = false;
            _logger.LogInformation("正在停止调度器...");
        }

        // 取消所有任务的调度循环
        _globalCancellationTokenSource?.Cancel();

        int stoppedCount = 0;
        lock (_lock)
        {
            foreach (var job in _jobs)
            {
                job.LoopCancellationTokenSource?.Cancel();
                stoppedCount++;
            }
        }

        _logger.LogInformation("调度器已停止, 共停止了 {Count} 个任务的调度", stoppedCount);
    }

    #endregion

    #region 公共 API - 任务信息查询

    /// <summary>
    /// 获取指定任务的信息
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <returns>任务信息字符串, 如果任务不存在返回 null</returns>
    public string? GetJobInfo<TJob>() where TJob : IJob
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobType == typeof(TJob));

            if (job == null)
            {
                return null;
            }

            return FormatJobInfo(job);
        }
    }

    /// <summary>
    /// 获取指定名称任务的信息
    /// </summary>
    /// <param name="name">任务名称</param>
    /// <returns>任务信息字符串, 如果任务不存在返回 null</returns>
    public string? GetJobInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.Name == name);

            if (job == null)
            {
                return null;
            }

            return FormatJobInfo(job);
        }
    }

    /// <summary>
    /// 获取所有任务的信息
    /// </summary>
    /// <returns>所有任务的信息字符串</returns>
    public string GetAllJobsInfo()
    {
        lock (_lock)
        {
            var sb = new System.Text.StringBuilder();

            // 调度器状态
            sb.AppendLine("==================== 调度器信息 ====================");
            sb.AppendLine($"调度器状态: {(_isRunning ? "运行中" : "已停止")}");
            sb.AppendLine($"已注册任务数: {_jobs.Count}");
            sb.AppendLine();

            // 如果没有任务
            if (_jobs.Count == 0)
            {
                sb.AppendLine("尚未注册任何任务");
                sb.AppendLine("====================================================");
                return sb.ToString();
            }

            // 遍历所有任务
            for (int i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];

                sb.AppendLine($"-------------------- 任务 [{i + 1}] --------------------");
                sb.Append(FormatJobInfo(job));
                sb.AppendLine();
            }

            sb.AppendLine("====================================================");

            return sb.ToString();
        }
    }

    #endregion

    #region IAsyncDisposable 实现

    /// <summary>
    /// 释放调度器资源(异步)
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // 停止调度
        Stop();

        // 等待所有任务完成
        _logger.LogInformation("等待所有任务执行完成...");

        List<Task> tasksToWait;
        lock (_lock)
        {
            tasksToWait = _jobs
                .Select(j => j.WaitForCompletionAsync())
                .ToList();
        }

        try
        {
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(
                Task.WhenAll(tasksToWait),
                timeoutTask
            );

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("等待任务完成超时(30秒)，强制继续清理");
            }
            else
            {
                _logger.LogInformation("所有任务已完成");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待任务完成时发生错误");
        }

        // 等待所有调度循环结束(异步等待, 超时 5 秒)
        List<Task> loopTasks;
        lock (_lock)
        {
            loopTasks = _jobs
                .Where(j => j.LoopTask != null)
                .Select(j => j.LoopTask!)
                .ToList();
        }

        if (loopTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(loopTasks).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "等待调度循环结束时超时或发生错误");
            }
        }

        // 释放取消令牌
        _globalCancellationTokenSource?.Dispose();
        _globalCancellationTokenSource = null;

        // 清空任务列表
        lock (_lock)
        {
            foreach (var job in _jobs)
            {
                job.LoopCancellationTokenSource?.Dispose();
            }
            _jobs.Clear();
        }

        _logger.LogInformation("调度器资源已释放");
    }

    #endregion

    #region 私有方法 - 核心调度与执行逻辑

    /// <summary>
    /// 启动任务的调度循环
    /// </summary>
    /// <param name="scheduledJob">要调度的任务</param>
    /// <param name="cancellationToken">取消令牌</param>
    private void StartJobLoop(ScheduledJob scheduledJob, CancellationToken cancellationToken)
    {
        // 释放上一次调度循环遗留的取消令牌源, 避免 Stop/Start 循环泄漏
        scheduledJob.LoopCancellationTokenSource?.Dispose();
        scheduledJob.LoopCancellationTokenSource = null;

        // 如果取消令牌已请求, 不再启动(避免以已取消令牌启动导致状态混乱)
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // 为每个任务创建独立的取消令牌源
        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scheduledJob.LoopCancellationTokenSource = jobCts;

        // 启动调度循环
        scheduledJob.LoopTask = Task.Run(async () =>
        {
            await RunJobLoopAsync(scheduledJob, jobCts.Token);
        }, jobCts.Token);
    }

    /// <summary>
    /// 任务调度循环(替代 Timer 的实现)
    /// </summary>
    /// <param name="scheduledJob">要调度的任务</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task RunJobLoopAsync(ScheduledJob scheduledJob, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 计算下次执行时间
                var nextOccurrence = scheduledJob.CronExpression.GetNextOccurrence(
                    DateTimeOffset.UtcNow,
                    scheduledJob.TimeZone
                );

                // 如果没有下次执行时间, 退出循环
                if (nextOccurrence == null)
                {
                    _logger.LogWarning(
                        "任务 {JobName} 在时区 {TimeZoneId} 没有下次执行时间, 调度循环结束",
                        scheduledJob.Name,
                        scheduledJob.TimeZone.Id
                    );
                    break;
                }

                // 更新下次执行时间(与查询方的 _lock 保护一致, 避免撕裂读)
                lock (_lock)
                {
                    scheduledJob.NextRun = nextOccurrence.Value;
                }

                _logger.LogDebug(
                    "任务 {JobName} 下次执行时间: {NextRun}",
                    scheduledJob.Name,
                    nextOccurrence.Value.ToString("yyyy-MM-dd HH:mm:ss zzz")
                );

                // 等待到执行时间(自动分片, 支持任意长间隔)
                try
                {
                    await WaitUntilAsync(nextOccurrence.Value, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug(
                        "任务 {JobName} 的调度循环被取消",
                        scheduledJob.Name
                    );
                    break;
                }

                // 检查是否被取消
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 执行任务
                await ExecuteJobWithTrackingAsync(scheduledJob, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "任务 {JobName} 的调度循环发生未处理的异常",
                scheduledJob.Name
            );
        }
    }

    /// <summary>
    /// 执行任务并追踪(统一入口)
    /// </summary>
    private async Task ExecuteJobWithTrackingAsync(ScheduledJob scheduledJob, CancellationToken cancellationToken)
    {
        // 检查任务是否正在运行
        if (!scheduledJob.TryBeginExecution())
        {
            _logger.LogWarning(
                "任务 {JobName} 仍在执行中, 跳过本次调度",
                scheduledJob.Name
            );
            return;
        }

        try
        {
            Task executionTask;

            // 根据任务类型创建执行任务
            if (scheduledJob.IsAsync)
            {
                executionTask = Task.Run(
                    () => ExecuteJobAsync(scheduledJob),
                    cancellationToken
                );
            }
            else
            {
                executionTask = Task.Run(
                    () => ExecuteJob(scheduledJob),
                    cancellationToken
                );
            }

            // 追踪任务
            scheduledJob.SetRunningTask(executionTask);

            // 等待任务完成(但不阻塞调度循环)
            try
            {
                await executionTask;
            }
            catch
            {
                // 异常已在 ExecuteJob/ExecuteJobAsync 中处理
            }
        }
        finally
        {
            // 无论任务是否启动、是否完成, 都确保执行状态复位
            scheduledJob.EndExecution();
        }
    }

    /// <summary>
    /// 执行同步任务
    /// </summary>
    /// <param name="scheduledJob">要执行的任务</param>
    private void ExecuteJob(ScheduledJob scheduledJob)
    {
        _logger.LogDebug(
            "开始执行任务: {JobName} (类型: {JobType})",
            scheduledJob.Name,
            scheduledJob.JobType.Name
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var history = new JobExecutionHistory
        {
            ExecutedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // 创建作用域(临时的服务容器)
            using var scope = _serviceProvider.CreateScope();

            // 从 DI 容器获取任务实例
            var jobInstance = scope.ServiceProvider.GetRequiredService(scheduledJob.JobType);

            // 转换为 IJob 接口类型
            var job = (IJob)jobInstance;

            // 执行任务
            job.Execute();

            stopwatch.Stop();

            // 记录成功
            history.Success = true;
            history.Duration = stopwatch.Elapsed;

            // 更新最后执行时间(与查询方的 _lock 保护一致, 避免撕裂读)
            lock (_lock)
            {
                scheduledJob.LastRun = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation(
                "任务执行成功: {JobName}, 耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // 记录失败
            history.Success = false;
            history.Duration = stopwatch.Elapsed;
            history.ErrorMessage = ex.Message;
            history.StackTrace = ex.StackTrace;

            // 捕获所有异常, 记录但不中断调度
            _logger.LogError(
                ex,
                "任务执行失败: {JobName}, 耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        finally
        {
            // 记录执行历史
            scheduledJob.RecordExecution(history);
        }
    }

    /// <summary>
    /// 执行异步任务
    /// </summary>
    /// <param name="scheduledJob">要执行的任务</param>
    private async Task ExecuteJobAsync(ScheduledJob scheduledJob)
    {
        _logger.LogDebug(
            "开始执行异步任务: {JobName} (类型: {JobType})",
            scheduledJob.Name,
            scheduledJob.JobType.Name
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var history = new JobExecutionHistory
        {
            ExecutedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // 创建作用域
            using var scope = _serviceProvider.CreateScope();

            // 从 DI 容器获取任务实例
            var jobInstance = scope.ServiceProvider.GetRequiredService(scheduledJob.JobType);

            // 转换为 IAsyncJob 接口类型
            var job = (IAsyncJob)jobInstance;

            // 异步执行任务
            await job.ExecuteAsync();

            stopwatch.Stop();

            // 记录成功
            history.Success = true;
            history.Duration = stopwatch.Elapsed;

            // 更新最后执行时间(与查询方的 _lock 保护一致, 避免撕裂读)
            lock (_lock)
            {
                scheduledJob.LastRun = DateTimeOffset.UtcNow;
            }

            _logger.LogInformation(
                "异步任务执行成功: {JobName}, 耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // 记录失败
            history.Success = false;
            history.Duration = stopwatch.Elapsed;
            history.ErrorMessage = ex.Message;
            history.StackTrace = ex.StackTrace;

            // 捕获所有异常, 记录但不中断调度
            _logger.LogError(
                ex,
                "异步任务执行失败: {JobName}, 耗时: {Elapsed:0.00}ms",
                scheduledJob.Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        finally
        {
            // 记录执行历史
            scheduledJob.RecordExecution(history);
        }
    }

    #endregion

    #region 私有方法 - 辅助工具

    /// <summary>
    /// 等待直到指定时间(自动分片, 支持任意长间隔)
    /// </summary>
    /// <remarks>
    /// 单次最多等待 1 天, 避免 Task.Delay 约 49.7 天的单次上限;
    /// 分片期间每次醒来都会重新计算剩余时间, 对系统时钟调整也具有自适应性。
    /// </remarks>
    /// <param name="target">目标时间(UTC 瞬时)</param>
    /// <param name="cancellationToken">取消令牌</param>
    private static async Task WaitUntilAsync(DateTimeOffset target, CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = target - DateTimeOffset.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var chunk = remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining;
            await Task.Delay(chunk, cancellationToken);
        }
    }

    /// <summary>
    /// 格式化单个任务的信息
    /// </summary>
    /// <param name="job">任务对象</param>
    /// <returns>格式化的信息字符串</returns>
    private string FormatJobInfo(ScheduledJob job)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"任务名称: {job.Name}");
        sb.AppendLine($"任务类型: {job.JobType.Name}");
        sb.AppendLine($"Cron 表达式: {job.CronExpression}");
        sb.AppendLine($"任务模式: {(job.IsAsync ? "异步" : "同步")}");
        sb.AppendLine($"时区: {job.TimeZone.Id} ({job.TimeZone.StandardName})");

        if (job.NextRun.HasValue)
        {
            var localNextRun = TimeZoneInfo.ConvertTime(job.NextRun.Value, job.TimeZone);
            sb.AppendLine($"下次执行 (UTC): {job.NextRun.Value:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"下次执行 (本地): {localNextRun:yyyy-MM-dd HH:mm:ss zzz}");
        }
        else
        {
            sb.AppendLine("下次执行: 未调度");
        }

        if (job.LastRun.HasValue)
        {
            var localLastRun = TimeZoneInfo.ConvertTime(job.LastRun.Value, job.TimeZone);
            sb.AppendLine($"上次执行 (UTC): {job.LastRun.Value:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"上次执行 (本地): {localLastRun:yyyy-MM-dd HH:mm:ss zzz}");
        }
        else
        {
            sb.AppendLine("上次执行: 从未执行");
        }

        sb.AppendLine($"运行状态: {(job.LoopTask != null && !job.LoopTask.IsCompleted ? "已调度" : "未调度")}");
        sb.AppendLine($"是否正在执行: {(job.IsRunning ? "是" : "否")}");

        // 显示执行历史统计
        var history = job.GetHistory();
        if (history.Count > 0)
        {
            sb.AppendLine($"执行历史记录数: {history.Count}");
            sb.AppendLine($"成功次数: {job.GetSuccessCount()}");
            sb.AppendLine($"失败次数: {job.GetFailureCount()}");

            var lastHistory = job.GetLastHistory();
            if (lastHistory != null)
            {
                sb.AppendLine($"最近一次执行:");
                sb.AppendLine($"  时间: {lastHistory.ExecutedAt:yyyy-MM-dd HH:mm:ss zzz}");
                sb.AppendLine($"  结果: {(lastHistory.Success ? "成功" : "失败")}");
                sb.AppendLine($"  耗时: {lastHistory.Duration.TotalMilliseconds:0.00}ms");
                if (!lastHistory.Success && !string.IsNullOrEmpty(lastHistory.ErrorMessage))
                {
                    sb.AppendLine($"  错误: {lastHistory.ErrorMessage}");
                }
            }
        }
        else
        {
            sb.AppendLine("执行历史: 无");
        }

        return sb.ToString();
    }

    #endregion
}