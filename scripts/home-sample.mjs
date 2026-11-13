/**
 * Rebuilds the generator output that the landing page shows next to its sample program.
 *
 * Compiles pages/_home/sample/Program.cs in a Release build against the generator in src, and writes what the
 * generator emitted for Motion.Move to pages/_home/sample/Motion.Move.Each.g.cs. The copy differs from the
 * emitted file in two ways: namespace qualifiers are removed, and the lines are indented.
 *
 * Run it after a change to the generator or to the sample program:
 *   node scripts/home-sample.mjs
 */
import { execFileSync } from "node:child_process";
import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { basename, join } from "node:path";

const root = join(import.meta.dirname, "..");
const sample = join(root, "pages/_home/sample");
const work = join(root, "artifacts/home-sample");
const emitted = join(work, "generated");
const output = join(sample, "Motion.Move.Each.g.cs");

rmSync(work, { recursive: true, force: true });
mkdirSync(work, { recursive: true });
writeFileSync(
  join(work, "HomeSample.csproj"),
  `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>${emitted}</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${join(sample, "Program.cs")}" />
    <ProjectReference Include="${join(root, "src/Archevolt/Archevolt.csproj")}" />
    <ProjectReference Include="${join(root, "src/Archevolt.Generators/Archevolt.Generators.csproj")}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
`,
);

execFileSync("dotnet", ["build", join(work, "HomeSample.csproj"), "-c", "Release", "--nologo", "-v", "q"], { stdio: "inherit" });

// The emitted file name carries a hash of the method's signature: Motion.Move.<hash>.Each.g.cs.
const matches = readdirSync(emitted, { recursive: true }).filter((file) => /^Motion\.Move\.[0-9a-f]+\.Each\.g\.cs$/u.test(basename(file)));
if (matches.length !== 1) throw new Error(`Expected one generated file for Motion.Move, found ${matches.length}.`);

const literal = /"(?:[^"\\]|\\.)*"/gu;
const qualifier = /global::(?:System\.Runtime\.(?:CompilerServices|InteropServices|Intrinsics)\.|System\.|Archevolt\.)?/gu;

/** Applies `change` to the code of a line and leaves its string literals as they are. */
function outsideLiterals(line, change) {
  let result = "";
  let last = 0;
  for (const match of line.matchAll(literal)) {
    result += change(line.slice(last, match.index)) + match[0];
    last = match.index + match[0].length;
  }
  return result + change(line.slice(last));
}

const count = (text, character) => text.split(character).length - 1;

let depth = 0;
let braceless = false;
const lines = [];
for (const emittedLine of readFileSync(join(emitted, matches[0]), "utf8").replace(/^﻿/u, "").trimEnd().split(/\r?\n/u)) {
  const line = outsideLiterals(emittedLine.trim(), (code) => code.replace(qualifier, ""));
  const code = line.replace(literal, '""');
  let level = depth - (code.match(/^\}+/u)?.[0].length ?? 0);
  if (line.startsWith("#")) level = 0;
  // A line that continues the expression above it, or the one statement of a loop without braces.
  else if (line.startsWith(".") || (braceless && !line.startsWith("{"))) level += 1;
  lines.push("    ".repeat(level) + line);
  depth += count(code, "{") - count(code, "}");
  braceless = /^(?:for|if|while|else)\b/u.test(line) && !/[{;]$/u.test(line);
}
if (depth !== 0) throw new Error("The generated file's braces do not balance, so its indentation would be wrong.");

writeFileSync(output, `${lines.join("\n")}\n`);
console.log(`Wrote ${lines.length} lines to ${output}`);
