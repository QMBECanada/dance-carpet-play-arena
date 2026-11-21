using System;
using System.Linq;
using Godot;

namespace AlfombraBaile;

/// <summary>Un tema de color. Todos asignan los mismos papeles, así que cambiar de tema cambia toda la escena.</summary>
public sealed record GameTheme(
    string Id, string Name, Color Bg, Color Floor, Color Card, Color V, Color H, Color Corner, Color Accent, Color Text,
    Color Ink, bool Light)
{
    public Color Role(ColorRole role) => role switch
    {
        ColorRole.V => V,
        ColorRole.H => H,
        ColorRole.Corner => Corner,
        _ => Accent,
    };

    /// <summary>El acento para texto: en los temas claros, si es muy pálido, se oscurece para que se lea.</summary>
    public Color AccentText => Light && Accent.Luminance > 0.55f ? Accent.Darkened(0.5f) : Accent;
}

public static class Themes
{
    private static GameTheme T(string id, string name, string bg, string floor, string card, string v, string h, string corner,
        string accent, string text, string ink, bool light = false) =>
        new(id, name, new Color(bg), new Color(floor), new Color(card), new Color(v), new Color(h), new Color(corner),
            new Color(accent), new Color(text), new Color(ink), light);

    public static readonly GameTheme[] All =
    {
        T("stay", "Stay Cool!", "#101418", "#1C2228", "#1E2A33", "#29A8E0", "#E8413C", "#F4F1EA", "#FFD23F", "#FFFFFF", "#101418"),
        T("neon", "Neón", "#0A0A1A", "#14102B", "#1A1033", "#00F0FF", "#FF2BD6", "#7CFF4F", "#FAFF00", "#E9E7FF", "#0A0A1A"),
        T("sunset", "Atardecer", "#1D1135", "#3C1F4F", "#2E1846", "#F7C59F", "#FF6B35", "#EF476F", "#FFD166", "#FFF4E6", "#1D1135"),
        T("sea", "Mar", "#03141F", "#0B3954", "#0B2A3D", "#087E8B", "#3DCCC7", "#BFD7EA", "#FF5A5F", "#E0FBFC", "#03141F"),
        T("space", "Espacio", "#05060F", "#1B1F3B", "#121633", "#3DDCFF", "#7B5CFF", "#C9C9E8", "#FFB86B", "#F2F2FF", "#05060F"),
        T("retro", "Retro", "#2B2118", "#3E3226", "#4A3B2C", "#2A9D8F", "#E76F51", "#F2E3BC", "#E9C46A", "#FFF8E7", "#2B2118"),
        T("pastel", "Pastel", "#F6F1FB", "#E9E2F3", "#FFFFFF", "#A0C4FF", "#FFADAD", "#CAFFBF", "#FFC6FF", "#3A3550", "#3A3550", true),
        T("sky", "Cielo", "#EAF4FB", "#D6E9F5", "#FFFFFF", "#5DADE2", "#FF8A65", "#A3E4D7", "#1F6FB2", "#1C3144", "#1C3144", true),
        T("spring", "Primavera", "#F3F9EF", "#E1EFD8", "#FFFFFF", "#7BC47F", "#F49AC2", "#FFE066", "#3A8D4F", "#23402B", "#23402B", true),
        T("cream", "Café con leche", "#F7EFE5", "#EADBC8", "#FFFBF5", "#8FB9A8", "#D9895B", "#E8C07D", "#A0522D", "#3B2A1E", "#3B2A1E", true),
        T("beach", "Playa", "#FFF8EC", "#FBE7C6", "#FFFFFF", "#36C5D9", "#FF6F59", "#FFD23F", "#E0457B", "#2A2D3E", "#2A2D3E", true),
    };

    private static GameTheme? _current;

    public static GameTheme Current
    {
        get
        {
            if (_current is null)
            {
                Settings.Load();
                _current = All.FirstOrDefault(t => t.Id == Settings.ThemeId) ?? All[0];
            }
            return _current;
        }
    }

    public static event Action? Changed;

    public static int IndexOf(GameTheme theme) => Array.IndexOf(All, theme);

    public static void Set(GameTheme theme)
    {
        if (ReferenceEquals(theme, _current)) return;
        _current = theme;
        Settings.ThemeId = theme.Id;
        Settings.Save();
        Changed?.Invoke();
    }
}
