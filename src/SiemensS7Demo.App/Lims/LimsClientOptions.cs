namespace SiemensS7Demo.App.Lims;

public enum LimsClientMode { Http, File }

public sealed class LimsClientOptions
{
    public LimsClientMode Mode { get; set; } = LimsClientMode.Http;
    public string BaseUrl { get; set; } = "http://lims.corp.intra/";
    public string? WatchDirectory { get; set; }
    public string? ApiToken { get; set; }
}
