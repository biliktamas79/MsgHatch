# MsgHatch stores

This guide explains how the library stores keep the inbox, the outbox, and the subscriptions, and what a store for another storage must do. It covers the JSON file stores and the SQLite stores. Phase 18 adds the section of the EF Core stores, and phase 19 adds the section of the MongoDB stores.

The design `docs/design.md` is the source of each rule. Section 4.5 has the persistence rules, section 4.6 has the contract rules, and section 4.13 has the versioning rules. This guide puts these rules together for each storage kind, and names the decision of each rule, for example D26.

## 1. Rules for each store

### 1.1 Two parts on one storage

A store has two parts (D127):

| Part | Interfaces | Lifetime | Scope of its data |
|---|---|---|---|
| Tenant store | `IInboxStore`, `IOutboxStore` | One view for each `TenantKey` | The messages of one tenant |
| Dispatch store | `IInboxDispatchStore`, `IOutboxDispatchStore` | One thread-safe singleton | The messages of all tenants |

- The library store class implements the dispatch store. Its method `For(TenantKey key)` gives the tenant store of one key.
- The tenant store is a small view that keeps its key. It reads and writes only the messages of its tenant.
- The setup method registers the tenant store with `KeyedService.AnyKey` through `For(key)`, and the unkeyed tenant store for `TenantKey.None` (D126).
- So the two parts share one storage, one lock or connection pool, and one schema setup.

### 1.2 The tenant field

Each stored message keeps its tenant in a field of its own (D114):

- The tenant store of a key reads and writes only the messages of its tenant.
- A message with another tenant than the key of the tenant store gives `Errors.ValidationFailed`, and the store adds nothing of the batch.
- The dispatch store claims the messages of all tenants. Each claimed message keeps its tenant.
- The key of a received message is its tenant and its id, because each caller gives the message id. Two tenants can give the same id.
- The id of an outgoing message is unique, because `DeterministicId` uses the tenant (section 4.8).

### 1.3 The message format

A store writes and reads whole messages with `MsgHatchJson` (D143):

- `MsgHatchJson` writes a whole `ReceivedMessage` or `OutgoingMessage` as one JSON document. It uses camelCase names and enum values as strings (D23), and it writes null values.
- It reads the document back as the closed type of the content type and version, for example `OutgoingMessage<EmailMessage>`. The registry `IContentTypeRegistry` gives that type.
- It writes a route trace with the discriminator property `$type`, for example `"$type": "Trace:Subscription@1"`.
- A store serializes one time, with the serializer of its storage. The pipeline does not serialize (section 4.11).

### 1.4 The publish tries

An outgoing message keeps the full history of its publish tries (D27):

- `RecordTryAsync` adds the try, sets `TryCount`, `LastTryAtUtc`, `LastError`, and the status, and ends the lease, in one atomic operation.
- `PublishTry.StatusAfter(nextTryAtUtc)` gives the status, and `PublishTry.ErrorText()` gives the text of `LastError`. So each store uses one rule.
- `RecordTryAsync` changes nothing when the stored `TryCount` is not the try number minus 1. So a dispatcher can repeat the call after a store failure, and a try is never recorded two times.
- A received message has no tries. `CompleteAsync` and `FailAsync` add 1 to its `TryCount` (D29).

### 1.5 The claim

The dispatch loops claim the due messages (D103, D144):

- `ClaimDueAsync` claims only pending messages with `NextTryAtUtc` at or before `nowUtc`.
- It sets `NextTryAtUtc` and `LeaseUntilUtc` to `nowUtc + lease`. So no other claim takes the message before the end of the lease.
- It takes the messages in the order of `NextTryAtUtc`. Messages with the same `NextTryAtUtc` come in the order of their add.
- It is atomic between concurrent dispatchers, also in other processes.
- `ClaimResult.NextDueAtUtc` is the earliest `NextTryAtUtc` of the pending messages after the claim, with the lease ends of the claimed messages. It is null when no message is pending.
- The outbox claim takes only the messages of one channel (D104). For a channel with a down state, it claims nothing, and returns the down state (D106).

