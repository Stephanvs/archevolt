/**
 * The landing page's sample program and the code the generator writes for it.
 *
 * sample/Program.cs is the whole program. The page shows it in pieces, cut here at their first lines, so the
 * pieces cannot drift from a file that compiles. sample/Motion.Move.Each.g.cs is what the generator emits for
 * Motion.Move in a Release build, with namespace qualifiers removed and indentation added. Rebuild it with
 * `node scripts/home-sample.mjs` after the generator or the program changes.
 */
import generatedSource from "./sample/Motion.Move.Each.g.cs?raw";
import programSource from "./sample/Program.cs?raw";

const program = programSource.replaceAll("\r\n", "\n");

/** The text from the first `from` up to the next `to`, or to the end of the program. */
function cut(from: string, to?: string): string {
  const start = program.indexOf(from);
  const end = to ? program.indexOf(to, start) : program.length;
  if (start < 0 || end < 0) throw new Error(`The sample program has no "${start < 0 ? from : to}".`);
  return program.slice(start, end).trim();
}

/** Removes the indentation that every line of `code` shares. */
function dedent(code: string): string {
  const lines = code.split("\n");
  const indent = Math.min(...lines.filter((line) => line.trim()).map((line) => line.length - line.trimStart().length));
  return lines.map((line) => line.slice(indent)).join("\n");
}

/** The two components and the system that moves them. */
export const system = cut("[Component]", "[Bundle]");

/** The bundle, the module, and the schedule. */
export const wiring = cut("[Bundle]", "public static class Program");

/** The statements of Main, from its first line to the brace that closes it. */
const mainStart = program.indexOf("        using var world");
const mainEnd = program.indexOf("\n    }", mainStart);
if (mainStart < 0 || mainEnd < 0) throw new Error("The sample program has no Main that builds a world.");
export const run = dedent(program.slice(mainStart, mainEnd));

export const generated = {
  name: "Motion.Move.Each.g.cs",
  code: generatedSource.replaceAll("\r\n", "\n").trimEnd(),
};

/**
 * The zero-based numbers of the lines that contain `markers`. Each marker is searched for after the line of
 * the one before it, so a text that occurs twice can be told apart by order. Throws when a marker is
 * missing, so a change to the program or to the generator fails the build instead of lighting the wrong line.
 */
export function linesOf(code: string, ...markers: string[]): number[] {
  const lines = code.split("\n");
  let from = 0;
  return markers.map((marker) => {
    const index = lines.findIndex((line, number) => number >= from && line.includes(marker));
    if (index < 0) throw new Error(`No line after line ${from} contains "${marker}".`);
    from = index + 1;
    return index;
  });
}

interface Hast {
  properties: Record<string, unknown>;
}

interface TransformerContext {
  addClassToHast(node: Hast, className: string): void;
}

/**
 * A Shiki transformer for `code`. It numbers each line with a zero-based data-line, marks the `lit` lines
 * with is-lit, names the part a line belongs to in data-part, and records the line's indentation in --indent
 * so a wrapped line can continue past it. The pre is not a tab stop, because the box around it wraps the code
 * or scrolls it.
 */
export function markLines(code: string, lit: number[], parts: Record<number, string> = {}) {
  const indents = code.split("\n").map((line) => line.length - line.trimStart().length);
  return {
    name: "home-mark-lines",
    pre(node: Hast) {
      delete node.properties.tabindex;
    },
    line(this: TransformerContext, node: Hast, line: number) {
      const index = line - 1;
      node.properties["data-line"] = index;
      node.properties.style = `--indent:${indents[index] ?? 0}`;
      if (index in parts) node.properties["data-part"] = parts[index];
      if (lit.includes(index)) this.addClassToHast(node, "is-lit");
    },
  };
}

/**
 * A Shiki transformer for the page's code windows. The dark code theme draws comments in #6A737D, which is
 * 3.7:1 against the windows' panel color. This lightens them to #8B949E, 5.8:1.
 */
export const lightenComments = {
  name: "home-lighten-comments",
  span(node: Hast) {
    if (node.properties.style === "color:#6A737D") node.properties.style = "color:#8B949E";
  },
};

/** Split text on backticks so inline code renders as <code>. */
export const segments = (text: string) => text.split("`").map((part, index) => ({ code: index % 2 === 1, part }));
