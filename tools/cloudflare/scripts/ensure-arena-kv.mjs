import { execSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const wranglerPath = path.resolve(root, "..", "wrangler.toml");
const NAMESPACE_TITLE = "tpd-arena-arenas";

function run(cmd) {
  return execSync(cmd, { encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] }).trim();
}

function listNamespaces() {
  try {
    const out = run("npx wrangler kv namespace list");
    return JSON.parse(out);
  } catch {
    return [];
  }
}

function ensureNamespace() {
  const namespaces = listNamespaces();
  let found = namespaces.find((ns) => ns.title === NAMESPACE_TITLE);
  if (!found) {
    run(`npx wrangler kv namespace create "${NAMESPACE_TITLE}"`);
    const updated = listNamespaces();
    found = updated.find((ns) => ns.title === NAMESPACE_TITLE);
  }
  if (!found?.id) {
    throw new Error(`Failed to resolve KV namespace "${NAMESPACE_TITLE}".`);
  }
  return found.id;
}

function patchWranglerKvId(namespaceId) {
  let content = fs.readFileSync(wranglerPath, "utf8");
  const bindingBlock = `[[kv_namespaces]]
binding = "ARENA_KV"
id = "${namespaceId}"
`;

  if (content.includes('binding = "ARENA_KV"')) {
    content = content.replace(
      /binding = "ARENA_KV"\s*\nid = "[^"]*"/,
      `binding = "ARENA_KV"\nid = "${namespaceId}"`,
    );
  } else {
    content = `${content.trim()}\n\n${bindingBlock}`;
  }

  fs.writeFileSync(wranglerPath, content);
  console.log(`ARENA_KV namespace id: ${namespaceId}`);
}

const id = ensureNamespace();
patchWranglerKvId(id);
