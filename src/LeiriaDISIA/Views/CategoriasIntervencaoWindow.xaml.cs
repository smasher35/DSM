using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class CategoriasIntervencaoWindow : Window
{
    // (1.3) Esta janela passou a ser apenas de escolha de cor. O nome (criar/renomear/ativar/
    // eliminar categorias) é gerido em Administração → Dados Fixos → Listas de Valores →
    // "Categorias de Intervenção", que altera diretamente a mesma tabela CategoriasIntervencao
    // usada aqui, pelo que fica sempre em sincronia com a dropdown de inserção de Intervenções.
    private CategoriaIntervencao? _selecionada;

    public CategoriasIntervencaoWindow()
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        Recarregar();
    }

    private void Recarregar()
    {
        var lista = App.Db.CategoriasIntervencao.OrderBy(c => c.Nome).ToList();
        Grid.ItemsSource = lista;
        if (lista.Count > 0)
        {
            Grid.SelectedItem = lista[0];
        }
        else
        {
            _selecionada = null;
            TxtNome.Clear();
            TxtCor.Text = "#3B82F6";
        }
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionada = Grid.SelectedItem as CategoriaIntervencao;
        if (_selecionada == null) return;

        TxtNome.Text = _selecionada.Nome;
        TxtCor.Text = _selecionada.CorHex;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null)
        {
            MessageBox.Show("Selecione primeiro uma categoria na lista.", "Ação necessária",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtCor.Text))
        {
            MessageBox.Show("Indique a cor.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entidade = App.Db.CategoriasIntervencao.First(c => c.Id == _selecionada.Id);
        entidade.CorHex = TxtCor.Text.Trim();
        App.Db.SaveChanges();
        Recarregar();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    private void EscolherCor_Click(object sender, RoutedEventArgs e)
    {
        var novaCor = Services.ColorPickerHelper.Escolher(TxtCor.Text);
        if (novaCor != null) TxtCor.Text = novaCor;
    }
}
