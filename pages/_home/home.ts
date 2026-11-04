/**
 * Landing page motion. One entry point, mountHome(), returns a teardown so Astro's
 * client router can swap pages cleanly.
 *
 * - Hero: a canvas "world" of entities streaming through four archetype tables.
 *   Batches migrate between tables, labelled with the component they gain or lose.
 * - Stories: each feature visual acts out what it does on a loop, only while visible.
 * - Reveals, tagline words, typed commands, counters, and copy buttons.
 */

type Teardown = () => void;

const reduceMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;
const clamp01 = (value: number) => Math.min(1, Math.max(0, value));
const easeInOut = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);
const easeOut = (t: number) => 1 - (1 - t) ** 3;
const toggle = (element: Element | null | undefined, name: string, on: boolean) => {
  if (element && element.classList.contains(name) !== on) element.classList.toggle(name, on);
};

export function mountHome(): Teardown {
  const root = document.querySelector<HTMLElement>("[data-home]");
  if (!root) return () => {};

  const reduced = reduceMotion();
  root.classList.add("is-enhanced");
  const frame = requestAnimationFrame(() => root.classList.add("is-loaded"));

  const teardowns: Teardown[] = [
    () => cancelAnimationFrame(frame),
    mountReveals(root, reduced),
    mountCopy(root),
    mountTagline(root, reduced),
    mountField(root, reduced),
    mountStories(root, reduced),
  ];
  return () => teardowns.forEach((teardown) => teardown());
}

/* Reveals, counters, and typed commands */

function mountReveals(root: HTMLElement, reduced: boolean): Teardown {
  const timers: number[] = [];
  const targets = root.querySelectorAll<HTMLElement>("[data-reveal], [data-scene]");

  const onEnter = (element: HTMLElement) => {
    element.classList.add("is-in");
    if (element.matches("[data-scene]")) {
      timers.push(window.setTimeout(() => element.classList.add("is-settled"), 1600));
    }
    if (!reduced && element.matches("[data-bars]")) countUp(element);
    element.querySelectorAll<HTMLElement>("[data-type-target]").forEach((code) => {
      if (!reduced) typeCommand(code, timers);
    });
  };

  if (reduced) {
    targets.forEach((element) => onEnter(element));
    return () => {};
  }

  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue;
        onEnter(entry.target as HTMLElement);
        observer.unobserve(entry.target);
      }
    },
    { rootMargin: "0px 0px -12% 0px", threshold: 0.15 },
  );
  targets.forEach((element) => observer.observe(element));

  return () => {
    observer.disconnect();
    timers.forEach((timer) => window.clearTimeout(timer));
  };
}

