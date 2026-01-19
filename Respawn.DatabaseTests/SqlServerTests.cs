using System.Threading.Tasks;
using Respawn.Graph;
using Xunit;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests
{
    using System.Linq;
    using NPoco;
    using Shouldly;

    public class SqlServerTests(ITestOutputHelper output, SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
    {
        public class Foo
        {
            public int Value { get; set; }
        }
        public class Bar
        {
            public int Value { get; set; }
        }
        public class Baz
        {
            public int Value { get; set; }
            public int FooValue { get; set; }
        }

        [PrimaryKey("Id", AutoIncrement = false)]
        public class Parent
        {
            public int Id { get; set; }
            public int? ChildId { get; set; }
        }

        [PrimaryKey("Id", AutoIncrement = false)]
        public class Child
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
        }

        [Fact]
        public async Task ShouldDeleteData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo (Value [int])");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection);
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldDeleteDataUsingCustomDeleteStatements()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo (Value [int])");
            await db.ExecuteAsync("create table Bar (Value [int])");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));
            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Bar { Value = i }));

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions()
            {
                FormatDeleteStatement = table =>
                {
                    if (table.Name == "Foo")
                    {
                        return $"DELETE FROM {table.GetFullName('"')} WHERE Value > 20;";
                    }
                    else
                    {
                        return $"DELETE FROM {table.GetFullName('"')} WHERE Value > 30;";
                    }
                }
            });

            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(21);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(31);
        }

        [Fact]
        public async Task ShouldHandleRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo (Value [int], constraint PK_Foo primary key nonclustered (value))");
            await db.ExecuteAsync("create table Baz (Value [int], FooValue [int], constraint FK_Foo foreign key (FooValue) references Foo (Value))");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));
            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Baz { Value = i, FooValue = i }));

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Baz").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection);
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Baz").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldHandleSelfRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table circle (id int primary key, parentid int NULL)");
            await db.ExecuteAsync("alter table circle add constraint FK_Parent foreign key (parentid) references circle (id)");

            await db.ExecuteAsync("INSERT INTO \"circle\" (id) VALUES (@0)", 1);
            for (int i = 1; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"circle\" (id, parentid) VALUES (@0, @1)", i + 1, i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM circle").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection);
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM circle").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldHandleComplexCycles()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table a (id int primary key, b_id int NULL)");
            await db.ExecuteAsync("create table b (id int primary key, a_id int NULL, c_id int NULL, d_id int NULL)");
            await db.ExecuteAsync("create table c (id int primary key, d_id int NULL)");
            await db.ExecuteAsync("create table d (id int primary key)");
            await db.ExecuteAsync("create table e (id int primary key, a_id int NULL)");
            await db.ExecuteAsync("create table f (id int primary key, b_id int NULL)");
            await db.ExecuteAsync("alter table a add constraint FK_a_b foreign key (b_id) references b (id)");
            await db.ExecuteAsync("alter table b add constraint FK_b_a foreign key (a_id) references a (id)");
            await db.ExecuteAsync("alter table b add constraint FK_b_c foreign key (c_id) references c (id)");
            await db.ExecuteAsync("alter table b add constraint FK_b_d foreign key (d_id) references d (id)");
            await db.ExecuteAsync("alter table c add constraint FK_c_d foreign key (d_id) references d (id)");
            await db.ExecuteAsync("alter table e add constraint FK_e_a foreign key (a_id) references a (id)");
            await db.ExecuteAsync("alter table f add constraint FK_f_b foreign key (b_id) references b (id)");


            await db.ExecuteAsync("insert into d (id) values (1)");
            await db.ExecuteAsync("insert into c (id, d_id) values (1, 1)");
            await db.ExecuteAsync("insert into a (id) values (1)");
            await db.ExecuteAsync("insert into b (id, c_id, d_id) values (1, 1, 1)");
            await db.ExecuteAsync("insert into e (id, a_id) values (1, 1)");
            await db.ExecuteAsync("insert into f (id, b_id) values (1, 1)");
            await db.ExecuteAsync("update a set b_id = 1");
            await db.ExecuteAsync("update b set a_id = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM a").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM b").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM c").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM d").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM e").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM f").ShouldBe(1);

            var checkpoint = await Respawner.CreateAsync(db.Connection);
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM a").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM b").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM c").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM d").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM e").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM f").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldHandleCircularRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Parent (Id [int] NOT NULL, ChildId [int] NULL, constraint PK_Parent primary key clustered (Id))");
            await db.ExecuteAsync("create table Child (Id [int] NOT NULL, ParentId [int] NULL, constraint PK_Child primary key clustered (Id))");
            await db.ExecuteAsync("alter table Parent add constraint FK_Child foreign key (ChildId) references Child (Id)");
            await db.ExecuteAsync("alter table Child add constraint FK_Parent foreign key (ParentId) references Parent (Id)");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Parent { Id = i, ChildId = null }));
            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Child { Id = i, ParentId = null }));

            await db.ExecuteAsync("update Parent set ChildId = 0");
            await db.ExecuteAsync("update Child set ParentId = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Parent").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Child").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection);
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Parent").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Child").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIgnoreTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo (Value [int])");
            await db.ExecuteAsync("create table Bar (Value [int])");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));
            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Bar { Value = i }));

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToIgnore = new Table[] { "Foo" }
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            output.WriteLine(checkpoint.DeleteSql);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIgnoreTablesWithSchema()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("drop schema if exists B");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create schema B");
            await db.ExecuteAsync("create table A.Foo (Value [int])");
            await db.ExecuteAsync("create table A.FooWithBrackets (Value [int])");
            await db.ExecuteAsync("create table B.Bar (Value [int])");
            await db.ExecuteAsync("create table B.Foo (Value [int])");

            for (var i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT A.Foo VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT A.FooWithBrackets VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT B.Bar VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT B.Foo VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToIgnore = new[]
                {
                    new Table("A", "Foo"),
                    new Table("A", "FooWithBrackets")
                }
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM A.Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM A.FooWithBrackets").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM B.Bar").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM B.Foo").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIncludeTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo (Value [int])");
            await db.ExecuteAsync("create table Bar (Value [int])");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));
            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Bar { Value = i }));

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToInclude = new Table[] { "Foo" }
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(100);
        }

        [Fact]
        public async Task ShouldExcludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("drop schema if exists B");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create schema B");
            await db.ExecuteAsync("create table A.Foo (Value [int])");
            await db.ExecuteAsync("create table B.Bar (Value [int])");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT A.Foo VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT B.Bar VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToExclude = new[] { "A" }
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM A.Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM B.Bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIncludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("drop schema if exists B");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create schema B");
            await db.ExecuteAsync("create table A.Foo (Value [int])");
            await db.ExecuteAsync("create table B.Bar (Value [int])");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT A.Foo VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT B.Bar VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { "B" }
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM A.Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM B.Bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldReseedId()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo ([id] [int] IDENTITY(1,1), Value int)");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));

            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.InsertAsync(new Foo {Value = 0});
            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(1);
        }

        [Fact]
        public async Task ShouldReseedId_TableWithSchema()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("IF EXISTS (SELECT * FROM sys.schemas WHERE name = 'A') DROP SCHEMA A");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create table A.Foo ([id] [int] IDENTITY(1,1), Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT A.Foo VALUES (" + i + ")");
            }

            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.ExecuteAsync("INSERT A.Foo VALUES (0)");

            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(1);
        }

        [Fact]
        public async Task ShouldReseedId_TableHasNeverHadAnyData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create table A.Foo ([id] [int] IDENTITY(1,1), Value int)");
            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.ExecuteAsync("INSERT A.Foo VALUES (0)");
            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(1);
        }

        [Fact]
        public async Task ShouldReseedId_TableWithSchemaHasNeverHadAnyData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo ([id] [int] IDENTITY(1,1), Value int)");
            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.InsertAsync(new Foo { Value = 0 });
            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(1);
        }

        [Fact]
        public async Task ShouldNotReseedId()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo ([id] [int] IDENTITY(1,1), Value int)");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));

            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = false
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.InsertAsync(new Foo { Value = 0 });
            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(101);
        }

        [Fact]
        public async Task ShouldNotReseedId_TableWithSchema()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create table A.Foo ([id] [int] IDENTITY(1,1), Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT A.Foo VALUES (" + i + ")");
            }

            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = false
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.ExecuteAsync("INSERT A.Foo VALUES (0)");
            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(101);
        }

        [Fact]
        public async Task ShouldReseedIdAccordingToIdentityInitialSeedValue()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo ([id] [int] IDENTITY(1001,1), Value int)");

            await db.InsertBulkAsync(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));

            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(1100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });

            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.InsertAsync(new Foo { Value = 0 });
            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(1001);
        }

        [Fact]
        public async Task ShouldReseedIdAccordingToIdentityInitialSeedValue_TableWithSchema()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create table A.Foo ([id] [int] IDENTITY(1001,1), Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT A.Foo VALUES (" + i + ")");
            }

            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(1100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });

            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.ExecuteAsync("INSERT A.Foo VALUES (0)");
            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(1001);
        }

        [Fact]
        public async Task ShouldReseedIdAccordingToIdentityInitialSeedValue_TableHasNeverHadAnyData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table Foo ([id] [int] IDENTITY(1001,1), Value int)");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });

            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.InsertAsync(new Foo { Value = 0 });
            db.ExecuteScalar<int>("SELECT MAX(id) FROM Foo").ShouldBe(1001);
        }

        [Fact]
        public async Task ShouldReseedIdAccordingToIdentityInitialSeedValue_TableWithSchemaHasNeverHadAnyData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop schema if exists A");
            await db.ExecuteAsync("create schema A");
            await db.ExecuteAsync("create table A.Foo ([id] [int] IDENTITY(1001,1), Value int)");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });

            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.ReseedSql);
                throw;
            }

            await db.ExecuteAsync("INSERT A.Foo VALUES (0)");
            db.ExecuteScalar<int>("SELECT MAX(id) FROM A.Foo").ShouldBe(1001);
        }

        [Fact]
        public async Task ShouldDeleteTemporalTablesData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop table if exists FooHistory");
            await db.ExecuteAsync("IF OBJECT_ID(N'Foo', N'U') IS NOT NULL alter table Foo set (SYSTEM_VERSIONING = OFF)");
            await db.ExecuteAsync("drop table if exists Foo");

            await db.ExecuteAsync("create table Foo (Value [int] not null primary key clustered, " +
                                         "ValidFrom datetime2 generated always as row start, " +
                                         "ValidTo datetime2 generated always as row end," +
                                         " period for system_time(ValidFrom, ValidTo)" +
                                         ") with (system_versioning = on (history_table = dbo.FooHistory))");

            await db.ExecuteAsync("INSERT Foo (Value) VALUES (1)");
            await db.ExecuteAsync("UPDATE Foo SET Value = 2 Where Value = 1");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                CheckTemporalTables = true
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM FooHistory").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldResetTemporalTableDefaultName()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("drop table if exists FooHistory");
            await db.ExecuteAsync("IF OBJECT_ID(N'Foo', N'U') IS NOT NULL alter table Foo set (SYSTEM_VERSIONING = OFF)");
            await db.ExecuteAsync("drop table if exists Foo");

            await db.ExecuteAsync("create table Foo (Value [int] not null primary key clustered, " +
                                         "ValidFrom datetime2 generated always as row start, " +
                                         "ValidTo datetime2 generated always as row end," +
                                         " period for system_time(ValidFrom, ValidTo)" +
                                         ") with (system_versioning = on (history_table = dbo.FooHistory))");

            await db.ExecuteAsync("INSERT Foo (Value) VALUES (1)");
            await db.ExecuteAsync("UPDATE Foo SET Value = 2 Where Value = 1");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                CheckTemporalTables = true
            });
            await checkpoint.ResetAsync(db.Connection);

            var sql = @"
