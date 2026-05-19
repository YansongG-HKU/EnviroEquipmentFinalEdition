# Phase 2 — Package 1: WPF Shell + Overview + Single Device Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bootstrap the WPF desktop client (`SiemensS7Demo.Wpf`), translate the 202605 design tokens into XAML, render the multi-device Overview grid and the Single-device control screen, wire both to live `SiemensS7Client` instances through a new `DeviceSessionManager`, and prove the whole stack end-to-end with a headless smoke run.

**Architecture:** Split the existing monolithic `SiemensS7Demo` project into four sibling layers — `SiemensS7Demo.Core` (renamed from current `SiemensS7Demo`), `SiemensS7Demo.Domain` (pure entities/value objects), `SiemensS7Demo.App` (services + orchestration), `SiemensS7Demo.Wpf` (UI). The previous `Program.cs` console host is extracted into `SiemensS7Demo.ConsoleHost`. The WPF app builds an `IHost` using `Microsoft.Extensions.Hosting`, registers `IDeviceSessionManager` + per-device `SiemensS7Client` factories, and uses CommunityToolkit.Mvvm source generators for ViewModels. Theme tokens are mechanically generated from `温箱202605/styles.css` into `Themes/Tokens.xaml` via `tools/CssToXaml.ps1`. The Shell reproduces the React `AppFrame` (TopBar + SideNav + content router + StatusBar). The Overview renders a 9-card grid bound to `DeviceSessionManager.Devices`. The Single screen renders PV/SV per device with run/pause/stop/reset commands and a segment indicator. A `--headless-smoke` CLI flag boots the host without `Application.Run`, drives a 3-device InMemoryAdapter scenario through ViewModels, and exits 0 on success.

**Tech Stack:** C# .NET 8 (`net8.0` for Domain/App/Core/ConsoleHost, `net8.0-windows` with `UseWPF=true` for Wpf), CommunityToolkit.Mvvm 8.x (ObservableObject, RelayCommand, source generators), Microsoft.Extensions.Hosting + Microsoft.Extensions.DependencyInjection 8.x, Serilog + Microsoft.Extensions.Logging facade, xunit + FluentAssertions for tests. No Prism, no Caliburn, no third-party MVVM framework.

**Scope guard:** This plan covers **only Milestones M1.1–M1.6**. It does NOT cover the alarm subsystem (Package 2), program/history/SQLite persistence (Package 3), or login/RBAC/LIMS/MQTT/FTP (Package 4). The RBAC surface used in M1.5 is a hard-coded `IRbacContext` stub that always returns `Role.Admin`. The XAML guidance banner / role-aware copy from `guidance.jsx` is not exercised in Pkg 1 — only the structural shell is. No real S7-200 SMART hardware is touched; everything runs against `InMemoryS7Adapter`.

**Branch:** `feat/phase2-pkg1-shell-overview-single`
**Worktree:** `.claude/worktrees/phase2-pkg1-shell-overview-single`
**Base:** `main` after Wave 1 (`feat/gap1-modbus-float`, `feat/gap3-tag-options`, `feat/gap8-snap7-batch-read`) is merged.

---

## File Structure

| Action | Path | Responsibility |
|--------|------|----------------|
| Rename | `src/SiemensS7Demo/` → `src/SiemensS7Demo.Core/` | Existing PLC kernel project (folder + csproj + root namespace stay `SiemensS7Demo.*`) |
| Modify | `src/SiemensS7Demo.Core/SiemensS7Demo.Core.csproj` | Renamed csproj; assembly name = `SiemensS7Demo.Core`; root namespace = `SiemensS7Demo` (unchanged) |
| Delete | `src/SiemensS7Demo.Core/Program.cs` | Moved to `ConsoleHost` |
| Create | `src/SiemensS7Demo.ConsoleHost/SiemensS7Demo.ConsoleHost.csproj` | `net8.0`, `OutputType=Exe`, references Core |
| Create | `src/SiemensS7Demo.ConsoleHost/Program.cs` | Existing `Program.cs` body relocated verbatim |
| Create | `src/SiemensS7Demo.Domain/SiemensS7Demo.Domain.csproj` | `net8.0`, no project references |
| Create | `src/SiemensS7Demo.Domain/DeviceId.cs` | `public sealed record DeviceId(string Value)` |
| Create | `src/SiemensS7Demo.Domain/DeviceStatus.cs` | `public enum DeviceStatus { Run, Idle, Scheduled, Paused, Alarm, Offline }` |
| Create | `src/SiemensS7Demo.Domain/DeviceType.cs` | `public enum DeviceType { Standard, Standard1500, LowPressure, Shock }` |
| Create | `src/SiemensS7Demo.Domain/Setpoints.cs` | `public sealed record Setpoints(double? Temp, double? Humidity, double? Pressure)` |
| Create | `src/SiemensS7Demo.Domain/ReadingSnapshot.cs` | `public sealed record ReadingSnapshot(DateTimeOffset At, double? Pv, double? Sv, double? Humid, double? HumidSv, double? Press, double? PressSv)` |
| Create | `src/SiemensS7Demo.Domain/Device.cs` | `public sealed class Device { DeviceId Id; string Bay; DeviceType Type; DeviceStatus Status; Setpoints Setpoints; ReadingSnapshot? LastReading; }` |
| Create | `src/SiemensS7Demo.Domain/DeviceWriteResult.cs` | `public sealed record DeviceWriteResult(bool Ok, string? ErrorCode, string? ErrorMessage)` |
| Create | `src/SiemensS7Demo.Domain/ProjectConfig.cs` | `public sealed record ProjectConfig(IReadOnlyList<DeviceProvisioning> Devices)` + `DeviceProvisioning` record |
| Create | `src/SiemensS7Demo.App/SiemensS7Demo.App.csproj` | `net8.0`, references Core + Domain |
| Create | `src/SiemensS7Demo.App/IDeviceSessionManager.cs` | The interface from spec §4 |
| Create | `src/SiemensS7Demo.App/DeviceSessionManager.cs` | Multi-device lifecycle implementation using `Subject<Device>` over per-device PLC polling |
| Create | `src/SiemensS7Demo.App/IRbacContext.cs` | `Role Current { get; }` |
| Create | `src/SiemensS7Demo.App/AdminRbacContext.cs` | Stub always-Admin implementation |
| Create | `src/SiemensS7Demo.App/Role.cs` | `public enum Role { Operator, Engineer, Admin }` |
| Create | `src/SiemensS7Demo.App/AppServiceCollectionExtensions.cs` | `AddSiemensS7DemoApp(this IServiceCollection)` |
| Create | `src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj` | `net8.0-windows`, `UseWPF=true`, references App + Domain + Core |
| Create | `src/SiemensS7Demo.Wpf/App.xaml` | XAML app w/ Themes/Tokens merged |
| Create | `src/SiemensS7Demo.Wpf/App.xaml.cs` | `IHost`-driven startup, `--headless-smoke` switch |
| Create | `src/SiemensS7Demo.Wpf/Themes/Tokens.xaml` | Color/Brush/FontFamily/Thickness resources from styles.css |
| Create | `src/SiemensS7Demo.Wpf/Themes/Controls.xaml` | Button/Pill/Card control templates referencing tokens |
| Create | `src/SiemensS7Demo.Wpf/Views/Shell.xaml` | TopBar + SideNav + content `ContentControl` + StatusBar |
| Create | `src/SiemensS7Demo.Wpf/Views/Shell.xaml.cs` | Code-behind shell window |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs` | Active screen + nav handler |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/NavItem.cs` | Nav item view-model record |
| Create | `src/SiemensS7Demo.Wpf/Views/OverviewView.xaml` | 9-card responsive grid |
| Create | `src/SiemensS7Demo.Wpf/Views/OverviewView.xaml.cs` | Code-behind |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/OverviewViewModel.cs` | Binds to `IDeviceSessionManager.Devices` |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/DeviceCardViewModel.cs` | Per-card observable state |
| Create | `src/SiemensS7Demo.Wpf/Views/SingleDeviceView.xaml` | PV/SV hero, controls, segment indicator |
| Create | `src/SiemensS7Demo.Wpf/Views/SingleDeviceView.xaml.cs` | Code-behind |
| Create | `src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs` | Run/Pause/Stop/Reset commands + SV write |
| Create | `src/SiemensS7Demo.Wpf/Converters/StatusToBrushConverter.cs` | Maps `DeviceStatus` → tokens brush |
| Create | `src/SiemensS7Demo.Wpf/Converters/StatusToLabelConverter.cs` | Maps `DeviceStatus` → Chinese label |
| Create | `src/SiemensS7Demo.Wpf/Smoke/HeadlessSmokeRunner.cs` | `--headless-smoke` orchestrator |
| Create | `src/SiemensS7Demo.Wpf/appsettings.json` | Default `ProjectConfig` w/ 3 InMemory devices |
| Create | `tools/CssToXaml.ps1` | Deterministic CSS-custom-property → XAML brush regen |
| Create | `tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj` | `net8.0`, references App + Domain + Core |
| Create | `tests/EnviroEquipment.App.Tests/DeviceSessionManagerTests.cs` | Multi-device lifecycle, reconnect, polling cadence |
| Create | `tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj` | `net8.0-windows`, `UseWPF=true`, references Wpf |
| Create | `tests/EnviroEquipment.Wpf.Tests/Themes/TokensTests.cs` | CSS↔XAML brush parity |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/ShellViewModelTests.cs` | Nav routing |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/OverviewViewModelTests.cs` | Snapshot binding + alarm pip |
| Create | `tests/EnviroEquipment.Wpf.Tests/ViewModels/SingleDeviceViewModelTests.cs` | Command matrix + SV write |
| Create | `tests/EnviroEquipment.E2ETests/EnviroEquipment.E2ETests.csproj` | `net8.0-windows`, references Wpf |
| Create | `tests/EnviroEquipment.E2ETests/Pkg1/SmokeTests.cs` | 3-device InMemory E2E |
| Modify | `EnviroEquipmentFinalEdition.sln` | Add 4 new projects + 3 new test projects; remove old `SiemensS7Demo` entry; add `SiemensS7Demo.Core` + `SiemensS7Demo.ConsoleHost` entries |
| Modify | `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj` | Update project reference from `SiemensS7Demo` to `SiemensS7Demo.Core` |

---

## Task 1: WPF project bootstrap (M1.1)

**Files:** Rename existing project folder, extract `ConsoleHost`, create the 3 new project skeletons (`Domain`, `App`, `Wpf`), update solution + downstream references, prove the WPF host opens a smoke window.

- [ ] **Step 1.1: Rename `src/SiemensS7Demo` to `src/SiemensS7Demo.Core`**

From the repo root:

```pwsh
git mv src/SiemensS7Demo src/SiemensS7Demo.Core
git mv src/SiemensS7Demo.Core/SiemensS7Demo.csproj src/SiemensS7Demo.Core/SiemensS7Demo.Core.csproj
```

Then edit `src/SiemensS7Demo.Core/SiemensS7Demo.Core.csproj` so it reads exactly:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>SiemensS7Demo.Core</AssemblyName>
    <RootNamespace>SiemensS7Demo</RootNamespace>
  </PropertyGroup>
</Project>
```

Notes:
- `OutputType` is dropped (this is now a library).
- `RootNamespace` stays `SiemensS7Demo` so existing `namespace SiemensS7Demo.Models`, `SiemensS7Demo.Drivers`, etc. continue to compile unchanged.

- [ ] **Step 1.2: Extract `Program.cs` into `SiemensS7Demo.ConsoleHost`**

Create `src/SiemensS7Demo.ConsoleHost/SiemensS7Demo.ConsoleHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>SiemensS7Demo.ConsoleHost</AssemblyName>
    <RootNamespace>SiemensS7Demo.ConsoleHost</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SiemensS7Demo.Core\SiemensS7Demo.Core.csproj" />
  </ItemGroup>
