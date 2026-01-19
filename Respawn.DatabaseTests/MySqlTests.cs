using System.Threading.Tasks;
using Respawn.Graph;
using Xunit;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests
{
    using System.Linq;
    using Shouldly;

    public class MySqlTests(ITestOutputHelper output, MySqlFixture fixture) : IClassFixture<MySqlFixture>
    {
        public class Foo
        {
            public int Value { get; set; }
        }
        public class Bar
        {
            public int Value { get; set; }
        }

        [SkipOnCI]
        public async Task ShouldDeleteData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("drop table if exists Foo");
            db.Execute("CREATE TABLE `Foo` (`Value` int(3))");

            db.InsertBulk(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { nameof(ShouldDeleteData) }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldDeleteDataWithRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            // Tests a more complex scenario with 2 FK relationships
            
            // - Foo has both a PK and an FK relationship
            // - Bob.BobValue PK --> Foo.BobValue
            // - Foo.FooValue PK --> Bar.BarValue

            // It should delete the tables in the order Bar, Foo, Bob

            db.Execute("drop table if exists Bar");
            db.Execute("drop table if exists Foo");
            db.Execute("drop table if exists Bob");

            db.Execute(@"
CREATE TABLE `Bob` (
  `BobValue` int(3) NOT NULL, 
  PRIMARY KEY (`BobValue`)
)");

            db.Execute(@"
CREATE TABLE `Foo` (
  `FooValue` int(3) NOT NULL,
  `BobValue` int(3) NOT NULL,
  PRIMARY KEY (`FooValue`),
  KEY `IX_BobValue` (`BobValue`),
  CONSTRAINT `FK_FOO_BOB` FOREIGN KEY (`BobValue`) REFERENCES `Bob` (`BobValue`) ON DELETE NO ACTION ON UPDATE NO ACTION
)");

            db.Execute(@"
CREATE TABLE `Bar` (
  `BarValue` int(3) NOT NULL,
  PRIMARY KEY (`BarValue`),
  CONSTRAINT `FK_BAR_FOO` FOREIGN KEY (`BarValue`) REFERENCES `Foo` (`FooValue`) ON DELETE NO ACTION ON UPDATE NO ACTION
)");

            for (var i = 0; i < 100; i++)
            {
                db.Execute($"INSERT `Bob` VALUES ({i})");
                db.Execute($"INSERT `Foo` VALUES ({i},{i})");
                db.Execute($"INSERT `Bar` VALUES ({i})");
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bob").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { nameof(ShouldDeleteDataWithRelationships) }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bob").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldHandleSelfRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table foo (id int primary key, parentid int NULL)");
            db.Execute("alter table foo add constraint FK_Parent foreign key (parentid) references foo (id)");

            db.Execute("INSERT INTO `foo` (id) VALUES (@0)", 1);
            for (int i = 1; i < 100; i++)
            {
                db.Execute("INSERT INTO `foo` VALUES (@0, @1)", i + 1, i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM foo").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { nameof(ShouldHandleSelfRelationships) }
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
        }


        [SkipOnCI]
        public async Task ShouldHandleCircularRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table parent (id int primary key, childid int NULL)");
            db.Execute("create table child (id int primary key, parentid int NULL)");
            db.Execute("alter table parent add constraint FK_Child foreign key (ChildId) references child (Id)");
            db.Execute("alter table child add constraint FK_Parent foreign key (ParentId) references parent (Id)");

            for (int i = 0; i < 100; i++)
            {
                db.Execute("INSERT INTO parent VALUES (@0, null)", i);
                db.Execute("INSERT INTO child VALUES (@0, null)", i);
            }

            db.Execute("update parent set childid = 0");
            db.Execute("update child set parentid = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM parent").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM child").ShouldBe(100);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { nameof(ShouldHandleCircularRelationships) }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM parent").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM child").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldHandleComplexCycles()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table a (id int primary key, b_id int NULL)");
            db.Execute("create table b (id int primary key, a_id int NULL, c_id int NULL, d_id int NULL)");
            db.Execute("create table c (id int primary key, d_id int NULL)");
            db.Execute("create table d (id int primary key)");
            db.Execute("create table e (id int primary key, a_id int NULL)");
            db.Execute("create table f (id int primary key, b_id int NULL)");
            db.Execute("alter table a add constraint FK_a_b foreign key (b_id) references b (id)");
            db.Execute("alter table b add constraint FK_b_a foreign key (a_id) references a (id)");
            db.Execute("alter table b add constraint FK_b_c foreign key (c_id) references c (id)");
            db.Execute("alter table b add constraint FK_b_d foreign key (d_id) references d (id)");
            db.Execute("alter table c add constraint FK_c_d foreign key (d_id) references d (id)");
            db.Execute("alter table e add constraint FK_e_a foreign key (a_id) references a (id)");
            db.Execute("alter table f add constraint FK_f_b foreign key (b_id) references b (id)");


            db.Execute("insert into d (id) values (1)");
            db.Execute("insert into c (id, d_id) values (1, 1)");
            db.Execute("insert into a (id) values (1)");
            db.Execute("insert into b (id, c_id, d_id) values (1, 1, 1)");
            db.Execute("insert into e (id, a_id) values (1, 1)");
            db.Execute("insert into f (id, b_id) values (1, 1)");
            db.Execute("update a set b_id = 1");
            db.Execute("update b set a_id = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM a").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM b").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM c").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM d").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM e").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM f").ShouldBe(1);

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { nameof(ShouldHandleComplexCycles) }
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

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM a").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM b").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM c").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM d").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM e").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM f").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIgnoreTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("drop table if exists Foo");
            db.Execute("drop table if exists Bar");
            db.Execute("create table `Foo` (`Value` int(3))");
            db.Execute("create table `Bar` (`Value` int(3))");

            db.InsertBulk(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));
            db.InsertBulk(Enumerable.Range(0, 100).Select(i => new Bar { Value = i }));

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToIgnore = new Table[] { "Foo" },
                SchemasToInclude = new[] { nameof(ShouldIgnoreTables) }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIncludeTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("drop table if exists Foo");
            db.Execute("drop table if exists Bar");
            db.Execute("create table `Foo` (`Value` int(3))");
            db.Execute("create table `Bar` (`Value` int(3))");

            db.InsertBulk(Enumerable.Range(0, 100).Select(i => new Foo { Value = i }));
            db.InsertBulk(Enumerable.Range(0, 100).Select(i => new Bar { Value = i }));

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToInclude = new Table[] { "Foo" },
                SchemasToInclude = new[] { nameof(ShouldIncludeTables) }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Foo").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM Bar").ShouldBe(100);
        }

        [SkipOnCI]
        public async Task ShouldExcludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("drop table if exists `A`.`Foo`");
            db.Execute("drop table if exists `B`.`Bar`");
            db.Execute("drop schema if exists `A`");
            db.Execute("drop schema if exists `B`");
            db.Execute("create schema `A`");
            db.Execute("create schema `B`");
            db.Execute("create table `A`.`Foo` (`Value` int(3))");
            db.Execute("create table `B`.`Bar` (`Value` int(3))");

            for (var i = 0; i < 100; i++)
            {
                db.Execute("INSERT `A`.`Foo` VALUES (" + i + ")");
                db.Execute("INSERT `B`.`Bar` VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToExclude = new[] { "A", nameof(ShouldExcludeSchemas) }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM A.Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM B.Bar").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIncludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("drop table if exists `A`.`Foo`");
            db.Execute("drop table if exists `B`.`Bar`");
            db.Execute("drop schema if exists `A`");
            db.Execute("drop schema if exists `B`");
            db.Execute("create schema `A`");
            db.Execute("create schema `B`");
            db.Execute("create table `A`.`Foo` (`Value` int(3))");
            db.Execute("create table `B`.`Bar` (`Value` int(3))");

            for (var i = 0; i < 100; i++)
            {
                db.Execute("INSERT A.Foo VALUES (" + i + ")");
                db.Execute("INSERT B.Bar VALUES (" + i + ")");
            }

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { "B" }
            });
            await checkpoint.ResetAsync(db.Connection);

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM A.Foo").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM B.Bar").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldResetSequencesAndIdentities()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("CREATE TABLE a (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY)");
            db.Execute("INSERT INTO a(id) VALUES (0)");
            db.Execute("INSERT INTO a(id) VALUES (0)");
            db.Execute("INSERT INTO a(id) VALUES (0)");

            var checkpoint = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                WithReseed = true
            });

            await checkpoint.ResetAsync(db.Connection);
            db.ExecuteScalar<int>($"SELECT AUTO_INCREMENT FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{nameof(ShouldResetSequencesAndIdentities)}' AND TABLE_NAME = 'a';").ShouldBe(1);
        }

    }
}
