# MsgHatch stores

This guide explains how the library stores keep the inbox, the outbox, the subscriptions, and the registry settings, and what a store for another storage must do. It covers the JSON file stores, the SQLite stores, and the EF Core stores. Phase 19 adds the section of the MongoDB stores.

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

### 1.7 Delivery management

The delivery management of section 4.19 of the design uses these members of the tenant stores and the dispatch stores (D108):

- `QueryAsync` of a tenant store gives a page of summaries of the messages of its tenant, newest first: `CreatedAtUtc` descending, then `Id` descending (D112). An id compares as `Guid.CompareTo`, which is the order of its text in the format `D`. A summary has no content, so a SQL store reads only columns.
- `GetAsync` gives the whole message of its tenant, and for the outbox all its tries in the order of their numbers. An unknown id, or an id of another tenant, gives null.
- `RetryAsync` and `CancelAsync` change only the messages of its tenant, each in one atomic operation, and return the number of changed messages. The rules are in `DeliveryState.RetriedAt` and `DeliveryState.CancelledAt` (D110, D111). The cancel of an outgoing message records the try of `PublishTry.Cancel` with the next try number, and keeps `LastError`.
- `CountAsync` of a tenant store counts only its tenant.

- `PurgeAsync` deletes up to `maxCount` finished messages of all tenants, with their tries (D128). A finished message has the status `Done`, `Failed`, or `Cancelled`, and its finish time is `LastTryAtUtc`, or `CreatedAtUtc` for a message with no try. The library stores delete the messages that finished first. A purge also deletes what records the key of a message, so a purged id is a new message when it comes again.
- `IOutboxDispatchStore.CountAsync` counts the messages of all tenants that match a `TrafficQuery`: one `MessageCount` for each message type, channel, status, and test flag (D164). `CreatedFromUtc` is included, and `CreatedToUtc` is not. With `IncludeTest` false, it gives no row of the test messages. `OldestDueAtUtc` is set only in a row of the status `Pending`.
- A store that reads the messages into memory can use `InboxQuery.Matches`, `OutboxQuery.Matches`, `TrafficQuery.Matches`, `MessagePage.Of`, `DeliveryState.FinishedBefore`, and `MessageCount.Of`, so it follows the same rules as the library stores. `MsgHatchJson.DeserializeTries` reads the JSON array of the tries.

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

### 1.9 Registry settings

A registry settings store keeps the stored settings of the channels and the message types on top of the code (D118, section 4.20 of the design):

- The settings belong to the whole system, not to a tenant (D114). So the store has no tenant field and no `For(TenantKey key)`.
- An entry is a `ChannelEntry` or a `MessageTypeEntry`. A save writes the entry only when the stored revision is `entry.Revision - 1`, and a missing entry has the revision 0. Else it gives `Errors.Conflict` (D79). The library writer gives each entry its revision and its `LastModifiedAtUtc`.
- A delete of an unknown entry succeeds. After a delete, a new entry starts at the revision 1 again.
- `GetEntriesAsync` gives each list in the ordinal order of the names, so that each store gives the same result.
- `DisabledChannels` round-trips in its order. A channel entry and a message type entry with the same name are two entries.
- `StoredRegistry` reads all entries when the container creates it and after each `RegistryOptions.RefreshInterval`. A read error keeps its last snapshot. So a store gives `Errors.SerializationFailed` for an entry that a person changed into an invalid value, and does not skip it.

### 1.10 A storage for each tenant

A host can keep one storage for each tenant, for example one SQLite file for each tenant (D125). The setup methods of one shared storage do not change.

- The consumer implements `ITenantSource`. `GetTenantsAsync` gives each tenant that has its own storage. A tenant that the source does not give gets no claim, so the source must give each tenant that has messages.
- The storage package registers one store instance for each key, on the storage of that key. The key `TenantKey.None` uses the system storage: the messages with no tenant, the down states of the channels, and the registry settings.
- The package calls `UseStorageForEachTenant<TTenantSource>` with four functions: the inbox tenant store, the inbox dispatch store, the outbox tenant store, and the outbox dispatch store of one key. The core then registers the dispatch composites `TenantInboxDispatchStore` and `TenantOutboxDispatchStore`. A tenant store needs no composite, because it uses only the storage of its key.

