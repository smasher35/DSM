using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class ContactosView : UserControl
{
    private Contacto? _selecionado;
    private List<Contacto> _todos = new();

    public ContactosView()
    {
        InitializeComponent();
        CmbEscola.ItemsSource = App.Db.Escolas.OrderBy(e => e.Nome).ToList();
        Recarregar();
        Novo_Click(this, new RoutedEventArgs());
    }

    private void Recarregar()
    {
        _todos = App.Db.Contactos.Include(c => c.Escola).OrderBy(c => c.Nome).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        var termo = TxtPesquisa?.Text?.Trim();
        Grid.ItemsSource = string.IsNullOrWhiteSpace(termo)
            ? _todos
            : _todos.Where(c =>
                c.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (c.Escola?.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.EntidadeExterna?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.Funcao?.Contains(termo, StringComparison.OrdinalIgnoreCase) ?? false)
              ).ToList();
    }

    private void Filtro_Changed(object sender, TextChangedEventArgs e) => AplicarFiltro();

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionado = Grid.SelectedItem as Contacto;
        if (_selecionado == null) return;

        TxtNome.Text = _selecionado.Nome;
        CmbEscola.SelectedItem = ((List<Escola>)CmbEscola.ItemsSource).FirstOrDefault(x => x.Id == _selecionado.EscolaId);
        TxtEntidadeExterna.Text = _selecionado.EntidadeExterna;
        TxtFuncao.Text = _selecionado.Funcao;
        TxtTelefone.Text = _selecionado.Telefone;
        TxtTelemovel.Text = _selecionado.Telemovel;
        TxtEmail.Text = _selecionado.Email;
        TxtObservacoes.Text = _selecionado.Observacoes;
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        _selecionado = null;
        Grid.SelectedItem = null;
        TxtNome.Clear();
        CmbEscola.SelectedItem = null;
        TxtEntidadeExterna.Clear();
        TxtFuncao.Clear();
        TxtTelefone.Clear();
        TxtTelemovel.Clear();
        TxtEmail.Clear();
        TxtObservacoes.Clear();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text))
        {
            MessageBox.Show("Indique o nome do contacto.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selecionado == null)
        {
            _selecionado = new Contacto();
            App.Db.Contactos.Add(_selecionado);
        }

        _selecionado.Nome = TxtNome.Text.Trim();
        _selecionado.EscolaId = (CmbEscola.SelectedItem as Escola)?.Id;
        _selecionado.EntidadeExterna = string.IsNullOrWhiteSpace(TxtEntidadeExterna.Text) ? null : TxtEntidadeExterna.Text.Trim();
        _selecionado.Funcao = TxtFuncao.Text;
        _selecionado.Telefone = TxtTelefone.Text;
        _selecionado.Telemovel = TxtTelemovel.Text;
        _selecionado.Email = TxtEmail.Text;
        _selecionado.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;
        if (MessageBox.Show("Eliminar este contacto?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.Contactos.Remove(_selecionado);
        App.Db.SaveChanges();
        Recarregar();
        Novo_Click(sender, e);
    }
}
