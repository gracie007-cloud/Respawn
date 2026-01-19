using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using NPoco;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public class OracleFixture(IMessageSink messageSink) : DbFixture<OracleBuilder, OracleContainer>(messageSink)
{
    private string _serviceName;

    public override DbProviderFactory DbProviderFactory => OracleClientFactory.Instance;

    protected override DatabaseType DbType => DatabaseType.OracleManaged;

    protected override OracleBuilder CreateBuilder() => new("gvenzl/oracle-free:23-slim-faststart");

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        _serviceName = (string)(await ExecuteSqlAsSysDbaAsync(["SELECT NAME from V$PDBS"]))[0];
    }

    public string User => OracleBuilder.DefaultUsername.ToUpperInvariant();

    protected override async Task ExecuteCreateDatabaseAsync(string dbName)
    {
        var builder = new OracleConnectionStringBuilder(ConnectionString);

        await ExecuteSqlAsSysDbaAsync([
            "ALTER SESSION SET CONTAINER=CDB$ROOT",
            $"CREATE PLUGGABLE DATABASE {dbName} ADMIN USER {builder.UserID} IDENTIFIED BY {builder.Password} FILE_NAME_CONVERT = ('pdbseed', '{dbName}')",
            $"ALTER PLUGGABLE DATABASE {dbName} OPEN",
            $"ALTER SESSION SET CONTAINER={dbName}",
            $"GRANT ALL PRIVILEGES TO {builder.UserID}",
        ]);
    }

    protected override string GetConnectionString(string dbName)
    {
        var builder = new OracleConnectionStringBuilder(ConnectionString);
        var connectionString = builder.ConnectionString.Replace(_serviceName, dbName);
        return connectionString;
    }

    private async Task<List<object>> ExecuteSqlAsSysDbaAsync(string[] sqlScripts)
    {
        var connectionString = new OracleConnectionStringBuilder(ConnectionString) { UserID = "SYS", DBAPrivilege = "SYSDBA" }.ConnectionString;
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        var results = new List<object>(sqlScripts.Length);

        foreach (var sqlScript in sqlScripts)
        {
            command.CommandText = sqlScript;
            var result = await command.ExecuteScalarAsync();
            results.Add(result);
        }

        return results;
    }
}