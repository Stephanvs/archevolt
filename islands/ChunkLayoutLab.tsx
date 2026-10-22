import { useState, type CSSProperties } from "react";
import "./storage/storage.css";
import { chunkLayout, layoutRegions, type ComponentInfo } from "./storage/engine";

const PALETTE: readonly ComponentInfo[] = [
  { id: 0, name: "Position", size: 12, tracked: false, tag: false, tone: "pos" },
  { id: 1, name: "Velocity", size: 12, tracked: false, tag: false, tone: "vel" },
  { id: 2, name: "Health", size: 4, tracked: true, tag: false, tone: "hp" },
  { id: 3, name: "Acceleration", size: 4, tracked: false, tag: false, tone: "acc" },
  { id: 4, name: "WidePayload", size: 256, tracked: false, tag: false, tone: "wide" },
  { id: 5, name: "Frozen", size: 0, tracked: false, tag: true, tone: "tag" },
];

const TARGETS = [128, 256, 384, 512, 1024, 4096, 16384];

interface Preset {
  label: string;
  target: number;
  on: number[];
  tracked: number[];
  note: string;
}

const PRESETS: readonly Preset[] = [
  {
    label: "Benchmark movers",
    target: 16384,
    on: [0, 1],
    tracked: [],
    note: "Position and Velocity at the default 16 KiB. 512 rows per chunk is the layout behind the 100,000-entity benchmark: 196 chunks, 3,211,264 native bytes.",
  },
  {
    label: "Tracked Position",
    target: 16384,
    on: [0, 1],
    tracked: [0],
    note: "Tracking adds an 8-byte Added and an 8-byte Changed column. 341 rows pad past 16 KiB, so the loop steps down to 336.",
  },
  {
    label: "Wide payload",
    target: 16384,
    on: [0, 1, 4],
    tracked: [],
    note: "One 256-byte component cuts capacity to 56 rows. The chunk is 16,192 B, so the pool's bucket for it holds 16,192-byte blocks.",
  },
  {
    label: "Test-sized chunk",
    target: 256,
    on: [0, 1],
    tracked: [0],
    note: "The storage tests use WithChunkBytes(256) with tracked Position and Velocity. Five columns at a 64-byte minimum need 320 B, so every chunk holds one row and exceeds the target.",
  },
  {
    label: "This page's world",
    target: 384,
    on: [0, 1],
    tracked: [],
    note: "The explorer above uses 384-byte chunks. 12 and 11 rows pad past the target, so the Mover archetype holds 10.",
  },
];

const bytes = (value: number) => `${value.toLocaleString("en-US")} B`;