SELECT t1.name 
FROM sys.tables t1 
WHERE t1.object_id = (SELECT history_table_id FROM sys.tables t2 WHERE t2.name = 'Foo')
";
            db.ExecuteScalar<string>(sql).ShouldBe("FooHistory");
        }

        [Fact]
        public async Task ShouldResetTemporalTableAnonymousName()
        {
            using var db = await fixture.CreateDatabaseAsync();

            // _database.Execute("drop table if exists FooHistory");
            await db.ExecuteAsync("IF OBJECT_ID(N'Foo', N'U') IS NOT NULL alter table Foo set (SYSTEM_VERSIONING = OFF)");
            await db.ExecuteAsync("drop table if exists Foo");

            await db.ExecuteAsync("create table Foo (Value [int] not null primary key clustered, " +
                                         "ValidFrom datetime2 generated always as row start, " +
                                         "ValidTo datetime2 generated always as row end," +
                                         " period for system_time(ValidFrom, ValidTo)" +
                                         ") with (system_versioning = on)");

            await db.ExecuteAsync("INSERT Foo (Value) VALUES (1)");
            await db.ExecuteAsync("UPDATE Foo SET Value = 2 Where Value = 1");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                CheckTemporalTables = true
            });
            await checkpoint.ResetAsync(db.Connection);

            var sql = @"
