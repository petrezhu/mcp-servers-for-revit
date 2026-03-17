import http from "node:http";

const host = process.env.MOCK_OPENAI_HOST ?? "127.0.0.1";
const port = Number(process.env.MOCK_OPENAI_PORT ?? 4010);

function sendJson(response, statusCode, payload) {
  response.writeHead(statusCode, { "content-type": "application/json" });
  response.end(JSON.stringify(payload));
}

function readJson(request) {
  return new Promise((resolve, reject) => {
    let body = "";

    request.on("data", (chunk) => {
      body += chunk;
    });

    request.on("end", () => {
      if (!body) {
        resolve({});
        return;
      }

      try {
        resolve(JSON.parse(body));
      } catch (error) {
        reject(error);
      }
    });

    request.on("error", reject);
  });
}

function createResponsePayload(model, promptText) {
  return {
    id: "resp_mock_123",
    object: "response",
    created_at: Math.floor(Date.now() / 1000),
    status: "completed",
    model,
    output: [
      {
        type: "message",
        id: "msg_mock_123",
        status: "completed",
        role: "assistant",
        content: [
          {
            type: "output_text",
            text: `mock-response:${promptText || "ok"}`,
          },
        ],
      },
    ],
    usage: {
      input_tokens: 1,
      output_tokens: 1,
      total_tokens: 2,
    },
  };
}

function extractPromptText(body) {
  if (typeof body.input === "string") {
    return body.input;
  }

  if (Array.isArray(body.input)) {
    return body.input
      .map((item) => {
        if (typeof item === "string") {
          return item;
        }

        if (item?.content && Array.isArray(item.content)) {
          return item.content
            .map((contentItem) => contentItem?.text ?? "")
            .join(" ");
        }

        return item?.text ?? "";
      })
      .join(" ")
      .trim();
  }

  if (Array.isArray(body.messages)) {
    return body.messages
      .map((message) => message?.content ?? "")
      .join(" ")
      .trim();
  }

  return "";
}

const server = http.createServer(async (request, response) => {
  try {
    if (request.method === "GET" && request.url === "/health") {
      sendJson(response, 200, { ok: true });
      return;
    }

    if (request.method === "GET" && request.url === "/v1/models") {
      sendJson(response, 200, {
        object: "list",
        data: [
          {
            id: "mock-gpt-5-codex",
            object: "model",
            created: Math.floor(Date.now() / 1000),
            owned_by: "local-mock",
          },
        ],
      });
      return;
    }

    if (request.method === "POST" && request.url === "/v1/responses") {
      const body = await readJson(request);
      const model = body.model ?? "mock-gpt-5-codex";
      const promptText = extractPromptText(body);
      sendJson(response, 200, createResponsePayload(model, promptText));
      return;
    }

    if (request.method === "POST" && request.url === "/v1/chat/completions") {
      const body = await readJson(request);
      const model = body.model ?? "mock-gpt-5-codex";
      const promptText = extractPromptText(body);
      sendJson(response, 200, {
        id: "chatcmpl_mock_123",
        object: "chat.completion",
        created: Math.floor(Date.now() / 1000),
        model,
        choices: [
          {
            index: 0,
            finish_reason: "stop",
            message: {
              role: "assistant",
              content: `mock-response:${promptText || "ok"}`,
            },
          },
        ],
        usage: {
          prompt_tokens: 1,
          completion_tokens: 1,
          total_tokens: 2,
        },
      });
      return;
    }

    sendJson(response, 404, { error: `No route for ${request.method} ${request.url}` });
  } catch (error) {
    sendJson(response, 500, {
      error: error instanceof Error ? error.message : String(error),
    });
  }
});

server.listen(port, host, () => {
  console.log(`Mock OpenAI server listening on http://${host}:${port}`);
});
