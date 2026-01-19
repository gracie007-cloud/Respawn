using System.Data.Common;
using Microsoft.Data.SqlClient;
using NPoco;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public class SqlServerFixture(IMessageSink messageSink) : DbFixture<MsSqlBuilder, MsSqlContainer>(messageSink)
{
    public override DbProviderFactory DbProviderFactory => SqlClientFactory.Instance;

    protected override DatabaseType DbType => DatabaseType.SqlServer2012;

    protected override MsSqlBuilder CreateBuilder() => new("mcr.microsoft.com/mssql/server:2022-CU23-ubuntu-22.04");
}