A claim of a composite:

1. Reads the tenants from the tenant source. An error of the source fails the claim.
2. Claims from the store of `TenantKey.None`. With a down state of the channel there, it claims nothing more, and returns that state (D106). An error of this store fails the claim.
3. Asks the store of each tenant in turn, from the tenant after the last one that gave messages, until it has `maxCount` messages.
4. Skips a tenant whose known next due time is later than now, or that has no pending message. One claim in each `MaxPollInterval` asks all tenants.
5. Skips a tenant whose store gives an error, and logs the error with the tenant, event 1502 (D173). `Errors.OperationCancelled` fails the claim.

The known next due times:

- A claim keeps the next due time of each tenant and channel.
- A successful `AddAsync` or `RetryAsync` of a tenant store of the same process clears the known times of its tenant. A recorded try or a failure with a next try time clears them too. So the next claim asks that tenant.
- A message that another process adds is found by the next claim that asks all tenants, at most `MaxPollInterval` later.

Other rules:

- The claim order of D144 holds only inside the storage of one key. A claim returns the messages of the system storage first, and then the messages of each tenant in turn.
- `RecordTryAsync`, `CompleteAsync`, and `FailAsync` go to the store of the tenant of the message.
- The down state methods go to the store of `TenantKey.None`.
- `CountAsync` sums the counts of all stores. `DeleteTestDataAsync` and `PurgeAsync` call each store, and `PurgeAsync` stops at `maxCount` messages in total. Each of them returns the first error.
- The composite asks each tenant store. With many tenants, this sweep costs more time than the sends, so use one shared storage there.

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
├── subscriptions/  (the subscription store, section 2.6)
└── registry/       (the registry settings store, section 2.7)
    ├── channels/{channel}.json
    └── message-types/{type}.json
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
- A query, a purge, and a count read each file of the matching status folders, and skip a file that the code cannot read (D167). A purge deletes the file and the marker of its key.
- A retry and a cancel run while no claim of the same store instance runs. They write the file through a temporary file into the folder of its new status, for example `failed` to `pending` for a retry, or `pending` to `cancelled` for a cancel.

### 2.6 Subscription store

`MsgHatch.Subscriptions.Data.FileSystem` keeps one file for each subscription (D78, D149):

- The path is `{RootPath}/subscriptions/{tenantFolder}/{MessageType}/{level}/{ownerId}/{id}.json`. The level folder is `default`, `company`, or `user`, and `default` is also the owner folder of the default level.
- The file holds `schemaVersion` 1, `tenant`, the fields of `Subscription`, and the channel settings as the object `settings` with `settingsType` and `settingsVersion`.
- `FindForDeliveryAsync` reads only the folders of the requested owners.
- A file lock on the owner folder serializes the changes of one owner. A change while another caller holds the lock gives `Errors.Unavailable`.

### 2.7 Registry settings store

`FileSystemRegistrySettingsStore` keeps one file for each entry under `{RootPath}/registry` (section 4.20 of the design). `UseFileStores` registers it.

- The file holds the entry as one JSON document with camelCase names, for example `{"channel":"Email","displayName":"Shop email","description":null,"enabled":false,"revision":1,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}`. A message type file also has `disabledChannels` as an array of channel names.
- A new entry, with the revision 1, is a file that the store creates with `FileMode.CreateNew`. So of two writers only one creates it, and the other gets `Errors.Conflict`.
- A change opens the file with `FileShare.None`, reads the stored revision, and writes the entry into the same open file. A change while another caller holds the file gives `Errors.Unavailable`.
- A file with invalid JSON, the JSON null, or an invalid channel or message type name gives `Errors.SerializationFailed` for the whole read.

