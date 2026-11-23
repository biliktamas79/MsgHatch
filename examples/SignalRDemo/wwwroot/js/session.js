// One session pane: the flow of a page (domain, tenant choice, login, and account choice) with its own HubConnection.
// The pane keeps its token in memory only, so two panes can be two users of one tenant or of two tenants.

const maxLogEntries = 50;

export class SessionPane {
  #connection = null;
  #token = null;
  #tenant = null;
  #domainCompany = null;
  #joined = null;
  #host;
  #fields = {};
  #flow;
  #log;

  constructor(container, { id, host, data, onReceived = () => {}, onChanged = () => {}, onRemove = null }) {
    this.id = id;
    this.data = data;
    this.onReceived = onReceived;
    this.onChanged = onChanged;
    this.element = this.#render(container, host, onRemove);
  }

  state() {
    return {
      id: this.id,
      connected: this.#connection?.state === signalR.HubConnectionState.Connected,
      tenant: this.#joined?.tenant ?? null,
      company: this.#joined?.company ?? null,
      user: this.#joined?.user ?? null,
    };
  }

  async open() {
    await this.#stop();
    this.#token = null;
    this.#tenant = null;
    this.#joined = null;
    this.#flow.replaceChildren();
    this.#show();
    const response = await fetch(`/demo/resolve?host=${encodeURIComponent(this.#host.value)}`);
    const domain = await response.json();
    this.#domainCompany = domain.company;
    this.#fields.domain.textContent = describeDomain(domain);
    if (domain.tenant) {
      await this.#joinTenant(domain.tenant);
    } else {
      this.#showTenantChoice();
    }
  }

  async close() {
    await this.#stop();
    this.element.remove();
  }

