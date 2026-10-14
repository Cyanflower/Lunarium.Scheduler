# Lunarium.Scheduler

一个轻量级、高性能的 .NET 定时任务调度器，基于 Cron 表达式实现，原生集成依赖注入 (DI) 容器。

## 🎯 设计初衷

相比于市面上的其他定时任务库（如 Quartz.NET、Hangfire），`Lunarium.Scheduler` 专为 **DI 容器内使用** 而设计，完美适配 **任意 DI 架构**——包括在 `IHostedService` 内部自建私有容器的嵌套场景。

### 核心优势

**🪶 轻量级**
- 零配置文件，零外部依赖（除 Cronos）
- 不需要数据库、不需要持久化
- 整个调度器只有几个核心类，代码简洁易懂

**⚡ 高性能**
- 基于 `Task.Delay` 的调度循环，开销极小
- 每个任务有独立的调度循环，互不阻塞
- 内存占用低，适合长时间运行

**🔧 简单易配置**
- API 设计简洁，几行代码即可完成配置
- 标准 Cron 表达式 + 语义化的便捷扩展方法，学习成本低
- 无需复杂的配置文件或数据库表

**💉 DI 原生集成**
- **可在任意 DI 容器内注册和管理**，而不是只能在主机级别使用
- 任务通过 DI 容器创建，自动解析依赖
- 完美适配 `IHostedService` 内部的私有容器场景

### 典型应用场景

本调度器专为以下场景设计：

```csharp
// ❌ 其他库通常需要在主机级别配置
builder.Services.AddQuartz(...);  // 全局配置，所有服务共享

// ✅ LunariumScheduler 可以在服务类的私有容器内独立管理
public class MyModuleService : IHostedService
{
    private readonly IServiceProvider _privateServices;
    private readonly LunariumScheduler _scheduler;

    public MyModuleService(ILogger<MyModuleService> logger)
    {
        // 每个服务类都可以有自己的 DI 容器和调度器
        _privateServices = new ServiceCollection()
            .AddSingleton<FullSyncJob>()
            .AddSingleton<AnalysisJob>()
            .AddSingleton<LunariumScheduler>(provider =>
                new LunariumScheduler(provider, logger))
            .BuildServiceProvider();

        _scheduler = _privateServices.GetRequiredService<LunariumScheduler>();
        _scheduler.ScheduleAsync<FullSyncJob>("0 4 * * 2");
        _scheduler.Start();
    }
}
```

这种设计使得：
- **每个服务/模块可以有自己的任务调度器**，互不干扰
- **任务的依赖可以限定在服务内部**，不会污染全局容器
- **服务的启动/停止完全独立**，符合模块化架构思想

## ✨ 特性

- **🎯 Cron 表达式支持**：使用标准 Cron 表达式定义任务执行时间
- **🔧 便捷扩展方法**：`ScheduleDaily` / `ScheduleWeekly` / `ScheduleMonthly` / `ScheduleInterval` / `ScheduleHourly`，支持链式调用
- **💉 DI 原生集成**：任务通过依赖注入容器创建，自动解析依赖
- **⚡ 异步/同步双模式**：同时支持同步 (`IJob`) 和异步 (`IAsyncJob`) 任务
- **🌍 时区感知**：支持为每个任务指定独立的时区
- **🔒 并发保护**：内置任务执行状态管理，防止重复执行
- **📊 任务监控**：提供详细的任务信息查询接口（含执行历史统计）
- **🛡️ 异常隔离**：单个任务失败不影响其他任务的调度
- **📝 完整日志**：集成 Microsoft.Extensions.Logging，记录任务执行详情
- **🕐 异步释放**：`DisposeAsync` 等待所有任务完成后释放资源
- **🖥️ 执行历史**：保留最近 10 次执行记录，含耗时、成功/失败状态
- **🧩 多目标框架**：同时提供 net8.0 / net9.0 / net10.0 资产，按目标框架匹配 `Microsoft.Extensions.*` 主版本

## 📦 安装

通过 NuGet 包或项目引用引入：

```bash
dotnet add package Lunarium.Scheduler
```

或使用项目引用：

```xml
<ProjectReference Include="../Lunarium.Scheduler/Lunarium.Scheduler.csproj" />
```

## 🚀 快速开始

### 1. 定义任务

创建一个实现 `IJob` 或 `IAsyncJob` 接口的任务类：

