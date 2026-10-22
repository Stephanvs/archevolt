// A small-scale model of Archevolt's storage engine. Every operation follows the
// runtime code path step by step: src/Archevolt/Storage/Chunks.cs,
// src/Archevolt/Entities/Entity.cs, src/Archevolt/World.cs,
// src/Archevolt/Commands/Commands.cs, and the tick and partition rules in
// src/Archevolt/Execution/BoundSchedule.cs.

export const SOURCE_ROOT = "https://github.com/Stephanvs/archevolt/blob/main/";
export const PAGE_SIZE = 1024;
export const CHUNK_BYTES = 384;
export const WORLD_ID = 1;

export type Tone = "pos" | "vel" | "hp" | "tag" | "acc" | "wide";

export interface ComponentInfo {
  id: number;
  name: string;
  size: number;
  tracked: boolean;
  tag: boolean;
  tone: Tone;
}

export const COMPONENTS: readonly ComponentInfo[] = [
  { id: 0, name: "Position", size: 12, tracked: false, tag: false, tone: "pos" },
  { id: 1, name: "Velocity", size: 12, tracked: false, tag: false, tone: "vel" },
  { id: 2, name: "Health", size: 4, tracked: true, tag: false, tone: "hp" },
  { id: 3, name: "Frozen", size: 0, tracked: false, tag: true, tone: "tag" },
];

export const BUILDER_SOURCE = `var world = new WorldBuilder()
    .AddBundle<Mover>()                  // Position, Velocity → IDs 0, 1
    .AddComponent<Health>(tracked: true) // ID 2
    .AddComponent<Frozen>(tag: true)     // ID 3
    .WithChunkBytes(${CHUNK_BYTES})
    .Build();`;

// ---------------------------------------------------------------- layout

export interface LayoutAttempt {
  capacity: number;
  bytes: number;
}

export interface ChunkLayout {
  target: number;
  rowBytes: number;
  capacity: number;
  bytes: number;
  values: number[];
  added: number[];
  changed: number[];
  attempts: LayoutAttempt[];
}

export const align64 = (value: number) => (value + 63) & ~63;

/** Mirrors the ChunkLayout constructor in Chunks.cs. */
export function chunkLayout(components: readonly ComponentInfo[], target: number): ChunkLayout {
  let rowBytes = 8;
  for (const component of components) rowBytes += component.size + (component.tracked ? 16 : 0);
  let capacity = Math.max(1, Math.floor(target / rowBytes));
  const attempts: LayoutAttempt[] = [];
  for (;;) {
    const values: number[] = [];
    const added: number[] = [];
    const changed: number[] = [];
    let offset = align64(capacity * 8);
    for (const component of components) {
      values.push(component.tag ? -1 : offset);
      if (!component.tag) offset = align64(offset + capacity * component.size);
      added.push(component.tracked ? offset : -1);
      if (component.tracked) offset = align64(offset + capacity * 8);
      changed.push(component.tracked ? offset : -1);
      if (component.tracked) offset = align64(offset + capacity * 8);
    }
    attempts.push({ capacity, bytes: offset });
    if (offset <= target || capacity === 1)
      return { target, rowBytes, capacity, bytes: offset, values, added, changed, attempts };
    capacity--;
  }
}

export type RegionKind = "entity" | "value" | "added" | "changed" | "pad";

export interface Region {
  kind: RegionKind;
  /** Component slot inside the archetype, or -1 for the entity column and padding. */
  slot: number;
  start: number;
  end: number;
  stride: number;
}

export function layoutRegions(layout: ChunkLayout, components: readonly ComponentInfo[]): Region[] {
  const regions: Region[] = [];
  let cursor = 0;
  const push = (kind: RegionKind, slot: number, start: number, length: number, stride: number) => {
    if (start > cursor) regions.push({ kind: "pad", slot: -1, start: cursor, end: start, stride: 0 });
    regions.push({ kind, slot, start, end: start + length, stride });
    cursor = start + length;
  };
  push("entity", -1, 0, layout.capacity * 8, 8);
  components.forEach((component, slot) => {
    if (layout.values[slot] >= 0) push("value", slot, layout.values[slot], layout.capacity * component.size, component.size);
    if (layout.added[slot] >= 0) push("added", slot, layout.added[slot], layout.capacity * 8, 8);
    if (layout.changed[slot] >= 0) push("changed", slot, layout.changed[slot], layout.capacity * 8, 8);
  });
  if (layout.bytes > cursor) regions.push({ kind: "pad", slot: -1, start: cursor, end: layout.bytes, stride: 0 });
  return regions;
}

// ---------------------------------------------------------------- world model

export interface Handle {
  world: number;
  index: number;
  gen: number;
}

export interface Key {
  index: number;
  gen: number;
}

export type SlotState = "Free" | "Reserved" | "Alive" | "Retired";

export interface Slot {
  gen: number;
  state: SlotState;
  arch: number | null;
  chunk: number | null;
  row: number;
}

export type Vec = [number, number, number];
export type CellValue = Vec | number | null;

export interface Chunk {
  uid: number;
  block: number;
  arch: number;
  count: number;
  /** EntityKey column. null means the bytes are still zero from ChunkPool.Rent. */
  keys: (Key | null)[];
  values: CellValue[][];
  added: number[][];
  changed: number[][];
}

export interface Archetype {
  index: number;
  ids: number[];
  key: string;
  layout: ChunkLayout;
  chunks: number[];
  add: Record<number, number>;
  remove: Record<number, number>;
  origin: string;
}

export interface Match {
  arch: number;
  columns: number[];
}

export interface SystemState {
  lastRun: number;
  thisRun: number;
  epoch: number;
  seen: number;
  matches: Match[];
}

export interface CommandRecord {
  target: Handle;
  offset: number;
  length: number;
  position: Vec;
  velocity: Vec;
}

export type SystemName = "Movement" | "Regen" | "Watch" | "Spawner";

export interface World {
  id: number;
  tick: number;
  seed: number;
  archetypes: Archetype[];
  byKey: Record<string, number>;
  chunks: Record<number, Chunk>;
  nextChunk: number;
  table: { slots: Slot[]; count: number; free: number[]; pages: number };
  pool: { free: Record<number, number[]>; owned: number; allocated: number; nextBlock: number };
  systems: Record<SystemName, SystemState>;
  commands: CommandRecord[];
}

