import { RevitClientConnection } from "../utils/SocketClient.js";

const PRIMARY_CODE_COMMAND = "exec";
const LEGACY_CODE_COMMAND = "execute";

function isMethodNotFoundError(error: unknown): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  return /method\s+'(?:exec|execute)'\s+not\s+found/i.test(error.message);
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
