using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AlfombraBaile;

/// <summary>Lo que ha pasado en la mesa y la pantalla debe mostrar o hacer sonar.</summary>
public enum PinballEventKind
{
    Score, Launch, FlipperUp, Sling, Bumper, SaucerCatch, SaucerEject, Spinner, LaneLit, SkillShot, Cool,
    TargetDown, ExtraBall, Multiball, TargetsBonus, MultiballEnd, BallSaved, BallLost, GameOver, Nudge, Tilt,
}

/// <param name="At">Dónde ha ocurrido (para los números flotantes).</param>
/// <param name="Value">Puntos, vueltas, multiplicador, número de bola… según el tipo.</param>
/// <param name="Pitch">Tono sugerido para el sonido.</param>
public readonly record struct PinballEvent(PinballEventKind Kind, Vector2 At = default, int Value = 0, float Pitch = 1);

/// <summary>Cuánto se pisa una palanca: arriba suave, centro normal, abajo fuerte.</summary>
public enum FlipperInput { None, Soft, Normal, Strong }

/// <summary>
/// Física y reglas del pinball, sin nada de Godot más que los vectores: se puede simular y probar sin escena.
/// Plano de la mesa: x de 0 a 10, y de 0 (abajo) a 20 (arriba). Las paredes están medidas sobre los raíles del dibujo
/// de la mesa (mesa.png, ver <see cref="Px"/>). Avisa de lo que ocurre con <see cref="Happened"/>.
/// </summary>
public sealed class PinballTable
{
    // ---------------------------------------------------------------- medidas y ajustes

    public const float BallR = 0.3f;
    // Gravedad y velocidad máxima de la bola.
    public const float Gravity = 22f;
    public const float MaxSpeed = 46f;
    public const double Step = 1.0 / 240.0;
    // Medidas de la palanca dibujada: pivote a punta 1,9 y punta redondeada de radio 0,3.
    // Con el reposo a −0,72 rad queda un hueco de ~0,76 entre puntas: la bola (0,6) cae por el centro.
    public const float FlipperLen = 1.9f, FlipperR = 0.3f;
    // Velocidad de giro de las palancas (rad/s), medida para que cada fuerza tenga su zona: la suave sube hasta el
    // logo y el agujero (~9), la normal hasta las setas (~13) y la fuerte cruza la mesa hasta el molinete (~20).
    // Por encima de ~12 la bola ya llega siempre arriba, así que la normal y la fuerte no se distinguirían.
    public const float FlipSoft = 7f, FlipNormal = 9f, FlipStrong = 28f, FlipReturn = 22f;
    public const float FlipperRest = 0.72f, FlipperUp = 0.45f;
    public const double TensionTime = 2.0, BallSaveTime = 6.0, TargetsBackDelay = 3.0; // TensionTime: de llena a vacía (y vuelta)
    public const float LaunchMin = 20f, LaunchMax = 37f; // por debajo de ~35 % de tensión no sale del carril
    public const int StartBalls = 3, MaxReserve = 5, MaxInPlay = 2;

    // Correspondencia con mesa.png (887 × 1774 px): x = (px − 12)·K, y = (1765 − py)·K.
    public const float K = 10f / 863f;
    public const float LaneRail = 9.02f;
    public const float Axis = 4.51f; // eje de simetría del campo (entre la pared izquierda y el carril)
    public const float LaneExitY = 16.1f, DrainY = -0.8f;
    public static readonly Vector2 LaunchRest = new(9.51f, 0.95f);

    // Agujero (premia el golpe suave) y molinete (premia el fuerte), en el centro de la mesa.
    public static readonly Vector2 SaucerC = new(Axis, 9.75f);
    public const float SaucerCatch = 0.45f, SaucerMaxSpeed = 15f;
    public const double SaucerHold = 1.8, MultiballHold = 0.8;
    public const float SpinnerY = 15.0f, SpinnerHalf = 0.62f, SpinnerSpeedPerTurn = 2.2f;

    private const float WallThickness = 0.07f, WallBounce = 0.45f, SlingBounce = 0.6f, SlingKick = 11f;
    private const float BumperMinSpeed = 15f, FlipperBounce = 1.35f, LaneRadius = 0.45f;
    private const float StillSpeed = 0.4f;
    private const double StillTime = 1.5, NudgeWindow = 4, SkillWindow = 4;
    private const int NudgesWarn = 3, NudgesTilt = 4;

