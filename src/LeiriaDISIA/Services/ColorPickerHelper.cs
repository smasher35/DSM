namespace LeiriaDISIA.Services;

/// <summary>
/// Abre o seletor de cor nativo do Windows para escolher visualmente uma cor,
/// devolvendo o valor em formato hexadecimal (#RRGGBB), ou null se o utilizador cancelar.
/// </summary>
public static class ColorPickerHelper
{
    public static string? Escolher(string? corAtualHex)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };

        if (!string.IsNullOrWhiteSpace(corAtualHex))
        {
            try
            {
                var cor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(corAtualHex);
                dialog.Color = System.Drawing.Color.FromArgb(cor.A, cor.R, cor.G, cor.B);
            }
            catch
            {
                // Se a cor atual não for um hex válido, o seletor simplesmente abre sem pré-seleção.
            }
        }

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;

        var c = dialog.Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
