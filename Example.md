# 📚 完整示例

本文件提供 `Lunarium.Scheduler` 的完整可运行示例，覆盖三种典型集成方式：

1. **控制台应用** —— 独立使用，快速验证
2. **主机级别集成** —— 传统单层 DI 架构（Generic Host）
3. **私有容器集成** —— 在 `IHostedService` 内部自建 DI 容器（推荐，模块化架构）

---

## 1. 控制台应用

最简单的使用方式，适合快速上手和验证：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lunarium.Scheduler;
using Lunarium.Scheduler.Interfaces;

// ========== 1. 定义任务 ==========

// 同步任务
public class DailyReportJob : IJob
{
    private readonly ILogger<DailyReportJob> _logger;
    private readonly IDbContext _dbContext;

    public DailyReportJob(ILogger<DailyReportJob> logger, IDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public void Execute()
    {
        _logger.LogInformation("开始生成每日报告...");

        // 查询数据库
        var stats = _dbContext.GetDailyStats();

        // 生成报告
        var report = GenerateReport(stats);

        // 发送邮件
        SendEmail(report);

        _logger.LogInformation("每日报告生成完成");
    }

    private static string GenerateReport(object stats) => $"Report({stats})";
    private static void SendEmail(string report) { /* ... */ }
}

// 异步任务
public class DataSyncJob : IAsyncJob
{
    private readonly ILogger<DataSyncJob> _logger;
    private readonly IApiClient _apiClient;

    public DataSyncJob(ILogger<DataSyncJob> logger, IApiClient apiClient)
    {
        _logger = logger;
        _apiClient = apiClient;
    }

    public async Task ExecuteAsync()
    {
        _logger.LogInformation("开始数据同步...");
        await _apiClient.SyncDataAsync();
        _logger.LogInformation("数据同步完成");
    }
}

// 同步任务（间隔执行示例）
public class HeartbeatJob : IJob
{
    private readonly ILogger<HeartbeatJob> _logger;

    public HeartbeatJob(ILogger<HeartbeatJob> logger)
    {
        _logger = logger;
    }

    public void Execute()
    {
        _logger.LogInformation("心跳检查: OK");
    }
}

// 依赖服务
public interface IDbContext
{
    object GetDailyStats();
}

public interface IApiClient
{
    Task SyncDataAsync();
}

public class DbContext : IDbContext
{
    public object GetDailyStats() => new();
}

public class ApiClient : IApiClient
{
    public Task SyncDataAsync() => Task.CompletedTask;
}

// ========== 2. 组装与运行 ==========

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 注册依赖服务
        services.AddSingleton<IDbContext, DbContext>();
        services.AddSingleton<IApiClient, ApiClient>();

        // 注册任务
        services.AddScoped<DailyReportJob>();
        services.AddScoped<DataSyncJob>();
    })
    .Build();

// 创建调度器
var serviceProvider = host.Services;
var logger = serviceProvider.GetRequiredService<ILogger<LunariumScheduler>>();
var scheduler = new LunariumScheduler(serviceProvider, logger);

// 获取东京时区
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

// 使用便捷方法注册任务（链式调用）
scheduler
    .ScheduleDaily<DailyReportJob>(hour: 4, name: "每日报告", timeZone: tokyoTz)
    .ScheduleWeeklyAsync<DataSyncJob>(DayOfWeek.Tuesday, hour: 9, name: "每周同步", timeZone: tokyoTz)
    .ScheduleInterval<HeartbeatJob>(intervalMinutes: 5, name: "心跳检查");

// 启动调度器
scheduler.Start();

// 打印所有任务信息
Console.WriteLine(scheduler.GetAllJobsInfo());

// 模拟运行一段时间
await Task.Delay(TimeSpan.FromMinutes(30));

// 停止并释放
scheduler.Stop();
await scheduler.DisposeAsync();
```

---

## 2. 主机级别集成（传统方式）

适用于传统的单层 DI 架构，将调度器作为 `IHostedService` 挂载到主机：

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lunarium.Scheduler;

// Program.cs
var builder = Host.CreateApplicationBuilder(args);

// 注册任务类
builder.Services.AddTransient<CleanupJob>();
builder.Services.AddTransient<BackupJob>();

// 注册调度器宿主服务
builder.Services.AddHostedService<SchedulerHostedService>();

var host = builder.Build();
await host.RunAsync();

// SchedulerHostedService.cs
public class SchedulerHostedService : IHostedService
{
    private readonly LunariumScheduler _scheduler;

    public SchedulerHostedService(IServiceProvider serviceProvider, ILogger<SchedulerHostedService> logger)
    {
        _scheduler = new LunariumScheduler(serviceProvider, logger);

        // 注册所有任务
        _scheduler.Schedule<CleanupJob>("0 2 * * *", name: "每日清理");
        _scheduler.ScheduleAsync<BackupJob>("0 3 * * *", name: "每日备份");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler.Start();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _scheduler.Stop();
        await _scheduler.DisposeAsync();
    }
}
```

---

## 3. 私有容器集成（推荐）

在 `IHostedService` 内部自建私有 DI 容器，将任务的依赖限定在模块内部，不污染全局容器。适合模块化/微服务架构：

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lunarium.Scheduler;
using Lunarium.Scheduler.Interfaces;

// 任务定义
public class AnalysisJob : IAsyncJob
{
    private readonly ILogger<AnalysisJob> _logger;
    private readonly IAnalysisService _analysis;
    private readonly IDatabaseService _database;

    public AnalysisJob(
        ILogger<AnalysisJob> logger,
        IAnalysisService analysis,
        IDatabaseService database)
    {
        _logger = logger;
        _analysis = analysis;
        _database = database;
    }

