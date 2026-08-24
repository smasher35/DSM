using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class IntervencaoEditWindow : Window
{
    private class LinhaEquipamento
    {
        public int EquipamentoId { get; set; }
        public string NumeroSerie { get; set; } = "";
        public string NumeroInventario { get; set; } = "";
        public string Descricao { get; set; } = "";
        public int? PersistedId { get; set; }
    }

    private class LinhaRecolhidoEscola
    {
        public int Id { get; set; }
        public string TituloLinha { get; set; } = "";
        public string SubtituloLinha { get; set; } = "";
        public string Estado { get; set; } = "";
        public Brush CorFundo { get; set; } = Brushes.Gray;
        public bool PodeDevolver { get; set; }
    }

    private readonly Intervencao? _existente;

    /// <summary>Pedido de origem da intervenção — vem já preenchido quando a janela é aberta a
    /// partir de "Criar Intervenção" no <see cref="PedidoEditWindow"/>, ou pode ser preenchido
    /// depois, a meio da edição, através do botão "🔗 Associar a um Pedido..." (ver
    /// <see cref="AssociarPedido_Click"/>) — para intervenções registadas diretamente, sem passar
    /// pelo módulo de Pedidos. Em qualquer dos dois casos, ao gravar com o Estado "Fechada" este
    /// pedido é automaticamente marcado como concluído (ver o fim de <see cref="Guardar_Click"/>).</summary>
    private PedidoIntervencao? _pedidoOrigem;

    /// <summary>Cópia do valor de <see cref="_pedidoOrigem"/> tal como estava quando a janela
    /// abriu (antes de qualquer clique em "Associar"/"Remover associação"). Serve só para
    /// <see cref="Guardar_Click"/> conseguir distinguir "nunca houve pedido associado" de
    /// "havia um pedido associado e o utilizador removeu-o agora" — neste segundo caso é preciso
    /// desfazer o vínculo também na base de dados (ver Guardar_Click), não só em memória.</summary>
    private PedidoIntervencao? _pedidoOrigemInicial;
    private readonly List<CheckBox> _checkBoxesCategorias = new();
    private readonly List<Escola> _todasAsEscolas;
    private int? _intervencaoIdGuardada;

    private readonly ObservableCollection<LinhaEquipamento> _intervencionados = new();
    private readonly ObservableCollection<LinhaEquipamento> _recolhidos = new();
    private readonly ObservableCollection<LinhaEquipamento> _abatidos = new();

    public bool Sucesso { get; private set; }

    public IntervencaoEditWindow(Intervencao? intervencao, Escola? escolaPreSelecionada = null, PedidoIntervencao? pedidoOrigem = null)
    {
        InitializeComponent();
        // Modo Compacto (Administração → Aparência): em ecrãs pequenos/portáteis, encolhe a
        // janela para caber na área de trabalho disponível - ver Services/JanelaTamanhoHelper.cs.
        // Sem efeito em ecrãs normais/grandes ou com o modo desativado.
        JanelaTamanhoHelper.AjustarSePreciso(this);
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = intervencao;
        _pedidoOrigem = pedidoOrigem;
        _pedidoOrigemInicial = pedidoOrigem;

        _todasAsEscolas = App.Db.Escolas.Include(e => e.Agrupamento).Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        CmbEscola.ItemsSource = _todasAsEscolas;
        CmbEstado.ItemsSource = Enum.GetValues<EstadoIntervencao>();

        GridIntervencionados.ItemsSource = _intervencionados;
        GridRecolhidos.ItemsSource = _recolhidos;
        GridAbatidos.ItemsSource = _abatidos;

        foreach (var cat in App.Db.CategoriasIntervencao.Where(c => c.Ativa).OrderBy(c => c.Nome))
        {
            var cb = new CheckBox { Content = cat.Nome, Tag = cat, Margin = new Thickness(0, 2, 0, 2) };
            _checkBoxesCategorias.Add(cb);
            ListaCategorias.Items.Add(cb);
        }

        if (intervencao == null)
        {
            TxtTitulo.Text = "Registar Intervenção";
            DpData.SelectedDate = DateTime.Today;
            CmbEstado.SelectedItem = EstadoIntervencao.Fechada;

            if (escolaPreSelecionada != null)
                CmbEscola.SelectedItem = _todasAsEscolas.FirstOrDefault(e => e.Id == escolaPreSelecionada.Id);

            if (pedidoOrigem != null)
                TxtDescricao.Text = pedidoOrigem.Razao;

            AtualizarPainelPedidoAssociado();
            AtualizarRecolhidosDaEscola();
            return;
        }

        TxtTitulo.Text = "Editar Intervenção";
        _intervencaoIdGuardada = intervencao.Id;
        BtnImprimirPdf.IsEnabled = true;

        var completa = App.Db.Intervencoes.Include(i => i.Escola).ThenInclude(e => e!.Agrupamento)
            .Include(i => i.Categorias)
            .Include(i => i.EquipamentosIntervencionados).ThenInclude(ie => ie.Equipamento)
            .First(i => i.Id == intervencao.Id);

        CmbEscola.SelectedItem = _todasAsEscolas.FirstOrDefault(e => e.Id == completa.EscolaId);
        DpData.SelectedDate = completa.Data;
        TxtDescricao.Text = completa.Descricao;
        TxtMaterial.Text = completa.MaterialRecolhidoAbatido;
        CmbEstado.SelectedItem = completa.Estado;
        TxtMotivoPendente.Text = completa.MotivoPendente;

        var idsCategorias = completa.Categorias.Select(c => c.CategoriaIntervencaoId).ToHashSet();
        foreach (var cb in _checkBoxesCategorias)
            cb.IsChecked = cb.Tag is CategoriaIntervencao cat && idsCategorias.Contains(cat.Id);

        foreach (var ie in completa.EquipamentosIntervencionados)
        {
            if (ie.Equipamento == null) continue;
            _intervencionados.Add(new LinhaEquipamento
            {
                EquipamentoId = ie.EquipamentoId,
                NumeroSerie = ie.Equipamento.NumeroSerie,
                NumeroInventario = ie.Equipamento.NumeroInventario,
                Descricao = $"{ie.Equipamento.Tipo} {ie.Equipamento.Marca} {ie.Equipamento.Modelo}".Trim(),
                PersistedId = ie.Id
            });
        }

        foreach (var r in App.Db.EquipamentosRecolhidos.Include(r => r.Equipamento)
                     .Where(r => r.IntervencaoId == intervencao.Id || r.IntervencaoDisiaId == intervencao.Id))
        {
            if (r.Equipamento == null) continue;
            _recolhidos.Add(new LinhaEquipamento
            {
                EquipamentoId = r.EquipamentoId,
                NumeroSerie = r.Equipamento.NumeroSerie,
                NumeroInventario = r.Equipamento.NumeroInventario,
                Descricao = $"{r.Equipamento.Tipo} {r.Equipamento.Marca} {r.Equipamento.Modelo}".Trim(),
                PersistedId = r.Id
            });
        }

        foreach (var a in App.Db.EquipamentosAbatidos.Include(a => a.Equipamento).Where(a => a.IntervencaoId == intervencao.Id))
        {
            if (a.Equipamento == null) continue;
            _abatidos.Add(new LinhaEquipamento
            {
                EquipamentoId = a.Equipamento.Id,
                NumeroSerie = a.Equipamento.NumeroSerie,
                NumeroInventario = a.Equipamento.NumeroInventario,
                Descricao = $"{a.Equipamento.Tipo} {a.Equipamento.Marca} {a.Equipamento.Modelo}".Trim(),
                PersistedId = a.Id
            });
        }

        // Recupera o pedido já associado a esta intervenção (se algum), tal como a mesma lógica
        // usada ao gravar (ver o fim de Guardar_Click): ou via PedidoOrigemId (nasceu do pedido),
        // ou via IntervencaoId no próprio pedido (associado depois, através deste ecrã). Serve só
        // para mostrar corretamente o painel "Pedido de Intervenção" — a atribuição em si ao
        // gravar já continua a ser feita do mesmo modo que sempre foi.
        _pedidoOrigem = completa.PedidoOrigemId != null
            ? App.Db.PedidosIntervencao.Find(completa.PedidoOrigemId)
            : App.Db.PedidosIntervencao.FirstOrDefault(p => p.IntervencaoId == completa.Id);
        _pedidoOrigemInicial = _pedidoOrigem;

        AtualizarPainelPedidoAssociado();
        AtualizarRecolhidosDaEscola();
    }

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
        if (CmbEscola.SelectedItem is Escola escola)
            TxtEscolaInfo.Text = $"{escola.Nome}  •  {escola.Agrupamento?.Nome}  •  Cód. GEPE: {escola.CodGEPE}";
        else
            TxtEscolaInfo.Text = "";

        AtualizarRecolhidosDaEscola();
    }

    /// <summary>Atualiza o painel "Pedido de Intervenção" (mensagem informativa + visibilidade dos
    /// botões) conforme haja, ou não, um pedido associado (<see cref="_pedidoOrigem"/>) nesta
    /// intervenção — ver <see cref="AssociarPedido_Click"/> e <see cref="RemoverAssociacaoPedido_Click"/>.</summary>
    private void AtualizarPainelPedidoAssociado()
    {
        if (_pedidoOrigem == null)
        {
            TxtPedidoAssociadoInfo.Text =
                "Esta intervenção não está associada a nenhum pedido — ao fechá-la, nenhum pedido será encerrado automaticamente.";
            BtnAssociarPedido.Visibility = Visibility.Visible;
            BtnRemoverAssociacaoPedido.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtPedidoAssociadoInfo.Text =
                $"Associada ao pedido de {_pedidoOrigem.DataPedido:dd/MM/yyyy} — {_pedidoOrigem.Escola?.Nome} " +
                $"({_pedidoOrigem.Solicitante}): \"{_pedidoOrigem.Razao}\". Ao fechar esta intervenção, este pedido é " +
                "automaticamente marcado como concluído.";
            BtnAssociarPedido.Visibility = Visibility.Collapsed;
            BtnRemoverAssociacaoPedido.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Permite associar esta intervenção a um pedido já existente, quando ela foi (ou
    /// está a ser) registada diretamente, sem passar pelo módulo de Pedidos — para que, ao fechá-
    /// -la, o pedido seja também automaticamente marcado como concluído, tal como já acontecia
    /// quando a intervenção nasce a partir do próprio pedido (ver <see cref="PedidoEditWindow"/>).
    /// Ao escolher um pedido, a Escola desta intervenção passa a ser a do pedido (substitui a que
    /// estivesse selecionada, para não ficar inconsistente com o pedido escolhido), e a Descrição
    /// é preenchida com a Razão do pedido, mas só se ainda estiver vazia (não substitui uma
    /// descrição que o utilizador já tenha escrito).</summary>
    private void AssociarPedido_Click(object sender, RoutedEventArgs e)
    {
        var janela = new SelecionarPedidoWindow { Owner = this };
        if (janela.ShowDialog() != true || janela.PedidoSelecionado == null) return;

        _pedidoOrigem = janela.PedidoSelecionado;

        CmbEscola.SelectedItem = _todasAsEscolas.FirstOrDefault(esc => esc.Id == _pedidoOrigem.EscolaId);
        if (string.IsNullOrWhiteSpace(TxtDescricao.Text))
            TxtDescricao.Text = _pedidoOrigem.Razao;

        AtualizarPainelPedidoAssociado();
    }

    private void RemoverAssociacaoPedido_Click(object sender, RoutedEventArgs e)
    {
        _pedidoOrigem = null;
        AtualizarPainelPedidoAssociado();
    }

    private void AtualizarRecolhidosDaEscola()
    {
        ListaRecolhidosEscola.Items.Clear();

        if (CmbEscola.SelectedItem is not Escola escola)
        {
            TxtSemRecolhidos.Visibility = Visibility.Visible;
            TxtSemRecolhidos.Text = "Selecione uma escola para ver o equipamento recolhido.";
            return;
        }

        var recolhidos = App.Db.EquipamentosRecolhidos
            .Include(r => r.Equipamento)
            .Where(r => r.DataEntrega == null && r.Equipamento != null && r.Equipamento.EscolaId == escola.Id)
            .OrderBy(r => r.DataRecolha)
            .ToList();

        foreach (var r in recolhidos)
        {
            ListaRecolhidosEscola.Items.Add(new LinhaRecolhidoEscola
            {
                Id = r.Id,
                TituloLinha = $"{r.Equipamento!.Tipo} — Nº Série {r.Equipamento.NumeroSerie}",
                SubtituloLinha = $"Recolhido em {r.DataRecolha:dd/MM/yyyy}  •  {r.DiasEmRecolha} dia(s)",
                Estado = r.Estado,
                CorFundo = (Brush)new System.Windows.Media.BrushConverter().ConvertFromString(r.CorTempo)!,
                PodeDevolver = r.PodeSerEntregue
            });
        }

        TxtSemRecolhidos.Visibility = recolhidos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtSemRecolhidos.Text = "Sem equipamento recolhido para esta escola.";
    }

    private void DevolverRecolhido_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id }) return;

        var registo = App.Db.EquipamentosRecolhidos.Include(r => r.Equipamento).First(r => r.Id == id);
        if (!registo.PodeSerEntregue)
        {
            MessageBox.Show("Só é possível devolver equipamento depois de a Atividade DISIA de reparação ser fechada (estado 'Aguarda Entrega').",
                "Devolução não permitida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        registo.Estado = EstadosRecolha.Entregue;
        registo.DataEntrega = DateTime.Today;
        if (registo.Equipamento != null)
            registo.Equipamento.Estado = EstadosEquipamento.EmServico;
        App.Db.SaveChanges();
        AtualizarRecolhidosDaEscola();
    }

    /// <summary>Nenhum equipamento (reparado no local, a recolher ou a abater) pode ser adicionado
    /// sem uma escola selecionada — o equipamento fica sempre ligado à escola da intervenção (ver
    /// <see cref="EquipamentoPickerWindow"/> e <see cref="Guardar"/>). A validação vive aqui, na
    /// camada de comando, e não apenas num IsEnabled de botão, para não poder ser contornada.</summary>
    private void AdicionarLinha(ObservableCollection<LinhaEquipamento> lista)
    {
        var escolaId = (CmbEscola.SelectedItem as Escola)?.Id;
        if (escolaId == null)
        {
            MessageBox.Show("Selecione primeiro uma escola antes de adicionar um equipamento à intervenção.",
                "Escola não selecionada", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPesquisaEscola.Focus();
            return;
        }

        var idsJaNaLista = _intervencionados.Concat(_recolhidos).Concat(_abatidos).Select(l => l.EquipamentoId);

        var picker = new EquipamentoPickerWindow(escolaId, idsJaNaLista) { Owner = this };
        if (picker.ShowDialog() != true || picker.EquipamentoSelecionado == null) return;

        var eq = picker.EquipamentoSelecionado;
        lista.Add(new LinhaEquipamento
        {
            EquipamentoId = eq.Id,
            NumeroSerie = eq.NumeroSerie,
            NumeroInventario = eq.NumeroInventario,
            Descricao = $"{eq.Tipo} {eq.Marca} {eq.Modelo}".Trim()
        });
    }

    private void AdicionarIntervencionado_Click(object sender, RoutedEventArgs e) => AdicionarLinha(_intervencionados);
    private void AdicionarRecolhido_Click(object sender, RoutedEventArgs e) => AdicionarLinha(_recolhidos);
    private void AdicionarAbatido_Click(object sender, RoutedEventArgs e) => AdicionarLinha(_abatidos);

    private void RemoverLinha(object sender, ObservableCollection<LinhaEquipamento> lista, string tipoRegisto)
    {
        if (sender is not Button { DataContext: LinhaEquipamento linha }) return;

        if (linha.PersistedId != null)
        {
            var confirmar = MessageBox.Show(
                $"Este {tipoRegisto} já está gravado. Deseja mesmo removê-lo? Esta ação apaga o registo definitivamente.",
                "Confirmar remoção", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmar != MessageBoxResult.Yes) return;

            switch (tipoRegisto)
            {
                case "equipamento intervencionado":
                    var ie = App.Db.IntervencaoEquipamentos.Find(linha.PersistedId);
                    if (ie != null) App.Db.IntervencaoEquipamentos.Remove(ie);
                    break;
                case "equipamento recolhido":
                    var r = App.Db.EquipamentosRecolhidos.Find(linha.PersistedId);
                    if (r != null) App.Db.EquipamentosRecolhidos.Remove(r);
                    break;
                case "equipamento abatido":
                    var a = App.Db.EquipamentosAbatidos.Find(linha.PersistedId);
                    if (a != null)
                    {
                        var equipamento = App.Db.Equipamentos.Find(a.EquipamentoId);
                        if (equipamento != null) equipamento.Estado = EstadosEquipamento.EmServico;
                        App.Db.EquipamentosAbatidos.Remove(a);
                    }
                    break;
            }
            App.Db.SaveChanges();
        }

        lista.Remove(linha);
    }

    private void RemoverIntervencionado_Click(object sender, RoutedEventArgs e) => RemoverLinha(sender, _intervencionados, "equipamento intervencionado");
    private void RemoverRecolhido_Click(object sender, RoutedEventArgs e) => RemoverLinha(sender, _recolhidos, "equipamento recolhido");
    private void RemoverAbatido_Click(object sender, RoutedEventArgs e) => RemoverLinha(sender, _abatidos, "equipamento abatido");

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (Guardar()) Close();
    }

    private bool Guardar()
    {
        if (CmbEscola.SelectedItem is not Escola escola || string.IsNullOrWhiteSpace(TxtDescricao.Text))
        {
            MessageBox.Show("Selecione a escola e descreva a intervenção.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var data = DpData.SelectedDate ?? DateTime.Today;
        var estado = (EstadoIntervencao)(CmbEstado.SelectedItem ?? EstadoIntervencao.Fechada);

        Intervencao intervencao;
        if (_existente == null)
        {
            intervencao = new Intervencao();
            App.Db.Intervencoes.Add(intervencao);
        }
        else
        {
            intervencao = App.Db.Intervencoes.Include(i => i.Categorias).First(i => i.Id == _existente.Id);
            App.Db.IntervencaoCategorias.RemoveRange(intervencao.Categorias);
        }

        intervencao.Data = data;
        intervencao.Mes = data.Month;
        intervencao.Ano = data.Year;
        intervencao.EscolaId = escola.Id;
        intervencao.AgrupamentoId = escola.AgrupamentoId;
        intervencao.Descricao = TxtDescricao.Text.Trim();
        intervencao.MaterialRecolhidoAbatido = string.IsNullOrWhiteSpace(TxtMaterial.Text) ? null : TxtMaterial.Text;
        intervencao.Estado = estado;
        intervencao.MotivoPendente = estado is EstadoIntervencao.Pendente or EstadoIntervencao.EmEspera
            ? TxtMotivoPendente.Text : null;

        // O utilizador removeu (via botão "✖ Remover associação") um pedido que já estava
        // realmente ligado a esta intervenção — é preciso desfazer o vínculo na base de dados
        // também (não só limpar _pedidoOrigem em memória), ou a resolução de "pedidoLigado" mais
        // abaixo voltaria a encontrá-lo pelo IntervencaoId antigo e a "ressuscitar" a ligação.
        if (_pedidoOrigemInicial != null && _pedidoOrigem == null)
        {
            var pedidoAntigo = App.Db.PedidosIntervencao.Find(_pedidoOrigemInicial.Id);
            if (pedidoAntigo != null && pedidoAntigo.IntervencaoId == intervencao.Id)
                pedidoAntigo.IntervencaoId = null;
            intervencao.PedidoOrigemId = null;
        }

        if (_pedidoOrigem != null && intervencao.PedidoOrigemId == null)
            intervencao.PedidoOrigemId = _pedidoOrigem.Id;

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

        App.Db.SaveChanges();
        _intervencaoIdGuardada = intervencao.Id;
        BtnImprimirPdf.IsEnabled = true;

        foreach (var linha in _intervencionados.Where(l => l.PersistedId == null))
        {
            App.Db.IntervencaoEquipamentos.Add(new IntervencaoEquipamento
            {
                IntervencaoId = intervencao.Id,
                EquipamentoId = linha.EquipamentoId
            });
        }

        var novosRecolhidos = _recolhidos.Where(l => l.PersistedId == null).ToList();
        if (novosRecolhidos.Count > 0)
        {
            // Lógica de recolha centralizada em RecolhaEquipamentoService (cria a Atividade DISIA
            // que agrega a reparação de todo o equipamento recolhido nesta gravação, o registo de
            // EquipamentoRecolhido de cada um, e marca-os como "Recolhido") — reutilizada também em
            // EquipamentoEditWindow quando um equipamento novo é criado já com o estado "Recolhido".
            RecolhaEquipamentoService.RegistarRecolha(
                novosRecolhidos.Select(l => new RecolhaEquipamentoService.EquipamentoARecolher(
                    l.EquipamentoId, l.Descricao, l.NumeroSerie)).ToList(),
                escola, data, intervencao.Id);
        }

        foreach (var linha in _abatidos.Where(l => l.PersistedId == null))
        {
            App.Db.EquipamentosAbatidos.Add(new EquipamentoAbatido
            {
                EquipamentoId = linha.EquipamentoId,
                IntervencaoId = intervencao.Id,
                DataAbate = data,
                Status = "Abatido",
                EscolaOuLocal = escola.Nome,
                DescricaoEquipamento = linha.Descricao,
                NumeroSerie = linha.NumeroSerie,
                NumeroInventario = linha.NumeroInventario
            });

            var equipamento = App.Db.Equipamentos.Find(linha.EquipamentoId);
            if (equipamento != null) equipamento.Estado = EstadosEquipamento.Abatido;
        }

        App.Db.SaveChanges();

        var pedidoLigado = _pedidoOrigem != null
            ? App.Db.PedidosIntervencao.First(p => p.Id == _pedidoOrigem.Id)
            : App.Db.PedidosIntervencao.FirstOrDefault(p => p.IntervencaoId == intervencao.Id);

        if (pedidoLigado != null)
        {
            pedidoLigado.IntervencaoId = intervencao.Id;
            if (estado == EstadoIntervencao.Fechada)
            {
                pedidoLigado.Estado = EstadoPedido.Concluido;
                pedidoLigado.DataConclusao ??= DateTime.Today;
            }
            App.Db.SaveChanges();
        }

        if (estado == EstadoIntervencao.Fechada)
        {
            // Compatibilidade com registos criados por versões anteriores, em que a reparação
            // ainda era acompanhada por uma "Intervenção DISIA" (em vez de uma Atividade DISIA).
            var recolhidosDestaIntervencaoDisia = App.Db.EquipamentosRecolhidos
                .Include(r => r.Equipamento)
                .Where(r => r.IntervencaoDisiaId == intervencao.Id && r.DataEntrega == null)
                .ToList();

            foreach (var r in recolhidosDestaIntervencaoDisia)
            {
                r.Estado = EstadosRecolha.AguardaEntrega;
                if (r.Equipamento != null) r.Equipamento.Estado = EstadosEquipamento.AguardaEntrega;
            }

            if (recolhidosDestaIntervencaoDisia.Count > 0) App.Db.SaveChanges();
        }

        AtualizarRecolhidosDaEscola();
        Sucesso = true;
        return true;
    }

    private void ImprimirPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!Guardar()) return;

        var intervencao = App.Db.Intervencoes
            .Include(i => i.Escola).ThenInclude(e => e!.Agrupamento)
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Include(i => i.Categorias).ThenInclude(c => c.SubCategoria)
            .Include(i => i.EquipamentosIntervencionados).ThenInclude(ie => ie.Equipamento)
            .First(i => i.Id == _intervencaoIdGuardada);

        var recolhidosDaIntervencao = App.Db.EquipamentosRecolhidos
            .Include(r => r.Equipamento)
            .Where(r => r.IntervencaoId == intervencao.Id || r.IntervencaoDisiaId == intervencao.Id)
            .ToList();

        var abatidosDaIntervencao = App.Db.EquipamentosAbatidos
            .Include(a => a.Equipamento)
            .Where(a => a.IntervencaoId == intervencao.Id)
            .ToList();

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório da intervenção",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            FileName = $"Intervencao_{intervencao.Escola?.Nome}_{intervencao.Data:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            new IntervencaoPdfService().Gerar(intervencao, recolhidosDaIntervencao, abatidosDaIntervencao, dialog.FileName);
            var abrir = MessageBox.Show("PDF gerado com sucesso. Deseja abri-lo agora?", "Concluído",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o PDF:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
