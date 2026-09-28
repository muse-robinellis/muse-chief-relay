const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const path = require("path");

const srcDir = path.join(__dirname, "../../web/muse/src");

test("board route is an exact match", async () => {
  const { isBoardRoute } = await import("../../web/muse/src/navRoute.js");
  assert.equal(isBoardRoute("#/board"), true);
  assert.equal(isBoardRoute("#/board/"), true);
  assert.equal(isBoardRoute("#/"), false);
  assert.equal(isBoardRoute("#/watch"), false);
  assert.equal(isBoardRoute("#/boardroom"), false);
  assert.equal(isBoardRoute("#/Board"), false);
  assert.equal(isBoardRoute(""), false);
  assert.equal(isBoardRoute(null), false);
});

test("the app shell wires the board section without writing the hash", () => {
  const app = fs.readFileSync(path.join(srcDir, "App.vue"), "utf8");
  assert.match(app, /from\s+"\.\/navRoute\.js"/);
  assert.match(app, /isBoardRoute\(window\.location\.hash\)/);
  assert.match(app, /import SiteHeader from "\.\/SiteHeader\.vue"/);
  assert.match(app, /<SiteHeader/);
  assert.match(app, /key="board"/);
  assert.match(app, /key="chat"/);
  assert.doesNotMatch(app, /location\.hash\s*=/);

  const nav = fs.readFileSync(path.join(srcDir, "navRoute.js"), "utf8");
  assert.doesNotMatch(nav, /startsWith/);
  assert.doesNotMatch(nav, /console\./);
});

test("the shared header links the three sections and shows a board badge", () => {
  const header = fs.readFileSync(path.join(srcDir, "SiteHeader.vue"), "utf8");
  assert.match(header, /href: "#\/"/);
  assert.match(header, /href: "#\/board"/);
  assert.match(header, /href: "#\/watch"/);
  assert.match(header, /boardBadge/);
  assert.match(header, /aria-current/);
  assert.match(header, /live-ping/);
  assert.doesNotMatch(header, /location\.hash\s*=/);
  assert.doesNotMatch(header, /console\./);

  const watch = fs.readFileSync(path.join(srcDir, "WatchLive.vue"), "utf8");
  assert.match(watch, /<SiteHeader section="watch"/);
  assert.match(watch, /live-dot/);

  const css = fs.readFileSync(path.join(srcDir, "styles.css"), "utf8");
  assert.match(css, /\.live-ping/);
  assert.match(css, /@keyframes live-ping/);
  assert.match(css, /\.view-enter-active/);
});

test("the room board keeps its identity and the chat keeps its structure", () => {
  const board = fs.readFileSync(path.join(srcDir, "RoomBoard.vue"), "utf8");
  assert.match(board, /id="room-board"/);

  const app = fs.readFileSync(path.join(srcDir, "App.vue"), "utf8");
  const join = app.indexOf('id="join-form"');
  const chat = app.indexOf('id="chat-panel"');
  assert.ok(join > 0 && chat > join, "join form still precedes the chat panel");
  assert.match(app, /id="send-form"/);
  assert.match(app, /href="#\/board"/);
});