    // Puntos.
    private const int SlingPoints = 10, BumperPoints = 100, LanePoints = 250, CoolPoints = 1000, SkillPoints = 5000;
    private const int SaucerPoints = 1500, SpinnerTurnPoints = 40, TargetPoints = 500, AllTargetsPoints = 2000;
    private const int TargetsBonusPoints = 3000, MaxMultiplier = 5;

    public static Vector2 Px(float px, float py) => new((px - 12) * K, (1765 - py) * K);
    public static Vector2 Mirror(Vector2 p) => new(2 * Axis - p.X, p.Y);

    // ---------------------------------------------------------------- piezas

    public enum SegKind { Wall, Sling, Target, Gate }

    public sealed class Seg
    {
        public Vector2 A, B;
        public SegKind Kind;
        public bool Active = true;
        public int Id;
    }

    public sealed class Bumper
    {
        public Vector2 C;
        public float R;
    }

    public sealed class Flipper
    {
        public Vector2 Pivot;
        public bool Left;
        public float Rest, Up, Angle, Omega;
        public Vector2 Tip => Pivot + new Vector2(Mathf.Cos(Angle), Mathf.Sin(Angle)) * FlipperLen;
    }

    public sealed class Lane
    {
        public Vector2 C;
        public bool Lit;
    }

    /// <summary>Goma: el lado de <see cref="Top"/> a <see cref="Bottom"/> rebota; <see cref="Corner"/> la cierra.</summary>
    public readonly record struct Sling(Vector2 Corner, Vector2 Top, Vector2 Bottom, bool Left);

    public sealed class Ball
    {
        public Vector2 Pos = LaunchRest, Vel;
        public bool InLane = true, Resting = true, Captured, Drained;
        public double Held, Still;
    }

    private readonly List<Seg> _segs = new();
    private readonly List<Seg> _targets = new();
    private readonly List<Bumper> _bumpers = new();
    private readonly List<Lane> _lanes = new();
    private readonly List<Sling> _slings = new();
    private readonly List<Ball> _balls = new();
    private readonly List<double> _nudges = new();
    private readonly Seg _gate;
    private readonly Random _rng;
    private FlipperInput _leftInput, _rightInput;
    private double _accum, _launchTime, _targetsBackAt;
    private bool _extraGiven;

    public event Action<PinballEvent>? Happened;

    public IReadOnlyList<Seg> Targets => _targets;
    public IReadOnlyList<Bumper> Bumpers => _bumpers;
    public IReadOnlyList<Lane> Lanes => _lanes;
    public IReadOnlyList<Sling> Slings => _slings;
    public IReadOnlyList<Ball> Balls => _balls;
    public Flipper Left { get; }
    public Flipper Right { get; }

    public double Clock { get; private set; }
    private readonly PowerMeter _power = new();
    public double Tension => _power.Value;
    public double BallSave { get; private set; }
    public int Score { get; private set; }
    public int BallsLeft { get; internal set; } = StartBalls;
    public int BallNumber { get; private set; } = 1;
    public int Multiplier { get; private set; } = 1;
    public bool Tilted { get; private set; }
    public bool SkillLive { get; private set; }
    public int SkillLane { get; private set; }
    public bool IsOver { get; private set; }

    /// <summary>La bola que espera en el lanzador, si hay alguna.</summary>
    public Ball? BallOnPlunger => _balls.FirstOrDefault(b => b.Resting && b.InLane);

