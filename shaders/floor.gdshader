shader_type spatial;
render_mode unshaded, cull_disabled;

// Suelo con rejilla que avanza despacio y un brillo bajo el centro de la escena.
uniform vec4 floor_color : source_color;
uniform vec4 bg_color : source_color;
uniform vec4 line_color : source_color;
uniform float cell = 1.2;
uniform float speed = 0.25;
uniform vec2 focus = vec2(0.0, 0.0);
uniform float reach = 16.0;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = wpos.xz / cell;
	p.y -= TIME * speed;
	vec2 g = abs(fract(p - 0.5) - 0.5) / fwidth(p);
	float line = 1.0 - min(min(g.x, g.y), 1.0);
	float dist = length(wpos.xz - focus);
	float fade = clamp(1.0 - dist / reach, 0.0, 1.0);
	float spot = exp(-dist * dist / 10.0);
	vec3 col = mix(bg_color.rgb, floor_color.rgb, fade);
	col = mix(col, line_color.rgb, line * fade * 0.45);
	col = mix(col, line_color.rgb, spot * 0.18);
	ALBEDO = col;
}
