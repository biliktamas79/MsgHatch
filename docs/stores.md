# MsgHatch stores

This guide explains how the library stores keep the inbox, the outbox, the subscriptions, and the registry settings, and what a store for another storage must do. It covers the JSON file stores, the SQLite stores, the EF Core stores, and the MongoDB stores.

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

### 1.10 Channel states

The outbox dispatch store keeps the state of each channel as insert-only `ChannelState` rows (D106, D277):

- A row is `Up`, or `Down` with the reason, `NextCheckAtUtc`, the error code, the error name, and the note. A channel with no row is up. The latest row is the current state.
- Each row has `Version`: 1 for the first row of the channel, and the version of the latest row plus 1. The store keeps a unique key on the channel and the version.
- Each row has `PreviousKind` and `PreviousDuration`: the kind of the previous row, and the time since its `SinceUtc` in whole milliseconds, 0 for a negative difference. Both are null for the version 1. So the up time of a period is the sum of `PreviousDurationMs` of the rows with `PreviousKind = 'Up'`, plus the open time of an up latest row.
- `SetChannelDownAsync` and `SetChannelUpAsync` read the latest row, and insert the row of `ChannelState.AfterDown` or `ChannelState.AfterUp`, or nothing when the state does not change. `SetChannelUpAsync` has the parameter `upAtUtc`, because a store reads no clock.
- A change is one insert, so no store needs a transaction. When another caller added the same version first, the insert adds nothing, and the store reads the latest row again. `ChannelState.Change` and `ChangeAsync` do these tries, at most three, and then give `Errors.Conflict`.
- `TryStartChannelCheckAsync` updates only `NextCheckAtUtc` of the latest row. A store never changes another value of a row, and keeps all rows.
- `GetChannelStatesAsync(fromUtc)` gives the rows at or after `fromUtc` and the row of each channel that was current at `fromUtc`, newest first: by `SinceUtc`, then by the ordinal order of the channel names, then by the version. `ChannelState.NewestFirst` gives this order for a store that reads the rows into memory.

### 1.11 A storage for each tenant

A host can keep one storage for each tenant, for example one SQLite file for each tenant (D125). The setup methods of one shared storage do not change.

- The consumer implements `ITenantSource`. `GetTenantsAsync` gives each tenant that has its own storage. A tenant that the source does not give gets no claim, so the source must give each tenant that has messages.
- The storage package registers one store instance for each key, on the storage of that key. The key `TenantKey.None` uses the system storage: the messages with no tenant, the state rows of the channels, and the registry settings.
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
- The state methods of the channels, `GetChannelStatesAsync` too, go to the store of `TenantKey.None`.
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
│   │   └── states/{version:D10}.json
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
- `RecordTryAsync` runs while no claim, retry, cancel, or other try record of the same store instance runs. Without this gate, 8 parallel records of the first try of each of 20 messages kept one try for each message, but about 55 of the 160 calls gave `Errors.Unavailable`, because a rename or a delete found the file of another caller (checked after phase 18).
- A file of a received message has no `tries`.

### 2.4 Claim pattern

- A claim renames each due file from `pending` into `claimed`, with the lease end as its new time prefix (D103). A file in `claimed` is due again when its lease ends.
- A rename is atomic. The first caller that moves a file wins, and the others skip that file. The claims of one store instance take turns.
- On Windows, a rename goes through a handle of the source. So two processes on one folder can rarely claim one file in the same lease.
- A claimed file that the code cannot read stays out of the claim. A file with a newer version stays in `claimed` for a newer instance. A file with a permanent error moves to `failed`.
- Each state row of a channel is the file `{channel}/states/{version:D10}.json` (D277). `MessageFiles.TryCreate` writes the whole row to a temporary file, and gives it the path of its version with a call that fails when the file exists: `link()` on Linux and macOS, and `File.Move` with no overwrite on Windows (D312). So of two parallel inserts of one version, only the first succeeds, and a reader never sees half a file. The file with the highest version is the current state.
- On Unix, `File.Move` with no overwrite is not enough. .NET does it as `lstat` of the target and then `rename`, and `rename` replaces a file that another caller created between the two calls. An error of `link()` other than `EEXIST`, for example on a file system with no hard links, gives `Errors.Unavailable`.
- `TryStartChannelCheckAsync` moves the next check time of the latest file while it holds the file open with no sharing. So of two parallel callers, only one starts the health check of the channel.
- A golden-file sample `ChannelState.v1.json` of the file store tests keeps the row format, with camelCase names, enum values as strings, and `previousDuration` as a `TimeSpan` text.

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
- The folder must allow an atomic rename, and on Linux and macOS also hard links. A network share often does not (section 6 of the design).
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
| `MsgHatchChannelState` | `Channel`, `Version`, `Kind`, `SinceUtc`, `PreviousKind`, `PreviousDurationMs` (an integer), `Reason`, `NextCheckAtUtc`, `ErrorCode`, `ErrorName`, and `Note`, with `PRIMARY KEY (Channel, Version)`: one row for each state of a channel (D277) |
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

1. With no write lock, the claim reads `MIN(NextTryAtUtc)` of the pending rows, of its channel for the outbox. The outbox claim also reads the latest row of its channel in `MsgHatchChannelState`. With a down row, it claims nothing, and returns the row as `ClaimResult.Down` (D106). With no due row, it returns the minimum as `NextDueAtUtc`.
2. Else it starts a `BEGIN IMMEDIATE` transaction, which holds the write lock from its start.
3. A first statement reads the first due rows in the claim order: `ORDER BY NextTryAtUtc, Sequence LIMIT @max` (D144).
4. A second statement leases these rows with `UPDATE ... WHERE Sequence IN (SELECT value FROM json_each(@sequences)) RETURNING ...`. SQLite gives the rows of `RETURNING` in an arbitrary order, so the store returns them in the order of the first statement.
5. When the first statement reads fewer than `maxCount` rows, a third statement reads `MIN(NextTryAtUtc)` of the pending rows as `NextDueAtUtc`. Else `NextDueAtUtc` is now, because more rows can be due.

A claimed row that the code cannot read stays out of the result. A transient error, for example a newer version, keeps the row pending until the lease ends. A permanent error sets the status `Failed`, ends the lease, and writes `{ErrorName}: {message}` into `LastError`.

