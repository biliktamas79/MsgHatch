# Example app

`ExampleApp` runs the subscription example of section 1.1 of `docs/design.md` as a web app. It needs no broker server, no SMTP server, no cloud account, and no network. It writes each email to the log, sends the broker messages to a local Wolverine queue, and keeps all data in one SQLite file. It maps the HTTP APIs of the subscriptions, the delivery, the registry, and the templates.

`examples/AdminUiExample` is the admin UI of this app. It calls the subscription and template APIs of `ExampleApp` through the HTTP clients.

Run each command from the root folder of the repository. The commands work in PowerShell and in bash.

## Requirements

- The .NET 10 SDK.
- Nothing else. The app makes its own data at its first start.

## Setup

Do these steps one time.

1. Make a token for the user `"1"` with the roles that you need:

   ```bash
   dotnet user-jwts create --project examples/ExampleApp --name 1 --role TemplateAdmin
   ```

   The first run makes the signing key, and writes it into the user secrets of `ExampleApp`. Git does not see the user secrets. The tool also writes the issuer `dotnet-user-jwts` and the audience `http://localhost:5080` into `appsettings.Development.json`. The repository already holds these values, so the run changes no file of the repository.

2. Select the roles of the token from this table. One token can have more than one role, with one `--role` for each role.

   | Role | Endpoints |
   |---|---|
   | None | The subscription endpoints under `/api/subscriptions`. Each signed-in user passes. |
   | `TemplateAdmin` | The template endpoints under `/api/templates` (D225). |
   | `RegistryAdmin` | The registry endpoints under `/api/registry` (D170). |
   | `DeliveryAdmin` | The delivery overview and actions under `/api/delivery` (D165). |
   | `DeliveryContent` | The content of the delivered messages under `/api/delivery` (D165). |

   For example, a token for all endpoints:

   ```bash
   dotnet user-jwts create --project examples/ExampleApp --name 1 --role TemplateAdmin --role RegistryAdmin --role DeliveryAdmin --role DeliveryContent
   ```

## Start the example app

```bash
dotnet run --project examples/ExampleApp
```

The app listens on `http://localhost:5080`.

- **The first start** writes the scenario "Acme and Globex" of `docs/subscription-domain.md`: the companies, the users, and the 12 subscriptions. The data goes into `msghatch-example.db` in `examples/ExampleApp`, and git ignores this file.
- **In the `Development` environment,** the start also writes an example history of the channel states (D288), so that the History dialog of the admin UI has data. Email gets four down states, one of them 40 days ago, before the 30-day window, and one of them a pause with the note "Example maintenance". SignalR gets one down state, and Broker gets no row. Each down state ends, so each channel is up after the start. The start writes the history only for a channel with no state row. So a new database file gets the history, and a channel with a state row keeps its history as it is.
- **The message type A** registers the channels Email and SignalR. The scenario registers only Email for A, so its example M10 is not true in this app. With SignalR on A, the admin UI can make a UI notification of A for a company, for example Acme.
- **To start again from the scenario,** stop the app. Then delete the files `msghatch-example.db*` in `examples/ExampleApp`.
- **The email templates** are in the folder `Templates` of the build output (D221). A write through the template endpoints changes only the build output, never the files of the repository. A build does not replace a changed template, because it copies only the newer files. To get back the templates of the repository, stop the app. Then delete the folder `examples/ExampleApp/bin/Debug/net10.0/Templates`. The next build or run copies the templates again.

To call an endpoint, put a new token into the header. This example lists the subscriptions of the signed-in user:

```bash
curl -H "Authorization: Bearer $(dotnet user-jwts create --project examples/ExampleApp --name 1 --output token)" http://localhost:5080/api/subscriptions/me
```

`--output token` writes only the token, so the token does not show on the screen.

## Start the admin UI