    public PinballTable(Random? rng = null)
    {
        _rng = rng ?? new Random();

        void Wall(Vector2 a, Vector2 b, SegKind kind = SegKind.Wall) => _segs.Add(new Seg { A = a, B = b, Kind = kind });

        // Raíles dibujados: marco, arco, carril del lanzador y guías en V.
        var arcCenter = new Vector2(5.0f, 15.37f);
        Wall(new Vector2(0, 0.3f), new Vector2(0, arcCenter.Y));
        Wall(new Vector2(10, 0), new Vector2(10, arcCenter.Y));
        Wall(new Vector2(LaneRail, 0), Px(790, 465));
        Wall(new Vector2(LaneRail, 0.05f), new Vector2(10, 0.05f));
        const int arcSteps = 32;
        for (int i = 0; i < arcSteps; i++)
        {
            float a0 = Mathf.Pi * i / arcSteps, a1 = Mathf.Pi * (i + 1) / arcSteps;
            Wall(arcCenter + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 5, arcCenter + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 5);
        }
        // Compuerta de un solo sentido (invisible): deja subir la bola por el carril pero no volver a entrar.
        Wall(Px(790, 465), new Vector2(10, 15.9f), SegKind.Gate);
        _gate = _segs[^1];
        _gate.Active = false;

        var guideL = (A: Px(40, 1485), B: Px(303, 1700));
        var guideR = (A: Px(770, 1485), B: Px(505, 1700));
        // Las palancas se apoyan sobre las guías, a la altura donde x = 2,4 (y su simétrica).
        var pivotL = PointOnGuide(guideL.A, guideL.B, 2.4f);
        var pivotR = PointOnGuide(guideR.A, guideR.B, 2 * Axis - 2.4f);
        Wall(guideL.A, pivotL);
        Wall(guideR.A, pivotR);

        // Gomas sobre las guías: el lado que mira al centro rebota y el resto las cierra.
        AddSling(new Vector2(0.55f, 3.6f), new Vector2(1.0f, 6.5f), new Vector2(2.35f, 2.55f), left: true);
        AddSling(Mirror(new Vector2(0.55f, 3.6f)), Mirror(new Vector2(1.0f, 6.5f)), Mirror(new Vector2(2.35f, 2.55f)), left: false);
        Wall(new Vector2(0, 7.1f), new Vector2(1.0f, 6.5f));
        Wall(Mirror(new Vector2(0, 7.1f)), Mirror(new Vector2(1.0f, 6.5f)));

        // Carriles de arriba (C · O · O · L): postes de tachuelas dibujados.
        foreach (float px in new[] { 285f, 443f, 600f }) Wall(Px(px, 110), Px(px, 305));
        foreach (float px in new[] { 206f, 365f, 522f, 680f }) _lanes.Add(new Lane { C = Px(px, 222) });

        // Dianas abatibles pegadas a la pared izquierda: el hueco detrás (0,4) es menor que la bola.
        for (int i = 0; i < 3; i++)
        {
            float y = 9.0f + i * 0.85f;
            var seg = new Seg { A = new Vector2(0.55f, y), B = new Vector2(0.55f, y + 0.66f), Kind = SegKind.Target, Id = i };
            _segs.Add(seg);
            _targets.Add(seg);
        }

        foreach (var c in new[] { new Vector2(3.2f, 13.1f), new Vector2(5.8f, 13.1f), new Vector2(Axis, 11.2f) })
            _bumpers.Add(new Bumper { C = c, R = 0.7f });

        Left = new Flipper { Pivot = pivotL, Left = true, Rest = -FlipperRest, Up = FlipperUp };
        Right = new Flipper { Pivot = pivotR, Left = false, Rest = Mathf.Pi + FlipperRest, Up = Mathf.Pi - FlipperUp };
        Left.Angle = Left.Rest;
        Right.Angle = Right.Rest;

        ResetBall();
    }

    private void AddSling(Vector2 corner, Vector2 top, Vector2 bottom, bool left)
    {
        _segs.Add(new Seg { A = top, B = bottom, Kind = SegKind.Sling });
        _segs.Add(new Seg { A = top, B = corner, Kind = SegKind.Wall });
        _segs.Add(new Seg { A = corner, B = bottom, Kind = SegKind.Wall });
        _slings.Add(new Sling(corner, top, bottom, left));
    }

    private static Vector2 PointOnGuide(Vector2 a, Vector2 b, float x)
    {
        float t = (x - a.X) / (b.X - a.X);
        return a.Lerp(b, t);
    }

    private void Emit(PinballEventKind kind, Vector2 at = default, int value = 0, float pitch = 1) =>
        Happened?.Invoke(new PinballEvent(kind, at, value, pitch));

    // ---------------------------------------------------------------- controles

    public void SetFlippers(FlipperInput left, FlipperInput right)
    {
        _leftInput = left;
        _rightInput = right;
    }

    /// <summary>Tensa el lanzador al máximo; luego la fuerza baja y sube sola (<see cref="TensionTime"/> segundos cada recorrido).</summary>
    public void PullPlunger()
    {
        if (BallOnPlunger is not null) _power.Start();
    }

    /// <summary>Suelta el lanzador con la tensión que le quede.</summary>
    public void ReleasePlunger()
    {
        if (BallOnPlunger is not { } ball || Tension <= 0) return;
        float force = (float)Tension;
        ball.Vel = new Vector2(0, Mathf.Lerp(LaunchMin, LaunchMax, force));
        ball.Resting = false;
        _power.Stop();
        _launchTime = Clock;
        BallSave = BallSaveTime;
        Emit(PinballEventKind.Launch, pitch: 0.8f + force * 0.4f);
    }

