// JS <-> C# WebView2 message bridge.
//
// Protocol (request/response, requestId-correlated):
//   JS  -> C#:  { requestId, method, args }
//   C#  -> JS:  { requestId, ok, result?, error? }
//
// The C# side posts responses via CoreWebView2.PostWebMessageAsJson; we receive
// them on window.chrome.webview's "message" event.

interface BridgeResponse {
  requestId: string;
  ok: boolean;
  result?: unknown;
  error?: string;
}

interface WebView2Like {
  postMessage: (msg: unknown) => void;
  addEventListener: (
    type: "message",
    listener: (event: { data: unknown }) => void,
  ) => void;
}

function getWebView(): WebView2Like | undefined {
  return (
    window as unknown as { chrome?: { webview?: WebView2Like } }
  ).chrome?.webview;
}

type Pending = {
  resolve: (value: unknown) => void;
  reject: (reason: Error) => void;
};

const pending = new Map<string, Pending>();
let listenerInstalled = false;
let counter = 0;

function ensureListener() {
  if (listenerInstalled) return;
  const webview = getWebView();
  if (!webview) return;

  webview.addEventListener("message", (event) => {
    const data = event.data as BridgeResponse;
    if (!data || typeof data !== "object" || !("requestId" in data)) return;

    const entry = pending.get(data.requestId);
    if (!entry) return;
    pending.delete(data.requestId);

    if (data.ok) entry.resolve(data.result);
    else entry.reject(new Error(data.error ?? "bridge call failed"));
  });

  listenerInstalled = true;
}

/** True when running inside the WebView2 host (vs. a plain browser tab). */
export function hasBridge(): boolean {
  return getWebView() !== undefined;
}

/** Invoke a C# bridge method, resolving with its (already-deserialized) result. */
export function invoke<T = unknown>(
  method: string,
  args?: Record<string, unknown>,
): Promise<T> {
  const webview = getWebView();
  if (!webview) {
    return Promise.reject(
      new Error("Not running inside WebView2 (no bridge available)."),
    );
  }

  ensureListener();

  const requestId = `r${++counter}-${Date.now()}`;
  return new Promise<T>((resolve, reject) => {
    pending.set(requestId, {
      resolve: resolve as (v: unknown) => void,
      reject,
    });

    // Time out so a dropped message never wedges a button forever.
    setTimeout(() => {
      if (pending.has(requestId)) {
        pending.delete(requestId);
        reject(new Error(`bridge call '${method}' timed out`));
      }
    }, 15000);

    webview.postMessage({ requestId, method, args: args ?? {} });
  });
}