export default function ChunkLayoutLab() {
  const [on, setOn] = useState<number[]>(PRESETS[1].on);
  const [tracked, setTracked] = useState<number[]>(PRESETS[1].tracked);
  const [target, setTarget] = useState(PRESETS[1].target);
  const [preset, setPreset] = useState<number | null>(1);

  const components = PALETTE.filter((component) => on.includes(component.id)).map((component) => ({
    ...component,
    tracked: tracked.includes(component.id),
  }));
  const layout = chunkLayout(components, target);
  const regions = layoutRegions(layout, components);
  const padding = regions.filter((region) => region.kind === "pad").reduce((sum, region) => sum + region.end - region.start, 0);
  const first = layout.attempts[0].capacity;
  const over = layout.bytes > target;
  const span = Math.max(layout.bytes, target);
  const lines = layout.bytes / 64;
  const attempts =
    layout.attempts.length > 9 ? [...layout.attempts.slice(0, 4), null, ...layout.attempts.slice(-4)] : layout.attempts;

  const label = (slot: number, kind: string) =>
    kind === "pad" ? "padding" : kind === "entity" ? "Entity" : `${components[slot].name}${kind === "value" ? "" : kind === "added" ? ".Added" : ".Changed"}`;
  const tone = (slot: number, kind: string) => (kind === "pad" ? "pad" : slot < 0 ? "entity" : components[slot].tone);

  function apply(index: number) {
    const chosen = PRESETS[index];
    setOn(chosen.on);
    setTracked(chosen.tracked);
    setTarget(chosen.target);
    setPreset(index);
  }

  function toggle(list: number[], id: number) {
    return list.includes(id) ? list.filter((other) => other !== id) : [...list, id];
  }

  return (
    <div className="sx not-prose" aria-label="Chunk layout lab">
      <div className="sx-stack">
        <div className="sx-row" role="group" aria-label="Presets">
          <span className="sx-group-label">Presets</span>
          {PRESETS.map((entry, index) => (
            <button
              type="button"
              key={entry.label}
              className="sx-chip"
              aria-pressed={preset === index}
              onClick={() => apply(index)}
            >
              {entry.label}
            </button>
          ))}
        </div>

        <div className="sx-palette" role="group" aria-label="Components, in component-ID order">
          {PALETTE.map((component) => {
            const active = on.includes(component.id);
            return (
              <div key={component.id} className="sx-palette-item">
                <button
                  type="button"
                  className="sx-chip"
                  data-tone={component.tone}
                  aria-pressed={active}
                  onClick={() => {
                    setOn(toggle(on, component.id));
                    setPreset(null);
                  }}
                >
                  {component.name} · {component.tag ? "tag" : `${component.size} B`}
                </button>
                <label className="sx-check">
                  <input
                    type="checkbox"
                    disabled={!active}
                    checked={tracked.includes(component.id)}
                    onChange={() => {
                      setTracked(toggle(tracked, component.id));
                      setPreset(null);
                    }}
                  />
                  [TrackChanges]
                </label>
              </div>
            );
          })}
        </div>

        <div className="sx-row">
          <span className="sx-group-label">WithChunkBytes</span>
          <span className="sx-seg" role="group" aria-label="Target chunk bytes">
            {TARGETS.map((value) => (
              <button
                type="button"
                key={value}
                aria-pressed={target === value}
                onClick={() => {
                  setTarget(value);
                  setPreset(null);
                }}
              >
                {value >= 1024 ? `${value / 1024}K` : value}
              </button>
            ))}
          </span>
          <input
            className="sx-number"
            type="number"
            min={64}
            max={1048576}
            step={64}
            value={target}
            aria-label="Custom target bytes"
            onChange={(event) => {
              const value = Number(event.target.value);
              if (Number.isFinite(value)) {
                setTarget(Math.min(1048576, Math.max(64, Math.round(value))));
                setPreset(null);
              }
            }}
          />
        </div>

        {preset !== null && <p className="sx-muted">{PRESETS[preset].note}</p>}

        <div className="sx-card sx-stack">
          <p className="sx-equation">
            rowBytes = 8 (EntityKey)
            {components.map((component) => (
              <span key={component.id}>
                {" + "}
                {component.size}
                {component.tracked ? " + 16" : ""} ({component.name}
                {component.tracked ? ", tracked" : ""})
              </span>
            ))}{" "}
            = <b>{layout.rowBytes}</b>
            <br />
            first capacity = max(1, ⌊{target.toLocaleString("en-US")} / {layout.rowBytes}⌋) = <b>{first}</b>
          </p>
          <div>
            <p className="sx-meta" style={{ marginBottom: "0.3rem" }}>
              Each try aligns every column to 64 bytes. While the total exceeds the target and capacity is above 1, capacity steps down by one:
            </p>
            <div className="sx-attempts">
              {attempts.map((attempt, index) =>
                attempt === null ? (
                  <span key="gap" className="sx-attempt">
                    …
                  </span>
                ) : (
                  <span key={attempt.capacity} className="sx-attempt" data-ok={String(attempt === layout.attempts[layout.attempts.length - 1])}>
                    {attempt.capacity} rows → {bytes(attempt.bytes)}
                  </span>
                ),
              )}
            </div>
          </div>

          <dl className="sx-stats sx-tiles">
            <div className="sx-stat">
              <dt>Capacity</dt>
              <dd>{layout.capacity} rows</dd>
            </div>
            <div className="sx-stat">
              <dt>Layout.Bytes</dt>
              <dd>{bytes(layout.bytes)}</dd>
            </div>
            <div className="sx-stat">
              <dt>Alignment padding</dt>
              <dd>{bytes(padding)}</dd>
            </div>
            <div className="sx-stat">
              <dt>{over ? "Over target" : "Under target"}</dt>
              <dd>{bytes(Math.abs(target - layout.bytes))}</dd>
            </div>
          </dl>

          <div>
            <div
              className="sx-bar"
              role="img"
              aria-label={`Chunk layout: ${regions.map((region) => `${label(region.slot, region.kind)} from ${region.start} to ${region.end - 1}`).join("; ")}`}
            >
              {regions.map((region) => (
                <span
                  key={region.start}
                  className="sx-bar-region"
                  data-kind={region.kind}
                  data-tone={tone(region.slot, region.kind)}
                  style={{ flexGrow: region.end - region.start, flexBasis: 0 }}
                  title={`${label(region.slot, region.kind)}: ${region.start}–${region.end - 1} (${region.end - region.start} B)`}
                />
              ))}
              {!over && target > layout.bytes && (
                <span className="sx-bar-region" data-kind="pad" style={{ flexGrow: target - layout.bytes, flexBasis: 0, opacity: 0.35 }} title="Unallocated: the block is only Layout.Bytes long" />
              )}
              {lines <= 96 && <span className="sx-bar-lines" style={{ "--line": `${(64 / span) * 100}%` } as CSSProperties} />}
              {over && <span className="sx-target" style={{ left: `${(target / span) * 100}%` }} title={`target ${target} B`} />}
            </div>
            <div className="sx-byte-axis">
              <span>0</span>
              <span>{lines <= 96 ? `gridlines every 64 B` : `${lines.toLocaleString("en-US")} cache lines`}</span>
              <span>{span.toLocaleString("en-US")}</span>
            </div>
          </div>

          <div className="sx-scroll">
            <table className="sx-table">
              <thead>
                <tr>
                  <th>Column</th>
                  <th>Offset</th>
                  <th>Length</th>
                  <th>Layout array</th>
                </tr>
              </thead>
              <tbody>
                {regions.map((region) => (
                  <tr key={region.start}>
                    <td>
                      <span className="sx-swatch" data-kind={region.kind} data-tone={tone(region.slot, region.kind)} style={{ display: "inline-block", marginRight: "0.4rem", verticalAlign: "-1px" }} />
                      {label(region.slot, region.kind)}
                    </td>
                    <td>{region.start.toLocaleString("en-US")}</td>
                    <td>
                      {region.kind === "pad"
                        ? bytes(region.end - region.start)
                        : `${region.stride} × ${layout.capacity} = ${bytes(region.end - region.start)}`}
                    </td>
                    <td className="sx-muted">
                      {region.kind === "entity"
                        ? "offset 0"
                        : region.kind === "pad"
                          ? "—"
                          : `${region.kind === "value" ? "Values" : region.kind === "added" ? "Added" : "Changed"}[${region.slot}]`}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
