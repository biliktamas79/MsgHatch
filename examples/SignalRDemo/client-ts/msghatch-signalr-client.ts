// A typed browser client of the MsgHatch SignalR channel, for the hub of the SignalR demo (D316).
// Read docs/signalr-client.md for the rules that this file follows.
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";

/** What the hub tells a connection after each join. It is the `JoinedSession` record of `DemoHub`. */
export interface JoinedSession {
  tenant: string;
  company: string | null;
  user: string | null;
  userName: string | null;
  groups: string[];
}

/** The payload of the message type `DemoNotice`. It is the `DemoPayload` record of the demo, in camelCase. */
export interface DemoPayload {
  id: string;
  tenant: string | null;
  level: "Tenant" | "Company" | "User";
  targetId: string;
  text: string;
}

/**
 * The map from a message type name to its payload. The SignalR transport calls the client method whose name is the
 * message type name, with the payload as its one argument. Add one entry for each message type on the SignalR channel.
 */
export interface NotificationMap {
  DemoNotice: DemoPayload;
}

export type NotificationName = keyof NotificationMap;
export type NotificationHandler<TName extends NotificationName> = (payload: NotificationMap[TName]) => void;

export interface ClientOptions {
  /** The URL of the hub, for example `/hubs/demo`. */
  hubUrl: string;
  /** Gives the token of the user, or null for an anonymous page. */
  getToken?: () => string | null | Promise<string | null>;
  /** The tenant of an anonymous page, which the page gets from its domain or its landing page. */
  anonymousTenant?: string;
  onJoined?: (session: JoinedSession) => void;
  onStateChanged?: (state: HubConnectionState) => void;
}

/**
 * One connection to the hub. With a token, the hub joins the groups of the tenant and the company of the token, and
 * sends the user notifications to the user name `{tenant}:{user}`. With no token, the client calls `JoinTenant`, and
 * calls it again after each reconnect, because a new connection id has no groups.
 */
export class MsgHatchSignalRClient {
  readonly #options: ClientOptions;
  readonly #connection: HubConnection;

  constructor(options: ClientOptions) {
    this.#options = options;
    this.#connection = new HubConnectionBuilder()
      .withUrl(options.hubUrl, { accessTokenFactory: async () => (await options.getToken?.()) ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.#connection.on("Joined", (session: JoinedSession) => options.onJoined?.(session));
    this.#connection.onreconnecting(() => this.#notifyState());
    this.#connection.onreconnected(() => this.#rejoinAnonymousTenant());
    this.#connection.onclose(() => this.#notifyState());
  }

  get state(): HubConnectionState {
    return this.#connection.state;
  }

  /** Adds a handler of one message type. Call it before `start`, so that no notification arrives with no handler. */
  on<TName extends NotificationName>(name: TName, handler: NotificationHandler<TName>): () => void {
    const callback = (payload: NotificationMap[TName]) => handler(payload);
    this.#connection.on(name, callback);
    return () => this.#connection.off(name, callback);
  }

  async start(): Promise<void> {
    await this.#connection.start();
    await this.#joinAnonymousTenant();
    this.#notifyState();
  }

  async stop(): Promise<void> {
    await this.#connection.stop();
  }

  async #isAnonymous(): Promise<boolean> {
    return !(await this.#options.getToken?.());
  }

  async #joinAnonymousTenant(): Promise<void> {
    if (this.#options.anonymousTenant && (await this.#isAnonymous())) {
      await this.#connection.invoke("JoinTenant", this.#options.anonymousTenant);
    }
  }

  async #rejoinAnonymousTenant(): Promise<void> {
    await this.#joinAnonymousTenant();
    this.#notifyState();
  }

  #notifyState(): void {
    this.#options.onStateChanged?.(this.#connection.state);
  }
}

/** An example: an anonymous page of the tenant `eu`, then the same page after the login of `bela` of `acme`. */
export async function example(): Promise<void> {
  let token: string | null = null;
  const anonymous = new MsgHatchSignalRClient({ hubUrl: "/hubs/demo", anonymousTenant: "eu" });
  anonymous.on("DemoNotice", payload => console.log(`${payload.level} notice: ${payload.text}`));
  await anonymous.start();

  const response = await fetch("/demo/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ tenant: "eu", user: "bela", account: "acme" }),
  });
  token = ((await response.json()) as { token: string }).token;
  await anonymous.stop();

  const signedIn = new MsgHatchSignalRClient({
    hubUrl: "/hubs/demo",
    getToken: () => token,
    onJoined: session => console.log(`Joined ${session.groups.join(", ")} as ${session.userName}`),
  });
  signedIn.on("DemoNotice", payload => console.log(`${payload.level} notice for ${payload.targetId}: ${payload.text}`));
  await signedIn.start();
}
