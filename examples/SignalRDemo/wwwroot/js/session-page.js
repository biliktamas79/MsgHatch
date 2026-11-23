// The page /session: one session pane alone, for a demo in real browser tabs. The host comes from ?host=, or from the
// address of the tab, for example http://acme.localhost:5091/session.
import { SessionPane } from "./session.js";

const data = await (await fetch("/demo/data")).json();
const host = new URLSearchParams(location.search).get("host") ?? location.host;
const pane = new SessionPane(document.getElementById("panes"), { id: "S", host, data });
pane.open();