A state row is one `INSERT OR IGNORE`, and a changed row count of 0 means that another caller added the version first. `TryStartChannelCheckAsync` is one `UPDATE` of the latest version with the condition `NextCheckAtUtc <= @now`. A changed row count of 1 means that this caller runs the health check. The table `MsgHatchChannelDown` of a database of an earlier phase stays unused, with no data migration.

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
- The system file holds the messages with no tenant, the state rows of the channels, and the registry settings of `SqliteRegistrySettingsStore`.
- Each file has the full schema of section 3.1, and the store creates it at the first operation on the file. So the first claim makes the file of each tenant of the tenant source.
- `UseSqliteTenantSubscriptionStore` of `MsgHatch.Subscriptions.Data.Sqlite` keeps the subscriptions of each key in the file of that key, with the same options.
- A setup with `UseSqliteStores` and `UseSqliteTenantStores` fails the startup validation, because two methods select the stores (D62). A value in code in both `UseSqliteTenantStores` and `UseSqliteTenantSubscriptionStore` fails it too (D150).

## 4. EF Core stores

`MsgHatch.Data.EFCore` keeps the inbox, the outbox, the state rows of the channels, and the registry settings in a database through EF Core, on each relational EF Core provider (D120, D318). The package has no SQL text and no provider name. `UseEntityFrameworkStores(o => o.UseSqlite(connectionString))` selects the stores on a provider with no MsgHatch package. The SQL Server package `MsgHatch.Data.EFCore.SqlServer` selects them with `UseEntityFrameworkSqlServerStores(connectionString)`, the PostgreSQL package `MsgHatch.Data.EFCore.PostgreSql` with `UseEntityFrameworkPostgreSqlStores(connectionString)`, and the MySQL package `MsgHatch.Data.EFCore.MySql` with `UseEntityFrameworkMySqlStores(connectionString)`. `MsgHatch.Subscriptions.Data.EFCore` adds the subscription store with `UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(connectionString))`, `MsgHatch.Subscriptions.Data.EFCore.SqlServer` with `UseEntityFrameworkSqlServerSubscriptionStore(connectionString)`, `MsgHatch.Subscriptions.Data.EFCore.PostgreSql` with `UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString)`, and `MsgHatch.Subscriptions.Data.EFCore.MySql` with `UseEntityFrameworkMySqlSubscriptionStore(connectionString)`.

The base package has feature folders, and the path of each folder is its namespace (D50):

| Folder | Types |
|---|---|
| The root | The types that a host uses: the three stores, `EntityFrameworkStoreOptions` with its validator, and `MsgHatchDbContext` |
| `DependencyInjection` | The setup methods and the migration step |
| `Messages` | The rows of the inbox, the outbox, and the channel states, with the queries, the claims, the documents, and the publish tries |
| `Registry` | The rows of the registry settings |
| `Model` | The column rules, the options of a context, and the error boundary of the operations |
| `Providers` | The API of a provider package: `IEntityFrameworkStoreDialect`, `EntityFrameworkStoreDialectExtensions`, and `ClaimStatementNames` |
| `Migrations` | The base classes of the migrations of a provider package, and the migrations assembly of the contexts |

### 4.1 Model and tables

`MsgHatchDbContext` maps the tables of section 3.1 with the same column names, in the schema of `EntityFrameworkStoreOptions.Schema`. The options bind from the section `MsgHatch:EntityFramework`, and the default schema is `msghatch`. The schema keeps the tables apart from the tables of the host, so the table names have no prefix `MsgHatch`: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, and `MessageTypeEntry` (D321). A name such as `msghatch.MsgHatchOutbox` repeats the schema. The names keep PascalCase on each provider. The SQLite and MongoDB stores keep the prefix, because their databases have no schemas. In MySQL a schema is a database, so the MySQL dialect gives `HasSchemas` false, and the tables keep the prefix in the database of the connection: `MsgHatchInbox`, `MsgHatchOutbox`, `MsgHatchChannelState`, `MsgHatchChannelEntry`, and `MsgHatchMessageTypeEntry` (D326). The option `Schema` then has no effect, so the startup validation fails when it is not the default `msghatch`. There is no table `MsgHatchSchema`: the history table of the migrations of each context keeps the version of its schema (section 4.9). SQLite has no schemas, so EF Core ignores the schema there.

| Value | Column | Rule |
|---|---|---|
| `Sequence` | `bigint` identity, the primary key of a message table (`INTEGER PRIMARY KEY AUTOINCREMENT` on SQLite) | It keeps the add order (D144). |
| A tenant, company, user, or target id | `nvarchar(256)` (`character varying(256)` on PostgreSQL, `varchar(256)` in `utf8mb4` on MySQL) | An empty text means no tenant. An id must be case-sensitive, so two tenants that differ only in case stay apart (D178). |
| A message id | `char(36)` in the format `D` (`character(36)` on PostgreSQL, `varchar(36)` on MySQL, because MySqlConnector reads a `char(36)` column as a `Guid`. A host on MySQL with no MySQL package sets `GuidFormat=None` in its connection string) | The text sorts as `Guid.CompareTo`, so the pages of a query keep the order of D112. EF Core gives each parameter the type of its column, so a parameter of a message id is a fixed ANSI text on SQL Server too. With an `nvarchar` parameter, SQL Server converts the column, and parallel adds of one id got the deadlock 1205 (checked fact after phase 18). |
| A message type, channel, content type, or trace type name | `varchar(256)` | A longer name gives `Errors.ValidationFailed` with the dialect of SQL Server or PostgreSQL, and `Errors.Unavailable` with no dialect (D178, D322). |
| An enum value | `varchar(32)` | The name of the value, for example `Pending`. |
| A time | A UTC `DateTime` in the time type of the provider: `datetime2(7)` on SQL Server, text on SQLite, `timestamptz` on PostgreSQL, `datetime(6)` on MySQL | SQL Server and SQLite keep 100 ns. PostgreSQL keeps microseconds, and Npgsql cuts a time down to the microsecond with an integer division, so a time never comes back later than it was. MySQL keeps microseconds too, and the provider cuts a time down to the microsecond before the server can round it up (checked fact of phase 52). The store gives each time the kind UTC on a write and on a read, because a provider can refuse a time with no kind (D319). |
| The claim token and the due time before the claim | `ClaimToken` as `char(36)` (`varchar(36)` on MySQL), and `ClaimedDueAtUtc` | The values of the last claim of a message (section 4.4). |
| JSON | The option `JsonColumnType`, or else the JSON type of the dialect, or else the text type of the provider: `json` on PostgreSQL, `nvarchar(max)` on SQL Server, `TEXT` on SQLite, `longtext` on MySQL | `Content`, `Trace`, `Tries`, `DisabledChannels`, `Settings`, and `NamedUsers`. The property stays a `string`, and the store keeps its JSON work in C# (D176, D323). |

