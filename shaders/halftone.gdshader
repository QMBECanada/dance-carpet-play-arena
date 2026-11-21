shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, fog_disabled;

// Trama de puntos de fondo, como la de la alfombra.
uniform vec4 dot_color : source_color;
uniform float rows = 60.0;
uniform float aspect = 2.0;

void fragment() {
	vec2 grid = UV * vec2(rows * aspect, rows);
	float r = length(fract(grid) - 0.5);
	vec2 c = (UV - vec2(0.5, 0.42)) * vec2(1.0, 1.4);
	float mask = 1.0 - smoothstep(0.05, 0.55, length(c));
	float rad = 0.08 + 0.26 * mask;
	float d = smoothstep(rad, rad - 0.06, r);
	ALBEDO = dot_color.rgb;
	ALPHA = d * mask * dot_color.a;
}
