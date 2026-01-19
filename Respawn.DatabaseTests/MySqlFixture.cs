using System.Data.Common;
using MySql.Data.MySqlClient;
using NPoco;
using Testcontainers.MySql;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public class MySqlFixture(IMessageSink messageSink) : DbFixture<MySqlBuilder, MySqlContainer>(messageSink)
{
    public override DbProviderFactory DbProviderFactory => MySqlClientFactory.Instance;

    protected override DatabaseType DbType => DatabaseType.MySQL;

    protected override MySqlBuilder CreateBuilder() => new MySqlBuilder("mysql:8.0").WithUsername("root");
}