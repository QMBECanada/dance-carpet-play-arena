using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AlfombraBaile;

/// <summary>Cuánto barre cada barrendero: de la fila de arriba (✕ / ○, barre) o de la de abajo (△ / □, frota).</summary>
public enum SweepInput { None, Soft, Strong }

/// <summary>
/// Física de la pista de curling, sin nada de Godot más que los vectores (se puede simular y probar sin escena).
/// Coordenadas en metros: x a lo ancho (0 en el centro), y a lo largo desde el punto de lanzamiento hacia la casa.
/// Las piedras frenan por rozamiento, la lanzada curva poco a poco hacia el lado de su giro (más cuanto más despacio
/// va) y chocan entre sí. Los barrenderos actúan sobre la piedra lanzada:
///   · el izquierdo (✕ fuerte, △ suave) la empuja a la derecha y el derecho (○ fuerte, □ suave) a la izquierda;
///   · la fila de arriba barre (la piedra se desliza algo más) y la de abajo frota (frena algo);
///   · los efectos se suman: ✕+○ va recta y más lejos, △+□ va recta y frena.
/// </summary>
public sealed class CurlingSheet
{
    // ---------------------------------------------------------------- medidas (metros)

    public const float StoneR = 0.145f;
    public const float HalfWidth = 2.37f;
    public const float TeeY = 30f;
    public const float HouseR = 1.83f;
    public static readonly float[] RingRadii = { 0.15f, 0.61f, 1.22f, HouseR }; // centro, rojo, blanco, azul
    public const float HogY = TeeY - 6.4f;   // una piedra parada antes de esta línea queda fuera
    public const float BackY = TeeY + HouseR; // pasada la línea de fondo, fuera
    public static readonly Vector2 Tee = new(0, TeeY);

    // ---------------------------------------------------------------- física

    /// <summary>La física va más rápida que el reloj: un lanzamiento hasta la casa dura ~11 s en pantalla.</summary>
    public const double TimeScale = 2.4;
    public const double Step = 1.0 / 120.0; // paso de física, en segundos de pista
    public const float Friction = 0.09f;    // deceleración (m/s²)
    public const float CurlStrength = 0.011f, CurlSoftening = 0.4f; // ~1 m de curva en un lanzamiento a la casa
    public const float SweepGlide = 0.03f, SweepDrag = 0.06f;          // por barrendero, sobre el rozamiento
    public const float SteerStrong = 0.012f, SteerSoft = 0.004f;       // empuje lateral (m/s²)
    public const float Restitution = 0.9f;
    public const float StopSpeed = 0.01f;

    // ---------------------------------------------------------------- lanzamiento

    // Velocidad según la tensión del lanzador (0..1), en curva: los tiros suaves a la casa tienen más margen que los
    // fuertes para sacar piedras. La fuerza oscila: baja de llena a vacía en TensionTime segundos y vuelve a subir.
    public const float MinSpeed = 1.9f, MaxSpeed = 3.4f;
    public const double TensionTime = 3.0;
    public const float MaxAim = 2.2f;                      // desvío máximo apuntado a la altura de la casa (m)

    public sealed class Stone
    {
        public int Team;
        public Vector2 Pos, Vel;
        public int Curl;     // −1 curva a la izquierda, +1 a la derecha, 0 sin giro (piedras golpeadas)
        public bool Out;
        public bool Moving => !Out && Vel.LengthSquared() > StopSpeed * StopSpeed;
    }

    private readonly List<Stone> _stones = new();
    private double _accum;
    private SweepInput _left, _right;

    public IReadOnlyList<Stone> Stones => _stones;
    public IEnumerable<Stone> InPlay => _stones.Where(s => !s.Out);

    /// <summary>La piedra recién lanzada (la que barren), o null si no hay ninguna en movimiento.</summary>
    public Stone? Thrown { get; private set; }

    public bool Moving => _stones.Any(s => s.Moving);

    /// <summary>Choques entre piedras en este paso (para el sonido).</summary>
    public event Action<Stone, Stone, float>? Hit;

    public CurlingSheet Clone()
    {
        var copy = new CurlingSheet();
        foreach (var s in _stones)
            copy._stones.Add(new Stone { Team = s.Team, Pos = s.Pos, Vel = s.Vel, Curl = s.Curl, Out = s.Out });
        if (Thrown is not null) copy.Thrown = copy._stones[_stones.IndexOf(Thrown)];
        return copy;
    }

    /// <summary>Velocidad inicial para una tensión del lanzador (0..1).</summary>
    public static float SpeedFor(float tension)
    {
        tension = Mathf.Clamp(tension, 0, 1);
        return MinSpeed + (MaxSpeed - MinSpeed) * tension * tension;
    }

    /// <summary>Lanza una piedra hacia <paramref name="aim"/> (desvío en metros a la altura de la casa).</summary>
    public Stone Throw(int team, float aim, float tension, int curl)
    {
        aim = Mathf.Clamp(aim, -MaxAim, MaxAim);
        var dir = new Vector2(aim, TeeY).Normalized();
        var stone = new Stone { Team = team, Pos = Vector2.Zero, Vel = dir * SpeedFor(tension), Curl = Math.Sign(curl) };
        _stones.Add(stone);
        Thrown = stone;
        _left = _right = SweepInput.None;
        return stone;
    }

    public void SetSweep(SweepInput left, SweepInput right)
    {
        _left = left;
        _right = right;
    }