### 2.8 Versioning and limits

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
| `MsgHatchChannelEntry` | `Channel TEXT PRIMARY KEY`, `DisplayName`, `Description`, `Enabled` (0 or 1), `Revision`, and `LastModifiedAtUtc`: one row for each channel entry (section 4.20) |
| `MsgHatchMessageTypeEntry` | `Type TEXT PRIMARY KEY`, `DisplayName`, `Description`, `Enabled` (0 or 1), `DisabledChannels` (a JSON array of channel names), `Revision`, and `LastModifiedAtUtc`: one row for each message type entry (section 4.20) |

- The partial index `(NextTryAtUtc) WHERE Status = 'Pending'` of the inbox and `(Channel, NextTryAtUtc) WHERE Status = 'Pending'` of the outbox hold only the backlog and the claimed messages (D104).
- An index on `Flags` serves `DeleteTestDataAsync`.
- The index `(TenantId, CreatedAtUtc, Id)` of each message table serves the pages of a query. The indexes `(TenantId, TargetKind, TargetId, CreatedAtUtc)` and `(SourceMessageId)` of the outbox serve the filters of a target and of an event. They come with `CREATE INDEX IF NOT EXISTS`, so the schema version stays 1.
- A query is one `SELECT` with `ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @limit`, and the cursor in the `WHERE` clause. A retry and a cancel are each one `UPDATE` of the rows of the ids, and the cancel of the outbox adds its try with `json_insert`.
- A purge is one `DELETE` of the finished rows, in the order of their finish time, with `LIMIT @maxCount`. A count is one `SELECT` with `GROUP BY MessageType, Channel, Status, Flags = 'Test'`.
- `Sequence` keeps the add order. The SQLite documentation says that `VACUUM` can change the `rowid` of a table with no `INTEGER PRIMARY KEY` (D144).
- Enum values are text. Times are ISO 8601 text in UTC, for example `2026-10-31T08:00:00.0000000Z` (D57).
- The tables have no `NOT NULL` constraint, because `INSERT OR IGNORE` also skips a row that breaks a `NOT NULL` constraint, with no error.
- The registry tables of phase 16 come with `CREATE TABLE IF NOT EXISTS`, so a database of an earlier phase gets them at its first open, and the schema version stays 1.

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

### 3.7 Registry settings store

`SqliteRegistrySettingsStore` keeps the registry settings in the tables `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry` of the same database (section 4.20 of the design). `UseSqliteStores` registers it.

- A new entry, with the revision 1, is an `INSERT OR IGNORE`. A change is an `UPDATE ... WHERE Channel = @name AND Revision = @revision - 1`, or with `Type` for a message type. A changed row count of 0 gives `Errors.Conflict`.
- `GetEntriesAsync` reads each table with `ORDER BY` the name. The names have only ASCII letters, digits, and `_`, so the binary order of SQLite is the ordinal order.
- A row with invalid JSON in `DisabledChannels`, an invalid name, or an invalid time gives `Errors.SerializationFailed`.

### 3.8 Connections, versioning, and limits

- Each operation opens its own connection. `Microsoft.Data.Sqlite` pools the connections.
- The first operation in the process sets `PRAGMA journal_mode = WAL`, and creates the tables with `CREATE TABLE IF NOT EXISTS`, one time for each connection string.
- A busy database waits through the command timeout, the `Default Timeout` of the connection string, 30 seconds by default (checked fact of phase 8). After it, the store gives `Errors.Unavailable`.
- The version of the store schema is in `MsgHatchSchema`. The version of each message is in the columns `SchemaVersion` and `ContentVersion`, and the version of a route trace is in `TraceType`.
- The database file must be on a local disk. The stores do not create the folder of the file. SQLite works only for processes on one machine.

### 3.9 A file for each tenant

`UseSqliteTenantStores<TTenantSource>` keeps one database file for each tenant (section 1.10, D125). `SqliteTenantStoreOptions` binds from the section `MsgHatch:SqliteTenants`:

| Option | Example | Rule |
|---|---|---|
| `TenantConnectionString` | `Data Source=data/msghatch-{tenant}.db` | Required. It must contain `{tenant}`. |
| `SystemConnectionString` | `Data Source=data/msghatch.db` | Required. It must not contain `{tenant}`. |