The collation of each text column comes from the option `TextCollation`, or else from the dialect of a provider package, or else from the database. The SQL Server package gives `Latin1_General_100_BIN2`, the PostgreSQL package gives `C`, and the MySQL package gives `utf8mb4_bin`, so an id or a name is case-sensitive and sorts in ordinal order. The MySQL package sets the character set `utf8mb4` on each table and not on the database, so a migration changes no setting of the database of the host. The default collation of SQLite, `BINARY`, is case-sensitive. A deterministic collation of PostgreSQL is case-sensitive too, but it can sort in another order than the ordinal order. A database with a case-insensitive default, for example SQL Server with `SQL_Latin1_General_CP1_CI_AS`, needs the option or a dialect. A JSON column has no collation, because no query compares or sorts a document, and PostgreSQL refuses a collation on `json` with the error 42804 (checked fact of phase 50).

The JSON type of the column is a choice between the check of each write and the speed (D323):

- PostgreSQL: the dialect gives `json`, which checks each write and keeps the text unchanged. `jsonb` changes the order of the keys and drops duplicate keys, so an event type of a host that needs `$type` as the first key can fail to read. A host selects `jsonb` only with the option.
- SQL Server: the dialect gives no JSON type, so the columns stay `nvarchar(max)`. The type `json` needs SQL Server 2025 or Azure SQL, and a host on such a server selects it with the option.
- MySQL: the dialect gives no JSON type, so the columns are `longtext`. The type `json` sorts the keys and drops duplicate keys, as `jsonb` does, so a host selects it only with the option.
- The stores write a document one time and read it whole, so a text type is faster: `json` parses each write, and `jsonb` also converts each read.

The indexes have the names of the model with no prefix. The due indexes have no filter, so each provider can use them: `IX_Inbox_Due` on `(Status, NextTryAtUtc, Sequence)` and `IX_Outbox_Due` on `(Channel, Status, NextTryAtUtc, Sequence)` (D104). `Sequence` is the last column, so the index gives the claim order. The other indexes are the indexes of section 3.1: the unique `(TenantId, Id)` of the inbox and `Id` of the outbox, `Flags`, `(TenantId, CreatedAtUtc, Id)`, `(TenantId, TargetKind, TargetId, CreatedAtUtc)`, and `SourceMessageId`.

### 4.2 Message format

- `AddAsync` writes the whole message with `MsgHatchJson`. The store takes the event or the envelope, the route trace, and its `$type` out of that document in C#, and writes them into their columns (D143, D318).
- To read a message, the store writes the document from the columns of the row with `Utf8JsonWriter`, and `MsgHatchJson` reads it (D176).
- A JSON column that is not valid JSON goes into the document as a JSON string. So an edited row gives the error of that row only, and an edited column cannot add a property to the document, for example a second `tenant`.

### 4.3 Publish tries

`RecordTryAsync` reads the stored `TryCount` and `Tries` of the message, and then writes with one `ExecuteUpdate`:

- With a stored `TryCount` that is not the try number minus 1, the try is already recorded, and the store changes nothing (D27). An id that is not stored gives `Errors.NotFound`.
- Else the store appends the try to the JSON array in C#, and writes the array, the status, `TryCount`, `LastTryAtUtc`, `LastError`, the end of the lease, and the next due time, on the condition of the `TryCount` that it read.
- When another writer changed the row first, the write changes no row, and the store reads the row again.
- Stored tries that a person changed into invalid JSON give `Errors.SerializationFailed`.

The cancel of an outgoing message does the same for each id: it reads the cancellable row of the tenant, appends the try of `PublishTry.Cancel` with the next try number, and writes on the condition of the `TryCount` that it read (D110).

### 4.4 Claim pattern

A claim takes the due messages in the order of `NextTryAtUtc`, and then in the order of `Sequence` (D144). It has two forms.

The claim of the base package, with no claim statement, which the MySQL package also uses, because MySQL 8.4 has no `UPDATE ... RETURNING`:

1. A LINQ query reads the `Sequence` of the first due candidates: pending, due at `nowUtc`, in the claim order, at most the count that the claim still needs.
2. One `ExecuteUpdate` claims the candidates that are still pending and due. It sets `ClaimedDueAtUtc` to the due time before the claim, then a new `ClaimToken`, and `NextTryAtUtc` and `LeaseUntilUtc` to the end of the lease (D103). The old due time comes first, because some databases, for example MySQL, apply the assignments from left to right.
3. When another dispatcher took a candidate first, the update changes fewer rows, and the store reads the next candidates, until it has the maximum count or no due message is left. So two parallel claims of 5 get 10 different messages.
4. The store reads the claimed rows by the `Sequence` of the candidates and the token, so the token needs no index.

The claim of a dialect is one statement with the quoted names of the model (section 4.8). On SQL Server:

```sql
UPDATE [msghatch].[Outbox] SET [ClaimedDueAtUtc] = [NextTryAtUtc], [ClaimToken] = @claimToken,
    [NextTryAtUtc] = @leaseUntilUtc, [LeaseUntilUtc] = @leaseUntilUtc
OUTPUT inserted.*
WHERE [Sequence] IN (
    SELECT TOP (@maxCount) due.[Sequence] FROM [msghatch].[Outbox] AS due WITH (ROWLOCK, READPAST, UPDLOCK)
    WHERE due.[Status] = @pending AND due.[Channel] = @channel AND due.[NextTryAtUtc] <= @nowUtc
    ORDER BY due.[NextTryAtUtc], due.[Sequence])
```

1. The subquery reads the due index in the claim order, and stops after `maxCount` rows. `UPDLOCK` locks only these rows, and `READPAST` lets a parallel claim skip them. So two parallel claims get different messages.
2. An `UPDATE` of a CTE with the `TOP` puts a sort under the `TOP` as the Halloween protection, because the statement changes the key of the index that it reads. The sort reads and locks each due row, so a parallel claim got only 0 to 3 of 5 rows (checked fact of phase 18).
3. The status is a parameter. SQL Server cannot use a filtered index for a parameter, so a filtered due index made the subquery read each due row, and the parallel claim test failed some runs (checked fact of phase 48). The due index has no filter, and `Status` is a column of it.

On PostgreSQL:

```sql
UPDATE "msghatch"."Outbox" AS claimed SET "ClaimedDueAtUtc" = due."NextTryAtUtc", "ClaimToken" = @claimToken,
    "NextTryAtUtc" = @leaseUntilUtc, "LeaseUntilUtc" = @leaseUntilUtc
FROM (
    SELECT "Sequence", "NextTryAtUtc" FROM "msghatch"."Outbox"
    WHERE "Status" = @pending AND "Channel" = @channel AND "NextTryAtUtc" <= @nowUtc
    ORDER BY "NextTryAtUtc", "Sequence"
    LIMIT @maxCount
    FOR UPDATE SKIP LOCKED) AS due
WHERE claimed."Sequence" = due."Sequence"
RETURNING claimed.*
```

