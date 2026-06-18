import type { Env } from "../lib/env";
import { jsonResponse } from "../lib/env";

export const onRequestGet: PagesFunction<Env> = async () => {
  return jsonResponse({ ok: true });
};
