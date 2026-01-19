using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NPoco;
using Testcontainers.Xunit;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public abstract class DbFixture<TBuilderEntity, TContainerEntity>(IMessageSink messageSink) : DbContainerFixture<TBuilderEntity, TContainerEntity>(messageSink)
    where TBuilderEntity : IContainerBuilder<TBuilderEntity, TContainerEntity, IContainerConfiguration>, new()
    where TContainerEntity : IDatabaseContainer
{
    public async Task<IDatabase> CreateDatabaseAsync([CallerMemberName] string dbName = "")
    {
        await ExecuteCreateDatabaseAsync(dbName);

        var connectionString = GetConnectionString(dbName);
        var database = new Database(connectionString, DbType, DbProviderFactory);
        return database.OpenSharedConnection();
    }

    protected abstract DatabaseType DbType { get; }

    protected abstract TBuilderEntity CreateBuilder();

    protected override TBuilderEntity Configure() => CreateBuilder().WithWaitStrategy(Wait.ForUnixContainer().UntilDatabaseIsAvailable(DbProviderFactory));

    protected virtual async Task ExecuteCreateDatabaseAsync(string dbName)
    {
        var builder = DbProviderFactory.CreateCommandBuilder();
        var sql = $"CREATE DATABASE {builder?.QuotePrefix}{dbName}{builder?.QuoteSuffix}";

        await using var command = CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    protected virtual string GetConnectionString(string dbName)
    {
        var builder = DbProviderFactory.CreateConnectionStringBuilder() ?? throw new InvalidOperationException($"CreateConnectionStringBuilder returned null for {DbProviderFactory}");
        builder.ConnectionString = ConnectionString;
        builder["Database"] = dbName;
        return builder.ToString();
    }
}