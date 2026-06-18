export interface Env {
  TELEGRAM_BOT_TOKEN: string;
  WEB_APP_URL: string;
  ARENA_KV: KVNamespace;
  STATS_API_URL?: string;
}

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

export function errorResponse(message: string, status: number): Response {
  return jsonResponse({ detail: message }, status);
}
