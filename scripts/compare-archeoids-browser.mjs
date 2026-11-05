import { readFileSync } from "node:fs";

const paths = process.argv.slice(2);
if (paths.length !== 2) {
    throw new Error("Usage: node scripts/compare-archeoids-browser.mjs <baseline.json> <candidate.json>");
}

const measurements = [];
for (const path of paths) {
    let result = JSON.parse(readFileSync(path, "utf8"));
    if (typeof result === "string") result = JSON.parse(result);
    const measurement = {
        frames: result.denseFrames,
        framesPerSecond: result.denseAverageFps,
        medianFrameMilliseconds: result.denseMedianFrameMs,
        peakEntities: result.peak?.values?.[1],
    };
    for (const [name, value] of Object.entries(measurement)) {
        if (!Number.isFinite(value) || value <= 0) {
            throw new Error(`${path}: ${name} must be a positive finite number`);
        }
    }
    if (measurement.frames < 60 || measurement.peakEntities < 800) {
        throw new Error(`${path}: need at least 60 dense frames and 800 concurrent entities`);
    }
    measurements.push(measurement);
}

const [baseline, candidate] = measurements;
if (candidate.peakEntities < baseline.peakEntities * 0.95) {
    throw new Error("The candidate reduced peak entity load by more than 5%; rerun comparable workloads");
}
if (candidate.medianFrameMilliseconds > 1000 / 60) {
    throw new Error("The candidate exceeds the 60 FPS median frame budget in dense combat");
}

console.log(JSON.stringify({
    baseline,
    candidate,
    framesPerSecondRatio: candidate.framesPerSecond / baseline.framesPerSecond,
    medianFrameTimeReductionPercent: (1 - candidate.medianFrameMilliseconds / baseline.medianFrameMilliseconds) * 100,
}, null, 2));