    /// <summary>Avanza la pista <paramref name="realSeconds"/> segundos de reloj (la física va <see cref="TimeScale"/> veces más rápida).</summary>
    public void Advance(double realSeconds)
    {
        _accum += realSeconds * TimeScale;
        while (_accum >= Step)
        {
            _accum -= Step;
            Simulate((float)Step);
        }
    }

    /// <summary>Simula hasta que todo se pare (sin barrer). Para el rival y las pruebas.</summary>
    public void SettleWithoutSweeping(int maxSteps = 20000)
    {
        SetSweep(SweepInput.None, SweepInput.None);
        for (int i = 0; i < maxSteps && Moving; i++) Simulate((float)Step);
    }

    private static float Glide(SweepInput s) => s == SweepInput.Strong ? SweepGlide : 0;
    private static float Drag(SweepInput s) => s == SweepInput.Soft ? SweepDrag : 0;
    private static float Steer(SweepInput s) => s switch { SweepInput.Strong => SteerStrong, SweepInput.Soft => SteerSoft, _ => 0 };

    private void Simulate(float dt)
    {
        foreach (var stone in _stones)
        {
            if (!stone.Moving)
            {
                stone.Vel = Vector2.Zero;
                continue;
            }
            float speed = stone.Vel.Length();
            var forward = stone.Vel / speed;
            var side = new Vector2(forward.Y, -forward.X); // a la derecha del avance

            float friction = Friction;
            var lateral = Vector2.Zero;
            if (stone.Curl != 0) lateral += side * stone.Curl * CurlStrength / (speed + CurlSoftening);
            if (stone == Thrown)
            {
                friction *= 1 - Glide(_left) - Glide(_right) + Drag(_left) + Drag(_right);
                lateral += side * (Steer(_left) - Steer(_right)); // el izquierdo empuja a la derecha
            }

            stone.Vel += lateral * dt;
            float newSpeed = Mathf.Max(0, stone.Vel.Length() - friction * dt);
            stone.Vel = newSpeed > 0 ? stone.Vel.Normalized() * newSpeed : Vector2.Zero;
            stone.Pos += stone.Vel * dt;
        }

        Collide();

        foreach (var stone in _stones)
        {
            if (stone.Out) continue;
            if (Mathf.Abs(stone.Pos.X) > HalfWidth - StoneR || stone.Pos.Y > BackY + StoneR) Remove(stone);
            else if (!stone.Moving && stone.Pos.Y < HogY) Remove(stone);
        }
        if (Thrown is { } t && !t.Moving) Thrown = null;
    }

    private void Remove(Stone stone)
    {
        stone.Out = true;
        stone.Vel = Vector2.Zero;
    }

    /// <summary>Choques entre piedras de igual masa: se intercambian la velocidad a lo largo de la línea que las une.</summary>
    private void Collide()
    {
        for (int i = 0; i < _stones.Count; i++)
        for (int j = i + 1; j < _stones.Count; j++)
        {
            Stone a = _stones[i], b = _stones[j];
            if (a.Out || b.Out) continue;
            var d = b.Pos - a.Pos;
            float dist = d.Length();
            if (dist >= 2 * StoneR || dist < 1e-6f) continue;
            var n = d / dist;
            float overlap = 2 * StoneR - dist;
            a.Pos -= n * overlap / 2;
            b.Pos += n * overlap / 2;
            float approach = (a.Vel - b.Vel).Dot(n);
            if (approach <= 0) continue;
            float impulse = approach * (1 + Restitution) / 2;
            a.Vel -= n * impulse;
            b.Vel += n * impulse;
            // Una piedra golpeada pierde el giro: ya no curva.
            if (b != Thrown) b.Curl = 0;
            if (a != Thrown) a.Curl = 0;
            Hit?.Invoke(a, b, approach);
        }
    }

    // ---------------------------------------------------------------- puntuación

    /// <summary>Distancia de la piedra al centro de la casa, descontando su radio (lo que cuenta para tocar un anillo).</summary>
    public static float Distance(Stone stone) => (stone.Pos - Tee).Length() - StoneR;

    public static bool InHouse(Stone stone) => !stone.Out && Distance(stone) <= HouseR;

    /// <summary>Puntos en solitario: centro 10, rojo 6, blanco 3, azul 1 y fuera de la casa 0.</summary>
    public static int SoloPoints(Stone stone)
    {
        if (stone.Out) return 0;
        float d = Distance(stone);
        int[] points = { 10, 6, 3, 1 };
        for (int i = 0; i < RingRadii.Length; i++)
            if (d <= RingRadii[i]) return points[i];
        return 0;
    }

    /// <summary>
    /// Resultado de una entrada según las reglas del curling: el equipo con la piedra más cercana al centro suma un
    /// punto por cada piedra suya en la casa más cercana que la mejor del rival. Devuelve (equipo, puntos), o (−1, 0).
    /// </summary>
    /// <summary>Las piedras que puntuarían si la entrada acabase ahora (las que cuenta <see cref="EndScore"/>).</summary>
    public List<Stone> CountingStones()
    {
        var (team, points) = EndScore();
        if (team < 0) return new List<Stone>();
        return InPlay.Where(s => s.Team == team && InHouse(s)).OrderBy(Distance).Take(points).ToList();
    }

    public (int Team, int Points) EndScore()
    {
        var house = InPlay.Where(InHouse).OrderBy(Distance).ToList();
        if (house.Count == 0) return (-1, 0);
        int team = house[0].Team;
        var rival = house.FirstOrDefault(s => s.Team != team);
        float limit = rival is null ? float.MaxValue : Distance(rival);
        return (team, house.Count(s => s.Team == team && Distance(s) < limit));
    }
}
