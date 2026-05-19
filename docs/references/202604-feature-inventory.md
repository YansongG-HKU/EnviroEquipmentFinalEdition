# 202604 Feature Inventory

`EnviroEquipmentFinalEdition_202604/` is the legacy **Qt 5 / C++** desktop application that the new
Phase 2 WPF client inherits its operational behavior from. The codebase is organised as an
"AppFramework" bundle host (`Code/AppFramework.sln`) plus a `Code/Library/FrameworkCore` runtime,
a tree of MVVM service bundles under `Code/Model/` and `Code/ViewModel/`, view-side widgets under
`Code/View/`, a `TCPmod/` Qt-Widgets test client, and a ship/runtime tree under `Code/Bin/Release/`
that contains the device protocol XML plus packaged binaries. This document tells contributors
picking up Phase 2 work which legacy subsystem maps to which new package, so you can read the
relevant `*.cpp` files alongside the C# code you write without spelunking the whole tree.

Mappings follow the authoritative "Maps to 202604" lines in
`docs/superpowers/specs/2026-05-15-phase2-wpf-client-design.md` §4, extended with what was
discovered while enumerating the tree. Anything that has no current Phase 2 home is listed under
**Deferred to Phase 3** with the spec §9 Open Questions citation.

---

## Code/Library/FrameworkCore

The framework runtime. Loads bundles described by `Manifest.xml`, wires up an OSGi-style command/
service registry, supplies utility code, and bootstraps the Qt application.

### FrameworkCore/Code/Framework
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `AppFramework.cpp/h` | Top-level bundle host. | Replaced by .NET `IHost` in `SiemensS7Demo.Wpf/App.xaml.cs` (Pkg 1 M1.1). |
| `BundleContext.cpp/h`, `BundleInfo.cpp/h` | Per-bundle lifecycle/metadata. | Replaced by `IServiceProvider` / `Microsoft.Extensions.DependencyInjection` (Pkg 1 M1.1). |
| `Launcher.cpp/h` | Entry-point boot sequence. | Pkg 1 M1.1 (`App.OnStartup`). |
| `Registry.cpp/h`, `iRegistry.h` | Service registry. | Folded into DI container (Pkg 1 M1.1). |
| `ICommand.h`, `iBundleActivator.h`, `iBundleContext.h` | Bundle command surface. | Not ported — CommunityToolkit.Mvvm `[RelayCommand]` covers the WPF needs (Pkg 1 M1.5+). |

### FrameworkCore/Code/Command
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `Command.cpp/h`, `CommandFactory.cpp/h`, `CommandGroup.cpp/h`, `CommandJob.cpp/h`, `CommandManager.cpp/h`, `CommandHeader.h` | Generic deferred-command pipeline. | Subsumed by CommunityToolkit.Mvvm command source-gen + the `App` services in Pkg 1/2/3. |
| `指令框架.vsdx` | Visio diagram of the command framework. | Reference doc only; not ported. |

### FrameworkCore/Code/Config
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `ActivatorHelp.cpp/h`, `BundleConfiguration.cpp/h`, `ConfigFileReader.cpp/h`, `ExtensionNode.cpp/h`, `ManifestHelp.cpp/h` | XML manifest reader for bundle wiring. | Replaced by `Microsoft.Extensions.Configuration` JSON in `App.xaml.cs` (Pkg 1 M1.1). |

### FrameworkCore/Code/Factory
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `BaseFactory.h`, `Factory.h`, `ObjectCreator.h`, `ObjectCreatorHelper.h`, `ObjectCreatorRepository.cpp/h`, `iObjectCreator.h` | Template factories used by the bundle host. | Covered by DI factory registrations in Pkg 1 M1.3 (`DeviceSessionManager`) and Pkg 4 M4.4 (`LimsClient`). |

### FrameworkCore/Code/Services
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `BundleRuntimeService.cpp/h`, `iBundleRuntimeService.h` | Hot-swap bundle ops. | Deferred to Phase 3 (no hot-reload requirement in Phase 2). |
| `ServiceHelper.cpp/h` | Service-lookup convenience. | Covered by DI in Pkg 1. |
| `iAdministrationProvider.h` | Admin interface contract. | Deferred to Phase 3 admin UI. |
| `iExtensionResource.cpp/h` | Bundle resource lookup. | Replaced by WPF `Pack URIs` in Pkg 1 M1.2. |
| `iMVVM.h` | Legacy MVVM contract. | Replaced by `ObservableObject` (Pkg 1 M1.4+). |

### FrameworkCore/Code/Tools
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `CommonTimer.cpp/h` | Qt timer wrapper. | Covered by `System.Threading.PeriodicTimer` in Pkg 1 M1.3. |
| `CrashDump.cpp/h` | Windows mini-dump writer. | Deferred to Phase 3 (Serilog covers structured logs in Phase 2). |
| `Delegate.h`, `FastBinding.h` | Type-erased delegates. | Native C# delegates / events (Pkg 1). |
| `EvProcessDlg.cpp/h`, `MsgBoxDlg.cpp/h`, `ProgressDlgHelper.cpp/h` | Modal/progress dialogs. | Replaced by WPF dialogs in Pkg 1 M1.5 + Pkg 3 M3.3. |
| `Path.cpp/h`, `ScopeGuard.h`, `SingleThreaded.h`, `SmartPtr.h` | Misc utilities. | Native .NET equivalents. |
| `Socket.cpp/h`, `SockectHelper.cpp/h` | Low-level socket helpers. | Covered by `SiemensS7Demo.Core` adapters; not ported separately. |
| `Thread/` | Qt thread wrappers. | Replaced by `Task` / `IAsyncEnumerable` (Pkg 1 M1.3). |

