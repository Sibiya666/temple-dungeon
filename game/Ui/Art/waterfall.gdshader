shader_type canvas_item;
render_mode unshaded;
uniform float level : hint_range(0.0, 1.0) = 0.0;
uniform vec4 water_color : source_color = vec4(0.2, 0.4, 0.65, 1.0);
uniform float motion : hint_range(0.0, 1.0) = 1.0;

void fragment() {
    vec2 p = UV;
    // The hard boundary is the exact reading. No animated spray may imply water beyond it.
    if (level <= 0.0 || p.y > level) {
        COLOR = vec4(0.0);
    } else {
        float t = TIME * motion;
        float bend = sin(p.y * 7.0 - t * 1.4) * 0.035;
        float a = exp(-pow((p.x - 0.27 - bend) / 0.10, 2.0));
        float b = exp(-pow((p.x - 0.52 + bend * 0.5) / 0.15, 2.0));
        float c = exp(-pow((p.x - 0.76 - bend * 0.7) / 0.065, 2.0));
        float strands = clamp(a * 0.85 + b + c * 0.7, 0.0, 1.0);
        float fall = 0.5 + 0.5 * sin(p.y * 24.0 - t * 5.0 + p.x * 6.0);
        // A ragged, soft end lies wholly INSIDE the reading's exact boundary.
        float edge = strands * smoothstep(0.0, 0.025, level - p.y);
        float contact = step(0.999999, level) * smoothstep(0.94, 1.0, p.y);
        vec3 light = vec3(0.63, 0.74, 0.78) * (fall * strands * 0.09 + contact * 0.17);
        COLOR = vec4(water_color.rgb * (0.62 + fall * 0.16) + light, edge * 0.93);
    }
}
