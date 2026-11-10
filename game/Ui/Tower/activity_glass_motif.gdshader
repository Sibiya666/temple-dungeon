shader_type canvas_item;
render_mode unshaded;

uniform bool hourglass = false;
uniform sampler2D silhouette : filter_linear, repeat_disable;

void fragment() {
    vec4 ink = texture(TEXTURE, UV);
    if (hourglass) {
        // Keep the dark solder and glass opaque, including where the window pattern crosses the neck.
        ink.a *= texture(silhouette, UV).a;
        ink.rgb = pow(ink.rgb, vec3(.84));
    } else {
        vec2 px = TEXTURE_PIXEL_SIZE;
        float rim = max(max(texture(TEXTURE, UV + vec2(px.x, 0)).a,
                            texture(TEXTURE, UV - vec2(px.x, 0)).a),
                        max(texture(TEXTURE, UV + vec2(0, px.y)).a,
                            texture(TEXTURE, UV - vec2(0, px.y)).a));
        ink.rgb = mix(vec3(.052,.075,.072), vec3(.89,.83,.66), ink.a);
        ink.a = max(ink.a, rim);
    }
    COLOR = ink;
}
