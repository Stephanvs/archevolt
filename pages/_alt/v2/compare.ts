/**
 * Clickable code comparisons on the round two pages.
 *
 * A [data-compare] root holds pick controls [data-pick="key"]. Picking one shows the matching
 * [data-panel="key"] and hides the others, and lights the lines named in the control's data-lit JSON
 * ({ "target name": [line, ...] }) inside each [data-target="target name"]. Lines carry data-line from
 * code.ts markLines. A [data-scroll] container scrolls its first lit line into view. Controls with
 * role="tab" get arrow key navigation. A [data-next] button picks the next control.
 */
export function mountCompare(): () => void {
  const controller = new AbortController();
  const { signal } = controller;
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /** The viewport x of the first non-blank character in a line, so deep indentation can be scrolled past. */
  const textLeft = (line: HTMLElement) => {
    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const index = node.textContent?.search(/\S/) ?? -1;
      if (index < 0) continue;
      const range = document.createRange();
      range.setStart(node, index);
      range.setEnd(node, index + 1);
      return range.getBoundingClientRect().left;
    }
    return undefined;
  };

  const reveal = (scope: HTMLElement, smooth: boolean) => {
    for (const scroller of scope.querySelectorAll<HTMLElement>("[data-scroll]")) {
      if (scroller.closest("[hidden]")) continue;
      const lit = [...scroller.querySelectorAll<HTMLElement>(".is-lit")];
      if (lit.length === 0) continue;
      const box = scroller.getBoundingClientRect();
      const top = scroller.scrollTop + lit[0].getBoundingClientRect().top - box.top - 48;
      const lefts = lit.map(textLeft).filter((left): left is number => left !== undefined);
      const indent = lefts.length > 0 ? scroller.scrollLeft + Math.min(...lefts) - box.left : 0;
      // Scroll sideways only past deep indentation, and keep a quarter of the width for the enclosing lines.
      const left = indent > box.width * 0.4 ? indent - box.width * 0.25 : 0;
      scroller.scrollTo({ top: Math.max(top, 0), left, behavior: smooth && !reduce ? "smooth" : "auto" });
    }
  };

  for (const root of document.querySelectorAll<HTMLElement>("[data-compare]")) {
    const picks = [...root.querySelectorAll<HTMLElement>("[data-pick]")];
    if (picks.length === 0) continue;
    const isTab = (pick: HTMLElement) => pick.getAttribute("role") === "tab";

    const select = (pick: HTMLElement, smooth: boolean) => {
      const key = pick.dataset.pick;
      for (const other of picks) {
        const on = other === pick;
        other.setAttribute(isTab(other) ? "aria-selected" : "aria-pressed", String(on));
        if (isTab(other)) other.tabIndex = on ? 0 : -1;
        other.closest("[data-pick-block]")?.classList.toggle("is-on", on);
      }
      for (const panel of root.querySelectorAll<HTMLElement>("[data-panel]")) panel.hidden = panel.dataset.panel !== key;
      if (pick.dataset.lit) {
        const lit = JSON.parse(pick.dataset.lit) as Record<string, number[]>;
        for (const target of root.querySelectorAll<HTMLElement>("[data-target]")) {
          const lines = lit[target.dataset.target ?? ""] ?? [];
          for (const line of target.querySelectorAll<HTMLElement>("[data-line]")) {
            line.classList.toggle("is-lit", lines.includes(Number(line.dataset.line)));
          }
        }
      }
      reveal(root, smooth);
    };

    root.addEventListener(
      "click",
      (event) => {
        const target = event.target as HTMLElement;
        const next = target.closest<HTMLElement>("[data-next]");
        if (next) {
          const current = picks.findIndex((pick) => pick.dataset.pick === next.dataset.next);
          const pick = picks[(current + 1) % picks.length];
          select(pick, true);
          pick.focus();
          return;
        }
        const block = target.closest<HTMLElement>("[data-pick-block]");
        const pick = target.closest<HTMLElement>("[data-pick]") ?? block?.querySelector<HTMLElement>("[data-pick]");
        if (pick && root.contains(pick)) select(pick, true);
      },
      { signal },
    );

    root.addEventListener(
      "keydown",
      (event) => {
        const pick = (event.target as HTMLElement).closest<HTMLElement>("[data-pick]");
        if (!pick || !isTab(pick)) return;
        const index = picks.indexOf(pick);
        const moves: Record<string, number> = { ArrowRight: index + 1, ArrowDown: index + 1, ArrowLeft: index - 1, ArrowUp: index - 1, Home: 0, End: picks.length - 1 };
        if (!(event.key in moves)) return;
        event.preventDefault();
        const nextPick = picks[(moves[event.key] + picks.length) % picks.length];
        select(nextPick, true);
        nextPick.focus();
      },
      { signal },
    );

    const initial = picks.find((pick) => pick.getAttribute("aria-pressed") === "true" || pick.getAttribute("aria-selected") === "true");
    select(initial ?? picks[0], false);
  }

  return () => controller.abort();
}