### 1.6 Time, errors, and test data

- A store never reads the clock. The library gives each time to the store, for example `nowUtc` to `ClaimDueAsync` (section 4.14).
- A store returns an `Error` for each expected failure, and does not throw (section 4.12). A busy or missing storage gives `Errors.Unavailable`. A record that the code cannot read gives `Errors.SerializationFailed`.
- Each record keeps its `MessageFlags`. `DeleteTestDataAsync` deletes each message with `MessageFlags.Test` of all tenants, with its tries, and keeps each other message (D82).

### 1.7 Purge and counts

The delivery management of section 4.19 of the design uses these members of the dispatch stores:

- `PurgeAsync` deletes up to `maxCount` finished messages of all tenants, with their tries (D128). A finished message has the status `Done`, `Failed`, or `Cancelled`, and its finish time is `LastTryAtUtc`, or `CreatedAtUtc` for a message with no try. The library stores delete the messages that finished first. A purge also deletes what records the key of a message, so a purged id is a new message when it comes again.
- `IOutboxDispatchStore.CountAsync` counts the messages of all tenants that match a `TrafficQuery`: one `MessageCount` for each message type, channel, status, and test flag (D164). `CreatedFromUtc` is included, and `CreatedToUtc` is not. With `IncludeTest` false, it gives no row of the test messages. `OldestDueAtUtc` is set only in a row of the status `Pending`.
- A store that reads the messages into memory can use `TrafficQuery.Matches`, `DeliveryState.FinishedBefore`, and `MessageCount.Of`, so it follows the same rules as the library stores.

### 1.8 Versioning

Each persisted contract is backward compatible and has a version number (D24, section 4.13):

| Version | Owner | Where the store keeps it |
|---|---|---|
| `SchemaVersion` of a message | The library | In each message document |
| The schema version of a store | The library | A file or a table of the store, for example `MsgHatchSchema` |
| `ContentVersion` of an event or a payload | The consumer | In each message document |
| `SettingsVersion` of the channel settings of a subscription | The channel package | In each subscription record (D77) |
| The version of a route trace | The router package | In the discriminator, for example `Trace:Subscription@1` (D98) |

- A store reads an old version as the old closed type. The library upgrades it after the claim, and does not write the upgraded record back.
- A version that is newer than the code gives `Errors.UnsupportedVersion`. This error is transient, so a newer instance reads the record during a rolling deployment.
- A person can edit a stored record. The edit must obey the same rules. An invalid edit fails at read time with `Errors.SerializationFailed`.
- A golden-file test keeps one JSON sample for each released version of each persisted type. `tests/MsgHatch.Domain.Tests.Unit/Samples/` holds the samples of the library, and `tests/ExampleApp.Tests.Unit/Samples/` shows the same pattern for consumer types.

## 2. JSON file stores

`MsgHatch.Data.FileSystem` keeps one JSON file for each message (D26). `UseFileStores(rootPath)` selects the inbox and the outbox under one root folder.

### 2.1 Folders

```
{RootPath}/
├── inbox/
│   ├── pending/  claimed/  done/  failed/
│   └── ids/{tenantFolder}/{id}
├── outbox/
│   ├── {channel}/
│   │   ├── pending/  claimed/  done/  failed/
│   │   └── down.json
│   └── ids/{tenantFolder}/{id}
├── test/
│   ├── inbox/      (the same layout, for test messages)
│   └── outbox/     (the same layout, for test messages)
└── subscriptions/  (the subscription store, section 2.6)
```

- The outbox has a folder for each channel, so a claim reads only the files of its channel (D104).
- The status folder of a file is its status: `pending`, `claimed`, `done`, `failed`, or `cancelled`. An add writes a file into the folder of its status, so a message that is added as `Cancelled` is never claimed.
- `{tenantFolder}` is `no-tenant` for no tenant, and `tenants/{tenant name}` for a tenant.

### 2.2 Message format

