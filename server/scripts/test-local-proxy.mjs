const baseUrl = process.env.LOCAL_PROXY_BASE_URL ?? "http://127.0.0.1:18317/v1";
const apiKey = process.env.LOCAL_PROXY_API_KEY ?? "sk-local-proxy";
const model = process.env.LOCAL_PROXY_MODEL ?? "gpt-5.4";
const expectedOutputPrefix =
  process.env.LOCAL_PROXY_EXPECTED_OUTPUT_PREFIX ?? "mock-response:";

async function readJson(path, init = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${apiKey}`,
      "Content-Type": "application/json",
      ...(init.headers ?? {}),
    },
  });

  const payload = await response.json();
  if (!response.ok) {
    throw new Error(
      `${init.method ?? "GET"} ${path} failed: ${response.status} ${JSON.stringify(payload)}`
    );
  }

  return payload;
}

function getOutputText(payload) {
  return payload?.output?.[0]?.content?.[0]?.text ?? "";
}

async function main() {
  const modelsPayload = await readJson("/models");
  const modelIds = (modelsPayload.data ?? []).map((entry) => entry.id);

  if (!modelIds.includes(model)) {
    throw new Error(`Model '${model}' not found in ${JSON.stringify(modelIds)}`);
  }

  const responsesPayload = await readJson("/responses", {
    method: "POST",
    body: JSON.stringify({
      model,
      input: "local proxy smoke test",
    }),
  });
  const outputText = getOutputText(responsesPayload);

  if (!outputText.startsWith(expectedOutputPrefix)) {
    throw new Error(
      `Unexpected response text '${outputText}', expected prefix '${expectedOutputPrefix}'`
    );
  }

  console.log(
    JSON.stringify(
      {
        success: true,
        baseUrl,
        model,
        outputText,
      },
      null,
      2
    )
  );
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
