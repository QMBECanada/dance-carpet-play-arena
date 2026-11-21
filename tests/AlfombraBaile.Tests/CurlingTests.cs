using Godot;

namespace AlfombraBaile.Tests;

/// <summary>Pruebas de la física, la puntuación y el rival del curling, sin Godot.</summary>
public class CurlingTests
{
    /// <summary>Lanza una piedra en una pista vacía (o la dada), barriendo como se indique, y la deja pararse.</summary>
    private static CurlingSheet.Stone Throw(float tension, int curl = 1, float aim = 0,
        SweepInput left = SweepInput.None, SweepInput right = SweepInput.None, CurlingSheet? sheet = null)
    {
        sheet ??= new CurlingSheet();
        var stone = sheet.Throw(0, aim, tension, curl);
        sheet.SetSweep(left, right);
        for (int i = 0; i < 40000 && sheet.Moving; i++) sheet.Advance(CurlingSheet.Step / CurlingSheet.TimeScale);
        return stone;
    }

    private static CurlingSheet.Stone Place(CurlingSheet sheet, int team, Vector2 pos)
    {
        var stone = sheet.Throw(team, 0, 0, 0);
        stone.Pos = pos;
        stone.Vel = Vector2.Zero;
        sheet.SettleWithoutSweeping();
        return stone;
    }

    // ---------------------------------------------------------------- fuerza

    [Fact]
    public void Muy_flojo_se_queda_antes_de_la_linea_y_queda_fuera()
    {
        var stone = Throw(0.1f);
        Assert.True(stone.Out);
        Assert.True(stone.Pos.Y < CurlingSheet.HogY);
    }

    [Fact]
    public void Con_tension_media_llega_a_la_casa()
    {
        var stone = Throw(0.53f);
        Assert.False(stone.Out);
        Assert.True(CurlingSheet.InHouse(stone), $"se paró en {stone.Pos}");
    }

    [Fact]
    public void A_toda_fuerza_y_sin_piedras_se_sale_por_el_fondo()
    {
        var stone = Throw(1f);
        Assert.True(stone.Out);
        Assert.True(stone.Pos.Y > CurlingSheet.TeeY);
    }

    [Fact]
    public void Un_lanzamiento_a_la_casa_dura_unos_diez_segundos()
    {
        var sheet = new CurlingSheet();
        sheet.Throw(0, 0, 0.53f, 1);
        int frames = 0;
        while (sheet.Moving) { sheet.Advance(1.0 / 60); frames++; }
        Assert.InRange(frames / 60.0, 8, 13);
    }

    // ---------------------------------------------------------------- curva y barrenderos

    [Fact]
    public void La_piedra_curva_hacia_el_lado_de_su_giro_alrededor_de_un_metro()
    {
        var right = Throw(0.5f, curl: 1);
        var left = Throw(0.5f, curl: -1);
        Assert.InRange(right.Pos.X, 0.6f, 1.5f);
        Assert.Equal(-right.Pos.X, left.Pos.X, 3);
    }

    [Fact]
    public void El_barrendero_izquierdo_empuja_a_la_derecha_y_el_derecho_a_la_izquierda()
    {
        float none = Throw(0.5f).Pos.X;
        Assert.True(Throw(0.5f, left: SweepInput.Strong).Pos.X > none);
        Assert.True(Throw(0.5f, right: SweepInput.Strong).Pos.X < none);
    }

    [Fact]
    public void La_fila_de_arriba_desvia_mas_que_la_de_abajo()
    {
        float none = Throw(0.5f).Pos.X;
        float strong = Throw(0.5f, left: SweepInput.Strong).Pos.X - none;
        float soft = Throw(0.5f, left: SweepInput.Soft).Pos.X - none;
        Assert.True(strong > soft * 2 && soft > 0, $"fuerte {strong:0.00} m, suave {soft:0.00} m");
    }

    [Fact]
    public void Barrer_con_X_y_O_a_la_vez_no_desvia_y_llega_algo_mas_lejos()
    {
        var none = Throw(0.5f);
        var both = Throw(0.5f, left: SweepInput.Strong, right: SweepInput.Strong);
        Assert.InRange(both.Pos.Y - none.Pos.Y, 0.5f, 3f);
        Assert.True(Mathf.Abs(both.Pos.X - none.Pos.X) < 0.25f, $"desvío {both.Pos.X - none.Pos.X:0.00} m");
    }

    [Fact]
    public void Frotar_con_triangulo_y_cuadrado_a_la_vez_frena_sin_desviar()
    {
        var none = Throw(0.5f);
        var both = Throw(0.5f, left: SweepInput.Soft, right: SweepInput.Soft);
        Assert.True(both.Pos.Y < none.Pos.Y - 0.5f, $"none {none.Pos.Y:0.00}, frotando {both.Pos.Y:0.00}");
        Assert.True(Mathf.Abs(both.Pos.X - none.Pos.X) < 0.25f);
    }

    [Fact]
    public void Barrer_solo_afecta_a_la_piedra_lanzada()
    {
        var sheet = new CurlingSheet();
        var still = Place(sheet, 1, new Vector2(1.5f, 28f));
        Throw(0.5f, aim: -1.5f, left: SweepInput.Strong, right: SweepInput.Strong, sheet: sheet);
        Assert.Equal(new Vector2(1.5f, 28f), still.Pos);
    }

    // ---------------------------------------------------------------- choques y límites