1. Make a token with the roles `TemplateAdmin` and `RegistryAdmin`. This command also stores the token in the user secrets of `AdminUiExample`:

   ```bash
   dotnet user-secrets set "AdminUiExample:ApiToken" $(dotnet user-jwts create --project examples/ExampleApp --name 1 --role TemplateAdmin --role RegistryAdmin --output token) --project examples/AdminUiExample
   ```

   The role `TemplateAdmin` gives access to the template endpoints. The role `RegistryAdmin` gives access to the registry endpoints, which the pages Channels and Message types read (D170). The subscription endpoints accept each signed-in user. Never write a token into a file of the repository.

2. Start `ExampleApp` in a terminal, and keep it open:

   ```bash
   dotnet run --project examples/ExampleApp
   ```

3. Start `AdminUiExample` in a second terminal:

   ```bash
   dotnet run --project examples/AdminUiExample
   ```

4. Open `http://localhost:5090`. The admin UI has no sign-in, and listens only on `localhost`. It sends the same token for each user.

The emails of the message types A to E use the placeholder engine, so their html parts open in the plain editor with the HTML highlight. The password reset email of F uses the Liquid engine, so its parts open in the Liquid editor with its check.

Each row of the template list has the buttons View and Edit. The view page shows the stored text read-only. The edit page shows the editor. Both pages show a preview of the text next to it, or under it on a narrow screen. The preview renders the text with the sample payload that `ExampleSetup` gives each email with `WithTemplateSample` (D254). For example, the preview of the password reset email shows the link `https://example.com/reset`. The edit page renders the preview again 500 ms after the last change of the text. An html part shows its preview in a frame that runs no script.

The menu has the order Channels, Message types, Subscriptions, Templates, and Verification. The page Channels shows the channels of `ExampleApp`, each with its state: the badge Up or Down, the reason of a down state, and the start of the state with the time since then and the duration of the previous state. The button Check runs the health check of the channel, and shows the result above the list. The transports of the example give no error, so a check only ends a down state for errors, and never a pause. The button History opens a timeline of the last 30 days, red where the channel was down, with the list of its state changes. The view page of a channel shows the same state, and the message types that register it. The page Message types shows each message type with its channels and its number of subscriptions of each owner level, and each number links to the subscription list with these filters. The channel and message type pages are read-only. To see a down state, pause a channel with `POST /api/delivery/channels/{channel}/pause`, which needs the role `DeliveryAdmin`.

The admin UI reads the token one time, at its start. After a change of the token, restart `AdminUiExample`.

## Run the tests

The tests make their own tokens and their own data in a temporary folder. They need no setup:

```bash
dotnet test tests/ExampleApp.Tests.Unit
```

```bash
dotnet test tests/AdminUiExample.Tests.Unit
```

## Problems

| Problem | Cause | Fix |
|---|---|---|
| Each page of the admin UI shows "You have no permission to do this action." | `ExampleApp` rejects the token of the admin UI with 401. The token is not a JWT (for example a placeholder text), or it expired, or another signing key signed it. | Store a new token with step 1 of "Start the admin UI". Then restart `AdminUiExample`. |
| An endpoint gives 403. | The token does not have the role of the endpoint. | Make a token with the role from the table in "Setup". |
| The pages Channels and Message types show "You have no permission to do this action.", and the other pages work. | `ExampleApp` rejects the calls of the registry pages with 403, because the token has no role `RegistryAdmin`. | Store a new token with step 1 of "Start the admin UI". Then restart `AdminUiExample`. |
| `AdminUiExample` stops at its start with an options error about `ApiToken`. | The user secrets of `AdminUiExample` have no token. | Do step 1 of "Start the admin UI". |
| `dotnet user-jwts` gives "Could not find file ... appsettings.Development.json". | The checkout is older than the commit that added this file. | Pull `main`. |
| `dotnet run` gives an error that the address is in use. | The app already runs in another terminal. | Use the app that runs, or stop it first. |
