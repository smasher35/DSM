using System.Windows;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

/// <summary>Pequena janela genérica para pedir um valor de texto ao utilizador (ex: email de teste).</summary>
public partial class InputTextoWindow : Window
{
    public string? TextoIntroduzido { get; private set; }

    public InputTextoWindow(string mensagem, string? valorInicial = null)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        TxtMensagem.Text = mensagem;
        TxtInput.Text = valorInicial ?? "";
        TxtInput.Focus();
        TxtInput.SelectAll();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        TextoIntroduzido = null;
        Close();
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        TextoIntroduzido = TxtInput.Text;
        Close();
    }
}
