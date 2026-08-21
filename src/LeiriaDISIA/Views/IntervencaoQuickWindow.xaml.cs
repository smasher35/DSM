using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class IntervencaoQuickWindow : Window
{
    private readonly PedidoIntervencao? _pedidoOrigem;
    private readonly Escola _escola;
    private readonly List<CheckBox> _checkBoxesCategorias = new();

    public bool Sucesso { get; private set; }

    public IntervencaoQuickWindow(Escola escola, PedidoIntervencao? pedidoOrigem = null)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _escola = escola;
        _pedidoOrigem = pedidoOrigem;

        TxtEscolaInfo.Text = $"{escola.Nome}  •  {escola.Agrupamento?.Nome}  •  Cód. GEPE: {escola.CodGEPE}";
        DpData.SelectedDate = DateTime.Today;

        if (pedidoOrigem != null)
            TxtDescricao.Text = pedidoOrigem.Razao;

        var categorias = App.Db.CategoriasIntervencao.Where(c => c.Ativa).OrderBy(c => c.Nome).ToList();
        foreach (var cat in categorias)
        {
            var cb = new CheckBox { Content = cat.Nome, Tag = cat, Margin = new Thickness(0, 2, 0, 2) };
            _checkBoxesCategorias.Add(cb);
            ListaCategorias.Items.Add(cb);
        }

        CmbEstado.ItemsSource = Enum.GetValues<EstadoIntervencao>();
        CmbEstado.SelectedItem = EstadoIntervencao.Fechada;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDescricao.Text))
        {
            MessageBox.Show("Descreva o tipo de intervenção realizada.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var data = DpData.SelectedDate ?? DateTime.Today;
        var estado = (EstadoIntervencao)(CmbEstado.SelectedItem ?? EstadoIntervencao.Fechada);

        var intervencao = new Intervencao
        {
            Data = data,
            Mes = data.Month,
            Ano = data.Year,
            EscolaId = _escola.Id,
            AgrupamentoId = _escola.AgrupamentoId,
            Descricao = TxtDescricao.Text.Trim(),
            MaterialRecolhidoAbatido = string.IsNullOrWhiteSpace(TxtMaterial.Text) ? null : TxtMaterial.Text,
            Estado = estado,
            MotivoPendente = estado == EstadoIntervencao.Pendente ? TxtMotivoPendente.Text : null
        };

        foreach (var cb in _checkBoxesCategorias)
        {
            if (cb.IsChecked == true && cb.Tag is CategoriaIntervencao cat)
            {
                intervencao.Categorias.Add(new IntervencaoCategoria
                {
                    CategoriaIntervencaoId = cat.Id,
                    Quantidade = 1
                });
            }
        }

        App.Db.Intervencoes.Add(intervencao);
        App.Db.SaveChanges();

        if (_pedidoOrigem != null)
        {
            var pedido = App.Db.PedidosIntervencao.First(p => p.Id == _pedidoOrigem.Id);
            pedido.IntervencaoId = intervencao.Id;
            pedido.Estado = EstadoPedido.Concluido;
            pedido.DataConclusao = DateTime.Today;
            App.Db.SaveChanges();
        }

        Sucesso = true;
        Close();
    }
}