export interface SystemInfo {
  name: SystemName;
  signature: string;
  summary: string;
  terms: { comp: number; access: "Read" | "Write"; changed?: boolean }[];
  without: number[];
  partitionable: boolean;
  commands: boolean;
}

export const SYSTEMS: readonly SystemInfo[] = [
  {
    name: "Movement",
    signature: "[Each(Partitioned = true), Without<Frozen>]\nstatic void Move(scoped ref Position p, scoped in Velocity v)",
    summary: "Adds Velocity to Position. Untracked and partitionable.",
    terms: [{ comp: 0, access: "Write" }, { comp: 1, access: "Read" }],
    without: [3],
    partitionable: true,
    commands: false,
  },
  {
    name: "Regen",
    signature: "[Each]\nstatic void Regen(scoped ref Health h)",
    summary: "Heals by 10, up to 100. The writable ref stamps Changed.",
    terms: [{ comp: 2, access: "Write" }],
    without: [],
    partitionable: false,
    commands: false,
  },
  {
    name: "Watch",
    signature: "[Each, Changed<Health>]\nstatic void Watch(Entity e, scoped in Health h)",
    summary: "Visits rows whose Health changed since its last successful run.",
    terms: [{ comp: 2, access: "Read", changed: true }],
    without: [],
    partitionable: false,
    commands: false,
  },
  {
    name: "Spawner",
    signature: "[EcsSystem]\nstatic void Spawn(scoped Commands commands)",
    summary: "Records two Commands.Spawn(new Mover(...)) for the barrier.",
    terms: [],
    without: [],
    partitionable: false,
    commands: true,
  },
];

export const systemInfo = (name: SystemName) => SYSTEMS.find((system) => system.name === name)!;

// ---------------------------------------------------------------- traces

export type Role = "write" | "read" | "hole" | "source" | "dest" | "match" | "skip" | "stale";

export interface Focus {
  slots?: number[];
  archetypes?: number[];
  cells?: { chunk: number; row: number; role: Role }[];
  matched?: number[];
  workers?: Record<number, number>;
  pool?: boolean;
  tick?: boolean;
  commands?: boolean;
  /** The chunk the byte map should show for this step. */
  chunk?: number;
}

export interface CodeRef {
  member: string;
  file: string;
}

export interface Frame {
  state: World;
  title: string;
  detail: string;
  code: CodeRef;
  focus: Focus;
}

export interface Trace {
  id: number;
  call: string;
  frames: Frame[];
  /** The handle the UI should select after this operation. */
  select?: Handle;
}

const FILES = {
  world: "src/Archevolt/World.cs",
  chunks: "src/Archevolt/Storage/Chunks.cs",
  entity: "src/Archevolt/Entities/Entity.cs",
  descriptors: "src/Archevolt/Storage/Descriptors.cs",
  commands: "src/Archevolt/Commands/Commands.cs",
  query: "src/Archevolt/Queries/QueryState.cs",
  schedule: "src/Archevolt/Execution/BoundSchedule.cs",
  systems: "src/Archevolt/Systems/Systems.cs",
} as const;

const code = (member: string, file: string): CodeRef => ({ member, file });

// ---------------------------------------------------------------- helpers

export const names = (ids: readonly number[]) =>
  ids.length === 0 ? "{ }" : `{${ids.map((id) => COMPONENTS[id].name).join(", ")}}`;

export const maskBits = (ids: readonly number[]) =>
  `0b${ids.reduce((mask, id) => mask | (1 << id), 0).toString(2).padStart(COMPONENTS.length, "0")}`;

export const handleLabel = (handle: Handle) => `(${handle.world}, ${handle.index}, ${handle.gen})`;

export function binarySearch(ids: readonly number[], id: number): number {
  let low = 0;
  let high = ids.length - 1;
  while (low <= high) {
    const mid = (low + high) >> 1;
    if (ids[mid] === id) return mid;
    if (ids[mid] < id) low = mid + 1;
    else high = mid - 1;
  }
  return ~low;
}

const list = (items: readonly string[]) =>
  items.length <= 1 ? items.join("") : `${items.slice(0, -1).join(", ")} and ${items[items.length - 1]}`;

export const formatValue = (value: CellValue) =>
  value === null ? "0" : typeof value === "number" ? String(value) : `(${value.join(", ")})`;

export function chunkOrdinal(world: World, uid: number) {
  const chunk = world.chunks[uid];
  return chunk ? world.archetypes[chunk.arch].chunks.indexOf(uid) : -1;
}

export interface AliveCheck {
  label: string;
  ok: boolean;
}

/** The four comparisons in EntityTable.IsAlive, in evaluation order. */
export function aliveChecks(world: World, handle: Handle): AliveCheck[] {
  const table = world.table;
  const inRange = handle.index >= 0 && handle.index < table.count;
  const slot = inRange ? table.slots[handle.index] : null;
  return [
    { label: `WorldId ${handle.world} == ${world.id}`, ok: handle.world === world.id },
    { label: `Index ${handle.index} < _count ${table.count}`, ok: inRange },
    { label: `Generation ${handle.gen} == slot ${slot ? slot.gen : "–"}`, ok: slot !== null && slot.gen === handle.gen },
    { label: `State ${slot ? slot.state : "–"} == Alive`, ok: slot !== null && slot.state === "Alive" },
  ];
}

function failedCheck(world: World, handle: Handle): string | null {
  const checks = aliveChecks(world, handle);
  const index = checks.findIndex((check) => !check.ok);
  return index < 0 ? null : `check ${index + 1}, ${checks[index].label}, is false`;
}

const clone = <T>(value: T): T => structuredClone(value);

function cloneValue(value: CellValue): CellValue {
  return Array.isArray(value) ? [value[0], value[1], value[2]] : value;
}

// ---------------------------------------------------------------- tracer

let traceIds = 0;

class Tracer {
  w: World;
  frames: Frame[] = [];
  recording = true;

  constructor(world: World) {
    this.w = clone(world);
  }

  step(title: string, detail: string, ref: CodeRef, focus: Focus = {}) {
    if (this.recording) this.frames.push({ state: clone(this.w), title, detail, code: ref, focus });
  }

  trace(call: string, select?: Handle): Trace {
    return { id: ++traceIds, call, frames: this.frames, select };
  }

  random(min: number, max: number) {
    this.w.seed = (this.w.seed * 1103515245 + 12345) & 0x7fffffff;
    return min + (this.w.seed % (max - min + 1));
  }

