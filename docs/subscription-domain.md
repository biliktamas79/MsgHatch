# MsgHatch subscription domain

This document describes the subscription domain of the package `MsgHatch.Subscriptions`, the router of MsgHatch with managed subscriptions (design D97): its model, its rules, its actions, and numbered examples. The examples are the source of the test rows. A test that uses an example names its number, for example `R7`, in the name of its data row.

`docs/design.md` is the source for the types (section 4.4), the contracts (section 4.6), the processing (section 4.8), and the subscription router with its routing procedure and its management API (section 4.17). This document uses the same terms (design section 2).

## 1. Model

### 1.1 Users, companies, and ids

- A user is a person who gets messages. A company is a group of users. A user belongs to one company or to no company.
- `UserId` and `CompanyId` hold strings, so that each consumer keeps its own id type, for example `int`, `long`, `Guid`, or a string (design D73).
- `From<T>` maps an id of the consumer's type to its text, with the invariant culture. `TryAs<T>` maps the text back. `Create` checks a string from outside, for example from an HTTP request, and returns `Errors.ValidationFailed` for an invalid string.
- A tenant is a group of companies and users that MsgHatch keeps apart from the other tenants. `TenantId` holds a string, as the other ids do. User ids and company ids are unique only inside one tenant, so the user `"1"` of one tenant is not the user `"1"` of another tenant (design D114). A setup with one tenant uses no tenant.
- MsgHatch keeps the tenants apart with tenant services: the subscription router, the subscription store, and the recipient directory exist one time for each tenant, with the tenant key as their DI key. Their methods have no tenant parameter (design D126).
- The consumer's `IRecipientDirectory` of one tenant knows the company, the UI language, and the channel addresses of each user, the default language and the channel addresses of each company, and the users of each company (design D76). A company address is the address of the company itself, for example the email address of the company administrator.

### 1.2 Subscriptions, levels, and recipients

- A subscription belongs to one owner, is for one message type, and has recipients.
- The subscription store of a tenant holds only the subscriptions of that tenant, and the store of the key `TenantKey.None` holds the subscriptions with no tenant. The three levels work inside one store (design D116, D126).
- The owner has one of three levels. The level tells who owns the subscription:
  - **Default.** The owner is the whole system, or the whole tenant.
  - **Company.** The owner is one company.
  - **User.** The owner is one user.
- The recipients tell who gets the messages. They are separate from the level (design D89):
  - **Scope users.** The users of the event that the owner covers: all users of the event for a default subscription, the users of the company for a company subscription, and the owner user for a user subscription.
  - **Company.** The company that the event concerns: its SignalR group, or its own address. Only the default and the company levels can have these recipients.
  - **Named users.** A list of 1 to 100 users. Only the default and the company levels can have these recipients. A company subscription names only users of that company.
- A subscription has one of two kinds:
  - **Channel.** It names one channel and has the typed settings of that channel, for example the language and the format of an email.
  - **Opt-out.** It has no settings. It means "not subscribed" at its level, for one channel or for all channels. An opt-out is always for the scope users.
- One owner can have several subscriptions for one message type: one for each channel and recipient kind.
- A subscription is overridable or fixed:
  - **Overridable.** A subscription for the scope users that is not mandatory. A lower level overrides it for each channel (design D72).
  - **Fixed.** A mandatory subscription, or a subscription for the company or for named users. No lower level overrides it (design D88, D89). Only a default or company subscription for the scope users can be mandatory.

### 1.3 Channels

Each channel describes itself with a descriptor (design D77):

| Channel | Name | Languages | Rich text | Raw data | Group delivery | Settings |
|---|---|---|---|---|---|---|
| `Email` | Email | yes | yes | no | no | `EmailChannelSettings(Language, Format)` |
| `SignalR` | UI notification | yes | no | yes | yes | `SignalRChannelSettings(Language)` |
| `Broker` | Broker message | no | no | yes | yes | `BrokerChannelSettings(Headers)` |

- **Languages.** The channel can send in the language of the settings, or else in the UI language of the user.
- **Rich text.** The channel can send formatted text. For email, `EmailFormat.Html` sends an HTML body and `EmailFormat.PlainText` sends only a text body.
- **Raw data.** The channel sends the payload itself, as data, not as rendered text.
- **Group delivery.** The channel can send one message to a whole company, for example to the SignalR group of the company.

