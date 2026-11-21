shader_type spatial;
render_mode cull_disabled;

// Cara superior de una casilla de la alfombra con su símbolo dibujado por distancia.
// glyph: 0 flecha, 1 equis, 2 círculo, 3 triángulo, 4 cuadrado, 5 punto, 6 nada.
uniform vec4 base : source_color;
uniform vec4 ink : source_color;
uniform int glyph = 0;
uniform float angle = 0.0;
uniform float pressed = 0.0;
uniform float blink = 0.0;
uniform float dim = 1.0;

float sd_box(vec2 p, vec2 b) {
	vec2 d = abs(p) - b;
	return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}

float sd_seg(vec2 p, vec2 a, vec2 b) {
	vec2 pa = p - a, ba = b - a;
	float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
	return length(pa - ba * h);
}

float sd_tri(vec2 p, float r) {
	const float k = sqrt(3.0);
	p.x = abs(p.x) - r;
	p.y = p.y + r / k;
	if (p.x + k * p.y > 0.0) p = vec2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
	p.x -= clamp(p.x, -2.0 * r, 0.0);
	return -length(p) * sign(p.y);
}

float glyph_sd(vec2 p) {
	if (glyph == 0) {
		float c = cos(angle), s = sin(angle);
		vec2 q = vec2(c * p.x - s * p.y, s * p.x + c * p.y);
		float head = sd_tri(q - vec2(0.0, 0.14), 0.42);
		float shaft = sd_box(q - vec2(0.0, -0.3), vec2(0.15, 0.24));
		return min(head, shaft);
	}
	if (glyph == 1) return min(sd_seg(p, vec2(-0.4), vec2(0.4)), sd_seg(p, vec2(-0.4, 0.4), vec2(0.4, -0.4))) - 0.11;
	if (glyph == 2) return abs(length(p) - 0.42) - 0.09;
	if (glyph == 3) return abs(sd_tri(p - vec2(0.0, -0.08), 0.46)) - 0.08;
	if (glyph == 4) return abs(sd_box(p, vec2(0.38))) - 0.08;
	if (glyph == 5) return length(p) - 0.15;
	return 1.0;
}

void fragment() {
	vec2 p = (UV - 0.5) * 2.0;
	p.y = -p.y;
	float d = glyph_sd(p);
	float fill = 1.0 - smoothstep(0.0, fwidth(d) * 1.5, d);
	vec3 col = mix(base.rgb, ink.rgb, fill * 0.9) * dim;
	float glow = 0.22 + blink * 0.55 + pressed * 1.3;
	ALBEDO = col * 0.65;
	EMISSION = col * glow;
	ROUGHNESS = 0.4;
}
