using Godot;

namespace AlfombraBaile.Tests;

/// <summary>Pruebas de la física y las reglas del pinball, sin Godot: se simula la mesa paso a paso.</summary>
public class PinballTableTests
{
    // ---------------------------------------------------------------- utilidades

    private static PinballTable NewTable(List<PinballEvent>? events = null)
    {
        var table = new PinballTable(new Random(1));
        if (events is not null) table.Happened += events.Add;
        return table;
    }

    /// <summary>Saca la bola del lanzador y la pone en juego en un punto, con una velocidad.</summary>
    private static PinballTable.Ball Place(PinballTable table, Vector2 pos, Vector2 vel = default)
    {
        var ball = table.Balls[0];
        ball.InLane = false;
        ball.Resting = false;
        ball.Pos = pos;
        ball.Vel = vel;
        return ball;
    }

    private static void Run(PinballTable table, double seconds)
    {
        int steps = (int)Math.Round(seconds / PinballTable.Step);
        for (int i = 0; i < steps; i++) table.Advance(PinballTable.Step);
    }

    private static void HitAllTargets(PinballTable table)
    {
        foreach (var target in table.Targets)
        {
            target.Active = true;
            table.HitTarget(target);
        }
    }

    // ---------------------------------------------------------------- regresión

    [Fact]
    public void La_fisica_coincide_con_la_foto_grabada()
    {
        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fisica_pinball.txt"));
        var actual = PinballAutoplay.Run();
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(expected[i] == actual[i], $"La física cambió en la línea {i + 1}:\n  antes:   {expected[i]}\n  ahora:   {actual[i]}\n" +
                "Si el cambio es a propósito, graba otra foto con «-- --grabar-fisica=Bastidor/pruebas/fisica_pinball.txt».");
    }

    [Fact]
    public void La_misma_semilla_da_la_misma_partida() =>
        Assert.Equal(PinballAutoplay.Run(seed: 7, steps: 6000), PinballAutoplay.Run(seed: 7, steps: 6000));

    // ---------------------------------------------------------------- palancas

