using System.Threading.Tasks;
using Respawn.Graph;
using Xunit;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests
{
    using Shouldly;

    public class PostgresTests(ITestOutputHelper output, PostgresFixture fixture) : IClassFixture<PostgresFixture>
    {
        [Fact]
        public async Task ShouldDeleteData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"foo\"").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection);
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"foo\"").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIgnoreTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table foo (Value int)");
            await db.ExecuteAsync("create table bar (Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToIgnore = new Table[] { "foo" }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIgnoreTablesIfSchemaSpecified()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create schema eggs");
            await db.ExecuteAsync("create table eggs.foo (Value int)");
            await db.ExecuteAsync("create table eggs.bar (Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"eggs\".\"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"eggs\".\"bar\" VALUES (@0)", i);
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToIgnore = new Table[] { new Table("eggs", "foo") }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM eggs.foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM eggs.bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIncludeTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table foo (Value int)");
            await db.ExecuteAsync("create table bar (Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToInclude = new Table[] { "foo" }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM bar").ShouldBe(100);
        }

        [Fact]
        public async Task ShouldIncludeTablesIfSchemaSpecified()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create schema eggs");
            await db.ExecuteAsync("create table eggs.foo (Value int)");
            await db.ExecuteAsync("create table eggs.bar (Value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"eggs\".\"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"eggs\".\"bar\" VALUES (@0)", i);
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToInclude = new Table[] { new Table("eggs", "foo") }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM eggs.foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM eggs.bar").ShouldBe(100);
        }

        [Fact]
        public async Task ShouldHandleRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table foo (value int, primary key (value))");
            await db.ExecuteAsync("create table baz (value int, foovalue int, constraint FK_Foo foreign key (foovalue) references foo (value))");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"baz\" VALUES (@0, @0)", i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM baz").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new [] { "public" }
            });
            try
            {
                await checkpoint.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(checkpoint.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM baz").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldHandleCircularRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table parent (id int primary key, childid int NULL)");
            await db.ExecuteAsync("create table child (id int primary key, parentid int NULL)");
            await db.ExecuteAsync("alter table parent add constraint FK_Child foreign key (ChildId) references Child (Id)");
            await db.ExecuteAsync("alter table child add constraint FK_Parent foreign key (ParentId) references Parent (Id)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"parent\" VALUES (@0, null)", i);
                await db.ExecuteAsync("INSERT INTO \"child\" VALUES (@0, null)", i);
            }

            await db.ExecuteAsync("update parent set childid = 0");
            await db.ExecuteAsync("update child set parentid = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM parent").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM child").ShouldBe(100);

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

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM parent").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM child").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldHandleSelfRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table foo (id int primary key, parentid int NULL)");
            await db.ExecuteAsync("alter table foo add constraint FK_Parent foreign key (parentid) references foo (id)");

            await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", 1);
            for (int i = 1; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0, @1)", i+1, i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(100);

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

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(0);
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
        public async Task ShouldExcludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create schema a");
            await db.ExecuteAsync("create schema b");
            await db.ExecuteAsync("create table a.foo (value int)");
            await db.ExecuteAsync("create table b.bar (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO a.foo VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT INTO b.bar VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToExclude = new [] { "a" }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM a.foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM b.bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldIncludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create schema a");
            await db.ExecuteAsync("create schema b");
            await db.ExecuteAsync("create table a.foo (value int)");
            await db.ExecuteAsync("create table b.bar (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO a.foo VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT INTO b.bar VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new [] { "b" }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM a.foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM b.bar").ShouldBe(0);
        }

        [Fact]
        public async Task ShouldResetSequencesAndIdentities()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("CREATE TABLE a (id INT GENERATED ALWAYS AS IDENTITY, value SERIAL)");
            await db.ExecuteAsync("INSERT INTO a DEFAULT VALUES");
            await db.ExecuteAsync("INSERT INTO a DEFAULT VALUES");
            await db.ExecuteAsync("INSERT INTO a DEFAULT VALUES");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });

            await checkpoint.ResetAsync(db.Connection);
            db.ExecuteScalar<int>("SELECT nextval('a_id_seq')").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT nextval('a_value_seq')").ShouldBe(1);
        }
    }
}