### FrameworkCore/Code/Utils
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `AutoType.hpp`, `Delegate.hpp`, `GeneralFactory.hpp`, `NodeT.hpp`, `Singleton.hpp`, `TimeHelper.hpp`, `UnorderedPairSet.hpp` | Generic helpers. | Native .NET equivalents. |
| `CacheContainer.cpp/h` | In-memory cache. | `Microsoft.Extensions.Caching.Memory` in Pkg 1 M1.3 when needed. |
| `Data.cpp/h`, `Field.cpp/h`, `Property.cpp/h`, `PropertyManager.cpp/h`, `SimpleNode.cpp/h`, `SimpleString.cpp/h` | Legacy reflection/property bag. | Folded into `Domain` records (Pkg 1/3). |
| `File.cpp/h`, `FilePlus.cpp/h`, `IncrementalFileReader.cpp/h`, `MapFile.cpp/h` | File-IO helpers. | Native `System.IO` (Pkg 3 M3.5, Pkg 4 M4.7). |
| `Logger.cpp/h`, `SimpleLog.cpp/h` | Logging facade. | Replaced by Serilog (Pkg 1 M1.1). |
| `MathHelper.cpp/h`, `Random.h` | Math/random helpers. | `System.Math` / `System.Random` (Pkg 3 M3.4). |
| `MessageDigest5.cpp/h` | MD5 helper. | Replaced by Argon2id password hashing (Pkg 4 M4.1) — MD5 is unused in the Phase 2 design. |
| `SafeQueue.hpp`, `SafeVector.hpp` | Lock-protected containers. | `System.Threading.Channels` (Pkg 3 M3.5 `HistoryWriter`). |
| `SerializeManager.cpp/h` | Custom binary serializer. | Replaced by `System.Text.Json` (Pkg 3 M3.2). |
| `SimpleArray.cpp` | Misc array utility. | Native `Span<T>` / `Array`. |
| `QtHelper.hpp` | Qt-specific glue. | N/A (no Qt in Phase 2). |
| `Utils.cpp/h`, `includes.h` | Catch-all utilities. | Folded across `Domain` / `App`. |

### FrameworkCore/Code/ThirdPartyExtension
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `ZeroMQ/`, `libuv/`, `rapidjson/`, `rapidxml/`, `uthash/` | Bundled native deps. | Not ported; .NET has `System.Text.Json`, `System.Xml.Linq`, BCL containers. |
| `md5.cpp/h` | MD5 implementation. | Unused (see above). |

### FrameworkCore/Code/multiLanguage
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `Locale.cpp/h`, `MultiLanguageManager.cpp/h`, `wxLocale.h`, `wxMsgCatalog.cpp/h`, `wxMsgCatalogFile.cpp/h` | gettext-style i18n catalogues. | Deferred — Phase 2 spec §9 Open Questions: "i18n scope" assumes Chinese-only; add `.resx` pair in Pkg 1 M1.2 if multi-language requested. |

### FrameworkCore project files
`Code/Library/FrameworkCore/{FrameworkCore.vcxproj,.vcxproj.filters,.vcxproj.user,frameworkcore_global.h,Code/{HeaderDefine.h, MacroDefine.h, PlatformQtDefine.h, StandardTemplateLibraryDefine.h}}` are build/glue artefacts with no Phase 2 counterpart — the new sln uses `dotnet`.

---

## Code/Model — service bundles

Each `Model/<Service>` directory is one OSGi-style bundle: `<Name>Impl.cpp/h` is the body,
`MyActivator.cpp/h` registers it with the bundle host, `Manifest.xml` declares its services, and
the matching `<name>.vcxproj` builds it as a DLL. The interface (`iXxx.h`) is referenced from
`Code/ServiceHeader/` (see next major section).

### CommunicationService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `S7TCP.cpp/h` | Siemens S7 TCP wire driver. | Already mirrored by `SiemensS7Demo.Core/Drivers/Snap7S7Adapter`; Wave 1/2/3 of `legacy-protocol-coverage-design.md` extends it (Gap #8 batch read). |
| `ModbusTCP.cpp/h`, `ModbusHelper.cpp/h` | Schneider Modbus TCP driver + helpers. | Mirrored by `SiemensS7Demo.Core/Drivers/ModbusTcpAdapter`; Gap #1/#2 extend it for HRF/HRD. |
| `TCPHealth.cpp/h` | TCP keepalive/probe loop. | Folded into `DeviceSessionManager` reconnect logic (Pkg 1 M1.3). |
| `iCommunication.h`, `MyActivator.cpp/h`, `Manifest.xml`, `CommunicationService.vcxproj` | Bundle wiring. | Replaced by DI registration in Pkg 1 M1.1. |