    /// <summary>Velocidad con la que sale una bola que cae sobre la palanca izquierda al golpearla.</summary>
    private static float ExitSpeed(FlipperInput level, float along)
    {
        var table = NewTable();
        var contact = table.Left.Pivot.Lerp(table.Left.Tip, along);
        var ball = Place(table, contact + new Vector2(0, 2.5f));
        bool fired = false;
        float speed = 0;
        for (int i = 0, firedAt = 0; i < 480 && table.Balls.Count > 0; i++)
        {
            if (!fired && ball.Pos.Y < contact.Y + 0.75f)
            {
                table.SetFlippers(level, FlipperInput.None);
                fired = true;
                firedAt = i;
            }
            table.Advance(PinballTable.Step);
            if (fired && i - firedAt < 60) speed = Math.Max(speed, ball.Vel.Length());
        }
        return speed;
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    public void Cuanto_mas_fuerte_se_pisa_mas_rapido_sale_la_bola(float along)
    {
        float soft = ExitSpeed(FlipperInput.Soft, along);
        float normal = ExitSpeed(FlipperInput.Normal, along);
        float strong = ExitSpeed(FlipperInput.Strong, along);
        Assert.True(soft < normal && normal < strong, $"suave {soft:0.0}, normal {normal:0.0}, fuerte {strong:0.0}");
        Assert.True(strong > soft * 1.8f, $"el golpe fuerte ({strong:0.0}) debería notarse mucho más que el suave ({soft:0.0})");
    }

    /// <summary>
    /// Alturas que alcanza la bola al golpearla con la palanca izquierda en 21 situaciones (7 puntos de la palanca ×
    /// 3 alturas de caída), sin setas para medir solo la fuerza del golpe.
    /// </summary>
    private static List<float> Heights(FlipperInput level)
    {
        var heights = new List<float>();
        foreach (float along in new[] { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f })
        foreach (float drop in new[] { 1.5f, 2.5f, 3.5f })
        {
            var table = NewTable();
            foreach (var bumper in table.Bumpers) bumper.C = new Vector2(-10, -10);
            var contact = table.Left.Pivot.Lerp(table.Left.Tip, along);
            var ball = Place(table, contact + new Vector2(0, drop));
            bool fired = false;
            float max = 0;
            for (int i = 0; i < 360 && table.Balls.Count > 0; i++)
            {
                if (!fired && ball.Pos.Y < contact.Y + 0.75f)
                {
                    table.SetFlippers(level, FlipperInput.None);
                    fired = true;
                }
                table.Advance(PinballTable.Step);
                max = Math.Max(max, ball.Pos.Y);
            }
            heights.Add(max);
        }
        heights.Sort();
        return heights;
    }

    [Fact]
    public void Cada_fuerza_llega_a_su_zona_de_la_mesa()
    {
        var soft = Heights(FlipperInput.Soft);
        var normal = Heights(FlipperInput.Normal);
        var strong = Heights(FlipperInput.Strong);
        float Median(List<float> h) => h[h.Count / 2];
        int Top(List<float> h) => h.Count(y => y >= PinballTable.SpinnerY);

        // Suave: hasta el logo y el agujero; casi nunca arriba.
        Assert.InRange(Median(soft), 7f, 11f);
        Assert.True(Top(soft) <= 2, $"el golpe suave llega arriba {Top(soft)} de 21 veces");
        // Normal: hasta las setas.
        Assert.InRange(Median(normal), 11f, 15f);
        Assert.True(Top(normal) <= 7, $"el golpe normal llega arriba {Top(normal)} de 21 veces");
        // Fuerte: cruza la mesa.
        Assert.True(Top(strong) >= 19, $"el golpe fuerte solo llega arriba {Top(strong)} de 21 veces");
    }

    [Fact]
    public void Con_las_palancas_en_reposo_la_bola_cae_por_el_centro()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        Place(table, new Vector2(PinballTable.Axis, 5f));
        Run(table, 3);
        Assert.Contains(events, e => e.Kind == PinballEventKind.BallLost);
        Assert.Equal(PinballTable.StartBalls - 1, table.BallsLeft);
    }

    [Fact]
    public void Con_TILT_las_palancas_no_se_mueven()
    {
        var table = NewTable();
        Place(table, new Vector2(PinballTable.Axis, 12f));
        for (int i = 0; i < 4; i++) table.Nudge(forward: true);
        table.SetFlippers(FlipperInput.Strong, FlipperInput.Strong);
        Run(table, 0.3);
        Assert.Equal(table.Left.Rest, table.Left.Angle, 4);
        Assert.Equal(table.Right.Rest, table.Right.Angle, 4);
    }

    // ---------------------------------------------------------------- meneo

