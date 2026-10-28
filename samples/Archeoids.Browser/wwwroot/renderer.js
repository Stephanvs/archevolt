const width = 1280;
const height = 800;
const dim = "#4f6e7d";
const white = "#dbeeed";
const amber = "#ffa642";
const cyan = "#66f2e6";

export async function createRenderer(canvas, overlay, fail) {
    if (!navigator.gpu)
        throw new Error("WebGPU is unavailable. Open this game in a WebGPU-capable browser over HTTPS or localhost.");
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter)
        throw new Error("No WebGPU adapter is available. Check your browser's hardware acceleration and graphics driver.");
    const device = await adapter.requestDevice();
    device.lost.then(info => fail(new Error(`The WebGPU device was lost (${info.reason}). Reload to try again.`)));
    device.addEventListener("uncapturederror", event => fail(event.error));

    const context = canvas.getContext("webgpu");
    const text = overlay.getContext("2d");
    if (!context || !text) throw new Error("The browser could not create the game canvases.");
    const format = navigator.gpu.getPreferredCanvasFormat();
    context.configure({ device, format, alphaMode: "opaque" });

    const shader = device.createShaderModule({ code: `
        struct VertexOutput {
            @builtin(position) position: vec4f,
            @location(0) color: vec4f,
        }
        @vertex fn vertex(@location(0) position: vec2f, @location(1) color: vec4f) -> VertexOutput {
            var result: VertexOutput;
            result.position = vec4f(position.x / 640.0 - 1.0, 1.0 - position.y / 400.0, 0.0, 1.0);
            result.color = color;
            return result;
        }
        @fragment fn fragment(input: VertexOutput) -> @location(0) vec4f {
            return input.color;
        }
    ` });
    const pipeline = await device.createRenderPipelineAsync({
        layout: "auto",
        vertex: {
            module: shader, entryPoint: "vertex",
            buffers: [{ arrayStride: 24, attributes: [
                { shaderLocation: 0, offset: 0, format: "float32x2" },
                { shaderLocation: 1, offset: 8, format: "float32x4" },
            ] }],
        },
        fragment: {
            module: shader, entryPoint: "fragment",
            targets: [{ format, blend: {
                color: { srcFactor: "src-alpha", dstFactor: "one-minus-src-alpha" },
                alpha: { srcFactor: "one", dstFactor: "one-minus-src-alpha" },
            } }],
        },
        primitive: { topology: "triangle-list" },
    });

    try {
        const fonts = await Promise.all([
            document.fonts.load('400 16px "Barlow"'),
            document.fonts.load('600 32px "Barlow"'),
        ]);
        if (fonts.some(faces => faces.length === 0)) throw new Error("Font faces are missing.");
    } catch (cause) {
        throw new Error("The bundled HUD font could not be loaded. Reload to try again.", { cause });
    }

    let staging = new Uint8Array(0);
    let vertexBuffer;
    function label(value, x, y, size, color, weight = 400, align = "left") {
        text.font = `${weight} ${size}px Barlow`;
        text.fillStyle = color;
        const measured = text.measureText(value).width;
        text.fillText(value, x - (align === "center" ? measured / 2 : align === "right" ? measured : 0), y);
    }

    return function render(vertices, score, wave, lives, entities, milliseconds, paused) {
        // The MemoryView expires when this synchronous import returns. Copy only the active
        // bytes into reusable storage; never retain a view into a movable WASM heap.
        const byteLength = vertices.length;
        if (byteLength > staging.length) {
            const capacity = 2 ** Math.ceil(Math.log2(Math.max(byteLength, 65536)));
            staging = new Uint8Array(capacity);
            vertexBuffer?.destroy();
            vertexBuffer = device.createBuffer({ size: capacity, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST });
        }
        vertices.copyTo(staging);
        device.queue.writeBuffer(vertexBuffer, 0, staging, 0, byteLength);

        // Read DPR each frame so moving between monitors and browser zoom need no special listener.
        const bounds = canvas.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        const limit = device.limits.maxTextureDimension2D;
        const pixelScale = Math.min(dpr, limit / Math.max(bounds.width, 1), limit / Math.max(bounds.height, 1));
        const w = Math.max(1, Math.floor(bounds.width * pixelScale));
        const h = Math.max(1, Math.floor(bounds.height * pixelScale));
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = overlay.width = w;
            canvas.height = overlay.height = h;
        }
        const scale = Math.min(w / width, h / height);
        const vw = Math.max(1, Math.floor(width * scale));
        const vh = Math.max(1, Math.floor(height * scale));
        const x = Math.floor((w - vw) / 2);
        const y = Math.floor((h - vh) / 2);
        const encoder = device.createCommandEncoder();
        const pass = encoder.beginRenderPass({ colorAttachments: [{
            view: context.getCurrentTexture().createView(),
            clearValue: { r: 0.018, g: 0.032, b: 0.049, a: 1 },
            loadOp: "clear", storeOp: "store",
        }] });
        pass.setPipeline(pipeline);
        pass.setViewport(x, y, vw, vh, 0, 1);
        pass.setVertexBuffer(0, vertexBuffer);
        pass.draw(byteLength / 24);
        pass.end();
        device.queue.submit([encoder.finish()]);

        text.resetTransform();
        text.clearRect(0, 0, w, h);
        text.setTransform(vw / width, 0, 0, vh / height, x, y);
        text.textBaseline = "top";
        label("ARCHEOIDS", 34, 18, 42, amber, 600);
        label("ARCHEVOLT / WEBGPU", 36, 70, 15, dim);
        label("SCORE", 590, 25, 14, dim);
        label(String(score).padStart(6, "0"), 590, 46, 32, white, 600);
        label("SECTOR", 830, 25, 14, dim);
        label(String(Math.max(wave, 1)).padStart(2, "0"), 830, 46, 32, white, 600);
        label("HULL", 1070, 25, 14, dim);
        label("A/D  TURN    W  THRUST    SPACE  FIRE    P  PAUSE    R  RESTART", 36, 764, 15, dim);
        label(`${entities} ENTITIES / ${milliseconds.toFixed(2)} MS`, 1246, 764, 15, cyan, 400, "right");
        if (paused || lives === 0) {
            label(paused ? "PAUSED" : "SIGNAL LOST", 640, 343, 64, paused ? white : amber, 600, "center");
            label(paused ? "PRESS P TO RESUME" : "PRESS R TO LAUNCH AGAIN", 640, 430, 21, dim, 400, "center");
        } else if (wave === 0) {
            label("CLEAR THE SECTOR", 640, 245, 30, white, 600, "center");
            label("THRUST. DRIFT. SURVIVE.", 640, 292, 16, dim, 400, "center");
        }
    };
}