</Project>
```

Move the existing top-level statements out of `src/SiemensS7Demo.Core/Program.cs` and into `src/SiemensS7Demo.ConsoleHost/Program.cs` verbatim, then delete `src/SiemensS7Demo.Core/Program.cs`:

```pwsh
git mv src/SiemensS7Demo.Core/Program.cs src/SiemensS7Demo.ConsoleHost/Program.cs
```

The `Program.cs` body is the existing top-level statement block; no edits are needed because the `using SiemensS7Demo.Drivers;` etc. directives still resolve via the Core project reference.

- [ ] **Step 1.3: Update `EnviroEquipmentFinalEdition.sln`**

Open `EnviroEquipmentFinalEdition.sln` and:

1. Remove the existing `SiemensS7Demo` project entry (`{GUID}` block + corresponding `Project` line).
2. Add new entries for `SiemensS7Demo.Core`, `SiemensS7Demo.ConsoleHost`, `SiemensS7Demo.Domain`, `SiemensS7Demo.App`, `SiemensS7Demo.Wpf`, `EnviroEquipment.App.Tests`, `EnviroEquipment.Wpf.Tests`, `EnviroEquipment.E2ETests`.

The simplest reproducible path is:

```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln remove src/SiemensS7Demo.Core/SiemensS7Demo.Core.csproj 2>$null
dotnet sln EnviroEquipmentFinalEdition.sln add src/SiemensS7Demo.Core/SiemensS7Demo.Core.csproj
dotnet sln EnviroEquipmentFinalEdition.sln add src/SiemensS7Demo.ConsoleHost/SiemensS7Demo.ConsoleHost.csproj
```

(The other projects are added in later steps as their csproj files are created.)

Expected output: `Project ... was added successfully.` for each `add` line.

- [ ] **Step 1.4: Update existing test project reference**

Edit `tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj`: replace any `ProjectReference` line that points to `..\..\src\SiemensS7Demo\SiemensS7Demo.csproj` with `..\..\src\SiemensS7Demo.Core\SiemensS7Demo.Core.csproj`.

- [ ] **Step 1.5: Build the whole solution to confirm the rename did not break Core**

```pwsh
dotnet build EnviroEquipmentFinalEdition.sln
```

Expected output: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 1.6: Run the existing Core tests to confirm nothing regressed**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected output: `Passed!  - Failed: 0, Passed: <N>, Skipped: 0` (whatever the current Wave 1 test count is — at minimum > 0 passing).

- [ ] **Step 1.7: Create `SiemensS7Demo.Domain` project**

Create `src/SiemensS7Demo.Domain/SiemensS7Demo.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>SiemensS7Demo.Domain</AssemblyName>
    <RootNamespace>SiemensS7Demo.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

Add a placeholder `src/SiemensS7Demo.Domain/_AssemblyMarker.cs` so the project compiles:

```csharp
namespace SiemensS7Demo.Domain;

/// <summary>Assembly marker; real types are added in subsequent tasks.</summary>
internal static class AssemblyMarker { }
```

Add to solution:
```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln add src/SiemensS7Demo.Domain/SiemensS7Demo.Domain.csproj
```

Expected output: `Project ... was added successfully.`

- [ ] **Step 1.8: Create `SiemensS7Demo.App` project**

Create `src/SiemensS7Demo.App/SiemensS7Demo.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>SiemensS7Demo.App</AssemblyName>
    <RootNamespace>SiemensS7Demo.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
    <PackageReference Include="System.Reactive" Version="6.0.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SiemensS7Demo.Core\SiemensS7Demo.Core.csproj" />
    <ProjectReference Include="..\SiemensS7Demo.Domain\SiemensS7Demo.Domain.csproj" />
  </ItemGroup>
</Project>
```

Add a placeholder `src/SiemensS7Demo.App/_AssemblyMarker.cs`:

```csharp
namespace SiemensS7Demo.App;

internal static class AssemblyMarker { }
```

Add to solution:
```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln add src/SiemensS7Demo.App/SiemensS7Demo.App.csproj
```

Expected output: `Project ... was added successfully.`

- [ ] **Step 1.9: Create `SiemensS7Demo.Wpf` project**

Create `src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>SiemensS7Demo.Wpf</AssemblyName>
    <RootNamespace>SiemensS7Demo.Wpf</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
    <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
    <PackageReference Include="System.Reactive" Version="6.0.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\SiemensS7Demo.Core\SiemensS7Demo.Core.csproj" />
    <ProjectReference Include="..\SiemensS7Demo.Domain\SiemensS7Demo.Domain.csproj" />
    <ProjectReference Include="..\SiemensS7Demo.App\SiemensS7Demo.App.csproj" />
  </ItemGroup>
</Project>
```

Create `src/SiemensS7Demo.Wpf/app.manifest` (default Windows manifest):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="SiemensS7Demo.Wpf"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
      <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}"/>
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 1.10: Create the bare `App.xaml`, `App.xaml.cs`, and a smoke `MainWindow`**

Create `src/SiemensS7Demo.Wpf/App.xaml`:

```xml
<Application x:Class="SiemensS7Demo.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnMainWindowClose">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="/Themes/Tokens.xaml" />
                <ResourceDictionary Source="/Themes/Controls.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Create `src/SiemensS7Demo.Wpf/App.xaml.cs`:

```csharp
using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SiemensS7Demo.App;
using SiemensS7Demo.Wpf.Smoke;
using SiemensS7Demo.Wpf.Views;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Host has not been built yet.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/wpf-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureServices((ctx, services) =>
            {
                services.AddSiemensS7DemoApp();
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<OverviewViewModel>();
                services.AddTransient<SingleDeviceViewModel>();
                services.AddSingleton<Shell>();
                services.AddTransient<HeadlessSmokeRunner>();
            })
            .Build();

        await _host.StartAsync();

        if (TryGetHeadlessSwitch(e.Args))
        {
            var runner = _host.Services.GetRequiredService<HeadlessSmokeRunner>();
            var exitCode = await runner.RunAsync();
            Shutdown(exitCode);
            return;
        }

        var shell = _host.Services.GetRequiredService<Shell>();
        shell.DataContext = _host.Services.GetRequiredService<ShellViewModel>();
        shell.Show();
        MainWindow = shell;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static bool TryGetHeadlessSwitch(string[] args)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, "--headless-smoke", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
```

Add stub files so the build succeeds in this step (the real implementations land in later tasks). The stubs are intentionally minimal — every method body is filled in by the later milestone.

Create `src/SiemensS7Demo.App/AppServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace SiemensS7Demo.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddSiemensS7DemoApp(this IServiceCollection services)
    {
        // Real registrations land in Task 3. M1.1 only needs the method to exist.
        return services;
    }
}
```

Create `src/SiemensS7Demo.Wpf/Themes/Tokens.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Token resources land in Task 2 (M1.2). Placeholder so App.xaml merges cleanly. -->
    <SolidColorBrush x:Key="BrushBg1" Color="#F6F8FB" />
    <SolidColorBrush x:Key="BrushTxt0" Color="#0F172A" />
</ResourceDictionary>
```

Create `src/SiemensS7Demo.Wpf/Themes/Controls.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Control templates land in Task 2 (M1.2). Empty placeholder for now. -->
</ResourceDictionary>
```

Create `src/SiemensS7Demo.Wpf/Views/Shell.xaml`:

```xml
<Window x:Class="SiemensS7Demo.Wpf.Views.Shell"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="温箱控制系统"
        Width="1280" Height="800"
        Background="{StaticResource BrushBg1}">
    <Grid>
        <TextBlock Text="温箱 · Boot smoke window"
                   Foreground="{StaticResource BrushTxt0}"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   FontSize="20" />
    </Grid>
</Window>
```

Create `src/SiemensS7Demo.Wpf/Views/Shell.xaml.cs`:

```csharp
using System.Windows;

namespace SiemensS7Demo.Wpf.Views;

public partial class Shell : Window
{
    public Shell()
    {
        InitializeComponent();
    }
}
```

Create `src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "温箱控制系统";

    // Full M1.2/M1.4/M1.5 fields are added in later tasks.
}
```

Create `src/SiemensS7Demo.Wpf/Smoke/HeadlessSmokeRunner.cs`:

```csharp
using System.Threading.Tasks;

namespace SiemensS7Demo.Wpf.Smoke;

public sealed class HeadlessSmokeRunner
{
    public Task<int> RunAsync()
    {
        // Real implementation lands in Task 6 (M1.6).
        return Task.FromResult(0);
    }
}
```

Create empty stubs `src/SiemensS7Demo.Wpf/ViewModels/OverviewViewModel.cs` and `src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject
{
    // Implemented in Task 4 (M1.4).
}
```

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class SingleDeviceViewModel : ObservableObject
{
    // Implemented in Task 5 (M1.5).
}
```

- [ ] **Step 1.11: Add Wpf project to solution**

```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln add src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj
```

Expected output: `Project ... was added successfully.`

- [ ] **Step 1.12: Build and run the smoke window**

```pwsh
dotnet build src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj
```

Expected output: `Build succeeded.` with `0 Error(s)`.

```pwsh
dotnet run --project src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj -- --headless-smoke
```

Expected output: process exits with code 0 within ~3 seconds and stdout contains a Serilog informational line for host startup. No window is shown because `--headless-smoke` short-circuits before `shell.Show()`.

- [ ] **Step 1.13: Commit**

```pwsh
git add src/SiemensS7Demo.Core/ src/SiemensS7Demo.ConsoleHost/ src/SiemensS7Demo.Domain/ src/SiemensS7Demo.App/ src/SiemensS7Demo.Wpf/ tests/EnviroEquipment.Tests/EnviroEquipment.Tests.csproj EnviroEquipmentFinalEdition.sln
git commit -m "feat(phase2/pkg1): bootstrap WPF project + Domain/App layers + ConsoleHost extraction"
```

---

## Task 2: Theme & shell (M1.2)

**Files:** Add real token resources in `Tokens.xaml`, real control templates in `Controls.xaml`, the TopBar/SideNav/StatusBar shell in `Shell.xaml`, the `tools/CssToXaml.ps1` regen script, and parity tests.

- [ ] **Step 2.1: Write failing token-parity test**

Create `tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\SiemensS7Demo.Wpf\SiemensS7Demo.Wpf.csproj" />
  </ItemGroup>
</Project>
```

Create `tests/EnviroEquipment.Wpf.Tests/Themes/TokensTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Themes;

[Trait("Category", "Pkg1")]
public class TokensTests
{
    private static readonly string[] RequiredBrushKeys =
    {
        // Surfaces
        "BrushBg0", "BrushBg1", "BrushBg2", "BrushBg3", "BrushBg4", "BrushBg5",
        // Lines
        "BrushLine1", "BrushLine2", "BrushLine3",
        // Text
        "BrushTxt0", "BrushTxt1", "BrushTxt2", "BrushTxt3", "BrushTxt4",
        // Accent
        "BrushSteel", "BrushSteelDim",
        "BrushCyan", "BrushCyanDim", "BrushCyanBg",
        // Status
        "BrushOk", "BrushOkBg",
        "BrushRun", "BrushRunBg",
        "BrushSched", "BrushSchedBg",
        "BrushPause", "BrushPauseBg",
        "BrushWarn", "BrushWarnBg",
        "BrushAlarm", "BrushAlarmBg", "BrushAlarmStrong",
        "BrushOffline", "BrushOfflineBg",
        // Guide
        "BrushGuide", "BrushGuideBg", "BrushGuideBorder",
        // Series
        "BrushSeriesTemp", "BrushSeriesHumid", "BrushSeriesPress", "BrushSeriesSet",
    };

    private static readonly (string Key, string Hex)[] SpotCheckBrushes =
    {
        ("BrushBg1", "#F6F8FB"),
        ("BrushTxt0", "#0F172A"),
        ("BrushCyan", "#0E7CB5"),
        ("BrushRun", "#0E7CB5"),
        ("BrushAlarm", "#DC2626"),
        ("BrushWarn", "#D97706"),
        ("BrushOk", "#16A34A"),
        ("BrushOffline", "#94A3B8"),
        ("BrushSeriesTemp", "#DC2626"),
        ("BrushSeriesHumid", "#0E7CB5"),
    };

    private static ResourceDictionary LoadTokens()
    {
        var uri = new Uri("pack://application:,,,/SiemensS7Demo.Wpf;component/Themes/Tokens.xaml", UriKind.Absolute);
        return (ResourceDictionary)Application.LoadComponent(uri);
    }

    [Fact]
    public void Tokens_ContainAllRequiredBrushKeys()
    {
        var dict = LoadTokens();
        var missing = new List<string>();
        foreach (var key in RequiredBrushKeys)
        {
            if (!dict.Contains(key))
            {
                missing.Add(key);
            }
        }
        missing.Should().BeEmpty("every CSS custom property must have a XAML brush counterpart");
    }

    [Theory]
    [MemberData(nameof(SpotCheckData))]
    public void Tokens_SpotCheckMatchesCssHex(string key, string expectedHex)
    {
        var dict = LoadTokens();
        dict.Contains(key).Should().BeTrue($"{key} must exist");
        var brush = dict[key] as SolidColorBrush;
        brush.Should().NotBeNull($"{key} must be a SolidColorBrush");
        var actual = $"#{brush!.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        actual.Should().BeEquivalentTo(expectedHex, $"{key} must match styles.css");
    }

    public static IEnumerable<object[]> SpotCheckData()
    {
        foreach (var t in SpotCheckBrushes)
        {
            yield return new object[] { t.Key, t.Hex };
        }
    }
}
```