  moverValues(): [Vec, Vec] {
    const position: Vec = [this.random(0, 9), this.random(0, 9), 0];
    let velocity: Vec = [this.random(-1, 1), this.random(-1, 1), 0];
    if (velocity[0] === 0 && velocity[1] === 0) velocity = [1, 0, 0];
    return [position, velocity];
  }

  nextTick() {
    this.w.tick += 1;
    return this.w.tick;
  }

  arch(index: number) {
    return this.w.archetypes[index];
  }

  chunk(uid: number) {
    return this.w.chunks[uid];
  }

  slot(index: number) {
    return this.w.table.slots[index];
  }

  label(uid: number) {
    const chunk = this.chunk(uid);
    return `#${chunk.arch} chunk ${chunkOrdinal(this.w, uid)}`;
  }

  // EntityTable.Reserve
  reserve() {
    const table = this.w.table;
    let index: number;
    const parts: string[] = [];
    if (table.free.length !== 0) {
      index = table.free.pop()!;
      parts.push(`Pops slot ${index} off the LIFO free stack. It already carries generation ${table.slots[index].gen}, because ReleaseCore bumped it when the previous occupant left.`);
    } else {
      index = table.count;
      parts.push(`The free stack is empty, so the slot is the next fresh index, _count = ${index}.`);
      if (Math.floor(index / PAGE_SIZE) === table.pages) {
        table.pages += 1;
        parts.push(`It is the first slot of page ${table.pages - 1}, so the outer page array grows by one ${PAGE_SIZE}-entry page and is published with Volatile.Write. Existing pages never move.`);
      }
      table.count += 1;
      table.slots[index] = { gen: 0, state: "Free", arch: null, chunk: null, row: 0 };
    }
    const slot = table.slots[index];
    if (slot.gen === 0) {
      slot.gen = 1;
      parts.push("Generation 0 is never issued, so the slot starts at generation 1.");
    }
    slot.state = "Reserved";
    const handle: Handle = { world: this.w.id, index, gen: slot.gen };
    parts.push(`State: Reserved. The handle is ${handleLabel(handle)}.`);
    return { handle, text: parts.join(" ") };
  }

  // World.GetArchetype
  intern(ids: number[], origin: string) {
    const key = ids.join(",");
    const found = this.w.byKey[key];
    if (found !== undefined) return { arch: this.arch(found), created: false };
    const arch: Archetype = {
      index: this.w.archetypes.length,
      ids,
      key,
      layout: chunkLayout(ids.map((id) => COMPONENTS[id]), CHUNK_BYTES),
      chunks: [],
      add: {},
      remove: {},
      origin,
    };
    this.w.archetypes.push(arch);
    this.w.byKey[key] = arch.index;
    return { arch, created: true };
  }

  layoutText(arch: Archetype) {
    const layout = arch.layout;
    const first = layout.attempts[0].capacity;
    const settle =
      layout.attempts.length === 1
        ? `${layout.capacity} rows fit in ${layout.bytes} B`
        : `${first} rows pad past ${CHUNK_BYTES} B, so capacity steps down to ${layout.capacity} rows in ${layout.bytes} B`;
    return `ChunkLayout: rowBytes = ${layout.rowBytes}, first capacity ⌊${CHUNK_BYTES} / ${layout.rowBytes}⌋ = ${first}; ${settle}.`;
  }