A file name is `{dueAtUtc:yyyyMMddHHmmssfff}_{sequence:D10}_{key}.json`:

- `dueAtUtc` is the next try time. In `claimed`, it is the end of the lease.
- `sequence` counts the files that one store instance adds with the same time prefix. So a sort by name keeps the add order of the messages with the same due time (D144).
- The key of an outgoing message is its id. The key of a received message is its id and the name of its tenant.
- The name of a tenant or another id in a name is escaped with `Uri.EscapeDataString`. Then each capital letter A to Z becomes `^` and the small letter, because Windows and macOS ignore the case of a name, but an id is case-sensitive.

The file holds the message as `MsgHatchJson` writes it. Section 4.5 of the design shows a whole outgoing message file with one failed try.

### 2.3 Publish tries

- The file of an outgoing message has the property `tries`: an array of the publish tries, in the order of their record. `MsgHatchJson` ignores this property when it reads a message.
- `RecordTryAsync` writes the new content through a temporary file and a rename, into the folder of the new status, with the next try time as the due time. Then it deletes the old file. So a reader never sees half a file.
- A file of a received message has no `tries`.

### 2.4 Claim pattern

- A claim renames each due file from `pending` into `claimed`, with the lease end as its new time prefix (D103). A file in `claimed` is due again when its lease ends.
- A rename is atomic. The first caller that moves a file wins, and the others skip that file. The claims of one store instance take turns.
- On Windows, a rename goes through a handle of the source. So two processes on one folder can rarely claim one file in the same lease.
- A claimed file that the code cannot read stays out of the claim. A file with a newer version stays in `claimed` for a newer instance. A file with a permanent error moves to `failed`.
- The down state of a channel is the file `{channel}/down.json` (D106). `TryStartChannelCheckAsync` moves the next check time while it holds the file open with no sharing. So of two parallel callers, only one starts the health check of the channel.

### 2.5 Two parts and the tenant field

- `FileSystemInboxStore` and `FileSystemOutboxStore` are the dispatch stores. `For(key)` gives the tenant store of a key.
- The tenant is the property `tenant` of the message document, and a part of the key of a received message.
- Before it renames a new file into `pending`, a store creates the marker `ids/{tenantFolder}/{id}` with `FileMode.CreateNew`. A marker that already exists means that the key is stored, so `AddAsync` ignores the message.
- A test message goes under `{RootPath}/test/inbox` or `{RootPath}/test/outbox`. `DeleteTestDataAsync` deletes the `test` folder of its store.
- A purge and a count read each file of the matching status folders, and skip a file that the code cannot read (D167). A purge deletes the file and the marker of its key.

### 2.6 Subscription store

`MsgHatch.Subscriptions.Data.FileSystem` keeps one file for each subscription (D78, D149):

- The path is `{RootPath}/subscriptions/{tenantFolder}/{MessageType}/{level}/{ownerId}/{id}.json`. The level folder is `default`, `company`, or `user`, and `default` is also the owner folder of the default level.
- The file holds `schemaVersion` 1, `tenant`, the fields of `Subscription`, and the channel settings as the object `settings` with `settingsType` and `settingsVersion`.
- `FindForDeliveryAsync` reads only the folders of the requested owners.
- A file lock on the owner folder serializes the changes of one owner. A change while another caller holds the lock gives `Errors.Unavailable`.

### 2.7 Versioning and limits

- Each message document has `schemaVersion` and `contentVersion`, and each subscription file has `schemaVersion` and `settingsVersion`.
- The folder must allow an atomic rename. A network share often does not (section 6 of the design).
- The file stores suit a small volume and one machine. The file subscription store has the comment `ponytail: one file for each subscription and a folder lock suit small subscription sets, use a database store for many owners`.

## 3. SQLite stores

`MsgHatch.Data.Sqlite` keeps the inbox and the outbox in one SQLite file (D55). `UseSqliteStores(connectionString)` selects them, and `UseSqliteSubscriptionStore()` can share the same file.