    public async Task ExecuteAsync()
    {
        _logger.LogInformation("开始执行分析任务...");

        try
        {
            // 生成分析报告
            var report = await _analysis.GenerateReportAsync();

            // 保存到数据库
            await _database.SaveReportAsync(report);

            _logger.LogInformation("分析任务执行完成。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分析任务执行失败");
            throw;
        }
    }
}

public class SyncJob : IAsyncJob
{
    private readonly ISyncService _syncService;
    private readonly ILogger<SyncJob> _logger;

    public SyncJob(ISyncService syncService, ILogger<SyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        _logger.LogInformation("开始执行数据同步...");
        await _syncService.SyncDataAsync();
        _logger.LogInformation("数据同步完成。");
    }
}

// 模块服务：每个模块拥有独立的调度器
public class ModuleService : IHostedService
{
    private readonly IServiceProvider _privateServices;
    private readonly LunariumScheduler _scheduler;

    public ModuleService(ILoggerFactory loggerFactory)
    {
        // ========== 构建私有 DI 容器 ==========
        _privateServices = new ServiceCollection()
            // 基础设施
            .AddSingleton<IDatabaseService, DatabaseService>()
            .AddSingleton<IAnalysisService, AnalysisService>()
            .AddSingleton<ISyncService, SyncService>()

            // ========== 定时任务 ==========
            .AddSingleton<AnalysisJob>()      // 分析任务
            .AddSingleton<SyncJob>()          // 同步任务

            // ========== 调度器 ==========
            .AddSingleton<LunariumScheduler>(provider =>
                new LunariumScheduler(
                    provider,
                    loggerFactory.CreateLogger<LunariumScheduler>()
                ))

            .BuildServiceProvider();

        // 解析调度器
        _scheduler = _privateServices.GetRequiredService<LunariumScheduler>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 配置时区
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

        // 注册任务
        _scheduler.ScheduleAsync<SyncJob>(
            cronExpression: "0 4 * * 2",    // 每周二 4:00
            name: "数据同步",
            timeZone: timeZone
        );

        _scheduler.ScheduleAsync<AnalysisJob>(
            cronExpression: "0 4 * * *",    // 每天 4:00
            name: "每日分析",
            timeZone: timeZone
        );

        // 启动调度器
        _scheduler.Start();

        Console.WriteLine(_scheduler.GetAllJobsInfo());

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // 停止调度器并等待所有任务完成
        _scheduler.Stop();
        await _scheduler.DisposeAsync();

        // 释放私有容器
        if (_privateServices is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

// Program.cs：注册模块
var host = Host.CreateApplicationBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<ModuleService>();
    })
    .Build();

await host.RunAsync();
```

---

## 4. 常见调度模式速查

### 每天执行

```csharp
// 每天 4:00 执行（UTC）
scheduler.ScheduleDaily<DailyJob>(hour: 4);

// 每天 8:30 执行（东京时区）
scheduler.ScheduleDaily<DailyJob>(hour: 8, minute: 30, timeZone: tokyoTz);
```

### 每周执行

```csharp
// 每周二 9:00 执行
scheduler.ScheduleWeekly<WeeklyJob>(DayOfWeek.Tuesday, hour: 9);

// 工作日 8:30 执行（原始 Cron）
scheduler.Schedule<WeeklyJob>("30 8 * * 1-5");
```

### 每月执行

```csharp
// 每月 1 号 2:00 执行
scheduler.ScheduleMonthly<MonthlyJob>(day: 1, hour: 2);

// 每月最后一天 23:00 执行（原始 Cron）
scheduler.Schedule<MonthlyJob>("0 23 L * *");
```

### 间隔执行

```csharp
// 每 5 分钟执行一次（仅支持 1-59 分钟）
scheduler.ScheduleInterval<HeartbeatJob>(intervalMinutes: 5);

// 每 2 小时执行一次（使用原始 Cron）
scheduler.Schedule<HeartbeatJob>("0 */2 * * *");
```

### 每小时执行

```csharp
// 每小时的第 30 分钟执行
scheduler.ScheduleHourly<DataCleanupJob>(minute: 30);
```

### 动态添加与移除

```csharp
// 调度器运行时动态添加任务
scheduler.ScheduleDaily<NewJob>(hour: 6);
// 新任务会立即被调度

// 移除任务（按名称，同步/异步均可）
bool removed = scheduler.Remove("每日报告");

// 移除任务（按类型，仅限同步任务 IJob）
bool removedByType = scheduler.Remove<DailyReportJob>();
```

---

## 5. 任务定义参考

### 同步任务 `IJob`

```csharp
using Lunarium.Scheduler.Interfaces;

public class CleanupJob : IJob
{
    private readonly ILogger<CleanupJob> _logger;

    public CleanupJob(ILogger<CleanupJob> logger)
    {
        _logger = logger;
    }

    public void Execute()
    {
        _logger.LogInformation("开始清理过期数据...");
        // 业务逻辑...
    }
}
```

### 异步任务 `IAsyncJob`

```csharp
using Lunarium.Scheduler.Interfaces;

public class BackupJob : IAsyncJob
{
    private readonly ILogger<BackupJob> _logger;

    public BackupJob(ILogger<BackupJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        _logger.LogInformation("开始备份...");
        await Task.Delay(TimeSpan.FromSeconds(10));
        _logger.LogInformation("备份完成");
    }
}
```

### 任务执行历史

```csharp
// 调度器保留每个任务最近 10 次执行记录
var info = scheduler.GetJobInfo("每日报告");

// 输出包含：
// 执行历史记录数: 10
// 成功次数: 9
// 失败次数: 1
// 最近一次执行:
//   时间: 2025-11-05 04:00:00 +09:00
//   结果: 成功
//   耗时: 1234.56ms
```