    [Fact]
    public void Tres_meneos_avisan_y_el_cuarto_es_TILT()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        Place(table, new Vector2(PinballTable.Axis, 12f));
        for (int i = 0; i < 3; i++) table.Nudge(forward: true);
        Assert.False(table.Tilted);
        Assert.True(PinballTable.WarnNudges(3));
        table.Nudge(forward: true);
        Assert.True(table.Tilted);
        Assert.Contains(events, e => e.Kind == PinballEventKind.Tilt);
    }

    [Fact]
    public void Los_meneos_espaciados_no_dan_TILT()
    {
        var table = NewTable();
        Place(table, new Vector2(PinballTable.Axis, 12f));
        for (int i = 0; i < 6; i++)
        {
            table.Nudge(forward: false);
            Run(table, 1.5); // dos meneos cada 3 s: nunca cuatro en 4 s
        }
        Assert.False(table.Tilted);
    }

    // ---------------------------------------------------------------- agujero y molinete

    [Fact]
    public void El_agujero_atrapa_una_bola_lenta_y_la_suelta()
    {
        // Llega desde abajo, subiendo despacio (golpe suave). Desde arriba no puede: la seta amarilla la empuja
        // hacia abajo a la velocidad mínima de rebote, que ya es demasiado rápida para el agujero.
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        var ball = Place(table, PinballTable.SaucerC - new Vector2(0, 0.6f), new Vector2(0, 5));
        Run(table, 0.2);
        Assert.True(ball.Captured);
        Assert.Contains(events, e => e.Kind == PinballEventKind.SaucerCatch);
        Run(table, PinballTable.SaucerHold);
        Assert.False(ball.Captured);
        Assert.Contains(events, e => e.Kind == PinballEventKind.SaucerEject);
    }

    [Fact]
    public void El_agujero_deja_pasar_una_bola_rapida()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        Place(table, PinballTable.SaucerC + new Vector2(0, 1.2f), new Vector2(0, -20));
        Run(table, 0.15);
        Assert.DoesNotContain(events, e => e.Kind == PinballEventKind.SaucerCatch);
    }

    [Fact]
    public void El_molinete_da_mas_vueltas_cuanto_mas_rapida_va_la_bola()
    {
        int Turns(float speed)
        {
            var events = new List<PinballEvent>();
            var table = NewTable(events);
            Place(table, new Vector2(PinballTable.Axis, PinballTable.SpinnerY - 0.3f), new Vector2(0, speed));
            Run(table, 0.1);
            return events.Where(e => e.Kind == PinballEventKind.Spinner).Select(e => e.Value).DefaultIfEmpty(-1).First();
        }
        int slow = Turns(8), fast = Turns(30);
        Assert.InRange(slow, 0, 4);
        Assert.True(fast >= 10, $"un golpe fuerte debería dar al menos 10 vueltas (dio {fast})");
    }

    // ---------------------------------------------------------------- dianas, bola extra y multibola

    [Fact]
    public void Abatir_las_tres_dianas_da_una_sola_bola_extra_por_bola()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        Place(table, new Vector2(PinballTable.Axis, 12f));
        HitAllTargets(table);
        Assert.Equal(PinballTable.StartBalls + 1, table.BallsLeft);
        HitAllTargets(table);
        Assert.Equal(PinballTable.StartBalls + 1, table.BallsLeft);
        Assert.Contains(events, e => e.Kind == PinballEventKind.TargetsBonus);
    }

    [Fact]
    public void Con_la_reserva_llena_las_dianas_dan_multibola_y_nunca_mas_de_dos_bolas()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        Place(table, new Vector2(PinballTable.Axis, 12f));
        table.BallsLeft = PinballTable.MaxReserve;
        HitAllTargets(table);
        Assert.Equal(2, table.Balls.Count);
        Assert.Contains(events, e => e.Kind == PinballEventKind.Multiball);
        HitAllTargets(table);
        HitAllTargets(table);
        Assert.Equal(PinballTable.MaxInPlay, table.Balls.Count);
    }

    [Fact]
    public void Perder_una_bola_en_multibola_no_cuenta()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        Place(table, new Vector2(PinballTable.Axis, 12f));
        table.StartMultiball();
        int before = table.BallsLeft;
        var first = table.Balls[0];
        first.Pos = new Vector2(PinballTable.Axis, PinballTable.DrainY - 0.5f);
        first.Vel = new Vector2(0, -5);
        Run(table, 0.05);
        Assert.Single(table.Balls);
        Assert.Equal(before, table.BallsLeft);
        Assert.Contains(events, e => e.Kind == PinballEventKind.MultiballEnd);
        Assert.DoesNotContain(events, e => e.Kind == PinballEventKind.BallLost);
    }

    // ---------------------------------------------------------------- lanzador y bola salvada

    [Fact]
    public void Un_lanzamiento_flojo_vuelve_al_lanzador_y_uno_fuerte_sale()
    {
        var weak = NewTable();
        weak.PullPlunger();
        Run(weak, PinballTable.TensionTime * 0.8); // queda ~20 % de tensión
        weak.ReleasePlunger();
        Run(weak, 3); // sube hasta ~13,3 (la salida está en 16,1) y tarda algo más de 2 s en volver
        Assert.NotNull(weak.BallOnPlunger);

        var strong = NewTable();
        strong.PullPlunger();
        strong.ReleasePlunger();
        Run(strong, 1.5);
        Assert.Null(strong.BallOnPlunger);
        Assert.False(strong.Balls[0].InLane);
    }

    [Fact]
    public void La_fuerza_del_lanzador_baja_y_vuelve_a_subir()
    {
        var table = NewTable();
        table.PullPlunger();
        Assert.Equal(1, table.Tension, 3);
        Run(table, PinballTable.TensionTime / 2);
        Assert.InRange(table.Tension, 0.45, 0.55);      // bajando
        Run(table, PinballTable.TensionTime / 2);
        Assert.InRange(table.Tension, 0, 0.03);         // vacía…
        Run(table, PinballTable.TensionTime * 0.75);
        Assert.InRange(table.Tension, 0.7, 0.8);        // …y subiendo otra vez: nunca se queda sin fuerza
        Run(table, PinballTable.TensionTime * 2.25);
        Assert.InRange(table.Tension, 0.45, 1);
    }

    [Fact]
    public void Una_bola_perdida_nada_mas_lanzarla_se_salva()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        table.PullPlunger();
        table.ReleasePlunger();
        Run(table, 0.5);
        var ball = table.Balls[0];
        ball.InLane = false;
        ball.Pos = new Vector2(PinballTable.Axis, PinballTable.DrainY - 0.5f);
        ball.Vel = new Vector2(0, -5);
        Run(table, 0.05);
        Assert.Contains(events, e => e.Kind == PinballEventKind.BallSaved);
        Assert.Equal(PinballTable.StartBalls, table.BallsLeft);
    }

    [Fact]
    public void Al_perder_la_ultima_bola_se_acaba_la_partida()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        table.BallsLeft = 1;
        Place(table, new Vector2(PinballTable.Axis, 5f));
        Run(table, 3);
        Assert.True(table.IsOver);
        Assert.Contains(events, e => e.Kind == PinballEventKind.GameOver);
    }

    // ---------------------------------------------------------------- carriles y puntos

    [Fact]
    public void Encender_los_cuatro_carriles_sube_el_multiplicador()
    {
        var events = new List<PinballEvent>();
        var table = NewTable(events);
        var ball = Place(table, table.Lanes[0].C);
        foreach (var lane in table.Lanes)
        {
            ball.Pos = lane.C + new Vector2(0, 0.1f);
            ball.Vel = new Vector2(0, -1);
            table.Advance(PinballTable.Step);
        }
        Assert.Equal(2, table.Multiplier);
        Assert.All(table.Lanes, l => Assert.False(l.Lit));
        Assert.Contains(events, e => e.Kind == PinballEventKind.Cool && e.Value == 2);
    }

    [Fact]
    public void Con_TILT_no_se_suman_puntos()
    {
        var table = NewTable();
        Place(table, new Vector2(PinballTable.Axis, 12f));
        for (int i = 0; i < 4; i++) table.Nudge(forward: true);
        int score = table.Score;
        HitAllTargets(table);
        Assert.Equal(score, table.Score);
    }

    // ---------------------------------------------------------------- paredes

    [Fact]
    public void La_bola_nunca_atraviesa_las_paredes_ni_a_toda_velocidad()
    {
        var rng = new Random(42);
        for (int n = 0; n < 300; n++)
        {
            var table = NewTable();
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            var start = new Vector2(0.8f + (float)rng.NextDouble() * 7.4f, 7f + (float)rng.NextDouble() * 6f);
            var ball = Place(table, start, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * PinballTable.MaxSpeed);
            for (int i = 0; i < 480 && table.Balls.Count > 0 && !ball.Drained; i++)
            {
                table.Advance(PinballTable.Step);
                Assert.True(ball.Pos.X > -0.05f && ball.Pos.X < 10.05f && ball.Pos.Y < 20.6f,
                    $"Bola {n} se salió de la mesa en {ball.Pos} (salió de {start} a {Mathf.RadToDeg(angle):0}°)");
            }
        }
    }
}
