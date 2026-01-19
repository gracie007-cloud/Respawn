using System;
using System.Data.Common;
using System.IO;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using IBM.Data.Db2;
using NPoco;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests;

public class InformixFixture(IMessageSink messageSink) : DbFixture<InformixBuilder, InformixContainer>(messageSink)
{
    public override DbProviderFactory DbProviderFactory => DB2Factory.Instance;

    protected override DatabaseType DbType => null;

    protected override InformixBuilder CreateBuilder()
    {
        return new InformixBuilder("ibmcom/informix-developer-database:14.10.FC5DE");
    }
}

public sealed class InformixBuilder : ContainerBuilder<InformixBuilder, InformixContainer, ContainerConfiguration>
{
    public InformixBuilder()
        : this("")
    {
        throw new NotSupportedException();
    }

    public InformixBuilder(string image)
        : this(new DockerImage(image))
    {
    }

    public InformixBuilder(IImage image)
        : this(new ContainerConfiguration())
    {
        DockerResourceConfiguration = Init().WithImage(image).DockerResourceConfiguration;
    }

    private InformixBuilder(ContainerConfiguration configuration) : base(configuration)
    {
        DockerResourceConfiguration = configuration;
    }

    protected override ContainerConfiguration DockerResourceConfiguration { get; }

    protected override InformixBuilder Init()
    {
        return base.Init()

            // = environment:
            .WithEnvironment("LICENSE", "accept")
            .WithEnvironment("ONCONFIG_FILE", "onconfig")
            .WithEnvironment("RUN_FILE_PRE_INIT", "my_post.sh")

            // = ports:
            .WithPortBinding(9088, assignRandomHostPort: true)
            .WithPortBinding(9089, assignRandomHostPort: true)
            .WithPortBinding(27017, assignRandomHostPort: true)
            .WithPortBinding(27018, assignRandomHostPort: true)
            .WithPortBinding(27883, assignRandomHostPort: true)

            // = volumes:
            .WithBindMount(
                source: Path.GetFullPath("./informix-server"),
                destination: "/opt/ibm/config",
                AccessMode.ReadWrite)

            // = privileged: true
            .WithPrivileged(true)

            // = user: root
            //.WithUser("root")

            // = tty: true
            //.WithTty(true)

            // optional: equivalent to "restart: always" but Testcontainers
            // does not automatically restart containers (it recreates instead)
            // .WithAutoRemove(false)

            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilExternalTcpPortIsAvailable(9088)
                .UntilExternalTcpPortIsAvailable(9089)
                .UntilInternalTcpPortIsAvailable(9088)
                .UntilInternalTcpPortIsAvailable(9089)
                // This is the last success message
                .UntilMessageIsLogged("starting mqtt listener on port 27883")
            );
    }

    public override InformixContainer Build()
    {
        Validate();
        return new InformixContainer(DockerResourceConfiguration);
    }

    protected override InformixBuilder Clone(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new ContainerConfiguration(resourceConfiguration));
    }

    protected override InformixBuilder Clone(IContainerConfiguration resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new ContainerConfiguration(resourceConfiguration));
    }

    protected override InformixBuilder Merge(ContainerConfiguration oldValue, ContainerConfiguration newValue)
    {
        return new InformixBuilder(new ContainerConfiguration(oldValue, newValue));
    }
}

public sealed class InformixContainer(IContainerConfiguration configuration) : DockerContainer(configuration), IDatabaseContainer
{
    public string GetConnectionString()
    {
        var host = Hostname;
        var port = GetMappedPublicPort(9089); // SQL port
        return $"Server={host}:{port};Database=sysadmin;UID=informix;Password=in4mix;Persist Security Info=True;Authentication=Server;";
    }
}