    /// <summary>Menear la mesa: empujón a las bolas. El cuarto meneo en 4 s es TILT: palancas muertas hasta perder la bola.</summary>
    public void Nudge(bool forward)
    {
        if (Tilted) return;
        _nudges.Add(Clock);
        _nudges.RemoveAll(t => Clock - t > NudgeWindow);
        foreach (var ball in _balls.Where(b => !b.InLane && !b.Captured))
            ball.Vel += forward ? new Vector2((float)(_rng.NextDouble() - 0.5) * 2, 4.5f) : new Vector2(_rng.Next(2) == 0 ? -3.5f : 3.5f, 1.5f);
        Emit(PinballEventKind.Nudge, value: _nudges.Count);
        if (_nudges.Count >= NudgesTilt)
        {
            Tilted = true;
            Emit(PinballEventKind.Tilt);
        }
    }

    /// <summary>Segunda bola en juego: sale disparada desde el agujero.</summary>
    public void StartMultiball()
    {
        var extra = new Ball { Pos = SaucerC, InLane = false, Resting = false, Captured = !_balls.Any(b => b.Captured), Held = MultiballHold };
        if (!extra.Captured) extra.Vel = new Vector2(_rng.Next(2) == 0 ? -4.5f : 4.5f, 10f);
        _balls.Add(extra);
        Emit(PinballEventKind.Multiball);
    }

    public static bool WarnNudges(int count) => count == NudgesWarn;

    // ---------------------------------------------------------------- tiempo

    /// <summary>Avanza la mesa <paramref name="delta"/> segundos: reloj, lanzador, bola salvada, dianas y física.</summary>
    public void Advance(double delta)
    {
        if (IsOver) return;
        Clock += delta;
        if (BallOnPlunger is not null) _power.Advance(delta, TensionTime);
        BallSave = Math.Max(0, BallSave - delta);
        if (_targetsBackAt > 0 && Clock >= _targetsBackAt)
        {
            _targetsBackAt = 0;
            foreach (var target in _targets) target.Active = true;
        }

        _accum += delta;
        while (_accum >= Step)
        {
            _accum -= Step;
            Simulate((float)Step);
            if (IsOver) break;
        }
    }

    private float FlipSpeed(FlipperInput input) => Tilted ? 0 : input switch
    {
        FlipperInput.Strong => FlipStrong,
        FlipperInput.Normal => FlipNormal,
        FlipperInput.Soft => FlipSoft,
        _ => 0,
    };

    private void Simulate(float dt)
    {
        MoveFlipper(Left, FlipSpeed(_leftInput), dt);
        MoveFlipper(Right, FlipSpeed(_rightInput), dt);

        foreach (var ball in _balls.ToList())
            if (!ball.Resting) SimulateBall(ball, dt);

        var drained = _balls.Where(b => b.Drained).ToList();
        if (drained.Count == 0) return;
        foreach (var ball in drained) _balls.Remove(ball);
        // Solo se pierde bola cuando se va la última; con multibola, perder una no cuenta.
        if (_balls.Count == 0) Drain();
        else Emit(PinballEventKind.MultiballEnd);
    }