```csharp
using Lunarium.Scheduler.Interfaces;

// 同步任务
public class DailyReportJob : IJob
{
    private readonly ILogger<DailyReportJob> _logger;
    private readonly IDataService _dataService;

    public DailyReportJob(ILogger<DailyReportJob> logger, IDataService dataService)
    {
        _logger = logger;
        _dataService = dataService;
    }

    public void Execute()
    {
        _logger.LogInformation("正在生成每日报告...");
        var report = _dataService.GenerateReport();
        // 处理报告...
    }
}

// 异步任务
public class DataSyncJob : IAsyncJob
{
    private readonly IApiClient _apiClient;

    public DataSyncJob(IApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task ExecuteAsync()
    {
        await _apiClient.SyncDataAsync();
    }
}
```

### 2. 创建调度器并注册任务

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lunarium.Scheduler;

// 创建服务容器并注册任务
var services = new ServiceCollection()
    .AddLogging(builder => builder.AddConsole())
    .AddScoped<DailyReportJob>()
    .AddScoped<DataSyncJob>()
    .BuildServiceProvider();

// 创建调度器
var logger = services.GetRequiredService<ILogger<LunariumScheduler>>();
var scheduler = new LunariumScheduler(services, logger);

// 使用便捷方法注册任务（支持链式调用）
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
scheduler
    .ScheduleDaily<DailyReportJob>(hour: 4, name: "每日报告")
    .ScheduleWeeklyAsync<DataSyncJob>(DayOfWeek.Tuesday, hour: 9, name: "每周同步", timeZone: tokyoTz)
    .ScheduleInterval<HeartbeatJob>(intervalMinutes: 5, name: "心跳检查");

// 启动调度器
scheduler.Start();
```

### 3. 停止与释放

```csharp
// 停止调度器（取消所有调度循环）
scheduler.Stop();

// 异步释放资源（等待所有任务执行完成）
await scheduler.DisposeAsync();
```

完整的可运行示例请参阅 [Example.md](./Example.md)。

## 📖 详细用法

### Cron 表达式格式

调度器使用标准的 5 位 Cron 表达式：

```
┌───────────── 分钟 (0 - 59)
│ ┌───────────── 小时 (0 - 23)
│ │ ┌───────────── 日期 (1 - 31)
│ │ │ ┌───────────── 月份 (1 - 12)
│ │ │ │ ┌───────────── 星期 (0 - 6) (0 = 星期日)
│ │ │ │ │
* * * * *
```

**常用示例：**

| Cron 表达式 | 说明 |
|------------|------|
| `0 * * * *` | 每小时整点执行 |
| `*/5 * * * *` | 每 5 分钟执行一次 |
| `0 9 * * *` | 每天 9:00 执行 |
| `0 9 * * 1` | 每周一 9:00 执行 |
| `0 0 1 * *` | 每月 1 号 0:00 执行 |
| `30 8 * * 1-5` | 工作日 8:30 执行 |
| `0 */2 * * *` | 每 2 小时执行一次 |

### 便捷扩展方法

无需手写 Cron 表达式，使用语义化方法即可完成常见调度：

| 方法 | 说明 | Cron 等效 |
|------|------|-----------|
| `ScheduleDaily<TJob>(hour, minute)` | 每天执行 | `minute hour * * *` |
| `ScheduleWeekly<TJob>(dayOfWeek, hour, minute)` | 每周执行 | `minute hour * * n` |
| `ScheduleMonthly<TJob>(day, hour, minute)` | 每月执行 | `minute hour day * *` |
| `ScheduleInterval<TJob>(intervalMinutes)` | 按分钟间隔执行（1-59） | `*/n * * * *` |
| `ScheduleHourly<TJob>(minute)` | 每小时执行 | `minute * * * *` |

所有方法均有对应的异步版本（`ScheduleDailyAsync` / `ScheduleWeeklyAsync` / `ScheduleMonthlyAsync` / `ScheduleIntervalAsync` / `ScheduleHourlyAsync`），且都返回 `LunariumScheduler`，支持 **Fluent 链式调用**：

```csharp
var scheduler = new LunariumScheduler(services, logger)
    .ScheduleDaily<DailyReportJob>(hour: 4)
    .ScheduleWeekly<WeeklyReviewJob>(DayOfWeek.Tuesday, hour: 9)
    .ScheduleInterval<HeartbeatJob>(intervalMinutes: 5);

scheduler.Start();
```

### 原始 Cron 表达式

复杂的调度规则仍然可以使用原始方法：

```csharp
// 同步任务
scheduler.Schedule<ComplexJob>("0 0,12 1 */2 *", name: "复杂任务");