  // World.Transition
  transition(source: Archetype, id: number, add: boolean) {
    const name = COMPONENTS[id].name;
    const cache = add ? source.add : source.remove;
    const cacheName = add ? "AddTransitions" : "RemoveTransitions";
    const cached = cache[id];
    if (cached !== undefined) {
      const arch = this.arch(cached);
      this.step(
        `Transition ${add ? "+" : "−"}${name}: cache hit`,
        `#${source.index}.${cacheName} already maps ${name} to #${arch.index} ${names(arch.ids)}. No lookup, no allocation.`,
        code("World.Transition", FILES.world),
        { archetypes: [source.index, arch.index] },
      );
      return arch;
    }
    const ids = add ? [...source.ids, id].sort((a, b) => a - b) : source.ids.filter((other) => other !== id);
    const { arch, created } = this.intern(ids, `${add ? "+" : "−"}${name} from #${source.index}`);
    cache[id] = arch.index;
    const derive = add ? `Ids.Append(${id}).Order()` : `Ids.Where(i => i != ${id})`;
    this.step(
      `Transition ${add ? "+" : "−"}${name}: cache miss`,
      `${derive} = [${ids.join(", ")}]. GetArchetype looks up the key "${arch.key}": ${
        created ? `it is new, so the world interns #${arch.index} ${names(ids)}. ${this.layoutText(arch)}` : `already interned as #${arch.index}.`
      } The edge is stored in #${source.index}.${cacheName}.`,
      code("World.Transition", FILES.world),
      { archetypes: [source.index, arch.index] },
    );
    return arch;
  }

  // ChunkPool.Rent / Return
  rent(bytes: number) {
    const stack = this.w.pool.free[bytes];
    if (stack && stack.length !== 0) return { block: stack.pop()!, reused: true };
    this.w.pool.nextBlock += 1;
    this.w.pool.owned += 1;
    this.w.pool.allocated += bytes;
    return { block: this.w.pool.nextBlock, reused: false };
  }

  giveBack(block: number, bytes: number) {
    const stack = (this.w.pool.free[bytes] ??= []);
    if (stack.length < 64) {
      stack.push(block);
      return true;
    }
    this.w.pool.owned -= 1;
    this.w.pool.allocated -= bytes;
    return false;
  }

  // Archetype.Append
  append(arch: Archetype, key: Key, scope: string) {
    const capacity = arch.layout.capacity;
    const lastUid = arch.chunks[arch.chunks.length - 1];
    const last = lastUid === undefined ? null : this.chunk(lastUid);
    let chunk: Chunk;
    if (last === null || last.count === capacity) {
      const rented = this.rent(arch.layout.bytes);
      chunk = {
        uid: ++this.w.nextChunk,
        block: rented.block,
        arch: arch.index,
        count: 0,
        keys: new Array(capacity).fill(null),
        values: arch.ids.map(() => new Array(capacity).fill(null)),
        added: arch.ids.map(() => new Array(capacity).fill(0)),
        changed: arch.ids.map(() => new Array(capacity).fill(0)),
      };
      this.w.chunks[chunk.uid] = chunk;
      arch.chunks.push(chunk.uid);
      this.step(
        `${scope} · Append creates a chunk`,
        `${last === null ? `#${arch.index} has no chunks` : `The last chunk of #${arch.index} is full (${capacity}/${capacity})`}, so Append creates a NativeChunk. ChunkPool.Rent(${arch.layout.bytes}) ${
          rented.reused
            ? `pops block #${rented.block} off the ${arch.layout.bytes} B free stack`
            : `has no free ${arch.layout.bytes} B block and calls NativeMemory.AlignedAlloc(${arch.layout.bytes}, 64), giving block #${rented.block}`
        }. Rent zero-fills the block either way.`,
        code("Archetype.Append", FILES.chunks),
        { archetypes: [arch.index], pool: true, chunk: chunk.uid },
      );
    } else chunk = last;
    const row = chunk.count++;
    chunk.keys[row] = { index: key.index, gen: key.gen };
    this.step(
      `${scope} · Append writes row ${row}`,
      `Writes EntityKey(${key.index}, ${key.gen}) into the entity column of ${this.label(chunk.uid)} at row ${row}. Count becomes ${chunk.count}/${capacity}. Only the last chunk can have room, so Append never searches.`,
      code("Archetype.Append", FILES.chunks),
      { cells: [{ chunk: chunk.uid, row, role: "dest" }], chunk: chunk.uid },
    );
    return { chunk, row };
  }

  // NativeChunk.CopyRowTo
  copyRow(source: Chunk, sourceRow: number, target: Chunk, targetRow: number) {
    const from = this.arch(source.arch);
    const to = this.arch(target.arch);
    const copied: string[] = [];
    const skipped: string[] = [];
    from.ids.forEach((id, column) => {
      const component = COMPONENTS[id];
      const targetColumn = binarySearch(to.ids, id);
      if (targetColumn < 0) {
        skipped.push(component.name);
        return;
      }
      if (!component.tag) target.values[targetColumn][targetRow] = cloneValue(source.values[column][sourceRow]);
      if (component.tracked) {
        target.added[targetColumn][targetRow] = source.added[column][sourceRow];
        target.changed[targetColumn][targetRow] = source.changed[column][sourceRow];
      }
      copied.push(component.tag ? `${component.name} (a tag, so no bytes)` : component.tracked ? `${component.name} with its Added and Changed ticks` : component.name);
    });
    return { copied, skipped };
  }

  relocate(index: number, arch: number, chunk: number, row: number) {
    const slot = this.slot(index);
    slot.arch = arch;
    slot.chunk = chunk;
    slot.row = row;
  }

  // Archetype.Remove
  swapRemove(arch: Archetype, chunk: Chunk, row: number, scope: string) {
    const last = this.chunk(arch.chunks[arch.chunks.length - 1]);
    const lastRow = last.count - 1;
    const hole = `${this.label(chunk.uid)} row ${row}`;
    let prefix: string;
    if (chunk.uid !== last.uid || row !== lastRow) {
      this.copyRow(last, lastRow, chunk, row);
      const moved = last.keys[lastRow]!;
      chunk.keys[row] = { index: moved.index, gen: moved.gen };
      this.relocate(moved.index, arch.index, chunk.uid, row);
      const across = chunk.uid !== last.uid;
      this.step(
        `${scope} · swap-remove fills the hole`,
        `The hole at ${hole} is not the archetype's last row, so Remove copies the last row of the last chunk (${this.label(last.uid)} row ${lastRow}, e${moved.index}) into it${
          across ? ", across chunks" : ""
        }. It writes that EntityKey and calls EntityTable.Relocate, so slot ${moved.index} now points at row ${row}.`,
        code("Archetype.Remove", FILES.chunks),
        {
          cells: [
            { chunk: chunk.uid, row, role: "hole" },
            { chunk: last.uid, row: lastRow, role: "source" },
          ],
          slots: [moved.index],
          chunk: chunk.uid,
        },
      );
      prefix = "";
    } else prefix = `The removed row is already the archetype's last row, so nothing has to fill it. `;
    last.count -= 1;
    if (last.count === 0) {
      const ordinal = chunkOrdinal(this.w, last.uid);
      arch.chunks.pop();
      delete this.w.chunks[last.uid];
      const cached = this.giveBack(last.block, arch.layout.bytes);
      this.step(
        `${scope} · the empty chunk is freed`,
        `${prefix}Count drops to 0, so Remove drops chunk ${ordinal} from #${arch.index}.Chunks and disposes it. ChunkPool.Return ${
          cached ? `pushes block #${last.block} onto the ${arch.layout.bytes} B free stack for the next Rent of that size` : `frees block #${last.block}, because 64 blocks of this size are already cached`
        }.`,
        code("Archetype.Remove", FILES.chunks),
        { pool: true, archetypes: [arch.index] },
      );
    } else {
      this.step(
        `${scope} · Count shrinks`,
        `${prefix}last.Count-- leaves ${this.label(last.uid)} at ${last.count}/${arch.layout.capacity}. Row ${lastRow} keeps its old bytes: nothing clears memory past Count, and the next Append overwrites every column.`,
        code("Archetype.Remove", FILES.chunks),
        { cells: [{ chunk: last.uid, row: lastRow, role: "stale" }], chunk: last.uid },
      );
    }
  }

  // World.Move
  move(handle: Handle, destination: Archetype) {
    const slot = this.slot(handle.index);
    const source = this.arch(slot.arch!);
    const sourceChunk = this.chunk(slot.chunk!);
    const sourceRow = slot.row;
    const location = this.append(destination, { index: handle.index, gen: handle.gen }, "Move");
    const { copied, skipped } = this.copyRow(sourceChunk, sourceRow, location.chunk, location.row);
    this.step(
      "Move · CopyRowTo",
      `For each source column, BinarySearch finds the destination column. It copies ${list(copied)} from ${this.label(sourceChunk.uid)} row ${sourceRow}${
        skipped.length ? `, and skips ${list(skipped)}, which the destination has no column for` : ""
      }. Ticks travel verbatim, so a move is never a change.`,
      code("NativeChunk.CopyRowTo", FILES.chunks),
      {
        cells: [
          { chunk: sourceChunk.uid, row: sourceRow, role: "source" },
          { chunk: location.chunk.uid, row: location.row, role: "dest" },
        ],
        chunk: location.chunk.uid,
      },
    );
    this.swapRemove(source, sourceChunk, sourceRow, "Move");
    this.relocate(handle.index, destination.index, location.chunk.uid, location.row);
    this.step(
      "Move · Relocate",
      `EntityTable.Relocate points slot ${handle.index} at ${this.label(location.chunk.uid)} row ${location.row}. The handle ${handleLabel(handle)} is unchanged; only the table entry moved.`,
      code("EntityTable.Relocate", FILES.entity),
      { slots: [handle.index], cells: [{ chunk: location.chunk.uid, row: location.row, role: "dest" }], chunk: location.chunk.uid },
    );
  }

  // World.Stamp
  stamp(chunk: Chunk, column: number, row: number, tick: number, added: boolean) {
    if (!COMPONENTS[this.arch(chunk.arch).ids[column]].tracked) return;
    if (added) chunk.added[column][row] = tick;
    chunk.changed[column][row] = tick;
  }

  requireAlive(handle: Handle, api: string) {
    const failure = failedCheck(this.w, handle);
    if (failure === null) return true;
    this.step(
      "RequireAlive throws",
      `IsAlive fails: ${failure}. ${api} throws InvalidOperationException ("The entity is stale, reserved, or belongs to another world.") before drawing a tick.`,
      code("World.RequireAlive", FILES.world),
      { slots: handle.index < this.w.table.count ? [handle.index] : [] },
    );
    return false;
  }

  // ApplySpawn + WriteBundle + Materialize for the Mover bundle
  spawnMover(handle: Handle, position: Vec, velocity: Vec, tick: number, scope: string) {
    const arch = this.arch(0);
    const location = this.append(arch, { index: handle.index, gen: handle.gen }, scope);
    location.chunk.values[0][location.row] = position;
    location.chunk.values[1][location.row] = velocity;
    this.step(
      `${scope} · WriteBundle`,
      `WriteBundle walks the payload in declaration order and BinarySearch([0, 1], id) finds each column: Position goes to column 0, Velocity to column 1. Stamp(…, tick ${tick}) does nothing, because neither component is tracked.`,
      code("World.WriteBundle", FILES.commands),
      { cells: [{ chunk: location.chunk.uid, row: location.row, role: "write" }], chunk: location.chunk.uid },
    );
    const slot = this.slot(handle.index);
    this.relocate(handle.index, arch.index, location.chunk.uid, location.row);
    slot.state = "Alive";
    this.step(
      `${scope} · Materialize`,
      `Reserved becomes Alive. Slot ${handle.index} records #0, chunk ${chunkOrdinal(this.w, location.chunk.uid)}, row ${location.row}.`,
      code("EntityTable.Materialize", FILES.entity),
      { slots: [handle.index], cells: [{ chunk: location.chunk.uid, row: location.row, role: "dest" }], chunk: location.chunk.uid },
    );
    return location;
  }
}