1. The subquery reads the due rows in the claim order, stops after `maxCount` rows, and locks them. `SKIP LOCKED` lets a parallel claim skip the locked rows and take the next due rows.
2. The subquery gives the due time before the claim to `ClaimedDueAtUtc`, and `RETURNING` gives each column of each claimed row after the claim. `FromSqlRaw` runs the statement with no composition.
3. When a parallel writer changed a locked candidate first, PostgreSQL checks the condition again on the new row, and the claim takes fewer rows.

After each form:

- The store sorts the claimed rows by `ClaimedDueAtUtc` and `Sequence`. The end of the lease is the next due time of a claimed message (D103).
- A claimed row that the code cannot read stays out of the result. A transient error keeps the row leased for a newer instance. A permanent error sets the status `Failed`, ends the lease, and writes `{ErrorName}: {message}` into `LastError`.
- When the claim takes fewer than `maxCount` rows, a query reads `MIN(NextTryAtUtc)` of the pending rows as `NextDueAtUtc`. Else `NextDueAtUtc` is now.

The outbox claim first reads the latest state row of its channel. With a down row, it claims nothing, and returns the down state as `ClaimResult.Down` (D106). A state row is an EF Core insert on the key `(Channel, Version)`. When another caller added the version first, the insert fails on the key, the store reads that the version is stored, and gives false, so `ChannelState.ChangeAsync` reads the latest row again (D277). `TryStartChannelCheckAsync` reads the latest version, and then runs one `ExecuteUpdate` of that version with the condition `NextCheckAtUtc <= @now`, as the MongoDB store does. One statement with a subquery of the latest version does not run on MySQL, which refuses a subquery on the table of an `UPDATE` with the error 1093 (checked fact of phase 52).

### 4.5 Two parts and the tenant field

- `EntityFrameworkInboxStore` and `EntityFrameworkOutboxStore` are the dispatch stores. `For(key)` gives the tenant store of a key.
- `AddAsync` of the inbox is an EF Core insert on the unique key `(TenantId, Id)`. When the insert fails and the key is stored, for example from a parallel caller, the store treats the add as done. Each other failed insert gives `Errors.Unavailable`.
- `AddAsync` of the outbox adds a batch in one transaction, inside the execution strategy of the provider, one row after the other, so `Sequence` keeps the order of the batch. The batch first drops a second message with the same id. When a row has a stored id, the store rolls back, reads the stored ids, and adds the other rows in a new transaction, because some databases, for example PostgreSQL, end a transaction after an error.
- The other operations are LINQ: a query page with `OrderByDescending` and `Take`, a count with `GroupBy`, a retry and the cancel of a received message with `ExecuteUpdateAsync`, and a purge with `ExecuteDeleteAsync`. An `ExecuteUpdate` sets a column only after each assignment that reads it, because MySQL applies the assignments from left to right: a retry first sets the new try limit from the old status, and then the status (checked fact of phase 52). A query reads only the columns of a summary.

### 4.6 Subscription store

`MsgHatchSubscriptionDbContext` maps the table `Subscription` with the columns of section 3.6, in the same schema and with the same column rules and collation. It is a context of its own, because the core packages never reference `MsgHatch.Subscriptions`.

- The unique index `UX_Subscription_Owner` on the column `OwnerKey` stops a duplicate (D327). `OwnerKey` is a binary column of 32 bytes: the SHA-256 of `TenantId`, `MessageType`, `Level`, `CompanyId`, `UserId`, `Channel`, and `Recipients`, each as its UTF-8 byte count in 4 bytes little-endian and then its UTF-8 bytes. So two keys that differ only at the border of two values, for example `ab` and `c` against `a` and `bc`, give two hashes. The store calculates the hash in C# before each insert and each update, and writes an empty text for a missing tenant, company, user, or channel.
- The 7 key columns have 1,344 characters, and no MySQL index key can hold them in `utf8mb4`. On SQL Server, the declared key of the old index had 2,112 bytes, but a real key has at most 1,553 bytes, below the limit of 1,700 bytes, because an owner has a company id or a user id, never both (checked fact of phase 52). The hash has the same size on each provider.
- The index `IX_Subscription_Type` on `(TenantId, MessageType)` is not unique, and serves `FindForDeliveryAsync`.
- The key compares bytes, which is the rule of the binary collations of the dialects. A host that sets a `TextCollation` that ignores case gets one owner for each spelling of an id.
- `AddAsync` inserts the row with EF Core. A failed insert gives `Errors.Conflict` when another row has the id or the same owner, message type, channel, and recipient kind.
- `UpdateAsync` is one `ExecuteUpdateAsync` with the id and the expected revision. A changed row count of 0 gives `Errors.Conflict` for a stored id, and `Errors.NotFound` else. A change into the key of another row gives `Errors.Conflict`.
- `UseEntityFrameworkSubscriptionStore` shares `EntityFrameworkStoreOptions` with `UseEntityFrameworkStores` (D59), and registers its own pooled context factory.

### 4.7 Registry settings store

`EntityFrameworkRegistrySettingsStore` keeps the registry settings in `ChannelEntry` and `MessageTypeEntry` (section 4.20 of the design). `UseEntityFrameworkStores` registers it.

- A new entry, with the revision 1, is an EF Core insert. A failed insert of a stored name gives `Errors.Conflict`.
- A change is one `ExecuteUpdateAsync` with the condition `Revision = @revision - 1`. A changed row count of 0 gives `Errors.Conflict`.
- `GetEntriesAsync` sorts each list with the ordinal comparer, so each provider gives the same order.

### 4.8 Dialect contract

A provider package implements `IEntityFrameworkStoreDialect` of the namespace `MsgHatch.Data.EFCore.Providers`, and gives it to the contexts with `UseEntityFrameworkStoreDialect` in the provider setup of its setup method (D318):

- `ProviderName`: the name of the EF Core provider that it serves, for example `Microsoft.EntityFrameworkCore.SqlServer`, `Npgsql.EntityFrameworkCore.PostgreSQL`, or `Microting.EntityFrameworkCore.MySql`. The startup validation fails when the provider of a context has another name.
- `TextCollation`: the default collation of the text columns, or null. The option `TextCollation` wins over it.
- `ClaimStatement(ClaimStatementNames names)`: the claim statement of one message table, or null for the claim of the base package. `ClaimStatementNames` has the table with its schema, the columns, and the placeholders of the parameters, each quoted with the names of the EF Core model and `ISqlGenerationHelper`. The base package gives each parameter the type of its column, and runs the statement with `FromSqlRaw`. The statement sets the same columns as the claim of the base package, and returns each column of each claimed row with its values after the claim.
- `JsonColumnType`: the type of each JSON column, or null for the text type of the provider. The option `JsonColumnType` wins over it (D323). It is a default property that gives null, so the dialect of a host still compiles.
- `ErrorTypeOf(DbException exception)`: the error type of a database exception of the provider, or null for the built-in mapping (D322). It is a default method that gives null. The store gives it the `DbException` also when EF Core wraps it, for example in `DbUpdateException`.
- `HasSchemas`: false for a database with no schemas inside it, for example MySQL. Then each table and each history table of both contexts gets the prefix `MsgHatch` and no schema, and the startup validation fails when the option `Schema` is not the default `msghatch` (D326). It is a default property that gives true, and a part of the model cache key.

