using System;

namespace AlfombraBaile;

/// <summary>Marcador de tenis: 0-15-30-40, iguales, ventaja y juegos (gana quien llega antes a 2).</summary>
public sealed class TennisScore
{
    public enum Result { Point, Game, Match }

    private const int GamesToWin = 2;
    private int _you, _rival;

    public int GamesYou { get; private set; }
    public int GamesRival { get; private set; }

    public Result Add(bool you)
    {
        if (you) _you++;
        else _rival++;
        if ((_you < 4 && _rival < 4) || Math.Abs(_you - _rival) < 2) return Result.Point;

        if (_you > _rival) GamesYou++;
        else GamesRival++;
        _you = _rival = 0;
        return GamesYou == GamesToWin || GamesRival == GamesToWin ? Result.Match : Result.Game;
    }

    /// <summary>Lo que se ve en el marcador para cada jugador.</summary>
    public string PointLabel(bool you)
    {
        int mine = you ? _you : _rival, theirs = you ? _rival : _you;
        if (mine >= 3 && theirs >= 3) return mine > theirs ? "AD" : "40";
        return mine switch { 0 => "0", 1 => "15", 2 => "30", _ => "40" };
    }

    /// <summary>Lo que cantaría el juez de silla, o vacío al empezar un juego.</summary>
    public string Call()
    {
        if (_you == 0 && _rival == 0) return "";
        if (_you >= 3 && _rival >= 3)
            return _you == _rival ? "IGUALES" : _you > _rival ? "VENTAJA PARA TI" : "VENTAJA RIVAL";
        return $"{PointLabel(true)} - {PointLabel(false)}";
    }
}
