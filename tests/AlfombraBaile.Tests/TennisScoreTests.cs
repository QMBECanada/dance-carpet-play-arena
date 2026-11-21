namespace AlfombraBaile.Tests;

/// <summary>Marcador de tenis: 0-15-30-40, iguales, ventaja, juegos y partido al mejor de 3.</summary>
public class TennisScoreTests
{
    private static TennisScore Play(string points)
    {
        var score = new TennisScore();
        foreach (char p in points) score.Add(p == 'T');
        return score;
    }

    [Fact]
    public void Los_puntos_se_cantan_0_15_30_40()
    {
        var score = new TennisScore();
        Assert.Equal("", score.Call());
        score.Add(true);
        Assert.Equal("15 - 0", score.Call());
        score.Add(true);
        score.Add(false);
        Assert.Equal("30 - 15", score.Call());
        score.Add(true);
        Assert.Equal("40", score.PointLabel(you: true));
    }

    [Fact]
    public void Con_40_iguales_se_cantan_iguales_y_ventaja()
    {
        var score = Play("TTTRRR");
        Assert.Equal("IGUALES", score.Call());
        Assert.Equal(TennisScore.Result.Point, score.Add(true));
        Assert.Equal("VENTAJA PARA TI", score.Call());
        Assert.Equal("AD", score.PointLabel(you: true));
        score.Add(false);
        Assert.Equal("IGUALES", score.Call());
        score.Add(false);
        Assert.Equal("VENTAJA RIVAL", score.Call());
    }

    [Fact]
    public void Con_ventaja_el_siguiente_punto_gana_el_juego()
    {
        var score = Play("TTTRRRT");
        Assert.Equal(TennisScore.Result.Game, score.Add(true));
        Assert.Equal(1, score.GamesYou);
        Assert.Equal("0", score.PointLabel(you: true));
        Assert.Equal("", score.Call());
    }

    [Fact]
    public void Cuatro_puntos_seguidos_son_juego_en_blanco()
    {
        var score = Play("TTT");
        Assert.Equal(TennisScore.Result.Game, score.Add(true));
        Assert.Equal(0, score.GamesRival);
    }

    [Fact]
    public void Gana_el_partido_quien_llega_antes_a_dos_juegos()
    {
        var score = Play("TTTT" + "RRRR" + "TTT");
        Assert.Equal(1, score.GamesYou);
        Assert.Equal(1, score.GamesRival);
        Assert.Equal(TennisScore.Result.Match, score.Add(true));
        Assert.Equal(2, score.GamesYou);
    }
}