function countUp(scope: HTMLElement) {
  const cells = [...scope.querySelectorAll<HTMLElement>("[data-count]")];
  const start = performance.now();
  const duration = 1100;
  const step = (now: number) => {
    const k = easeOut(clamp01((now - start) / duration));
    for (const cell of cells) {
      const target = Number(cell.dataset.count);
      cell.textContent = (target * k).toFixed(2);
    }
    if (k < 1) requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

function typeCommand(code: HTMLElement, timers: number[]) {
  const text = code.dataset.typeTarget ?? "";
  const install = code.closest(".install");
  code.textContent = "";
  install?.classList.add("is-typing");
  let index = 0;
  const next = () => {
    index += 1;
    code.textContent = text.slice(0, index);
    if (index < text.length) {
      timers.push(window.setTimeout(next, 45 + Math.random() * 25));
    } else {
      install?.classList.remove("is-typing");
      install?.classList.add("is-caret-idle");
      timers.push(window.setTimeout(() => install?.classList.remove("is-caret-idle"), 3200));
    }
  };
  timers.push(window.setTimeout(next, 400));
}

/* Copy buttons */

function mountCopy(root: HTMLElement): Teardown {
  const onClick = async (event: Event) => {
    const button = (event.target as Element | null)?.closest<HTMLButtonElement>("button[data-copy]");
    if (!button) return;
    const status = button.closest("[data-install]")?.querySelector("[data-copy-status]");
    try {
      await navigator.clipboard.writeText(button.dataset.copy ?? "");
      button.classList.add("is-copied");
      if (status) status.textContent = "Command copied to the clipboard.";
    } catch {
      if (status) status.textContent = "Copy failed. Select the command and copy it manually.";
    }
    window.setTimeout(() => {
      button.classList.remove("is-copied");
      if (status) status.textContent = "";
    }, 1800);
  };
  root.addEventListener("click", onClick);
  return () => root.removeEventListener("click", onClick);
}

/* Tagline: words light up one at a time as they cross a trigger line */

function mountTagline(root: HTMLElement, reduced: boolean): Teardown {
  const words = [...root.querySelectorAll<HTMLElement>("[data-word]")];
  if (reduced) {
    words.forEach((word) => word.classList.add("is-on"));
    return () => {};
  }
  let next = 0;
  let frame = 0;
  const activate = () => {
    frame = 0;
    const trigger = window.innerHeight * 0.72;
    let batch = 0;
    while (next < words.length && words[next].getBoundingClientRect().top < trigger) {
      words[next].style.transitionDelay = `${batch * 70}ms`;
      words[next].classList.add("is-on");
      next += 1;
      batch += 1;
    }
    if (next >= words.length) window.removeEventListener("scroll", onScroll);
  };
  const onScroll = () => {
    if (!frame) frame = requestAnimationFrame(activate);
  };
  window.addEventListener("scroll", onScroll, { passive: true });
  activate();
  return () => {
    window.removeEventListener("scroll", onScroll);
    if (frame) cancelAnimationFrame(frame);
  };
}

/* Hero field */

interface Lane {
  sig: string[];
  speed: number;
  color: number;
  count: number;
}

function mountField(root: HTMLElement, reduced: boolean): Teardown {
  const canvas = root.querySelector<HTMLCanvasElement>("[data-field]");
  const context = canvas?.getContext("2d");
  if (!canvas || !context) return () => {};
  const ctx: CanvasRenderingContext2D = context;
  const stage = canvas.parentElement as HTMLElement;
  const hudEntities = root.querySelector<HTMLElement>("[data-hud-entities]");
  const hudTick = root.querySelector<HTMLElement>("[data-hud-tick]");

  const lanes: Lane[] = [
    { sig: ["Position", "Velocity"], speed: 1, color: 0, count: 0 },
    { sig: ["Position", "Velocity", "Health"], speed: 0.72, color: 1, count: 0 },
    { sig: ["Position", "Health"], speed: 0, color: 2, count: 0 },
    { sig: ["Position", "Velocity", "Invulnerable"], speed: 1.35, color: 3, count: 0 },
  ];
  const share = [0.36, 0.3, 0.2, 0.14];

  let width = 0;
  let height = 0;
  let colors: string[] = [];
  let lineColor = "";
  let tableColor = "";
  let textColor = "";
  let accentColor = "";
  let monoFont = "";

  // Structure of arrays, naturally.
  let count = 0;
  let px = new Float32Array(0);
  let offset = new Float32Array(0);
  let jitter = new Float32Array(0);
  let lane = new Uint8Array(0);
  let fromLane = new Int8Array(0);
  let migrateAt = new Float32Array(0);

  interface Batch {
    from: number;
    to: number;
    start: number;
    members: number[];
    label: string;
  }
  let batches: Batch[] = [];
  let time = 0;
  let nextMigration = 1.2;
  let tick = 0;
  let hudClock = 0;

  const readColors = () => {
    const style = getComputedStyle(root);
    colors = ["--f-1", "--f-2", "--f-3", "--f-4"].map((name) => style.getPropertyValue(name).trim());
    lineColor = style.getPropertyValue("--f-line").trim();
    tableColor = style.getPropertyValue("--f-table").trim();
    textColor = style.getPropertyValue("--f-text").trim();
    accentColor = style.getPropertyValue("--f-accent").trim();
    monoFont = style.getPropertyValue("--h-mono").trim() || "monospace";
  };

  const geo = { gap: 12, tableHeight: 0, header: 24 };
  const measure = () => {
    const hud = 28;
    geo.tableHeight = (height - hud - geo.gap * (lanes.length - 1)) / lanes.length;
  };

  const laneY = (index: number, off: number) => {
    const top = index * (geo.tableHeight + geo.gap);
    return top + geo.header + 6 + off * Math.max(4, geo.tableHeight - geo.header - 12);
  };

  const seed = () => {
    count = Math.round(Math.min(1800, Math.max(420, (width * height) / 110)));
    px = new Float32Array(count);
    offset = new Float32Array(count);
    jitter = new Float32Array(count);
    lane = new Uint8Array(count);
    fromLane = new Int8Array(count).fill(-1);
    migrateAt = new Float32Array(count);
    lanes.forEach((entry) => (entry.count = 0));
    let cursor = 0;
    lanes.forEach((entry, index) => {
      const n = index === lanes.length - 1 ? count - cursor : Math.round(count * share[index]);
      for (let i = cursor; i < cursor + n; i++) {
        lane[i] = index;
        px[i] = Math.random() * width;
        offset[i] = Math.random();
        jitter[i] = 0.55 + Math.random() * 0.9;
      }
      entry.count = n;
      cursor += n;
    });
    batches = [];
    if (hudEntities) hudEntities.textContent = count.toLocaleString("en-US");
  };

  const resize = () => {
    const rect = stage.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    width = rect.width;
    height = rect.height;
    canvas.width = Math.round(width * dpr);
    canvas.height = Math.round(height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    measure();
    seed();
    draw();
  };

  const startMigration = () => {
    const sources = lanes.map((entry, index) => ({ index, count: entry.count })).filter((entry) => entry.count > count * 0.1);
    if (!sources.length) return;
    const from = sources[Math.floor(Math.random() * sources.length)].index;
    const targets = lanes
      .map((entry, index) => ({ index, weight: 1 / (1 + entry.count) }))
      .filter((entry) => entry.index !== from);
    const total = targets.reduce((sum, entry) => sum + entry.weight, 0);
    let pick = Math.random() * total;
    let to = targets[0].index;
    for (const entry of targets) {
      pick -= entry.weight;
      if (pick <= 0) {
        to = entry.index;
        break;
      }
    }
    const size = Math.round(Math.min(60, Math.max(14, count * 0.018)));
    // Take a contiguous run in x, so the batch reads as one group moving between tables.
    const anchor = Math.random() * width * 0.7 + width * 0.15;
    const candidates: number[] = [];
    for (let i = 0; i < count; i++) {
      if (lane[i] === from && fromLane[i] === -1) candidates.push(i);
    }
    candidates.sort((a, b) => Math.abs(px[a] - anchor) - Math.abs(px[b] - anchor));
    const members = candidates.slice(0, size);
    if (!members.length) return;
    for (const i of members) {
      fromLane[i] = from;
      migrateAt[i] = time + Math.random() * 0.15;
      lane[i] = to;
    }
    const added = lanes[to].sig.filter((name) => !lanes[from].sig.includes(name));
    const removed = lanes[from].sig.filter((name) => !lanes[to].sig.includes(name));
    const label = [...added.map((name) => `+${name}`), ...removed.map((name) => `−${name}`)].join(" ");
    batches.push({ from, to, start: time, members, label });
  };

  const migrationDuration = 1.1;

  const update = (dt: number) => {
    time += dt;
    tick += dt * 60;
    hudClock += dt;
    if (hudClock > 0.1 && hudTick) {
      hudClock = 0;
      hudTick.textContent = String(Math.floor(tick)).padStart(6, "0");
    }
    if (time >= nextMigration) {
      startMigration();
      nextMigration = time + 1.1 + Math.random() * 0.6;
    }
    const base = Math.max(40, width * 0.09);
    for (let i = 0; i < count; i++) {
      let speed = lanes[lane[i]].speed;
      if (fromLane[i] !== -1) {
        const p = clamp01((time - migrateAt[i]) / migrationDuration);
        speed = lanes[fromLane[i]].speed + (speed - lanes[fromLane[i]].speed) * p;
      }
      px[i] += speed * jitter[i] * base * dt;
      if (px[i] > width + 4) px[i] -= width + 8;
    }
    for (const batch of batches) {
      if (time - batch.start >= migrationDuration + 0.2) {
        for (const i of batch.members) fromLane[i] = -1;
        lanes[batch.from].count -= batch.members.length;
        lanes[batch.to].count += batch.members.length;
      }
    }
    batches = batches.filter((batch) => time - batch.start < migrationDuration + 0.2);
  };

  const draw = () => {
    ctx.clearRect(0, 0, width, height);
    const { gap, tableHeight, header } = geo;

    // Tables
    ctx.lineWidth = 1;
    lanes.forEach((entry, index) => {
      const top = index * (tableHeight + gap);
      ctx.fillStyle = tableColor;
      ctx.strokeStyle = lineColor;
      ctx.beginPath();
      ctx.roundRect(0.5, top + 0.5, width - 1, tableHeight - 1, 8);
      ctx.fill();
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(0, top + header);
      ctx.lineTo(width, top + header);
      ctx.stroke();

      ctx.font = `500 11px ${monoFont}`;
      ctx.textBaseline = "middle";
      ctx.fillStyle = colors[entry.color];
      ctx.fillRect(12, top + header / 2 - 3, 6, 6);
      ctx.fillStyle = textColor;
      ctx.textAlign = "left";
      ctx.fillText(entry.sig.join(" · "), 26, top + header / 2 + 0.5);
      ctx.textAlign = "right";
      const note = entry.speed === 0 ? "no Velocity, skipped by Move · " : "";
      ctx.fillText(`${note}${entry.count.toLocaleString("en-US")} rows`, width - 12, top + header / 2 + 0.5);
    });

    // Entities, batched per colour
    for (let c = 0; c < lanes.length; c++) {
      ctx.strokeStyle = colors[c];
      ctx.fillStyle = colors[c];
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      for (let i = 0; i < count; i++) {
        let y: number;
        let colorIndex = lanes[lane[i]].color;
        let speed = lanes[lane[i]].speed;
        if (fromLane[i] !== -1) {
          const p = easeInOut(clamp01((time - migrateAt[i]) / migrationDuration));
          y = laneY(fromLane[i], offset[i]) + (laneY(lane[i], offset[i]) - laneY(fromLane[i], offset[i])) * p;
          if (p < 0.5) colorIndex = lanes[fromLane[i]].color;
          speed = speed * p + lanes[fromLane[i]].speed * (1 - p);
        } else {
          y = laneY(lane[i], offset[i]);
        }
        if (colorIndex !== c) continue;
        const length = Math.max(1.5, speed * jitter[i] * 9);
        ctx.moveTo(px[i] - length, y);
        ctx.lineTo(px[i], y);
      }
      ctx.stroke();
    }

    // Migration labels
    ctx.font = `500 11px ${monoFont}`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    for (const batch of batches) {
      const age = time - batch.start;
      const alpha = Math.sin(clamp01(age / (migrationDuration + 0.2)) * Math.PI);
      let sx = 0;
      let sy = 0;
      for (const i of batch.members) {
        const p = easeInOut(clamp01((time - migrateAt[i]) / migrationDuration));
        sx += px[i];
        sy += laneY(batch.from, offset[i]) + (laneY(batch.to, offset[i]) - laneY(batch.from, offset[i])) * p;
      }
      const cx = Math.min(width - 70, Math.max(70, sx / batch.members.length));
      const cy = sy / batch.members.length;
      const text = batch.label;
      const w = ctx.measureText(text).width + 12;
      ctx.globalAlpha = alpha;
      ctx.fillStyle = accentColor;
      ctx.beginPath();
      ctx.roundRect(cx - w / 2, cy - 22, w, 18, 4);
      ctx.fill();
      ctx.fillStyle = "#000000";
      ctx.fillText(text, cx, cy - 13);
      ctx.globalAlpha = 1;
    }
  };

  readColors();
  resize();

  let frame = 0;
  let last = 0;
  let visible = false;
  const loop = (now: number) => {
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 0.016;
    last = now;
    update(dt);
    draw();
    frame = visible ? requestAnimationFrame(loop) : 0;
  };
  const start = () => {
    if (reduced || frame || !visible || document.hidden) return;
    last = 0;
    frame = requestAnimationFrame(loop);
  };
  const stop = () => {
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
  };

  const observer = new IntersectionObserver((entries) => {
    visible = entries[0]?.isIntersecting ?? false;
    if (visible) start();
    else stop();
  });
  observer.observe(stage);

  const resizeObserver = new ResizeObserver(() => resize());
  resizeObserver.observe(stage);

  const themeObserver = new MutationObserver(() => {
    readColors();
    draw();
  });
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme", "class"] });

  const onVisibility = () => (document.hidden ? stop() : start());
  document.addEventListener("visibilitychange", onVisibility);
  document.fonts?.ready.then(() => draw());

  return () => {
    stop();
    observer.disconnect();
    resizeObserver.disconnect();
    themeObserver.disconnect();
    document.removeEventListener("visibilitychange", onVisibility);
  };
}

/* Story loops */

interface Story {
  period: number;
  init?: (element: HTMLElement) => Teardown | void;
  frame: (element: HTMLElement, t: number) => void;
}

const stories: Record<string, Story> = {
  system: {
    period: 3.4,
    init(element) {
      // Pin the highlight to the line inside Motion.Move that the generated loop calls.
      const place = () => {
        const code = element.querySelector<HTMLElement>(".code");
        const line = [...element.querySelectorAll<HTMLElement>(".line")].find((item) => item.textContent?.includes("position with"));
        if (!code || !line) return;
        const codeBox = code.getBoundingClientRect();
        const lineBox = line.getBoundingClientRect();
        code.style.setProperty("--hot-top", `${lineBox.top - codeBox.top - 2}px`);
        code.style.setProperty("--hot-height", `${lineBox.height + 4}px`);
      };
      place();
      document.fonts?.ready.then(place);
      window.addEventListener("resize", place, { passive: true });
      return () => window.removeEventListener("resize", place);
    },
    frame(element, t) {
      toggle(element, "is-hot", t > 0.3 && t < 1.9);
    },
  },

  targets: {
    period: 3.6,
    frame(element, t) {
      const words = element.querySelectorAll("[data-target-word]");
      const active = Math.floor(t / 1.2);
      words.forEach((word, index) => toggle(word, "is-hot", index === active));
    },
  },

  query: {
    period: 6.4,
    frame(element, t) {
      toggle(element.querySelector("[data-decl]"), "is-eval", t > 0.1 && t < 4.9);
      element.querySelectorAll<HTMLElement>("[data-table]").forEach((table, k) => {
        const checkAt = 0.5 + k * 1.35;
        const checked = t >= checkAt && t < 5;
        const matched = table.dataset.match === "true";
        toggle(table, "is-checked", checked);
        toggle(table, "is-matched", checked && matched);
        toggle(table, "is-skipped", checked && !matched);
        table.querySelectorAll("[data-row]").forEach((row, i) => {
          toggle(row, "is-lit", matched && t >= checkAt + 0.2 + i * 0.14 && t < 5);
        });
      });
    },
  },

  storage: {
    period: 6,
    frame(element, t) {
      const columns = element.querySelectorAll(".vs-column");
      columns.forEach((column, c) => {
        column.querySelectorAll<HTMLElement>("[data-cell]").forEach((cell) => {
          const row = Number(cell.dataset.rowIndex);
          if (row < 7) {
            const at = 0.3 + row * 0.16;
            toggle(cell, "is-lit", t >= at && t < at + 0.55);
          } else {
            const filled = t >= 2.1 + c * 0.12 && t < 4.8;
            toggle(cell, "is-empty", !filled);
            toggle(cell, "is-spawned", filled && t < 3.3);
            toggle(cell, "is-lit", filled && t >= 3.3);
          }
        });
      });
      const counter = element.querySelector("[data-storage-count]");
      const rows = t >= 2.35 && t < 4.8 ? "8" : "7";
      if (counter && counter.textContent !== rows) counter.textContent = rows;
    },
  },

  schedule: {
    period: 5.4,
    frame(element, t) {
      const on = (start: number) => t >= start && t < 4.3;
      const nodes: Record<string, number> = { input: 0.2, motion: 0.9, steer: 0.9, collide: 1.6, cleanup: 3.0 };
      for (const [id, start] of Object.entries(nodes)) {
        toggle(element.querySelector(`[data-node="${id}"]`), "is-on", on(start));
      }
      element.querySelectorAll("[data-batch]").forEach((batch, index) => {
        toggle(batch, "is-on", on([0.2, 0.9, 1.6, 3.0][index]));
      });
      toggle(element.querySelector("[data-barrier]"), "is-flash", t >= 1.9 && t < 2.5);
      const travel: Record<string, [number, number]> = {
        "input-motion": [0.3, 0.9],
        "input-steer": [0.3, 0.9],
        "motion-collide": [1.0, 1.6],
        "steer-collide": [1.0, 1.6],
        "collide-cleanup": [2.4, 3.0],
      };
      for (const [id, [from, to]] of Object.entries(travel)) {
        const path = element.querySelector<SVGPathElement>(`[data-edge="${id}"]`);
        const pulse = element.querySelector<SVGCircleElement>(`[data-pulse="${id}"]`);
        if (!path || !pulse) continue;
        toggle(path, "is-hot", on(from));
        const moving = t >= from && t <= to;
        pulse.style.opacity = moving ? "1" : "0";
        if (moving) {
          const p = easeInOut((t - from) / (to - from));
          const point = path.getPointAtLength(path.getTotalLength() * p);
          pulse.setAttribute("cx", point.x.toFixed(2));
          pulse.setAttribute("cy", point.y.toFixed(2));
        }
      }
    },
  },

  exec: {
    period: 5.6,
    frame(element, t) {
      const durations = [0.95, 1.1, 0.85, 1.15];
      toggle(element, "is-clearing", t >= 4.4);
      element.querySelectorAll<HTMLElement>("[data-fill]").forEach((fill) => {
        const k = Number(fill.dataset.fill);
        const laneIndex = k % 4;
        const slot = Math.floor(k / 4);
        const start = 0.4 + slot * 1.3 + laneIndex * 0.07;
        const p = t >= 4.4 && t < 4.9 ? 1 : t >= 4.9 ? 0 : clamp01((t - start) / durations[laneIndex]);
        fill.style.transform = `scaleX(${p.toFixed(3)})`;
        const chunk = element.querySelector(`[data-chunk="${k}"]`);
        const live = t < 4.4;
        toggle(chunk, "is-taken", live && t >= start);
        toggle(chunk, "is-done", live && p >= 1);
      });
      toggle(element.querySelector("[data-barrier]"), "is-flash", t >= 3.3 && t < 4.1);
    },
  },

  messages: {
    period: 6.6,
    frame(element, t) {
      const clearing = t >= 4.6 && t < 5.4;
      toggle(element, "is-clearing", clearing);
      const writes = [0.5, 1.3, 2.1, 2.9];
      let written = 3 + writes.filter((w) => t >= w).length;
      let readerA = 3 + writes.filter((w) => t >= w + 0.35).length;
      let readerB = t >= 3.6 ? 7 : t >= 3.0 ? 4 : t >= 1.6 ? 2 : 1;
      if (clearing) written = 0;
      if (t >= 5.4) {
        written = 3;
        readerA = 3;
        readerB = 1;
      }
      toggle(element.querySelector("[data-rotate]"), "is-flash", t >= 4.0 && t < 4.7);
      element.querySelectorAll("[data-msg]").forEach((message, index) => {
        toggle(message, "is-on", index < written);
        toggle(message, "is-read-b", index < readerB);
        toggle(message, "is-read-a", index < readerA && index >= readerB);
      });
      const a = element.querySelector<HTMLElement>('[data-cursor="a"]');
      const b = element.querySelector<HTMLElement>('[data-cursor="b"]');
      a?.style.setProperty("--i", String(readerA));
      b?.style.setProperty("--i", String(readerB));
    },
  },

  aot: {
    period: 7.4,
    frame(element, t) {
      const command = element.querySelector<HTMLElement>("[data-command]");
      const text = command?.dataset.command ?? "";
      let shown = text.length;
      if (t >= 0.2 && t < 1.0) shown = Math.max(0, text.length - Math.floor((t - 0.2) / 0.018));
      else if (t >= 1.0 && t < 1.2) shown = 0;
      else if (t >= 1.2 && t < 3.6) shown = Math.min(text.length, Math.floor((t - 1.2) / 0.05));
      if (command && command.textContent?.length !== shown) command.textContent = text.slice(0, shown);
      toggle(element, "is-typing", t >= 0.2 && t < 3.6);
      element.querySelectorAll<HTMLElement>("[data-target]").forEach((target, k) => {
        const start = 3.7 + k * 0.85;
        const resetting = t >= 0.2 && t < start;
        const p = t < 0.2 ? 1 : resetting ? 0 : clamp01((t - start) / 0.7);
        target.style.setProperty("--p", easeInOut(p).toFixed(3));
        toggle(target, "is-done", p >= 1);
      });
    },
  },

  arcade: {
    period: 6.2,
    frame(element, t) {
      element.querySelectorAll("[data-callout]").forEach((callout, k) => {
        toggle(callout, "is-on", t >= 0.4 + k * 1.0 && t < 5.2);
      });
    },
  },
};

function mountStories(root: HTMLElement, reduced: boolean): Teardown {
  const entries = [...root.querySelectorAll<HTMLElement>("[data-story]")]
    .map((element, index) => ({ element, story: stories[element.dataset.story ?? ""], visible: false, time: 0, offset: (index % 3) * 0.7 }))
    .filter((entry) => entry.story);

  // Every story starts and rests on the design.
  const cleanups: Teardown[] = [];
  entries.forEach((entry) => {
    const cleanup = entry.story.init?.(entry.element);
    if (cleanup) cleanups.push(cleanup);
    entry.story.frame(entry.element, 0);
  });
  if (reduced) return () => cleanups.forEach((cleanup) => cleanup());

  const ready = (element: HTMLElement) => {
    const scene = element.closest("[data-scene], [data-reveal]");
    return !scene || scene.classList.contains("is-in");
  };

  let frame = 0;
  let last = 0;
  const loop = (now: number) => {
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 0;
    last = now;
    let active = false;
    for (const entry of entries) {
      if (!entry.visible || !ready(entry.element)) continue;
      active = true;
      entry.time += dt;
      // Let the card's own entrance land before its story starts.
      const local = entry.time - 0.9 - entry.offset;
      if (local < 0) continue;
      entry.story.frame(entry.element, local % entry.story.period);
    }
    frame = active ? requestAnimationFrame(loop) : 0;
  };
  const start = () => {
    if (frame || document.hidden) return;
    last = 0;
    frame = requestAnimationFrame(loop);
  };

  const observer = new IntersectionObserver(
    (records) => {
      for (const record of records) {
        const entry = entries.find((item) => item.element === record.target);
        if (entry) entry.visible = record.isIntersecting;
      }
      start();
    },
    { threshold: 0.3 },
  );
  entries.forEach((entry) => observer.observe(entry.element));

  // A scene can finish revealing while the loop is idle.
  const poke = window.setInterval(() => {
    if (!frame && entries.some((entry) => entry.visible)) start();
  }, 500);

  const onVisibility = () => {
    if (document.hidden) {
      if (frame) cancelAnimationFrame(frame);
      frame = 0;
    } else start();
  };
  document.addEventListener("visibilitychange", onVisibility);

  return () => {
    cleanups.forEach((cleanup) => cleanup());
    if (frame) cancelAnimationFrame(frame);
    observer.disconnect();
    window.clearInterval(poke);
    document.removeEventListener("visibilitychange", onVisibility);
  };
}
