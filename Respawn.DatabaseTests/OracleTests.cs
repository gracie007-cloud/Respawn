using System;
using System.Threading.Tasks;
using NPoco;
using Respawn.Graph;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Respawn.DatabaseTests
{
    public class OracleTests(ITestOutputHelper output, OracleFixture fixture) : IClassFixture<OracleFixture>
    {
        [SkipOnCI]
        public async Task ShouldDeleteData()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
            }

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(100);

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User }
            });
            try
            {
                await respawner.ResetAsync(db.Connection);
            }
            finally
            {
                output.WriteLine(respawner.DeleteSql);
            }

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldDeleteMultipleTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");
            await db.ExecuteAsync("create table \"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User },
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(0);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"bar\"")).ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldHandleRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table \"foo\" (value int, primary key (value))");
            db.Execute("create table \"baz\" (value int, foovalue int, constraint FK_Foo foreign key (foovalue) references \"foo\" (value))");

            for (int i = 0; i < 100; i++)
            {
                db.Execute("INSERT INTO \"foo\" VALUES (@0)", i);
                db.Execute("INSERT INTO \"baz\" VALUES (@0, @0)", i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"foo\"").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"baz\"").ShouldBe(100);

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User },
            });
            try
            {
                await respawner.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(respawner.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"foo\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"baz\"").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldHandleRelationshipsWithTableNames()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table \"foo\" (value int, primary key (value))");
            db.Execute("create table \"baz\" (value int, foovalue int, constraint FK_Foo foreign key (foovalue) references \"foo\" (value))");

            for (int i = 0; i < 100; i++)
            {
                db.Execute("INSERT INTO \"foo\" VALUES (@0)", i);
                db.Execute("INSERT INTO \"baz\" VALUES (@0, @0)", i);
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"foo\"").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"baz\"").ShouldBe(100);

            var createdUser = fixture.User;
            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { createdUser },
                TablesToInclude = new[] { new Table(createdUser, "foo"), new Table(createdUser, "baz") },
                TablesToIgnore = new[] { new Table(createdUser, "bar") }
            });
            try
            {
                await respawner.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(respawner.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"foo\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"baz\"").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldHandleRelationshipsWithNamedPrimaryKeyConstraint()
        {
            using var db = await fixture.CreateDatabaseAsync();

            var userA = Guid.NewGuid().ToString().Substring(0, 8);
            var userB = Guid.NewGuid().ToString().Substring(0, 8);
            await CreateUser(db, userA);
            await CreateUser(db, userB);
            await db.ExecuteAsync($"create table \"{userA}\".\"foo\" (value int, constraint PK_Foo primary key (value))");
            await db.ExecuteAsync($"create table \"{userA}\".\"baz\" (value int, foovalue int, constraint FK_Foo foreign key (foovalue) references \"{userA}\".\"foo\" (value))");
            await db.ExecuteAsync($"create table \"{userB}\".\"foo\" (value int, constraint PK_Foo primary key (value))");
            await db.ExecuteAsync($"create table \"{userB}\".\"baz\" (value int, foovalue int, constraint FK_Foo foreign key (foovalue) references \"{userB}\".\"foo\" (value))");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync($"INSERT INTO \"{userA}\".\"foo\" VALUES (@0)", i);
                await db.ExecuteAsync($"INSERT INTO \"{userA}\".\"baz\" VALUES (@0, @0)", i);
                await db.ExecuteAsync($"INSERT INTO \"{userB}\".\"foo\" VALUES (@0)", i);
                await db.ExecuteAsync($"INSERT INTO \"{userB}\".\"baz\" VALUES (@0, @0)", i);
            }

            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userA}\".\"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userA}\".\"baz\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userB}\".\"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userB}\".\"baz\"")).ShouldBe(100);

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { userA },
            });
            try
            {
                await respawner.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(respawner.DeleteSql ?? string.Empty);
                throw;
            }

            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userA}\".\"foo\"")).ShouldBe(0);
            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userA}\".\"baz\"")).ShouldBe(0);
            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userB}\".\"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{userB}\".\"baz\"")).ShouldBe(100);
        }

        [SkipOnCI]
        public async Task ShouldHandleComplexCycles()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table \"a\" (\"id\" int primary key, \"b_id\" int NULL)");
            db.Execute("create table \"b\" (\"id\" int primary key, \"a_id\" int NULL, \"c_id\" int NULL, \"d_id\" int NULL)");
            db.Execute("create table \"c\" (\"id\" int primary key, \"d_id\" int NULL)");
            db.Execute("create table \"d\" (\"id\" int primary key)");
            db.Execute("create table \"e\" (\"id\" int primary key, \"a_id\" int NULL)");
            db.Execute("create table \"f\" (\"id\" int primary key, \"b_id\" int NULL)");
            db.Execute("alter table \"a\" add constraint \"FK_a_b\" foreign key (\"b_id\") references \"b\" (\"id\")");
            db.Execute("alter table \"b\" add constraint \"FK_b_a\" foreign key (\"a_id\") references \"a\" (\"id\")");
            db.Execute("alter table \"b\" add constraint \"FK_b_c\" foreign key (\"c_id\") references \"c\" (\"id\")");
            db.Execute("alter table \"b\" add constraint \"FK_b_d\" foreign key (\"d_id\") references \"d\" (\"id\")");
            db.Execute("alter table \"c\" add constraint \"FK_c_d\" foreign key (\"d_id\") references \"d\" (\"id\")");
            db.Execute("alter table \"e\" add constraint \"FK_e_a\" foreign key (\"a_id\") references \"a\" (\"id\")");
            db.Execute("alter table \"f\" add constraint \"FK_f_b\" foreign key (\"b_id\") references \"b\" (\"id\")");


            db.Execute("insert into \"d\" (\"id\") values (1)");
            db.Execute("insert into \"c\" (\"id\", \"d_id\") values (1, 1)");
            db.Execute("insert into \"a\" (\"id\") values (1)");
            db.Execute("insert into \"b\" (\"id\", \"c_id\", \"d_id\") values (1, 1, 1)");
            db.Execute("insert into \"e\" (\"id\", \"a_id\") values (1, 1)");
            db.Execute("insert into \"f\" (\"id\", \"b_id\") values (1, 1)");
            db.Execute("update \"a\" set \"b_id\" = 1");
            db.Execute("update \"b\" set \"a_id\" = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"a\"").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"b\"").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"c\"").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"d\"").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"e\"").ShouldBe(1);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"f\"").ShouldBe(1);

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User },
            });
            try
            {
                await respawner.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(respawner.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"a\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"b\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"c\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"d\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"e\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"f\"").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldHandleCircularRelationships()
        {
            using var db = await fixture.CreateDatabaseAsync();

            db.Execute("create table \"parent\" (id int primary key, childid int NULL)");
            db.Execute("create table \"child\" (id int primary key, parentid int NULL)");
            db.Execute("alter table \"parent\" add constraint FK_Child foreign key (ChildId) references \"child\" (Id)");
            db.Execute("alter table \"child\" add constraint FK_Parent foreign key (ParentId) references \"parent\" (Id)");

            for (int i = 0; i < 100; i++)
            {
                db.Execute("INSERT INTO \"parent\" VALUES (@0, null)", i);
                db.Execute("INSERT INTO \"child\" VALUES (@0, null)", i);
            }

            db.Execute("update \"parent\" set childid = 0");
            db.Execute("update \"child\" set parentid = 1");

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"parent\"").ShouldBe(100);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"child\"").ShouldBe(100);

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User },
            });
            try
            {
                await respawner.ResetAsync(db.Connection);
            }
            catch
            {
                output.WriteLine(respawner.DeleteSql ?? string.Empty);
                throw;
            }

            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"parent\"").ShouldBe(0);
            db.ExecuteScalar<int>("SELECT COUNT(1) FROM \"child\"").ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIgnoreTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");
            await db.ExecuteAsync("create table \"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User },
                TablesToIgnore = new[] { new Table("foo") }
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"bar\"")).ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIgnoreTablesWithSchema()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");
            await db.ExecuteAsync("create table \"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var createdUser = fixture.User;
            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { createdUser },
                TablesToIgnore = new[] { new Table(createdUser, "foo") }
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"bar\"")).ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIncludeTables()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");
            await db.ExecuteAsync("create table \"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { fixture.User },
                TablesToInclude = new[] { new Table("foo") }
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(0);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"bar\"")).ShouldBe(100);
        }

        [SkipOnCI]
        public async Task ShouldIncludeTablesWithSchema()
        {
            using var db = await fixture.CreateDatabaseAsync();

            await db.ExecuteAsync("create table \"foo\" (value int)");
            await db.ExecuteAsync("create table \"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"foo\" VALUES (@0)", i);
                await db.ExecuteAsync("INSERT INTO \"bar\" VALUES (@0)", i);
            }

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                TablesToInclude = new[] { new Table(fixture.User, "foo") }
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"foo\"")).ShouldBe(0);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"bar\"")).ShouldBe(100);
        }

        [SkipOnCI]
        public async Task ShouldExcludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            var userA = Guid.NewGuid().ToString().Substring(0, 8);
            var userB = Guid.NewGuid().ToString().Substring(0, 8);
            await CreateUser(db, userA);
            await CreateUser(db, userB);
            await db.ExecuteAsync("create table \"" + userA + "\".\"foo\" (value int)");
            await db.ExecuteAsync("create table \"" + userB + "\".\"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"" + userA + "\".\"foo\" VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT INTO \"" + userB + "\".\"bar\" VALUES (" + i + ")");
            }

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                // We must make sure we don't delete all these users that are used by Oracle
                SchemasToExclude = new[]
                {
                    userA, "ANONYMOUS", "APEX_040000", "APEX_PUBLIC_USER", "APPQOSSYS",
                    "CTXSYS", "DBSNMP", "DIP", "FLOWS_FILES", "HR", "MDSYS",
                    "ORACLE_OCM", "OUTLN", "SYS", "XDB", "XS$NULL", "SYSTEM",
                    "GSMADMIN_INTERNAL", "WMSYS", "OJVMSYS", "ORDSYS", "ORDDATA",
                    "LBACSYS", "APEX_040200", "DVSYS", "AUDSYS", "OLAPSYS", "SCOTT"
                }
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"" + userA + "\".\"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"" + userB + "\".\"bar\"")).ShouldBe(0);
        }

        [SkipOnCI]
        public async Task ShouldIncludeSchemas()
        {
            using var db = await fixture.CreateDatabaseAsync();

            var userA = Guid.NewGuid().ToString().Substring(0, 8);
            var userB = Guid.NewGuid().ToString().Substring(0, 8);
            await CreateUser(db, userA);
            await CreateUser(db, userB);
            await db.ExecuteAsync("create table \"" + userA + "\".\"foo\" (value int)");
            await db.ExecuteAsync("create table \"" + userB + "\".\"bar\" (value int)");

            for (int i = 0; i < 100; i++)
            {
                await db.ExecuteAsync("INSERT INTO \"" + userA + "\".\"foo\" VALUES (" + i + ")");
                await db.ExecuteAsync("INSERT INTO \"" + userB + "\".\"bar\" VALUES (" + i + ")");
            }

            var respawner = await Respawner.CreateAsync(db.Connection, new RespawnerOptions
            {
                SchemasToInclude = new[] { userB }
            });
            await respawner.ResetAsync(db.Connection);

            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"" + userA + "\".\"foo\"")).ShouldBe(100);
            (await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"" + userB + "\".\"bar\"")).ShouldBe(0);
        }

        private static async Task CreateUser(IDatabase db, string userName)
        {
            await db.ExecuteAsync($"""create user "{userName}" IDENTIFIED BY 123456""");
            await db.ExecuteAsync($"""alter user "{userName}" IDENTIFIED BY 123456 account unlock""");
            await db.ExecuteAsync($"""grant all privileges to "{userName}" IDENTIFIED BY 123456""");
        }
    }
}