Add the project to the solution:
```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln add tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj
```

Expected output: `Project ... was added successfully.`

- [ ] **Step 2.2: Run, confirm failure**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~TokensTests"
```

Expected output: `Failed!  - Failed: 11, Passed: 0` (1 parity test + 10 spot-check rows; current `Tokens.xaml` only has `BrushBg1` and `BrushTxt0`).

- [ ] **Step 2.3: Write the CSS-to-XAML regen script**

Create `tools/CssToXaml.ps1`:

```pwsh
[CmdletBinding()]
param(
    [string]$CssPath = "$PSScriptRoot/../温箱202605/styles.css",
    [string]$OutPath = "$PSScriptRoot/../src/SiemensS7Demo.Wpf/Themes/Tokens.xaml"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CssPath)) {
    throw "CSS not found at $CssPath"
}

$content = Get-Content -LiteralPath $CssPath -Raw

# Match the :root { ... } block only (skip :root.theme-night and tweak rules in pass 1)
$match = [regex]::Match($content, ":root\s*\{(?<body>[^}]*)\}", 'IgnoreCase')
if (-not $match.Success) {
    throw "No :root block found in $CssPath"
}

$body = $match.Groups['body'].Value
$varRe = '(?m)^\s*--(?<name>[a-z0-9\-]+)\s*:\s*(?<value>[^;]+);'
$matches = [regex]::Matches($body, $varRe)

# CSS custom property name -> XAML resource key
function ConvertTo-Key([string]$n) {
    $segments = $n -split '-' | ForEach-Object {
        if ($_.Length -gt 0) { $_.Substring(0,1).ToUpper() + $_.Substring(1) } else { '' }
    }
    return "Brush" + ($segments -join '')
}

function ConvertHexShortToFull([string]$hex) {
    if ($hex.Length -eq 4) {
        $r = $hex[1]; $g = $hex[2]; $b = $hex[3]
        return "#$r$r$g$g$b$b"
    }
    return $hex
}

function ConvertColor([string]$v) {
    $v = $v.Trim()
    if ($v.StartsWith('#')) {
        return (ConvertHexShortToFull $v).ToUpper()
    }
    $m = [regex]::Match($v, 'rgba?\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)(?:\s*,\s*(?<a>[\d.]+))?\s*\)')
    if ($m.Success) {
        $r = [int]$m.Groups['r'].Value
        $g = [int]$m.Groups['g'].Value
        $b = [int]$m.Groups['b'].Value
        if ($m.Groups['a'].Success) {
            $a = [int]([math]::Round([double]$m.Groups['a'].Value * 255))
            return "#{0:X2}{1:X2}{2:X2}{3:X2}" -f $a, $r, $g, $b
        }
        return "#{0:X2}{1:X2}{2:X2}" -f $r, $g, $b
    }
    return $null
}

$brushLines = New-Object System.Collections.Generic.List[string]
$thicknessLines = New-Object System.Collections.Generic.List[string]
$fontLines = New-Object System.Collections.Generic.List[string]

foreach ($m in $matches) {
    $name = $m.Groups['name'].Value
    $value = $m.Groups['value'].Value.Trim()
    $key = ConvertTo-Key $name

    if ($name -like 'font-*') {
        # Strip stack quotes -> first family
        $first = ($value -split ',')[0].Trim().Trim('"').Trim("'")
        $fontLines.Add("    <FontFamily x:Key=`"$key`">$first</FontFamily>")
        continue
    }
    if ($name -like 'r-*' -or $name -in @('row-h','tab-h','bar-h')) {
        $px = ($value -replace 'px','').Trim()
        $thicknessLines.Add("    <sys:Double x:Key=`"$key`">$px</sys:Double>")
        continue
    }
    $hex = ConvertColor $value
    if ($null -ne $hex) {
        $brushLines.Add("    <SolidColorBrush x:Key=`"$key`" Color=`"$hex`" />")
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
[void]$sb.AppendLine('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
[void]$sb.AppendLine('                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">')
[void]$sb.AppendLine('    <!-- Generated from 温箱202605/styles.css via tools/CssToXaml.ps1. Do not hand-edit. -->')
foreach ($l in $brushLines)    { [void]$sb.AppendLine($l) }
foreach ($l in $fontLines)     { [void]$sb.AppendLine($l) }
foreach ($l in $thicknessLines){ [void]$sb.AppendLine($l) }
[void]$sb.AppendLine('</ResourceDictionary>')

$dir = Split-Path -Parent $OutPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
Set-Content -LiteralPath $OutPath -Value $sb.ToString() -Encoding utf8 -NoNewline

Write-Output "Wrote $($brushLines.Count) brushes, $($fontLines.Count) fonts, $($thicknessLines.Count) doubles -> $OutPath"
```

- [ ] **Step 2.4: Run the regen script**

```pwsh
pwsh tools/CssToXaml.ps1
```

Expected output: a single line of the form `Wrote 47 brushes, 2 fonts, 6 doubles -> .../Themes/Tokens.xaml` (counts are approximate; the important assertion is the file is replaced).

- [ ] **Step 2.5: Verify generated tokens compile + parity test passes**

```pwsh
dotnet build src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj
```

Expected output: `Build succeeded.` with `0 Error(s)`.

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~TokensTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 11`.

- [ ] **Step 2.6: Author `Controls.xaml` styles referenced by the shell**

Overwrite `src/SiemensS7Demo.Wpf/Themes/Controls.xaml` with the real control surface:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Style x:Key="LabelText" TargetType="TextBlock">
        <Setter Property="Foreground" Value="{StaticResource BrushTxt2}" />
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="SemiBold" />
    </Style>

    <Style x:Key="MonoText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{StaticResource BrushFontMono}" />
        <Setter Property="Foreground" Value="{StaticResource BrushTxt0}" />
    </Style>

    <Style x:Key="TopBarSurface" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource BrushBg2}" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushLine1}" />
        <Setter Property="BorderThickness" Value="0,0,0,1" />
        <Setter Property="Height" Value="56" />
    </Style>

    <Style x:Key="SideNavSurface" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource BrushBg2}" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushLine1}" />
        <Setter Property="BorderThickness" Value="0,0,1,0" />
        <Setter Property="Width" Value="232" />
    </Style>

    <Style x:Key="StatusBarSurface" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource BrushBg2}" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushLine1}" />
        <Setter Property="BorderThickness" Value="0,1,0,0" />
        <Setter Property="Height" Value="30" />
    </Style>

    <Style x:Key="DeviceCardSurface" TargetType="Border">
        <Setter Property="Background" Value="{StaticResource BrushBg2}" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushLine1}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="4" />
        <Setter Property="Padding" Value="12" />
    </Style>

    <Style x:Key="PrimaryButton" TargetType="Button">
        <Setter Property="Background" Value="{StaticResource BrushCyan}" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushCyan}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Height" Value="32" />
        <Setter Property="Padding" Value="14,0" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="FontWeight" Value="Medium" />
    </Style>

    <Style x:Key="DangerButton" TargetType="Button" BasedOn="{StaticResource PrimaryButton}">
        <Setter Property="Background" Value="{StaticResource BrushAlarm}" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushAlarm}" />
    </Style>

    <Style x:Key="GhostButton" TargetType="Button">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{StaticResource BrushTxt1}" />
        <Setter Property="BorderBrush" Value="{StaticResource BrushLine2}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Height" Value="26" />
        <Setter Property="Padding" Value="10,0" />
        <Setter Property="FontSize" Value="12" />
    </Style>

</ResourceDictionary>
```

NOTE: the regen script writes `BrushFontUi` and `BrushFontMono` for the `--font-ui` / `--font-mono` properties. The `StaticResource BrushFontMono` reference above resolves to a `FontFamily` because the regen script emits `<FontFamily x:Key="BrushFontMono">...`.

- [ ] **Step 2.7: Write a failing shell-routing test**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/ShellViewModelTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class ShellViewModelTests
{
    [Fact]
    public void Ctor_DefaultsToOverview()
    {
        var vm = new ShellViewModel();
        vm.ActiveScreen.Should().Be("overview");
    }

    [Fact]
    public void NavigateCommand_ChangesActiveScreen()
    {
        var vm = new ShellViewModel();
        vm.NavigateCommand.Execute("single");
        vm.ActiveScreen.Should().Be("single");
    }

    [Fact]
    public void NavItems_AreOrderedAsInDesign()
    {
        var vm = new ShellViewModel();
        vm.NavItems.Should().HaveCountGreaterThanOrEqualTo(2);
        vm.NavItems[0].Id.Should().Be("overview");
        vm.NavItems[1].Id.Should().Be("single");
    }
}
```

- [ ] **Step 2.8: Run, confirm failure**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~ShellViewModelTests"
```

Expected output: `Failed!  - Failed: 3, Passed: 0` (current `ShellViewModel` only has `Title`).

- [ ] **Step 2.9: Implement `ShellViewModel`, `NavItem`, and the full Shell XAML**

Create `src/SiemensS7Demo.Wpf/ViewModels/NavItem.cs`:

```csharp
namespace SiemensS7Demo.Wpf.ViewModels;