// 异步任务
scheduler.ScheduleAsync<ComplexAsyncJob>("30 8 * * 1-5", name: "工作日任务");
```

### 时区支持

每个任务可以指定独立的时区：

```csharp
// 使用 UTC（默认）
scheduler.Schedule<JobA>("0 12 * * *");

// 使用东京时区（Linux/macOS 使用 IANA 时区 ID）
var tokyoTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
scheduler.Schedule<JobB>("0 12 * * *", timeZone: tokyoTz);

// 使用纽约时区
var nyTz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
scheduler.Schedule<JobC>("0 12 * * *", timeZone: nyTz);
```

**注意：** Cron 表达式中的时间将按照指定时区解释。

### 任务管理

```csharp
// 移除任务（按类型，仅限同步任务 IJob）
scheduler.Remove<DailyReportJob>();

// 移除任务（按名称，同步/异步任务均可）
scheduler.Remove("每日报告");

// 查询单个任务信息（按类型，仅限同步任务 IJob）
string? info = scheduler.GetJobInfo<DailyReportJob>();
Console.WriteLine(info);

// 查询单个任务信息（按名称）
string? infoByName = scheduler.GetJobInfo("每日报告");
Console.WriteLine(infoByName);

// 查询所有任务
string allInfo = scheduler.GetAllJobsInfo();
Console.WriteLine(allInfo);
```

### 任务信息输出示例

```
==================== 调度器信息 ====================
调度器状态: 运行中
已注册任务数: 2

-------------------- 任务 [1] --------------------
任务名称: 每日报告
任务类型: DailyReportJob
Cron 表达式: 0 9 * * *
任务模式: 同步
时区: UTC (Coordinated Universal Time)
下次执行 (UTC): 2025-10-31 09:00:00 +00:00
下次执行 (本地): 2025-10-31 09:00:00 +00:00
上次执行 (UTC): 2025-10-30 09:00:00 +00:00
上次执行 (本地): 2025-10-30 09:00:00 +00:00
运行状态: 已调度
是否正在执行: 否
执行历史记录数: 3
成功次数: 3
失败次数: 0
最近一次执行:
  时间: 2025-10-30 09:00:00 +00:00
  结果: 成功
  耗时: 234.56ms

-------------------- 任务 [2] --------------------
任务名称: 数据同步
任务类型: DataSyncJob
Cron 表达式: 0 * * * *
任务模式: 异步
时区: Asia/Tokyo (Tokyo Standard Time)
下次执行 (UTC): 2025-10-30 15:00:00 +00:00
下次执行 (本地): 2025-10-31 00:00:00 +09:00
上次执行: 从未执行
运行状态: 已调度
是否正在执行: 否
执行历史: 无

====================================================
```

### 执行历史

调度器保留每个任务最近 10 次执行记录：

```csharp
// 通过任务信息查询（包含历史统计）
var info = scheduler.GetJobInfo("每日报告");
Console.WriteLine(info);

// 输出会包含：
// 执行历史记录数: 10
// 成功次数: 9
// 失败次数: 1
// 最近一次执行:
//   时间: 2025-11-05 04:00:00 +09:00
//   结果: 成功
//   耗时: 1234.56ms
```

## 🔧 高级特性

### 并发控制

调度器内置了任务执行状态管理。如果某个任务仍在执行中，调度器会自动跳过下一次调度：

```csharp
public class LongRunningJob : IAsyncJob
{
    public async Task ExecuteAsync()
    {
        // 这个任务可能需要 10 分钟才能完成
        await SomeLongOperation();
    }
}

// 每 5 分钟调度一次
// 但如果任务还在运行，下一次调度会被跳过
scheduler.ScheduleIntervalAsync<LongRunningJob>(intervalMinutes: 5);
```

日志输出：

```
[警告] 任务 LongRunningJob 仍在执行中, 跳过本次调度
```

### 动态任务注册

调度器启动后，仍可以动态添加新任务：

```csharp
// 启动调度器
scheduler.Start();

// 运行中动态添加任务
scheduler.Schedule<NewJob>("0 * * * *");
// 新任务会立即被调度
```

### 异常处理

任务执行中的异常会被自动捕获和记录，不会影响其他任务：

```csharp
public class FaultyJob : IJob
{
    public void Execute()
    {
        throw new Exception("出错了！");
    }
}
```

日志输出：

```
[错误] 任务执行失败: FaultyJob，耗时: 0.52ms
System.Exception: 出错了！
   at FaultyJob.Execute()
```

调度器会继续为该任务安排下一次执行。

## 📚 完整示例

完整的可运行示例（控制台应用、主机级别集成、私有容器集成）请参阅 [Example.md](./Example.md)。

以下为在 `IHostedService` 私有容器中使用的最小示例：

```csharp
public class ModuleService : IHostedService
{
    private readonly IServiceProvider _privateServices;
    private readonly LunariumScheduler _scheduler;

