shader_type canvas_item;
render_mode unshaded;

varying vec4 surface;
void vertex() {
    surface = COLOR;
}

// Concentric lead rings and eight intersecting petals form one rose across all sixteen sockets.
float rose_lead(vec2 p) {
    float radius = length(p);
    float distance_to_lead = min(abs(radius - 0.12), min(abs(radius - 0.33), abs(radius - 0.47)));
    float angle = atan(p.y, p.x);
    float sector = 0.78539816;
    float folded = mod(angle + 0.5 * sector, sector) - 0.5 * sector;
    vec2 petal = radius * vec2(cos(folded), sin(folded));
    distance_to_lead = min(distance_to_lead, abs(length(petal - vec2(0.235, 0.115)) - 0.195));
    distance_to_lead = min(distance_to_lead, abs(length(petal - vec2(0.235,-0.115)) - 0.195));
    if (radius > 0.33) distance_to_lead = min(distance_to_lead, radius * abs(sin(folded)));
    return distance_to_lead;
}

void fragment() {
    vec2 p = UV - vec2(0.5, 0.48);
    float lead = rose_lead(p);
    float aa = max(fwidth(lead), 0.0006);
    float seam = 1.0 - smoothstep(0.0012, 0.0012 + aa, lead);
    vec3 tower = texture(TEXTURE, UV).rgb;
    vec3 hue = vec3(.13,.34,.38);
    vec3 color;
    if (surface.r > .4) {
        float bevel = 1.0 - smoothstep(.0012, .0045 + aa, abs(lead - .004));
        color = vec3(.035,.063,.077) + tower * .15 + bevel * vec3(.05,.065,.07);
    } else {
        color = hue * .31 + tower * .12;
    }
    color = mix(color, vec3(.025,.032,.034), seam * .55);
    if (surface.r > .20 && surface.r < .30) {
        color = (surface.b > .5 ? vec3(.83,.73,.48) : vec3(.264,.270,.242)) * surface.g;
    }
    COLOR = vec4(color, surface.a);
}
