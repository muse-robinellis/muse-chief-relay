// Exact board route. A prefix match would send #/boardroom to the board view;
// those stay on the chat view.
const BOARD_HASHES = new Set(["#/board", "#/board/"]);

export function isBoardRoute(hash) {
  return BOARD_HASHES.has(String(hash || ""));
}
