import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const cli = join(import.meta.dirname, "../node_modules/blume/bin/blume.mjs");
const child = spawn(process.execPath, [cli, "build"], { stdio: ["inherit", "pipe", "pipe"] });
let log = "";
for (const [stream, target] of [[child.stdout, process.stdout], [child.stderr, process.stderr]]) {
  stream.on("data", (chunk) => {
    log += chunk.toString();
    target.write(chunk);
  });
}
child.on("error", (error) => {
  console.error(error.message);
  process.exitCode = 1;
});
child.on("close", (code) => {
  mkdirSync("artifacts/logs", { recursive: true });
  writeFileSync("artifacts/logs/docs-build.txt", log);
  if (/BLUME_SOURCE_(?:UNAVAILABLE|OFFLINE)/u.test(log)) {
    console.error("The deploy build could not refresh its GitHub release notes. Refusing to publish stale or empty changelog content.");
    process.exitCode = 1;
  } else {
    process.exitCode = code ?? 1;
  }
});