The contract does not expose the row types of the stores. The dialect object travels in the options of each context, so the stores and the subscription store of one host can use two providers.

### 4.9 Schema migrations

Only `MigrateMsgHatchAsync` of the base package changes the schema (D320). It applies the migrations of each registered MsgHatch context, `MsgHatchDbContext` and `MsgHatchSubscriptionDbContext`:

- Each context has a history table of its own in the schema of the options, `__StoreMigrations` and `__SubscriptionMigrations`, so the history of the host does not change. With no schemas, the history tables are `__MsgHatchStoreMigrations` and `__MsgHatchSubscriptionMigrations` in the database of the connection (D326). A history table that the host set in the provider setup stays.
- EF Core 10 locks the database during `MigrateAsync`, so two hosts that start at the same time apply each migration one time. A second call changes nothing. On MySQL, the provider takes the lock with `GET_LOCK('__{database}_EFMigrationsLock')`, and MySQL refuses a lock name with more than 64 characters. So the name of the database has at most 45 characters (checked fact of phase 52).
- An optional `Action<DbContextOptionsBuilder>` runs on top of the setup of the stores, for example `o => o.UseSqlServer(migrationConnectionString)`. The migration then uses that connection, and the stores keep theirs. On MySQL, the provider setup also needs the server version, for example `o => o.UseMySql(migrationConnectionString, ServerVersion.Parse("8.4.0-mysql"))`. MsgHatch never logs a connection string.
- `ScriptMsgHatchMigrationsAsync` gives an idempotent script of the migrations of each context. EF Core writes no idempotent script for SQLite. The script of PostgreSQL has no batch separator, and one command runs it. The script of MySQL runs each migration in a procedure between `DELIMITER` lines, so it needs the `mysql` client or another client that knows `DELIMITER`, and the right to create a procedure.
- With `RequireCurrentSchema`, the startup validation reads the history tables, and fails for the first migration that is not applied, with the names of the context and the migration.

A provider package ships the migrations of its context, which the EF Core tools generate. A migration takes the schema and the collation of the options at run time:

- The migrations derive from `EntityFrameworkStoreMigration`, and the snapshot from `EntityFrameworkStoreModelSnapshot`, of the namespace `MsgHatch.Data.EFCore.Migrations`. Their code has `Schema`, `TextCollation`, and `JsonColumnType` in place of the schema, the collation, and the JSON type of the design time, and their classes are internal.
- The base package replaces `IMigrationsAssembly` of each context. It finds the migrations and the snapshot of the context in the migrations assembly, and gives them the schema, the collation, and the JSON type of the options. With no JSON type in the options or the dialect, the migrations get the type of a `string` with no length of the provider. So a context with another schema, collation, or JSON type has no pending model change, and `MigrateAsync` of EF Core 10 does not refuse it.
- The generator `StoreMigrationsCodeGenerator` of `tests/MsgHatch.Data.EFCore.Tests.Integration` writes this code. The test project of each provider package is the startup project of the EF Core tools, with an `IDesignTimeDbContextFactory` and a `DesignTimeServicesReference` to the generator. A new migration of the SQL Server package:

```powershell
dotnet ef migrations add Name --context MsgHatchDbContext --project src/MsgHatch.Data.EFCore.SqlServer --startup-project tests/MsgHatch.Data.EFCore.SqlServer.Tests.Integration --output-dir Migrations
dotnet ef migrations add Name --context MsgHatchSubscriptionDbContext --project src/MsgHatch.Subscriptions.Data.EFCore.SqlServer --startup-project tests/MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration --output-dir Migrations
```

A new migration of the PostgreSQL packages:

```powershell
dotnet ef migrations add Name --context MsgHatchDbContext --project src/MsgHatch.Data.EFCore.PostgreSql --startup-project tests/MsgHatch.Data.EFCore.PostgreSql.Tests.Integration --output-dir Migrations
dotnet ef migrations add Name --context MsgHatchSubscriptionDbContext --project src/MsgHatch.Subscriptions.Data.EFCore.PostgreSql --startup-project tests/MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration --output-dir Migrations
```

A new migration of the MySQL packages. The design-time contexts give the server version, so the tools open no connection:

```powershell
dotnet ef migrations add Name --context MsgHatchDbContext --project src/MsgHatch.Data.EFCore.MySql --startup-project tests/MsgHatch.Data.EFCore.MySql.Tests.Integration --output-dir Migrations
dotnet ef migrations add Name --context MsgHatchSubscriptionDbContext --project src/MsgHatch.Subscriptions.Data.EFCore.MySql --startup-project tests/MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration --output-dir Migrations
```

The generator replaces each text literal of a dialect collation, `"Latin1_General_100_BIN2"`, `"C"`, and `"utf8mb4_bin"`, with `TextCollation`. A generated migration has no other literal with one of these texts. The design-time contexts set the option `JsonColumnType` to `json`, and the generator replaces the literal `"json"` with `JsonColumnType`. No other column has this type, so the literal marks only the JSON columns.

- A file `.editorconfig` with `generated_code = true` in each folder `Migrations` stops the analyzers on the generated code.
- The test "no pending model change" of each package fails when the model changes with no new migration.
- The MySQL package replaces `IModelCustomizer` of its contexts. The customizer sets the character set `utf8mb4` on each table, because by default the provider writes `ALTER DATABASE` with the character set in each migration. It also makes each fixed-length text column `varchar`, because the provider maps the store type `char(36)` to `Guid`, and the snapshot model of the migrations then fails (checked facts of phase 52).

For a provider with no MsgHatch package, the host generates the migrations of the MsgHatch contexts in its own assembly, for example with `o => o.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("MyHost"))`, and `MigrateMsgHatchAsync` applies them unchanged. `tests/MsgHatch.Data.EFCore.Tests.Unit` keeps such migrations on SQLite in its folder `SqliteMigrations`.

### 4.10 Connections, versioning, and limits

