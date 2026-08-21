using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LeiriaDISIA.Services;

/// <summary>
/// Aplica uma cor sóbria à barra de título NATIVA do Windows, através da API DWM (Desktop Window
/// Manager) do próprio sistema operativo — sem substituir o chrome da janela, sem
/// <c>WindowStyle="None"</c> e sem qualquer lógica de negócio. A janela continua 100% nativa:
/// mover, minimizar, maximizar, fechar e o comportamento modal não são afetados de forma alguma;
/// apenas a cor da barra de título passa a acompanhar a identidade visual da aplicação
/// (por omissão, a mesma cor de <c>BrushPrimary</c> — ver Themes/ModernTheme.xaml).
///
/// Só tem efeito visível no Windows 11 (build 22000 ou superior, atributo
/// <c>DWMWA_CAPTION_COLOR</c>); em versões anteriores do Windows a chamada à DWM falha e é
/// ignorada em silêncio — a janela mantém então a barra de título standard do sistema. Por isso é
/// seguro chamar isto em qualquer computador, independentemente da versão do Windows instalada.
/// </summary>
public static class TitleBarService
{
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    /// <summary>Tinge a barra de título da janela indicada com uma cor sóbria e consistente com a
    /// identidade da aplicação. Deve ser chamado depois de a janela já ter um handle válido — por
    /// exemplo, no evento <c>SourceInitialized</c> da janela.</summary>
    public static void AplicarCorSobria(Window janela, Color? corBarra = null, Color? corTexto = null)
    {
        try
        {
            var hwnd = new WindowInteropHelper(janela).Handle;
            if (hwnd == IntPtr.Zero) return;

            var cor = corBarra ?? (Color)ColorConverter.ConvertFromString("#1F4E79");
            var corLetras = corTexto ?? Colors.White;

            var corBarraRef = ParaCOLORREF(cor);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref corBarraRef, sizeof(int));

            var corTextoRef = ParaCOLORREF(corLetras);
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref corTextoRef, sizeof(int));
        }
        catch
        {
            // Windows mais antigo (sem suporte a DWMWA_CAPTION_COLOR) ou qualquer outra falha ao
            // comunicar com a DWM: a janela simplesmente mantém a barra de título standard do
            // sistema — não há nada de crítico a tratar aqui.
        }
    }

    /// <summary>Converte uma <see cref="Color"/> do WPF para o formato COLORREF (0x00BBGGRR)
    /// esperado pela API do Windows.</summary>
    private static int ParaCOLORREF(Color cor) => cor.R | (cor.G << 8) | (cor.B << 16);
}