### DataManagerSerivce *(sic — typo preserved in source)*
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `DataManager.cpp/h`, `iDataManager.h` | Per-device tag-value cache and observable. | Replaced by `DeviceSessionManager` + reactive snapshot stream (Pkg 1 M1.3). |
| `MyActivator.cpp/h`, `Manifest.xml`, `DataManagerSerivce.vcxproj` | Bundle wiring. | DI in Pkg 1 M1.1. |

### DataSerivce *(sic)*
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `AddressManage.cpp/h` | Address-string → driver-call planner. | Mirrored by `TagConfigLoader` + `S7AdapterBase.ReadRawBatchAsync` (Wave 1 Gap #6, Wave 2 Gap #8). |
| `S7Data.cpp/h` | S7-side data marshalling. | Folded into `Snap7S7Adapter.ReadRawBatchAsync` (Gap #8). |
| `ModbusData.cpp/h` | Modbus-side data marshalling, incl. 32-bit float. | Mirrored by Wave 1 Gap #1/#2 work in `ModbusTcpAdapter`. |
| `iData.h/cpp` | Service contract. | Replaced by `IS7Adapter`. |
| `MyActivator.cpp/h`, `Manifest.xml`, `DataService.vcxproj` | Bundle wiring. | DI in Pkg 1 M1.1. |

### DeviceService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `DeviceImpl.cpp/h`, `iDevice.h` | Per-device runtime state. | `Device` record + `DeviceSessionManager` (Pkg 1 M1.3/M1.4). |
| `DeviceServiceImpl.cpp/h`, `iDeviceService.h` | Service entry-point. | `IDeviceSessionManager` (Pkg 1 M1.3). |
| `readwriteEnvironment.cpp/h` | Read/write loop on environmental tags. | Polling/write services in `SiemensS7Demo.Core/Services` (already present); Pkg 1 M1.5 wires it to ViewModels. |
| `alarmSoundThread.cpp/h` | Background thread for alarm beeps. | Pkg 2 M2.5 (`AlarmPopupWindow` + system sound). |
| `MyActivator.cpp/h`, `Manifest.xml`, `DeviceService.vcxproj` | Bundle wiring. | DI in Pkg 1 M1.1. |

### FTPService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `FTPServiceImpl.cpp/h`, `iFTPService.h` | FTP upload service. | `IFtpUploader` via FluentFTP (Pkg 4 M4.7). |
| `MyActivator.cpp/h`, `Manifest.xml`, `FTPService.vcxproj/.user` | Bundle wiring. | DI in Pkg 1 M1.1. |

### FileManager
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `FileManagerIFFImpl.cpp/h`, `iFileManager.h`, `LoadData.cpp/h` | Test-data IFF file load/save. | **Deferred to Phase 3** (spec §9 Open Questions: "CenterWindow / FileManager / TaskSerivce inheritance: deferred"). The 202605 mock has no equivalent screen. |
| `MyActivator.cpp/h`, `Manifest.xml`, `FileManager.vcxproj/.filters/.user` | Bundle wiring. | N/A (deferred). |

### LogSerivce *(sic)*
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `logClass.cpp/h`, `logServiceImpl.cpp/h`, `iLog.h`, `iLogService.h` | Per-level logger. | Replaced by Serilog (Pkg 1 M1.1). |
| `MyActivator.cpp/h`, `Manifest.xml`, `LogService.vcxproj` | Bundle wiring. | DI in Pkg 1 M1.1. |

### LoginService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `LoginServiceImpl.cpp/h`, `iLoginService.h` | Username/password verification. | `IAuthService` with Argon2id (Pkg 4 M4.1). |
| `MyActivator.cpp/h`, `Manifest.xml`, `LoginService.vcxproj/.user` | Bundle wiring. | DI in Pkg 1 M1.1. |

### MQTTSerivce *(sic)*
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `MQTTClient.cpp/h` | Qt-MQTT client wrapper. | `IMqttPublisher` via MQTTnet (Pkg 4 M4.6). |
| `MQTTServiceImpl.cpp/h`, `iMQTTService.h` | Per-device publish service. | Pkg 4 M4.6. |
| `MQTTThread.cpp/h` | Background publisher thread. | Folded into background `IHostedService` (Pkg 4 M4.6). |
| `MyActivator.cpp/h`, `Manifest.xml`, `MQTTService.vcxproj` | Bundle wiring. | DI in Pkg 1 M1.1. |

### MainAppService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `MainAppServiceImpl.cpp/h`, `iMainAppService.h` | Top-level app orchestration. | Replaced by `App.xaml.cs` + `IHost` (Pkg 1 M1.1). |
| `Startor.cpp/h` | Startup sequencer. | `App.OnStartup` (Pkg 1 M1.1). |
| `MyActivator.cpp/h`, `Manifest.xml`, `MainAppService.vcxproj/.filters/.user` | Bundle wiring. | DI in Pkg 1 M1.1. |

### SQLITEService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `SQLITEServiceImpl.cpp/h`, `iSQLITEService.h` | Direct SQLite I/O (Qt SQL). | Replaced by EF Core 8 + `EnviroDbContext` (Pkg 3 M3.1). |
| `MyActivator.cpp/h`, `Manifest.xml`, `SQLITEService.vcxproj/.user` | Bundle wiring. | DI in Pkg 1 M1.1. |

### TCPService
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `TcpClient.cpp/h`, `iClient.h` | Generic TCP client for LIMS/back-office. | Body of work for Pkg 4 M4.4 (`LimsClient`); reuse `HttpClient` / `System.Net.Sockets` as appropriate after the M4.4 spike. |
| `WinsockHelp.cpp/h` | Winsock initialization. | Not needed (.NET handles this). |
| `MyActivator.cpp/h`, `Manifest.xml`, `TCPService.vcxproj/.filters/.user` | Bundle wiring. | DI in Pkg 1 M1.1. |

### TaskSerivce *(sic)*
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `TaskImpl.cpp/h`, `iTask.h` | LIMS-task lifecycle. | Partially covered by `LimsTask` + `ILimsClient` (Pkg 4 M4.4/M4.5). |
| `dealTaskThread.cpp/h` | Worker thread polling LIMS tasks. | Background `IHostedService` (Pkg 4 M4.4). |
| `MyActivator.cpp/h`, `Manifest.xml`, `TaskService.vcxproj/.user` | Bundle wiring. | DI in Pkg 1 M1.1. |
| Outstanding generic task scheduler responsibilities | The legacy "task" abstraction is broader than LIMS (chained jobs). | **Deferred to Phase 3** (spec §9 Open Questions). |

---

## Code/ServiceHeader

Public interface headers that other bundles `#include`. These are pure contracts; the implementations live under `Code/Model/`.

| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `iCenterWindow.h` | Centre-window view contract. | **Deferred to Phase 3** (spec §9 Open Questions: "CenterWindow ... deferred"). |
| `iClient.h` | TCP client contract. | Used by Pkg 4 M4.4 reverse-engineering. |
| `iCommunication.h` | Driver contract. | Replaced by `SiemensS7Demo.Core/Drivers/IS7Adapter`. |
| `iData.h` | Tag-data contract. | Replaced by `TagDefinition` + `TagValue` in Core. |
| `iDataManager.h` | Cache contract. | Replaced by `IDeviceSessionManager` (Pkg 1 M1.3). |
| `iDevice.h`, `iDeviceService.h` | Device contracts. | Replaced by `Device` + `IDeviceSessionManager` (Pkg 1). |
| `iLog.h`, `iLogService.h` | Logger contracts. | Replaced by `ILogger` / Serilog (Pkg 1 M1.1). |
| `iMQTTService.h` | MQTT contract. | Replaced by `IMqttPublisher` (Pkg 4 M4.6). |
| `iMainAppService.h` | App-lifecycle contract. | Replaced by `IHost` (Pkg 1 M1.1). |
| `iTask.h` | Task-scheduler contract. | Partially Pkg 4 M4.4/M4.5; generic scheduler **deferred to Phase 3**. |
| `iVMPlotting.h` | Trend ViewModel contract. | Replaced by `HistoryTrendViewModel` (Pkg 3 M3.6). |

---

## Code/View — Qt widgets

### View/LoginWindow
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `LoginWindow.cpp/h`, `iLoginWindow.h` | Main login dialog. | `LoginView.xaml` + `LoginViewModel` (Pkg 4 M4.2). |
| `CustomLineEditBase.cpp/h` | Reusable line-edit control. | Native WPF `TextBox` styled in `Themes/Tokens.xaml` (Pkg 1 M1.2). |
| `WarningDialog.cpp/h` | Auth-error dialog. | WPF `MessageBox` / custom dialog (Pkg 4 M4.2). |
| `MyActivator.cpp/h`, `Manifest.xml`, `LoginWindow.vcxproj/.user/.filters` | Bundle wiring. | DI registration (Pkg 4 M4.2). |
| `Resource.qrc`, `Resources/*.png` | Icons + background. | Re-export to `SiemensS7Demo.Wpf/Resources/Login/` (Pkg 4 M4.2). |

### View/MainWindow
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `MainWindow.cpp/h` | Top-level shell, ribbon, content router. | `Shell.xaml` + `ShellViewModel` (Pkg 1 M1.2). |
| `AlarmCurrentWidget.cpp/h` | Current-alarm dock. | `CurrentAlarmsView` + VM (Pkg 2 M2.3). |
| `AlarmHistoryWidget.cpp/h` | Alarm history dock. | `HistoryAlarmsView` + VM (Pkg 2 M2.4). |
| `AlarmNoticeWidget.cpp/h` | Non-blocking alarm toast. | `AlarmToastHost` (Pkg 2 M2.5). |
| `alarmpopwidget.cpp/h` | Modal alarm popup. | `AlarmPopupWindow` (Pkg 2 M2.5). |
| `BackStageWindow/BackStageConnectDevice.cpp/h` | "Back stage" connection-config screen. | Initial wiring covered by `DeviceSessionManager` config UI (Pkg 1 M1.3); full screen folds into Pkg 4 M4.3 RBAC-gated device config when Pkg 4 ships. |
| `BackStageWindow/BackStageDeviceSetting.cpp/h` | Device parameter editor. | `screens-c.jsx → ScreenDevice` consumer — Pkg 4 M4.3 (RBAC-gated). |
| `BackStageWindow/BackStageLims.cpp/h` | LIMS task-list back-end view. | `LimsView` + VM (Pkg 4 M4.5). |
| `BackStageWindow/BackStagePlotting.cpp/h` | Plot-config back-end. | Folded into `HistoryTrendView` config dialog (Pkg 3 M3.6). |
| `BackStageWindow/CurrentTestRibbonPage.cpp/h` | Ribbon page for live test. | Pkg 1 M1.5 (`SingleDeviceView` action bar). |
| `BackStageWindow/HistoryTestRibbonPage.cpp/h` | Ribbon page for history. | Pkg 3 M3.6 (`HistoryTrendView` toolbar). |
| `BackStageWindow/DialogEditDevice.cpp/h` | Add/edit device dialog. | Pkg 4 M4.3 (RBAC-gated). |
| `BackStageWindow/TestOpenDialog.cpp/h` | "Open test" picker. | Pkg 3 M3.3 (`ProgramEditorView` open-program dialog). |
| `BackStageWindow/autotemperaturecontrolWidget.cpp/h` | Auto-temperature program runtime widget. | `ProgramExecutionService` + `SingleDeviceView` segment indicator (Pkg 1 M1.5 + Pkg 3 M3.4). |
| `BackStageWindow/autostandardcontrolWidget.cpp/h` | Auto-standard (composite) program runtime widget. | Same as above — Pkg 1 M1.5 + Pkg 3 M3.4. |
| `BackStageWindow/rangedialog.cpp/h/.ui` | Y-axis range picker. | Pkg 3 M3.6 (`HistoryTrendView` controls). |
| `BackStageWindow/selectDialog.cpp/h` | Generic selector. | Native WPF dialog (Pkg 1 M1.5). |
| `MyActivator.cpp/h`, `Manifest.xml`, `MainWindow.vcxproj/.user/.filters` | Bundle wiring. | DI registration (Pkg 1 M1.1). |
| `Resource.qrc`, `Resource/{MainIcon, PlotIcon, StartIcon}/*.png` | Toolbar/ribbon icons. | Re-export to `SiemensS7Demo.Wpf/Resources/` (Pkg 1 M1.2). |

### View/CenterWindow
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `CenterWindow.cpp/h`, `iCenterWindow.h` | The "centre" dockable area shell. | **Deferred to Phase 3** — the 202605 mock replaces this with the `AppFrame` + content router (Pkg 1 M1.2), so the legacy concept is intentionally not ported. (Spec §9 Open Questions.) |
| `BigPage.cpp/h`, `SmallPage.cpp/h`, `SmallWidget.cpp/h` | Layout variants for the centre window. | Same — deferred. The Pkg 1 overview/single-device split covers the equivalent UX. |
| `HistoryWidget.cpp/h` | History viewer. | `HistoryTrendView` + VM (Pkg 3 M3.6). |
| `TestImportDialog.cpp/h` | Import-test-data dialog. | Deferred to Phase 3 (linked to `FileManager`). |
| `editgraphwidget.cpp/h` | Graph editor. | Pkg 3 M3.6 (`HistoryTrendView` overlay edit controls). |
| `InfoControlWidget/InfoWidget.cpp/h` | Per-device summary card. | `DeviceCard` in `OverviewView` (Pkg 1 M1.4). |
| `InfoControlWidget/temperaturecontrolWidget.cpp/h` | Temperature-device control panel. | `SingleDeviceView` for `DeviceType.Standard` (Pkg 1 M1.5). |
| `InfoControlWidget/standardcontrolWidget.cpp/h` | Composite chamber control panel. | `SingleDeviceView` for `DeviceType.Standard1500/Shock/LowPressure` (Pkg 1 M1.5). |
| `PlotWidget/defaultPlot.cpp/h` | Base chart widget (QCustomPlot). | Replaced by `OxyPlot.Wpf` base bindings (Pkg 3 M3.6). |
| `PlotWidget/historyplot.cpp/h` | Historical trace plot. | `HistoryTrendView` (Pkg 3 M3.6). |
| `PlotWidget/realtimeplot.cpp/h` | Realtime PV/SV trace. | `SingleDeviceView` mini-chart (Pkg 1 M1.5); pulls from `HistoryWriter` snapshot (Pkg 3 M3.5). |
| `PlotWidget/rangedialog.cpp/h/.ui` | Range picker (duplicate of `BackStageWindow/rangedialog`). | Pkg 3 M3.6. |
| `MyActivator.cpp/h`, `Manifest.xml`, `Resource.qrc`, `CenterWindow.vcxproj/.user/.filters`, `Resource/` | Bundle wiring + icons. | Partially re-exported in Pkg 1 M1.2; legacy "centre window" concept deferred. |

---

## Code/ViewModel — Qt MVVM

### ViewModel/VMPlotting
| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `VMPlotting.cpp/h`, `iVMPlotting.h` | ViewModel for plots. | `HistoryTrendViewModel` (Pkg 3 M3.6) + `SingleDeviceViewModel.SparklineSeries` (Pkg 1 M1.5). |
| `MyActivator.cpp/h`, `Manifest.xml`, `VMPlotting.vcxproj/.user/.filters` | Bundle wiring. | DI in Pkg 1 M1.1. |

ViewModels for non-plot screens were embedded inside the `View/*Widget` files in the legacy
code (no separate VM bundle). Pkg 1/2/3 explicitly split View and ViewModel into the
`SiemensS7Demo.Wpf/Views` and `SiemensS7Demo.Wpf/ViewModels` folders.

---

## Code/AppFrameworkLauncher

| File | Purpose | Phase 2 home |
|------|---------|--------------|
| `main.cpp` | `main()` for the legacy app. | Replaced by `App.xaml.cs` (Pkg 1 M1.1). |
| `Startor.cpp/h` | Boot orchestrator. | `App.OnStartup` (Pkg 1 M1.1). |
| `Model.xml`, `start.xml` | Bundle wiring manifests. | Replaced by `appsettings.json` + DI registrations (Pkg 1 M1.1). |
| `icon1.ico`, `resource.h`, `AppFrameworkLauncher.rc`, `Resource/` | Windows icon + resource script. | Re-export to `SiemensS7Demo.Wpf/Resources/AppIcon.ico` (Pkg 1 M1.1). |
| `AppFrameworkLauncher.vcxproj/.filters/.user` | Build script. | Replaced by `SiemensS7Demo.Wpf.csproj`. |

---

## Code/Bin/Release — runtime assets

| Path | Purpose | Phase 2 home |
|------|---------|--------------|
| `addressProtocol/` | Per-vendor × per-model device tag XML (7 device configs total). | Source of truth for `TagConfigLoader` and `ProjectConfigLoader` — already covered by Wave 1/2/3 gaps in `2026-05-14-legacy-protocol-coverage-design.md` (Gaps #3, #4, #5, #6, #7, #9). The new code lives in `SiemensS7Demo.Core/Services/TagConfigLoader.cs` and consumes these XML files unchanged. |
| `addressProtocol/Siemens/standardBoxDevice/addressConfig.xml` | Standard chamber (S7-200 SMART). | Loaded via Pkg 1 M1.3 device list. |
| `addressProtocol/Siemens/standardBoxDevice(1500)/addressConfig.xml` | 1500 L chamber variant. | Pkg 1 M1.3. |
| `addressProtocol/Siemens/TemperatureShockBoxDevice/addressConfig.xml` | Temperature-shock chamber. | Pkg 1 M1.3. |
| `addressProtocol/Siemens/lowAirPressureDevice/addressConfig.xml` | Low-air-pressure chamber. | Pkg 1 M1.3. |
| `addressProtocol/Schneider/standardBoxDevice/addressConfig.xml` | Schneider variant of standard chamber. | Pkg 1 M1.3 (driven via `ModbusTcpAdapter`). |
| `addressProtocol/Schneider/TemperatureShockBoxDevice/addressConfig.xml` | Schneider temperature-shock. | Pkg 1 M1.3. |
| `addressProtocol/Schneider/lowAirPressureDevice/addressConfig.xml` | Schneider low-pressure. | Pkg 1 M1.3. |
| `ProjectData/equipmentConfig.xml` | Project-level device list (vendor × model × bay). | Source for `ProjectConfigLoader` in Pkg 1 M1.3 (Wave 3 Gap #9). |
| `ProjectData/mqttConfig.xml` | MQTT broker config. | Loaded by Pkg 4 M4.6 `MqttPublisher`. |
| `ProjectData/plotConfig.xml` | Default plot ranges. | Loaded by Pkg 3 M3.6 `HistoryTrendViewModel`. |
| `start.xml` | Launcher bundle-load order. | Replaced by DI in `App.xaml.cs` (Pkg 1 M1.1). |
| `Qt5*.dll`, `Qtitan*.dll/.lib`, `D3Dcompiler_47.dll`, `api-ms-win-*`, `concrt140.dll`, `msvcp140*.dll`, `vcruntime140.dll`, `ucrtbase.dll`, `vccorlib140.dll`, `dbghelp.dll`, `freeglut.dll/.lib`, `gc.dll`, `vld_x86.dll`, `iconengines/`, `platforms/`, `sqldrivers/`, `styles/` | Qt 5 + 3rd-party runtime. | N/A — `dotnet publish` produces the self-contained Phase 2 deployment. |

---

## Top-level documents and tooling

| Path | Purpose | Phase 2 home |
|------|---------|--------------|
| `README.md` | Project notes. | Reference only. |
| `addressConfig.xml` (top-level copy) | Stub/example. | Use the per-device files under `Code/Bin/Release/addressProtocol/` instead. |
| `tag_list.lua` | Lua-syntax tag dump for the field NetAssist tool. | Reference only — XML is the source of truth. |
| `NetAssist.cfg`, `NetAssist.exe` | TCP/UDP testing tool. | Replaced by Modbus loopback servers and Snap7 test harness in `tests/EnviroEquipment.Tests`. |
| `V1.0文档/2022-03-03联调收集问题与处理方法.docx` | V1 commissioning issue log. | Reference only. |
| `V1.0文档/测试报告.docx` | V1 test report. | Reference only. |
| `V1.0文档/环境试验箱集中管理系统技术协议—20210630.docx` | V1 technical specification. | Reference for Pkg 1/2/3 acceptance criteria. |
| `V1.0文档/用户使用手册.docx` | V1 user manual. | Reference for the operational mental model the 202605 mock formalises. |
| `V1.0文档/研制总结报告.docx`, `软件验收报告.docx`, `需求分析报告.docx` | V1 retrospectives. | Reference only. |
| `V2.0文档/环境试验箱集中管理系统二期使用手册.docx` | V2 user manual. | Reference for Pkg 4 M4.4/M4.5 (LIMS UI). |
| `V2.0文档/环境试验箱集中管理系统二期技术协议与概要设计.docx` | V2 tech spec + design. | Reference for Pkg 4 M4.4 LIMS protocol reverse-engineering spike (mitigation in spec §8). |
| `V2.0文档/环境试验箱集中管理系统二期需求分析报告.docx` | V2 requirements. | Reference for Pkg 4 scope clarification. |
| `V2.0文档/环境试验箱集中管理系统二期测试报告.docx`, `研制总结报告.docx`, `项目二期验收报告.docx` | V2 retrospectives. | Reference only. |
| `V2.0文档/环境试验箱集中管理系统与黑灯系统交互流程-20221026.docx` | "Lights-out" lab integration flow. | Reference for Pkg 4 M4.4/M4.5/M4.6 (LIMS + MQTT). |
| `V2.0文档/环境试验箱集中管理系统与黑灯系统通信协议(v1.2)_20230424.docx` | Lights-out comm protocol. | Authoritative source for Pkg 4 M4.6 MQTT topic schema. |
| `文档/基于航天云检平台的气候环境黑灯试验系统(环境试验箱管理软件版本).docx` | Cloud-platform integration overview. | Reference for Pkg 4 M4.6 MQTT topology. |
| `西门子协议.txt` | Field notes on Siemens S7 protocol. | Reference for Wave 2 Gap #8 batch-read tuning. |
| `试验箱情景分析.vsdx` | Visio scenario diagram. | Reference for Pkg 3 M3.4 state-machine acceptance. |
| `软著（公开）.pdf` | Public software-copyright registration document. | Reference only — confirms the legacy app boundary. |
| `702试验箱进度表.xlsx` (also at `Code/`) | Project schedule spreadsheet. | Reference only. |
| `地址（按界面）(老版本).xlsx`, `地址（按界面）.xlsx` | Tag dictionary in spreadsheet form (old + current). | Reference cross-check against `addressProtocol/*.xml`; the XML is authoritative. |
| `TCPmod/main.cpp/mainwindow.cpp/.h/.ui/TCPmod.pro/102/` | Standalone Qt Widgets TCP test client. | Replaced by Modbus + Snap7 loopback in `tests/EnviroEquipment.Tests`. |
| `Code/AppFramework.sln`, `Code/ThirdPartLibrary/` | Legacy solution + bundled native libs (QCustomPlot, QtMqtt, QtitianLibrary, RunTimeLibrary, Visual Leak Detector, curl-7.61.0, freeglut, qtsingleapplication). | Replaced by `EnviroEquipmentFinalEdition.sln` + NuGet packages. |

---

## Mapping summary

| 202604 subsystem | Phase 2 Pkg | Status |
|------------------|-------------|--------|
| `Library/FrameworkCore` runtime + DI | Pkg 1 M1.1 (`IHost` bootstrap) | Replaced |
| `Model/CommunicationService` (S7 + Modbus + TCP health) | Pkg 1 M1.3 + Wave 1/2/3 Core gaps | Mirrored, extended |
| `Model/DataManagerSerivce` | Pkg 1 M1.3 (`DeviceSessionManager`) | Replaced |
| `Model/DataSerivce` (address synthesis + driver-side marshalling) | Wave 1 Gap #6 + Wave 2 Gap #8 in Core | Mirrored |
| `Model/DeviceService` | Pkg 1 M1.3 / M1.4 / M1.5 | Mirrored |
| `Model/FTPService` | Pkg 4 M4.7 | Mirrored |
| `Model/FileManager` | — | **Deferred to Phase 3** (spec §9) |
| `Model/LogSerivce` | Pkg 1 M1.1 (Serilog) | Replaced |
| `Model/LoginService` | Pkg 4 M4.1 | Mirrored |
| `Model/MQTTSerivce` | Pkg 4 M4.6 | Mirrored |
| `Model/MainAppService` | Pkg 1 M1.1 | Replaced |
| `Model/SQLITEService` | Pkg 3 M3.1 (EF Core 8) | Replaced |
| `Model/TCPService` | Pkg 4 M4.4 (LIMS over TCP) | Mirrored |
| `Model/TaskSerivce` (LIMS tasks subset) | Pkg 4 M4.4 / M4.5 | Mirrored partially |
| `Model/TaskSerivce` (generic scheduler subset) | — | **Deferred to Phase 3** (spec §9) |
| `ServiceHeader/*.h` interfaces | Folded into Pkg 1–4 contracts | Replaced |
| `View/LoginWindow` | Pkg 4 M4.2 | Mirrored |
| `View/MainWindow` shell + ribbon | Pkg 1 M1.2 | Mirrored |
| `View/MainWindow` alarm widgets (current/history/notice/popup) | Pkg 2 M2.3–M2.5 | Mirrored |
| `View/MainWindow/BackStageWindow` auto-program widgets | Pkg 1 M1.5 + Pkg 3 M3.3/M3.4 | Mirrored |
| `View/MainWindow/BackStageWindow/BackStageConnectDevice/DeviceSetting/DialogEditDevice/TestOpenDialog` | Pkg 1 M1.3 (initial) + Pkg 4 M4.3 (RBAC-gated) | Mirrored |
| `View/MainWindow/BackStageWindow/BackStageLims` | Pkg 4 M4.5 | Mirrored |
| `View/MainWindow/BackStageWindow/BackStagePlotting` + `View/CenterWindow/PlotWidget` | Pkg 3 M3.6 | Mirrored |
| `View/CenterWindow` shell (BigPage/SmallPage/SmallWidget/CenterWindow) | — | **Deferred to Phase 3** (spec §9; 202605 replaces it with `AppFrame`) |
| `View/CenterWindow/HistoryWidget` + `editgraphwidget` | Pkg 3 M3.6 | Mirrored |
| `View/CenterWindow/InfoControlWidget` (info + temp + standard control) | Pkg 1 M1.4 + M1.5 | Mirrored |
| `View/CenterWindow/TestImportDialog` | — | **Deferred to Phase 3** (linked to FileManager) |
| `ViewModel/VMPlotting` | Pkg 3 M3.6 + Pkg 1 M1.5 | Mirrored |
| `AppFrameworkLauncher` | Pkg 1 M1.1 | Replaced |
| `Bin/Release/addressProtocol/*` | Loaded as-is by Wave 1 Core | Reused unchanged |
| `Bin/Release/ProjectData/{equipmentConfig,mqttConfig,plotConfig}.xml` | Wave 3 Gap #9 + Pkg 4 M4.6 + Pkg 3 M3.6 | Reused / regenerated |
| `TCPmod/` test client | Replaced by loopback servers in `tests/EnviroEquipment.Tests` | Replaced |
| `V1.0文档/`, `V2.0文档/`, `文档/`, `西门子协议.txt`, `试验箱情景分析.vsdx`, `软著（公开）.pdf`, `702试验箱进度表.xlsx`, `地址（按界面）*.xlsx` | Reference docs | Read-only |
| `NetAssist.exe`, `NetAssist.cfg`, `tag_list.lua` | Field-debug tooling | Reference only |
| Top-level `addressConfig.xml` stub | Superseded by per-device files | Ignored |

---

## Deferred to Phase 3

Items below have no Phase 2 home. They are captured here so that, when Phase 3 starts, the
backlog is explicit rather than implicit. Each cites spec §9 Open Questions of
`docs/superpowers/specs/2026-05-15-phase2-wpf-client-design.md` as the source of the deferral
decision.

- `Code/Model/FileManager/` — IFF test-file load/save. No 202605 mock equivalent.
  *(Spec §9 — "CenterWindow / FileManager / TaskSerivce inheritance: deferred.")*
- `Code/Model/TaskSerivce/` generic task scheduler (the subset not owned by LIMS in Pkg 4).
  *(Spec §9 — same line.)*
- `Code/View/CenterWindow/` (CenterWindow shell, BigPage, SmallPage, SmallWidget,
  TestImportDialog). The 202605 design replaces this with `AppFrame` + content router; the
  legacy "dockable centre" concept is intentionally not ported.
  *(Spec §9 — same line.)*
- `Code/ServiceHeader/iCenterWindow.h` — paired interface for the above.
- `Code/Library/FrameworkCore/Code/Services/iAdministrationProvider.h` and
  `BundleRuntimeService` — hot-swap bundle administration. No Phase 2 requirement.
- `Code/Library/FrameworkCore/Code/multiLanguage/` — gettext-style catalogues.
  *(Spec §9 — "i18n scope: 202605 is Chinese-only ... add Resources/zh-CN.resx +
  Resources/en-US.resx in Pkg 1 M1.2 if multi-language is required.")*
- `Code/Library/FrameworkCore/Code/Tools/CrashDump.cpp/h` — Windows mini-dump writer.
  Serilog covers structured logs in Phase 2; full crash-dump capture is Phase 3.

Spec §9 explicitly notes that these are revisited once Phase 2 ships and the operational
need is concrete. Until then, contributors should leave the corresponding modules alone.