### 3.1 Tables

The table and column names are the C# property names (D56):

| Table | Columns |
|---|---|
| `MsgHatchSchema` | `Version`: the schema version 1 |
| `MsgHatchInbox` | `Sequence INTEGER PRIMARY KEY`, `TenantId`, `Id`, with `UNIQUE (TenantId, Id)`, the columns of `DeliveryState`, `SchemaVersion`, `ContentType`, `ContentVersion`, `Flags`, `TraceParent`, and `Content` (the event as JSON) |
| `MsgHatchOutbox` | `Sequence INTEGER PRIMARY KEY`, `Id TEXT UNIQUE`, `TenantId`, the columns of `DeliveryState`, `SchemaVersion`, `SourceMessageId`, `MessageType`, `Channel`, `TargetKind`, `TargetId`, `ContentType`, `ContentVersion`, `Flags`, `TraceParent`, `TraceType`, `Trace`, `Content` (the envelope as JSON), and `Tries` (a JSON array) |
| `MsgHatchChannelDown` | `Channel TEXT PRIMARY KEY`, `Reason`, `SinceUtc`, `NextCheckAtUtc`, `ErrorCode`, `ErrorName`, and `Note`: one row for each down channel |

- The partial index `(NextTryAtUtc) WHERE Status = 'Pending'` of the inbox and `(Channel, NextTryAtUtc) WHERE Status = 'Pending'` of the outbox hold only the backlog and the claimed messages (D104).
- An index on `Flags` serves `DeleteTestDataAsync`.
- A purge is one `DELETE` of the finished rows, in the order of their finish time, with `LIMIT @maxCount`. A count is one `SELECT` with `GROUP BY MessageType, Channel, Status, Flags = 'Test'`.
- `Sequence` keeps the add order. The SQLite documentation says that `VACUUM` can change the `rowid` of a table with no `INTEGER PRIMARY KEY` (D144).
- Enum values are text. Times are ISO 8601 text in UTC, for example `2026-10-31T08:00:00.0000000Z` (D57).
- The tables have no `NOT NULL` constraint, because `INSERT OR IGNORE` also skips a row that breaks a `NOT NULL` constraint, with no error.

### 3.2 Message format

- `AddAsync` writes the whole message with `MsgHatchJson` (D143). SQLite takes the event or the envelope, the route trace, and the trace discriminator out of that document into their columns, with `json_extract`.
- To read a message, one SQL expression puts the columns back into one document with `json_object`, and `MsgHatchJson` reads it as the closed type.
- A column with invalid JSON goes into the document as a JSON string. So one edited row gives the error of that row, and does not fail the whole claim.

### 3.3 Publish tries

`RecordTryAsync` is one statement:

```sql
UPDATE MsgHatchOutbox
SET Tries = json_insert(Tries, '$[#]', json_object('tryNumber', @tryNumber, ...)), TryCount = @tryNumber, ...
WHERE Id = @id AND TryCount = @tryNumber - 1
```

- A changed row count of 0 means that the try is already recorded, or that the message is not stored (D27).
- Only then a second statement reads whether the id is stored. An id that is not stored gives `Errors.NotFound`.

### 3.4 Claim pattern

1. With no write lock, the claim reads `MIN(NextTryAtUtc)` of the pending rows, of its channel for the outbox. The outbox claim also reads the row of its channel in `MsgHatchChannelDown`. With a down row, it claims nothing, and returns the row as `ClaimResult.Down` (D106). With no due row, it returns the minimum as `NextDueAtUtc`.
2. Else it starts a `BEGIN IMMEDIATE` transaction, which holds the write lock from its start.
3. A first statement reads the first due rows in the claim order: `ORDER BY NextTryAtUtc, Sequence LIMIT @max` (D144).
4. A second statement leases these rows with `UPDATE ... WHERE Sequence IN (SELECT value FROM json_each(@sequences)) RETURNING ...`. SQLite gives the rows of `RETURNING` in an arbitrary order, so the store returns them in the order of the first statement.
5. When the first statement reads fewer than `maxCount` rows, a third statement reads `MIN(NextTryAtUtc)` of the pending rows as `NextDueAtUtc`. Else `NextDueAtUtc` is now, because more rows can be due.