### 1.4 Scope of an event

A domain event has one scope (design D74):
- **User scope.** One or more users. Each user gets the message.
- **Company scope.** One company. A channel with group delivery sends one message to the company. A channel without group delivery sends one message to each user of the company (design D75).

An event concerns a company when it has the company scope of that company, or a user scope with at least one user of that company. A fixed subscription of a company sends for each event that concerns that company (design D90).

## 2. Rules

### 2.1 Resolution

For one message type, one user of the scope, and each registered channel (design D72, D88):
1. A mandatory default subscription for the channel applies.
2. Otherwise, a mandatory subscription of the company of the user for the channel applies.
3. Otherwise, the most specific level with a subscription for the scope users for the channel applies: the user, then the company of the user, then the default.
4. An opt-out for the channel, or for all channels, counts as a subscription for the channel. It gives no message on that channel.
5. The levels merge for each channel. For example, a user subscription for `Email` does not remove a default subscription for `SignalR`.

For one message type and a company scope:
1. For each channel with group delivery, the same order for the company: a mandatory default subscription, a mandatory subscription of the company, a subscription of the company, and then a default subscription. The target is the company.
2. For each user of the company: the user resolution above, with only the channels without group delivery. So no user gets the message of a group channel two times.

Fixed subscriptions for the company and for named users (design D89, D90), for each company that the event concerns:
1. Each default subscription and each subscription of that company for the company gives one message to the company.
2. Each subscription of that company for named users gives one message to each named user who belongs to that company. A named user of another company is skipped, so that the data of one company does not reach another company.
3. Each default subscription for named users gives one message to each named user.
4. A fixed subscription gives one message for each event, also when several users of the scope belong to the company.

Merge (design D91). For one target and one channel, the resolution gives one message:
1. A fixed subscription applies before an overridable one.
2. Between two fixed subscriptions, the higher level applies.
3. On the same level, a subscription for named users or for the company applies before a mandatory one for the scope users.

Common rules:
- A subscription for a channel that the message type has not registered is skipped.
- A duplicate user id in a user scope gives one delivery.
- An unknown user gives no delivery and no error.
- An event of a tenant uses only the subscription store and the directory of that tenant. An event with no tenant uses only those of `TenantKey.None` (design D116, D126).

### 2.2 Language, address, and template scope

- **Language.** The language of the channel settings. If it is null: the UI language of the user, or the default language of the company for a company target. If that is also missing: `MsgHatchOptions.DefaultLanguage`.
- **Address.** For a user target, the address of the user for the channel, from the directory. For a company target on a channel without group delivery, the address of the company for the channel. When the profile has no address for the channel, the route of that target has no address. The email composer then fails the message to that target with `Errors.ValidationFailed`. The other targets of the event still get their messages.
- **Template scope.** The company of the target user, or the target company. A company can therefore have its own email templates.

### 2.3 Validation of a change

A create or an update returns `Errors.ValidationFailed`, and changes nothing, when:
1. The owner does not match its level.
2. The message type is not registered, or has no scope function, so the subscription router does not route it.
3. A subscription names a channel that the message type has not registered. Or a channel subscription has no channel or no settings.
4. The settings type is not the settings type of the channel.
5. The settings have a language, and the channel has no feature `Languages`.
6. The language is not null, and is not a known culture name.
7. The settings validator of the channel rejects the settings.
8. An opt-out has settings.
9. An update changes the owner, the message type, or the recipient kind.
10. A user subscription has recipients other than the scope users.
11. The named users are empty, have a duplicate, or have more than 100 users. Or the directory does not know a named user. Or a named user of a company subscription belongs to another company.
12. An opt-out has recipients other than the scope users, or is mandatory.
13. A mandatory subscription is a user subscription, or has recipients other than the scope users.

### 2.4 Conflicts

