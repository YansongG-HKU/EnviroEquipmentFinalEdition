using SiemensS7Demo.Drivers;
using SiemensS7Demo.Models;
using SiemensS7Demo.Services;

var options = new PlcConnectionOptions
{
    Name = "DemoPLC",
    IpAddress = "192.168.0.10",
    CpuType = "S7-1500",
    Rack = 0,
    Slot = 1
};

var tags = new List<TagDefinition>
{
    new() { Name = "TemperaturePV", DisplayName = "当前温度", Group = "Acquire", Address = "DB100.DBD10", DataType = TagDataType.Real, Unit = "℃", Access = TagAccess.Read },
    new() { Name = "HumidityPV", DisplayName = "当前湿度", Group = "Acquire", Address = "DB100.DBD14", DataType = TagDataType.Real, Unit = "%RH", Access = TagAccess.Read },
    new() { Name = "RunCommand", DisplayName = "运行命令", Group = "Control", Address = "DB100.DBX100.0", DataType = TagDataType.Bool, Unit = "", Access = TagAccess.Write },
};

var adapter = new InMemoryS7Adapter();
var plcClient = new SiemensS7Client(options, adapter);
var pollingService = new PlcPollingService(plcClient);
var writeService = new PlcWriteService(plcClient);

using var cts = new CancellationTokenSource();

await plcClient.ConnectAsync(cts.Token);
Console.WriteLine($"Connected: {options}");

var pollTask = pollingService.RunAsync(
    readTags: tags.Where(t => t.Access != TagAccess.Write).ToList(),
    interval: TimeSpan.FromSeconds(1),
    onSnapshot: snapshot =>
    {
        foreach (var item in snapshot)
        {
            Console.WriteLine($"[{item.Value.TimestampUtc:O}] {item.Key} = {item.Value.Value}");
        }
    },
    cancellationToken: cts.Token);

Console.WriteLine("Press ENTER to write RunCommand=true, then any key to stop...");
Console.ReadLine();

var runCommandTag = tags.Single(t => t.Name == "RunCommand");
await writeService.WriteAsync(runCommandTag, true, cts.Token);
Console.WriteLine("Run command written.");

Console.ReadKey();
cts.Cancel();

try
{
    await pollTask;
}
catch (OperationCanceledException)
{
    // Expected when exiting.
}

await plcClient.DisconnectAsync(CancellationToken.None);
Console.WriteLine("Disconnected.");
