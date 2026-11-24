# MsgHatch.Data.EFCore.PostgreSql

The PostgreSQL package of the EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Data.EFCore` what PostgreSQL does better:

- The claim with `SKIP LOCKED`: one `UPDATE ... FROM` a subquery that reads the due rows in the order of the due time and the add, stops with `LIMIT`, and locks them with `FOR UPDATE SKIP LOCKED`. A parallel claim skips the rows that another claim locked. `RETURNING` gives the claimed rows.
- The collation `C` of the ids and the names, so an id is case-sensitive and sorts in ordinal order.
- The schema migrations of `MsgHatchDbContext` for PostgreSQL.
- A setup with one call.

Each time is a `timestamptz`, which keeps microseconds. A time comes back cut down to the microsecond.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore.PostgreSql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString);
```

`UseEntityFrameworkPostgreSqlStores` calls `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore` with `UseNpgsql`, the dialect `PostgreSqlStoreDialect`, and the migrations of this package. The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`, from the section `MsgHatch:EntityFramework`. An optional action changes them in code, and another one changes the Npgsql options, for example a retry on a transient failure:

[//]: # (snippet: setup-options)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(
        connectionString,
        o => o.Schema = "messaging",
        npgsql => npgsql.EnableRetryOnFailure());
```

## Schema migrations

The package creates the schema only in the migration step. `MigrateMsgHatchAsync` applies the migrations of the package in the schema of the options, with the history table `__MsgHatchMigrations` in that schema:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

In a setup where only the pipeline has the rights to change the schema, a console app of the pipeline calls the method with a connection string of its own, and the service gets only the rights to read and write rows:

[//]: # (snippet: migrate-own-setup)
```csharp
await serviceProvider.MigrateMsgHatchAsync(o => o.UseNpgsql(migrationConnectionString), cancellationToken);
```

A pipeline or a database administrator that runs only SQL gets an idempotent script:

[//]: # (snippet: script)
```csharp
var script = await serviceProvider.ScriptMsgHatchMigrationsAsync(cancellationToken);
```

With `RequireCurrentSchema`, the start of the service fails when a migration is not applied. The check reads only the history table:

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "EntityFramework": {
      "RequireCurrentSchema": true
    }
  }
}
```

## Version history

### 0.4.0 (2026-11-24)

- The first version: `UseEntityFrameworkPostgreSqlStores`, the dialect `PostgreSqlStoreDialect` with the claim with `SKIP LOCKED` and the collation `C`, and the migrations of `MsgHatchDbContext` for PostgreSQL.