// ---------------------------------------------------------------- construction

function emptyWorld(): World {
  const system = (): SystemState => ({ lastRun: 0, thisRun: 0, epoch: 0, seen: 0, matches: [] });
  return {
    id: WORLD_ID,
    tick: 0,
    seed: 20261021,
    archetypes: [],
    byKey: {},
    chunks: {},
    nextChunk: 0,
    table: { slots: [], count: 0, free: [], pages: 0 },
    pool: { free: {}, owned: 0, allocated: 0, nextBlock: 0 },
    systems: { Movement: system(), Regen: system(), Watch: system(), Spawner: system() },
    commands: [],
  };
}

export const SEED_MOVERS = 12;

export function initialTrace(): Trace {
  const tracer = new Tracer(emptyWorld());
  const mover = tracer.intern([0, 1], "AddBundle<Mover>() binding, created at Build()").arch;
  tracer.step(
    "WorldBuilder.Build()",
    `ComponentRegistry assigns world-local IDs in registration order: Position 0 and Velocity 1 from AddBundle<Mover>, Health 2, Frozen 3. The Mover binding sorts its IDs to [0, 1] and interns archetype #0 under the key "0,1" before any entity exists. ${tracer.layoutText(mover)}`,
    code("World(WorldBuilder)", FILES.world),
    { archetypes: [0] },
  );
  tracer.recording = false;
  for (let i = 0; i < SEED_MOVERS; i++) {
    const [position, velocity] = tracer.moverValues();
    const tick = tracer.nextTick();
    const { handle } = tracer.reserve();
    tracer.spawnMover(handle, position, velocity, tick, "ApplySpawn");
  }
  tracer.recording = true;
  tracer.step(
    `${SEED_MOVERS} × world.Spawn(new Mover { … })`,
    `Spawns e0–e${SEED_MOVERS - 1} at ticks 1–${SEED_MOVERS}. Rows 0–${mover.layout.capacity - 1} fill chunk 0. The next spawn finds the last chunk full, so Append rents a second ${mover.layout.bytes} B block for chunk 1.`,
    code("World.Spawn<TBundle>", FILES.world),
    { archetypes: [0], pool: true },
  );
  return tracer.trace("new WorldBuilder() … .Build()");
}

// ---------------------------------------------------------------- idle world API

export function spawnEmpty(world: World): Trace {
  const tracer = new Tracer(world);
  const tick = tracer.nextTick();
  const { handle, text } = tracer.reserve();
  tracer.step(
    `NextTick → ${tick}, then Reserve`,
    `RequireIdle passes and the clock advances to tick ${tick}. An empty spawn stamps nothing, but it still consumes a tick. ${text}`,
    code("EntityTable.Reserve", FILES.entity),
    { slots: [handle.index], tick: true },
  );
  const { arch, created } = tracer.intern([], "first Spawn()");
  tracer.step(
    `GetArchetype([]) → #${arch.index}`,
    created
      ? `The key "" is not interned yet, so this first empty spawn creates #${arch.index} ${names([])}. ${tracer.layoutText(arch)}`
      : `The key "" maps to #${arch.index}. Spawn() still builds the string key on every call.`,
    code("World.GetArchetype", FILES.world),
    { archetypes: [arch.index] },
  );
  const location = tracer.append(arch, { index: handle.index, gen: handle.gen }, "Spawn");
  tracer.relocate(handle.index, arch.index, location.chunk.uid, location.row);
  tracer.slot(handle.index).state = "Alive";
  tracer.step(
    "Materialize",
    `Reserved becomes Alive. Slot ${handle.index} records #${arch.index}, chunk ${chunkOrdinal(tracer.w, location.chunk.uid)}, row ${location.row}. Spawn returns ${handleLabel(handle)}.`,
    code("EntityTable.Materialize", FILES.entity),
    { slots: [handle.index], cells: [{ chunk: location.chunk.uid, row: location.row, role: "dest" }], chunk: location.chunk.uid },
  );
  return tracer.trace("world.Spawn()", handle);
}

