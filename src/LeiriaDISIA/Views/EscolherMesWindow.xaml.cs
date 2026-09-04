using System.Windows;

namespace LeiriaDISIA.Views;

/// <summary>
/// Pequeno diálogo para escolher Ano+Mês antes de gerar um relatório "Mês Escolhido" — usado por
/// todos os botões "Mês Escolhido" do módulo Relatórios (Resumo de Intervenções por Agrupamento,
/// por Categoria e por Tipo/Agrupamento; Lista de Atividades e Resumo por Categoria das Atividades
/// DISIA), que antes liam silenciosamente o Ano/Mês de uma combo partilhada, visível apenas no
/// separador "Relatório de Atividades" — sem saber que precisava de lá ir mudar o mês primeiro, o
/// utilizador via o botão "Mês Escolhido" ir sempre direto para a gravação do PDF com o mês que lá
/// estivesse selecionado (por omissão, o mês corrente), dando a sensação de que não deixava
/// escolher nada. Este diálogo pergunta o mês ali mesmo, no momento em que o botão é premido, seja
/// qual for o separador onde o utilizador esteja — ver <see cref="Perguntar"/>, chamado a partir de
/// Views/RelatoriosWindow.xaml.cs.
/// </summary>
public partial class EscolherMesWindow : Window
{
    public int AnoEscolhido { get; private set; }
    public int MesEscolhido { get; private set; }

    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public EscolherMesWindow(int? anoInicial = null, int? mesInicial = null)
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoInicial ?? anoAtual;

        CmbMes.ItemsSource = NomesMeses;
        CmbMes.SelectedIndex = (mesInicial ?? DateTime.Today.Month) - 1;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (CmbAno.SelectedItem is not int ano || CmbMes.SelectedIndex < 0)
        {
            MessageBox.Show("Escolha o ano e o mês.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AnoEscolhido = ano;
        MesEscolhido = CmbMes.SelectedIndex + 1;
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Mostra o diálogo e devolve (Ano, Mês) escolhidos, ou <c>null</c> se o utilizador
    /// cancelar — quem chama deve tratar esse caso como "não gerar nada", sem mostrar erro nenhum
    /// (é uma desistência normal, não uma falha).</summary>
    public static (int Ano, int Mes)? Perguntar(Window owner, int? anoInicial = null, int? mesInicial = null)
    {
        var janela = new EscolherMesWindow(anoInicial, mesInicial) { Owner = owner };
        return janela.ShowDialog() == true ? (janela.AnoEscolhido, janela.MesEscolhido) : null;
    }
}
