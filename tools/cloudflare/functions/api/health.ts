import type { Env } from "../lib/env";
import { jsonResponse } from "../lib/env";

export const onRequestGet: PagesFunction<Env> = async (context) => {
  return jsonResponse({
    ok: true,
    kv: Boolean(context.env.ARENA_KV),
  });
};