A create or an update returns `Errors.Conflict`, and changes nothing, when:
1. The owner already has a subscription for the same message type, channel, and recipient kind. An opt-out for one channel counts as a subscription for that channel.
2. The change adds an opt-out for all channels, and the owner already has a subscription for the scope users for the same message type.
3. The change adds a subscription for the scope users, and the owner already has an opt-out for all channels for the same message type.
4. An update gives a revision that is not the stored revision.
5. The change adds or changes a company or user subscription for the scope users, or an opt-out, for one channel, and a higher level has a mandatory subscription for the same message type and channel. An opt-out for all channels is allowed. It leaves the mandatory channel in place.

### 2.5 Resolution trace

Each delivery has a resolution trace that tells where each decision comes from (design D92):
- **Applied.** The subscription that gave the delivery: its id, its owner with the level, its recipient kind, and its mandatory flag. The channel and the channel settings come from it, whole.
- **Target.** `ScopeUser`, `CompanyGroup` (the SignalR group of a company scope), `Company` (a subscription for the company), or `NamedUser`.
- **Language.** `ChannelSettings` (the settings of the applied subscription), `UserUiLanguage`, `CompanyDefaultLanguage`, or `LibraryDefault`.
- **Address.** `None` (the profile has no address for the channel, for example for SignalR), `UserProfile`, or `CompanyProfile`.
- **Skipped.** Each other subscription for the same target and channel, from the default level down, with the rule that removed it:
  - `Overridden`: a more specific level has a subscription or an opt-out for the channel (rule 2.1).
  - `Locked`: a mandatory subscription of a higher level applies to the channel.
  - `Merged`: another subscription gave the same target and channel, and comes first in the merge order.

An opt-out that applies gives an opt-out decision instead of a delivery: the target, the channel, the opt-out, and its own skipped list. So the trace also tells why a target got no message.

## 3. Domain actions

| Action | Actor | Input | Output | Errors | Examples |
|---|---|---|---|---|---|
| A1 List channels | An administrator or a settings UI | none | The channel descriptors | none | M1 |
| A2 List message types | An administrator, a company administrator, or a user | Optional message type, company, user | Each message type with a scope function, with its channels and its applied subscriptions: for each channel, the subscription or the opt-out that applies, each with the level that it comes from | `NotFound` for an unknown user | M2 to M6, M19 |
| A3 List subscriptions | An administrator | Optional message type, company, user, level | The stored subscriptions that match all filters | none | M7 to M9 |
| A4 Read a subscription | An administrator | Subscription id | The subscription | `NotFound` | M18 |
| A5 Create a subscription | An administrator, a company administrator, or a user for the own user level | A draft: owner, message type, kind, channel, settings, recipients, mandatory flag | The new subscription, with revision 1 | `ValidationFailed`, `Conflict` | M10 to M15, M20 to M22 |
| A6 Update a subscription | The same actors as A5 | Subscription id, expected revision, draft | The changed subscription, with the next revision | `ValidationFailed`, `Conflict`, `NotFound` | M16, M17 |
| A7 Delete a subscription | The same actors as A5 | Subscription id | none | none. An unknown id succeeds. | M18 |
| A8 Resolve the deliveries of an event | The subscription router | Message type, scope | The effective subscriptions: target, channel, settings, language, address, template scope, and resolution trace. The opt-out decisions. | none. The email composer fails a delivery with no address. | R1 to R19, P1 to P7, T1 to T8 |
| A9 Verify the routing | A test job or an administrator | Test users, languages, optional message type | One report row for each message type, test case, test user, language, and channel | none. A failed check is a row, not an error. | X1 to X7 |
| A10 Delete the test data | A test job | none | The numbers of deleted inbox and outbox messages | none | Design section 4.18 |

`MsgHatch.Subscriptions` does not check who the actor is. The consumer protects the management API with its own authorization. The `/me` routes of the HTTP endpoints let a signed-in user change only the own user-level subscriptions.

## 4. Shared test scenario: Acme and Globex

Most examples use this scenario. A test seeds it one time as a named scenario for a group of tests that only read. A test that writes starts from the same data in its own storage. The scenario has no tenant. The examples R19 and S12 add the tenant `"t2"`.

Companies:

| Company | `CompanyId` | Default language | Email address |
|---|---|---|---|
| Acme | `"100"` | `hu` | `admin@acme.example` |
| Globex | `"200"` | `en` | none |

Users:

