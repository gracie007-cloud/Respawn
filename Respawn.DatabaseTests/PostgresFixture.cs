using System.Data.Common;
using Npgsql;
using NPoco;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public class PostgresFixture(IMessageSink messageSink) : DbFixture<PostgreSqlBuilder, PostgreSqlContainer>(messageSink)
{
    public override DbProviderFactory DbProviderFactory => NpgsqlFactory.Instance;

    protected override DatabaseType DbType => DatabaseType.PostgreSQL;

    protected override PostgreSqlBuilder CreateBuilder() => new("postgres:16");

    public override string ConnectionString => new NpgsqlConnectionStringBuilder(base.ConnectionString) { IncludeErrorDetail = true }.ConnectionString;
}