SELECT t1.name 
FROM sys.tables t1 
WHERE t1.object_id = (SELECT history_table_id FROM sys.tables t2 WHERE t2.name = 'Foo')
";
            db.ExecuteScalar<string>(sql).ShouldStartWith("MSSQL_TemporalHistoryFor_");
        }

        [Fact]
        public async Task ShouldDeleteTemporalTablesDataFromNotDefaultSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("CREATE SCHEMA [TableSchema] AUTHORIZATION [dbo];");
            await db.ExecuteAsync("CREATE SCHEMA [HistorySchema] AUTHORIZATION [dbo];");

            await db.ExecuteAsync("create table TableSchema.Foo (Value [int] not null primary key clustered, " +
                                         "ValidFrom datetime2 generated always as row start, " +
                                         "ValidTo datetime2 generated always as row end," +
                                         " period for system_time(ValidFrom, ValidTo)" +
                                         ") with (system_versioning = on (history_table = HistorySchema.FooHistory))");

            await db.ExecuteAsync("INSERT TableSchema.Foo (Value) VALUES (1)");
            await db.ExecuteAsync("UPDATE TableSchema.Foo SET Value = 2 Where Value = 1");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                CheckTemporalTables = true
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM HistorySchema.FooHistory").ShouldBe(0);
        }
    }
}