| User | `UserId` | Company | UI language | Email address |
|---|---|---|---|---|
| Client 1 | `"1"` | Acme | `en` | `client1@acme.example` |
| Client 2 | `"2"` | Acme | `hu` | `client2@acme.example` |
| Client 3 | `"3"` | Globex | `en` | `client3@globex.example` |
| Client 4 | `"4"` | none | `de` | `client4@example.com` |
| Client 5 | `"5"` | Acme | `hu` | none |

Registered message types and their channels:

| Message type | Channels |
|---|---|
| A | Email |
| B | Email |
| C | Email, SignalR |
| D | Email, SignalR, Broker |
| E | Email, Broker |

Subscriptions:

| Label | Level | Owner | Type | Kind | Channel | Settings | Recipients | Mandatory |
|---|---|---|---|---|---|---|---|---|
| #1 | Default | none | A | Channel | Email | no language, `Html` | scope users | no |
| #2 | Default | none | B | Channel | Email | no language, `Html` | scope users | no |
| #3 | Default | none | C | Channel | SignalR | no language | scope users | yes |
| #4 | Company | Acme | B | Opt-out | all channels | none | scope users | no |
| #5 | Company | Acme | E | Channel | Email | `en`, `Html` | scope users | no |
| #6 | User | Client 1 | B | Channel | Email | `en`, `PlainText` | scope users | no |
| #7 | User | Client 1 | E | Opt-out | all channels | none | scope users | no |
| #8 | Company | Globex | D | Channel | SignalR | no language | scope users | no |
| #9 | Company | Globex | D | Channel | Email | `en`, `Html` | scope users | no |
| #10 | Company | Acme | B | Channel | Email | no language, `Html` | company | no |
| #11 | Company | Acme | D | Channel | Email | no language, `Html` | named users: Client 2 | no |
| #12 | User | Client 2 | D | Channel | Email | `en`, `Html` | scope users | no |

The fixed subscriptions are #3 (mandatory), #10 (for the company), and #11 (for named users). Each subscription has revision 1. Its `CreatedAtUtc` and `LastModifiedAtUtc` are `2030-01-01T00:00:00Z`, the start time of `FakeTimeProvider`.

## 5. Examples

In the expected results, a delivery has the form `target / channel / language / level`. `u:1` is the user `"1"`, and `c:100` is the company `"100"`. The level is the level of the subscription that applied.

### 5.1 Identity

| No. | Given | When | Then |
|---|---|---|---|
| I1 | The `int` 42, the `int` -5, and the `long` 9000000000 | `UserId.From` | `"42"`, `"-5"`, and `"9000000000"` |
| I2 | The `Guid` `3F2504E0-4F89-11D3-9A0C-0305E82C3301` | `CompanyId.From` | `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"` |
| I3 | The `int` 1234567 and the current thread culture `hu-HU`, then `ar-SA` | `UserId.From` | `"1234567"` both times |
| I4 | The texts `"42"` and `"abc"`, and the text of I2 | `TryAs<int>`, then `TryAs<Guid>` | true with 42, false, and true with the `Guid` of I2 |
| I5 | null, `""`, a string of 257 characters, `"a\u0001b"`, a string of 256 characters, and `"auth0|abc"` | `UserId.Create` | `ValidationFailed` four times, then two valid ids |
| I6 | `new UserId("A1")` and `new UserId("a1")`, and `UserId.From(42)` and `new UserId("42")` | `Equals` | not equal, then equal |

### 5.2 Value rules

| No. | Given | When | Then |
|---|---|---|---|
| V1 | none | `SubscriptionOwner.Default`, `ForCompany(new CompanyId("100"))`, `ForUser(new UserId("1"))` | Levels `Default`, `Company`, and `User`. Only the company owner has a company, and only the user owner has a user. |
| V2 | The users `"1"`, `"1"`, and `"2"` | `NotificationScope.ForUsers` | The scope keeps the three users in their order, and has no company. The resolution removes the duplicate (R10). |
| V3 | The company `"100"` | `NotificationScope.ForCompany` | The scope has the company and no users. |
| V4 | The delivery targets `u:1` and `c:1`, the same message id, and the channel `SignalR` | `DeterministicId.Create` for each target | Two different ids. The targets are not equal. |
| V5 | `SubscriptionRecipients.ForUsers` with the users `"1"` and `"2"`, a second time with the same users in new lists, and a third time with `"2"` and `"1"` | `Equals` | The first two are equal. The third is not equal to them, because the order counts. |

