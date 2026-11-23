// The dashboard: a grid of session panes, the publish panel with its automatic producer, and the sent log. The server
// gives the expected panes of each notification from the state of the panes, and the sent log marks a miss or an extra
// hit in red.
import { SessionPane } from "./session.js";

const missDelayMs = 2000;
const maxSentRows = 100;
const data = await (await fetch("/demo/data")).json();
const panes = new Map();
const sent = new Map();
const receivedBeforeTheResponse = new Map();
const panesElement = document.getElementById("panes");
const form = document.getElementById("publish-form");
const sentLog = document.getElementById("sent-log");
let paneCount = 0;
let autoCount = 0;
let producer = null;

document.getElementById("add-session").addEventListener("click", () => addPane("localhost"));
form.elements.level.addEventListener("change", fillTargets);
form.elements.tenant.addEventListener("change", fillTargets);
form.addEventListener("submit", async event => {
  event.preventDefault();
  await publish(form.elements.level.value, form.elements.tenant.value, form.elements.target.value, form.elements.text.value);
});
form.elements.auto.addEventListener("change", restartProducer);
form.elements.interval.addEventListener("change", restartProducer);
form.elements.tenant.append(...data.tenants.map(tenant => new Option(tenant.id, tenant.id)));
fillTargets();
addPane("localhost");

function addPane(host) {
  paneCount += 1;
  const pane = new SessionPane(panesElement, {
    id: `P${paneCount}`,
    host,
    data,
    onReceived: received,
    onRemove: async removed => {
      panes.delete(removed.id);
      await removed.close();
    },
  });
  panes.set(pane.id, pane);
  pane.open();
}

function fillTargets() {
  const tenant = data.tenants.find(item => item.id === form.elements.tenant.value);
  const level = form.elements.level.value;
  const ids = level === "Company" ? tenant.companies : level === "User" ? tenant.users.map(user => user.id) : [];
  form.elements.target.replaceChildren(...ids.map(id => new Option(id, id)));
  form.elements.target.disabled = ids.length === 0;
}

async function publish(level, tenant, targetId, text) {
  const response = await fetch("/demo/publish", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ tenant, level, targetId: level === "Tenant" ? null : targetId, text, panes: [...panes.values()].map(pane => pane.state()) }),
  });
  const body = await response.json();
  if (!response.ok) {
    alert(`${body.errorType}: ${body.detail}`);
    return;
  }

  const row = sentLog.insertRow(0);
  const entry = { row, expected: new Set(body.expected), got: receivedBeforeTheResponse.get(body.id) ?? new Set(), settled: false };
  receivedBeforeTheResponse.delete(body.id);
  sent.set(body.id, entry);
  for (const value of [new Date().toLocaleTimeString(), level, tenant, level === "Tenant" ? "-" : targetId, text]) {
    row.insertCell().textContent = value;
  }

  row.insertCell().className = "chips";
  row.insertCell().className = "result";
  render(entry);
  setTimeout(() => {
    entry.settled = true;
    render(entry);
  }, missDelayMs);
  while (sentLog.rows.length > maxSentRows) {
    sentLog.deleteRow(-1);
    sent.delete(sent.keys().next().value);
  }
}

function received(paneId, payload) {
  const entry = sent.get(payload.id);
  if (entry) {
    entry.got.add(paneId);
    render(entry);
    return;
  }

  const early = receivedBeforeTheResponse.get(payload.id) ?? new Set();
  receivedBeforeTheResponse.set(payload.id, early.add(paneId));
}

function render(entry) {
  const ids = [...new Set([...entry.expected, ...entry.got])].sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));
  const states = ids.map(id => ({ id, state: stateOf(entry, id) }));
  entry.row.cells[5].replaceChildren(...states.map(({ id, state }) => {
    const chip = document.createElement("span");
    chip.className = `chip ${state}`;
    chip.textContent = id;
    chip.title = state;
    return chip;
  }));
  const misses = states.filter(item => item.state === "miss").length;
  const extras = states.filter(item => item.state === "extra").length;
  entry.row.cells[6].textContent = misses + extras === 0 ? (entry.settled ? "OK" : "...") : `${misses} miss, ${extras} extra hit`;
  entry.row.classList.toggle("bad", misses + extras > 0);
}

function stateOf(entry, id) {
  if (entry.expected.has(id)) {
    return entry.got.has(id) ? "hit" : entry.settled ? "miss" : "waiting";
  }

  return "extra";
}

function restartProducer() {
  clearInterval(producer);
  producer = null;
  if (form.elements.auto.checked) {
    const seconds = Math.min(60, Math.max(1, Number(form.elements.interval.value) || 3));
    producer = setInterval(publishRandom, seconds * 1000);
  }
}

async function publishRandom() {
  const tenant = pick(data.tenants);
  const level = pick(["Tenant", "Company", "User"]);
  const targetId = level === "Company" ? pick(tenant.companies) : level === "User" ? pick(tenant.users).id : null;
  autoCount += 1;
  await publish(level, tenant.id, targetId, `Automatic ${autoCount}`);
}

function pick(items) {
  return items[Math.floor(Math.random() * items.length)];
}
