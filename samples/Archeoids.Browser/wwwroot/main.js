import { createRenderer } from "./renderer.js";

const canvas = document.querySelector("#game");
const status = document.querySelector("#status");
let failed = false;
let animation;

function fail(error) {
    if (failed) return;
    failed = true;
    cancelAnimationFrame(animation);
    console.error(error);
    status.textContent = `Archeoids could not continue. ${error.message || error}`;
    status.hidden = false;
}

try {
    const render = await createRenderer(canvas, document.querySelector("#hud"), fail);
    const { dotnet } = await import("./_framework/dotnet.js");
    const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();
    setModuleImports("archeoids", { render });
    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    await runMain();
    const game = exports.Archeoids.Program;
    const keys = new Set();
    const gameKeys = new Set(["KeyA", "KeyD", "ArrowLeft", "ArrowRight", "KeyW", "ArrowUp", "Space", "KeyP", "KeyR"]);
    let lastTime;

    function resetInput() {
        keys.clear();
        lastTime = undefined;
        game.ResetClock();
    }
    canvas.addEventListener("pointerdown", () => canvas.focus({ preventScroll: true }));
    canvas.addEventListener("keydown", event => {
        if (failed || !gameKeys.has(event.code) || event.ctrlKey || event.metaKey || event.altKey) return;
        event.preventDefault();
        keys.add(event.code);
        if (event.repeat) return;
        if (event.code === "KeyP") { game.TogglePause(); lastTime = undefined; }
        if (event.code === "KeyR") { game.Restart(); lastTime = undefined; }
    });
    window.addEventListener("keyup", event => { keys.delete(event.code); });
    canvas.addEventListener("blur", resetInput);
    canvas.addEventListener("focus", resetInput);
    window.addEventListener("blur", resetInput);
    window.addEventListener("focus", resetInput);
    document.addEventListener("visibilitychange", resetInput);

    function frame(time) {
        if (failed) return;
        try {
            const active = document.activeElement === canvas && document.hasFocus() && !document.hidden;
            const delta = active && lastTime !== undefined ? (time - lastTime) / 1000 : 0;
            lastTime = active ? time : undefined;
            const left = keys.has("KeyA") || keys.has("ArrowLeft");
            const right = keys.has("KeyD") || keys.has("ArrowRight");
            game.Frame(delta, Number(right) - Number(left), keys.has("KeyW") || keys.has("ArrowUp"), keys.has("Space"));
            animation = requestAnimationFrame(frame);
        } catch (error) { fail(error); }
    }
    if (!failed) {
        status.hidden = true;
        canvas.focus({ preventScroll: true });
        animation = requestAnimationFrame(frame);
    }
} catch (error) { fail(error); }