### 5.3 Resolution

Each example uses the scenario of section 4.

| No. | Type | Scope | Expected deliveries | Rule |
|---|---|---|---|---|
| R1 | A | users 1 | `u:1 / Email / en / Default` | The default applies, with the UI language. |
| R2 | A | users 2 | `u:2 / Email / hu / Default` | The default applies, with the UI language. |
| R3 | B | users 1 | `u:1 / Email / en / User`, `c:100 / Email / hu / Company` | The user subscription #6 overrides the company opt-out #4 for Email. The fixed subscription #10 sends one email to the company address, in the default language of Acme. |
| R4 | B | users 2 | `c:100 / Email / hu / Company` | The company opt-out #4 stops the email to user 2. It does not stop the fixed subscription #10, because the opt-out is for the scope users only. |
| R5 | C | users 1, 2 | `u:1 / SignalR / en / Default`, `u:2 / SignalR / hu / Default` | The mandatory default #3 applies to each user. A user scope sends a group channel to each user. |
| R6 | D | users 1 | `u:2 / Email / hu / Company` | No level has a subscription of D for user 1. The fixed subscription #11 sends to its named user 2, because the event concerns Acme. User 2 is not in the scope. |
| R7 | E | users 1 | none | The user opt-out #7 overrides the company subscription #5. |
| R8 | E | users 2 | `u:2 / Email / en / Company` | The company subscription #5 applies, with its own language. |
| R9 | A | users 4 | `u:4 / Email / de / Default` | A user with no company gets the default. |
| R10 | A | users 1, 1, 2 | `u:1 / Email / en / Default`, `u:2 / Email / hu / Default` | A duplicate user gives one delivery. |
| R11 | C | company Acme | `c:100 / SignalR / hu / Default` | The group channel goes one time to the company, in its default language. C has no email subscription, so no user gets an email. |
| R12 | D | company Globex | `c:200 / SignalR / en / Company`, `u:3 / Email / en / Company` | The group channel goes to the company. The email goes to each user of the company. |
| R13 | D | users 3 | `u:3 / SignalR / en / Company`, `u:3 / Email / en / Company` | The company subscriptions #8 and #9 apply together. |
| R14 | A | users 99 | none | An unknown user gives no delivery and no error. |
| R15 | D | users 2 | `u:2 / Email / hu / Company` | The fixed #11 and the user subscription #12 give the same target and channel. The fixed one applies, so user 2 cannot override it. |
| R16 | B | company Acme | `u:1 / Email / en / User`, `c:100 / Email / hu / Company` | The email goes to each user of Acme: user 1 by #6, and users 2 and 5 get nothing because of #4. The fixed #10 sends one email to the company. |
| R17 | B | users 1, 2 | `u:1 / Email / en / User`, `c:100 / Email / hu / Company` | Two users of Acme in one event give one email to the company, not two (design D90). |
| R18 | D | users 1, with #11 changed to name user 3 | none | User 3 belongs to Globex, so the resolution skips the named user. Acme data does not reach Globex. |
| R19 | A, tenant `"t2"` | users 1 | none | The services of the tenant `"t2"` are other instances. Its directory has its own user `"1"`, with the UI language `de`, and its subscription store is empty. The default #1 is in the store of `TenantKey.None`, so it does not apply to an event of `"t2"` (design D116, D126). This example runs through the subscription router, because the resolver has no tenant. |

### 5.4 Pipeline

Each example calls `ProcessAsync` with the scenario, the in-memory outbox, substitutes for the subscription store and the directory, and the fake time. It asserts the whole list of outgoing messages in the outbox.