export function spawnMovers(world: World, count: number): Trace {
  const tracer = new Tracer(world);
  if (count === 1) {
    const [position, velocity] = tracer.moverValues();
    tracer.step(
      "BundleWriter packs the payload",
      `The generated Mover.Write puts members in declaration order into a 24-byte stack payload: Position (12 B) at bytes 0–11, Velocity (12 B) at bytes 12–23. Put<T> checks each member's TypeKey, and Complete() checks that every byte was written. Payloads up to 1024 B live on the stack.`,
      code("BundleWriter.Put", FILES.descriptors),
    );
    const tick = tracer.nextTick();
    const { handle, text } = tracer.reserve();
    tracer.step(`NextTick → ${tick}, then Reserve`, `The clock advances to tick ${tick}. ${text}`, code("EntityTable.Reserve", FILES.entity), {
      slots: [handle.index],
      tick: true,
    });
    tracer.spawnMover(handle, position, velocity, tick, "ApplySpawn");
    return tracer.trace(`world.Spawn(new Mover { Position = (${position.join(", ")}), Velocity = (${velocity.join(", ")}) })`, handle);
  }
  let last: Handle | undefined;
  for (let i = 0; i < count; i++) {
    const [position, velocity] = tracer.moverValues();
    tracer.recording = false;
    const tick = tracer.nextTick();
    const { handle } = tracer.reserve();
    const location = tracer.spawnMover(handle, position, velocity, tick, "ApplySpawn");
    last = handle;
    tracer.recording = true;
    tracer.step(
      `Spawn ${i + 1} of ${count} → e${handle.index}`,
      `Tick ${tick}. Reserve hands out slot ${handle.index}, generation ${handle.gen}. The row lands in ${tracer.label(location.chunk.uid)} at row ${location.row}.`,
      code("World.Spawn<TBundle>", FILES.world),
      { slots: [handle.index], cells: [{ chunk: location.chunk.uid, row: location.row, role: "dest" }], chunk: location.chunk.uid },
    );
  }
  return tracer.trace(`${count} × world.Spawn(new Mover { … })`, last);
}

const INSERT_VALUES: Record<number, CellValue> = { 0: [0, 0, 0], 1: [1, 1, 0], 2: 80, 3: null };
const INSERT_SOURCE: Record<number, string> = {
  0: "new Position(0, 0, 0)",
  1: "new Velocity(1, 1, 0)",
  2: "new Health(80)",
  3: "new Frozen()",
};

export function insert(world: World, handle: Handle, id: number): Trace {
  const component = COMPONENTS[id];
  const call = `world.Insert(e${handle.index}, ${INSERT_SOURCE[id]})`;
  const tracer = new Tracer(world);
  if (!tracer.requireAlive(handle, "Insert")) return tracer.trace(call);
  const tick = tracer.nextTick();
  const source = tracer.arch(tracer.slot(handle.index).arch!);
  const added = !source.ids.includes(id);
  tracer.step(
    `NextTick → ${tick}; Has(${component.name})?`,
    `RequireAlive passes. Registry.Id<${component.name}>() = ${id} and the payload is ${component.size} bytes. ApplyInsert tests bit ${id} of #${source.index}'s mask ${maskBits(source.ids)}: ${
      added ? "it is clear, so this is an addition and the row must move to another archetype" : "it is set, so the value is replaced in place"
    }.`,
    code("World.ApplyInsert", FILES.commands),
    { tick: true, archetypes: [source.index], slots: [handle.index] },
  );
  if (added) tracer.move(handle, tracer.transition(source, id, true));
  const slot = tracer.slot(handle.index);
  const arch = tracer.arch(slot.arch!);
  const chunk = tracer.chunk(slot.chunk!);
  const column = binarySearch(arch.ids, id);
  const previousAdded = component.tracked ? chunk.added[column][slot.row] : 0;
  if (!component.tag) chunk.values[column][slot.row] = cloneValue(INSERT_VALUES[id]);
  tracer.stamp(chunk, column, slot.row, tick, added);
  let detail: string;
  if (component.tag)
    detail = added
      ? "Frozen is a tag: there is no value column, so no bytes are copied, and it is untracked, so nothing is stamped. Membership alone changed the archetype."
      : "The entity is already Frozen. A tag has no bytes and Frozen is untracked, so the call does nothing beyond consuming its tick.";
  else if (added)
    detail = `BinarySearch([${arch.ids.join(", ")}], ${id}) = column ${column}. The ${component.size}-byte payload is copied into ValueBytes(${column}, ${slot.row}). ${
      component.tracked ? `${component.name} is tracked and new, so Added = Changed = ${tick}.` : `${component.name} is untracked, so Stamp returns immediately.`
    }`;
  else
    detail = `The ${component.size}-byte payload overwrites column ${column} in place. ${
      component.tracked
        ? `Stamp(added: false) sets Changed = ${tick} and keeps Added = ${previousAdded}. An equal value still counts as a change.`
        : `${component.name} is untracked, so nothing is stamped.`
    }`;
  tracer.step(
    `Write ${component.name}${component.tracked ? " and stamp" : ""}`,
    detail,
    code("World.Stamp", FILES.world),
    { cells: [{ chunk: chunk.uid, row: slot.row, role: "write" }], slots: [handle.index], chunk: chunk.uid },
  );
  return tracer.trace(call);
}

export function remove(world: World, handle: Handle, id: number): Trace {
  const component = COMPONENTS[id];
  const call = `world.Remove<${component.name}>(e${handle.index})`;
  const tracer = new Tracer(world);
  if (!tracer.requireAlive(handle, "Remove")) return tracer.trace(call);
  const source = tracer.arch(tracer.slot(handle.index).arch!);
  if (!source.ids.includes(id)) {
    tracer.step(
      "Has is false: return false",
      `Bit ${id} of #${source.index}'s mask ${maskBits(source.ids)} is clear. Remove<${component.name}> returns false without drawing a tick, and nothing moves.`,
      code("World.Remove<T>", FILES.world),
      { archetypes: [source.index], slots: [handle.index] },
    );
    return tracer.trace(call);
  }
  const tick = tracer.nextTick();
  tracer.step(
    `NextTick → ${tick}; RecordRemoval`,
    `ApplyRemove calls RecordRemoval(${component.name}, e${handle.index}, ${tick}). No RemovedComponents<${component.name}> reader is bound in this world, so nothing is logged.`,
    code("World.ApplyRemove", FILES.commands),
    { tick: true, archetypes: [source.index], slots: [handle.index] },
  );
  tracer.move(handle, tracer.transition(source, id, false));
  return tracer.trace(call);
}

