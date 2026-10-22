import { useEffect, useState, type CSSProperties, type KeyboardEvent } from "react";
import "./storage/storage.css";
import {
  BUILDER_SOURCE,
  CHUNK_BYTES,
  COMPONENTS,
  SOURCE_ROOT,
  SYSTEMS,
  aliveChecks,
  chunkOrdinal,
  despawn,
  formatValue,
  handleLabel,
  initialTrace,
  insert,
  layoutRegions,
  liveEntities,
  maskBits,
  names,
  remove,
  rowValues,
  runSystem,
  spawnEmpty,
  spawnMovers,
  type Archetype,
  type CellValue,
  type Chunk,
  type Focus,
  type Handle,
  type Role,
  type Slot,
  type Trace,
  type Vec,
  type World,
} from "./storage/engine";

const STEP_MS = 1600;
const ROWS_PER_LINE = 16;
const ARROWS: Record<string, string> = {
  "1,0": "→",
  "1,1": "↗",
  "0,1": "↑",
  "-1,1": "↖",
  "-1,0": "←",
  "-1,-1": "↙",
  "0,-1": "↓",
  "1,-1": "↘",
  "0,0": "·",
};

const vars = (values: Record<string, string | number>) => values as CSSProperties;

function cellText(id: number, value: CellValue) {
  if (value === null) return "";
  if (Array.isArray(value)) {
    if (id === 1) return ARROWS[`${Math.sign(value[0])},${Math.sign(value[1])}`];
    return `${value[0]},${value[1]}`;
  }
  return String(value);
}

const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

/** The handle a slot most recently issued: a freed slot's generation was already bumped. */
function slotHandle(world: World, index: number, slot: Slot): Handle {
  const gen = slot.state === "Free" ? Math.max(1, slot.gen - 1) : slot.gen;
  return { world: world.id, index, gen };
}

