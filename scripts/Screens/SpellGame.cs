using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Deletrear: 90 segundos para pisar palabras en orden, cada vez más largas.
/// 10 puntos por letra y un premio por palabra completa; cada fallo quita 3 segundos.
/// </summary>
public partial class SpellGame : LetterGame
{
    private const double Duration = 90, Penalty = 3;

    private readonly HashSet<string> _used = new();
    private double _left = Duration;
    private int _score, _words, _mistakes;
    private Label _timeLabel = null!;
    private TimeBar _timeBar = null!;

    protected override GameInfo Game => GameCatalog.All.First(g => g.Id == "spell");
    protected override string ScenePath => "res://scenes/Spell.tscn";
    protected override string ScoreTitle => "PUNTOS";

    protected override void Begin()
    {
        _timeLabel = Label("1:30", "Big", HorizontalAlignment.Right);
        _timeBar = new TimeBar { CustomMinimumSize = new Vector2(220, 12) };
        RightBox.AddChild(_timeLabel);
        RightBox.MoveChild(_timeLabel, 0);
        RightBox.AddChild(_timeBar);
        RightBox.MoveChild(_timeBar, 1);

        Clue.Text = "DELETREA";
        Big("¡PREPÁRATE!", Themes.Current.AccentText, 0.7);
        Audio.Play("start");
        GetTree().CreateTimer(1.2).Timeout += NextWord;
    }

    private void NextWord()
    {
        int tier = Math.Min(_words / 3, WordBank.SpellTiers.Length - 1);
        var options = WordBank.SpellTiers[tier].Where(w => WordBank.Fits(w) && !_used.Contains(w)).ToArray();
        if (options.Length == 0) options = WordBank.SpellTiers[tier];
        string word = options[Rng.Next(options.Length)];
        _used.Add(word);
        Clue.Text = "DELETREA";
        SetWord(word, showWord: true);
    }

    protected override void Tick(double delta)
    {
        _left = Math.Max(0, _left - delta);
        int secs = (int)Math.Ceiling(_left);
        _timeLabel.Text = $"{secs / 60}:{secs % 60:00}";
        _timeBar.Value = (float)(_left / Duration);
        _timeBar.Hurry = _left <= 10;
        if (_left <= 0)
        {
            Big("¡TIEMPO!", Themes.Current.AccentText, 0.6);
            Audio.Impact("impact_bell_heavy", -8, 1.2f);
            Finish("¡SE ACABÓ!", _score, $"{_score} PUNTOS",
                $"Palabras: {_words}   ·   Fallos: {_mistakes}");
        }
    }

    protected override void WordDone()
    {
        int bonus = Plain.Length * 20;
        _score += Plain.Length * 10 + bonus;
        _words++;
        ScoreLabel.Text = _score.ToString();
        Pop(ScoreLabel, 0.7f);
        Big($"¡{Answer}!", Themes.Current.AccentText, 0.35);
        Audio.Play("sparkle");
        GetTree().CreateTimer(0.75).Timeout += () =>
        {
            if (State == Phase.Wait) NextWord();
        };
    }

    protected override void Mistake(Pad pad)
    {
        _mistakes++;
        _left = Math.Max(0, _left - Penalty);
        Big($"−{Penalty:0} s", Themes.Current.H, 0.3);
    }
}
