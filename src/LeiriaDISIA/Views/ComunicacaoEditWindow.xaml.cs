using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class ComunicacaoEditWindow : Window
{
    private readonly Comunicacao? _existente;
    private static readonly string[] TiposLigacao = { "Fibra", "ADSL", "4G/5G", "Satélite", "Outro" };
    private static readonly string[] EstadosComunicacao = { "Ativa", "Inativa", "Pendente de Instalação", "Pendente de Integração" };

    public bool Sucesso { get; private set; }

    public ComunicacaoEditWindow(Comunicacao? comunicacao)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = comunicacao;

        CmbEscola.ItemsSource = App.Db.Escolas.Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        CmbTipoLigacao.ItemsSource = TiposLigacao;
        CmbEstado.ItemsSource = EstadosComunicacao;

        var velocidades = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.VelocidadeFibra && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToList();
        CmbVelocidadeFibra.ItemsSource = velocidades;

        CmbTipoLigacao.SelectionChanged += (_, _) => AtualizarVisibilidadeVelocidade();

        if (comunicacao == null)
        {
            TxtTitulo.Text = "Nova Ligação de Comunicações";
            CmbTipoLigacao.SelectedItem = "Fibra";
            CmbEstado.SelectedItem = "Ativa";
            DpDataInstalacao.SelectedDate = DateTime.Today;
            AtualizarVisibilidadeVelocidade();
            return;
        }

        TxtTitulo.Text = "Editar Ligação de Comunicações";
        var completa = App.Db.Comunicacoes.Include(c => c.Escola).First(c => c.Id == comunicacao.Id);

        CmbEscola.SelectedItem = ((List<Escola>)CmbEscola.ItemsSource).FirstOrDefault(e => e.Id == completa.EscolaId);
        CmbTipoLigacao.Text = completa.TipoLigacao;
        CmbVelocidadeFibra.Text = completa.VelocidadeFibra;
        TxtOperadora.Text = completa.Operadora;
        TxtNumeroContrato.Text = completa.NumeroContrato;
        DpDataInstalacao.SelectedDate = completa.DataInstalacao;
        ChkIntegrado.IsChecked = completa.Integrado;
        CmbEstado.Text = completa.Estado;
        TxtObservacoes.Text = completa.Observacoes;
        AtualizarVisibilidadeVelocidade();
    }

    private void AtualizarVisibilidadeVelocidade()
    {
        var ehFibra = (CmbTipoLigacao.Text ?? "").Equals("Fibra", StringComparison.OrdinalIgnoreCase);
        CmbVelocidadeFibra.IsEnabled = ehFibra;
        TxtVelocidadeLabel.Opacity = ehFibra ? 1 : 0.5;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (CmbEscola.SelectedItem is not Escola escola)
        {
            MessageBox.Show("Selecione a escola.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DpDataInstalacao.SelectedDate == null)
        {
            MessageBox.Show("Indique a data.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Comunicacao comunicacao;
        if (_existente == null)
        {
            comunicacao = new Comunicacao();
            App.Db.Comunicacoes.Add(comunicacao);
        }
        else
        {
            comunicacao = App.Db.Comunicacoes.First(c => c.Id == _existente.Id);
        }

        comunicacao.EscolaId = escola.Id;
        comunicacao.TipoLigacao = string.IsNullOrWhiteSpace(CmbTipoLigacao.Text) ? "Fibra" : CmbTipoLigacao.Text.Trim();
        comunicacao.VelocidadeFibra = string.IsNullOrWhiteSpace(CmbVelocidadeFibra.Text) ? null : CmbVelocidadeFibra.Text.Trim();
        comunicacao.Operadora = string.IsNullOrWhiteSpace(TxtOperadora.Text) ? null : TxtOperadora.Text.Trim();
        comunicacao.NumeroContrato = string.IsNullOrWhiteSpace(TxtNumeroContrato.Text) ? null : TxtNumeroContrato.Text.Trim();
        comunicacao.DataInstalacao = DpDataInstalacao.SelectedDate;
        comunicacao.Integrado = ChkIntegrado.IsChecked == true;
        comunicacao.Estado = string.IsNullOrWhiteSpace(CmbEstado.Text) ? "Ativa" : CmbEstado.Text.Trim();
        comunicacao.Observacoes = string.IsNullOrWhiteSpace(TxtObservacoes.Text) ? null : TxtObservacoes.Text.Trim();

        App.Db.SaveChanges();
        Sucesso = true;
        Close();
    }
}
