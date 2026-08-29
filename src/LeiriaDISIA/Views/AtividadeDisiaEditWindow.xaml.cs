using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class AtividadeDisiaEditWindow : Window
{
    private class LinhaEquipamentoRecolhido
    {
        public int EquipamentoId { get; set; }
        public string TituloLinha { get; set; } = "";
        public string SubtituloLinha { get; set; } = "";
    }

    private readonly AtividadeDisia? _existente;
    private readonly EstadoIntervencao? _estadoOriginal;
    public bool Sucesso { get; private set; }

    public AtividadeDisiaEditWindow(AtividadeDisia? atividade)
    {
        InitializeComponent();

        // Perfil Guest (Services/SessaoAtual.PodeEditar): não pode criar/editar/eliminar
        // registos - fecha-se logo a seguir a abrir, com um aviso, em vez de deixar o
        // formulário aberto só para descobrir mais tarde que não consegue gravar nada.
        if (LeiriaDISIA.Services.PermissoesService.BloquearAberturaSeGuest(this)) return;

        // Modo Compacto (Administração → Aparência): em ecrãs pequenos/portáteis, encolhe a
        // janela para caber na área de trabalho disponível - ver Services/JanelaTamanhoHelper.cs.
        // Sem efeito em ecrãs normais/grandes ou com o modo desativado.
        JanelaTamanhoHelper.AjustarSePreciso(this);

        // 1.2.1: tinge a barra de título nativa com um tom azul sóbrio, consistente com a
        // identidade da aplicação — ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal não são afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = atividade;

        CmbCategoria.ItemsSource = App.Db.CategoriasDisia.OrderBy(c => c.Nome).ToList();

        // Bug corrigido: a combo mostrava o nome técnico do enum (ex.: "EmProgresso") em vez do
        // nome configurado em Administração → Dados Fixos → "Estados das Intervenções / Atividades
        // DISIA" (EstadoCorPersonalizada.NomeExibicao). Os cinco estados em si são fixos — estão
        // associados a lógica de negócio no código (ver EstadoIntervencao) e não podem ser
        // criados/removidos/desativados nos Dados Fixos, à semelhança do que já acontece em
        // Intervenções — mas o nome apresentado é configurável e agora é lido em tempo real (a
        // lista é reconstruída sempre que esta janela abre, refletindo qualquer alteração feita
        // entretanto em Dados Fixos). ItemEstado empareia o valor do enum (gravado tal como
        // sempre) com o nome a mostrar — ver ItemEstado mais abaixo.
        CmbEstado.ItemsSource = Enum.GetValues<EstadoIntervencao>()
            .Select(estado => new ItemEstado(estado, EstadoCores.NomeExibicaoEstadoIntervencao(estado)))
            .ToList();
        CmbEstado.DisplayMemberPath = nameof(ItemEstado.NomeExibicao);
        CmbEstado.SelectedValuePath = nameof(ItemEstado.Valor);

        if (atividade == null)
        {
            TxtTitulo.Text = "Nova Atividade DISIA";
            DpData.SelectedDate = DateTime.Today;
            CmbEstado.SelectedValue = EstadoIntervencao.Fechada;
            return;
        }

        TxtTitulo.Text = "Editar Atividade DISIA";
        DpData.SelectedDate = atividade.Data;
        TxtDescricao.Text = atividade.Descricao;
        CmbCategoria.SelectedItem = ((List<CategoriaDisia>)CmbCategoria.ItemsSource)
            .FirstOrDefault(c => c.Id == atividade.CategoriaDisiaId);
        TxtLocal.Text = atividade.Local;
        TxtDivisao.Text = atividade.Divisao;
        TxtSuporte.Text = atividade.Suporte;
        TxtQuantidade.Text = atividade.Quantidade.ToString();
        CmbEstado.SelectedValue = atividade.Estado;
        _estadoOriginal = atividade.Estado;
        TxtObservacoes.Text = atividade.Observacoes;

        CarregarEquipamentoRecolhido(atividade.Id);
    }

    /// <summary>Item da combo de Estado: empareia o valor do enum (o que é efetivamente gravado em
    /// AtividadeDisia.Estado) com o nome configurado em Dados Fixos para apresentação.</summary>
    private record ItemEstado(EstadoIntervencao Valor, string NomeExibicao);

    /// <summary>Mostra o equipamento recolhido que esta atividade agrega e cuja reparação está a
    /// acompanhar — ver fluxo automático em <see cref="IntervencaoEditWindow"/>. Cada linha tem um
    /// botão "✏️ Editar Equipamento" que abre o registo do equipamento para permitir atualizar
    /// diretamente o hardware (ex: troca de disco, memória adicionada) sem ter de sair desta janela.</summary>
    private void CarregarEquipamentoRecolhido(int atividadeId)
    {
        var recolhidos = App.Db.EquipamentosRecolhidos
            .Include(r => r.Equipamento).ThenInclude(eq => eq!.Escola)
            .Where(r => r.AtividadeDisiaId == atividadeId)
            .ToList();

        // 1.2.3: a área de equipamentos fica sempre visível (nunca é removida nem colapsada) —
        // basta atribuir a lista (mesmo vazia); o próprio XAML (DataTrigger em "HasItems") decide
        // declarativamente se mostra a lista ou o estado vazio, sem lógica de negócio aqui.
        ListaEquipamentoRecolhido.ItemsSource = recolhidos.Select(r => new LinhaEquipamentoRecolhido
        {
            EquipamentoId = r.EquipamentoId,
            TituloLinha = $"{r.Equipamento?.Tipo} {r.Equipamento?.Marca} {r.Equipamento?.Modelo} — Nº Série {r.Equipamento?.NumeroSerie}".Trim(),
            SubtituloLinha = $"Escola: {r.Equipamento?.Escola?.Nome ?? "—"}  •  Estado do equipamento: {r.Equipamento?.Estado}  •  " +
                $"Recolhido em {r.DataRecolha:dd/MM/yyyy}  •  Hardware: {ResumoHardware(r.Equipamento)}"
        }).ToList();
    }

    /// <summary>Resumo curto do hardware atual (processador, memória, disco), para se ver de
    /// relance na lista sem ter de abrir o equipamento — útil sobretudo depois de uma atualização.</summary>
    private static string ResumoHardware(Equipamento? eq)
    {
        if (eq == null) return "—";
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(eq.Processador)) partes.Add(eq.Processador);
        if (eq.QuantidadeMemoriaGB != null) partes.Add($"{eq.QuantidadeMemoriaGB}GB{(string.IsNullOrWhiteSpace(eq.TipoMemoria) ? "" : " " + eq.TipoMemoria)}");
        if (eq.TamanhoDiscoGB != null || !string.IsNullOrWhiteSpace(eq.TipoDisco)) partes.Add($"{eq.TipoDisco} {eq.TamanhoDiscoGB}GB".Trim());
        return partes.Count > 0 ? string.Join(", ", partes) : "—";
    }

    /// <summary>Abre o equipamento recolhido para edição direta a partir desta janela — permite,
    /// por exemplo, atualizar o tipo/tamanho de disco ou a memória depois de um upgrade feito
    /// durante a reparação, sem ser preciso ir depois ao módulo de Equipamentos à parte. Se algo de
    /// relevante no hardware mudar, um resumo é automaticamente acrescentado às Observações desta
    /// atividade (ainda por gravar, para o utilizador rever antes de clicar em "Guardar").</summary>
    private void EditarEquipamentoRecolhido_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: int equipamentoId }) return;

        var equipamento = App.Db.Equipamentos.Find(equipamentoId);
        if (equipamento == null) return;

        var janela = new EquipamentoEditWindow(equipamento, _existente) { Owner = this };
        janela.ShowDialog();
        if (!janela.Sucesso) return;

        if (!string.IsNullOrWhiteSpace(janela.ResumoAlteracoes))
        {
            TxtObservacoes.Text = string.IsNullOrWhiteSpace(TxtObservacoes.Text)
                ? janela.ResumoAlteracoes
                : $"{TxtObservacoes.Text}\n{janela.ResumoAlteracoes}";
        }

        if (_existente != null) CarregarEquipamentoRecolhido(_existente.Id);
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
            MessageBox.Show("Descreva a atividade realizada.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DpData.SelectedDate == null)
        {
            MessageBox.Show("Indique a data.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var data = DpData.SelectedDate ?? DateTime.Today;
        int.TryParse(TxtQuantidade.Text, out var quantidade);
        if (quantidade <= 0) quantidade = 1;

        AtividadeDisia atividade;
        if (_existente == null)
        {
            atividade = new AtividadeDisia();
            App.Db.AtividadesDisia.Add(atividade);
        }
        else
        {
            atividade = App.Db.AtividadesDisia.First(a => a.Id == _existente.Id);
        }

        var estadoNovo = (EstadoIntervencao)(CmbEstado.SelectedValue ?? EstadoIntervencao.Fechada);
        var estavaFechadaAntes = _estadoOriginal == EstadoIntervencao.Fechada;

        atividade.Data = data;
        atividade.Mes = data.Month;
        atividade.Ano = data.Year;
        atividade.Descricao = TxtDescricao.Text.Trim();
        atividade.CategoriaDisiaId = (CmbCategoria.SelectedItem as CategoriaDisia)?.Id;
        atividade.Local = TxtLocal.Text;
        atividade.Divisao = TxtDivisao.Text;
        atividade.Suporte = TxtSuporte.Text;
        atividade.Quantidade = quantidade;
        atividade.Estado = estadoNovo;
        atividade.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();

        // Quando a atividade PASSA a Em Progresso (não estava já em progresso), o(s)
        // equipamento(s) recolhido(s) que esta atividade agrega avançam automaticamente para
        // "Em Reparação" — tanto no registo de recolha como no próprio equipamento.
        if (estadoNovo == EstadoIntervencao.EmProgresso && _estadoOriginal != EstadoIntervencao.EmProgresso)
        {
            var recolhidosEmReparacao = App.Db.EquipamentosRecolhidos
                .Include(r => r.Equipamento)
                .Where(r => r.AtividadeDisiaId == atividade.Id && r.DataEntrega == null)
                .ToList();

            foreach (var r in recolhidosEmReparacao)
            {
                r.Estado = EstadosRecolha.EmReparacao;
                if (r.Equipamento != null)
                    r.Equipamento.Estado = EstadosEquipamento.EmReparacao;
            }

            if (recolhidosEmReparacao.Count > 0) App.Db.SaveChanges();
        }

        // Só quando a atividade PASSA a Fechada (não estava já fechada) é que o equipamento
        // recolhido associado avança para "Aguarda Entrega", ativando a devolução à escola.
        List<Escola> escolasParaDevolucao = new();
        if (estadoNovo == EstadoIntervencao.Fechada && !estavaFechadaAntes)
        {
            var recolhidosParaFechar = App.Db.EquipamentosRecolhidos
                .Include(r => r.Equipamento).ThenInclude(eq => eq!.Escola)
                .Where(r => r.AtividadeDisiaId == atividade.Id && r.DataEntrega == null)
                .ToList();

            foreach (var r in recolhidosParaFechar)
            {
                r.Estado = EstadosRecolha.AguardaEntrega;
                if (r.Equipamento != null)
                {
                    r.Equipamento.Estado = EstadosEquipamento.AguardaEntrega;
                    if (r.Equipamento.Escola != null)
                        escolasParaDevolucao.Add(r.Equipamento.Escola);
                }
            }

            if (recolhidosParaFechar.Count > 0) App.Db.SaveChanges();
        }

        Sucesso = true;
        var ownerParaNovasJanelas = Owner;
        Close();

        // Abre uma nova Intervenção (por cada escola distinta envolvida) já pronta para o botão
        // "Devolver à Escola" — o equipamento já está em "Aguarda Entrega" nesta altura.
        foreach (var escola in escolasParaDevolucao.DistinctBy(e => e.Id))
        {
            var janela = new IntervencaoEditWindow(null, escola) { Owner = ownerParaNovasJanelas };
            janela.ShowDialog();
        }
    }
}