| No. | Event | Expected outgoing messages | Expected `NotificationResult` |
|---|---|---|---|
| P1 | A, users 1, 2 | An email to `client1@acme.example` in English, and an email to `client2@acme.example` in Hungarian, both with an HTML body | 2 stored, 0 failed |
| P2 | B, users 1, 2 | An email to `client1@acme.example` in English, with a text body and no HTML body. An email to `admin@acme.example` in Hungarian, with an HTML body. | 2 stored, 0 failed |
| P3 | C, company Acme | One SignalR message with the target `c:100` and the method name `C` | 1 stored, 0 failed |
| P4 | D, company Globex | One SignalR message with the target `c:200`, and one email to `client3@globex.example` | 2 stored, 0 failed |
| P5 | A, users 1, 5 | An email to `client1@acme.example` | 1 stored, 1 failed, with `Errors.ValidationFailed` for user 5, who has no email address |
| P6 | A, users 1, processed two times with the same message id | The same outgoing message id both times | 1 stored each time |
| P7 | D, users 1 | An email to `client2@acme.example` in Hungarian. The directory gets a second `GetUsersAsync` call with only user 2. | 1 stored, 0 failed |

### 5.5 Management

Each example uses the scenario of section 4 and `ISubscriptionManager`. A change example starts from the full scenario in its own storage.

