using System;
using Lunarium.Scheduler.Interfaces;

namespace Lunarium.Scheduler;

/// <summary>
/// 为 LunariumScheduler 提供便捷的扩展方法
/// </summary>
public static class SchedulerExtensions
{
    #region 同步任务扩展方法
    
    /// <summary>
    /// 调度每天在指定时间执行的任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59，默认为 0)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleDaily<TJob>(
        this LunariumScheduler scheduler,
        int hour,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IJob
    {
        ValidateTime(hour, minute);
        var cron = $"{minute} {hour} * * *";
        scheduler.Schedule<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度每周在指定星期和时间执行的任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="dayOfWeek">星期几</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59，默认为 0)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleWeekly<TJob>(
        this LunariumScheduler scheduler,
        DayOfWeek dayOfWeek,
        int hour,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IJob
    {
        ValidateTime(hour, minute);
        // Cron: 星期日 = 0, 星期一 = 1, ..., 星期六 = 6
        var dayNum = (int)dayOfWeek;
        var cron = $"{minute} {hour} * * {dayNum}";
        scheduler.Schedule<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度每月在指定日期和时间执行的任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="day">日期 (1-31)</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59，默认为 0)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleMonthly<TJob>(
        this LunariumScheduler scheduler,
        int day,
        int hour,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IJob
    {
        ValidateTime(hour, minute);
        if (day < 1 || day > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "日期必须在 1-31 之间");
        }
        var cron = $"{minute} {hour} {day} * *";
        scheduler.Schedule<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度按固定间隔执行的任务（仅支持分钟级别的间隔）
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="intervalMinutes">间隔分钟数 (1-59)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    /// <remarks>
    /// 示例：
    /// - ScheduleInterval(5) = 每 5 分钟执行一次
    /// - ScheduleInterval(15) = 每 15 分钟执行一次
    /// 对于更复杂的间隔，请直接使用 Schedule 方法并提供 Cron 表达式
    /// </remarks>
    public static LunariumScheduler ScheduleInterval<TJob>(
        this LunariumScheduler scheduler,
        int intervalMinutes,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IJob
    {
        if (intervalMinutes < 1 || intervalMinutes > 59)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMinutes),
                "间隔分钟数必须在 1-59 之间"
            );
        }
        var cron = $"*/{intervalMinutes} * * * *";
        scheduler.Schedule<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度每小时在指定分钟执行的任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleHourly<TJob>(
        this LunariumScheduler scheduler,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IJob
    {
        if (minute < 0 || minute > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "分钟必须在 0-59 之间");
        }
        var cron = $"{minute} * * * *";
        scheduler.Schedule<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    #endregion
    
    #region 异步任务扩展方法
    
    /// <summary>
    /// 调度每天在指定时间执行的异步任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59，默认为 0)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleDailyAsync<TJob>(
        this LunariumScheduler scheduler,
        int hour,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IAsyncJob
    {
        ValidateTime(hour, minute);
        var cron = $"{minute} {hour} * * *";
        scheduler.ScheduleAsync<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度每周在指定星期和时间执行的异步任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="dayOfWeek">星期几</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59，默认为 0)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleWeeklyAsync<TJob>(
        this LunariumScheduler scheduler,
        DayOfWeek dayOfWeek,
        int hour,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IAsyncJob
    {
        ValidateTime(hour, minute);
        var dayNum = (int)dayOfWeek;
        var cron = $"{minute} {hour} * * {dayNum}";
        scheduler.ScheduleAsync<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度每月在指定日期和时间执行的异步任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="day">日期 (1-31)</param>
    /// <param name="hour">小时 (0-23)</param>
    /// <param name="minute">分钟 (0-59，默认为 0)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleMonthlyAsync<TJob>(
        this LunariumScheduler scheduler,
        int day,
        int hour,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IAsyncJob
    {
        ValidateTime(hour, minute);
        if (day < 1 || day > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "日期必须在 1-31 之间");
        }
        var cron = $"{minute} {hour} {day} * *";
        scheduler.ScheduleAsync<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度按固定间隔执行的异步任务（仅支持分钟级别的间隔）
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="intervalMinutes">间隔分钟数 (1-59)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleIntervalAsync<TJob>(
        this LunariumScheduler scheduler,
        int intervalMinutes,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IAsyncJob
    {
        if (intervalMinutes < 1 || intervalMinutes > 59)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMinutes),
                "间隔分钟数必须在 1-59 之间"
            );
        }
        var cron = $"*/{intervalMinutes} * * * *";
        scheduler.ScheduleAsync<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    /// <summary>
    /// 调度每小时在指定分钟执行的异步任务
    /// </summary>
    /// <typeparam name="TJob">任务类型</typeparam>
    /// <param name="scheduler">调度器实例</param>
    /// <param name="minute">分钟 (0-59)</param>
    /// <param name="name">任务名称（可选）</param>
    /// <param name="timeZone">时区（可选，默认为 UTC）</param>
    /// <returns>调度器实例，支持链式调用</returns>
    public static LunariumScheduler ScheduleHourlyAsync<TJob>(
        this LunariumScheduler scheduler,
        int minute = 0,
        string? name = null,
        TimeZoneInfo? timeZone = null) where TJob : IAsyncJob
    {
        if (minute < 0 || minute > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "分钟必须在 0-59 之间");
        }
        var cron = $"{minute} * * * *";
        scheduler.ScheduleAsync<TJob>(cron, name, timeZone);
        return scheduler;
    }
    
    #endregion
    
    #region 辅助方法
    
    /// <summary>
    /// 验证时间参数
    /// </summary>
    private static void ValidateTime(int hour, int minute)
    {
        if (hour < 0 || hour > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hour), "小时必须在 0-23 之间");
        }
        if (minute < 0 || minute > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(minute), "分钟必须在 0-59 之间");
        }
    }
    
    #endregion
}