public sealed record NavItem(string Id, string Label, string Icon);
```

Overwrite `src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs`:

```csharp
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "温箱控制系统";

    [ObservableProperty]
    private string _activeScreen = "overview";

    [ObservableProperty]
    private int _alarmCount;

    [ObservableProperty]
    private int _warningCount;

    public IReadOnlyList<NavItem> NavItems { get; } = new List<NavItem>
    {
        new("overview", "总览", "grid"),
        new("single",   "单设备", "monitor"),
    };

    [RelayCommand]
    private void Navigate(string id)
    {
        ActiveScreen = id;
    }
}
```

Overwrite `src/SiemensS7Demo.Wpf/Views/Shell.xaml` to reproduce the 202605 TopBar + LeftNav + content router + StatusBar:

```xml
<Window x:Class="SiemensS7Demo.Wpf.Views.Shell"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:SiemensS7Demo.Wpf.ViewModels"
        xmlns:views="clr-namespace:SiemensS7Demo.Wpf.Views"
        Title="温箱控制系统"
        Width="1280" Height="800"
        Background="{StaticResource BrushBg1}">
    <Window.Resources>
        <DataTemplate DataType="{x:Type vm:OverviewViewModel}">
            <views:OverviewView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:SingleDeviceViewModel}">
            <views:SingleDeviceView />
        </DataTemplate>
    </Window.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- TopBar -->
        <Border Grid.Row="0" Style="{StaticResource TopBarSurface}">
            <DockPanel LastChildFill="False">
                <TextBlock DockPanel.Dock="Left" Margin="16,0,0,0" VerticalAlignment="Center"
                           Foreground="{StaticResource BrushTxt0}" FontSize="13" FontWeight="Medium">
                    温箱 · THERMOTRON CONTROL
                </TextBlock>
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Margin="0,0,16,0" VerticalAlignment="Center">
                    <TextBlock Style="{StaticResource LabelText}" Margin="0,0,8,0">报警</TextBlock>
                    <TextBlock Style="{StaticResource MonoText}" Text="{Binding AlarmCount, StringFormat={}{0:D2}}" />
                    <TextBlock Style="{StaticResource LabelText}" Margin="16,0,8,0">警告</TextBlock>
                    <TextBlock Style="{StaticResource MonoText}" Text="{Binding WarningCount, StringFormat={}{0:D2}}" />
                </StackPanel>
            </DockPanel>
        </Border>

        <!-- Body: SideNav + content router -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <Border Grid.Column="0" Style="{StaticResource SideNavSurface}">
                <ItemsControl ItemsSource="{Binding NavItems}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Command="{Binding DataContext.NavigateCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding Id}"
                                    Style="{StaticResource GhostButton}"
                                    HorizontalContentAlignment="Left"
                                    Margin="8,4" Width="216">
                                <TextBlock Text="{Binding Label}" />
                            </Button>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </Border>

            <ContentControl Grid.Column="1"
                            Content="{Binding ActiveScreenViewModel}" />
        </Grid>

        <!-- StatusBar -->
        <Border Grid.Row="2" Style="{StaticResource StatusBarSurface}">
            <DockPanel LastChildFill="False">
                <TextBlock DockPanel.Dock="Left" Margin="12,0" VerticalAlignment="Center"
                           Style="{StaticResource LabelText}" Text="连接 · OK" />
                <TextBlock DockPanel.Dock="Right" Margin="0,0,12,0" VerticalAlignment="Center"
                           Style="{StaticResource LabelText}" Text="温箱 v4.2.1 · build 20260418" />
            </DockPanel>
        </Border>
    </Grid>
</Window>
```

Add `ActiveScreenViewModel` to `ShellViewModel` so the `ContentControl` has something to bind. Update `src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs` to inject the two child VMs and expose a content selector:

```csharp
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly OverviewViewModel _overview;
    private readonly SingleDeviceViewModel _single;

    public ShellViewModel(OverviewViewModel overview, SingleDeviceViewModel single)
    {
        _overview = overview;
        _single = single;
        _activeScreenViewModel = _overview;
    }

    [ObservableProperty]
    private string _title = "温箱控制系统";

    [ObservableProperty]
    private string _activeScreen = "overview";

    [ObservableProperty]
    private object _activeScreenViewModel;

    [ObservableProperty]
    private int _alarmCount;

    [ObservableProperty]
    private int _warningCount;

    public IReadOnlyList<NavItem> NavItems { get; } = new List<NavItem>
    {
        new("overview", "总览", "grid"),
        new("single",   "单设备", "monitor"),
    };

    [RelayCommand]
    private void Navigate(string id)
    {
        ActiveScreen = id;
        ActiveScreenViewModel = id switch
        {
            "single" => _single,
            _        => _overview,
        };
    }
}
```

Update the test to construct `ShellViewModel` with the now-required child VMs (the child VMs both have parameterless constructors at this point — real DI wiring comes in Task 3+):

Overwrite `tests/EnviroEquipment.Wpf.Tests/ViewModels/ShellViewModelTests.cs`:

```csharp
using FluentAssertions;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class ShellViewModelTests
{
    private static ShellViewModel Build() => new(new OverviewViewModel(), new SingleDeviceViewModel());

    [Fact]
    public void Ctor_DefaultsToOverview()
    {
        var vm = Build();
        vm.ActiveScreen.Should().Be("overview");
        vm.ActiveScreenViewModel.Should().BeOfType<OverviewViewModel>();
    }

    [Fact]
    public void NavigateCommand_ChangesActiveScreen()
    {
        var vm = Build();
        vm.NavigateCommand.Execute("single");
        vm.ActiveScreen.Should().Be("single");
        vm.ActiveScreenViewModel.Should().BeOfType<SingleDeviceViewModel>();
    }

    [Fact]
    public void NavItems_AreOrderedAsInDesign()
    {
        var vm = Build();
        vm.NavItems.Should().HaveCountGreaterThanOrEqualTo(2);
        vm.NavItems[0].Id.Should().Be("overview");
        vm.NavItems[1].Id.Should().Be("single");
    }
}
```

- [ ] **Step 2.10: Run, confirm pass**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~ShellViewModelTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 3`.

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~TokensTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 11`.

- [ ] **Step 2.11: Commit**

```pwsh
git add tools/CssToXaml.ps1 src/SiemensS7Demo.Wpf/Themes/ src/SiemensS7Demo.Wpf/Views/Shell.xaml src/SiemensS7Demo.Wpf/Views/Shell.xaml.cs src/SiemensS7Demo.Wpf/ViewModels/ShellViewModel.cs src/SiemensS7Demo.Wpf/ViewModels/NavItem.cs tests/EnviroEquipment.Wpf.Tests/ EnviroEquipmentFinalEdition.sln
git commit -m "feat(phase2/pkg1): theme tokens generated from styles.css + Shell TopBar/SideNav/StatusBar"
```

---

## Task 3: DeviceSessionManager (M1.3)

**Files:** Domain types (`Device`, `DeviceId`, etc.), `ProjectConfig`, `IDeviceSessionManager` + `DeviceSessionManager`, registration extension, App.Tests project, multi-device lifecycle tests.

- [ ] **Step 3.1: Create Domain types**

Create `src/SiemensS7Demo.Domain/DeviceId.cs`:

```csharp
namespace SiemensS7Demo.Domain;

public sealed record DeviceId(string Value)
{
    public override string ToString() => Value;
}
```

Create `src/SiemensS7Demo.Domain/DeviceStatus.cs`:

```csharp
namespace SiemensS7Demo.Domain;

public enum DeviceStatus
{
    Run,
    Idle,
    Scheduled,
    Paused,
    Alarm,
    Offline
}
```

Create `src/SiemensS7Demo.Domain/DeviceType.cs`:

```csharp
namespace SiemensS7Demo.Domain;

public enum DeviceType
{
    Standard,
    Standard1500,
    LowPressure,
    Shock
}
```

Create `src/SiemensS7Demo.Domain/Setpoints.cs`:

```csharp
namespace SiemensS7Demo.Domain;

public sealed record Setpoints(double? Temp, double? Humidity, double? Pressure);
```

Create `src/SiemensS7Demo.Domain/ReadingSnapshot.cs`:

```csharp
using System;

namespace SiemensS7Demo.Domain;

public sealed record ReadingSnapshot(
    DateTimeOffset At,
    double? Pv,
    double? Sv,
    double? Humid,
    double? HumidSv,
    double? Press,
    double? PressSv);
```

Create `src/SiemensS7Demo.Domain/Device.cs`:

```csharp
namespace SiemensS7Demo.Domain;

public sealed class Device
{
    public required DeviceId Id { get; init; }
    public required string Bay { get; init; }
    public required DeviceType Type { get; init; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public Setpoints Setpoints { get; set; } = new(null, null, null);
    public ReadingSnapshot? LastReading { get; set; }
}
```

Create `src/SiemensS7Demo.Domain/DeviceWriteResult.cs`:

```csharp
namespace SiemensS7Demo.Domain;

public sealed record DeviceWriteResult(bool Ok, string? ErrorCode, string? ErrorMessage)
{
    public static DeviceWriteResult Success() => new(true, null, null);
    public static DeviceWriteResult Failure(string code, string message) => new(false, code, message);
}
```

Create `src/SiemensS7Demo.Domain/ProjectConfig.cs`:

```csharp
using System.Collections.Generic;

namespace SiemensS7Demo.Domain;

public sealed record DeviceProvisioning(
    string Id,
    string Bay,
    DeviceType Type,
    string IpAddress,
    int Port,
    string CpuType,
    short Rack,
    short Slot,
    string PvTagName,
    string SvTagName,
    string PvAddress,
    string SvAddress,
    bool UseInMemoryAdapter);

public sealed record ProjectConfig(IReadOnlyList<DeviceProvisioning> Devices);
```

Delete the placeholder `src/SiemensS7Demo.Domain/_AssemblyMarker.cs`.

- [ ] **Step 3.2: Create the App-Tests project**

Create `tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
    <PackageReference Include="System.Reactive" Version="6.0.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\SiemensS7Demo.App\SiemensS7Demo.App.csproj" />
    <ProjectReference Include="..\..\src\SiemensS7Demo.Core\SiemensS7Demo.Core.csproj" />
    <ProjectReference Include="..\..\src\SiemensS7Demo.Domain\SiemensS7Demo.Domain.csproj" />
  </ItemGroup>
</Project>
```

```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln add tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj
```

Expected output: `Project ... was added successfully.`

- [ ] **Step 3.3: Write failing DeviceSessionManager tests**

Create `tests/EnviroEquipment.App.Tests/DeviceSessionManagerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using Xunit;

namespace EnviroEquipment.App.Tests;

[Trait("Category", "Pkg1")]
public class DeviceSessionManagerTests
{
    private static ProjectConfig SampleConfig(int count) => new(
        Enumerable.Range(1, count).Select(i => new DeviceProvisioning(
            Id: $"TH-{i:00}",
            Bay: $"A{i}",
            Type: DeviceType.Standard,
            IpAddress: "127.0.0.1",
            Port: 102,
            CpuType: "Mock",
            Rack: 0,
            Slot: 1,
            PvTagName: "Pv",
            SvTagName: "Sv",
            PvAddress: "DB100.DBD10",
            SvAddress: "DB100.DBD14",
            UseInMemoryAdapter: true)).ToList());

    [Fact]
    public async Task ConnectAllAsync_PublishesOneSnapshotPerDevice()
    {
        var config = SampleConfig(3);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        var seen = new List<DeviceId>();
        using var sub = mgr.Devices.Subscribe(d => { lock (seen) { seen.Add(d.Id); } });

        await mgr.ConnectAllAsync(CancellationToken.None);

        // Wait up to 5s for all 3 to publish at least once.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (seen)
            {
                if (seen.Select(x => x.Value).Distinct().Count() >= 3) break;
            }
            await Task.Delay(100);
        }

