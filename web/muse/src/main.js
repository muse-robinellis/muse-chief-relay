import { createApp, h, ref } from "vue";
import App from "./App.vue";
import WatchLive from "./WatchLive.vue";
import "./styles.css";

// Tiny hash router. "#/watch" mounts the read-only live spectator view;
// anything else mounts the interactive client. The hash is only ever read,
// never written, and switching views unmounts the previous root cleanly.
function pick() {
  return window.location.hash.startsWith("#/watch") ? WatchLive : App;
}

const current = ref(pick());
window.addEventListener("hashchange", () => {
  current.value = pick();
});

createApp({ setup: () => () => h(current.value) }).mount("#app");
