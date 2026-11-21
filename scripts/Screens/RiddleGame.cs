using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Adivinanza: una pista y tantas casillas vacías como letras tiene la respuesta. 3 vidas.
/// Cada adivinanza tiene 25 segundos; si pasan 8 sin avanzar, parpadea la casilla de la siguiente letra.
/// </summary>
public partial class RiddleGame : LetterGame
{
    private const int Lives = 3;
    private const double PerRiddle = 25, HintAfter = 8;

    private readonly Queue<(string Clue, string Answer)> _queue = new();
    private int _lives = Lives, _right, _mistakes;
    private double _left, _sinceProgress;
    private int _lastIndex;
    private bool _hinting;
    private LivesView _livesView = null!;
    private TimeBar _timeBar = null!;

    protected override GameInfo Game => GameCatalog.All.First(g => g.Id == "riddle");
    protected override string ScenePath => "res://scenes/Riddle.tscn";
    protected override string ScoreTitle => "ACIERTOS";

    protected override void Begin()
    {
        _livesView = new LivesView { Lives = Lives, Max = Lives, CustomMinimumSize = new Vector2(132, 40) };
        _livesView.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        _timeBar = new TimeBar { CustomMinimumSize = new Vector2(220, 12) };
        RightBox.AddChild(_livesView);
        RightBox.MoveChild(_livesView, 0);
        RightBox.AddChild(_timeBar);
        RightBox.MoveChild(_timeBar, 1);

        foreach (var riddle in WordBank.Riddles.Where(r => WordBank.Fits(r.Answer)).OrderBy(_ => Rng.Next()))
            _queue.Enqueue(riddle);

        Big("¡PREPÁRATE!", Themes.Current.AccentText, 0.7);
        Audio.Play("start");
        GetTree().CreateTimer(1.2).Timeout += NextRiddle;
    }

    private void NextRiddle()
    {
        if (_queue.Count == 0)
        {
            Finish("¡TODAS!", _right, $"{_right} ACIERTOS", "Has resuelto todas las adivinanzas.");
            return;
        }
        var (clue, answer) = _queue.Dequeue();
        Clue.Text = clue;
        Pop(Clue, 0.8f);
        _left = PerRiddle;
        _sinceProgress = 0;
        _lastIndex = 0;
        _hinting = false;
        SetWord(answer, showWord: false);
    }

    protected override void Tick(double delta)
    {
        if (State != Phase.Play) return;
        _left -= delta;
        _timeBar.Value = (float)Math.Max(0, _left / PerRiddle);
        _timeBar.Hurry = _left < 5;

        if (Index != _lastIndex)
        {
            _lastIndex = Index;
            _sinceProgress = 0;
            StopHint();
        }
        _sinceProgress += delta;
        if (!_hinting && _sinceProgress >= HintAfter)
        {
            _hinting = true;
            Mat.SetBlink(NextPad, true);
        }

        if (_left <= 0)
        {
            StopHint();
            Slots.Reveal();
            Big("¡TIEMPO!", Themes.Current.H, 0.5);
            LoseLife(showAnswer: true);
        }
    }

    private void StopHint()
    {
        if (!_hinting) return;
        _hinting = false;
        foreach (var pad in Ring) Mat.SetBlink(pad, false);
    }

    protected override void WordDone()
    {
        StopHint();
        _right++;
        ScoreLabel.Text = _right.ToString();
        Pop(ScoreLabel, 0.7f);
        Big("¡CORRECTO!", Themes.Current.AccentText, 0.45);
        Audio.Play("sparkle");
        GetTree().CreateTimer(1.2).Timeout += () =>
        {
            if (State == Phase.Wait) NextRiddle();
        };
    }

    protected override void Mistake(Pad pad)
    {
        _mistakes++;
        Big("¡UY!", Themes.Current.H, 0.3);
        LoseLife(showAnswer: false);
    }

    private void LoseLife(bool showAnswer)
    {
        _lives--;
        _livesView.Lives = _lives;
        if (_lives <= 0)
        {
            StopHint();
            Slots.Reveal();
            Audio.Impact("impact_soft_medium", -3, 0.6f);
            Finish("¡SE ACABÓ!", _right, _right == 1 ? "1 ACIERTO" : $"{_right} ACIERTOS",
                $"Era «{Answer}».   ·   Fallos: {_mistakes}");
            return;
        }
        if (!showAnswer) return;
        // Se acabó el tiempo de esta adivinanza: se ve la respuesta y se pasa a la siguiente.
        State = Phase.Wait;
        Mat.ClearAll();
        GetTree().CreateTimer(1.8).Timeout += () =>
        {
            if (State == Phase.Wait) NextRiddle();
        };
    }
}