- The store puts the tenant id into `{tenant}` with the escape of the file stores (D172): `Uri.EscapeDataString`, and then each capital letter A to Z as `^` and the small letter. So the tenant `a|b` gives `msghatch-a%7^cb.db`, and the tenants `Acme` and `acme` give `msghatch-^acme.db` and `msghatch-acme.db`.
- `SqliteInboxStore` and `SqliteOutboxStore` are keyed singletons with `KeyedService.AnyKey`, one instance for each key, on the file of that key. The tenant store of a key is `For(key)` of that instance.
- The system file holds the messages with no tenant, the down states, and the registry settings of `SqliteRegistrySettingsStore`.
- Each file has the full schema of section 3.1, and the store creates it at the first operation on the file. So the first claim makes the file of each tenant of the tenant source.
- `UseSqliteTenantSubscriptionStore` of `MsgHatch.Subscriptions.Data.Sqlite` keeps the subscriptions of each key in the file of that key, with the same options.
- A setup with `UseSqliteStores` and `UseSqliteTenantStores` fails the startup validation, because two methods select the stores (D62). A value in code in both `UseSqliteTenantStores` and `UseSqliteTenantSubscriptionStore` fails it too (D150).

## 4. EF Core stores

`MsgHatch.Data.EntityFrameworkCore` keeps the inbox, the outbox, the down states, and the registry settings in a database through EF Core, with SQL Server as the first provider (D120). `UseEntityFrameworkStores(o => o.UseSqlServer(connectionString))` selects them. `MsgHatch.Subscriptions.Data.EntityFrameworkCore` adds the subscription store with `UseEntityFrameworkSubscriptionStore(o => o.UseSqlServer(connectionString))`.

### 4.1 Model and tables

`MsgHatchDbContext` maps the tables of section 3.1 with the same table and column names, in the schema of `EntityFrameworkStoreOptions.Schema`. The options bind from the section `MsgHatch:EntityFramework`, and the default schema is `msghatch`. There is no table `MsgHatchSchema`, because the migration history of the host keeps the version of the schema.

| Value | SQL Server type | Rule |
|---|---|---|
| `Sequence` | `bigint` identity, the clustered primary key of a message table | It keeps the add order (D144). |
| A tenant, company, user, or target id | `nvarchar(256)` with the collation `Latin1_General_100_BIN2` | An id is case-sensitive, so two tenants that differ only in case stay apart (D178). An empty text means no tenant. |
| A message id | `char(36)` in the format `D` | The text sorts as `Guid.CompareTo`, so the pages of a query keep the order of D112. A `uniqueidentifier` sorts by its last bytes first (checked fact of phase 18). |
| A message type, channel, content type, or trace type name | `varchar(256)` with the binary collation | A longer name gives `Errors.Unavailable` (D178). The binary collation gives the ordinal order. |
| An enum value | `varchar(32)` | The name of the value, for example `Pending`. |
| A time | `datetime2(7)` in UTC | The store gives each time that it reads the kind UTC. |
| JSON | `nvarchar(max)` | `Content`, `Trace`, `Tries`, `DisabledChannels`, `Settings`, and `NamedUsers`. |

The indexes have the names of the model, for example `IX_MsgHatchOutbox_Due` on `(Channel, NextTryAtUtc)` with the filter `Status = 'Pending'` (D104). The other indexes are the indexes of section 3.1: the unique `(TenantId, Id)` of the inbox and `Id` of the outbox, `Flags`, `(TenantId, CreatedAtUtc, Id)`, `(TenantId, TargetKind, TargetId, CreatedAtUtc)`, and `SourceMessageId`.

The package creates no schema. The host adds EF Core migrations of `MsgHatchDbContext` and `MsgHatchSubscriptionDbContext` to its own assembly, for example with `o.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("MyHost"))`, and applies them. The integration tests use `EnsureCreatedAsync`, which creates the tables only when it creates the database. So use migrations for a database with both contexts.

### 4.2 Message format

- `AddAsync` writes the whole message with `MsgHatchJson`. SQL Server takes the event or the envelope, the route trace, and its `$type` out of that document into their columns with `JSON_QUERY` and `JSON_VALUE` (D143).
- To read a message, the store writes the document from the columns of the row with `Utf8JsonWriter`, and `MsgHatchJson` reads it (D176).
- A JSON column that is not valid JSON goes into the document as a JSON string. So an edited row gives the error of that row only, and an edited column cannot add a property to the document, for example a second `tenant`.