    [Fact]
    public void Un_choque_de_frente_pasa_casi_toda_la_velocidad_a_la_piedra_golpeada()
    {
        var sheet = new CurlingSheet();
        var target = Place(sheet, 1, new Vector2(0, 26f)); // pasada la línea: antes quedaría fuera
        var thrown = sheet.Throw(0, 0, 0.8f, 0);
        float speedBefore = 0;
        for (int i = 0; i < 20000 && target.Vel.LengthSquared() == 0; i++)
        {
            speedBefore = thrown.Vel.Length();
            sheet.Advance(CurlingSheet.Step / CurlingSheet.TimeScale);
        }
        Assert.True(target.Vel.Length() > speedBefore * 0.85f);
        Assert.True(thrown.Vel.Length() < speedBefore * 0.15f);
    }

    [Fact]
    public void Las_piedras_que_tocan_el_lateral_quedan_fuera()
    {
        var stone = Throw(0.9f, curl: 0, aim: CurlingSheet.MaxAim);
        Assert.True(stone.Out);
    }

    [Fact]
    public void Las_piedras_nunca_se_solapan()
    {
        var rng = new Random(3);
        var sheet = new CurlingSheet();
        for (int n = 0; n < 8; n++)
        {
            sheet.Throw(n % 2, (float)(rng.NextDouble() * 2 - 1), 0.45f + (float)rng.NextDouble() * 0.5f, rng.Next(2) * 2 - 1);
            sheet.SettleWithoutSweeping();
            var stones = sheet.InPlay.ToList();
            for (int i = 0; i < stones.Count; i++)
            for (int j = i + 1; j < stones.Count; j++)
                Assert.True((stones[i].Pos - stones[j].Pos).Length() >= 2 * CurlingSheet.StoneR - 0.01f);
        }
    }

    // ---------------------------------------------------------------- puntuación

    [Theory]
    [InlineData(0.0f, 10)]
    [InlineData(0.5f, 6)]
    [InlineData(1.1f, 3)]
    [InlineData(1.9f, 1)]
    [InlineData(2.5f, 0)]
    public void Puntos_en_solitario_segun_el_anillo(float distance, int points)
    {
        var sheet = new CurlingSheet();
        var stone = Place(sheet, 0, CurlingSheet.Tee + new Vector2(distance, 0));
        Assert.Equal(points, CurlingSheet.SoloPoints(stone));
    }

    [Fact]
    public void Gana_la_entrada_quien_tiene_la_piedra_mas_cercana_y_cuenta_las_que_mejoran_a_la_del_rival()
    {
        var sheet = new CurlingSheet();
        Place(sheet, 0, CurlingSheet.Tee + new Vector2(0.2f, 0));
        Place(sheet, 0, CurlingSheet.Tee + new Vector2(-0.7f, 0));
        Place(sheet, 1, CurlingSheet.Tee + new Vector2(0, 1.0f));
        Place(sheet, 0, CurlingSheet.Tee + new Vector2(1.6f, 0));
        Assert.Equal((0, 2), sheet.EndScore());
    }

    [Fact]
    public void Las_piedras_que_puntuan_son_las_mas_cercanas_del_equipo_que_gana()
    {
        var sheet = new CurlingSheet();
        var a = Place(sheet, 0, CurlingSheet.Tee + new Vector2(0.2f, 0));
        var b = Place(sheet, 0, CurlingSheet.Tee + new Vector2(-0.7f, 0));
        Place(sheet, 1, CurlingSheet.Tee + new Vector2(0, 1.0f));
        Place(sheet, 0, CurlingSheet.Tee + new Vector2(1.6f, 0));
        var counting = sheet.CountingStones();
        Assert.Equal(2, counting.Count);
        Assert.Contains(a, counting);
        Assert.Contains(b, counting);
    }

    [Fact]
    public void Sin_piedras_en_la_casa_nadie_puntua()
    {
        var sheet = new CurlingSheet();
        Place(sheet, 0, new Vector2(0, 26f));
        Assert.Equal((-1, 0), sheet.EndScore());
    }

    // ---------------------------------------------------------------- rival

    [Fact]
    public void El_rival_dificil_deja_su_piedra_en_la_casa_con_la_pista_vacia()
    {
        int inHouse = 0;
        for (int seed = 0; seed < 5; seed++)
        {
            var sheet = new CurlingSheet();
            var shot = CurlingAI.Choose(sheet, 1, CurlingLevel.Hard, new Random(seed));
            var stone = sheet.Throw(1, shot.Aim, shot.Tension, shot.Curl);
            sheet.SettleWithoutSweeping();
            if (CurlingSheet.InHouse(stone)) inHouse++;
        }
        Assert.True(inHouse >= 4, $"solo {inHouse} de 5 en la casa");
    }

    [Fact]
    public void El_rival_dificil_saca_o_supera_una_piedra_en_el_centro()
    {
        int better = 0;
        for (int seed = 0; seed < 5; seed++)
        {
            var sheet = new CurlingSheet();
            Place(sheet, 0, CurlingSheet.Tee);
            var shot = CurlingAI.Choose(sheet, 1, CurlingLevel.Hard, new Random(seed));
            sheet.Throw(1, shot.Aim, shot.Tension, shot.Curl);
            sheet.SettleWithoutSweeping();
            if (sheet.EndScore().Team == 1) better++;
        }
        Assert.True(better >= 3, $"solo gana la posición {better} de 5 veces");
    }

    [Fact]
    public void El_rival_facil_falla_mas_que_el_dificil()
    {
        float Error(CurlingLevel level)
        {
            float total = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                var sheet = new CurlingSheet();
                var shot = CurlingAI.Choose(sheet, 1, level, new Random(seed));
                var stone = sheet.Throw(1, shot.Aim, shot.Tension, shot.Curl);
                sheet.SettleWithoutSweeping();
                total += stone.Out ? 5 : (stone.Pos - CurlingSheet.Tee).Length();
            }
            return total;
        }
        Assert.True(Error(CurlingLevel.Easy) > Error(CurlingLevel.Hard));
    }
}