export function despawn(world: World, handle: Handle): Trace {
  const call = `world.Despawn(e${handle.index})`;
  const tracer = new Tracer(world);
  const failure = failedCheck(tracer.w, handle);
  if (failure !== null) {
    tracer.step(
      "IsAlive is false: return false",
      `Despawn checks IsAlive first: ${failure}. It returns false without drawing a tick.`,
      code("World.Despawn", FILES.world),
      { slots: handle.index < tracer.w.table.count ? [handle.index] : [] },
    );
    return tracer.trace(call);
  }
  const tick = tracer.nextTick();
  const slot = tracer.slot(handle.index);
  const arch = tracer.arch(slot.arch!);
  tracer.step(
    `NextTick → ${tick}; RecordRemoval for each component`,
    `ApplyDespawn calls RecordRemoval for ${arch.ids.length === 0 ? "no components" : list(arch.ids.map((id) => COMPONENTS[id].name))}. No removal readers are bound, so nothing is logged.`,
    code("World.ApplyDespawn", FILES.commands),
    { tick: true, slots: [handle.index], archetypes: [arch.index] },
  );
  tracer.swapRemove(arch, tracer.chunk(slot.chunk!), slot.row, "Despawn");
  slot.arch = null;
  slot.chunk = null;
  slot.row = 0;
  const retired = slot.gen === 0xffffffff;
  if (retired) slot.state = "Retired";
  else {
    slot.gen += 1;
    slot.state = "Free";
    tracer.w.table.free.push(handle.index);
  }
  tracer.step(
    "Release",
    retired
      ? `ReleaseCore clears the location. The generation is uint.MaxValue, so slot ${handle.index} is retired and never reused.`
      : `ReleaseCore clears the location, bumps the generation from ${handle.gen} to ${slot.gen}, marks the slot Free, and pushes ${handle.index} onto the free stack. The next Reserve reuses it first. Handle ${handleLabel(handle)} is now stale: its generation no longer matches.`,
    code("EntityTable.Release", FILES.entity),
    { slots: [handle.index] },
  );
  return tracer.trace(call);
}

// ---------------------------------------------------------------- schedules

function matchReason(info: SystemInfo, arch: Archetype): string | null {
  const missing = info.terms.map((term) => term.comp).filter((id) => !arch.ids.includes(id));
  if (missing.length) return `lacks ${list(missing.map((id) => COMPONENTS[id].name))}`;
  const excluded = info.without.filter((id) => arch.ids.includes(id));
  if (excluded.length) return `has ${list(excluded.map((id) => COMPONENTS[id].name))}`;
  return null;
}

