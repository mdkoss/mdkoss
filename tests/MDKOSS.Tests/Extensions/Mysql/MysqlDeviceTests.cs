using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Extensions.Mysql;
using MySqlConnector;

namespace MDKOSS.Tests.Extensions.Mysql;

public sealed class MysqlDeviceParameterTests
{
    [Fact]
    public void ParseConfig_defaults_and_aliases()
    {
        var empty = MysqlDeviceParameters.ParseConfig(null);
        Assert.Equal("127.0.0.1", empty.Host);
        Assert.Equal(3306, empty.Port);
        Assert.Equal("root", empty.User);
        Assert.Equal("", empty.Password);
        Assert.Equal("utf8mb4", empty.Charset);
        Assert.False(empty.AutoConnect);
        Assert.Equal(MySqlSslMode.None, empty.SslMode);

        var parsed = MysqlDeviceParameters.ParseConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "db.local",
            ["port"] = "3311",
            ["database"] = "mdkossdb",
            ["user"] = "mdkossdb",
            ["password"] = "secret",
            ["connectTimeoutMs"] = "15000",
            ["commandTimeout"] = "20000",
            ["sslMode"] = "preferred",
            ["autoConnect"] = "true",
        });
        Assert.Equal("db.local", parsed.Host);
        Assert.Equal(3311, parsed.Port);
        Assert.Equal("mdkossdb", parsed.Database);
        Assert.Equal(15000, parsed.ConnectTimeoutMs);
        Assert.Equal(20000, parsed.CommandTimeoutMs);
        Assert.Equal(MySqlSslMode.Preferred, parsed.SslMode);
        Assert.True(parsed.AutoConnect);
        Assert.Contains("Server=db.local", parsed.BuildConnectionString());
    }

    [Fact]
    public void FromJson_maps_sql_parameter_types()
    {
        Assert.Null(MysqlDeviceApi.FromJson(JsonDocument.Parse("null").RootElement));
        Assert.True((bool)MysqlDeviceApi.FromJson(JsonDocument.Parse("true").RootElement)!);
        Assert.Equal(42L, MysqlDeviceApi.FromJson(JsonDocument.Parse("42").RootElement));
        Assert.Equal("hi", MysqlDeviceApi.FromJson(JsonDocument.Parse("\"hi\"").RootElement));
    }
}

public sealed class MysqlDeviceOfflineTests
{
    [Fact]
    public void Query_execute_without_connection_fail()
    {
        var vars = new MVarStore();
        var device = new MysqlDevice("mysql1", "demo", new MysqlDeviceParameters(), vars);
        Assert.False(device.IsConnected);
        Assert.Equal(MysqlErrorCode.NotConnected, device.Ping());
        Assert.Equal(MysqlErrorCode.NotConnected, device.Query("SELECT 1").error);
        Assert.Equal(MysqlErrorCode.NotConnected, device.Execute("SELECT 1").error);
        Assert.Equal(MysqlErrorCode.InvalidParameter, device.Query("").error);
        Assert.Equal(MysqlErrorCode.InvalidParameter, device.Execute("  ").error);

        var snap = device.GetSnapshot();
        Assert.Equal("mysqldev", snap.Type);
        Assert.False(snap.DriverConnected);
        device.Dispose();
    }

