shader_type spatial;
render_mode cull_disabled;

// Cara frontal de una ficha: la textura viene de un SubViewport que dibuja icono y nombre.
uniform sampler2D face_tex : source_color, filter_linear;
uniform float dim = 1.0;
uniform float self_light = 0.55;

void fragment() {
	vec3 c = texture(face_tex, UV).rgb * dim;
	ALBEDO = c * (1.0 - self_light);
	EMISSION = c * self_light;
	ROUGHNESS = 0.35;
	SPECULAR = 0.6;
}
