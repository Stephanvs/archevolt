/** The landing page alternatives under /alt, in review order. Round 2 combines the picks from the round 1 review. */
export interface Alternative {
  round: 1 | 2;
  slug: string;
  name: string;
  origin: string;
  shape: string;
  summary: string;
}

export const alternatives: Alternative[] = [
  {
    round: 1,
    slug: "ladder",
    name: "Question ladder",
    origin: "Candidate 1 · Claude Opus 5.5",
    shape: "Eight sections, one visitor question each, in the order a .NET developer asks them.",
    summary:
      "Each section answers one question and is worth reading only after the previous one got a yes. The hero names the problem, then code, compile time, performance, features, sample, fit, and start.",
  },
  {
    round: 1,
    slug: "spine",
    name: "Value spine",
    origin: "Candidate 2 · GPT-6.1 Sol",
    shape: "A hero and three value chapters. Code, numbers, and the sample sit inside the value they prove.",
    summary:
      "The page is the three values. Every feature card, chart, and sample becomes evidence inside one of them, and the hero links straight to each.",
  },
  {
    round: 1,
    slug: "experience",
    name: "Experience first",
    origin: "Candidate 3 · Grok 4.7",
    shape: "The running game comes first. Each later section shows an artifact, then names the value under it.",
    summary:
      "Show, then name. The visitor sees Archevolt running before reading a single claim, then follows the code, the numbers, the hosts, and the refusals behind it.",
  },
  {
    round: 1,
    slug: "judge-a",
    name: "Judge A's structure",
    origin: "Judge A · GPT-6.1 Sol · a refinement of the value spine",
    shape: "Three value chapters, folded tighter, with the ladder's fit facts and setup code grafted in.",
    summary:
      "The value spine with its catch-all third chapter pruned, compiler depth made optional, and the visible fit facts from the ladder.",
  },
  {
    round: 1,
    slug: "judge-b",
    name: "Judge B's structure",
    origin: "Judge B · Claude Opus 5.5 · a refinement of the question ladder",
    shape: "The question ladder with the grafts from both other candidates and both judges.",
    summary:
      "The ladder plus the status line, the real generated code, scoped performance wording, the browser build's revision note, and the fit list as structured data.",
  },
  {
    round: 1,
    slug: "story",
    name: "One argument",
    origin: "New shape · from your feedback on the values",
    shape: "One continuous argument. The values come first, and every technical detail follows from them.",
    summary:
      "Like TigerBeetle's page: the problem, why the usual answer fails, the decision Archevolt made, and the evidence, with performance, ergonomics, and flexibility as the reasons behind each detail.",
  },
  {
    round: 2,
    slug: "blocks",
    name: "Every declaration",
    origin: "Round 2 · the fullest page",
    shape: "Click any of the five declarations to open the file the generator writes for it.",
    summary:
      "Closest to the current page: setup, performance with the known gaps, headless, the feature cards, and the FAQ.",
  },
  {
    round: 2,
    slug: "lines",
    name: "Line by line",
    origin: "Round 2 · judge A's value chapters",
    shape: "Click a part of Motion.Move to light it up in your code and in the generated file.",
    summary:
      "The hero links to three chapters: plain C# (with build errors and setup), speed (the comparison, numbers, and gaps), and headless.",
  },
  {
    round: 2,
    slug: "steps",
    name: "Four steps",
    origin: "Round 2 · the shortest page",
    shape: "Step from your method to the loop, the kernel, and the measured result.",
    summary: "Four sections and the call to action. The numbers live in the last step. No FAQ, no feature cards.",
  },
];
