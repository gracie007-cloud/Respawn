using System;
using System.Data.Common;
using DotNet.Testcontainers.Images;
using IBM.Data.Db2;
using NPoco;
using Testcontainers.Db2;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public class DB2Fixture(IMessageSink messageSink) : DbFixture<Db2Builder, Db2Container>(messageSink)
{
    public override DbProviderFactory DbProviderFactory => DB2Factory.Instance;

    protected override DatabaseType DbType => null;

    protected override Db2Builder CreateBuilder() => new Db2Builder(new DockerImage("icr.io/db2_community/db2:12.1.0.0", new Platform("amd64"))).WithAcceptLicenseAgreement(true);
}