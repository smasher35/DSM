using System.Windows;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class ConfirmacaoTextoWindow : Window
{
    private readonly string _palavraEsperada;
    public bool Confirmado { get; private set; }

    public ConfirmacaoTextoWindow(string mensagem, string palavraEsperada)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _palavraEsperada = palavraEsperada;
        TxtMensagem.Text = mensagem;
        TxtInstrucao.Text = $"Para confirmar, escreva \"{palavraEsperada}\" na caixa abaixo:";
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Confirmado = false;
        Close();
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(TxtInput.Text.Trim(), _palavraEsperada, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show($"O texto introduzido não corresponde a \"{_palavraEsperada}\".",
                "Confirmação inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Confirmado = true;
        Close();
    }
}