  async #connectAnonymously() {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/demo")
      .withAutomaticReconnect()
      .build();
    await this.#start(connection);
  }

  async #joinTenant(tenant) {
    await this.#connectAnonymously();
    this.#tenant = tenant;
    await this.#connection.invoke("JoinTenant", tenant);
    this.#showLogin();
  }

  async #logIn(user, account) {
    const response = await fetch("/demo/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ tenant: this.#tenant, user, account: account ?? this.#domainCompany }),
    });
    const body = await response.json();
    if (!response.ok) {
      return body;
    }

    this.#token = body.token;
    await this.#connectWithToken();
    return null;
  }

  async #connectWithToken() {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/demo", { accessTokenFactory: () => this.#token })
      .withAutomaticReconnect()
      .build();
    await this.#start(connection);
  }

  async #start(connection) {
    await this.#stop();
    connection.on("Joined", session => this.#onJoined(session));
    connection.on("DemoNotice", payload => this.#onNotice(payload));
    connection.onreconnecting(() => this.#show());
    connection.onreconnected(() => this.#rejoin());
    connection.onclose(() => this.#show());
    this.#connection = connection;
    this.#joined = null;
    await connection.start();
    this.#show();
  }

  async #rejoin() {
    if (!this.#token && this.#tenant) {
      await this.#connection.invoke("JoinTenant", this.#tenant);
    }

    this.#show();
  }

  async #stop() {
    const connection = this.#connection;
    this.#connection = null;
    await connection?.stop();
  }

  #onJoined(session) {
    this.#joined = session;
    this.#show();
  }

  #onNotice(payload) {
    const entry = document.createElement("li");
    const time = document.createElement("time");
    time.textContent = new Date().toLocaleTimeString();
    const level = document.createElement("span");
    level.className = `level ${payload.level.toLowerCase()}`;
    level.textContent = payload.level;
    const text = document.createElement("code");
    text.textContent = JSON.stringify(payload);
    entry.append(time, " ", level, " ", text);
    this.#log.prepend(entry);
    while (this.#log.children.length > maxLogEntries) {
      this.#log.lastElementChild.remove();
    }

    this.element.classList.remove("flash");
    void this.element.offsetWidth;
    this.element.classList.add("flash");
    this.onReceived(this.id, payload);
  }

  #showTenantChoice() {
    const select = selectOf(this.data.tenants.map(tenant => tenant.id));
    const choose = button("Choose tenant", async () => this.#joinTenant(select.value));
    this.#flow.replaceChildren(labelOf("The domain gives no tenant. Tenant", select), choose);
  }

  #showLogin() {
    const tenant = this.data.tenants.find(item => item.id === this.#tenant);
    const users = selectOf(tenant.users.map(user => user.id));
    const accountLabel = labelOf("Account", selectOf([]));
    accountLabel.hidden = true;
    const message = document.createElement("p");
    message.className = "message";
    const logIn = button("Log in", async () => {
      const account = accountLabel.hidden ? null : accountLabel.querySelector("select").value;
      const problem = await this.#logIn(users.value, account);
      if (!problem) {
        this.#flow.replaceChildren(button("Log out", async () => this.open()));
        return;
      }

      message.textContent = `${problem.errorType}: ${problem.detail}`;
      if (problem.errorType === "ValidationFailed") {
        const user = tenant.users.find(item => item.id === users.value);
        accountLabel.querySelector("select").replaceWith(selectOf(user.accounts));
        accountLabel.hidden = false;
      }
    });
    users.addEventListener("change", () => {
      accountLabel.hidden = true;
      message.textContent = "";
    });
    this.#flow.replaceChildren(labelOf("User", users), accountLabel, logIn, message);
  }

  #show() {
    const joined = this.#joined;
    this.#fields.tenant.textContent = joined?.tenant ?? this.#tenant ?? "-";
    this.#fields.company.textContent = joined?.company ?? "-";
    this.#fields.user.textContent = joined?.user ? `${joined.user} (${joined.userName})` : "anonymous";
    this.#fields.connection.textContent = this.#connection?.state ?? "Disconnected";
    this.#fields.groups.replaceChildren(...(joined?.groups ?? []).map(group => {
      const item = document.createElement("li");
      item.textContent = group;
      return item;
    }));
    this.onChanged();
  }

  #render(container, host, onRemove) {
    const pane = document.createElement("article");
    pane.className = "pane";
    pane.innerHTML = `
      <header>
        <strong class="pane-id"></strong>
        <input class="host" aria-label="Host" />
        <button type="button" class="open">Open</button>
      </header>
      <dl class="facts">
        <dt>Domain</dt><dd data-field="domain">-</dd>
        <dt>Tenant</dt><dd data-field="tenant">-</dd>
        <dt>Company</dt><dd data-field="company">-</dd>
        <dt>User</dt><dd data-field="user">anonymous</dd>
        <dt>Connection</dt><dd data-field="connection">Disconnected</dd>
        <dt>Groups</dt><dd><ul data-field="groups"></ul></dd>
      </dl>
      <div class="flow"></div>
      <ol class="log"></ol>`;
    pane.querySelector(".pane-id").textContent = this.id;
    for (const field of pane.querySelectorAll("[data-field]")) {
      this.#fields[field.dataset.field] = field;
    }

    this.#host = pane.querySelector(".host");
    this.#host.value = host;
    this.#flow = pane.querySelector(".flow");
    this.#log = pane.querySelector(".log");
    pane.querySelector(".open").addEventListener("click", () => this.open());
    if (onRemove) {
      pane.querySelector("header").append(button("×", async () => onRemove(this)));
    }

    pane.addEventListener("animationend", () => pane.classList.remove("flash"));
    container.append(pane);
    return pane;
  }
}

function describeDomain(domain) {
  if (domain.company) {
    return `company ${domain.company} of tenant ${domain.tenant}`;
  }

  return domain.tenant ? `tenant ${domain.tenant}` : "nothing";
}

function selectOf(values) {
  const select = document.createElement("select");
  select.append(...values.map(value => new Option(value, value)));
  return select;
}

function labelOf(text, control) {
  const label = document.createElement("label");
  label.append(`${text} `, control);
  return label;
}

function button(text, onClick) {
  const element = document.createElement("button");
  element.type = "button";
  element.textContent = text;
  element.addEventListener("click", onClick);
  return element;
}
