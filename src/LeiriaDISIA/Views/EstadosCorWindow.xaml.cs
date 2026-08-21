using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class EstadosCorWindow : Window
{
    private readonly string _grupo;
    private EstadoCorPersonalizada? _selecionado;

    /// <param name="grupo">Ver <see cref="GruposEstadoCor"/>.</param>
    /// <param name="titulo">Título apresentado na janela (ex: "Estados das Intervenções").</param>
    public EstadosCorWindow(string grupo, string titulo)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _grupo = grupo;
        Title = titulo;
        TxtTitulo.Text = titulo;
        Recarregar();
    }

    private void Recarregar()
    {
        Grid.ItemsSource = App.Db.EstadosCorPersonalizados
            .Where(e => e.Grupo == _grupo)
            .OrderBy(e => e.Id)
            .ToList();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionado = Grid.SelectedItem as EstadoCorPersonalizada;
        if (_selecionado == null) return;

        TxtNomeExibicao.Text = _selecionado.NomeExibicao;
        TxtCor.Text = _selecionado.Cor;
    }

    private void EscolherCor_Click(object sender, RoutedEventArgs e)
    {
        var novaCor = Services.ColorPickerHelper.Escolher(TxtCor.Text);
        if (novaCor != null) TxtCor.Text = novaCor;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null)
        {
            MessageBox.Show("Selecione primeiro um estado na lista à esquerda.", "Ação necessária",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtCor.Text))
        {
            MessageBox.Show("Indique a cor.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // (1.4) O nome apresentado passou a ser editado em Dados Fixos → Listas de Valores;
        // esta janela só grava a cor.
        var entidade = App.Db.EstadosCorPersonalizados.First(e => e.Id == _selecionado.Id);
        entidade.Cor = TxtCor.Text.Trim();
        App.Db.SaveChanges();

        Recarregar();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