        lock (seen)
        {
            seen.Select(x => x.Value).Should().Contain(new[] { "TH-01", "TH-02", "TH-03" });
        }

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAllAsync_IsIdempotent()
    {
        var config = SampleConfig(2);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        await mgr.ConnectAllAsync(CancellationToken.None);
        Func<Task> again = () => mgr.ConnectAllAsync(CancellationToken.None);
        await again.Should().NotThrowAsync();

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task WriteSetpointAsync_ReturnsSuccessForKnownDevice()
    {
        var config = SampleConfig(1);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        await mgr.ConnectAllAsync(CancellationToken.None);

        var result = await mgr.WriteSetpointAsync(
            new DeviceId("TH-01"),
            new Setpoints(85.0, null, null),
            CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.ErrorCode.Should().BeNull();

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task WriteSetpointAsync_ReturnsFailureForUnknownDevice()
    {
        var config = SampleConfig(1);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromSeconds(1));

        await mgr.ConnectAllAsync(CancellationToken.None);

        var result = await mgr.WriteSetpointAsync(
            new DeviceId("TH-NOPE"),
            new Setpoints(85.0, null, null),
            CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("UNKNOWN_DEVICE");

        await mgr.DisposeAsync();
    }

    [Fact]
    public async Task Devices_HotStream_ReplaysLastValueToLateSubscribers()
    {
        var config = SampleConfig(1);
        var mgr = new DeviceSessionManager(config, NullLogger<DeviceSessionManager>.Instance,
            pollingInterval: TimeSpan.FromMilliseconds(200));

        await mgr.ConnectAllAsync(CancellationToken.None);
        await Task.Delay(600);

        Device? captured = null;
        using var sub = mgr.Devices.Take(1).Subscribe(d => captured = d);

        await Task.Delay(800);
        captured.Should().NotBeNull();
        captured!.Id.Value.Should().Be("TH-01");

        await mgr.DisposeAsync();
    }
}
```

- [ ] **Step 3.4: Run, confirm failure**

```pwsh
dotnet test tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj
```

Expected output: compile failure — `DeviceSessionManager` and `IDeviceSessionManager` do not exist yet.

- [ ] **Step 3.5: Implement `IRbacContext`, `Role`, `IDeviceSessionManager`, `DeviceSessionManager`**

Create `src/SiemensS7Demo.App/Role.cs`:

```csharp
namespace SiemensS7Demo.App;

public enum Role
{
    Operator,
    Engineer,
    Admin
}
```

Create `src/SiemensS7Demo.App/IRbacContext.cs`:

```csharp
namespace SiemensS7Demo.App;

public interface IRbacContext
{
    Role Current { get; }
    bool IsAtLeast(Role minimum);
}
```

Create `src/SiemensS7Demo.App/AdminRbacContext.cs`:

```csharp
namespace SiemensS7Demo.App;

/// <summary>
/// Stub RBAC context used by Pkg 1. Always reports Admin. The real implementation
/// arrives with Package 4 (Auth/Login).
/// </summary>
public sealed class AdminRbacContext : IRbacContext
{
    public Role Current => Role.Admin;
    public bool IsAtLeast(Role minimum) => true;
}
```

Create `src/SiemensS7Demo.App/IDeviceSessionManager.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.App;

public interface IDeviceSessionManager
{
    IObservable<Device> Devices { get; }
    Task ConnectAllAsync(CancellationToken ct);
    Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct);
}
```

Create `src/SiemensS7Demo.App/DeviceSessionManager.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.App;

public sealed class DeviceSessionManager : IDeviceSessionManager, IAsyncDisposable
{
    private readonly ProjectConfig _config;
    private readonly ILogger<DeviceSessionManager> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly BehaviorSubject<Device> _subject =
        new(new Device { Id = new DeviceId("__sentinel__"), Bay = string.Empty, Type = DeviceType.Standard });

    private readonly ConcurrentDictionary<string, DeviceSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _connectLock = new();
    private bool _connected;
    private CancellationTokenSource? _runCts;

    public DeviceSessionManager(
        ProjectConfig config,
        ILogger<DeviceSessionManager> logger)
        : this(config, logger, TimeSpan.FromSeconds(1)) { }

    public DeviceSessionManager(
        ProjectConfig config,
        ILogger<DeviceSessionManager> logger,
        TimeSpan pollingInterval)
    {
        _config = config;
        _logger = logger;
        _pollingInterval = pollingInterval;
    }

    public IObservable<Device> Devices =>
        _subject.Where(d => d.Id.Value != "__sentinel__");

    public Task ConnectAllAsync(CancellationToken ct)
    {
        lock (_connectLock)
        {
            if (_connected) return Task.CompletedTask;
            _connected = true;
        }

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        foreach (var prov in _config.Devices)
        {
            var session = new DeviceSession(prov, _logger);
            if (_sessions.TryAdd(prov.Id, session))
            {
                _ = Task.Run(() => session.RunAsync(_pollingInterval, _subject, _runCts.Token));
            }
        }
        return Task.CompletedTask;
    }

    public async Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(id.Value, out var session))
        {
            return DeviceWriteResult.Failure("UNKNOWN_DEVICE", $"Unknown device id '{id.Value}'.");
        }

        try
        {
            await session.WriteSetpointAsync(sp, ct);
            return DeviceWriteResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setpoint write failed for {DeviceId}", id.Value);
            return DeviceWriteResult.Failure("WRITE_FAILED", ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _runCts?.Cancel(); } catch { /* ignore */ }
        var tasks = _sessions.Values.Select(s => s.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(tasks);
        _sessions.Clear();
        _subject.OnCompleted();
        _subject.Dispose();
        _runCts?.Dispose();
    }

    private sealed class DeviceSession : IAsyncDisposable
    {
        private readonly DeviceProvisioning _prov;
        private readonly ILogger _logger;
        private readonly SiemensS7Client _client;
        private readonly Device _state;
        private readonly TagDefinition _pvTag;
        private readonly TagDefinition _svTag;
        private bool _connected;

        public DeviceSession(DeviceProvisioning prov, ILogger logger)
        {
            _prov = prov;
            _logger = logger;
            var opts = new PlcConnectionOptions
            {
                Name = prov.Id,
                IpAddress = prov.IpAddress,
                Port = prov.Port,
                CpuType = prov.CpuType,
                Rack = prov.Rack,
                Slot = prov.Slot,
            };
            IS7Adapter adapter = prov.UseInMemoryAdapter
                ? new InMemoryS7Adapter()
                : new InMemoryS7Adapter(); // Real Snap7 adapter is a Pkg 1+ follow-up; InMemory keeps tests offline-safe.
            _client = new SiemensS7Client(opts, adapter);

            _pvTag = new TagDefinition
            {
                Name = prov.PvTagName, DisplayName = prov.PvTagName, Group = "Pv",
                Address = prov.PvAddress, DataType = TagDataType.Real, Unit = "C",
                Access = TagAccess.Read,
            };
            _svTag = new TagDefinition
            {
                Name = prov.SvTagName, DisplayName = prov.SvTagName, Group = "Sv",
                Address = prov.SvAddress, DataType = TagDataType.Real, Unit = "C",
                Access = TagAccess.ReadWrite,
            };
            _state = new Device
            {
                Id = new DeviceId(prov.Id),
                Bay = prov.Bay,
                Type = prov.Type,
                Status = DeviceStatus.Idle,
            };
        }

        public async Task RunAsync(TimeSpan interval, BehaviorSubject<Device> sink, CancellationToken ct)
        {
            try
            {
                await _client.ConnectAsync(ct);
                _connected = true;
                _state.Status = DeviceStatus.Idle;
                sink.OnNext(_state);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var snap = await _client.ReadTagsAsync(new[] { _pvTag, _svTag }, ct);
                        var pv = snap.TryGetValue(_pvTag.Name, out var pvV)
                            ? Convert.ToDouble(pvV.Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (double?)null;
                        var sv = snap.TryGetValue(_svTag.Name, out var svV)
                            ? Convert.ToDouble(svV.Value, System.Globalization.CultureInfo.InvariantCulture)
                            : (double?)null;

                        _state.LastReading = new ReadingSnapshot(
                            DateTimeOffset.UtcNow, pv, sv, null, null, null, null);
                        _state.Setpoints = new Setpoints(sv, null, null);
                        _state.Status = DeviceStatus.Run;
                        sink.OnNext(_state);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Polling failed for {DeviceId}; marking offline.", _prov.Id);
                        _state.Status = DeviceStatus.Offline;
                        sink.OnNext(_state);
                    }

                    try { await Task.Delay(interval, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException) { /* expected on shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session loop crashed for {DeviceId}", _prov.Id);
            }
        }

        public async Task WriteSetpointAsync(Setpoints sp, CancellationToken ct)
        {
            if (sp.Temp is double t)
            {
                await _client.WriteTagAsync(_svTag, (float)t, ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_connected)
                {
                    await _client.DisconnectAsync(CancellationToken.None);
                }
            }
            catch
            {
                // best-effort disconnect during shutdown.
            }
        }
    }
}
```

Update `src/SiemensS7Demo.App/AppServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddSiemensS7DemoApp(this IServiceCollection services)
    {
        services.AddSingleton<IRbacContext, AdminRbacContext>();
        services.AddSingleton(sp =>
        {
            // Default 3-device InMemory config; the WPF app can override before resolve.
            var devs = new[]
            {
                new DeviceProvisioning("TH-01", "A1", DeviceType.Standard, "127.0.0.1", 102, "Mock", 0, 1,
                    "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", true),
                new DeviceProvisioning("TH-02", "A2", DeviceType.Standard, "127.0.0.1", 102, "Mock", 0, 1,
                    "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", true),
                new DeviceProvisioning("TH-03", "A3", DeviceType.Standard, "127.0.0.1", 102, "Mock", 0, 1,
                    "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", true),
            };
            return new ProjectConfig(devs);
        });
        services.AddSingleton<IDeviceSessionManager, DeviceSessionManager>();
        return services;
    }
}
```

- [ ] **Step 3.6: Run, confirm pass**

```pwsh
dotnet test tests/EnviroEquipment.App.Tests/EnviroEquipment.App.Tests.csproj --filter "FullyQualifiedName~DeviceSessionManagerTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 3.7: Commit**

```pwsh
git add src/SiemensS7Demo.Domain/ src/SiemensS7Demo.App/ tests/EnviroEquipment.App.Tests/ EnviroEquipmentFinalEdition.sln
git commit -m "feat(phase2/pkg1): Domain entities + DeviceSessionManager + RBAC stub"
```

---

## Task 4: Overview screen (M1.4)

**Files:** `DeviceCardViewModel`, `OverviewViewModel`, `OverviewView.xaml`, `StatusToBrushConverter`, `StatusToLabelConverter`, and ViewModel tests.

- [ ] **Step 4.1: Write failing OverviewViewModel tests**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/OverviewViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class OverviewViewModelTests
{
    private sealed class FakeSessionManager : IDeviceSessionManager
    {
        private readonly Subject<Device> _subject = new();
        public IObservable<Device> Devices => _subject;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
            => Task.FromResult(DeviceWriteResult.Success());
        public void Push(Device d) => _subject.OnNext(d);
    }

    private static Device Make(string id, DeviceStatus status)
        => new() { Id = new DeviceId(id), Bay = "A1", Type = DeviceType.Standard, Status = status };

    [Fact]
    public void Ctor_StartsWithEmptyGrid()
    {
        var vm = new OverviewViewModel(new FakeSessionManager());
        vm.Cards.Should().BeEmpty();
        vm.OnlineCount.Should().Be(0);
        vm.AlarmCount.Should().Be(0);
    }

    [Fact]
    public async Task IncomingDeviceSnapshot_AddsCard()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);

        vm.Cards.Should().HaveCount(1);
        vm.Cards[0].Id.Should().Be("TH-01");
        vm.Cards[0].Status.Should().Be(DeviceStatus.Run);
    }

    [Fact]
    public async Task SecondSnapshotForSameDevice_UpdatesInPlace()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        fake.Push(Make("TH-01", DeviceStatus.Alarm));
        await Task.Delay(50);

        vm.Cards.Should().HaveCount(1);
        vm.Cards[0].Status.Should().Be(DeviceStatus.Alarm);
    }

    [Fact]
    public async Task SummaryCounters_TrackByStatus()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        fake.Push(Make("TH-02", DeviceStatus.Alarm));
        fake.Push(Make("TH-03", DeviceStatus.Offline));
        await Task.Delay(50);

        vm.OnlineCount.Should().Be(2);
        vm.AlarmCount.Should().Be(1);
        vm.OfflineCount.Should().Be(1);
    }

    [Fact]
    public async Task AlarmPipFlag_FlipsTrueWhenAnyDeviceAlarms()
    {
        var fake = new FakeSessionManager();
        var vm = new OverviewViewModel(fake);
        vm.Subscribe();

        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.AnyAlarm.Should().BeFalse();

        fake.Push(Make("TH-01", DeviceStatus.Alarm));
        await Task.Delay(50);
        vm.AnyAlarm.Should().BeTrue();
    }
}
```

- [ ] **Step 4.2: Run, confirm failure**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~OverviewViewModelTests"
```

Expected output: compile error — `OverviewViewModel` does not yet accept a session manager or expose `Cards` / `Subscribe()`.

- [ ] **Step 4.3: Implement `DeviceCardViewModel`**

Create `src/SiemensS7Demo.Wpf/ViewModels/DeviceCardViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class DeviceCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _bay = string.Empty;

    [ObservableProperty]
    private DeviceType _type;

    [ObservableProperty]
    private DeviceStatus _status;

    [ObservableProperty]
    private double? _pv;

    [ObservableProperty]
    private double? _sv;

    [ObservableProperty]
    private bool _online;

    public void Apply(Device d)
    {
        Id = d.Id.Value;
        Bay = d.Bay;
        Type = d.Type;
        Status = d.Status;
        Pv = d.LastReading?.Pv;
        Sv = d.Setpoints.Temp;
        Online = d.Status != DeviceStatus.Offline;
    }
}
```

- [ ] **Step 4.4: Implement `OverviewViewModel`**

Overwrite `src/SiemensS7Demo.Wpf/ViewModels/OverviewViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceSessionManager? _sessionManager;
    private IDisposable? _subscription;

    public OverviewViewModel() : this(null) { }

    public OverviewViewModel(IDeviceSessionManager? sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public ObservableCollection<DeviceCardViewModel> Cards { get; } = new();

    [ObservableProperty]
    private int _onlineCount;

    [ObservableProperty]
    private int _alarmCount;

    [ObservableProperty]
    private int _offlineCount;

    [ObservableProperty]
    private bool _anyAlarm;

    public void Subscribe()
    {
        if (_sessionManager is null) return;
        _subscription?.Dispose();
        _subscription = _sessionManager.Devices.Subscribe(ApplyOnUi);
    }

    private void ApplyOnUi(Device device)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.Invoke(() => Apply(device));
        }
        else
        {
            Apply(device);
        }
    }

    private void Apply(Device device)
    {
        var existing = Cards.FirstOrDefault(c => c.Id == device.Id.Value);
        if (existing is null)
        {
            var card = new DeviceCardViewModel();
            card.Apply(device);
            Cards.Add(card);
        }
        else
        {
            existing.Apply(device);
        }
        Recompute();
    }

    private void Recompute()
    {
        OnlineCount = Cards.Count(c => c.Online);
        AlarmCount = Cards.Count(c => c.Status == DeviceStatus.Alarm);
        OfflineCount = Cards.Count(c => c.Status == DeviceStatus.Offline);
        AnyAlarm = AlarmCount > 0;
    }

    [RelayCommand]
    private void Refresh() => Recompute();

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
```

- [ ] **Step 4.5: Run, confirm pass on ViewModel tests**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~OverviewViewModelTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 5`.

- [ ] **Step 4.6: Implement value converters**

Create `src/SiemensS7Demo.Wpf/Converters/StatusToBrushConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            DeviceStatus.Run       => "BrushRun",
            DeviceStatus.Idle      => "BrushOk",
            DeviceStatus.Scheduled => "BrushSched",
            DeviceStatus.Paused    => "BrushPause",
            DeviceStatus.Alarm     => "BrushAlarm",
            DeviceStatus.Offline   => "BrushOffline",
            _                      => "BrushTxt2",
        };
        var resource = Application.Current?.Resources[key];
        return resource as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Create `src/SiemensS7Demo.Wpf/Converters/StatusToLabelConverter.cs`:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class StatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            DeviceStatus.Run       => "运行",
            DeviceStatus.Idle      => "待机",
            DeviceStatus.Scheduled => "预约",
            DeviceStatus.Paused    => "暂停",
            DeviceStatus.Alarm     => "报警",
            DeviceStatus.Offline   => "离线",
            _                      => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4.7: Author `OverviewView.xaml`**

Create `src/SiemensS7Demo.Wpf/Views/OverviewView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.OverviewView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:SiemensS7Demo.Wpf.Converters"
             Background="{StaticResource BrushBg1}">
    <UserControl.Resources>
        <conv:StatusToBrushConverter x:Key="StatusToBrush" />
        <conv:StatusToLabelConverter x:Key="StatusToLabel" />
    </UserControl.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Summary strip -->
        <Border Grid.Row="0" Background="{StaticResource BrushBg2}"
                BorderBrush="{StaticResource BrushLine1}" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal" Margin="16,12">
                <StackPanel Margin="0,0,28,0">
                    <TextBlock Style="{StaticResource LabelText}" Text="在线" />
                    <TextBlock Style="{StaticResource MonoText}" FontSize="28"
                               Foreground="{StaticResource BrushOk}"
                               Text="{Binding OnlineCount, StringFormat={}{0:D2}}" />
                </StackPanel>
                <StackPanel Margin="0,0,28,0">
                    <TextBlock Style="{StaticResource LabelText}" Text="报警" />
                    <TextBlock Style="{StaticResource MonoText}" FontSize="28"
                               Foreground="{StaticResource BrushAlarm}"
                               Text="{Binding AlarmCount, StringFormat={}{0:D2}}" />
                </StackPanel>
                <StackPanel Margin="0,0,28,0">
                    <TextBlock Style="{StaticResource LabelText}" Text="离线" />
                    <TextBlock Style="{StaticResource MonoText}" FontSize="28"
                               Foreground="{StaticResource BrushOffline}"
                               Text="{Binding OfflineCount, StringFormat={}{0:D2}}" />
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- 3x3 grid -->
        <ItemsControl Grid.Row="1" ItemsSource="{Binding Cards}" Margin="14">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <UniformGrid Columns="3" Rows="3" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Style="{StaticResource DeviceCardSurface}" Margin="5">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="Auto" />
                            </Grid.RowDefinitions>

                            <!-- Left accent bar -->
                            <Border Width="4" HorizontalAlignment="Left" VerticalAlignment="Stretch"
                                    Grid.Row="0" Grid.RowSpan="3"
                                    Background="{Binding Status, Converter={StaticResource StatusToBrush}}" />

                            <!-- Header -->
                            <DockPanel Grid.Row="0" LastChildFill="False" Margin="14,2,2,8">
                                <TextBlock DockPanel.Dock="Left" Style="{StaticResource MonoText}"
                                           FontSize="15" Text="{Binding Id}" />
                                <Border DockPanel.Dock="Right"
                                        Background="{Binding Status, Converter={StaticResource StatusToBrush}}"
                                        CornerRadius="9" Padding="8,2">
                                    <TextBlock Foreground="White" FontSize="11"
                                               Text="{Binding Status, Converter={StaticResource StatusToLabel}}" />
                                </Border>
                            </DockPanel>

                            <!-- Bay -->
                            <TextBlock Grid.Row="1" Margin="14,0" Style="{StaticResource LabelText}"
                                       Text="{Binding Bay}" />

                            <!-- PV / SV row -->
                            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="14,8,0,4">
                                <StackPanel Margin="0,0,24,0">
                                    <TextBlock Style="{StaticResource LabelText}" Text="温度 PV" />
                                    <TextBlock Style="{StaticResource MonoText}" FontSize="22"
                                               Text="{Binding Pv, StringFormat={}{0:F2}}" />
                                </StackPanel>
                                <StackPanel>
                                    <TextBlock Style="{StaticResource LabelText}" Text="温度 SV" />
                                    <TextBlock Style="{StaticResource MonoText}" FontSize="22"
                                               Foreground="{StaticResource BrushTxt2}"
                                               Text="{Binding Sv, StringFormat={}{0:F2}}" />
                                </StackPanel>
                            </StackPanel>
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Grid>
</UserControl>
```

Create `src/SiemensS7Demo.Wpf/Views/OverviewView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace SiemensS7Demo.Wpf.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4.8: Build and run full Wpf test suite**

```pwsh
dotnet build src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj
```

Expected output: `Build succeeded.` with `0 Error(s)`.

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "Category=Pkg1"
```

Expected output: `Passed!  - Failed: 0, Passed: 19` (11 tokens + 3 shell + 5 overview).

- [ ] **Step 4.9: Commit**

```pwsh
git add src/SiemensS7Demo.Wpf/ViewModels/DeviceCardViewModel.cs src/SiemensS7Demo.Wpf/ViewModels/OverviewViewModel.cs src/SiemensS7Demo.Wpf/Views/OverviewView.xaml src/SiemensS7Demo.Wpf/Views/OverviewView.xaml.cs src/SiemensS7Demo.Wpf/Converters/ tests/EnviroEquipment.Wpf.Tests/ViewModels/OverviewViewModelTests.cs
git commit -m "feat(phase2/pkg1): Overview screen with 9-card grid bound to DeviceSessionManager"
```

---

## Task 5: Single device screen (M1.5)

**Files:** `SingleDeviceViewModel` (with run/pause/stop/reset commands + SV write), `SingleDeviceView.xaml`, ViewModel command tests.

- [ ] **Step 5.1: Write failing SingleDeviceViewModel tests**

Create `tests/EnviroEquipment.Wpf.Tests/ViewModels/SingleDeviceViewModelTests.cs`:

```csharp
using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.ViewModels;

[Trait("Category", "Pkg1")]
public class SingleDeviceViewModelTests
{
    private sealed class FakeSession : IDeviceSessionManager
    {
        private readonly Subject<Device> _subject = new();
        public IObservable<Device> Devices => _subject;
        public Task ConnectAllAsync(CancellationToken ct) => Task.CompletedTask;

        public DeviceId? LastWriteTarget;
        public Setpoints? LastWriteSp;
        public DeviceWriteResult NextResult = DeviceWriteResult.Success();

        public Task<DeviceWriteResult> WriteSetpointAsync(DeviceId id, Setpoints sp, CancellationToken ct)
        {
            LastWriteTarget = id;
            LastWriteSp = sp;
            return Task.FromResult(NextResult);
        }

        public void Push(Device d) => _subject.OnNext(d);
    }

    private static Device Make(string id, DeviceStatus status, double? pv = 25.0, double? sv = 25.0)
        => new()
        {
            Id = new DeviceId(id),
            Bay = "A1",
            Type = DeviceType.Standard,
            Status = status,
            Setpoints = new Setpoints(sv, null, null),
            LastReading = new ReadingSnapshot(DateTimeOffset.UtcNow, pv, sv, null, null, null, null),
        };

    [Fact]
    public void Ctor_StartsWithNoSelection()
    {
        var vm = new SingleDeviceViewModel(new FakeSession(), new AdminRbacContext());
        vm.SelectedDeviceId.Should().BeNull();
        vm.Pv.Should().BeNull();
        vm.Sv.Should().BeNull();
    }

    [Fact]
    public async Task SelectDevice_LoadsLastReading()
    {
        var fake = new FakeSession();
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run, 50.5, 50.0));
        await Task.Delay(50);

        vm.Select("TH-01");
        vm.SelectedDeviceId.Should().Be("TH-01");
        vm.Pv.Should().Be(50.5);
        vm.Sv.Should().Be(50.0);
    }

    [Fact]
    public async Task WriteSetpointCommand_DispatchesToSessionManager()
    {
        var fake = new FakeSession();
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.Select("TH-01");

        vm.NewSvInput = 82.5;
        await vm.WriteSetpointCommand.ExecuteAsync(null);

        fake.LastWriteTarget!.Value.Should().Be("TH-01");
        fake.LastWriteSp!.Temp.Should().Be(82.5);
        vm.LastWriteOk.Should().BeTrue();
    }

    [Fact]
    public async Task WriteSetpoint_PropagatesFailure()
    {
        var fake = new FakeSession { NextResult = DeviceWriteResult.Failure("X", "boom") };
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.Select("TH-01");

        vm.NewSvInput = 60.0;
        await vm.WriteSetpointCommand.ExecuteAsync(null);

        vm.LastWriteOk.Should().BeFalse();
        vm.LastWriteError.Should().Be("boom");
    }

    [Theory]
    [InlineData(DeviceStatus.Idle, true,  false, false, false)]
    [InlineData(DeviceStatus.Run,  false, true,  true,  false)]
    [InlineData(DeviceStatus.Paused, true, false, true, false)]
    [InlineData(DeviceStatus.Alarm, false, false, true, true)]
    [InlineData(DeviceStatus.Offline, false, false, false, false)]
    public async Task CommandEnablement_Matrix(DeviceStatus status, bool canRun, bool canPause, bool canStop, bool canReset)
    {
        var fake = new FakeSession();
        var vm = new SingleDeviceViewModel(fake, new AdminRbacContext());
        vm.Subscribe();
        fake.Push(Make("TH-01", status));
        await Task.Delay(50);
        vm.Select("TH-01");

        vm.RunCommand.CanExecute(null).Should().Be(canRun);
        vm.PauseCommand.CanExecute(null).Should().Be(canPause);
        vm.StopCommand.CanExecute(null).Should().Be(canStop);
        vm.ResetCommand.CanExecute(null).Should().Be(canReset);
    }

    [Fact]
    public async Task RbacOperatorRole_DisablesStop()
    {
        var fake = new FakeSession();
        var rbac = new FixedRbac(Role.Operator);
        var vm = new SingleDeviceViewModel(fake, rbac);
        vm.Subscribe();
        fake.Push(Make("TH-01", DeviceStatus.Run));
        await Task.Delay(50);
        vm.Select("TH-01");
        vm.StopCommand.CanExecute(null).Should().BeFalse();
    }

    private sealed class FixedRbac : IRbacContext
    {
        public FixedRbac(Role r) { Current = r; }
        public Role Current { get; }
        public bool IsAtLeast(Role minimum) => (int)Current >= (int)minimum;
    }
}
```

- [ ] **Step 5.2: Run, confirm failure**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~SingleDeviceViewModelTests"
```

Expected output: compile error — `SingleDeviceViewModel` does not yet accept dependencies or expose `Select`/`RunCommand`/etc.

- [ ] **Step 5.3: Implement `SingleDeviceViewModel`**

Overwrite `src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.ViewModels;

public sealed partial class SingleDeviceViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceSessionManager? _sessionManager;
    private readonly IRbacContext _rbac;
    private readonly Dictionary<string, Device> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private IDisposable? _subscription;

    public SingleDeviceViewModel()
        : this(null, new AdminRbacContext()) { }

    public SingleDeviceViewModel(IDeviceSessionManager? sessionManager, IRbacContext rbac)
    {
        _sessionManager = sessionManager;
        _rbac = rbac;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(WriteSetpointCommand))]
    private string? _selectedDeviceId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private DeviceStatus? _selectedStatus;

    [ObservableProperty]
    private double? _pv;

    [ObservableProperty]
    private double? _sv;

    [ObservableProperty]
    private string _bay = string.Empty;

    [ObservableProperty]
    private double _newSvInput;

    [ObservableProperty]
    private bool _lastWriteOk;

    [ObservableProperty]
    private string? _lastWriteError;

    [ObservableProperty]
    private string _segmentDisplay = "段 —/—";

    public void Subscribe()
    {
        if (_sessionManager is null) return;
        _subscription?.Dispose();
        _subscription = _sessionManager.Devices.Subscribe(ApplyOnUi);
    }

    private void ApplyOnUi(Device d)
    {
        if (Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
        {
            disp.Invoke(() => Apply(d));
        }
        else
        {
            Apply(d);
        }
    }

    private void Apply(Device d)
    {
        _snapshots[d.Id.Value] = d;
        if (SelectedDeviceId == d.Id.Value)
        {
            HydrateFrom(d);
        }
    }

    public void Select(string deviceId)
    {
        SelectedDeviceId = deviceId;
        if (_snapshots.TryGetValue(deviceId, out var d))
        {
            HydrateFrom(d);
        }
    }

    private void HydrateFrom(Device d)
    {
        Bay = d.Bay;
        Pv = d.LastReading?.Pv;
        Sv = d.Setpoints.Temp;
        SelectedStatus = d.Status;
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task WriteSetpointAsync()
    {
        if (_sessionManager is null || SelectedDeviceId is null) return;
        var result = await _sessionManager.WriteSetpointAsync(
            new DeviceId(SelectedDeviceId),
            new Setpoints(NewSvInput, null, null),
            CancellationToken.None);
        LastWriteOk = result.Ok;
        LastWriteError = result.ErrorMessage;
        if (result.Ok) Sv = NewSvInput;
    }

    private bool CanWrite() =>
        SelectedDeviceId is not null
        && SelectedStatus is not (DeviceStatus.Offline or null)
        && _rbac.IsAtLeast(Role.Engineer);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run() { /* command write goes to PLC in Pkg 3 program engine */ }
    private bool CanRun() => SelectedStatus is DeviceStatus.Idle or DeviceStatus.Paused && _rbac.IsAtLeast(Role.Operator);

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() { /* command write goes to PLC in Pkg 3 program engine */ }
    private bool CanPause() => SelectedStatus is DeviceStatus.Run && _rbac.IsAtLeast(Role.Operator);

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() { /* command write goes to PLC in Pkg 3 program engine */ }
    private bool CanStop() => SelectedStatus is DeviceStatus.Run or DeviceStatus.Paused or DeviceStatus.Alarm
        && _rbac.IsAtLeast(Role.Engineer);

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset() { /* alarm reset write — wired in Pkg 2 */ }
    private bool CanReset() => SelectedStatus is DeviceStatus.Alarm && _rbac.IsAtLeast(Role.Operator);

    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
```

- [ ] **Step 5.4: Run, confirm pass**

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "FullyQualifiedName~SingleDeviceViewModelTests"
```

Expected output: `Passed!  - Failed: 0, Passed: 10` (1 ctor + 1 select + 2 write + 5 matrix rows + 1 rbac).

- [ ] **Step 5.5: Author `SingleDeviceView.xaml`**

Create `src/SiemensS7Demo.Wpf/Views/SingleDeviceView.xaml`:

```xml
<UserControl x:Class="SiemensS7Demo.Wpf.Views.SingleDeviceView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:SiemensS7Demo.Wpf.Converters"
             Background="{StaticResource BrushBg1}">
    <UserControl.Resources>
        <conv:StatusToBrushConverter x:Key="StatusToBrush" />
        <conv:StatusToLabelConverter x:Key="StatusToLabel" />
    </UserControl.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Status banner -->
        <Border Grid.Row="0" Padding="14,10"
                Background="{Binding SelectedStatus, Converter={StaticResource StatusToBrush}}">
            <StackPanel Orientation="Horizontal">
                <TextBlock Foreground="White" FontWeight="SemiBold" FontSize="14" Margin="0,0,12,0"
                           Text="{Binding SelectedDeviceId, FallbackValue=未选择设备}" />
                <TextBlock Foreground="White" FontSize="12"
                           Text="{Binding SelectedStatus, Converter={StaticResource StatusToLabel}}" />
                <TextBlock Foreground="White" FontSize="12" Margin="16,0,0,0"
                           Text="{Binding Bay}" />
            </StackPanel>
        </Border>

        <!-- PV / SV hero -->
        <Border Grid.Row="1" Padding="18,16" Background="{StaticResource BrushBg2}"
                BorderBrush="{StaticResource BrushLine1}" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal">
                <StackPanel Margin="0,0,40,0">
                    <TextBlock Style="{StaticResource LabelText}" Text="温度 PV" />
                    <TextBlock Style="{StaticResource MonoText}" FontSize="44"
                               Text="{Binding Pv, StringFormat={}{0:F2}}" />
                </StackPanel>
                <StackPanel Margin="0,0,40,0">
                    <TextBlock Style="{StaticResource LabelText}" Text="温度 SV" />
                    <TextBlock Style="{StaticResource MonoText}" FontSize="44"
                               Foreground="{StaticResource BrushTxt2}"
                               Text="{Binding Sv, StringFormat={}{0:F2}}" />
                </StackPanel>
                <StackPanel>
                    <TextBlock Style="{StaticResource LabelText}" Text="新 SV 写入" />
                    <StackPanel Orientation="Horizontal">
                        <TextBox Width="120" Margin="0,0,8,0"
                                 Text="{Binding NewSvInput, UpdateSourceTrigger=PropertyChanged, StringFormat={}{0:F2}}" />
                        <Button Content="写入" Style="{StaticResource PrimaryButton}"
                                Command="{Binding WriteSetpointCommand}" />
                    </StackPanel>
                    <TextBlock Margin="0,4,0,0" FontSize="11"
                               Foreground="{StaticResource BrushAlarm}"
                               Text="{Binding LastWriteError}"
                               Visibility="{Binding LastWriteOk, Converter={x:Static conv:StatusToLabelConverter.NotVisibleWhenTrue}, FallbackValue=Collapsed}" />
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- Commands -->
        <Border Grid.Row="2" Padding="14,10" Background="{StaticResource BrushBg2}"
                BorderBrush="{StaticResource BrushLine1}" BorderThickness="0,0,0,1">
            <StackPanel Orientation="Horizontal">
                <Button Content="运行"   Style="{StaticResource PrimaryButton}"  Command="{Binding RunCommand}"   Margin="0,0,8,0" />
                <Button Content="暂停"   Style="{StaticResource GhostButton}"    Command="{Binding PauseCommand}" Margin="0,0,8,0" />
                <Button Content="停止"   Style="{StaticResource DangerButton}"   Command="{Binding StopCommand}"  Margin="0,0,8,0" />
                <Button Content="报警复位" Style="{StaticResource GhostButton}"  Command="{Binding ResetCommand}" Margin="0,0,8,0" />
            </StackPanel>
        </Border>

        <!-- Segment placeholder strip -->
        <Border Grid.Row="3" Padding="14,12" Background="{StaticResource BrushBg2}"
                BorderBrush="{StaticResource BrushLine1}" BorderThickness="0,0,0,0">
            <StackPanel>
                <TextBlock Style="{StaticResource LabelText}" Text="程序段进度" />
                <TextBlock Margin="0,4,0,0" Style="{StaticResource MonoText}"
                           Text="{Binding SegmentDisplay}" />
                <TextBlock Margin="0,20,0,0" Style="{StaticResource LabelText}"
                           Text="详细趋势曲线 — Pkg 3 (Program/History/SQLite) 接入" />
            </StackPanel>
        </Border>
    </Grid>
</UserControl>
```

NOTE: the XAML binding `LastWriteOk, Converter={x:Static conv:StatusToLabelConverter.NotVisibleWhenTrue}` references a static helper. Add it to `StatusToLabelConverter`:

Append to `src/SiemensS7Demo.Wpf/Converters/StatusToLabelConverter.cs` (inside the namespace, after the class):

```csharp
public sealed class NotVisibleWhenTrueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

And add a static field on `StatusToLabelConverter` to expose it via `x:Static`:

```csharp
public static readonly NotVisibleWhenTrueConverter NotVisibleWhenTrue = new();
```

So the final `src/SiemensS7Demo.Wpf/Converters/StatusToLabelConverter.cs` is:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.Wpf.Converters;

public sealed class StatusToLabelConverter : IValueConverter
{
    public static readonly NotVisibleWhenTrueConverter NotVisibleWhenTrue = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            DeviceStatus.Run       => "运行",
            DeviceStatus.Idle      => "待机",
            DeviceStatus.Scheduled => "预约",
            DeviceStatus.Paused    => "暂停",
            DeviceStatus.Alarm     => "报警",
            DeviceStatus.Offline   => "离线",
            _                      => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NotVisibleWhenTrueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Create `src/SiemensS7Demo.Wpf/Views/SingleDeviceView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace SiemensS7Demo.Wpf.Views;

public partial class SingleDeviceView : UserControl
{
    public SingleDeviceView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5.6: Build, run full Wpf suite**

```pwsh
dotnet build src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj
```

Expected output: `Build succeeded.` with `0 Error(s)`.

```pwsh
dotnet test tests/EnviroEquipment.Wpf.Tests/EnviroEquipment.Wpf.Tests.csproj --filter "Category=Pkg1"
```

Expected output: `Passed!  - Failed: 0, Passed: 29` (11 tokens + 3 shell + 5 overview + 10 single).

- [ ] **Step 5.7: Commit**

```pwsh
git add src/SiemensS7Demo.Wpf/ViewModels/SingleDeviceViewModel.cs src/SiemensS7Demo.Wpf/Views/SingleDeviceView.xaml src/SiemensS7Demo.Wpf/Views/SingleDeviceView.xaml.cs src/SiemensS7Demo.Wpf/Converters/StatusToLabelConverter.cs tests/EnviroEquipment.Wpf.Tests/ViewModels/SingleDeviceViewModelTests.cs
git commit -m "feat(phase2/pkg1): SingleDeviceView with PV/SV hero, command matrix, SV write"
```

---

## Task 6: E2E smoke (M1.6)

**Files:** `HeadlessSmokeRunner` real implementation, `appsettings.json`, `EnviroEquipment.E2ETests` project with the 3-device InMemory scenario.

- [ ] **Step 6.1: Write failing E2E test**

Create `tests/EnviroEquipment.E2ETests/EnviroEquipment.E2ETests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\SiemensS7Demo.Wpf\SiemensS7Demo.Wpf.csproj" />
    <ProjectReference Include="..\..\src\SiemensS7Demo.App\SiemensS7Demo.App.csproj" />
    <ProjectReference Include="..\..\src\SiemensS7Demo.Domain\SiemensS7Demo.Domain.csproj" />
  </ItemGroup>
</Project>
```

```pwsh
dotnet sln EnviroEquipmentFinalEdition.sln add tests/EnviroEquipment.E2ETests/EnviroEquipment.E2ETests.csproj
```

Expected output: `Project ... was added successfully.`

Create `tests/EnviroEquipment.E2ETests/Pkg1/SmokeTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.Smoke;
using SiemensS7Demo.Wpf.ViewModels;
using Xunit;

namespace EnviroEquipment.E2ETests.Pkg1;

[Trait("Category", "Pkg1")]
public class SmokeTests
{
    [Fact]
    public async Task ThreeDevices_AppearInOverviewAndWriteSvSurvivesReadback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSiemensS7DemoApp();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<SingleDeviceViewModel>();
        services.AddSingleton(sp => new ShellViewModel(
            sp.GetRequiredService<OverviewViewModel>(),
            sp.GetRequiredService<SingleDeviceViewModel>()));
        services.AddTransient<HeadlessSmokeRunner>();

        await using var sp = services.BuildServiceProvider();

        var runner = sp.GetRequiredService<HeadlessSmokeRunner>();
        runner.SessionManager = sp.GetRequiredService<IDeviceSessionManager>();
        runner.Overview = sp.GetRequiredService<OverviewViewModel>();
        runner.Single = sp.GetRequiredService<SingleDeviceViewModel>();

        var exit = await runner.RunAsync();
        exit.Should().Be(0);
        runner.Overview!.Cards.Count.Should().BeGreaterThanOrEqualTo(3);
        runner.Single!.LastWriteOk.Should().BeTrue();
        runner.Single.Sv.Should().Be(77.5);
    }
}
```

- [ ] **Step 6.2: Run, confirm failure**

```pwsh
dotnet test tests/EnviroEquipment.E2ETests/EnviroEquipment.E2ETests.csproj --filter "Category=Pkg1"
```

Expected output: compile error — `HeadlessSmokeRunner` does not have the `SessionManager`/`Overview`/`Single` properties yet (current stub returns 0 immediately with nothing wired).

- [ ] **Step 6.3: Implement `HeadlessSmokeRunner`**

Overwrite `src/SiemensS7Demo.Wpf/Smoke/HeadlessSmokeRunner.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.App;
using SiemensS7Demo.Domain;
using SiemensS7Demo.Wpf.ViewModels;

namespace SiemensS7Demo.Wpf.Smoke;

public sealed class HeadlessSmokeRunner
{
    public IDeviceSessionManager? SessionManager { get; set; }
    public OverviewViewModel? Overview { get; set; }
    public SingleDeviceViewModel? Single { get; set; }

    public async Task<int> RunAsync()
    {
        if (SessionManager is null || Overview is null || Single is null)
        {
            return 2;
        }

        Overview.Subscribe();
        Single.Subscribe();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await SessionManager.ConnectAllAsync(cts.Token);

        // Poll until at least 3 cards arrive (config seeds 3 InMemory devices).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && Overview.Cards.Count < 3)
        {
            await Task.Delay(100, cts.Token);
        }
        if (Overview.Cards.Count < 3)
        {
            return 3;
        }

        // "Click" the first card.
        var firstId = Overview.Cards[0].Id;
        Single.Select(firstId);

        // Write a fresh SV through the UI command path.
        Single.NewSvInput = 77.5;
        await Single.WriteSetpointCommand.ExecuteAsync(null);
        if (!Single.LastWriteOk)
        {
            return 4;
        }

        // Wait for one more poll so the readback reflects the new SV.
        await Task.Delay(1500, cts.Token);

        return 0;
    }
}
```

- [ ] **Step 6.4: Wire the WPF App.xaml.cs to provide the runner with its dependencies**

Update `src/SiemensS7Demo.Wpf/App.xaml.cs`. Replace the `if (TryGetHeadlessSwitch(...))` block with:

```csharp
        if (TryGetHeadlessSwitch(e.Args))
        {
            var runner = _host.Services.GetRequiredService<HeadlessSmokeRunner>();
            runner.SessionManager = _host.Services.GetRequiredService<IDeviceSessionManager>();
            runner.Overview = _host.Services.GetRequiredService<OverviewViewModel>();
            runner.Single = _host.Services.GetRequiredService<SingleDeviceViewModel>();
            var exitCode = await runner.RunAsync();
            Shutdown(exitCode);
            return;
        }
```

Add a `using SiemensS7Demo.App;` directive at the top of `App.xaml.cs` if not already present.

- [ ] **Step 6.5: Create `appsettings.json`**

Create `src/SiemensS7Demo.Wpf/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Devices": [
    { "id": "TH-01", "bay": "A1", "type": "Standard" },
    { "id": "TH-02", "bay": "A2", "type": "Standard" },
    { "id": "TH-03", "bay": "A3", "type": "Standard" }
  ]
}
```

Add to `src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj` inside `<Project>`:

```xml
  <ItemGroup>
    <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 6.6: Run E2E test, confirm pass**

```pwsh
dotnet test tests/EnviroEquipment.E2ETests/EnviroEquipment.E2ETests.csproj --filter "Category=Pkg1"
```

Expected output: `Passed!  - Failed: 0, Passed: 1`.

- [ ] **Step 6.7: Run the WPF headless smoke from the command line**

```pwsh
dotnet run --project src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj -- --headless-smoke
```

Expected output: process exits with code `0`; stdout shows Serilog info lines from `Microsoft.Hosting.Lifetime` and from `DeviceSessionManager` polling.

- [ ] **Step 6.8: Run the full solution test pass**

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg1"
```

Expected output: `Passed!  - Failed: 0, Passed: 35` (5 DSM + 29 Wpf VM/theme + 1 E2E).

```pwsh
dotnet test EnviroEquipmentFinalEdition.sln
```

Expected output: `Passed!  - Failed: 0, Passed: <≥35>, Skipped: 0` (this also runs the pre-existing Core tests; the only regression possible would be from the rename, which Step 1.6 already verified is clean).

- [ ] **Step 6.9: Commit**

```pwsh
git add src/SiemensS7Demo.Wpf/Smoke/HeadlessSmokeRunner.cs src/SiemensS7Demo.Wpf/App.xaml.cs src/SiemensS7Demo.Wpf/appsettings.json src/SiemensS7Demo.Wpf/SiemensS7Demo.Wpf.csproj tests/EnviroEquipment.E2ETests/ EnviroEquipmentFinalEdition.sln
git commit -m "feat(phase2/pkg1): headless E2E smoke wiring 3 InMemory devices through ViewModels"
```

---

## Task 7: Open the PR

- [ ] **Step 7.1: Push branch + open PR**

```pwsh
git push -u origin feat/phase2-pkg1-shell-overview-single
gh pr create --title "Phase 2 Pkg 1: WPF Shell + Overview + Single Device" --body @'
## Summary

Bootstraps the WPF desktop client and implements Milestones M1.1–M1.6 from the Phase 2 design spec:

- M1.1 — Rename `SiemensS7Demo` → `SiemensS7Demo.Core`, extract `SiemensS7Demo.ConsoleHost`, add `SiemensS7Demo.Domain`, `SiemensS7Demo.App`, `SiemensS7Demo.Wpf` projects. WPF host uses `Microsoft.Extensions.Hosting` + `CommunityToolkit.Mvvm`. Smoke window launches.
- M1.2 — `Themes/Tokens.xaml` deterministically generated from `温箱202605/styles.css` via `tools/CssToXaml.ps1`. `Shell.xaml` reproduces the 202605 TopBar + LeftNav + content router + StatusBar. `TokensTests` asserts CSS↔XAML brush parity.
- M1.3 — `IDeviceSessionManager` + `DeviceSessionManager` lifecycle, `ProjectConfig`-driven device list, reactive `Devices` stream, RBAC stub `AdminRbacContext`.
- M1.4 — `OverviewView` 3×3 card grid + `OverviewViewModel`; status pill, online/offline counters, alarm pip on `AnyAlarm`.
- M1.5 — `SingleDeviceView` + `SingleDeviceViewModel`; PV/SV hero, Run/Pause/Stop/Reset command matrix, SV write happy + failure paths, RBAC gating.
- M1.6 — `HeadlessSmokeRunner` boots 3 InMemoryAdapter devices, drives ViewModels end-to-end, asserts SV readback equals written value, exits 0.

## Test plan
- [x] `dotnet test EnviroEquipmentFinalEdition.sln --filter "Category=Pkg1"` — 35 passing
- [x] `dotnet test EnviroEquipmentFinalEdition.sln` — full suite green
- [x] `dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke` — exit code 0
- [x] `pwsh tools/CssToXaml.ps1` is idempotent — re-running produces the same `Tokens.xaml`
- [x] `dotnet run --project src/SiemensS7Demo.ConsoleHost` still runs the original console demo

## Out of scope (handled by later packages)
- Alarm subsystem (Pkg 2)
- Program/History/SQLite (Pkg 3)
- Login/RBAC/LIMS/MQTT/FTP (Pkg 4)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@
```

- [ ] **Step 7.2: Report PR URL to the lead**

Use `SendMessage` to notify `team-lead` with the PR URL and the test summary line. Wait for review before merging.

---

## Self-Review Checklist

Before declaring task done:

- [ ] All `Category=Pkg1` xunit tests pass: 5 (DSM) + 11 (tokens) + 3 (shell) + 5 (overview) + 10 (single) + 1 (E2E) = 35.
- [ ] `dotnet test EnviroEquipmentFinalEdition.sln` (no filter) is green — the rename of `SiemensS7Demo` → `SiemensS7Demo.Core` did not regress any pre-existing Core test, and `EnviroEquipment.Tests` still references the renamed csproj.
- [ ] `dotnet run --project src/SiemensS7Demo.Wpf -- --headless-smoke` exits 0 and prints Serilog info lines.
- [ ] `dotnet run --project src/SiemensS7Demo.ConsoleHost` still executes the original demo (the `Program.cs` body was moved verbatim).
- [ ] `pwsh tools/CssToXaml.ps1` is deterministic — running it twice produces a byte-identical `Tokens.xaml`.
- [ ] No emojis in code, XAML, commit messages, or PR body.
- [ ] No third-party MVVM framework added (only `CommunityToolkit.Mvvm`); no Prism / Caliburn / ReactiveUI references in any `.csproj`.
- [ ] RBAC stub `AdminRbacContext` is the only `IRbacContext` registered in DI; Pkg 4 will replace it.
