# MsgHatch.Data.EFCore.PostgreSql

The PostgreSQL package of the EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Data.EFCore` what PostgreSQL does better:

- The claim with `SKIP LOCKED`: one `UPDATE ... FROM` a subquery that reads the due rows in the order of the due time and the add, stops with `LIMIT`, and locks them with `FOR UPDATE SKIP LOCKED`. A parallel claim skips the rows that another claim locked. `RETURNING` gives the claimed rows.
- The collation `C` of the ids and the names, so an id is case-sensitive and sorts in ordinal order.
- The schema migrations of `MsgHatchDbContext` for PostgreSQL.
- A setup with one call.
- A dispatch signal through `LISTEN` and `NOTIFY`, which wakes the dispatch loops of the hosts on one database.

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

`UseEntityFrameworkPostgreSqlStores` calls `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore` with `UseNpgsql`, the dialect `PostgreSqlStoreDialect`, and the migrations of this package. The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`, from the section `EntityFramework` under the root of `AddMsgHatch`, by default `MsgHatch:EntityFramework`. An optional action changes them in code, and another one changes the Npgsql options, for example a retry on a transient failure:

[//]: # (snippet: setup-options)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(
        connectionString,
        o => o.Schema = "messaging",
        npgsql => npgsql.EnableRetryOnFailure());
```

## Inbox in the transaction of the host

An app with its own `DbContext` can add an event to the inbox in the transaction of that context (D331). The app maps the inbox with `modelBuilder.MapMsgHatchInbox(this)` in `OnModelCreating` of its context, registers the context as a scoped service on the same database, and selects the stores with the type of the context. Here `TestHostContext` is the context of the app:

[//]: # (snippet: host)
```csharp
services.AddDbContext<TestHostContext>(o => o.UseNpgsql(connectionString));
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores<TestHostContext>(connectionString);
```

The context of the app gets the dialect of PostgreSQL and the MsgHatch options of the store contexts. `AcceptAsync` adds the inbox row to the context, and does not save, so the `SaveChangesAsync` of the app writes it with the rows of the app. The migrations of the app never contain the inbox table, so `MigrateMsgHatchAsync` still creates it. The README of `MsgHatch.Data.EFCore` describes the mode and its startup checks.

## Dispatch signal

`PostgreSqlDispatchSignal` wakes the dispatch loops of the hosts that use one database of the stores, also on different machines, through `LISTEN` and `NOTIFY` of PostgreSQL (D334). Each host of the system selects it after the stores:

[//]: # (snippet: signal)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString)
    .UsePostgreSqlDispatchSignal();
```

The signal uses the connection string of the store contexts, also with the inbox in the transaction of the host. It needs no other server and no table:

- `NotifyAsync` wakes the loops of the own process at once. In the background, it runs `SELECT pg_notify('msghatch_dispatch', @payload)` on a pooled connection, with the payload `{sender}:{topic}`. Many notifications before one write become one notification for each topic. While the last write failed, `NotifyAsync` returns `Errors.Unavailable`.
- At the first `WaitAsync`, one connection of the signal runs `LISTEN msghatch_dispatch`. Each notification of another signal instance wakes the loops of its topic: `inbox`, or `outbox:{Channel}` with a valid channel name. The signal skips its own notifications and each other payload.
- When the listening connection fails or ends, `WaitAsync` returns `Errors.Unavailable`, so the loops wait for their due times and their poll. The signal listens again after a pause of 5 seconds. It logs the Warning 3300 one time for each outage, and the Information 3301 when it listens again.
- PostgreSQL gives a notification only to the listeners of the same database. Two systems in one database wake each other, which costs one empty claim.

The startup validation fails when the EF Core stores are not on PostgreSQL, and when `EnablePolling` is false in the dispatch options of the inbox or the outbox, because a notification while the signal does not listen reaches a loop only through its poll.

The listening connection needs a session of its own on the server:

- A pooler in transaction mode, for example PgBouncer, does not support `LISTEN`. The connection string must reach the server, or a pooler in session mode.
- The listening connection has no multiplexing, also when the connection string sets `Multiplexing=true`, because Npgsql does not wait for notifications in that mode.
- A firewall that drops idle connections can end a listening connection with no error. So the listening connection has a keepalive of 30 seconds: Npgsql sends a message on it after each 30 seconds with no traffic. A `Keepalive` in the connection string, for example `Keepalive=60`, replaces the 30 seconds.

## JSON columns

The dialect gives each JSON column the type `json`, which checks each write and keeps the text unchanged. The dialect does not give `jsonb`, because `jsonb` changes the order of the keys and drops duplicate keys, so an event type of a host that needs `$type` as the first key can fail to read. A host selects `jsonb` with the option `JsonColumnType`, for example `o => o.JsonColumnType = "jsonb"`. The stores write a document one time and read it whole, so `text` is faster: `json` parses each write, and `jsonb` also converts each read.

## Errors

The dialect gives `Errors.Rejected` for a missing permission or a failed login (the states `42501`, `28000`, and `28P01`), and `Errors.ValidationFailed` for a missing table or column and for a value that is too long (`42P01`, `42703`, and `22001`). So the dispatcher does not retry these errors. Each other database failure stays `Errors.Unavailable`. A classifier of the host comes before the dialect, for example a classifier that rejects a write to a read-only database:

[//]: # (snippet: classifier)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString)
    .AddExceptionClassifier<MsgHatchDbContext, PostgreSqlReadOnlyClassifier>();
```

[//]: # (snippet: classifier-type)
```csharp
public sealed class PostgreSqlReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.ReadOnlySqlTransaction } ? Errors.Rejected : null;
}
```

## Schema migrations

The package creates the schema only in the migration step. `MigrateMsgHatchAsync` applies the migrations of the package in the schema of the options, with the history table `__StoreMigrations` in that schema:

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

### 0.10.0 (2026-11-27)

- `PostgreSqlDispatchSignal` and `UsePostgreSqlDispatchSignal` wake the dispatch loops of the hosts on one database through `LISTEN` and `NOTIFY` (D334). The setup uses the connection string of the store contexts, and requires `EnablePolling` true. The listening connection has a keepalive of 30 seconds, unless the connection string sets one.

### 0.9.0 (2026-11-26)

- `UseEntityFrameworkPostgreSqlStores<TContext>` selects the stores with the inbox in the transaction of the context `TContext` of the host (D331, D332). The context gets the dialect of PostgreSQL and the MsgHatch options of the store contexts.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.5.0 (2026-11-24)

- Breaking: each JSON column is `json`, not `text`, so a write of invalid JSON fails. A JSON column has no collation. The option `JsonColumnType` selects another type, for example `jsonb`.
- Breaking: the dialect gives `Errors.Rejected` for a missing permission or a failed login, and `Errors.ValidationFailed` for a missing table or column and for a value that is too long, not `Errors.Unavailable`. So the dispatcher does not retry these errors.
- Breaking: the tables, the indexes, and the history table drop the prefix `MsgHatch`, because the schema of the options keeps them apart from the tables of the host: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, `MessageTypeEntry`, `IX_Outbox_Due`, and `__StoreMigrations`. The SQLite and MongoDB stores keep the prefix. The `Initial` migration of the package creates the new names.

### 0.4.0 (2026-11-24)

- The first version: `UseEntityFrameworkPostgreSqlStores`, the dialect `PostgreSqlStoreDialect` with the claim with `SKIP LOCKED` and the collation `C`, and the migrations of `MsgHatchDbContext` for PostgreSQL.
