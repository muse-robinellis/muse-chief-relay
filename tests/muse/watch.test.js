const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const path = require("path");

const srcDir = path.join(__dirname, "../../web/muse/src");
const published = path.join(__dirname, "../../docs/muse");

test("watch format helpers", async () => {
  const f = await import("../../web/muse/src/watchFormat.js");

  assert.equal(f.roleOf("Alex"), "human");
  assert.equal(f.roleOf("Fuse"), "agent");
  assert.equal(f.roleOf("chief"), "agent");
  assert.equal(f.roleOf("Design"), "agent");
  assert.equal(f.roleOf("spectator-ab12"), "spectator");
  assert.equal(f.roleOf("stranger"), "guest");

  assert.deepEqual(f.nickStyle("Fuse"), { fg: "#7ee0b0", bg: "rgba(126,224,176,0.12)" });
  assert.deepEqual(f.nickStyle("nobody-in-particular"), f.nickStyle("nobody-in-particular"));

  const task = f.parseEnvelope(JSON.stringify({ type: "task", id: "t1", title: "Hi" }));
  assert.equal(task.type, "task");
  assert.equal(task.id, "t1");
  assert.equal(f.parseEnvelope("hello world"), null);
  assert.equal(f.parseEnvelope("{not json"), null);
  assert.equal(f.parseEnvelope(JSON.stringify({ cmd: "chat" })), null);
  assert.equal(f.parseEnvelope(""), null);

  assert.match(f.spectatorNick(), /^spectator-[a-z0-9]{4}$/);
});

test("watch route wiring", () => {
  const main = fs.readFileSync(path.join(srcDir, "main.js"), "utf8");
  assert.match(main, /#\/watch/);
  assert.match(main, /WatchLive/);
  assert.doesNotMatch(main, /location\.hash\s*=/);
  assert.ok(fs.existsSync(path.join(srcDir, "WatchLive.vue")));
  assert.ok(fs.existsSync(path.join(srcDir, "useWatch.js")));
  assert.ok(fs.existsSync(path.join(srcDir, "watchFormat.js")));
});

test("watch view joins the relay channel as a read-only spectator", () => {
  const w = fs.readFileSync(path.join(srcDir, "useWatch.js"), "utf8");
  assert.match(w, /fuse-grok-6f4e970cd8/);
  assert.match(w, /spectatorNick/);
  assert.doesNotMatch(w, /console\./);
  const v = fs.readFileSync(path.join(srcDir, "WatchLive.vue"), "utf8");
  assert.match(v, /Two AI agents and a human/);
  assert.match(v, /TransitionGroup/);
});

test("the Pages build includes the watch-live view", () => {
  const index = path.join(published, "index.html");
  assert.ok(fs.existsSync(index), "docs/muse/index.html missing; run npm run build in web/muse");
  let text = "";
  const walk = (dir) => {
    for (const name of fs.readdirSync(dir)) {
      const p = path.join(dir, name);
      if (fs.statSync(p).isDirectory()) walk(p);
      else if (/\.(html|js|css)$/.test(name)) text += fs.readFileSync(p, "utf8");
    }
  };
  walk(published);
  assert.match(text, /Two AI agents and a human/);
  assert.match(text, /#\/watch/);
});