export function runSystem(world: World, name: SystemName, concurrency: number): Trace {
  const info = systemInfo(name);
  const tracer = new Tracer(world);
  const system = tracer.w.systems[name];

  if (info.terms.length) {
    const before = system.seen;
    const total = tracer.w.archetypes.length;
    const fresh: string[] = [];
    const rejected: string[] = [];
    for (let index = before; index < total; index++) {
      const arch = tracer.arch(index);
      const reason = matchReason(info, arch);
      if (reason === null) {
        const columns = info.terms.map((term) => binarySearch(arch.ids, term.comp));
        system.matches.push({ arch: index, columns });
        fresh.push(`#${index} (columns ${columns.join(", ")})`);
      } else rejected.push(`#${index}, which ${reason}`);
    }
    system.seen = total;
    const required = list(info.terms.map((term) => COMPONENTS[term.comp].name));
    const excluded = info.without.map((id) => COMPONENTS[id].name);
    tracer.step(
      "Prepare → QueryState.Refresh",
      before === total
        ? `No archetypes were created since the last refresh (_seen = ${total}), so the cached matches are reused unchanged.`
        : `Scans World.Archetypes[${before}..${total}) with mask tests: requires ${required}${excluded.length ? `, excludes ${list(excluded)}` : ""}. ${
            fresh.length ? `Matches ${list(fresh)}.` : "No new matches."
          }${rejected.length ? ` Rejects ${list(rejected)}.` : ""} _seen becomes ${total}. Archetypes are never deleted, so the cache only grows.`,
      code("QueryState.Refresh", FILES.query),
      { matched: system.matches.map((match) => match.arch) },
    );
  }

  system.epoch += 1;
  system.thisRun = tracer.nextTick();
  const lastRun = system.lastRun;
  const now = system.thisRun;
  const matched = system.matches.map((match) => match.arch);
  tracer.step(
    `Begin → ThisRun = ${now}`,
    `Epoch ${system.epoch}. ThisRun = NextTick() = ${now}, so the change window is (LastRun, ThisRun] = (${lastRun}, ${now}].`,
    code("SystemInstance.Begin", FILES.systems),
    { tick: true, matched },
  );

  if (info.commands) {
    for (let k = 0; k < 2; k++) {
      const [position, velocity] = tracer.moverValues();
      const offset = tracer.w.commands.reduce((sum, record) => sum + record.length, 0);
      const { handle, text } = tracer.reserve();
      tracer.w.commands.push({ target: handle, offset, length: 24, position, velocity });
      tracer.step(
        `Commands.Spawn #${k}`,
        `BundleWriter writes the 24-byte Mover payload into the system's private CommandBuffer at offset ${offset}. Only then does EntityTable.Reserve take the table lock, which can happen on a worker thread. ${text} The record is Spawn(target, Mover, ${offset}, 24). Queries cannot see the entity yet, because it is Reserved, not Alive.`,
        code("Commands.Spawn<TBundle>", FILES.commands),
        { slots: [handle.index], commands: true },
      );
    }
  } else {
    const ordinals = system.matches.flatMap((match) => tracer.arch(match.arch).chunks.map((uid) => ({ uid, match })));
    const total = ordinals.length;
    const workers: Record<number, number> = {};
    let partitions = 1;
    let text: string;
    if (info.partitionable && concurrency > 1) {
      partitions = Math.max(1, Math.min(total, concurrency));
      const ranges: string[] = [];
      for (let p = 0; p < partitions; p++) {
        const first = Math.floor((total * p) / partitions);
        const end = Math.floor((total * (p + 1)) / partitions);
        for (let o = first; o < end; o++) workers[ordinals[o].uid] = p;
        ranges.push(`W${p} [${first}, ${end})`);
      }
      text = `TotalChunks = ${total} across the matched archetypes. With MaxConcurrency ${concurrency}, partitions = max(1, min(${total}, ${concurrency})) = ${partitions}, and partition p covers chunk ordinals [${total}·p/${partitions}, ${total}·(p+1)/${partitions}): ${list(ranges)}. The split counts chunks, not rows.`;
    } else {
      for (const entry of ordinals) workers[entry.uid] = 0;
      text = info.partitionable
        ? `MaxConcurrency is 1, so the partitionable system runs as a single work item over all ${total} chunks.`
        : `${name} is not partitioned, so it runs as one work item (0, int.MaxValue) over all ${total} chunks.`;
    }
    tracer.step("Work items", text, code("BoundSchedule.RunBatch", FILES.schedule), { workers, matched });

    ordinals.forEach((entry, ordinal) => {
      const chunk = tracer.chunk(entry.uid);
      const column = entry.match.columns[0];
      const worker = workers[entry.uid];
      const cells: { chunk: number; row: number; role: Role }[] = [];
      let detail: string;
      if (name === "Movement") {
        const velocityColumn = entry.match.columns[1];
        for (let row = 0; row < chunk.count; row++) {
          const position = chunk.values[column][row] as Vec;
          const velocity = chunk.values[velocityColumn][row] as Vec;
          chunk.values[column][row] = [position[0] + velocity[0], position[1] + velocity[1], position[2] + velocity[2]];
          cells.push({ chunk: chunk.uid, row, role: "write" });
        }
        detail = `cursor.Next yields ordinal ${ordinal}, ${tracer.label(chunk.uid)}. chunk.Write<Position>(${column}) and chunk.Read<Velocity>(${velocityColumn}) return spans of length Count = ${chunk.count}, then the generated loop runs Move for rows 0–${chunk.count - 1}. Position is untracked, so nothing is stamped.`;
      } else if (name === "Regen") {
        let unchanged = 0;
        for (let row = 0; row < chunk.count; row++) {
          const health = chunk.values[column][row] as number;
          if (health >= 100) unchanged += 1;
          chunk.values[column][row] = Math.min(100, health + 10);
          chunk.changed[column][row] = now;
          cells.push({ chunk: chunk.uid, row, role: "write" });
        }
        detail = `chunk.Tracked<Health>(${column}) wraps the value span and its Changed ticks. TrackedColumn.Write(row) stamps Changed[row] = ${now} before returning the ref, for all ${chunk.count} rows${
          unchanged ? `, including ${unchanged} already at 100 whose value does not change` : ""
        }. Added is untouched.`;
      } else {
        const seen: string[] = [];
        for (let row = 0; row < chunk.count; row++) {
          const stamp = chunk.changed[column][row];
          const inside = stamp > lastRun && stamp <= now;
          cells.push({ chunk: chunk.uid, row, role: inside ? "match" : "skip" });
          if (inside) seen.push(`e${chunk.keys[row]!.index}`);
        }
        detail = `chunk.Changed(${column}) returns the tick span. The generated loop skips a row unless Window.Contains(tick): tick > ${lastRun} && tick ≤ ${now}. ${
          seen.length ? `${seen.length} of ${chunk.count} rows pass: ${list(seen)}.` : `None of the ${chunk.count} rows pass.`
        }`;
      }
      tracer.step(`W${worker} · chunk ordinal ${ordinal}`, detail, code("QueryCursor.Next", FILES.query), { cells, workers, matched, chunk: chunk.uid });
    });
    if (total === 0)
      tracer.step("No chunks", "The matched archetypes hold no chunks, so the cursor yields nothing.", code("QueryCursor.Next", FILES.query), { matched });
  }

  system.lastRun = now;
  tracer.step(
    `Complete → LastRun = ${now}`,
    `Every work item finished without failure, so LastRun advances to ${now}.${
      name === "Regen" ? ` Its own stamps equal ${now}, and its next window starts after ${now}, so Regen never sees its own writes.` : ""
    } A failed run would keep LastRun and see the same changes again.`,
    code("SystemInstance.Complete", FILES.systems),
    { matched },
  );

  const barrier = tracer.nextTick();
  if (info.commands) {
    tracer.step(
      `ApplyDeferred → FlushCommands at tick ${barrier}`,
      `Workers have joined. FlushCommands draws one tick, ${barrier}, for every record in this flush. It plays producers in plan order and each producer's records in recording order.`,
      code("World.FlushCommands", FILES.commands),
      { tick: true, commands: true },
    );
    tracer.w.commands.slice().forEach((record, index) => {
      tracer.step(
        `Playback record ${index}: ApplySpawn`,
        `ApplySpawn checks that the reservation is still valid: world ${record.target.world}, generation ${record.target.gen}, state Reserved. Then it appends the row to #0.`,
        code("World.ApplySpawn", FILES.commands),
        { slots: [record.target.index], commands: true },
      );
      tracer.spawnMover(record.target, record.position, record.velocity, barrier, `Playback ${index}`);
    });
    tracer.w.commands = [];
    tracer.step(
      "Clear the buffer, then Prepare",
      "The buffer is cleared for the next run. Prepare refreshes every query, so archetypes created during playback join the caches before the next batch draws its ticks.",
      code("BoundSchedule.Run", FILES.schedule),
    );
  } else {
    tracer.step(
      `ApplyDeferred → tick ${barrier}`,
      `Every schedule ends with a barrier. ${name} recorded no commands, but FlushCommands still draws tick ${barrier}. Prepare then refreshes every query.`,
      code("BoundSchedule.Run", FILES.schedule),
      { tick: true },
    );
  }
  return tracer.trace(`schedule.Run() // ${name}`);
}

// ---------------------------------------------------------------- read helpers for the UI

export function liveEntities(world: World) {
  return world.table.slots.filter((slot) => slot.state === "Alive").length;
}

export function rowValues(world: World, handle: Handle) {
  const slot = world.table.slots[handle.index];
  if (!slot || slot.state !== "Alive" || slot.gen !== handle.gen) return null;
  const arch = world.archetypes[slot.arch!];
  const chunk = world.chunks[slot.chunk!];
  return {
    arch,
    chunk,
    row: slot.row,
    values: arch.ids.map((id, column) => ({
      component: COMPONENTS[id],
      value: chunk.values[column][slot.row],
      added: chunk.added[column][slot.row],
      changed: chunk.changed[column][slot.row],
    })),
  };
}