### 4.3 Publish tries

`RecordTryAsync` is one statement:

```sql
UPDATE [msghatch].[MsgHatchOutbox] SET
    Tries = JSON_MODIFY(Tries, 'append $', JSON_QUERY(@try)), Status = @status, TryCount = @tryNumber, ...
WHERE Id = @id AND TryCount = @tryNumber - 1
```

- `@try` is the try as one JSON object. Without `JSON_QUERY`, `JSON_MODIFY` appends the object as a JSON string (checked fact of phase 18).
- A changed row count of 0 means that the try is already recorded (D27), or that the message is not stored. Only then a second query reads whether the id is stored, and an id that is not stored gives `Errors.NotFound`.
- The cancel of an outgoing message appends the cancelled try with `JSON_MODIFY(@try, '$.tryNumber', TryCount + 1)`, so each row gets its next try number in the same statement.

### 4.4 Claim pattern

The claim is one statement (D120, D144):

```sql
UPDATE [msghatch].[MsgHatchOutbox] SET NextTryAtUtc = @leaseUntilUtc, LeaseUntilUtc = @leaseUntilUtc
OUTPUT inserted.Sequence, ..., deleted.NextTryAtUtc AS NextTryAtUtc, ...
WHERE Sequence IN (
    SELECT TOP (@maxCount) due.Sequence FROM [msghatch].[MsgHatchOutbox] AS due WITH (ROWLOCK, READPAST, UPDLOCK)
    WHERE due.Status = 'Pending' AND due.Channel = @channel AND due.NextTryAtUtc <= @nowUtc
    ORDER BY due.NextTryAtUtc, due.Sequence)
```

1. The subquery reads the filtered due index in the claim order, and stops after `maxCount` rows. `UPDLOCK` locks only these rows, and `READPAST` lets a parallel claim skip them. So two parallel claims get different messages.
2. An `UPDATE` of a CTE with the `TOP` puts a sort under the `TOP` as the Halloween protection, because the statement changes the key of the index that it reads. The sort reads and locks each due row, so a parallel claim got only 0 to 3 of 5 rows (checked fact of phase 18).
3. `OUTPUT` gives the rows in no order. The store sorts them by the due time before the lease, which `deleted.NextTryAtUtc` gives, and by `Sequence`. The end of the lease is the next due time of a claimed message (D103).
4. A claimed row that the code cannot read stays out of the result. A transient error keeps the row leased for a newer instance. A permanent error sets the status `Failed`, ends the lease, and writes `{ErrorName}: {message}` into `LastError`.
5. When the claim takes fewer than `maxCount` rows, a query reads `MIN(NextTryAtUtc)` of the pending rows as `NextDueAtUtc`. Else `NextDueAtUtc` is now.

The outbox claim first reads the down row of its channel. With a down row, it claims nothing, and returns the row as `ClaimResult.Down` (D106). `SetChannelDownAsync` with the reason `Errors` is an `INSERT ... WHERE NOT EXISTS` with `UPDLOCK, HOLDLOCK`, so the first loop wins. With the reason `Operator`, it is a `MERGE ... WITH (HOLDLOCK)`. `TryStartChannelCheckAsync` is one `UPDATE` with the condition `NextCheckAtUtc <= @now`.

### 4.5 Two parts and the tenant field

- `EntityFrameworkInboxStore` and `EntityFrameworkOutboxStore` are the dispatch stores. `For(key)` gives the tenant store of a key.
- `AddAsync` is an `INSERT ... SELECT ... WHERE NOT EXISTS` with `UPDLOCK, HOLDLOCK` on the key. So a second add of a key changes nothing, also from a parallel caller. The outbox adds a batch in one transaction, inside the execution strategy of the provider.
- The statements that LINQ can express are LINQ: a query page with `OrderByDescending` and `Take`, a count with `GroupBy`, a retry and the cancel of a received message with `ExecuteUpdateAsync`, and a purge with `ExecuteDeleteAsync`. A query reads only the columns of a summary.