    [Fact]
    public async Task Runtime_action_status_and_unknown()
    {
        using var rt = CreateRuntime(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = "127.0.0.1",
            ["port"] = "3306",
            ["autoConnect"] = "false",
        });
        rt.Initialize();
        rt.Start();
        try
        {
            Assert.True(rt.TryGetDevice("mysql1", out var dev));
            Assert.Equal("mysqldev", rt.GetSnapshot().Devices["mysql1"].Type);
            Assert.NotNull(dev);

            var status = rt.ExecuteDeviceAction("mysql1", "status", null);
            Assert.True(status.Success);

            var ping = rt.ExecuteDeviceAction("mysql1", "ping", null);
            Assert.False(ping.Success);

            var missingSql = rt.ExecuteDeviceAction("mysql1", "query", null);
            Assert.False(missingSql.Success);
            Assert.Equal("missing_sql", missingSql.Error);

            var unknown = rt.ExecuteDeviceAction("mysql1", "nope", null);
            Assert.False(unknown.Success);
            Assert.Equal("unknown_action", unknown.Error);
        }
        finally
        {
            await rt.StopAsync();
        }
    }

    internal static MdkRuntime CreateRuntime(Dictionary<string, string> mysqlParameters, int? monitorPort = null)
    {
        var port = monitorPort ?? GetFreeLoopbackPort();
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-mysql-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var setting = new MdkSetting
        {
            ProjectName = "mysql-tests",
            CycleMs = 20,
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = $"http://127.0.0.1:{port}/",
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "sim1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "mysql1",
                    Name = "MySQL",
                    Type = "mysqldev",
                    Enabled = true,
                    Parameters = mysqlParameters,
                },
            ],
        };
        return new MdkRuntime(setting, Path.Combine(dir, "setting.json"));
    }

    internal static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public sealed class MysqlDeviceLiveTests
{
    [Fact]
    public void Connect_query_execute_roundtrip()
    {
        if (!MysqlLiveCredentials.TryLoad(out var raw))
        {
            return;
        }

        var vars = new MVarStore();
        var device = new MysqlDevice("mysql1", "live", MysqlLiveCredentials.ToDeviceParameters(raw), vars);
        var table = "mdkoss_mysqldev_test";
        try
        {
            var connect = device.Connect();
            Assert.Equal(MysqlErrorCode.Ok, connect);

            Assert.True(device.IsConnected);
            Assert.Equal(MysqlErrorCode.Ok, device.Ping());

            var (qErr, q) = device.Query("SELECT 1 AS ok, DATABASE() AS db");
            Assert.Equal(MysqlErrorCode.Ok, qErr);
            Assert.NotNull(q);
            Assert.Contains("ok", q!.Columns);
            Assert.Equal(1, q.RowCount);

            device.Execute($"DROP TABLE IF EXISTS `{table}`");
            var (cErr, _, _) = device.Execute(
                $"CREATE TABLE `{table}` (id INT PRIMARY KEY AUTO_INCREMENT, name VARCHAR(64) NOT NULL)");
            Assert.Equal(MysqlErrorCode.Ok, cErr);

            var (iErr, affected, lastId) = device.Execute(
                $"INSERT INTO `{table}` (name) VALUES (@n)",
                new Dictionary<string, object?> { ["n"] = "hello" });
            Assert.Equal(MysqlErrorCode.Ok, iErr);
            Assert.Equal(1, affected);
            Assert.True(lastId > 0);

            var (sErr, scalar) = device.Scalar($"SELECT name FROM `{table}` WHERE id=@id",
                new Dictionary<string, object?> { ["id"] = lastId });
            Assert.Equal(MysqlErrorCode.Ok, sErr);
            Assert.Equal("hello", scalar?.ToString());

            Assert.Equal(MysqlErrorCode.Ok, device.Disconnect());
            Assert.False(device.IsConnected);
        }
        finally
        {
            if (device.IsConnected || device.Connect() == MysqlErrorCode.Ok)
            {
                device.Execute($"DROP TABLE IF EXISTS `{table}`");
                device.Disconnect();
            }

            device.Dispose();
        }
    }
}

public sealed class MysqlApiModuleTests
{
    [Fact]
    public async Task Http_status_connect_query_against_live_db()
    {
        if (!MysqlLiveCredentials.TryLoad(out var raw))
        {
            return;
        }

        var port = MysqlDeviceOfflineTests.GetFreeLoopbackPort();
        using var rt = MysqlDeviceOfflineTests.CreateRuntime(raw, port);
        rt.Initialize();
        rt.Start();
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(30),
            };

            var catalog = await client.GetFromJsonAsync<JsonElement>("/api/config/catalog");
            var types = catalog.GetProperty("types").GetProperty("devices")
                .EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Contains("mysqldev", types);

            var missing = await client.GetAsync("/api/mysql/status");
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            var st = await client.GetFromJsonAsync<JsonElement>("/api/mysql/status?deviceId=mysql1");
            Assert.Equal("mysql1", st.GetProperty("deviceId").GetString());
            Assert.False(st.GetProperty("isConnected").GetBoolean());

            var connect = await client.PostAsJsonAsync("/api/mysql/connect", new
            {
                deviceId = "mysql1",
                config = new
                {
                    host = raw["host"],
                    port = int.Parse(raw["port"], CultureInfo.InvariantCulture),
                    database = raw["database"],
                    user = raw["user"],
                    password = raw["password"],
                    connectTimeout = 15000,
                    charset = raw["charset"],
                    sslMode = "None",
                },
            });
            var connectJson = await connect.Content.ReadAsStringAsync();
            Assert.True(connect.IsSuccessStatusCode, connectJson);

            using var qDoc = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/mysql/query", new
            {
                deviceId = "mysql1",
                sql = "SELECT 1 AS ok",
            })).Content.ReadAsStringAsync());
            var q = qDoc.RootElement;
            Assert.True(q.GetProperty("success").GetBoolean());
            Assert.Equal(1, q.GetProperty("rowCount").GetInt32());

            var disc = await client.PostAsJsonAsync("/api/mysql/disconnect", new { deviceId = "mysql1" });
            disc.EnsureSuccessStatusCode();
        }
        finally
        {
            await rt.StopAsync();
        }
    }
}