A claimed row that the code cannot read stays out of the result. A transient error, for example a newer version, keeps the row pending until the lease ends. A permanent error sets the status `Failed`, ends the lease, and writes `{ErrorName}: {message}` into `LastError`.

The down state uses `INSERT OR IGNORE` for the reason `Errors`, so the first loop wins, and `INSERT OR REPLACE` for the reason `Operator`. `TryStartChannelCheckAsync` is one `UPDATE` with the condition `NextCheckAtUtc <= @now`. A changed row count of 1 means that this caller runs the health check.

### 3.5 Two parts and the tenant field

- `SqliteInboxStore` and `SqliteOutboxStore` are the dispatch stores. `For(key)` gives the tenant store of a key.
- The tenant is the column `TenantId`. The store writes an empty text for no tenant, so a unique index treats "no tenant" as one value.
- `UNIQUE (TenantId, Id)` of the inbox lets two tenants give the same message id. The claim uses `Sequence`, not the id.
- `AddAsync` uses `INSERT OR IGNORE` in one transaction for the batch, so a second add of an id changes nothing.

### 3.6 Subscription store

`MsgHatch.Subscriptions.Data.Sqlite` keeps the table `MsgHatchSubscription`, and its own schema version in `MsgHatchSubscriptionSchema`, so it can share the file of the core stores:

- The columns are the fields of `Subscription`, with `TenantId`, `SettingsType`, `SettingsVersion`, `Settings` (the channel settings as JSON, D149), and `NamedUsers` (a JSON array, or null).
- The unique index on `(TenantId, MessageType, Level, CompanyId, UserId, Channel, Recipients)` stops a duplicate, and also serves `FindForDeliveryAsync`. So the table has no second index (checked fact of phase 10).
- The store writes an empty text for a missing tenant, company, user, or channel, because SQLite treats each NULL as a different value in a unique index.
- `UpdateAsync` uses `WHERE Id = @id AND Revision = @expected`. A changed row count of 0 gives `Errors.Conflict`.

### 3.7 Connections, versioning, and limits

- Each operation opens its own connection. `Microsoft.Data.Sqlite` pools the connections.
- The first operation in the process sets `PRAGMA journal_mode = WAL`, and creates the tables with `CREATE TABLE IF NOT EXISTS`, one time for each connection string.
- A busy database waits through the command timeout, the `Default Timeout` of the connection string, 30 seconds by default (checked fact of phase 8). After it, the store gives `Errors.Unavailable`.
- The version of the store schema is in `MsgHatchSchema`. The version of each message is in the columns `SchemaVersion` and `ContentVersion`, and the version of a route trace is in `TraceType`.
- The database file must be on a local disk. The stores do not create the folder of the file. SQLite works only for processes on one machine.

## 4. A store for another storage

A consumer can write a store for another storage. Do these steps:

1. Implement the dispatch store and its method `For(TenantKey key)`, or two classes on one storage.
2. Keep the tenant, the flags, the versions, and the tries of each message in fields that a query can read.
3. Write and read each message with `MsgHatchJson`, and each subscription setting with `ChannelSettingsJson`.
4. Make `ClaimDueAsync` and `RecordTryAsync` atomic in your storage.
5. Register the store with `UseInboxStore`, `UseOutboxStore`, `UseInboxDispatchStore`, and `UseOutboxDispatchStore`, or with `UseSubscriptionStore`.
6. Add one subclass of each contract base class, for example `OutboxStoreTestsBase<TStore>` and `SubscriptionStoreTestsBase<TStore>` (section 5.5 of the design).
7. Keep a schema version in your storage, and a golden-file sample for each released version of your own types.

The in-memory stores of `MsgHatch.Domain` follow each rule and pass each contract base class, so they are the reference implementation (D119).
