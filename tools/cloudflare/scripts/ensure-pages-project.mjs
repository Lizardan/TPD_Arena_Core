/**
 * Ensures the Cloudflare Pages project exists before first deploy.
 * wrangler pages deploy fails in CI (non-interactive) if the project was never created.
 */
import { execSync } from "node:child_process";

const projectName = process.env.PAGES_PROJECT_NAME || "tpd-arena";
const productionBranch = process.env.PAGES_PRODUCTION_BRANCH || "main";

function run(cmd) {
  try {
    const out = execSync(cmd, { encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] });
    return { ok: true, out: out.trim() };
  } catch (error) {
    const out = `${error.stdout || ""}${error.stderr || ""}`.trim();
    return { ok: false, out, code: error.status };
  }
}

// #region agent log
console.log(
  JSON.stringify({
    sessionId: "a89d64",
    hypothesisId: "A",
    location: "ensure-pages-project.mjs",
    message: "ensure_project_start",
    data: { projectName, productionBranch },
    timestamp: Date.now(),
  }),
);
// #endregion

const create = run(
  `npx wrangler pages project create ${projectName} --production-branch=${productionBranch}`,
);

// #region agent log
console.log(
  JSON.stringify({
    sessionId: "a89d64",
    hypothesisId: "A",
    location: "ensure-pages-project.mjs",
    message: "create_project_result",
    data: { ok: create.ok, code: create.code, output: create.out.slice(0, 800) },
    timestamp: Date.now(),
  }),
);
// #endregion

if (create.ok) {
  console.log(`Created Pages project "${projectName}".`);
  process.exit(0);
}

if (/already exists|8000004|duplicate/i.test(create.out)) {
  console.log(`Pages project "${projectName}" already exists.`);
  process.exit(0);
}

console.error("Failed to ensure Pages project exists:");
console.error(create.out);
process.exit(1);
