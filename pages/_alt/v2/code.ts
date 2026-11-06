/**
 * Build-time helpers for the round two code comparisons: the source declarations, cut from the landing page
 * snippets so they cannot drift from them, and a Shiki transformer that numbers and marks lines.
 */
import { heroCode, moduleCode } from "../evidence";

interface Hast {
  properties: Record<string, unknown>;
}

interface TransformerContext {
  addClassToHast(node: Hast, className: string): void;
}

/** Numbers each line with a zero-based data-line attribute and marks the `lit` lines with is-lit. */
export function markLines(lit: number[] = []) {
  return {
    name: "alt-v2-mark-lines",
    line(this: TransformerContext, node: Hast, line: number) {
      node.properties["data-line"] = line - 1;
      if (lit.includes(line - 1)) this.addClassToHast(node, "is-lit");
    },
  };
}

const cut = (code: string, from: string, to?: string) => {
  const start = code.indexOf(from);
  if (start < 0) throw new Error(`Snippet has no "${from}"`);
  const end = to ? code.indexOf(to, start) : code.length;
  return code.slice(start, end).trim();
};

export const source = {
  components: cut(heroCode, "[Component]", "public static partial class Motion"),
  move: cut(heroCode, "public static partial class Motion"),
  bundle: cut(moduleCode, "[Bundle]", "[EcsModule"),
  module: cut(moduleCode, "[EcsModule", "[Schedule"),
  schedule: cut(moduleCode, "[Schedule"),
};

/** Split text on backticks so inline code renders as <code>. */
export const segments = (text: string) => text.split("`").map((part, index) => ({ code: index % 2 === 1, part }));

/** Print a measurement at the precision it was recorded with: 22.0 stays 22.0, 83.06 stays 83.06. */
export const us = (value: number) => (Number.isInteger(value) ? value.toFixed(1) : String(value));