    private void SimulateBall(Ball ball, float dt)
    {
        _gate.Active = !ball.InLane;

        // Bola atrapada en el agujero: quieta hasta que la escupe hacia arriba.
        if (ball.Captured)
        {
            ball.Pos = SaucerC;
            ball.Vel = Vector2.Zero;
            ball.Held -= dt;
            if (ball.Held > 0) return;
            ball.Captured = false;
            ball.Vel = new Vector2(_rng.Next(2) == 0 ? -4.5f : 4.5f, 10f);
            ball.Pos += ball.Vel.Normalized() * (SaucerCatch + 0.05f);
            Emit(PinballEventKind.SaucerEject);
        }

        var before = ball.Pos;
        ball.Vel += new Vector2(0, -Gravity) * dt;
        if (ball.Vel.Length() > MaxSpeed) ball.Vel = ball.Vel.Normalized() * MaxSpeed;
        ball.Pos += ball.Vel * dt;

        // Agujero: solo atrapa la bola si llega despacio (golpe suave); rápida, pasa por encima.
        if (!ball.InLane && (ball.Pos - SaucerC).Length() < SaucerCatch && ball.Vel.Length() < SaucerMaxSpeed
            && !_balls.Any(b => b != ball && b.Captured))
        {
            ball.Captured = true;
            ball.Held = SaucerHold;
            AddScore(SaucerPoints, SaucerC);
            Emit(PinballEventKind.SaucerCatch);
            return;
        }

        // Molinete: al cruzarlo, da tantas vueltas como rápida vaya la bola.
        bool crossed = (before.Y - SpinnerY) * (ball.Pos.Y - SpinnerY) < 0 && Mathf.Abs(ball.Pos.X - Axis) < SpinnerHalf;
        if (crossed && !ball.InLane)
        {
            int turns = (int)(Mathf.Abs(ball.Vel.Y) / SpinnerSpeedPerTurn);
            if (turns > 0) AddScore(SpinnerTurnPoints * turns, new Vector2(Axis, SpinnerY));
            Emit(PinballEventKind.Spinner, new Vector2(Axis, SpinnerY), turns);
        }

        // Al salir del carril se cierra la compuerta para que la bola no vuelva a entrar.
        if (ball.InLane && ball.Pos.Y > LaneExitY)
        {
            ball.InLane = false;
            _gate.Active = true;
        }
        // Si no tuvo fuerza para salir del carril, vuelve a reposar sobre el lanzador.
        if (ball.InLane && ball.Pos.Y <= LaunchRest.Y && ball.Vel.Y < 0)
        {
            ball.Pos = LaunchRest;
            ball.Vel = Vector2.Zero;
            ball.Resting = true;
            return;
        }

        foreach (var seg in _segs)
        {
            if (!seg.Active) continue;
            if (!Collide(ball, seg.A, seg.B, WallThickness, seg.Kind == SegKind.Sling ? SlingBounce : WallBounce, out var n)) continue;
            switch (seg.Kind)
            {
                case SegKind.Sling:
                    ball.Vel += n * SlingKick;
                    AddScore(SlingPoints, ball.Pos);
                    Emit(PinballEventKind.Sling, ball.Pos);
                    break;
                case SegKind.Target when ball.Pos.X > seg.A.X:
                    HitTarget(seg);
                    break;
            }
        }

        for (int i = 0; i < _bumpers.Count; i++)
        {
            var b = _bumpers[i];
            var d = ball.Pos - b.C;
            float dist = d.Length(), min = b.R + BallR;
            if (dist >= min || dist < 1e-4f) continue;
            var n = d / dist;
            ball.Pos = b.C + n * min;
            float vn = ball.Vel.Dot(n);
            if (vn < 0) ball.Vel -= 2 * vn * n;
            ball.Vel = ball.Vel.Normalized() * Mathf.Max(ball.Vel.Length(), BumperMinSpeed);
            AddScore(BumperPoints, b.C);
            Emit(PinballEventKind.Bumper, b.C, i, 0.9f + (float)_rng.NextDouble() * 0.3f);
        }

        CollideFlipper(ball, Left);
        CollideFlipper(ball, Right);

        for (int i = 0; i < _lanes.Count; i++)
        {
            var lane = _lanes[i];
            if ((ball.Pos - lane.C).Length() > LaneRadius || ball.Vel.Y > 0) continue;
            if (SkillLive)
            {
                SkillLive = false;
                if (i == SkillLane)
                {
                    AddScore(SkillPoints, lane.C);
                    Emit(PinballEventKind.SkillShot, lane.C);
                }
            }
            if (lane.Lit) continue;
            lane.Lit = true;
            AddScore(LanePoints, lane.C);
            Emit(PinballEventKind.LaneLit, lane.C, i);
            if (_lanes.All(l => l.Lit))
            {
                foreach (var l in _lanes) l.Lit = false;
                Multiplier = Math.Min(Multiplier + 1, MaxMultiplier);
                AddScore(CoolPoints, lane.C);
                Emit(PinballEventKind.Cool, lane.C, Multiplier);
            }
        }
        // Cualquier otro carril tras el lanzamiento anula el tiro de habilidad.
        if (SkillLive && !ball.InLane && Clock - _launchTime > SkillWindow) SkillLive = false;

        // Red de seguridad: si la bola se queda quieta fuera del lanzador, un empujoncito para que nunca se atasque.
        ball.Still = ball.Vel.Length() < StillSpeed && !ball.InLane ? ball.Still + dt : 0;
        if (ball.Still > StillTime)
        {
            ball.Still = 0;
            ball.Vel += new Vector2(_rng.Next(2) == 0 ? -2f : 2f, 2.5f);
        }

        if (ball.Pos.Y < DrainY) ball.Drained = true;
    }