### 4.6 Subscription store

`MsgHatchSubscriptionDbContext` maps the table `MsgHatchSubscription` with the columns of section 3.6, in the same schema and with the same column rules. It is a context of its own, because the core packages never reference `MsgHatch.Subscriptions`.

- The unique index `UX_MsgHatchSubscription_Owner` on `(TenantId, MessageType, Level, CompanyId, UserId, Channel, Recipients)` stops a duplicate, and also serves `FindForDeliveryAsync`. The store writes an empty text for a missing tenant, company, user, or channel.
- The declared size of this index is more than 1,700 bytes, so SQL Server creates it with a warning. A real row has a company id or a user id, never both, so its key stays below the limit (D178).
- `AddAsync` inserts the row with EF Core. A failed insert gives `Errors.Conflict` when another row has the id or the same owner, message type, channel, and recipient kind.
- `UpdateAsync` is one `ExecuteUpdateAsync` with the id and the expected revision. A changed row count of 0 gives `Errors.Conflict` for a stored id, and `Errors.NotFound` else. A change into the key of another row gives `Errors.Conflict`.
- `UseEntityFrameworkSubscriptionStore` shares `EntityFrameworkStoreOptions` with `UseEntityFrameworkStores` (D59), and registers its own pooled context factory.

### 4.7 Registry settings store

`EntityFrameworkRegistrySettingsStore` keeps the registry settings in `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry` (section 4.20 of the design). `UseEntityFrameworkStores` registers it.

- A new entry, with the revision 1, is an EF Core insert. A failed insert of a stored name gives `Errors.Conflict`.
- A change is one `ExecuteUpdateAsync` with the condition `Revision = @revision - 1`. A changed row count of 0 gives `Errors.Conflict`.
- `GetEntriesAsync` sorts each list with the ordinal comparer, so each provider gives the same order.

### 4.8 Connections, versioning, and limits

- Each operation makes its own context through the pooled `IDbContextFactory` of its context, so the stores are thread-safe singletons (D130).
- The stores have SQL statements only for SQL Server. With another provider, the startup validation fails with an `OptionsValidationException` that names the provider, and each store call gives `Errors.NotFound` (D175).
- A database failure gives `Errors.Unavailable`, a stored value that a person changed into an invalid value gives `Errors.SerializationFailed`, and a cancelled token gives `Errors.OperationCancelled`.
- The statements need SQL Server 2016 or later, for `JSON_MODIFY`, `JSON_QUERY`, `JSON_VALUE`, and `OPENJSON`. The integration tests run on SQL Server 2025 CU7.
- The schema is a part of the options of each context. So two hosts with two schemas in one process get two models.

## 5. A store for another storage

A consumer can write a store for another storage. Do these steps:

1. Implement the dispatch store and its method `For(TenantKey key)`, or two classes on one storage.
2. Keep the tenant, the flags, the versions, and the tries of each message in fields that a query can read.
3. Write and read each message with `MsgHatchJson`, and each subscription setting with `ChannelSettingsJson`.
4. Make `ClaimDueAsync`, `RecordTryAsync`, `RetryAsync`, and `CancelAsync` atomic in your storage.
5. Register the store with `UseInboxStore`, `UseOutboxStore`, `UseInboxDispatchStore`, and `UseOutboxDispatchStore`, with `UseSubscriptionStore`, or with `UseRegistrySettingsStore`.
6. Add one subclass of each contract base class, for example `OutboxStoreTestsBase<TStore>`, `SubscriptionStoreTestsBase<TStore>`, and `RegistrySettingsStoreTestsBase<TStore>` (section 5.5 of the design).
7. Keep a schema version in your storage, and a golden-file sample for each released version of your own types.
8. For one storage for each tenant, call `UseStorageForEachTenant<TTenantSource>` with the four parts of the storage of each key, in place of the four methods of step 5 (section 1.10).

The in-memory stores of `MsgHatch.Domain` follow each rule and pass each contract base class, so they are the reference implementation (D119).
