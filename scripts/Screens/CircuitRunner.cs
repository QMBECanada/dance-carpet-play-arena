using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Lleva el tiempo de cada estación mientras se juega: un marcador pequeño a la izquierda y, al acabar, pasa al
/// descanso o al resumen. Si se sale al carrusel, el circuito se cancela.
/// </summary>
public partial class CircuitRunner : Node
{
    private Label _label = null!;
    private PanelContainer _chip = null!;
    private bool _leaving;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var root = Root(this);
        ((CanvasLayer)root.GetParent()).Layer = 50;
        _chip = new PanelContainer { ThemeTypeVariation = "Status", Visible = false };
        _label = Label("", "Value");
        _chip.AddChild(_label);
        root.AddChild(Anchor(_chip, Control.LayoutPreset.CenterLeft, new Vector2(24, 0)));
    }

    public override void _Process(double delta)
    {
        bool inStation = Circuit.Current == Circuit.Stage.Station;
        string scene = GetTree().CurrentScene?.SceneFilePath ?? "";
        if (inStation && scene.EndsWith("Hub.tscn"))
        {
            Circuit.Cancel(); // se salió desde la pausa del juego
            inStation = false;
        }
        bool onStationScene = inStation && scene == Circuit.Station.Scene;
        _chip.Visible = onStationScene;
        if (!onStationScene)
        {
            _leaving = false;
            return;
        }

        Circuit.StationLeft -= delta;
        int s = (int)System.Math.Ceiling(System.Math.Max(0, Circuit.StationLeft));
        _label.Text = $"CIRCUITO {Circuit.Index + 1}/{Circuit.StationIds.Length}  ·  {s / 60}:{s % 60:00}";
        _label.Modulate = Circuit.StationLeft < 10 && (int)(Circuit.StationLeft * 2) % 2 == 0 ? new Color(1, 0.5f, 0.5f) : Colors.White;
        if (Circuit.StationLeft > 0 || _leaving) return;

        _leaving = true;
        Audio.Play("stars3");
        Circuit.Index++;
        Circuit.Current = Circuit.Index >= Circuit.StationIds.Length ? Circuit.Stage.Done : Circuit.Stage.Rest;
        GetTree().ChangeSceneToFile(Circuit.ScenePath);
    }
}
