import { execSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const wranglerPath = path.resolve(root, "..", "wrangler.toml");
const NAMESPACE_TITLE = "tpd-arena-arenas";

function run(cmd) {
  try {
    const out = execSync(cmd, { encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] });
    return { ok: true, out: out.trim() };
  } catch (error) {
    const out = `${error.stdout || ""}${error.stderr || ""}`.trim();
    return { ok: false, out, code: error.status };
  }
}

function parseNamespaceList(raw) {
  if (!raw) return [];
  const jsonMatch = raw.match(/\[[\s\S]*\]/);
  if (!jsonMatch) {
    console.warn("Could not find JSON in wrangler kv namespace list output.");
    return [];
  }
  try {
    const parsed = JSON.parse(jsonMatch[0]);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    console.warn("Could not parse KV namespace list JSON.");
    return [];
  }
}

function listNamespaces() {
  const result = run("npx wrangler kv namespace list");
  if (!result.ok) {
    console.error("wrangler kv namespace list failed:");
    console.error(result.out);
    return { namespaces: [], error: result.out };
  }
  return { namespaces: parseNamespaceList(result.out), error: null };
}

function findNamespace(namespaces) {
  return namespaces.find((ns) => ns.title === NAMESPACE_TITLE);
}

function ensureNamespace() {
  let { namespaces, error: listError } = listNamespaces();
  let found = findNamespace(namespaces);

  if (found?.id) {
    console.log(`Found existing KV namespace "${NAMESPACE_TITLE}" (${found.id}).`);
    return found.id;
  }

  console.log(`Creating KV namespace "${NAMESPACE_TITLE}"...`);
  const create = run(`npx wrangler kv namespace create "${NAMESPACE_TITLE}"`);

  if (!create.ok) {
    const alreadyExists = /already exists|10014|duplicate/i.test(create.out);
    if (alreadyExists) {
      console.log("Namespace may already exist, re-listing...");
      ({ namespaces } = listNamespaces());
      found = findNamespace(namespaces);
      if (found?.id) return found.id;
    }

    console.error("wrangler kv namespace create failed:");
    console.error(create.out);
    if (listError) {
      console.error("Earlier list error:");
      console.error(listError);
    }
    throw new Error(
      `Failed to create KV namespace "${NAMESPACE_TITLE}". ` +
        "Ensure API token has Workers KV Storage Edit permission, " +
        "or set ARENA_KV_NAMESPACE_ID GitHub secret with an existing namespace id.",
    );
  }

  // Parse id from create output or re-list
  const idMatch = create.out.match(/id\s*=\s*"([^"]+)"/i) || create.out.match(/"id"\s*:\s*"([^"]+)"/);
  if (idMatch?.[1]) {
    console.log(`Created KV namespace (${idMatch[1]}).`);
    return idMatch[1];
  }

  ({ namespaces } = listNamespaces());
  found = findNamespace(namespaces);
  if (found?.id) {
    console.log(`Created KV namespace (${found.id}).`);
    return found.id;
  }

  throw new Error(`Failed to resolve KV namespace "${NAMESPACE_TITLE}" after create.`);
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
  console.log(`Patched wrangler.toml ARENA_KV id: ${namespaceId}`);
}

const fromEnv = process.env.ARENA_KV_NAMESPACE_ID?.trim();
if (fromEnv) {
  console.log(`Using ARENA_KV_NAMESPACE_ID from environment.`);
  patchWranglerKvId(fromEnv);
} else {
  const id = ensureNamespace();
  patchWranglerKvId(id);
}