- Each operation makes its own context through the pooled `IDbContextFactory` of its context, so the stores are thread-safe singletons (D130).
- A provider that is not relational fails the startup validation of `UseEntityFrameworkStores` and of `UseEntityFrameworkSubscriptionStore` with an `OptionsValidationException` that names the setup method and the provider, and each store call gives `Errors.NotFound` (D175, D182).
- A store classifies an exception in the order of D141 and D322:
  1. A cancelled token gives `Errors.OperationCancelled`.
  2. The classifiers of the host, `IExceptionClassifier<TContext>` with the context of the store as the client, in the order of their registration. `IExceptionClassifier<MsgHatchDbContext>` serves the inbox, the outbox, and the registry settings, and `IExceptionClassifier<MsgHatchSubscriptionDbContext>` serves the subscription store. A host adds one with `AddExceptionClassifier<MsgHatchDbContext, TClassifier>()`.
  3. `ErrorTypeOf` of the dialect. The SQL Server dialect gives `Errors.Rejected` for the numbers 229, 230, 262, and 18456, a missing permission or a failed login, and `Errors.ValidationFailed` for 207, 208, 2628, and 8152, a missing column or table and a value that is too long. The PostgreSQL dialect gives `Errors.Rejected` for the states `42501`, `28000`, and `28P01`, and `Errors.ValidationFailed` for `42P01`, `42703`, and `22001`. The MySQL dialect gives `Errors.Rejected` for the numbers 1044, 1045, 1142, and 1143, and `Errors.ValidationFailed` for 1054, 1146, and 1406. So these errors are not transient.
  4. The built-in mapping: each other database failure gives `Errors.Unavailable`, and a stored value that a person changed into an invalid value gives `Errors.SerializationFailed`.
- The base package references only `Microsoft.EntityFrameworkCore.Relational`, so the host adds the provider. The SQL Server package references `Microsoft.EntityFrameworkCore.SqlServer`. With version 10.0.15, it brings `Microsoft.Data.SqlClient` 6.1.7 and `System.Security.Cryptography.Pkcs` 10.0.13, which has no advisory (checked fact of phase 48). The PostgreSQL package references `Npgsql.EntityFrameworkCore.PostgreSQL`, 10.0.4 with Npgsql 10.0.4 in phase 49. The MySQL package references `Microting.EntityFrameworkCore.MySql`, 10.0.14 with `MySqlConnector` 2.6.2 in phase 52 (D325).
- The tests run the contract bases on SQLite (unit tests), on SQL Server with no dialect and the binary collation of the option, on SQL Server with the dialect on a database of the migrations, on PostgreSQL with no dialect and the collation of the database, on PostgreSQL with the dialect on a database of the migrations, on MySQL with no dialect and the binary collation of the option, and on MySQL with the dialect on a database of the migrations. The contract bases also run with the option `JsonColumnType`: `jsonb` on PostgreSQL, and `json` on SQL Server 2025 (D323). The integration tests run on SQL Server 2025 CU7, on PostgreSQL 18.7, and on MySQL 8.4.13: on the servers of `appsettings.Local.json`, for example the local containers dev-mssql, dev-postgres, and dev-mysql, or else in containers through Testcontainers (D180).
- PostgreSQL allows 100 connections by default, and Npgsql keeps a pool of up to 100 connections for each connection string. The test fixture gives each test database a pool of at most 10 connections. With the default pool, the parallel tests failed with the error 53300, "too many clients already" (checked fact of phase 49). A host with many connection strings on one server sets `Maximum Pool Size` in the same way. MySQL allows 151 connections by default, so the MySQL test fixture also gives each test database a pool of at most 10 connections.
- The name of a MySQL test database starts with the prefix of D180 with no `_data_efcore`, for example `msghatch_subscriptions_mysql__`, because the migration lock allows at most 45 characters in the name of a database (section 4.9).
- A reset of a MySQL test database empties each table that has a row with `TRUNCATE TABLE`. After a `DELETE`, InnoDB keeps a deleted key as a delete-marked record, and two parallel inserts of that key took shared gap locks and deadlocked with the error 1213 (checked fact of phase 52). A store never deletes a channel state row. In a host, the deadlock can come only from two parallel inserts of a key that a delete removed, and it gives the transient `Errors.Unavailable`.
- The schema and the collation are a part of the options of each context. So two hosts with two schemas in one process get two models.

## 5. MongoDB stores

`MsgHatch.Data.MongoDb` keeps the inbox, the outbox, the state rows of the channels, and the registry settings in one MongoDB database through the official driver `MongoDB.Driver` (D121). `UseMongoDbStores(o => o.ConnectionString = "mongodb://127.0.0.1:27017")` selects them. `MsgHatch.Subscriptions.Data.MongoDb` adds the subscription store with `UseMongoDbSubscriptionStore()`.

### 5.1 Options and collections

`MongoDbStoreOptions` binds from the section `MsgHatch:MongoDb`:

| Option | Default | Rule |
|---|---|---|
| `ConnectionString` | none | Required. The setup method makes the `IMongoClient` from it with `TryAdd`, so a client that the host registered stays (D60). |
| `DatabaseName` | `msghatch` | Required. Only ASCII letters, digits, `_`, and `-`, and at most 63 characters, because MongoDB 8.3.7 rejects a longer name (checked fact of phase 19). |

| Collection | Documents | Indexes |
|---|---|---|
| `MsgHatchInbox` | One document for each received message | `UX_MsgHatchInbox_Key`, unique on `(tenant, delivery.id)`. `IX_MsgHatchInbox_Due` on `(delivery.nextTryAtUtc.t, _id)` with the filter `delivery.status: "Pending"` (D104). `IX_MsgHatchInbox_Flags` on `flags`. `IX_MsgHatchInbox_Created` on `(tenant, delivery.createdAtUtc.t, delivery.id)`. |
| `MsgHatchOutbox` | One document for each outgoing message | `UX_MsgHatchOutbox_Id`, unique on `delivery.id`. `IX_MsgHatchOutbox_Due` on `(channel, delivery.nextTryAtUtc.t, _id)` with the filter `delivery.status: "Pending"`. `IX_MsgHatchOutbox_Flags`, `IX_MsgHatchOutbox_Created`, `IX_MsgHatchOutbox_Target` on `(tenant, target.kind, target.id, delivery.createdAtUtc.t)`, and `IX_MsgHatchOutbox_Source` on `sourceMessageId`. |
| `MsgHatchChannelState` | One document for each state row, with the ObjectId of `SinceUtc` as `_id`, and `channel`, `version`, `kind`, `sinceUtc`, `previousKind`, `previousDurationMs` (a 64-bit integer), `reason`, `nextCheckAtUtc`, `errorCode`, `errorName`, and `note` (D277) | `UX_MsgHatchChannelState_Version`, unique on `(channel, version)` |
| `MsgHatchChannelEntry`, `MsgHatchMessageTypeEntry` | One document for each registry entry, with the name as `_id` (section 4.20 of the design) | Only `_id` |
| `msghatch_signals` | One document for each notification of the dispatch signal of section 5.9, with `topic`, `sender`, and `createdAtUtc` as a BSON date (D304) | `IX_msghatch_signals_Created` on `createdAtUtc`, a TTL index of one minute |

