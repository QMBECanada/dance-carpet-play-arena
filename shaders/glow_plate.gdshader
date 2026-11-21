shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, fog_disabled;

// Halo detrás de la ficha elegida. size = tamaño del quad; inner = tamaño de la ficha.
uniform vec4 color : source_color;
uniform float intensity = 0.0;
uniform vec2 size = vec2(2.3, 2.8);
uniform vec2 inner = vec2(1.5, 2.0);
uniform float radius = 0.14;

float sd_round_box(vec2 p, vec2 b, float r) {
	vec2 q = abs(p) - b + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
	vec2 p = (UV - 0.5) * size;
	float d = sd_round_box(p, inner * 0.5, radius);
	float ring = smoothstep(0.06, 0.0, abs(d - 0.02));
	float halo = exp(-max(d, 0.0) * 7.0) * step(-0.05, d);
	vec2 edge = min(UV, 1.0 - UV) * size;
	float fade = smoothstep(0.0, 0.3, min(edge.x, edge.y));
	ALBEDO = color.rgb * 1.8;
	ALPHA = clamp((halo * 0.55 + ring) * intensity * fade, 0.0, 1.0);
}