export default function StorageExplorer() {
  const [log, setLog] = useState<Trace[]>(() => [initialTrace()]);
  const [view, setView] = useState(0);
  const [step, setStep] = useState(Number.MAX_SAFE_INTEGER);
  const [playing, setPlaying] = useState(false);
  const [autoplay, setAutoplay] = useState(true);
  const [concurrency, setConcurrency] = useState(2);
  const [selected, setSelected] = useState<Handle | null>(null);
  const [pinned, setPinned] = useState<number | null>(null);

  const trace = log[view];
  const index = Math.min(step, trace.frames.length - 1);
  const frame = trace.frames[index];
  const previous = index > 0 ? trace.frames[index - 1].state : null;
  const world = frame.state;
  const focus = frame.focus;
  const latest = log[log.length - 1];
  const live = latest.frames[latest.frames.length - 1].state;
  const flashKey = `${trace.id}-${index}`;

  useEffect(() => {
    if (!playing) return;
    if (index >= trace.frames.length - 1) {
      setPlaying(false);
      return;
    }
    const timer = window.setTimeout(() => setStep(index + 1), STEP_MS);
    return () => window.clearTimeout(timer);
  }, [playing, index, trace]);

  function perform(make: (world: World) => Trace) {
    const next = make(live);
    const nextLog = [...log, next].slice(-24);
    setLog(nextLog);
    setView(nextLog.length - 1);
    setStep(autoplay ? 0 : next.frames.length - 1);
    setPlaying(autoplay && next.frames.length > 1);
    setPinned(null);
    if (next.select) setSelected(next.select);
  }

  function reset() {
    setLog([initialTrace()]);
    setView(0);
    setStep(Number.MAX_SAFE_INTEGER);
    setPlaying(false);
    setSelected(null);
    setPinned(null);
  }

  function go(target: number) {
    setPlaying(false);
    setStep(Math.max(0, Math.min(trace.frames.length - 1, target)));
  }

  function togglePlay() {
    if (playing) {
      setPlaying(false);
      return;
    }
    if (index >= trace.frames.length - 1) setStep(0);
    setPlaying(true);
  }

  function onTraceKey(event: KeyboardEvent<HTMLElement>) {
    if (event.key === "ArrowRight") {
      event.preventDefault();
      go(index + 1);
    } else if (event.key === "ArrowLeft") {
      event.preventDefault();
      go(index - 1);
    }
  }

  const selectedSlot = selected && selected.index < world.table.count ? world.table.slots[selected.index] : null;
  const selectedLive = selectedSlot && selectedSlot.state === "Alive" && selectedSlot.gen === selected!.gen ? selectedSlot : null;
  const focusChunk = focus.chunk !== undefined && world.chunks[focus.chunk] ? focus.chunk : undefined;
  const firstChunk = world.archetypes.flatMap((arch) => arch.chunks)[0];
  const mapChunk =
    (pinned !== null && world.chunks[pinned] ? pinned : undefined) ?? focusChunk ?? selectedLive?.chunk ?? firstChunk ?? null;
  const focusRow =
    mapChunk === null
      ? null
      : (focus.cells?.find((cell) => cell.chunk === mapChunk && cell.role !== "skip")?.row ??
        (selectedLive?.chunk === mapChunk ? selectedLive.row : null));

  return (
    <div className="sx not-prose" aria-label="Archevolt storage explorer">
      <div className="sx-stack">
        <details>
          <summary className="sx-meta" style={{ cursor: "pointer" }}>
            The simulated world: {COMPONENTS.length} components, one bundle, {CHUNK_BYTES}-byte chunks
          </summary>
          <div className="sx-code" style={{ marginTop: "0.5rem" }}>
            {BUILDER_SOURCE}
          </div>
        </details>

        <Toolbar
          selected={selected}
          concurrency={concurrency}
          onConcurrency={setConcurrency}
          perform={perform}
          reset={reset}
        />

        <section className="sx-card sx-trace" aria-label="Operation trace" onKeyDown={onTraceKey}>
          <div className="sx-trace-head">
            <span className="sx-call">{trace.call}</span>
            <span className="sx-meta">
              step {index + 1} / {trace.frames.length}
            </span>
          </div>
          <div aria-live="polite">
            <div className="sx-step-title">{frame.title}</div>
            <p className="sx-step-detail">{frame.detail}</p>
          </div>
          <div className="sx-row" style={{ justifyContent: "space-between" }}>
            <a className="sx-source" href={`${SOURCE_ROOT}${frame.code.file}`} target="_blank" rel="noreferrer">
              {frame.code.member} · {frame.code.file}
            </a>
            <div className="sx-row">
              <button type="button" className="sx-button" onClick={() => go(index - 1)} disabled={index === 0} aria-label="Previous step">
                ←
              </button>
              <button
                type="button"
                className="sx-button"
                onClick={togglePlay}
                disabled={trace.frames.length < 2}
                aria-label={playing ? "Pause" : "Play steps"}
              >
                {playing ? "Pause" : "Play"}
              </button>
              <button
                type="button"
                className="sx-button"
                onClick={() => go(index + 1)}
                disabled={index >= trace.frames.length - 1}
                aria-label="Next step"
              >
                →
              </button>
              <button
                type="button"
                className="sx-button"
                onClick={() => go(trace.frames.length - 1)}
                disabled={index >= trace.frames.length - 1}
                aria-label="Last step"
              >
                End
              </button>
            </div>
          </div>
          <div className="sx-row" style={{ justifyContent: "space-between" }}>
            <div className="sx-dots" role="group" aria-label="Steps">
              {trace.frames.map((step, i) => (
                <button
                  type="button"
                  key={i}
                  className="sx-dot"
                  aria-label={`Step ${i + 1}: ${step.title}`}
                  aria-current={i === index ? "step" : undefined}
                  data-done={i < index ? "" : undefined}
                  onClick={() => go(i)}
                />
              ))}
            </div>
            <label className="sx-check">
              <input type="checkbox" checked={autoplay} onChange={(event) => setAutoplay(event.target.checked)} />
              autoplay new operations
            </label>
          </div>
        </section>

        <Stats world={world} focus={focus} />

        <section aria-label="Archetypes and chunks" className="sx-stack">
          <div className="sx-heading">
            <h3>Archetypes and chunks</h3>
            <span className="sx-meta">World.Archetypes, in creation order</span>
          </div>
          {world.archetypes.map((arch) => (
            <ArchetypeCard
              key={arch.index}
              arch={arch}
              world={world}
              previous={previous}
              focus={focus}
              selected={selected}
              flashKey={flashKey}
              onSelect={setSelected}
              onBytes={setPinned}
            />
          ))}
        </section>

        {mapChunk !== null && <ByteMap world={world} uid={mapChunk} row={focusRow} />}

        <div className="sx-split">
          <EntityTablePanel world={world} focus={focus} selected={selected} onSelect={setSelected} />
          <Inspector world={world} selected={selected} />
        </div>

        <section aria-label="Systems" className="sx-stack">
          <div className="sx-heading">
            <h3>Systems</h3>
            <span className="sx-meta">each Run is a schedule holding that one system</span>
          </div>
          <div className="sx-systems">
            {SYSTEMS.map((info) => {
              const state = world.systems[info.name];
              return (
                <div key={info.name} className="sx-card sx-system">
                  <strong className="sx-mono">{info.name}</strong>
                  <div className="sx-signature">{info.signature}</div>
                  <dl className="sx-kv">
                    <dt>LastRun</dt>
                    <dd>{state.lastRun}</dd>
                    {info.commands ? (
                      <>
                        <dt>buffer</dt>
                        <dd>
                          {world.commands.length} record{world.commands.length === 1 ? "" : "s"}
                        </dd>
                      </>
                    ) : (
                      <>
                        <dt>_seen</dt>
                        <dd>{state.seen}</dd>
                        <dt>matches</dt>
                        <dd>{state.matches.length ? state.matches.map((match) => `#${match.arch}`).join(" ") : "none yet"}</dd>
                      </>
                    )}
                  </dl>
                </div>
              );
            })}
          </div>
          {world.commands.length > 0 && (
            <div className="sx-card">
              <div className="sx-heading">
                <h4>Spawner's CommandBuffer</h4>
                <span className="sx-meta">{world.commands.length * 24} payload bytes</span>
              </div>
              <div className="sx-scroll">
                <table className="sx-table" data-focus={focus.commands ? "" : undefined}>
                  <thead>
                    <tr>
                      <th>#</th>
                      <th>Kind</th>
                      <th>Target</th>
                      <th>TypeKey</th>
                      <th>Offset</th>
                      <th>Length</th>
                    </tr>
                  </thead>
                  <tbody>
                    {world.commands.map((record, i) => (
                      <tr key={i}>
                        <td>{i}</td>
                        <td>Spawn</td>
                        <td>{handleLabel(record.target)}</td>
                        <td>Mover</td>
                        <td>{record.offset}</td>
                        <td>{record.length}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </section>

        <section aria-label="Operation history">
          <div className="sx-heading">
            <h3>History</h3>
            <span className="sx-meta">replay an earlier trace; new operations always apply to the latest state</span>
          </div>
          <div className="sx-log">
            {log.map((entry, i) => (
              <button
                type="button"
                key={entry.id}
                className="sx-chip"
                aria-current={i === view ? "true" : undefined}
                title={entry.call}
                onClick={() => {
                  setView(i);
                  setStep(Number.MAX_SAFE_INTEGER);
                  setPlaying(false);
                }}
              >
                {entry.call}
              </button>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}

function Toolbar(props: {
  selected: Handle | null;
  concurrency: number;
  onConcurrency: (value: number) => void;
  perform: (make: (world: World) => Trace) => void;
  reset: () => void;
}) {
  const { selected, perform } = props;
  return (
    <div className="sx-toolbar" role="group" aria-label="Operations">
      <div className="sx-row">
        <span className="sx-group-label">World</span>
        <button type="button" className="sx-button" data-primary="" onClick={() => perform((world) => spawnMovers(world, 1))}>
          Spawn(Mover)
        </button>
        <button type="button" className="sx-button" onClick={() => perform((world) => spawnMovers(world, 5))}>
          Spawn(Mover) ×5
        </button>
        <button type="button" className="sx-button" onClick={() => perform(spawnEmpty)}>
          Spawn()
        </button>
        <button type="button" className="sx-button" onClick={props.reset}>
          Reset
        </button>
      </div>
      <div className="sx-row">
        <span className="sx-group-label">{selected ? `e${selected.index} g${selected.gen}` : "Entity"}</span>
        {COMPONENTS.map((component) => (
          <span key={component.id} className="sx-row" style={{ gap: "2px" }}>
            <button
              type="button"
              className="sx-button"
              data-tone={component.tone}
              disabled={!selected}
              title={selected ? `world.Insert(e${selected.index}, new ${component.name}(…))` : "Select an entity first"}
              onClick={() => selected && perform((world) => insert(world, selected, component.id))}
            >
              +{component.name}
            </button>
            <button
              type="button"
              className="sx-button"
              data-tone={component.tone}
              disabled={!selected}
              title={selected ? `world.Remove<${component.name}>(e${selected.index})` : "Select an entity first"}
              onClick={() => selected && perform((world) => remove(world, selected, component.id))}
              aria-label={`Remove ${component.name}`}
            >
              −
            </button>
          </span>
        ))}
        <button
          type="button"
          className="sx-button"
          disabled={!selected}
          onClick={() => selected && perform((world) => despawn(world, selected))}
        >
          Despawn
        </button>
      </div>
      {!selected && <p className="sx-meta">Select an entity by clicking its cell in a chunk or its slot in the entity table.</p>}
      <div className="sx-row">
        <span className="sx-group-label">Schedule</span>
        {SYSTEMS.map((info) => (
          <button
            type="button"
            key={info.name}
            className="sx-button"
            title={info.summary}
            onClick={() => perform((world) => runSystem(world, info.name, props.concurrency))}
          >
            Run {info.name}
          </button>
        ))}
        <span className="sx-meta">MaxConcurrency</span>
        <span className="sx-seg" role="group" aria-label="MaxConcurrency">
          {[1, 2, 3, 4].map((value) => (
            <button type="button" key={value} aria-pressed={props.concurrency === value} onClick={() => props.onConcurrency(value)}>
              {value}
            </button>
          ))}
        </span>
      </div>
    </div>
  );
}

function Stats({ world, focus }: { world: World; focus: Focus }) {
  const free = Object.values(world.pool.free).reduce((sum, stack) => sum + stack.length, 0);
  return (
    <dl className="sx-stats">
      <div className="sx-stat" data-focus={focus.tick ? "" : undefined}>
        <dt>World tick</dt>
        <dd>{world.tick}</dd>
      </div>
      <div className="sx-stat" data-focus={focus.pool ? "" : undefined}>
        <dt>NativeBytes</dt>
        <dd>{world.pool.allocated.toLocaleString("en-US")}</dd>
      </div>
      <div className="sx-stat" data-focus={focus.pool ? "" : undefined}>
        <dt>Blocks / pooled</dt>
        <dd>
          {world.pool.owned} / {free}
        </dd>
      </div>
      <div className="sx-stat">
        <dt>Archetypes</dt>
        <dd>{world.archetypes.length}</dd>
      </div>
      <div className="sx-stat">
        <dt>Alive</dt>
        <dd>{liveEntities(world)}</dd>
      </div>
    </dl>
  );
}

function ArchetypeCard(props: {
  arch: Archetype;
  world: World;
  previous: World | null;
  focus: Focus;
  selected: Handle | null;
  flashKey: string;
  onSelect: (handle: Handle) => void;
  onBytes: (uid: number) => void;
}) {
  const { arch, world, focus } = props;
  const matching = focus.matched !== undefined;
  const matched = matching && focus.matched!.includes(arch.index);
  const edges = [
    ...Object.entries(arch.add).map(([id, target]) => `+${COMPONENTS[Number(id)].name} → #${target}`),
    ...Object.entries(arch.remove).map(([id, target]) => `−${COMPONENTS[Number(id)].name} → #${target}`),
  ];
  return (
    <article
      className="sx-card sx-arch"
      data-focus={focus.archetypes?.includes(arch.index) ? "" : undefined}
      data-matched={matched ? "" : undefined}
      data-unmatched={matching && !matched ? "" : undefined}
      aria-label={`Archetype ${arch.index} ${names(arch.ids)}`}
    >
      <div className="sx-arch-head">
        <span className="sx-arch-id">#{arch.index}</span>
        {arch.ids.length === 0 ? (
          <span className="sx-chip">empty</span>
        ) : (
          arch.ids.map((id) => (
            <span key={id} className="sx-chip" data-tone={COMPONENTS[id].tone}>
              {COMPONENTS[id].name}
              {COMPONENTS[id].tracked ? " ·T" : COMPONENTS[id].tag ? " ·tag" : ""}
            </span>
          ))
        )}
        <span className="sx-meta">
          key "{arch.key}" · mask {maskBits(arch.ids)} · {arch.layout.capacity} rows · {arch.layout.bytes} B
        </span>
        {matched && <span className="sx-chip" data-tone="acc">matched</span>}
      </div>
      <div className="sx-edges">
        <span>{arch.origin}</span>
        {edges.length > 0 && <span>· edges: {edges.join(", ")}</span>}
      </div>
      {arch.chunks.length === 0 ? (
        <p className="sx-meta">No chunks. Archetypes are never deleted, so an empty one stays interned and keeps its index.</p>
      ) : (
        <div className="sx-chunks">
          {arch.chunks.map((uid) => (
            <ChunkView
              key={uid}
              chunk={world.chunks[uid]}
              arch={arch}
              ordinal={chunkOrdinal(world, uid)}
              previous={props.previous?.chunks[uid] ?? null}
              focus={focus}
              selected={props.selected}
              flashKey={props.flashKey}
              onSelect={props.onSelect}
              onBytes={props.onBytes}
            />
          ))}
        </div>
      )}
    </article>
  );
}

function ChunkView(props: {
  chunk: Chunk;
  arch: Archetype;
  ordinal: number;
  previous: Chunk | null;
  focus: Focus;
  selected: Handle | null;
  flashKey: string;
  onSelect: (handle: Handle) => void;
  onBytes: (uid: number) => void;
}) {
  const { chunk, arch, previous, focus, selected } = props;
  const capacity = arch.layout.capacity;
  const roles = new Map<number, Role>();
  for (const cell of focus.cells ?? []) if (cell.chunk === chunk.uid) roles.set(cell.row, cell.role);
  const worker = focus.workers?.[chunk.uid];
  const lines: number[][] = [];
  for (let start = 0; start < capacity; start += ROWS_PER_LINE)
    lines.push(Array.from({ length: Math.min(ROWS_PER_LINE, capacity - start) }, (_, i) => start + i));

  const isSelected = (row: number) => {
    const key = chunk.keys[row];
    return row < chunk.count && key !== null && selected !== null && key.index === selected.index && key.gen === selected.gen;
  };
  const state = (row: number, empty: boolean) => (row < chunk.count ? "live" : empty ? "zero" : "stale");
  const common = (row: number, changed: boolean, tone: string) => ({
    "data-tone": tone,
    "data-role": roles.get(row),
    "data-selected": isSelected(row) ? "" : undefined,
    "data-flash": changed ? "" : undefined,
  });

  return (
    <div
      className="sx-chunk"
      data-worker={worker}
      data-focus={focus.chunk === chunk.uid ? "" : undefined}
      aria-label={`Chunk ${props.ordinal} of archetype ${arch.index}, ${chunk.count} of ${capacity} rows`}
    >
      <div className="sx-chunk-head">
        <strong>chunk {props.ordinal}</strong>
        <span>block #{chunk.block}</span>
        <span>
          Count {chunk.count}/{capacity}
        </span>
        {worker !== undefined && (
          <span className="sx-worker" data-worker={worker}>
            W{worker}
          </span>
        )}
        <button type="button" className="sx-button" style={{ minHeight: "1.5rem", padding: "0 0.45rem", fontSize: "0.6875rem" }} onClick={() => props.onBytes(chunk.uid)}>
          bytes
        </button>
      </div>
      {lines.map((rows) => (
        <div key={rows[0]} className="sx-grid" style={vars({ "--cols": rows.length })}>
          <span className="sx-lab" aria-hidden="true" />
          {rows.map((row) => (
            <span key={row} className="sx-index" aria-hidden="true">
              {row}
            </span>
          ))}
          <span className="sx-lab" data-tone="entity">
            Entity
          </span>
          {rows.map((row) => {
            const key = chunk.keys[row];
            const changed = previous !== null && !same(previous.keys[row], key) && row < chunk.count;
            const live = row < chunk.count && key !== null;
            return (
              <button
                type="button"
                key={changed ? `e${row}-${props.flashKey}` : `e${row}`}
                className="sx-cell"
                data-state={state(row, key === null)}
                {...common(row, changed, "entity")}
                disabled={!live}
                title={key ? `row ${row}: EntityKey(${key.index}, ${key.gen})${live ? "" : ", stale bytes past Count"}` : `row ${row}: zeroed`}
                aria-label={live ? `Select entity ${key!.index} generation ${key!.gen}` : `row ${row} unused`}
                onClick={() => live && props.onSelect({ world: 1, index: key!.index, gen: key!.gen })}
              >
                {key ? key.index : ""}
              </button>
            );
          })}
          {arch.ids.map((id, column) => {
            const component = COMPONENTS[id];
            if (component.tag)
              return [
                <span key={`l${id}`} className="sx-lab" data-tone={component.tone}>
                  {component.name}
                </span>,
                <span key={`t${id}`} className="sx-tagline">
                  tag: membership in the mask, no value column
                </span>,
              ];
            const strips: [string, (row: number) => CellValue, (c: Chunk) => CellValue[]][] = [
              [component.name, (row) => chunk.values[column][row], (c) => c.values[column]],
            ];
            if (component.tracked) {
              strips.push(["↳ Added", (row) => chunk.added[column][row], (c) => c.added[column]]);
              strips.push(["↳ Changed", (row) => chunk.changed[column][row], (c) => c.changed[column]]);
            }
            return strips.flatMap(([label, read, column$]) => [
              <span key={`l${id}${label}`} className="sx-lab" data-tone={component.tone} title={label}>
                {label}
              </span>,
              ...rows.map((row) => {
                const stored = read(row);
                // Rent zero-fills the block, so a live row whose column was not written yet reads as zeros.
                const value: CellValue = stored === null && row < chunk.count ? (component.size === 12 ? ([0, 0, 0] as Vec) : 0) : stored;
                const changed = previous !== null && row < chunk.count && !same(column$(previous)[row], stored);
                const empty = stored === null || stored === 0;
                return (
                  <span
                    key={changed ? `${id}${label}${row}-${props.flashKey}` : `${id}${label}${row}`}
                    className="sx-cell"
                    data-state={state(row, empty)}
                    {...common(row, changed, component.tone)}
                    title={`${label === component.name ? component.name : `${component.name} ${label.slice(2)}`}[${row}] = ${formatValue(value)}${row < chunk.count ? "" : " (past Count)"}`}
                  >
                    {cellText(id, value as CellValue)}
                  </span>
                );
              }),
            ]);
          })}
        </div>
      ))}
    </div>
  );
}

function ByteMap({ world, uid, row }: { world: World; uid: number; row: number | null }) {
  const chunk = world.chunks[uid];
  const arch = world.archetypes[chunk.arch];
  const components = arch.ids.map((id) => COMPONENTS[id]);
  const regions = layoutRegions(arch.layout, components);
  const lines = arch.layout.bytes / 64;
  const tone = (slot: number, kind: string) => (kind === "pad" ? "pad" : slot < 0 ? "entity" : components[slot].tone);
  const label = (slot: number, kind: string) =>
    kind === "pad" ? "padding" : kind === "entity" ? "Entity" : `${components[slot].name}${kind === "value" ? "" : `.${kind === "added" ? "Added" : "Changed"}`}`;

  const bytes = [];
  let cursor = 0;
  for (let offset = 0; offset < arch.layout.bytes; offset++) {
    if (offset % 64 === 0)
      bytes.push(
        <span key={`o${offset}`} className="sx-byte-off">
          {offset}
        </span>,
      );
    while (offset >= regions[cursor].end) cursor++;
    const region = regions[cursor];
    const element = region.kind === "pad" ? -1 : Math.floor((offset - region.start) / region.stride);
    bytes.push(
      <span
        key={offset}
        className="sx-byte"
        data-kind={region.kind}
        data-tone={tone(region.slot, region.kind)}
        data-live={region.kind === "pad" ? undefined : String(element < chunk.count)}
        data-row={row !== null && element === row ? "" : undefined}
        title={`byte ${offset}: ${label(region.slot, region.kind)}${element >= 0 ? `[${element}]` : ""}`}
      />,
    );
  }

  const footprint =
    row === null
      ? null
      : regions
          .filter((r) => r.kind !== "pad")
          .map((r) => `${r.start + row * r.stride}–${r.start + (row + 1) * r.stride - 1}`)
          .join(", ");
  const key = row !== null ? chunk.keys[row] : null;

  return (
    <section className="sx-card" aria-label="Chunk byte map">
      <div className="sx-heading">
        <h3>
          Byte map · #{arch.index} chunk {chunkOrdinal(world, uid)} · block #{chunk.block}
        </h3>
        <span className="sx-meta">
          {arch.layout.bytes} B = {lines} cache lines · each square is 1 byte
        </span>
      </div>
      <div className="sx-bytes" role="img" aria-label={`Native memory of the chunk: ${regions.map((r) => `${label(r.slot, r.kind)} ${r.start} to ${r.end - 1}`).join("; ")}`}>
        {bytes}
      </div>
      <div className="sx-regions">
        {regions.map((r) => (
          <div key={r.start} className="sx-region">
            <span className="sx-swatch" data-kind={r.kind} data-tone={tone(r.slot, r.kind)} />
            <span>{label(r.slot, r.kind)}</span>
            <span className="sx-muted">
              {r.start}–{r.end - 1}
              {r.kind === "pad" ? ` · ${r.end - r.start} B to the next 64-byte boundary` : ` · ${r.stride} B × ${arch.layout.capacity} rows`}
            </span>
          </div>
        ))}
      </div>
      {footprint && (
        <p className="sx-meta" style={{ marginTop: "0.5rem" }}>
          Row {row}
          {key && row! < chunk.count ? ` (e${key.index})` : ""} is not stored in one place: its bytes are {footprint}. Each column is its own contiguous array, which is what lets a system stream one span per component.
        </p>
      )}
    </section>
  );
}

function EntityTablePanel(props: { world: World; focus: Focus; selected: Handle | null; onSelect: (handle: Handle) => void }) {
  const { world, focus, selected } = props;
  const table = world.table;
  return (
    <section className="sx-card" aria-label="Entity table">
      <div className="sx-heading">
        <h3>EntityTable</h3>
        <span className="sx-meta">
          {table.pages} page{table.pages === 1 ? "" : "s"} of 1024 · _count {table.count}
        </span>
      </div>
      <div className="sx-slots">
        {table.slots.map((slot, index) => (
          <button
            type="button"
            key={index}
            className="sx-slot"
            data-state={slot.state}
            data-focus={focus.slots?.includes(index) ? "" : undefined}
            data-selected={selected?.index === index ? "" : undefined}
            title={`slot ${index}: generation ${slot.gen}, ${slot.state}${slot.arch !== null ? `, #${slot.arch} chunk ${chunkOrdinal(world, slot.chunk!)} row ${slot.row}` : ""}`}
            onClick={() => props.onSelect(slotHandle(world, index, slot))}
          >
            <span>{index}</span>
            <small>g{slot.gen}</small>
          </button>
        ))}
      </div>
      <p className="sx-meta" style={{ marginTop: "0.5rem" }}>
        free stack: {table.free.length ? `[${table.free.join(", ")}] ← top` : "empty"}
      </p>
      <div className="sx-legend">
        <span data-state="Free">Free</span>
        <span data-state="Reserved">Reserved</span>
        <span data-state="Alive">Alive</span>
        <span data-state="Retired">Retired</span>
      </div>
    </section>
  );
}

function Inspector({ world, selected }: { world: World; selected: Handle | null }) {
  if (!selected)
    return (
      <section className="sx-card" aria-label="Handle inspector">
        <div className="sx-heading">
          <h3>Handle</h3>
        </div>
        <p className="sx-muted">
          An Entity is three fields: WorldId, Index and Generation. Select one to see how EntityTable.IsAlive resolves it.
        </p>
      </section>
    );
  const checks = aliveChecks(world, selected);
  const failed = checks.findIndex((check) => !check.ok);
  const row = rowValues(world, selected);
  return (
    <section className="sx-card" aria-label="Handle inspector">
      <div className="sx-heading">
        <h3>Handle</h3>
        <span className="sx-mono">Entity{handleLabel(selected)}</span>
      </div>
      <p className="sx-meta" style={{ marginBottom: "0.35rem" }}>
        IsAlive evaluates, short-circuiting:
      </p>
      <ul className="sx-checks">
        {checks.map((check, i) => (
          <li key={i} data-ok={failed >= 0 && i > failed ? "skip" : String(check.ok)}>
            {check.label}
          </li>
        ))}
      </ul>
      {row ? (
        <dl className="sx-kv" style={{ marginTop: "0.6rem" }}>
          <dt>entry</dt>
          <dd>
            #{row.arch.index} chunk {chunkOrdinal(world, row.chunk.uid)} row {row.row}
          </dd>
          {row.values.map(({ component, value, added, changed }) => (
            <span key={component.id} style={{ display: "contents" }}>
              <dt>{component.name}</dt>
              <dd>
                {component.tag ? "tag" : formatValue(value as Vec | number)}
                {component.tracked ? ` · Added ${added} · Changed ${changed}` : ""}
              </dd>
            </span>
          ))}
        </dl>
      ) : (
        <p className="sx-muted" style={{ marginTop: "0.6rem" }}>
          {failed === 2
            ? "The slot was reused or released, so this handle is stale. Every API that takes it either throws or returns false."
            : failed === 3
              ? "The slot is not Alive. A Reserved slot comes from Commands.Spawn and stays invisible until barrier playback."
              : "This handle does not resolve to a row."}
        </p>
      )}
    </section>
  );
}
