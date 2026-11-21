namespace AlfombraBaile;

/// <summary>
/// Barra de fuerza oscilante (lanzador del pinball, lanzamiento del curling): al empezar está llena, baja hasta 0 y
/// vuelve a subir hasta 1, una y otra vez, hasta que se suelta. Así nunca se queda sin fuerza: hay que esperar el momento.
/// </summary>
public sealed class PowerMeter
{
    private int _direction; // −1 bajando, +1 subiendo, 0 parada

    public double Value { get; private set; }
    public bool Running => _direction != 0;

    /// <summary>Empieza llena y bajando.</summary>
    public void Start()
    {
        Value = 1;
        _direction = -1;
    }

    /// <summary>Se para a <paramref name="value"/> (0 por defecto).</summary>
    public void Stop(double value = 0)
    {
        Value = value;
        _direction = 0;
    }

    /// <summary>Avanza: recorre de 1 a 0 (o de 0 a 1) en <paramref name="sweepSeconds"/> segundos y rebota en los extremos.</summary>
    public void Advance(double delta, double sweepSeconds)
    {
        if (_direction == 0) return;
        Value += _direction * delta / sweepSeconds;
        if (Value <= 0)
        {
            Value = -Value;
            _direction = 1;
        }
        else if (Value >= 1)
        {
            Value = 2 - Value;
            _direction = -1;
        }
    }
}
