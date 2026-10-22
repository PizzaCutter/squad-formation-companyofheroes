// World-space grid so movement and spacing read clearly on camera.
shader_type spatial;

uniform vec4 base_color : source_color = vec4(0.36, 0.45, 0.30, 1.0);
uniform vec4 line_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
uniform float cell_size = 2.0;
uniform float major_every = 5.0;
uniform float minor_strength = 0.12;
uniform float major_strength = 0.28;

varying vec3 world_pos;

float grid(vec2 p) {
	vec2 g = abs(fract(p - 0.5) - 0.5) / fwidth(p);
	return 1.0 - min(min(g.x, g.y), 1.0);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float minor = grid(world_pos.xz / cell_size);
	float major = grid(world_pos.xz / (cell_size * major_every));
	float amount = max(minor * minor_strength, major * major_strength);
	ALBEDO = mix(base_color.rgb, line_color.rgb, amount);
	ROUGHNESS = 1.0;
}