    public ModuleService(ILoggerFactory loggerFactory)
    {
        // ========== 构建私有 DI 容器 ==========
        _privateServices = new ServiceCollection()
            .AddSingleton<IDatabaseService, DatabaseService>()
            .AddSingleton<IAnalysisService, AnalysisService>()

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
```

## 🆚 与其他库的对比

| 特性 | LunariumScheduler | Quartz.NET | Hangfire |
|-----|------------------|-----------|----------|
| **轻量级** | ✅ 零配置，无外部依赖 | ❌ 需要配置文件 | ❌ 需要数据库 |
| **私有容器支持** | ✅ 完美支持 | ⚠️ 需要特殊配置 | ❌ 仅支持全局 |
| **学习成本** | ✅ 极低 | ⚠️ 中等 | ⚠️ 较高 |
| **内存占用** | ✅ 极小 | ⚠️ 中等 | ❌ 较大 |
| **集群支持** | ❌ 不支持 | ✅ 支持 | ✅ 支持 |
| **持久化** | ❌ 不支持 | ✅ 支持 | ✅ 支持 |
| **Web UI** | ❌ 无 | ⚠️ 第三方 | ✅ 内置 |

**结论：** 如果你需要分布式任务调度、持久化或 Web 管理界面，请选择 Quartz.NET 或 Hangfire。如果你只需要简单、轻量、高性能的进程内任务调度，`LunariumScheduler` 是最佳选择。

## ⚠️ 注意事项

1. **任务类必须注册到 DI 容器**：调度器通过 DI 容器获取任务实例，忘记注册会导致运行时错误。

2. **私有容器的作用域**：在私有容器中使用时，任务的依赖也应该注册在同一个容器中，否则会出现依赖解析失败。

   ```csharp
   // ✅ 正确：任务和依赖都在私有容器中
   _privateServices = new ServiceCollection()
       .AddSingleton<DatabaseService>()
       .AddSingleton<MyJob>()  // MyJob 依赖 DatabaseService
       .AddSingleton<LunariumScheduler>()
       .BuildServiceProvider();

   // ❌ 错误：MyJob 依赖的服务未注册
   _privateServices = new ServiceCollection()
       .AddSingleton<MyJob>()  // 缺少 DatabaseService
       .AddSingleton<LunariumScheduler>()
       .BuildServiceProvider();
   ```

3. **任务应该是轻量级的**：每次执行都会创建新的作用域 (`IServiceScope`)，避免在任务构造函数中执行耗时操作。

4. **时区陷阱**：注意夏令时 (DST) 的影响，在时区切换时可能会跳过或重复执行任务。Linux/macOS 请使用 IANA 时区 ID（如 `Asia/Tokyo`），Windows 可使用 `Tokyo Standard Time` 等 ID。

5. **异步任务不阻塞调度**：异步任务会在后台执行，不会阻塞下一次调度的计算。

6. **线程安全**：调度器的所有公共方法都是线程安全的。

7. **资源释放**：使用完毕后应调用 `DisposeAsync()` 释放资源，尤其是在服务停止时。同步的 `Dispose()` 已被移除。

   ```csharp
   public async Task StopAsync(CancellationToken cancellationToken)
   {
       _scheduler.Stop();
       await _scheduler.DisposeAsync();  // ✅ 等待任务完成并释放资源

       if (_privateServices is IDisposable disposable)
       {
           disposable.Dispose();  // ✅ 释放容器资源
       }
   }
   ```

8. **避免在主机和私有容器中重复注册**：如果使用私有容器模式，不要将调度器注册到主机容器，这会导致两个独立的调度器实例。

9. **不允许重复注册**：同一任务类型重复注册会抛出 `ArgumentException`（fail-fast）；不同类型的任务使用了相同名称时，后注册的会被忽略并记录警告日志。

## 🔗 依赖项

- **.NET 8.0 / 9.0 / 10.0**（多目标框架，与 Lunarium.Logging 一致）
- **Cronos** (v0.11.1)：用于解析 Cron 表达式
- **Microsoft.Extensions.Logging.Abstractions**：日志抽象，按目标框架匹配主版本（net8.0 → 8.0.x，net9.0 → 9.0.x，net10.0 → 10.0.x）

## 📄 许可证

本项目采用 [Apache License 2.0](LICENSE)。

---

*如有问题或建议，欢迎提交 Issue 或 Pull Request。*