| No. | Action | Input | Expected result |
|---|---|---|---|
| M1 | A1 | none | `Email`, `SignalR`, and `Broker`, with the features of section 1.3 |
| M2 | A2 | no filter | A: #1 at `Default`. B: #2 at `Default`. C: #3 at `Default`. D and E: no subscription. |
| M3 | A2 | user 1 | A: #1 at `Default`. B: #6 at `User`. C: #3 at `Default`. D: none. E: #7 (the opt-out) at `User`. |
| M4 | A2 | company Acme | A: #1 at `Default`. B: #4 (the opt-out) and #10, both at `Company`. C: #3 at `Default`. D: #11 at `Company`. E: #5 at `Company`. |
| M5 | A2 | message type B and user 2 | Only B: #4 at `Company`. The fixed #10 goes to the company, not to user 2. |
| M6 | A2 | user 99 | `Errors.NotFound` |
| M7 | A3 | company Acme | #4, #5, #10, and #11 |
| M8 | A3 | message type B | #2, #4, #6, and #10 |
| M9 | A3 | level `User` | #6, #7, and #12 |
| M10 | A5 | User 2, A, channel `SignalR` | `Errors.ValidationFailed`, because A has not registered `SignalR` (rule 2.3.3) |
| M11 | A5 | User 1, C, channel `Email`, `SignalRChannelSettings` | `Errors.ValidationFailed`, because the settings type is wrong (rule 2.3.4) |
| M12 | A5 | User 1, A, channel `Email`, language `"xx-invalid"` | `Errors.ValidationFailed`, because the culture is unknown (rule 2.3.6) |
| M13 | A5 | User 1, A, opt-out for the channel `Email`, with `EmailChannelSettings` | `Errors.ValidationFailed` (rule 2.3.8) |
| M14 | A5 | User 2, C, channel `Email`, language `hu` | A new subscription with revision 1, a version 7 `Guid` id, and the fake time as both times. After it, R5 also gives `u:2 / Email / hu / User`. The delivery `u:2 / SignalR / hu / Default` stays, because the levels merge for each channel. |
| M15 | A5 | Three rows: company Acme, B, channel `Email` (#4 is an opt-out). User 1, B, opt-out for all channels (#6 is a channel). User 1, B, channel `Email` again (#6 exists). | `Errors.Conflict` for each row (rules 2.4.3, 2.4.2, and 2.4.1) |
| M16 | A6 | #6 with the expected revision 1, format `Html`. Then #6 again with the expected revision 1. | Revision 2 and the new `LastModifiedAtUtc`. Then `Errors.Conflict` (rule 2.4.4). |
| M17 | A6 | #6 with the owner user 2. Then #6 with the message type A. | `Errors.ValidationFailed` both times (rule 2.3.9) |
| M18 | A7, then A4 | Delete #7. Then read #7. | The read gives `Errors.NotFound`. After it, R7 gives `u:1 / Email / en / Company`, because the company subscription #5 now applies. |
| M19 | A2 | message type D and user 2 | Only D: #11 at `Company`. #12 is not in the result, because the fixed #11 gives the same target and channel. |
| M20 | A5 | Three rows: user 1, C, opt-out for the channel `SignalR`. User 1, C, channel `SignalR`, language `hu`. User 1, C, opt-out for all channels. | `Errors.Conflict` for the first two rows, because the default #3 is mandatory for `SignalR` (rule 2.4.5). The third row succeeds. After it, R5 still gives `u:1 / SignalR / en / Default`, because the mandatory channel stays. |
| M21 | A5 | Four rows: user 1, A, channel `Email`, recipients company. Company Acme, D, channel `Email`, named user 3. Company Acme, A, opt-out for all channels, recipients company. User 1, A, channel `Email`, mandatory. | `Errors.ValidationFailed` for each row (rules 2.3.10, 2.3.11, 2.3.12, and 2.3.13) |
| M22 | A5 | User 2, E, opt-out for the channel `Email` | A new subscription. After it, R8 gives no delivery, because the user opt-out overrides the company subscription #5 for `Email`. |

### 5.6 Store

Each example runs on each subscription store, through `SubscriptionStoreTestsBase`.

| No. | Given | When | Then |
|---|---|---|---|
| S1 | An empty store | `AddAsync` of #6, then `GetAsync` of its id | The same subscription, with the settings as `EmailChannelSettings("en", PlainText)`, the recipients scope users, and no mandatory flag |
| S2 | #6 is stored | `AddAsync` of a second subscription for user 1, B, `Email`. Then `AddAsync` of user 1, B, `SignalR`. | `Errors.Conflict`. Then success. |
| S3 | #6 is stored with revision 1 | `UpdateAsync` with the expected revision 1 and revision 2 | Success. `GetAsync` gives revision 2. |
| S4 | #6 is stored with revision 2 | `UpdateAsync` with the expected revision 1 | `Errors.Conflict`. The stored subscription does not change. |
| S5 | An empty store | `UpdateAsync` of an unknown id | `Errors.NotFound` |
| S6 | #6 is stored | `DeleteAsync` of #6, then `GetAsync`. Then `DeleteAsync` of an unknown id. | null. Then success. |
| S7 | The scenario | `QueryAsync` with each filter alone, with the company Acme and the level `Company` together, and with no filter | Each result whole: the matching labels of section 4, and all twelve with no filter |
| S8 | The scenario | `FindForDeliveryAsync` with B, the company Acme, and the users 1 and 2 | #2, #4, #6, and #10 |
| S9 | The scenario | `FindForDeliveryAsync` with A, no companies, and no users | #1 |
| S10 | An empty store | `AddAsync` for the user `"auth0|abc"` and for the company `"Ődön-Űr"`, then `QueryAsync` with each id | Each subscription is found with its id unchanged. |
| S11 | An empty store | `AddAsync` of #4 and of #10. Then `AddAsync` of a second subscription for Acme, B, `Email`, recipients company. Then `AddAsync` of #11, and `GetAsync` of its id. | Success two times, because #4 and #10 differ in their recipient kind. Then `Errors.Conflict`. Then #11 with its named user 2 unchanged. |
| S12 | The empty stores of the keys `TenantKey.None` and `"t2"`, on one storage | `AddAsync` of #6 to the first store, and of a copy of #6 with a new id to the store of `"t2"`. Then `FindForDeliveryAsync` with B and the user 1 on each store. | Success two times, because the two stores are apart. Then only #6. Then only the copy. |

### 5.7 Directory

Each example runs on each recipient directory, through `RecipientDirectoryTestsBase`, with the users and companies of section 4.

| No. | When | Then |
|---|---|---|
| U1 | `GetUsersAsync` with the users 1 and 2 | The two profiles of section 4, with their companies, UI languages, and email addresses |
| U2 | `GetUsersAsync` with the users 1 and 99 | Only the profile of user 1, with no error |
| U3 | `GetCompanyUsersAsync` with Acme, then with the company `"999"` | The users 1, 2, and 5. Then an empty list. |
| U4 | `GetCompanyAsync` with Acme, then with Globex, then with the company `"999"` | Acme with the default language `hu` and the email address `admin@acme.example`. Globex with the default language `en` and no address. Then null. |

### 5.8 Routing verification

Each example runs `IRoutingVerifier.VerifyAsync` on the scenario of section 4, with the languages `en` and `hu`. Each message type A to E has one test case `basic`, except where an example says otherwise. The email templates of A to E exist in `en` and `hu`, except where an example says otherwise. A row has the form `test case / owner / target / channel / language / outcome`.

| No. | Given | Request | Expected rows |
|---|---|---|---|
| X1 | The scenario | Type A, test user 1 | `basic / Default / u:1 / Email / en / Passed`, `basic / Default / u:1 / Email / hu / Passed`. The subscription #1 has no language, so the language of the variation applies. |
| X2 | The scenario | Type B, test user 1 | `basic / User 1 / u:1 / Email / en / Passed` two times, one for each variation. The subscription #6 has the fixed language `en`. `basic / Company Acme / c:100 / Email / hu / Passed` two times. The fixed #10 uses the default language of Acme, because a variation changes only the profile of the test user. |
| X3 | The scenario | Type E, test user 1 | `basic / User 1 / none / none / en / Passed` and the same row for `hu`. The opt-out #7 gives no delivery, and that is the correct result. |
| X4 | The email template of A exists only in `en` | Type A, test user 1 | The `en` row is `Passed`. The `hu` row is `Failed` with the error code 404 and the name `NotFound`. |
| X5 | The scenario | Type D, test user 1 only | `basic / Company Acme / u:2 / Email / hu / Passed` two times, one for each variation. The fixed #11 sends to user 2 in the UI language of user 2. Three `Skipped` rows show #8 and #9 of Globex, because no test user of Globex took part, and #12 of user 2, because user 2 is not a test user. |
| X6 | Type C has no test case | Type C, test user 1 | One `Skipped` row for C |
| X7 | The scenario | All types, test users 1 and 3 | The outbox store gets no `AddAsync` call, and no transport gets a call. `Failed` is 0, because each template exists and Acme has an email address. |

### 5.9 Resolution traces

Each example asserts the whole trace of one delivery, or one opt-out decision (section 2.5). A subscription has the form `#label (owner)`. The skipped subscriptions are in level order, from the default down.

| No. | Given | Delivery | Expected trace |
|---|---|---|---|
| T1 | Not the scenario: a default subscription for A, `Email`, `de`. An Acme subscription for A, `Email`, `hu`. A user 1 subscription for A, `Email`, `en`. Event A, users 1. | `u:1 / Email / en / User` | Applied: the user 1 subscription. Target `ScopeUser`. Language `ChannelSettings`, so `en` comes from the user 1 subscription. Address `UserProfile`. Skipped: the default subscription `Overridden`, then the Acme subscription `Overridden`. |
| T2 | R1 | `u:1 / Email / en / Default` | Applied: #1 (Default). Target `ScopeUser`. Language `UserUiLanguage`, because #1 has no language. Address `UserProfile`. Skipped: none. |
| T3 | R3 | `u:1 / Email / en / User` | Applied: #6 (User 1). Target `ScopeUser`. Language `ChannelSettings`. Address `UserProfile`. Skipped: #2 (Default) `Overridden`, then #4 (Acme) `Overridden`. |
| T4 | R3 | `c:100 / Email / hu / Company` | Applied: #10 (Acme), recipients company. Target `Company`. Language `CompanyDefaultLanguage`. Address `CompanyProfile`. Skipped: none. |
| T5 | R15 | `u:2 / Email / hu / Company` | Applied: #11 (Acme), named users. Target `NamedUser`. Language `UserUiLanguage`. Address `UserProfile`. Skipped: #12 (User 2) `Merged`. |
| T6 | R11 | `c:100 / SignalR / hu / Default` | Applied: #3 (Default), mandatory. Target `CompanyGroup`. Language `CompanyDefaultLanguage`. Address `None`. Skipped: none. |
| T7 | R4 | No delivery to `u:2` on `Email` | An opt-out decision: target `u:2`, channel `Email`, opt-out #4 (Acme). Skipped: #2 (Default) `Overridden`. |
| T8 | The scenario, and a user 1 subscription for C, `SignalR`, `hu`, written straight into the store, so the check of rule 2.4.5 did not run. Event C, users 1. | `u:1 / SignalR / en / Default` | Applied: #3 (Default), mandatory. Target `ScopeUser`. Language `UserUiLanguage`. Address `None`. Skipped: the user 1 subscription `Locked`. |
