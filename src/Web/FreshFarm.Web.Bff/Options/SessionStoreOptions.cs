namespace FreshFarm.Web.Bff.Options;

public sealed class SessionStoreOptions
{
    public const string SectionName = "SessionStore";

    public string Provider { get; set; } = "Memory";

    public string? RedisConnectionString { get; set; }

    public string InstanceName { get; set; } = "FreshFarm:";

    public int ConnectTimeoutMilliseconds { get; set; } = 5000;

    public bool AbortOnConnectFail { get; set; } = false;

    public bool Ssl { get; set; }
}
