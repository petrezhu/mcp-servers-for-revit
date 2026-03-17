import { RevitClientConnection } from "./SocketClient.js";

// Mutex to serialize all Revit connections - prevents race conditions
// when multiple requests are made in parallel
let connectionMutex: Promise<void> = Promise.resolve();

function getRevitConnectionHost(): string {
  return process.env.REVIT_MCP_HOST?.trim() || "localhost";
}

function getRevitConnectionPort(): number {
  const rawPort = process.env.REVIT_MCP_PORT?.trim();
  const parsedPort = rawPort ? Number(rawPort) : 8080;

  return Number.isInteger(parsedPort) && parsedPort > 0 ? parsedPort : 8080;
}

/**
 * 连接到Revit客户端并执行操作
 * @param operation 连接成功后要执行的操作函数
 * @returns 操作的结果
 */
export async function withRevitConnection<T>(
  operation: (client: RevitClientConnection) => Promise<T>
): Promise<T> {
  // Wait for any pending connection to complete before starting a new one
  const previousMutex = connectionMutex;
  let releaseMutex: () => void;
  connectionMutex = new Promise<void>((resolve) => {
    releaseMutex = resolve;
  });
  await previousMutex;

  const revitClient = new RevitClientConnection(
    getRevitConnectionHost(),
    getRevitConnectionPort()
  );

  try {
    // 连接到Revit客户端
    if (!revitClient.isConnected) {
      await new Promise<void>((resolve, reject) => {
        let settled = false;

        const cleanup = () => {
          revitClient.socket.removeListener("connect", onConnect);
          revitClient.socket.removeListener("error", onError);
          clearTimeout(timeoutId);
        };

        const onConnect = () => {
          if (settled) {
            return;
          }

          settled = true;
          cleanup();
          resolve();
        };

        const onError = (error: any) => {
          if (settled) {
            return;
          }

          settled = true;
          cleanup();
          reject(new Error("connect to revit client failed"));
        };

        revitClient.socket.on("connect", onConnect);
        revitClient.socket.on("error", onError);

        revitClient.connect();

        const timeoutId = setTimeout(() => {
          if (settled) {
            return;
          }

          settled = true;
          cleanup();
          reject(new Error("连接到Revit客户端失败"));
        }, 5000);
      });
    }

    // 执行操作
    return await operation(revitClient);
  } finally {
    // 断开连接
    revitClient.disconnect();
    // Release the mutex so the next request can proceed
    releaseMutex!();
  }
}
