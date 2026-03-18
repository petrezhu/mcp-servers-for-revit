import { RevitClientConnection } from "../utils/SocketClient.js";

const PRIMARY_CODE_COMMAND = "exec";
const LEGACY_CODE_COMMAND = "execute";

function isMethodNotFoundErrorForMethod(
  error: unknown,
  method: string
): boolean {
  if (!(error instanceof Error) || !method) {
    return false;
  }

  const escapedMethod = method.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

  return (
    new RegExp(`method\\s+'${escapedMethod}'\\s+not\\s+found`, "i").test(
      error.message
    ) ||
    new RegExp(`method\\s+not\\s+found\\s*:\\s*'${escapedMethod}'`, "i").test(
      error.message
    ) ||
    new RegExp(`未找到方法\\s*:\\s*'${escapedMethod}'`, "i").test(error.message)
  );
}

function isMethodNotFoundError(error: unknown): boolean {
  return (
    isMethodNotFoundErrorForMethod(error, PRIMARY_CODE_COMMAND) ||
    isMethodNotFoundErrorForMethod(error, LEGACY_CODE_COMMAND)
  );
}

export async function sendCodeExecutionCommand(
  revitClient: RevitClientConnection,
  params: unknown
) {
  try {
    return await revitClient.sendCommand(PRIMARY_CODE_COMMAND, params);
  } catch (error) {
    if (!isMethodNotFoundError(error)) {
      throw error;
    }

    return await revitClient.sendCommand(LEGACY_CODE_COMMAND, params);
  }
}