    /// <summary>Choque de la bola con un segmento grueso (radio extra r). Devuelve la normal del choque.</summary>
    private static bool Collide(Ball ball, Vector2 a, Vector2 b, float r, float bounce, out Vector2 normal)
    {
        normal = Vector2.Zero;
        var ab = b - a;
        float t = Mathf.Clamp((ball.Pos - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        var q = a + ab * t;
        var d = ball.Pos - q;
        float dist = d.Length(), min = BallR + r;
        if (dist >= min || dist < 1e-5f) return false;
        normal = d / dist;
        ball.Pos = q + normal * min;
        float vn = ball.Vel.Dot(normal);
        if (vn < 0) ball.Vel -= (1 + bounce) * vn * normal;
        return vn < 0;
    }

    private void MoveFlipper(Flipper f, float speed, float dt)
    {
        float target = speed > 0 ? f.Up : f.Rest;
        float rate = speed > 0 ? speed : FlipReturn;
        float before = f.Angle;
        f.Angle = Mathf.MoveToward(f.Angle, target, rate * dt);
        f.Omega = (f.Angle - before) / dt;
        if (speed > 0 && before != f.Up && Mathf.IsEqualApprox(f.Angle, f.Up)) Emit(PinballEventKind.FlipperUp);
    }

    /// <summary>La palanca empuja con la velocidad de su superficie en el punto de contacto (ω × r).</summary>
    private static void CollideFlipper(Ball ball, Flipper f)
    {
        var a = f.Pivot;
        var ab = f.Tip - a;
        float t = Mathf.Clamp((ball.Pos - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        var q = a + ab * t;
        var d = ball.Pos - q;
        float dist = d.Length(), min = BallR + FlipperR;
        if (dist >= min || dist < 1e-5f) return;
        var n = d / dist;
        ball.Pos = q + n * min;
        var r = q - a;
        var surface = new Vector2(-r.Y, r.X) * f.Omega;
        var rel = ball.Vel - surface;
        float vn = rel.Dot(n);
        if (vn < 0) rel -= FlipperBounce * vn * n;
        ball.Vel = rel + surface;
    }

    // ---------------------------------------------------------------- reglas

    private void AddScore(int points, Vector2 at)
    {
        if (Tilted) return;
        int total = points * Multiplier;
        Score += total;
        Emit(PinballEventKind.Score, at, total);
    }

    internal void HitTarget(Seg seg)
    {
        seg.Active = false;
        AddScore(TargetPoints, seg.A);
        Emit(PinballEventKind.TargetDown, seg.A, seg.Id);
        if (_targets.Any(t => t.Active)) return;

        AddScore(AllTargetsPoints, seg.A);
        if (BallsLeft < MaxReserve && !_extraGiven)
        {
            // Una bola extra por bola en juego, hasta un máximo de 5 en reserva.
            _extraGiven = true;
            BallsLeft++;
            Emit(PinballEventKind.ExtraBall);
        }
        else if (BallsLeft >= MaxReserve && _balls.Count < MaxInPlay)
        {
            // Con la reserva llena, la bola extra sale ya: multibola (nunca más de 2 en juego).
            StartMultiball();
        }
        else
        {
            AddScore(TargetsBonusPoints, seg.A);
            Emit(PinballEventKind.TargetsBonus, seg.A);
        }
        _targetsBackAt = Clock + TargetsBackDelay;
    }

    private void ResetBall()
    {
        _balls.Clear();
        _balls.Add(new Ball());
        _extraGiven = false;
        _power.Stop();
        _gate.Active = false;
        Tilted = false;
        _nudges.Clear();
        SkillLane = _rng.Next(_lanes.Count);
        SkillLive = true;
    }

    private void Drain()
    {
        if (BallSave > 0 && !Tilted)
        {
            ResetBall();
            Emit(PinballEventKind.BallSaved);
            return;
        }
        BallsLeft--;
        if (BallsLeft <= 0)
        {
            IsOver = true;
            Emit(PinballEventKind.BallLost, value: BallNumber);
            Emit(PinballEventKind.GameOver);
            return;
        }
        BallNumber++;
        ResetBall();
        Emit(PinballEventKind.BallLost, value: BallNumber);
    }
}
