using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class EquipamentoRecolhidoEditWindow : Window
{
    private readonly EquipamentoRecolhido? _existente;
    private Equipamento? _equipamentoSelecionado;
    private readonly List<Escola> _todasAsEscolas;

    public bool Sucesso { get; private set; }

    /// <param name="recolha">Registo a editar; ou null para registar uma nova recolha.</param>
    public EquipamentoRecolhidoEditWindow(EquipamentoRecolhido? recolha)
    {
        InitializeComponent();

        // Perfil Guest (Services/SessaoAtual.PodeEditar): não pode criar/editar/eliminar
        // registos - fecha-se logo a seguir a abrir, com um aviso, em vez de deixar o
        // formulário aberto só para descobrir mais tarde que não consegue gravar nada.
        if (LeiriaDISIA.Services.PermissoesService.BloquearAberturaSeGuest(this)) return;
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = recolha;

        _todasAsEscolas = App.Db.Escolas.Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        CmbEscola.ItemsSource = _todasAsEscolas;

        var estados = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.EstadoRecolha && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToList();
        CmbEstado.ItemsSource = estados;

        if (recolha == null)
        {
            TxtTitulo.Text = "Registar Recolha";
            DpRecolha.SelectedDate = DateTime.Today;
            CmbEstado.SelectedItem = estados.FirstOrDefault(e => e == EstadosRecolha.Pendente) ?? estados.FirstOrDefault();
            return;
        }

        TxtTitulo.Text = "Editar Recolha";
        BtnEscolherEquipamento.IsEnabled = false; // não se troca o equipamento de uma recolha já registada
        TxtPesquisaEscola.IsEnabled = false;
        CmbEscola.IsEnabled = false;

        var completo = App.Db.EquipamentosRecolhidos.Include(r => r.Equipamento).ThenInclude(eq => eq!.Escola)
            .First(r => r.Id == recolha.Id);

        _equipamentoSelecionado = completo.Equipamento;
        CmbEscola.SelectedItem = _todasAsEscolas.FirstOrDefault(e => e.Id == _equipamentoSelecionado?.EscolaId);
        AtualizarTextoEquipamento();
        DpRecolha.SelectedDate = completo.DataRecolha;
        CmbEstado.SelectedItem = estados.FirstOrDefault(e => e == completo.Estado) ?? completo.Estado;
        DpEntrega.SelectedDate = completo.DataEntrega;
        TxtObservacoes.Text = completo.Observacoes;
    }

    /// <summary>7: campo de busca sobre a lista de escolas, tal como já usado noutros módulos
    /// (ex.: registo de Intervenções) — filtra por nome, localidade ou código GEPE.</summary>
    private void TxtPesquisaEscola_TextChanged(object sender, TextChangedEventArgs e)
    {
        var termo = TxtPesquisaEscola.Text.Trim();
        var filtradas = string.IsNullOrWhiteSpace(termo)
            ? _todasAsEscolas
            : _todasAsEscolas.Where(esc =>
                esc.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (esc.Localidade ?? "").Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                (esc.CodGEPE?.ToString() ?? "").Contains(termo, StringComparison.OrdinalIgnoreCase))
              .ToList();

        var selecionadaAtual = CmbEscola.SelectedItem as Escola;
        CmbEscola.ItemsSource = filtradas;
        if (selecionadaAtual != null && filtradas.Contains(selecionadaAtual))
            CmbEscola.SelectedItem = selecionadaAtual;
    }

    private void CmbEscola_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Mudar de escola invalida a escolha anterior de equipamento (era de outra escola).
        _equipamentoSelecionado = null;
        AtualizarTextoEquipamento();
    }

    private void AtualizarTextoEquipamento()
    {
        TxtEquipamentoSelecionado.Text = _equipamentoSelecionado == null
            ? ""
            : $"{_equipamentoSelecionado.NumeroSerie} — {_equipamentoSelecionado.Tipo} {_equipamentoSelecionado.Marca} {_equipamentoSelecionado.Modelo} " +
              $"({_equipamentoSelecionado.Escola?.Nome ?? "sem escola"})";
    }

    /// <summary>7: nova lógica — a escola tem de ser escolhida primeiro; o picker mostra apenas o
    /// equipamento dessa escola que ainda esteja disponível para recolha (exclui equipamento já
    /// com uma recolha ativa, corrigindo o erro que permitia recolher o mesmo equipamento mais do
    /// que uma vez). Se a escola não tiver equipamento disponível, é mostrada uma mensagem.</summary>
    private void EscolherEquipamento_Click(object sender, RoutedEventArgs e)
    {
        if (CmbEscola.SelectedItem is not Escola escola)
        {
            MessageBox.Show("Escolha primeiro a escola de onde o equipamento vai ser recolhido.",
                "Escola por escolher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existeDisponivel = App.Db.Equipamentos.Any(eq =>
            eq.EscolaId == escola.Id &&
            eq.Estado != EstadosEquipamento.Abatido &&
            !App.Db.EquipamentosRecolhidos.Any(r => r.EquipamentoId == eq.Id && r.Estado != EstadosRecolha.Entregue));

        if (!existeDisponivel)
        {
            MessageBox.Show($"A escola \"{escola.Nome}\" não tem, atualmente, nenhum equipamento disponível para recolha " +
                "(ou já está tudo recolhido/abatido).", "Sem equipamento disponível",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new EquipamentoPickerWindow(escola.Id, excluirJaRecolhido: true, restringirAEscola: true, exigirNaEscola: true) { Owner = this };
        if (picker.ShowDialog() != true || picker.EquipamentoSelecionado == null) return;

        _equipamentoSelecionado = App.Db.Equipamentos.Include(eq => eq.Escola)
            .First(eq => eq.Id == picker.EquipamentoSelecionado.Id);
        AtualizarTextoEquipamento();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (_equipamentoSelecionado == null)
        {
            MessageBox.Show("Só é possível recolher equipamento que já exista no inventário. Escolha o equipamento.",
                "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CmbEstado.SelectedItem is not string estado)
        {
            MessageBox.Show("Selecione o estado da recolha.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var novaRecolha = _existente == null;

        EquipamentoRecolhido registo;
        if (novaRecolha)
        {
            registo = new EquipamentoRecolhido { EquipamentoId = _equipamentoSelecionado.Id };
            App.Db.EquipamentosRecolhidos.Add(registo);
        }
        else
        {
            registo = App.Db.EquipamentosRecolhidos.First(r => r.Id == _existente.Id);
        }

        var dataRecolha = DpRecolha.SelectedDate ?? DateTime.Today;
        registo.DataRecolha = dataRecolha;
        registo.Estado = estado;
        registo.DataEntrega = estado.Equals(EstadosRecolha.Entregue, StringComparison.OrdinalIgnoreCase)
            ? (DpEntrega.SelectedDate ?? DateTime.Today)
            : DpEntrega.SelectedDate;
        registo.Observacoes = string.IsNullOrWhiteSpace(TxtObservacoes.Text) ? null : TxtObservacoes.Text.Trim();

        // Quando a recolha é criada avulsa (não a partir de uma Intervenção existente), é
        // automaticamente aberta uma Atividade DISIA ("Pendente") para que a reparação fique
        // refletida no módulo de Atividades DISIA, sem passos manuais adicionais. O equipamento
        // passa para "Recolhido" assim que é recolhido, tenha ou não escola associada. Só quando
        // alguém mudar manualmente o estado desta Atividade DISIA para "Em Progresso" é que o
        // equipamento avança para "Em Reparação" (ver AtividadeDisiaEditWindow.Guardar_Click).
        if (novaRecolha)
            _equipamentoSelecionado.Estado = EstadosEquipamento.Recolhido;

        if (novaRecolha && registo.AtividadeDisiaId == null)
        {
            var descricaoEquip = $"{_equipamentoSelecionado.Tipo} {_equipamentoSelecionado.Marca} {_equipamentoSelecionado.Modelo}".Trim();
            var descricaoAtividade = $"Reparação de equipamento recolhido: {descricaoEquip} (Nº Série {_equipamentoSelecionado.NumeroSerie}).";
            if (!string.IsNullOrWhiteSpace(registo.Observacoes))
                descricaoAtividade += $" Observações: {registo.Observacoes}";

            var atividadeAuto = new AtividadeDisia
            {
                Data = dataRecolha,
                Mes = dataRecolha.Month,
                Ano = dataRecolha.Year,
                Local = _equipamentoSelecionado.Escola?.Nome,
                Descricao = descricaoAtividade,
                Estado = EstadoIntervencao.Pendente
            };
            App.Db.AtividadesDisia.Add(atividadeAuto);
            App.Db.SaveChanges();

            registo.AtividadeDisiaId = atividadeAuto.Id;
        }

        App.Db.SaveChanges();
        Sucesso = true;
        Close();
    }
}