- A store creates the indexes of its collection before its first operation, one time for each database in the process. The creation is idempotent, so two processes can do it at the same time. A failed creation is tried again at the next operation.
- The claim index has `_id` as its last key. On MongoDB 8.3.7, a claim with the index on `(channel, delivery.nextTryAtUtc.t)` only has a `SORT` stage that reads each due document, and with `_id` as the last key, the plan stops after the `LIMIT` with no sort (checked fact of phase 19).
- No collection keeps a schema version of the store. Each message document has `schemaVersion` and `contentVersion`, and each index has a name, so a later version can change an index by its name.

### 5.2 Message format

`AddAsync` writes the whole message with `MsgHatchJson`, and converts that JSON document into one BSON document with the same names (D184):

- An object becomes a BSON document, an array a BSON array, and a string, a boolean, or null its BSON value. So the event, the envelope, and the route trace with its `$type` stay sub-documents that a person can read and edit in the tools of MongoDB.
- A number becomes a 32-bit or a 64-bit integer when it is one, and else a `Decimal128` with the digits of its text, so `19.90` comes back as `19.90`. A number with more than 34 significant digits gives `Errors.SerializationFailed`, because `Decimal128.TryParse` does not round it (checked fact of phase 19).
- Each time of the delivery state, of a publish try, of a channel state, of a registry entry, and of a subscription is the sub-document `{ d, t }` (D185). `d` is the BSON date cut to the millisecond, for the tools, the date operators, and a TTL index. `t` is the number of UTC ticks of 100 nanoseconds, the exact time. Each filter, sort, and index uses `t`, and a read takes `t` only. A person who edits a time changes `t`.
- `_id` is an ObjectId with the creation time of the message, in seconds, and the counter of the driver (D187). A store never reads the clock (section 1.6), so the time of the ObjectId comes from `CreatedAtUtc`. The counter increases in each process, so the ObjectIds of the adds of one process in a row increase.
- The stores register no BSON class map and no serializer, so they do not change the BSON setup of the host.

An outgoing message with one failed try, in the shell syntax of MongoDB:

```js
{
  _id: ObjectId("..."),
  schemaVersion: 1,
  delivery: {
    id: "7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31", status: "Pending", tryCount: 1, maxTryCount: 8,
    createdAtUtc: { d: ISODate("2026-10-31T08:00:00Z"), t: NumberLong("639290304000000000") },
    lastTryAtUtc: { d: ISODate("2026-10-31T08:00:01Z"), t: NumberLong("639290304010000000") },
    nextTryAtUtc: { d: ISODate("2026-10-31T08:00:06Z"), t: NumberLong("639290304060000000") },
    leaseUntilUtc: null, expiresAtUtc: null, lastError: "RateLimited: Maximum sending rate exceeded."
  },
  sourceMessageId: "0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9", tenant: null, type: "OrderShipped", channel: "Email",
  target: { kind: "User", id: "1" }, contentType: "Email:OrderShippedEmail", contentVersion: 1, flags: "None",
  traceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  trace: { $type: "Trace:Subscription@1", target: "ScopeUser", ... },
  envelope: { to: "client1@example.com", subject: "Order 1001 is shipped", ... },
  tries: [
    {
      tryNumber: 1,
      startedAtUtc: { d: ISODate("2026-10-31T08:00:00Z"), t: NumberLong("639290304000000000") },
      finishedAtUtc: { d: ISODate("2026-10-31T08:00:01Z"), t: NumberLong("639290304010000000") },
      outcome: "TransientError", providerMessageId: null, errorCode: 429, errorName: "RateLimited",
      errorMessage: "Maximum sending rate exceeded."
    }
  ]
}
```

To read a message, the store converts the BSON document back into the JSON document of `MsgHatchJson`, with no `_id` and no `tries`. A sub-document with only `d` as a date and `t` as a 64-bit integer becomes the ISO 8601 text of `t`. JSON has no BSON date, so a sub-document from JSON never has this shape. A value of a BSON type that JSON does not have, for example an ObjectId that a person wrote into an event, gives `Errors.SerializationFailed` for that document only.

### 5.3 Publish tries

`RecordTryAsync` is one `updateOne` (D27):

```js
db.MsgHatchOutbox.updateOne(
  { "delivery.id": id, "delivery.tryCount": tryNumber - 1 },
  { $push: { tries: publishTry }, $set: { "delivery.status": status, "delivery.tryCount": tryNumber, ... } })
```

- No matched document means that the try is already recorded, or that the message is not stored. Only then a count reads whether the id is stored, and an id that is not stored gives `Errors.NotFound`.
- The cancel of outgoing messages is one `updateMany` with an aggregation pipeline. `$concatArrays` appends the cancelled try with the try number `{ $add: ["$delivery.tryCount", 1] }`, so each document gets its next try number in the same update. Each value of the try is in `$literal`, so a text that starts with `$` stays a text.

### 5.4 Claim pattern

The claim is a loop of `findOneAndUpdate` calls, one for each message, until `maxCount` messages or no due message (D121):

```js
db.MsgHatchOutbox.findOneAndUpdate(
  { "delivery.status": "Pending", channel: channel, "delivery.nextTryAtUtc.t": { $lte: nowTicks } },
  { $set: { "delivery.nextTryAtUtc": leaseEnd, "delivery.leaseUntilUtc": leaseEnd } },
  { sort: { "delivery.nextTryAtUtc.t": 1, _id: 1 }, returnDocument: "after" })
```

1. The outbox claim first reads the latest state document of its channel. With a down document, it claims nothing, and returns its down state as `ClaimResult.Down` (D106).
2. Each call takes the due message with the earliest due time, and of these the one with the lowest `_id` (D144, D187). It sets the due time and the lease end to the end of the lease (D103). Each call is atomic, so two parallel claims get different messages.
3. A stop between two calls leaves only the messages that the store already claimed leased, so no message waits for more than one lease.
4. A claimed document that the code cannot read stays out of the result. A transient error, for example a newer version, keeps it leased for a newer instance. A permanent error sets the status `Failed`, ends the lease, and writes `{ErrorName}: {message}` into `lastError`.
5. When the claim takes fewer than `maxCount` documents, a query reads the earliest due time of the pending documents as `NextDueAtUtc`. Else `NextDueAtUtc` is now.

A state row is one `insertOne`, and a duplicate key of the unique index means that another caller added the version first. So no change needs a transaction, also on a server with no replica set (D277). `TryStartChannelCheckAsync` is one `updateOne` of the latest version with the condition `nextCheckAtUtc.t <= now`. The collection `MsgHatchChannelDown` of an earlier phase stays unused.

### 5.5 Two parts and the tenant field

- `MongoDbInboxStore` and `MongoDbOutboxStore` are the dispatch stores. `For(key)` gives the tenant store of a key.
- The tenant is the field `tenant`, with null for no tenant. A unique index treats null as one value, so the store needs no empty text for no tenant.
- `AddAsync` is one unordered `insertMany` with no transaction, which skips each document whose key is stored (D186). So a second add of a key changes nothing, also from a parallel caller, and the stores work on a server with no replica set. A server failure can store a part of a batch. The next add of the same messages adds the rest.
- A query is one `find` with the filters, the sort `delivery.createdAtUtc.t` descending and `delivery.id` descending (D112), and a projection with no content. A retry is one `updateMany` with a pipeline that gives a failed message new tries with `$cond` (D111).
- A purge reads the finished documents in the order of their finish time, `$ifNull` of `lastTryAtUtc.t` and `createdAtUtc.t`, up to `maxCount`, and deletes these documents with a filter that they are still finished (D128). A count is one aggregation with `$group` by the message type, the channel, the status, and the test flag, and `$min` of the due time of the pending documents (D164).

### 5.6 Subscription store

`MongoDbSubscriptionStore` of `MsgHatch.Subscriptions.Data.MongoDb` keeps one document of the collection `MsgHatchSubscription` for each subscription (D78, D149):

- The document has the subscription id as `_id`, and the fields of section 3.6 with camelCase names: `revision`, `tenant`, `level`, `company`, `user`, `type`, `kind`, `channel`, `settingsType`, `settingsVersion`, `settings`, `recipients`, `namedUsers`, `mandatory`, `createdAtUtc`, and `lastModifiedAtUtc`. A missing tenant, company, user, or channel is null.
- `settings` is the JSON of `ChannelSettingsJson` converted into a BSON sub-document (D184), `namedUsers` is an array of user ids or null, and each time is `{ d, t }` (D185).
- The unique index `UX_MsgHatchSubscription_Owner` on `(tenant, type, level, company, user, channel, recipients)` stops a duplicate, and also serves `FindForDeliveryAsync`. A unique index treats null as one value, so a second default subscription of one message type, channel, and recipient kind collides with the first.
- `AddAsync` is an `insertOne`. A duplicate key, of the id or of the owner index, gives `Errors.Conflict`.
- `UpdateAsync` is one `replaceOne` with the tenant, the id, and the expected revision. No matched document gives `Errors.Conflict` for a stored id, and `Errors.NotFound` else. A change into the key of another document gives `Errors.Conflict`.
- `UseMongoDbSubscriptionStore` shares `MongoDbStoreOptions` and the `IMongoClient` with `UseMongoDbStores` (D59). A value in code in both setup methods fails the startup validation, and the message names both methods (D150).

### 5.7 Registry settings store

`MongoDbRegistrySettingsStore` keeps the registry settings in `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry` (section 4.20 of the design). `UseMongoDbStores` registers it.

- A document is `{ _id: "Email", displayName, description, enabled, revision, lastModifiedAtUtc: { d, t } }`. A message type document also has `disabledChannels` as an array of channel names.
- A new entry, with the revision 1, is an `insertOne`, and a stored name gives `Errors.Conflict`. A change is a `replaceOne` with the condition `revision: revision - 1`, and no matched document gives `Errors.Conflict`.
- `GetEntriesAsync` sorts each list with the ordinal comparer. A field with another BSON type or an invalid name gives `Errors.SerializationFailed`.

### 5.8 Connections, errors, and limits

- The stores share one `IMongoClient`, which pools the connections, so they are thread-safe singletons (D130).
- A server failure gives `Errors.Unavailable`, also a server that the driver cannot select in its timeout. A field with another BSON type, a missing field, an invalid name, or invalid JSON gives `Errors.SerializationFailed`. A cancelled token gives `Errors.OperationCancelled`.
- The stores use no transaction and no change stream, so they work on a server with no replica set. Only the dispatch signal of section 5.9 needs a replica set. The integration tests run on MongoDB 8.3.7: on the server of `appsettings.Local.json`, for example the local container dev-mongo, a replica set with one member, or else in a container of the image `mongo:8.3` through Testcontainers, also as a replica set with one member (D183).

### 5.9 Dispatch signal

`UseMongoDbDispatchSignal()` selects `MongoDbDispatchSignal`, which wakes the dispatch loops of each host that uses the same database, also on another machine (D303). Section 4.9 of the design describes it.

- One change stream on the database of the stores sees each insert into `MsgHatchInbox`, `MsgHatchOutbox`, and `msghatch_signals`. A filter of the server gives only the inserts, and only the fields `ns.coll`, `fullDocument.channel`, and `fullDocument.topic`. The stores keep each tenant in the same database (section 5.5), so the watch sees the messages of each tenant.
- `NotifyAsync` inserts the topic into `msghatch_signals` in the background (D304). The TTL index deletes a document about one minute later, so the collection stays small.
- A change stream needs a replica set, for example a replica set with one member. The stores themselves work with no replica set.
- The signal has no options. It uses the `IMongoClient` and the `DatabaseName` of `UseMongoDbStores`, so the startup validation fails with no `UseMongoDbStores`.

## 6. A store for another storage

A consumer can write a store for another storage. Do these steps:

1. Implement the dispatch store and its method `For(TenantKey key)`, or two classes on one storage.
2. Keep the tenant, the flags, the versions, and the tries of each message in fields that a query can read.
3. Write and read each message with `MsgHatchJson`, and each subscription setting with `ChannelSettingsJson`.
4. Make `ClaimDueAsync`, `RecordTryAsync`, `RetryAsync`, and `CancelAsync` atomic in your storage. Keep the channel states as insert-only rows with a unique key on the channel and the version, and add each row with `ChannelState.Change` or `ChangeAsync` (section 1.10).
5. Register the store with `UseInboxStore`, `UseOutboxStore`, `UseInboxDispatchStore`, and `UseOutboxDispatchStore`, with `UseSubscriptionStore`, or with `UseRegistrySettingsStore`.
6. Add one subclass of each contract base class, for example `OutboxStoreTestsBase<TStore>`, `SubscriptionStoreTestsBase<TStore>`, and `RegistrySettingsStoreTestsBase<TStore>` (section 5.5 of the design).
7. Keep a schema version in your storage, and a golden-file sample for each released version of your own types.
8. For one storage for each tenant, call `UseStorageForEachTenant<TTenantSource>` with the four parts of the storage of each key, in place of the four methods of step 5 (section 1.11).

The in-memory stores of `MsgHatch.Domain` follow each rule and pass each contract base class, so they are the reference implementation (D119).
