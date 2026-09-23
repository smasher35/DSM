using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;

namespace LeiriaDISIA.Views;

public partial class EquipamentoEditWindow : Window
{
    // Já não é "readonly": PrepararParaNovoEquipamento (chamado por "💾 Guardar e Novo" — ver
    // GuardarNovo_Click) repõe este campo a null a meio da vida da janela, para o próximo Guardar()
    // voltar a criar um equipamento novo em vez de continuar a editar o que acabou de ser gravado.
    private Equipamento? _existente;

    /// <summary>Esconde <see cref="PainelConfirmacaoGuardado"/> alguns segundos depois de aparecer
    /// — ver MostrarConfirmacaoGuardado.</summary>
    private DispatcherTimer? _timerConfirmacaoGuardado;

    /// <summary>Quando esta janela é aberta a partir de uma Atividade DISIA (ver botão "✏️ Editar
    /// Equipamento" em <see cref="AtividadeDisiaEditWindow"/>), guarda essa atividade apenas para
    /// se saber que se deve calcular um resumo das alterações de hardware feitas — não é gravada
    /// diretamente por esta janela (ver <see cref="ResumoAlteracoes"/>).</summary>
    private readonly AtividadeDisia? _atividadeContexto;

    private readonly Dictionary<string, string?> _valoresHardwareOriginais = new();
    private List<Escola> _todasAsEscolas = [];

    /// <summary>(3) Estado do equipamento tal como estava ao abrir esta janela (ou "Em Serviço"
    /// para um equipamento novo) — usado em <see cref="Guardar_Click"/> para detetar a transição
    /// de um estado de recolha/reparação diretamente para "Em Serviço", e assim fechar/entregar
    /// automaticamente a recolha associada.</summary>
    private string? _estadoOriginal;

    /// <summary>(1.3) Campos de texto gerados dinamicamente na secção "Características
    /// Adicionais", indexados pelo Id da <see cref="CaracteristicaEquipamento"/> a que cada um
    /// corresponde — usado para ler os valores preenchidos ao gravar.</summary>
    private readonly Dictionary<int, Control> _camposCaracteristicasAdicionais = new();

    public bool Sucesso { get; private set; }

    /// <summary>Equipamento efetivamente gravado, depois de Guardar() correr com sucesso — usado
    /// por quem abre esta janela para inserir equipamento novo diretamente (ex.: "Equipamento
    /// novo entregue" numa Intervenção — ver Views/IntervencaoEditWindow.xaml.cs) e precisa do
    /// registo já com o Id atribuído pela base de dados, sem ter de o procurar separadamente.</summary>
    public Equipamento? EquipamentoGravado { get; private set; }

    /// <summary>Preenchido após "Guardar" quando esta janela foi aberta com um contexto de
    /// Atividade DISIA (<see cref="_atividadeContexto"/>) e alguma característica de hardware
    /// (processador, memória, disco, sistema operativo) foi alterada. É null se nada mudou ou se
    /// a janela não foi aberta a partir de uma atividade.</summary>
    public string? ResumoAlteracoes { get; private set; }

    private static readonly string[] TiposComputador = { "Computador de Secretária", "Portátil", "Servidor" };
    private static readonly string[] TiposMonitor = { "Monitor" };
    private static readonly string[] TiposImpressora = { "Impressora", "Multifunções" };
    private static readonly string[] TiposRede = { "Switch", "Router", "Access Point" };
    private static readonly string[] TiposCamera = { "Câmara CCTV" };
    private static readonly string[] TiposProjetor = { "Projetor", "Quadro Interativo" };

    private static readonly string[] TodosOsTipos =
        TiposComputador.Concat(TiposMonitor).Concat(TiposImpressora).Concat(TiposRede)
            .Concat(TiposCamera).Concat(TiposProjetor)
            .Concat(new[] { "Tablet", "UPS/No-break", "Telefone IP", "Outro" })
            .ToArray();

    /// <param name="escolaPreSelecionada">Só relevante quando <paramref name="equipamento"/> é
    /// null (equipamento novo) — pré-seleciona esta escola no campo "Escola (se aplicável)", para
    /// poupar um passo a quem já sabe para onde o equipamento vai (ex.: "Equipamento novo
    /// entregue" numa Intervenção — ver Views/IntervencaoEditWindow.xaml.cs). Continua a poder
    /// ser alterada livremente antes de gravar, tal como qualquer outro campo pré-preenchido.</param>
    public EquipamentoEditWindow(Equipamento? equipamento, AtividadeDisia? atividadeContexto = null,
        Escola? escolaPreSelecionada = null)
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
        // Modo normal (Modo Compacto desligado, ver acima): a janela usa SizeToContent="Height"
        // (ver XAML) para crescer conforme o Tipo de Equipamento escolhido tiver mais ou menos
        // campos de características específicas — trava-se aqui esse crescimento no limite da área
        // de trabalho disponível, para nunca ultrapassar o ecrã (com o ScrollViewer interno do XAML
        // como rede de segurança para esse caso). Sem efeito quando o Modo Compacto já mudou a
        // janela para tamanho fixo (Manual) na chamada acima.
        if (SizeToContent == SizeToContent.Height)
            MaxHeight = SystemParameters.WorkArea.Height - 24;
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = equipamento;
        _atividadeContexto = atividadeContexto;

        CmbTipo.ItemsSource = ValoresAtivos(GruposValorFixo.TipoEquipamento, TodosOsTipos);

        // Dados Fixos v2: Processador, Tipo de Memória, Tipo de Disco, Sistema Operativo (Computador),
        // Nº de Portas, Velocidade (Rede), Tipo, Resolução (Câmara), Tipo de Painel, Polegadas,
        // Resolução (Monitor) e Luminosidade, Resolução (Projetor) deixaram de vir de listas
        // genéricas de Dados Fixos ou de texto livre — vêm agora das características embutidas do
        // respetivo grupo (ver ValoresCaracteristicaEmbutida), geridas em Administração → Dados
        // Fixos → Tipos de Equipamento → (grupo). O valor escolhido continua a gravar-se
        // exatamente como sempre, nas mesmas propriedades de Equipamento (ver Guardar_Click).
        const string grpComputador = GruposCaracteristicasEquipamento.Computador;
        const string grpRede = GruposCaracteristicasEquipamento.Rede;
        const string grpCamera = GruposCaracteristicasEquipamento.Camera;
        const string grpMonitor = GruposCaracteristicasEquipamento.Monitor;
        const string grpProjetor = GruposCaracteristicasEquipamento.Projetor;

        CmbProcessador.ItemsSource = ValoresCaracteristicaEmbutida(grpComputador, "Processador");
        CmbTipoMemoria.ItemsSource = ValoresCaracteristicaEmbutida(grpComputador, "Tipo de Memória", new[] { "DDR3", "DDR4", "DDR5" });
        CmbTipoDisco.ItemsSource = ValoresCaracteristicaEmbutida(grpComputador, "Tipo de Disco", new[] { "HDD", "SSD", "NVMe" });
        CmbSistemaOperativo.ItemsSource = ValoresCaracteristicaEmbutida(grpComputador, "Sistema Operativo");

        CmbNumeroPortas.ItemsSource = ValoresCaracteristicaEmbutida(grpRede, "Nº de Portas", new[] { "4", "5", "8", "16", "24", "48" });
        CmbVelocidadeRede.ItemsSource = ValoresCaracteristicaEmbutida(grpRede, "Velocidade", new[] { "100 Mbps", "1 Gbps", "2.5 Gbps", "10 Gbps" });

        CmbTipoCamera.ItemsSource = ValoresCaracteristicaEmbutida(grpCamera, "Tipo", new[] { "IP", "Analógica" });
        CmbResolucaoCamera.ItemsSource = ValoresCaracteristicaEmbutida(grpCamera, "Resolução", new[] { "2MP", "4MP", "1080p", "4K" });

        CmbTipoPainel.ItemsSource = ValoresCaracteristicaEmbutida(grpMonitor, "Tipo de Painel", new[] { "LED", "LCD", "OLED" });
        CmbPolegadas.ItemsSource = ValoresCaracteristicaEmbutida(grpMonitor, "Polegadas", new[] { "19", "21", "24", "27", "32" });
        CmbResolucaoMonitor.ItemsSource = ValoresCaracteristicaEmbutida(grpMonitor, "Resolução", new[] { "1366x768", "1920x1080", "2560x1440", "3840x2160" });

        CmbLuminosidade.ItemsSource = ValoresCaracteristicaEmbutida(grpProjetor, "Luminosidade (Lumens)", new[] { "2000", "3000", "4000", "5000", "6000" });
        CmbResolucaoProjetor.ItemsSource = ValoresCaracteristicaEmbutida(grpProjetor, "Resolução", new[] { "1280x800", "1920x1080", "3840x2160" });

        // (12) "Tipo de Impressora" deixou de vir da lista genérica de Dados Fixos "Tipos de
        // Impressora" — passou a vir da característica embutida do grupo Impressora, tal como os
        // restantes campos acima (ver DbInitializer.MigrarCaracteristicasFixasEmbutidas). Passa a
        // ser gerido em Administração → Dados Fixos → Tipos de Equipamento → Impressora, à
        // semelhança de Computador/Rede/Câmara/Monitor/Projetor. "Ligação da Impressora" não fez
        // parte deste pedido e continua a vir de Dados Fixos genéricos, como antes.
        const string grpImpressora = GruposCaracteristicasEquipamento.Impressora;
        CmbTipoImpressora.ItemsSource = ValoresCaracteristicaEmbutida(grpImpressora, "Tipo de Impressora", new[] { "Laser", "Tinta" });
        // Antes lia da lista genérica GruposValorFixo.LigacaoImpressora — um valor acrescentado
        // pelo administrador em Características Específicas → "Tipo de Ligação" (o sítio onde
        // faz sentido gerir isto, ao lado de "Tipo de Impressora") nunca aparecia aqui, porque a
        // combo continuava ligada à lista antiga. Agora lê da mesma característica que o
        // administrador já usa, tal como CmbTipoImpressora acima.
        CmbLigacaoImpressora.ItemsSource = ValoresCaracteristicaEmbutida(grpImpressora, "Tipo de Ligação", new[] { "USB", "Rede", "WiFi" });

        CmbEstado.ItemsSource = ValoresAtivos(GruposValorFixo.EstadoEquipamento, new[]
        {
            EstadosEquipamento.EmServico, EstadosEquipamento.Recolhido, EstadosEquipamento.EmReparacao,
            EstadosEquipamento.Reparado, EstadosEquipamento.AguardaEntrega,
            EstadosEquipamento.EmArmazem, EstadosEquipamento.Abatido
        });
        _todasAsEscolas = App.Db.Escolas.Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();
        CmbEscola.ItemsSource = _todasAsEscolas;

        // Recalcular obsolescência sempre que campos relevantes mudarem
        Loaded += (_, _) => AtualizarObsolescencia();
        DpAquisicao.SelectedDateChanged += (_, _) => AtualizarObsolescencia();
        CmbProcessador.SelectionChanged += (_, _) => AtualizarObsolescencia();
        CmbProcessador.LostFocus += (_, _) => AtualizarObsolescencia();
        TxtFamiliaProcessador.TextChanged += (_, _) => AtualizarObsolescencia();
        CmbTipoDisco.SelectionChanged += (_, _) => AtualizarObsolescencia();
        CmbTipoDisco.LostFocus += (_, _) => AtualizarObsolescencia();

        // Dados Fixos v2: "Memória (GB)" e "Tamanho do Disco (GB)" são combos dependentes — as
        // opções mostradas mudam consoante o "Tipo de Memória"/"Tipo de Disco" escolhido. Ao
        // escolher um tipo diferente na combo (SelectionChanged, disparado ao selecionar da lista),
        // as opções da combo dependente são recarregadas e o valor anterior é limpo, para nunca
        // ficar um GB incoerente com o novo tipo por lapso. Ao só sair do campo depois de escrever
        // texto livre (LostFocus), as opções são recarregadas mas o valor já escrito não é tocado.
        CmbTipoMemoria.SelectionChanged += (_, _) =>
        {
            AtualizarOpcoesDependentes(CmbTipoMemoria, CmbMemoriaGB, "Tipo de Memória");
            CmbMemoriaGB.Text = string.Empty;
            AtualizarObsolescencia();
        };
        CmbTipoMemoria.LostFocus += (_, _) => AtualizarOpcoesDependentes(CmbTipoMemoria, CmbMemoriaGB, "Tipo de Memória");
        CmbMemoriaGB.SelectionChanged += (_, _) => AtualizarObsolescencia();
        CmbMemoriaGB.LostFocus += (_, _) => AtualizarObsolescencia();

        CmbTipoDisco.SelectionChanged += (_, _) =>
        {
            AtualizarOpcoesDependentes(CmbTipoDisco, CmbTamanhoDisco, "Tipo de Disco");
            CmbTamanhoDisco.Text = string.Empty;
        };
        CmbTipoDisco.LostFocus += (_, _) => AtualizarOpcoesDependentes(CmbTipoDisco, CmbTamanhoDisco, "Tipo de Disco");

        if (equipamento == null)
        {
            TxtTitulo.Text = "Novo Equipamento";
            CmbEstado.SelectedItem = EstadosEquipamento.EmServico;
            _estadoOriginal = EstadosEquipamento.EmServico;
            if (escolaPreSelecionada != null)
                CmbEscola.SelectedItem = _todasAsEscolas.FirstOrDefault(x => x.Id == escolaPreSelecionada.Id);
            return;
        }

        // "Guardar e Novo" só faz sentido a inserir equipamento novo de seguida — a editar um já
        // existente, ficaria confuso (o botão continuaria a gravar-EDITAR, não a criar) - por isso
        // só aparece quando esta janela abre em modo "Novo Equipamento".
        BtnGuardarNovo.Visibility = Visibility.Collapsed;

        TxtTitulo.Text = "Editar Equipamento";
        TxtNumeroSerie.Text = equipamento.NumeroSerie;
        TxtNumeroInventario.Text = equipamento.NumeroInventario;
        CmbTipo.Text = equipamento.Tipo;
        TxtMarca.Text = equipamento.Marca;
        TxtModelo.Text = equipamento.Modelo;
        DpAquisicao.SelectedDate = equipamento.DataAquisicao;
        TxtValor.Text = equipamento.ValorAquisicao?.ToString();
        TxtFornecedor.Text = equipamento.Fornecedor;
        CmbEscola.SelectedItem = _todasAsEscolas.FirstOrDefault(x => x.Id == equipamento.EscolaId);
        TxtLocalNaoEscolar.Text = equipamento.LocalNaoEscolar;
        CmbEstado.SelectedItem = equipamento.Estado;
        TxtObservacoes.Text = equipamento.Observacoes;
        _estadoOriginal = equipamento.Estado;

        CmbProcessador.Text = equipamento.Processador;
        TxtFamiliaProcessador.Text = equipamento.FamiliaProcessador;

        // Definir o Text de uma ComboBox editável por código não dispara SelectionChanged/LostFocus
        // — por isso as opções da combo dependente são recarregadas aqui explicitamente, antes de
        // se atribuir o valor já gravado, para nunca correr o risco de o limpar por engano.
        CmbTipoMemoria.Text = equipamento.TipoMemoria;
        AtualizarOpcoesDependentes(CmbTipoMemoria, CmbMemoriaGB, "Tipo de Memória");
        CmbMemoriaGB.Text = equipamento.QuantidadeMemoriaGB?.ToString();

        CmbTipoDisco.Text = equipamento.TipoDisco;
        AtualizarOpcoesDependentes(CmbTipoDisco, CmbTamanhoDisco, "Tipo de Disco");
        CmbTamanhoDisco.Text = equipamento.TamanhoDiscoGB?.ToString();

        CmbSistemaOperativo.Text = equipamento.SistemaOperativo;

        CmbPolegadas.Text = equipamento.PolegadasMonitor?.ToString();
        CmbTipoPainel.Text = equipamento.TipoPainelMonitor;
        CmbResolucaoMonitor.Text = equipamento.ResolucaoMonitor;

        CmbTipoImpressora.Text = equipamento.TipoImpressora;
        ChkImpressaoCor.IsChecked = equipamento.ImpressaoCor;
        CmbLigacaoImpressora.Text = equipamento.LigacaoImpressora;

        CmbNumeroPortas.Text = equipamento.NumeroPortas?.ToString();
        CmbVelocidadeRede.Text = equipamento.VelocidadeRede;
        ChkGerivel.IsChecked = equipamento.Gerivel;

        CmbResolucaoCamera.Text = equipamento.ResolucaoCamera;
        CmbTipoCamera.Text = equipamento.TipoCamera;
        ChkVisaoNoturna.IsChecked = equipamento.VisaoNoturna;

        CmbLuminosidade.Text = equipamento.LuminosidadeLumens?.ToString();
        CmbResolucaoProjetor.Text = equipamento.ResolucaoProjetor;

        TxtEspecificacoesAdicionais.Text = equipamento.EspecificacoesAdicionais;

        AtualizarGruposVisiveis(equipamento.Tipo);
        CarregarHistoricoIntervencoes(equipamento.Id);

        // Guarda o estado do hardware tal como estava ao abrir, para se poder calcular um resumo
        // do que mudou ao gravar (só é usado quando há um _atividadeContexto — ver Guardar_Click).
        _valoresHardwareOriginais["Processador"] = equipamento.Processador;
        _valoresHardwareOriginais["TipoMemoria"] = equipamento.TipoMemoria;
        _valoresHardwareOriginais["QuantidadeMemoriaGB"] = equipamento.QuantidadeMemoriaGB?.ToString();
        _valoresHardwareOriginais["TipoDisco"] = equipamento.TipoDisco;
        _valoresHardwareOriginais["TamanhoDiscoGB"] = equipamento.TamanhoDiscoGB?.ToString();
        _valoresHardwareOriginais["SistemaOperativo"] = equipamento.SistemaOperativo;
    }

    /// <summary>3: calcula o "Nº de Vezes Intervencionado" e preenche a grelha de histórico deste
    /// equipamento, juntando as duas origens possíveis de intervenção: reparações feitas no local
    /// (<see cref="IntervencaoEquipamento"/>, ligadas a uma <see cref="Intervencao"/> normal) e
    /// recolhas para reparação na DISIA (<see cref="EquipamentoRecolhido"/>). Ambas contam para o
    /// total e aparecem juntas no histórico, ordenadas da mais recente para a mais antiga.</summary>
    private void CarregarHistoricoIntervencoes(int equipamentoId)
    {
        var noLocal = App.Db.IntervencaoEquipamentos
            .Include(ie => ie.Intervencao)
            .Where(ie => ie.EquipamentoId == equipamentoId && ie.Intervencao != null)
            .Select(ie => new HistoricoIntervencaoEquipamento
            {
                Data = ie.Intervencao!.Data,
                Descricao = ie.Intervencao!.Descricao,
                Estado = ie.Intervencao!.Estado.ToString(),
                IntervencaoId = ie.IntervencaoId,
            })
            .ToList();

        // (2.2) Inclui a Atividade DISIA (ou, em registos antigos, a Intervenção DISIA obsoleta)
        // associada à recolha, para que se saiba logo aqui tudo o que foi feito à máquina, sem
        // ser preciso ir consultar o módulo de Atividades DISIA à parte.
        var porRecolha = App.Db.EquipamentosRecolhidos
            .Include(r => r.AtividadeDisia)
            .Include(r => r.IntervencaoDisia)
            .Where(r => r.EquipamentoId == equipamentoId)
            .ToList()
            .Select(r => new HistoricoIntervencaoEquipamento
            {
                Data = r.DataRecolha,
                Descricao = DescricaoRecolha(r),
                Estado = r.Estado,
                AtividadeDisiaId = r.AtividadeDisia?.Id,
            })
            .ToList();

        var historico = noLocal.Concat(porRecolha).OrderByDescending(h => h.Data).ToList();

        TxtVezesIntervencionado.Text = historico.Count.ToString();
        GridHistoricoIntervencoes.ItemsSource = historico;
        // (2.1) O painel (título + datagrid) mantém-se sempre visível, mesmo sem histórico, para
        // não deixar um espaço em branco pouco apelativo — a datagrid fica simplesmente vazia.
    }

    /// <summary>Abre, com duplo clique numa linha, o registo completo por trás dessa linha do
    /// histórico — a Intervenção (equipamento intervencionado no local) ou a Atividade DISIA
    /// (recolha), consoante o que a linha representa (ver HistoricoIntervencaoEquipamento). Um
    /// registo de recolha antigo, ligado só à IntervencaoDisia obsoleta (sem Atividade DISIA
    /// moderna associada), não tem para onde abrir — mostra-se um aviso em vez de não fazer nada
    /// silenciosamente. Recarrega o histórico ao voltar, já que a janela aberta pode ter mudado
    /// algo nele (ex.: o estado da intervenção).</summary>
    private void GridHistoricoIntervencoes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridHistoricoIntervencoes.SelectedItem is not HistoricoIntervencaoEquipamento linha) return;

        if (linha.IntervencaoId is { } intervencaoId)
        {
            var intervencao = App.Db.Intervencoes.Find(intervencaoId);
            if (intervencao == null) return;
            new IntervencaoEditWindow(intervencao) { Owner = this }.ShowDialog();
        }
        else if (linha.AtividadeDisiaId is { } atividadeId)
        {
            var atividade = App.Db.AtividadesDisia.Find(atividadeId);
            if (atividade == null) return;
            new AtividadeDisiaEditWindow(atividade) { Owner = this }.ShowDialog();
        }
        else
        {
            MessageBox.Show("Este registo não tem uma janela de consulta disponível (histórico antigo).",
                "Sem detalhe disponível", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_existente != null) CarregarHistoricoIntervencoes(_existente.Id);
    }

    /// <summary>(2.2) Descrição completa a mostrar no histórico para uma recolha, indo buscar o que
    /// foi realmente feito à Atividade DISIA associada (descrição + observações, se existirem).</summary>
    private static string DescricaoRecolha(EquipamentoRecolhido r)
    {
        if (r.AtividadeDisia != null)
        {
            var texto = $"Recolhido para reparação — {r.AtividadeDisia.Descricao}";
            if (!string.IsNullOrWhiteSpace(r.AtividadeDisia.Observacoes))
                texto += $" ({r.AtividadeDisia.Observacoes})";
            return texto;
        }

        // Compatibilidade com registos antigos, criados antes de existir AtividadeDisiaId.
        if (r.IntervencaoDisia != null)
            return $"Recolhido para reparação — {r.IntervencaoDisia.Descricao}";

        return "Recolhido para reparação";
    }

    /// <summary>Linha simples para a grelha de histórico do equipamento (3: Data/Descrição/Estado).</summary>
    private class HistoricoIntervencaoEquipamento
    {
        public DateTime Data { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;

        /// <summary>Id da Intervenção (quando a linha vem de "equipamento intervencionado no
        /// local") ou da Atividade DISIA (quando vem de uma recolha) — usado para abrir o registo
        /// completo com duplo clique (ver GridHistoricoIntervencoes_MouseDoubleClick). Nunca os
        /// dois ao mesmo tempo; ambos nulos quando nenhum se aplica (ex.: registos de recolha
        /// antigos, só ligados a uma IntervencaoDisia obsoleta, já sem janela de edição própria).</summary>
        public int? IntervencaoId { get; set; }
        public int? AtividadeDisiaId { get; set; }
    }

    /// <summary>
    /// Lê os valores ativos de um grupo de "Dados Fixos" (Administração). Se o grupo ainda
    /// não tiver nenhum valor configurado, usa a lista por omissão indicada, para que a
    /// aplicação continue a funcionar normalmente mesmo antes de qualquer configuração manual.
    /// </summary>
    private static string[] ValoresAtivos(string grupo, string[]? valoresPorOmissao = null)
    {
        var valores = App.Db.ValoresFixos
            .Where(v => v.Grupo == grupo && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToArray();

        return valores.Length > 0 ? valores : (valoresPorOmissao ?? Array.Empty<string>());
    }

    /// <summary>
    /// (Dados Fixos v2) Lê as opções sugeridas de uma característica embutida de qualquer grupo
    /// (Computador: Processador, Tipo de Memória, Memória (GB), Tipo de Disco, Tamanho do Disco
    /// (GB), Sistema Operativo; Rede: Nº de Portas, Velocidade; Câmara: Tipo, Resolução; Monitor:
    /// Tipo de Painel, Polegadas, Resolução; Projetor: Luminosidade (Lumens), Resolução) — geridas
    /// em Administração → Dados Fixos → Tipos de Equipamento → (grupo), tal como qualquer
    /// característica criada pelo administrador. Se a característica ainda não existir (base de
    /// dados muito antiga, antes da migração automática ter corrido — ver
    /// DbInitializer.MigrarCaracteristicasFixasEmbutidas) ou não tiver nenhuma opção ativa, usa a
    /// lista por omissão indicada, para a aplicação continuar a funcionar normalmente.
    /// </summary>
    private static string[] ValoresCaracteristicaEmbutida(string grupoCaracteristicas, string nome, string[]? valoresPorOmissao = null)
    {
        var valores = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupoCaracteristicas && c.Nome == nome)
            .Join(App.Db.CaracteristicaEquipamentoOpcoes.Where(o => o.Ativo),
                c => c.Id, o => o.CaracteristicaEquipamentoId, (c, o) => o)
            .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
            .Select(o => o.Valor)
            .ToArray();

        return valores.Length > 0 ? valores : (valoresPorOmissao ?? Array.Empty<string>());
    }

    /// <summary>
    /// (Dados Fixos v2) Recarrega as opções de uma combo dependente (ex.: <c>CmbMemoriaGB</c>) com
    /// base no valor atualmente escrito na combo-pai (ex.: <c>CmbTipoMemoria</c>): procura, entre as
    /// opções da característica-pai indicada, a que tiver o mesmo valor e, se essa opção tiver uma
    /// característica-filha associada (ver <see cref="CaracteristicaEquipamentoOpcao.CaracteristicaFilhaId"/>),
    /// usa as opções ativas dessa característica-filha. Sem correspondência (ou sem
    /// característica-filha nessa opção), a combo dependente fica sem opções — continua a poder-se
    /// escrever um valor livre, tal como as restantes combos da aplicação.
    /// Só recarrega o ItemsSource — nunca limpa o valor já escrito na combo dependente (essa decisão
    /// fica ao critério de quem chama, consoante o motivo: escolha ativa do utilizador limpa-se
    /// logo a seguir; carregamento de um equipamento existente não deve limpar nada).
    /// </summary>
    private static void AtualizarOpcoesDependentes(ComboBox comboPai, ComboBox comboFilha, string nomeCaracteristicaPai)
    {
        var valorPai = comboPai.Text;

        // A combo-filha só faz sentido usar depois de a combo-pai ter um valor (ex.: só faz sentido
        // escolher o "Tamanho do Disco" depois de saber se é HDD/SSD/NVMe) — mantê-la desativada
        // até lá evita valores "soltos", sem o respetivo tipo escolhido, e deixa claro, só
        // visualmente, a ordem a seguir a preencher o formulário.
        comboFilha.IsEnabled = !string.IsNullOrWhiteSpace(valorPai);

        var idCaracteristicaFilha = string.IsNullOrWhiteSpace(valorPai)
            ? null
            : App.Db.CaracteristicasEquipamento
                .Where(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Computador && c.Nome == nomeCaracteristicaPai)
                .Join(App.Db.CaracteristicaEquipamentoOpcoes.Where(o => o.Ativo && o.Valor == valorPai),
                    c => c.Id, o => o.CaracteristicaEquipamentoId, (c, o) => o.CaracteristicaFilhaId)
                .FirstOrDefault();

        comboFilha.ItemsSource = idCaracteristicaFilha == null
            ? Array.Empty<string>()
            : App.Db.CaracteristicaEquipamentoOpcoes
                .Where(o => o.CaracteristicaEquipamentoId == idCaracteristicaFilha && o.Ativo)
                .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
                .Select(o => o.Valor)
                .ToArray();
    }

    /// <summary>
    /// Gera um número de série padrão (ex: "SN-4K7X9QRT"), para usar quando o equipamento não
    /// tem um número de série visível ou legível. Garante que não colide com nenhum já existente.
    /// </summary>
    private void GerarNumeroSerie_Click(object sender, RoutedEventArgs e)
    {
        const string caracteres = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem O/0/I/1, para evitar confusões
        var aleatorio = new Random();
        string candidato;
        var tentativas = 0;

        do
        {
            var sufixo = new string(Enumerable.Range(0, 8).Select(_ => caracteres[aleatorio.Next(caracteres.Length)]).ToArray());
            candidato = $"SN-{sufixo}";
            tentativas++;
        }
        while (App.Db.Equipamentos.Any(x => x.NumeroSerie == candidato) && tentativas < 20);

        TxtNumeroSerie.Text = candidato;
        TxtNumeroSerie.Focus();
        TxtNumeroSerie.CaretIndex = TxtNumeroSerie.Text.Length;
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

    /// <summary>Reconstrói um Equipamento temporário com os valores atuais dos campos e
    /// atualiza o painel de obsolescência em tempo real.</summary>
    private void AtualizarObsolescencia()
    {
        var eq = new Equipamento
        {
            Tipo = CmbTipo.Text,
            DataAquisicao = DpAquisicao.SelectedDate,
            Processador = string.IsNullOrWhiteSpace(CmbProcessador.Text) ? null : CmbProcessador.Text,
            FamiliaProcessador = string.IsNullOrWhiteSpace(TxtFamiliaProcessador.Text) ? null : TxtFamiliaProcessador.Text,
            TipoMemoria = string.IsNullOrWhiteSpace(CmbTipoMemoria.Text) ? null : CmbTipoMemoria.Text,
            QuantidadeMemoriaGB = int.TryParse(CmbMemoriaGB.Text, out var ram) ? ram : null,
            TipoDisco = string.IsNullOrWhiteSpace(CmbTipoDisco.Text) ? null : CmbTipoDisco.Text,
        };

        var resultado = ObsolescenciaService.Calcular(eq);
        var cor = (Color)ColorConverter.ConvertFromString(resultado.CorHex)!;
        var brush = new SolidColorBrush(cor);

        PbObsolescencia.Value = resultado.Score ?? 0;
        PbObsolescencia.Foreground = brush;
        BadgeObsolescencia.Background = brush;
        TxtObsolescenciaClassif.Text = resultado.Classificacao;
        TxtObsolescenciaScore.Text = resultado.Score.HasValue
            ? $"Score: {resultado.Score}%"
            : string.Empty;
        TxtObsolescenciaDetalhe.Text = resultado.Detalhe;
    }

    private void CmbTipo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Numa ComboBox editável (IsEditable="True"), a propriedade Text ainda pode não estar
        // atualizada no preciso momento em que este evento dispara - por isso usa-se diretamente
        // o item selecionado (e.AddedItems), que já reflete a escolha feita, em vez de CmbTipo.Text.
        // É isto que faz as características específicas aparecerem imediatamente ao selecionar o
        // tipo, em vez de só ao mudar o foco para o campo seguinte.
        var tipoSelecionado = e.AddedItems.Count > 0 ? e.AddedItems[0]?.ToString() : CmbTipo.Text;
        AtualizarGruposVisiveis(tipoSelecionado);
        AtualizarObsolescencia();
    }

    private void CmbTipo_LostFocus(object sender, RoutedEventArgs e)
    {
        AtualizarGruposVisiveis(CmbTipo.Text);
        AtualizarObsolescencia();
    }

    /// <summary>Abre o seletor de "modelos base" (ver Models/ModeloEquipamento.cs e
    /// Views/SelecionarModeloWindow.xaml.cs), já filtrado ao Tipo escolhido acima — o próprio
    /// botão só fica ativo depois de haver um Tipo escolhido (ver AtualizarGruposVisiveis), pela
    /// mesma razão das combos dependentes de "Tipo de Memória"/"Tipo de Disco". Escolhido um
    /// modelo, copia Marca/Modelo e as suas características para o formulário — os campos
    /// próprios desta unidade em concreto (Nº de Série, Nº de Inventário, Escola, Aquisição,
    /// Estado, Observações) não são tocados, continuam a preencher-se à mão como até aqui.</summary>
    private void UsarModelo_Click(object sender, RoutedEventArgs e)
    {
        var tipo = CmbTipo.Text;
        if (string.IsNullOrWhiteSpace(tipo)) return;

        var todosOsModelos = App.Db.ModelosEquipamento.ToList();
        var modelo = SelecionarModeloWindow.Perguntar(this, tipo, todosOsModelos);
        if (modelo == null) return;

        TxtMarca.Text = modelo.Marca;
        TxtModelo.Text = modelo.Modelo;

        CmbProcessador.Text = modelo.Processador;
        TxtFamiliaProcessador.Text = modelo.FamiliaProcessador;

        // Mesma ordem (pai → AtualizarOpcoesDependentes → filho) já usada ao carregar um
        // equipamento existente (ver mais abaixo) — definir o Text de uma combo dependente antes
        // de recarregar as suas opções arrisca perdê-lo, já que AtualizarOpcoesDependentes também
        // decide se a combo fica ativa ou não a partir do valor atual da combo-pai.
        CmbTipoMemoria.Text = modelo.TipoMemoria;
        AtualizarOpcoesDependentes(CmbTipoMemoria, CmbMemoriaGB, "Tipo de Memória");
        CmbMemoriaGB.Text = modelo.QuantidadeMemoriaGB?.ToString();

        CmbTipoDisco.Text = modelo.TipoDisco;
        AtualizarOpcoesDependentes(CmbTipoDisco, CmbTamanhoDisco, "Tipo de Disco");
        CmbTamanhoDisco.Text = modelo.TamanhoDiscoGB?.ToString();

        CmbSistemaOperativo.Text = modelo.SistemaOperativo;

        CmbPolegadas.Text = modelo.PolegadasMonitor?.ToString();
        CmbTipoPainel.Text = modelo.TipoPainelMonitor;
        CmbResolucaoMonitor.Text = modelo.ResolucaoMonitor;

        CmbTipoImpressora.Text = modelo.TipoImpressora;
        ChkImpressaoCor.IsChecked = modelo.ImpressaoCor;
        CmbLigacaoImpressora.Text = modelo.LigacaoImpressora;

        CmbNumeroPortas.Text = modelo.NumeroPortas?.ToString();
        CmbVelocidadeRede.Text = modelo.VelocidadeRede;
        ChkGerivel.IsChecked = modelo.Gerivel;

        CmbResolucaoCamera.Text = modelo.ResolucaoCamera;
        CmbTipoCamera.Text = modelo.TipoCamera;
        ChkVisaoNoturna.IsChecked = modelo.VisaoNoturna;

        CmbLuminosidade.Text = modelo.LuminosidadeLumens?.ToString();
        CmbResolucaoProjetor.Text = modelo.ResolucaoProjetor;

        TxtEspecificacoesAdicionais.Text = modelo.EspecificacoesAdicionais;

        // Características adicionais definidas pelo administrador (ver
        // AtualizarCaracteristicasAdicionais/_camposCaracteristicasAdicionais mais abaixo) — os
        // campos dinâmicos já têm de estar construídos a esta altura para o Tipo atual, já que o
        // próprio botão "Usar Modelo..." só fica ativo depois de haver um Tipo escolhido (o que já
        // desencadeia essa construção). Só é preciso encontrar o campo de cada característica pelo
        // mesmo Id e preencher o valor gravado no modelo.
        var valoresAdicionais = App.Db.ModeloEquipamentoCaracteristicaValores
            .Where(v => v.ModeloEquipamentoId == modelo.Id)
            .ToDictionary(v => v.CaracteristicaEquipamentoId, v => v.Valor);

        foreach (var (caracteristicaId, valor) in valoresAdicionais)
        {
            if (!_camposCaracteristicasAdicionais.TryGetValue(caracteristicaId, out var campo)) continue;
            switch (campo)
            {
                case TextBox caixa: caixa.Text = valor; break;
                case ComboBox combo: combo.Text = valor; break;
            }
        }

        AtualizarObsolescencia();
    }

    /// <summary>Operação inversa de UsarModelo_Click: em vez de trazer dados de um modelo já
    /// existente para este formulário, guarda os dados JÁ ESCRITOS aqui (Tipo/Marca/Modelo/
    /// características) como um modelo novo reutilizável — ver
    /// ModelosEquipamentoWindow.PreencherParaNovoModelo. Útil ao inserir um equipamento cujo modelo
    /// ainda não existe no catálogo, para não ter de o repetir mais tarde a partir do zero em
    /// Administração de Equipamento Base. Os campos próprios de cada unidade (Nº de Série, Nº de
    /// Inventário, Escola, Aquisição, Estado, Observações) nunca fazem parte de um modelo, por isso
    /// não são copiados — só as características que fazem sentido serem iguais entre várias
    /// unidades. Exige pelo menos Tipo + (Marca ou Modelo) preenchidos — sem isto, não há dados
    /// suficientes para um modelo minimamente identificável, e mostra-se um aviso em vez de abrir a
    /// janela.</summary>
    private void AdicionarComoModelo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CmbTipo.Text) ||
            (string.IsNullOrWhiteSpace(TxtMarca.Text) && string.IsNullOrWhiteSpace(TxtModelo.Text)))
        {
            MessageBox.Show(
                "Indique pelo menos o Tipo de Equipamento e a Marca ou o Modelo antes de guardar como modelo reutilizável.",
                "Dados insuficientes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dados = new ModeloEquipamento
        {
            Tipo = CmbTipo.Text,
            Marca = string.IsNullOrWhiteSpace(TxtMarca.Text) ? null : TxtMarca.Text,
            Modelo = string.IsNullOrWhiteSpace(TxtModelo.Text) ? null : TxtModelo.Text,

            Processador = string.IsNullOrWhiteSpace(CmbProcessador.Text) ? null : CmbProcessador.Text,
            FamiliaProcessador = string.IsNullOrWhiteSpace(TxtFamiliaProcessador.Text) ? null : TxtFamiliaProcessador.Text,
            TipoMemoria = string.IsNullOrWhiteSpace(CmbTipoMemoria.Text) ? null : CmbTipoMemoria.Text,
            QuantidadeMemoriaGB = ParseInt(CmbMemoriaGB.Text),
            TipoDisco = string.IsNullOrWhiteSpace(CmbTipoDisco.Text) ? null : CmbTipoDisco.Text,
            TamanhoDiscoGB = ParseInt(CmbTamanhoDisco.Text),
            SistemaOperativo = string.IsNullOrWhiteSpace(CmbSistemaOperativo.Text) ? null : CmbSistemaOperativo.Text,

            PolegadasMonitor = ParseDouble(CmbPolegadas.Text),
            TipoPainelMonitor = string.IsNullOrWhiteSpace(CmbTipoPainel.Text) ? null : CmbTipoPainel.Text,
            ResolucaoMonitor = string.IsNullOrWhiteSpace(CmbResolucaoMonitor.Text) ? null : CmbResolucaoMonitor.Text,

            TipoImpressora = string.IsNullOrWhiteSpace(CmbTipoImpressora.Text) ? null : CmbTipoImpressora.Text,
            ImpressaoCor = ChkImpressaoCor.IsChecked,
            LigacaoImpressora = string.IsNullOrWhiteSpace(CmbLigacaoImpressora.Text) ? null : CmbLigacaoImpressora.Text,

            NumeroPortas = ParseInt(CmbNumeroPortas.Text),
            VelocidadeRede = string.IsNullOrWhiteSpace(CmbVelocidadeRede.Text) ? null : CmbVelocidadeRede.Text,
            Gerivel = ChkGerivel.IsChecked,

            ResolucaoCamera = string.IsNullOrWhiteSpace(CmbResolucaoCamera.Text) ? null : CmbResolucaoCamera.Text,
            TipoCamera = string.IsNullOrWhiteSpace(CmbTipoCamera.Text) ? null : CmbTipoCamera.Text,
            VisaoNoturna = ChkVisaoNoturna.IsChecked,

            LuminosidadeLumens = ParseInt(CmbLuminosidade.Text),
            ResolucaoProjetor = string.IsNullOrWhiteSpace(CmbResolucaoProjetor.Text) ? null : CmbResolucaoProjetor.Text,

            EspecificacoesAdicionais = string.IsNullOrWhiteSpace(TxtEspecificacoesAdicionais.Text) ? null : TxtEspecificacoesAdicionais.Text
        };

        var caracteristicasAdicionais = _camposCaracteristicasAdicionais
            .Select(kv => (Id: kv.Key, Valor: ObterTextoCampoCaracteristica(kv.Value)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Valor))
            .ToDictionary(x => x.Id, x => x.Valor);

        var janela = new ModelosEquipamentoWindow { Owner = this };
        janela.PreencherParaNovoModelo(dados, caracteristicasAdicionais);
        janela.ShowDialog();
    }

    private void AtualizarGruposVisiveis(string? tipo)
    {
        BtnUsarModelo.IsEnabled = !string.IsNullOrWhiteSpace(tipo);

        GrupoComputador.Visibility = Oculto(true);
        GrupoMonitor.Visibility = Oculto(true);
        GrupoImpressora.Visibility = Oculto(true);
        GrupoRede.Visibility = Oculto(true);
        GrupoCamera.Visibility = Oculto(true);
        GrupoProjetor.Visibility = Oculto(true);
        GrupoGenerico.Visibility = Oculto(true);

        if (string.IsNullOrWhiteSpace(tipo))
        {
            AtualizarCaracteristicasAdicionais(GruposCaracteristicasEquipamento.Generico, tipo);
            return;
        }

        var grupo = ObterGrupoCaracteristicas(tipo);
        switch (grupo)
        {
            case GruposCaracteristicasEquipamento.Computador: GrupoComputador.Visibility = Visibility.Visible; break;
            case GruposCaracteristicasEquipamento.Monitor: GrupoMonitor.Visibility = Visibility.Visible; break;
            case GruposCaracteristicasEquipamento.Impressora: GrupoImpressora.Visibility = Visibility.Visible; break;
            case GruposCaracteristicasEquipamento.Rede: GrupoRede.Visibility = Visibility.Visible; break;
            case GruposCaracteristicasEquipamento.Camera: GrupoCamera.Visibility = Visibility.Visible; break;
            case GruposCaracteristicasEquipamento.Projetor: GrupoProjetor.Visibility = Visibility.Visible; break;
            default: GrupoGenerico.Visibility = Visibility.Visible; break;
        }

        AtualizarCaracteristicasAdicionais(grupo, tipo);
    }

    /// <summary>(1.3) Gera dinamicamente, em <see cref="PainelCaracteristicasAdicionais"/>, um
    /// rótulo + campo para cada característica ativa definida pelo administrador (em Dados Fixos)
    /// para o grupo de características indicado. Pré-preenche com o valor já gravado (ao editar um
    /// equipamento existente) ou, em equipamento novo, com o valor por omissão da característica,
    /// se existir.
    ///
    /// (1.4) Quando a característica tiver, ela própria, uma lista de valores sugeridos ativos
    /// (definida em "Gerir Valores desta Característica..."), o campo é uma caixa de seleção
    /// editável (<see cref="ComboBox"/>) com esses valores como sugestões — tal como as restantes
    /// combos da aplicação, continua a ser possível escrever um valor livre não incluído na lista.
    /// Sem nenhum valor sugerido ativo, mantém-se uma simples caixa de texto livre.</summary>
    /// <summary>(Dados Fixos v2) Nomes das características embutidas de cada grupo (ver
    /// DbInitializer.MigrarCaracteristicasFixasEmbutidas) — já têm campo próprio no respetivo
    /// painel fixo ("Características do Computador/Rede/Câmara/Monitor/Projetor"), pelo que têm
    /// de ser excluídas daqui, ou apareceriam duplicadas no painel dinâmico "Características
    /// Adicionais".</summary>
    private static readonly Dictionary<string, HashSet<string>> NomesCaracteristicasEmbutidasPorGrupo = new()
    {
        [GruposCaracteristicasEquipamento.Computador] = new() { "Processador", "Tipo de Memória", "Memória (GB)", "Tipo de Disco", "Tamanho do Disco (GB)", "Sistema Operativo" },
        [GruposCaracteristicasEquipamento.Rede] = new() { "Nº de Portas", "Velocidade" },
        [GruposCaracteristicasEquipamento.Camera] = new() { "Tipo", "Resolução" },
        [GruposCaracteristicasEquipamento.Monitor] = new() { "Tipo de Painel", "Polegadas", "Resolução" },
        [GruposCaracteristicasEquipamento.Projetor] = new() { "Luminosidade (Lumens)", "Resolução" },
        // Faltava esta entrada — sem ela, NENHUMA característica do grupo Impressora era excluída
        // do painel dinâmico "Características Adicionais" (o TryGetValue abaixo falhava, e a
        // condição de filtro trata "sem lista nenhuma" como "não excluir nada"), pelo que "Tipo de
        // Impressora", "Tipo de Ligação" e "Impressão a cores" — já configuradas como
        // características do grupo em Dados Fixos, e cada uma já com o seu próprio campo fixo
        // (CmbTipoImpressora, CmbLigacaoImpressora, ChkImpressaoCor) — apareciam TAMBÉM no painel
        // dinâmico, duplicadas. "Cartuchos" não tem campo fixo equivalente, por isso não entra
        // aqui, e continua (corretamente) só no painel dinâmico.
        [GruposCaracteristicasEquipamento.Impressora] = new() { "Tipo de Impressora", "Tipo de Ligação", "Impressão a cores" }
    };

    private void AtualizarCaracteristicasAdicionais(string grupoCaracteristicas, string? tipo)
    {
        PainelCaracteristicasAdicionais.Children.Clear();
        _camposCaracteristicasAdicionais.Clear();

        // (Dados Fixos v2) Id do Tipo de Equipamento atualmente selecionado (para o filtro de
        // "Aplica-se apenas a" abaixo) — null se o texto não corresponder a nenhum Tipo configurado
        // em Dados Fixos (ex.: ainda a ser escrito manualmente).
        var idTipoAtual = string.IsNullOrWhiteSpace(tipo)
            ? (int?)null
            : App.Db.ValoresFixos
                .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento && v.Valor == tipo)
                .Select(v => (int?)v.Id)
                .FirstOrDefault();

        var nomesEmbutidosDesteGrupo = NomesCaracteristicasEmbutidasPorGrupo.TryGetValue(grupoCaracteristicas, out var nomes)
            ? nomes
            : null;

        var caracteristicas = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupoCaracteristicas && c.Ativo
                        // já têm campo fixo próprio neste grupo
                        && (nomesEmbutidosDesteGrupo == null || !nomesEmbutidosDesteGrupo.Contains(c.Nome))
                        // características-filha só aparecem através da característica-pai (ainda
                        // não há combo dependente no painel dinâmico para subtipos criados livremente
                        // pelo administrador — ficam reservadas às características embutidas)
                        && c.CaracteristicaPaiId == null
                        // "Aplica-se apenas a": partilhada (null) ou exclusiva do Tipo selecionado
                        && (c.TipoEquipamentoId == null || c.TipoEquipamentoId == idTipoAtual))
            .OrderBy(c => c.Ordem)
            .ThenBy(c => c.Nome)
            .ToList();

        if (caracteristicas.Count == 0)
        {
            GrupoCaracteristicasAdicionais.Visibility = Visibility.Collapsed;
            return;
        }

        var valoresExistentes = _existente == null
            ? new Dictionary<int, string?>()
            : App.Db.EquipamentoCaracteristicaValores
                .Where(v => v.EquipamentoId == _existente.Id)
                .ToDictionary(v => v.CaracteristicaEquipamentoId, v => v.Valor);

        // (1.4) Opções sugeridas de todas as características deste grupo, já agrupadas por
        // característica — evita uma consulta à base de dados por cada característica no ciclo.
        var idsCaracteristicas = caracteristicas.Select(c => c.Id).ToList();
        var opcoesPorCaracteristica = App.Db.CaracteristicaEquipamentoOpcoes
            .Where(o => idsCaracteristicas.Contains(o.CaracteristicaEquipamentoId) && o.Ativo)
            .OrderBy(o => o.Ordem)
            .ThenBy(o => o.Valor)
            .ToList()
            .GroupBy(o => o.CaracteristicaEquipamentoId)
            .ToDictionary(g => g.Key, g => g.Select(o => o.Valor).ToList());

        foreach (var caracteristica in caracteristicas)
        {
            var rotulo = new TextBlock
            {
                Text = caracteristica.Nome,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2)
            };

            valoresExistentes.TryGetValue(caracteristica.Id, out var valorGravado);
            var valorInicial = valorGravado
                ?? (_existente == null ? caracteristica.ValorPorOmissao : null);

            Control campo;
            if (opcoesPorCaracteristica.TryGetValue(caracteristica.Id, out var opcoes) && opcoes.Count > 0)
            {
                campo = new ComboBox { IsEditable = true, Margin = new Thickness(0, 4, 0, 0), ItemsSource = opcoes, Text = valorInicial ?? string.Empty };
            }
            else
            {
                campo = new TextBox { Margin = new Thickness(0, 4, 0, 0), Text = valorInicial ?? string.Empty };
            }

            PainelCaracteristicasAdicionais.Children.Add(rotulo);
            PainelCaracteristicasAdicionais.Children.Add(campo);
            _camposCaracteristicasAdicionais[caracteristica.Id] = campo;
        }

        GrupoCaracteristicasAdicionais.Visibility = Visibility.Visible;
    }

    /// <summary>(1.4) Lê o texto de um campo dinâmico de característica adicional, seja ele uma
    /// caixa de texto livre ou uma caixa de seleção editável (ver
    /// <see cref="AtualizarCaracteristicasAdicionais"/>).</summary>
    private static string? ObterTextoCampoCaracteristica(Control campo) => campo switch
    {
        TextBox caixa => caixa.Text,
        ComboBox combo => combo.Text,
        _ => null
    };

    /// <summary>(1.3) Grava (cria/atualiza/remove) os valores preenchidos nas Características
    /// Adicionais geradas por <see cref="AtualizarCaracteristicasAdicionais"/>. Usa a propriedade
    /// de navegação <c>Equipamento</c> em vez do Id diretamente, para que também funcione com um
    /// equipamento novo ainda sem Id atribuído — o EF Core resolve a chave estrangeira sozinho
    /// no mesmo <c>SaveChanges()</c> que grava o equipamento.</summary>
    private void GravarCaracteristicasAdicionais(Equipamento equipamento)
    {
        foreach (var (caracteristicaId, campo) in _camposCaracteristicasAdicionais)
        {
            var texto = ObterTextoCampoCaracteristica(campo);
            var valor = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

            var existente = _existente == null
                ? null
                : App.Db.EquipamentoCaracteristicaValores.FirstOrDefault(v =>
                    v.EquipamentoId == _existente.Id && v.CaracteristicaEquipamentoId == caracteristicaId);

            if (existente != null)
            {
                if (valor == null)
                    App.Db.EquipamentoCaracteristicaValores.Remove(existente);
                else
                    existente.Valor = valor;
            }
            else if (valor != null)
            {
                App.Db.EquipamentoCaracteristicaValores.Add(new EquipamentoCaracteristicaValor
                {
                    Equipamento = equipamento,
                    CaracteristicaEquipamentoId = caracteristicaId,
                    Valor = valor
                });
            }
        }
    }

    /// <summary>
    /// (1.1 - correção de bug) Determina a que grupo de características específicas pertence um
    /// Tipo de Equipamento. Antes, isto era decidido comparando o NOME do tipo diretamente com
    /// listas fixas no código (<see cref="TiposComputador"/>, etc.) — o que fazia desaparecer as
    /// características específicas sempre que o nome do tipo era alterado em Administração →
    /// Dados Fixos, já que deixava de coincidir com o texto hardcoded.
    ///
    /// Agora consulta-se primeiro o grupo gravado no próprio registo de Dados Fixos
    /// (<see cref="ValorFixo.GrupoCaracteristicas"/>), que fica ligado ao registo (Id) e não ao
    /// nome — por isso sobrevive a uma alteração do nome apresentado. Só quando não existe (ainda)
    /// nenhum registo correspondente em Dados Fixos (ex.: tipos por omissão, antes de o
    /// administrador configurar algo) é que se recorre às listas fixas como reserva.
    /// </summary>
    private static string ObterGrupoCaracteristicas(string tipo)
    {
        var grupoGravado = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento && v.Valor == tipo)
            .Select(v => v.GrupoCaracteristicas)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(grupoGravado)) return grupoGravado;

        if (TiposComputador.Contains(tipo)) return GruposCaracteristicasEquipamento.Computador;
        if (TiposMonitor.Contains(tipo)) return GruposCaracteristicasEquipamento.Monitor;
        if (TiposImpressora.Contains(tipo)) return GruposCaracteristicasEquipamento.Impressora;
        if (TiposRede.Contains(tipo)) return GruposCaracteristicasEquipamento.Rede;
        if (TiposCamera.Contains(tipo)) return GruposCaracteristicasEquipamento.Camera;
        if (TiposProjetor.Contains(tipo)) return GruposCaracteristicasEquipamento.Projetor;
        return GruposCaracteristicasEquipamento.Generico;
    }

    private static Visibility Oculto(bool colapsar) => colapsar ? Visibility.Collapsed : Visibility.Visible;

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private static int? ParseInt(string texto) => int.TryParse(texto, out var v) ? v : null;
    private static double? ParseDouble(string texto) => double.TryParse(texto, out var v) ? v : null;

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (Guardar()) Close();
    }

    /// <summary>Guarda o equipamento e devolve true se a gravação foi bem-sucedida (false num
    /// qualquer dos avisos de validação, ou se a gravação em si falhar) — extraído de Guardar_Click
    /// para ser partilhado com GuardarNovo_Click ("Guardar e Novo" — ver PrepararParaNovoEquipamento),
    /// que faz tudo o que este método faz mas sem fechar a janela a seguir.</summary>
    private bool Guardar()
    {
        if (string.IsNullOrWhiteSpace(TxtNumeroSerie.Text))
        {
            MessageBox.Show("O Número de Série é obrigatório. Se não conseguir encontrá-lo no equipamento, " +
                "use o botão \"🎲 Gerar\" para criar um número de série padrão.",
                "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(CmbTipo.Text))
        {
            MessageBox.Show("Selecione ou indique o Tipo de Equipamento.",
                "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var numeroInventario = TxtNumeroInventario.Text.Trim();
        var duplicado = App.Db.Equipamentos.Any(x =>
            (_existente == null || x.Id != _existente.Id) &&
            (x.NumeroSerie == TxtNumeroSerie.Text.Trim() ||
             (numeroInventario != "" && x.NumeroInventario == numeroInventario)));
        if (duplicado)
        {
            MessageBox.Show("Já existe um equipamento com o mesmo Número de Série ou de Inventário.",
                "Duplicado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // (2.1) Sem escola selecionada, o equipamento fica sem localização associada — o que pode
        // ser intencional (ex.: equipamento em armazém, ou num local não escolar, indicado à parte
        // em "Local (não escolar)") mas também pode ser um esquecimento fácil de cometer numa lista
        // longa de escolas. Em vez de gravar silenciosamente ou bloquear a gravação, avisa-se e
        // deixa-se o utilizador decidir se quer mesmo avançar sem escola.
        var escolaSelecionada = CmbEscola.SelectedItem as Escola;
        if (escolaSelecionada == null)
        {
            var confirmarSemEscola = MessageBox.Show(
                "Não selecionou nenhuma Escola para este equipamento. Pretende continuar a gravar mesmo assim?",
                "Sem escola selecionada", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmarSemEscola != MessageBoxResult.Yes)
                return false;
        }

        Equipamento equipamento;
        if (_existente == null)
        {
            equipamento = new Equipamento();
            App.Db.Equipamentos.Add(equipamento);
        }
        else
        {
            equipamento = App.Db.Equipamentos.First(x => x.Id == _existente.Id);
        }

        equipamento.NumeroSerie = TxtNumeroSerie.Text.Trim();
        equipamento.NumeroInventario = TxtNumeroInventario.Text.Trim();
        equipamento.Tipo = CmbTipo.Text;
        equipamento.Marca = TxtMarca.Text;
        equipamento.Modelo = TxtModelo.Text;
        equipamento.DataAquisicao = DpAquisicao.SelectedDate;
        equipamento.ValorAquisicao = decimal.TryParse(TxtValor.Text, out var v) ? v : null;
        equipamento.Fornecedor = TxtFornecedor.Text;
        equipamento.EscolaId = escolaSelecionada?.Id;
        equipamento.LocalNaoEscolar = TxtLocalNaoEscolar.Text;
        equipamento.Estado = CmbEstado.SelectedItem as string ?? EstadosEquipamento.EmServico;
        equipamento.Observacoes = TxtObservacoes.Text;

        equipamento.Processador = string.IsNullOrWhiteSpace(CmbProcessador.Text) ? null : CmbProcessador.Text;
        equipamento.FamiliaProcessador = string.IsNullOrWhiteSpace(TxtFamiliaProcessador.Text) ? null : TxtFamiliaProcessador.Text;
        equipamento.TipoMemoria = string.IsNullOrWhiteSpace(CmbTipoMemoria.Text) ? null : CmbTipoMemoria.Text;
        equipamento.QuantidadeMemoriaGB = ParseInt(CmbMemoriaGB.Text);
        equipamento.TipoDisco = string.IsNullOrWhiteSpace(CmbTipoDisco.Text) ? null : CmbTipoDisco.Text;
        equipamento.TamanhoDiscoGB = ParseInt(CmbTamanhoDisco.Text);
        equipamento.SistemaOperativo = string.IsNullOrWhiteSpace(CmbSistemaOperativo.Text) ? null : CmbSistemaOperativo.Text;

        equipamento.PolegadasMonitor = ParseDouble(CmbPolegadas.Text);
        equipamento.TipoPainelMonitor = string.IsNullOrWhiteSpace(CmbTipoPainel.Text) ? null : CmbTipoPainel.Text;
        equipamento.ResolucaoMonitor = string.IsNullOrWhiteSpace(CmbResolucaoMonitor.Text) ? null : CmbResolucaoMonitor.Text;

        equipamento.TipoImpressora = string.IsNullOrWhiteSpace(CmbTipoImpressora.Text) ? null : CmbTipoImpressora.Text;
        equipamento.ImpressaoCor = ChkImpressaoCor.IsChecked;
        equipamento.LigacaoImpressora = string.IsNullOrWhiteSpace(CmbLigacaoImpressora.Text) ? null : CmbLigacaoImpressora.Text;

        equipamento.NumeroPortas = ParseInt(CmbNumeroPortas.Text);
        equipamento.VelocidadeRede = string.IsNullOrWhiteSpace(CmbVelocidadeRede.Text) ? null : CmbVelocidadeRede.Text;
        equipamento.Gerivel = ChkGerivel.IsChecked;

        equipamento.ResolucaoCamera = string.IsNullOrWhiteSpace(CmbResolucaoCamera.Text) ? null : CmbResolucaoCamera.Text;
        equipamento.TipoCamera = string.IsNullOrWhiteSpace(CmbTipoCamera.Text) ? null : CmbTipoCamera.Text;
        equipamento.VisaoNoturna = ChkVisaoNoturna.IsChecked;

        equipamento.LuminosidadeLumens = ParseInt(CmbLuminosidade.Text);
        equipamento.ResolucaoProjetor = string.IsNullOrWhiteSpace(CmbResolucaoProjetor.Text) ? null : CmbResolucaoProjetor.Text;

        equipamento.EspecificacoesAdicionais = string.IsNullOrWhiteSpace(TxtEspecificacoesAdicionais.Text)
            ? null : TxtEspecificacoesAdicionais.Text;

        // (Editar equipamento a partir de uma Atividade DISIA) Antes de gravar, se esta janela foi
        // aberta a partir de uma Atividade DISIA, calcula-se o que mudou no hardware (processador,
        // memória, disco, sistema operativo) para se poder devolver esse resumo à janela chamadora,
        // que o insere nas Observações da atividade — assim fica tudo registado no mesmo sítio.
        if (_atividadeContexto != null)
            ResumoAlteracoes = ConstruirResumoAlteracoesHardware(equipamento);

        GravarCaracteristicasAdicionais(equipamento);

        // (3) Confirmação de Atividade DISIA de acompanhamento: ao gravar um equipamento com o
        // estado final "Recolhido" ou "Aguarda Entrega" (seja um equipamento novo, seja uma edição
        // em que o estado passou a ser um destes dois), pergunta-se se se quer criar uma Atividade
        // DISIA associada — ver RecolhaEquipamentoService.CriarAtividadeAcompanhamento, que
        // reutiliza o mesmo mecanismo (Atividade DISIA + EquipamentoRecolhido) já usado quando a
        // recolha é feita a partir de uma Intervenção (RegistarRecolha). A gravação do equipamento
        // e a criação da atividade são propositadamente independentes uma da outra: o equipamento
        // fica sempre gravado, quer a atividade seja criada, recusada, ou falhe a criar.
        var estadoFinal = equipamento.Estado;

        try
        {
            App.Db.SaveChanges();
        }
        catch (Exception ex)
        {
            // EF Core's DbUpdateException.Message é sempre o mesmo texto genérico ("An error
            // occurred while saving the entity changes. See the inner exception for details.") —
            // a causa real (ex.: violação de uma restrição UNIQUE/NOT NULL na base de dados) vem
            // sempre na InnerException, por vezes vários níveis abaixo. Percorre a cadeia toda até
            // à causa raiz, para o utilizador (e quem for corrigir o problema a seguir) ver
            // logo o motivo real, em vez de só "See the inner exception for details" sem essa
            // informação em lado nenhum.
            var causaRaiz = ex;
            while (causaRaiz.InnerException != null) causaRaiz = causaRaiz.InnerException;

            MessageBox.Show($"Não foi possível gravar o equipamento:\n{causaRaiz.Message}",
                "Erro ao gravar", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        Sucesso = true;
        EquipamentoGravado = equipamento;

        // TemRecolhaPendente evita voltar a perguntar ou duplicar a atividade quando já existe uma
        // recolha em curso para este equipamento — cobre tanto o caso de regravar sem alterar o
        // estado (já haveria uma recolha pendente da vez anterior) como o de editar um equipamento
        // já recolhido por outra via (ex.: através de uma Intervenção). Avaliado uma única vez aqui
        // (antes de qualquer registo de recolha automático, já a seguir, poder alterar o
        // resultado).
        var jaTinhaRecolhaPendente = RecolhaEquipamentoService.TemRecolhaPendente(equipamento.Id);

        // (3.0) "Aguarda Entrega" significa, por definição, equipamento pronto a entregar a uma
        // escola — normalmente equipamento novo que nunca chegou a estar fisicamente lá (ex.:
        // comprado e já preparado para a primeira entrega, sem nenhuma reparação associada). Para
        // aparecer em "Equipamento Recolhido" ao criar a Intervenção normal de entrega, e poder ser
        // entregue pelo botão "Devolver à Escola", tem sempre de existir um registo
        // EquipamentoRecolhido — por isso este é criado já aqui, incondicionalmente, e não só se o
        // utilizador aceitar a Atividade DISIA a seguir (que pode legitimamente recusar, já que não
        // houve nenhuma reparação a acompanhar). "Recolhido" continua a depender só dessa escolha,
        // porque implica sempre algum tipo de reparação/acompanhamento.
        EquipamentoRecolhido? recolhaAutomatica = null;
        if (estadoFinal == EstadosEquipamento.AguardaEntrega && !jaTinhaRecolhaPendente)
            recolhaAutomatica = RecolhaEquipamentoService.RegistarAguardaEntregaSemAtividade(equipamento);

        if ((estadoFinal == EstadosEquipamento.Recolhido || estadoFinal == EstadosEquipamento.AguardaEntrega)
            && !jaTinhaRecolhaPendente)
        {
            var resposta = MessageBox.Show(
                $"O equipamento foi guardado com o estado \"{estadoFinal}\". Pretende criar uma Atividade DISIA associada a este equipamento?",
                "Criar Atividade DISIA", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resposta == MessageBoxResult.Yes)
            {
                try
                {
                    RecolhaEquipamentoService.CriarAtividadeAcompanhamento(equipamento, escolaSelecionada, estadoFinal, recolhaAutomatica);
                }
                catch (Exception ex)
                {
                    // O equipamento (e, para "Aguarda Entrega", também o registo de recolha) já
                    // estão gravados; só a criação da Atividade DISIA falhou, pelo que não há nada
                    // a reverter — mostra-se o erro e fica-se sem atividade, mas o equipamento
                    // continua corretamente rastreável em Equipamentos Recolhidos.
                    MessageBox.Show(
                        $"O equipamento foi gravado, mas não foi possível criar a Atividade DISIA associada:\n{ex.Message}",
                        "Erro ao criar Atividade DISIA", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // (3.2) Direção inversa: quando o estado passa de um estado de recolha/reparação em
        // curso ("Recolhido", "Em Reparação", "Reparado" ou "Aguarda Entrega") diretamente para
        // "Em Serviço" — o equipamento voltou fisicamente à escola — a aplicação, de forma
        // automática (sem passos manuais adicionais):
        //   1) fecha (estado "Fechada") qualquer Atividade DISIA de acompanhamento ainda aberta
        //      ligada a este equipamento, tal como aconteceria ao fechá-la manualmente em
        //      Atividades DISIA (ver AtividadeDisiaEditWindow.Guardar_Click);
        //   2) marca automaticamente a entrega do respetivo registo de EquipamentoRecolhido —
        //      o mesmo que o botão "Devolver à Escola" faz manualmente em Intervenções (ver
        //      IntervencaoEditWindow.DevolverRecolhido_Click) — dispensando esse passo manual,
        //      já que o estado "Em Serviço" escolhido aqui já confirma que o equipamento voltou.
        // Só atua sobre recolhas ainda pendentes (DataEntrega == null) deste equipamento; uma
        // recolha já entregue anteriormente não é tocada.
        var estadosDeRecolhaOuReparacao = new[]
        {
            EstadosEquipamento.Recolhido, EstadosEquipamento.EmReparacao,
            EstadosEquipamento.Reparado, EstadosEquipamento.AguardaEntrega
        };
        if (estadoFinal == EstadosEquipamento.EmServico
            && _estadoOriginal != null && estadosDeRecolhaOuReparacao.Contains(_estadoOriginal))
        {
            var recolhidosPendentes = App.Db.EquipamentosRecolhidos
                .Include(r => r.AtividadeDisia)
                .Where(r => r.EquipamentoId == equipamento.Id && r.DataEntrega == null)
                .ToList();

            if (recolhidosPendentes.Count > 0)
            {
                foreach (var recolhido in recolhidosPendentes)
                {
                    if (recolhido.AtividadeDisia != null && recolhido.AtividadeDisia.Estado != EstadoIntervencao.Fechada)
                        recolhido.AtividadeDisia.Estado = EstadoIntervencao.Fechada;

                    recolhido.Estado = EstadosRecolha.Entregue;
                    recolhido.DataEntrega = DateTime.Today;
                }

                try
                {
                    App.Db.SaveChanges();
                }
                catch (Exception ex)
                {
                    // O equipamento já ficou gravado com o novo estado; só a atualização
                    // automática da recolha/atividade associada falhou — avisa-se, mas não há
                    // nada a reverter (mesma filosofia da criação de Atividade DISIA acima).
                    MessageBox.Show(
                        $"O equipamento foi gravado com o estado \"Em Serviço\", mas não foi possível " +
                        $"fechar/entregar automaticamente a recolha associada:\n{ex.Message}\n\n" +
                        "Pode fazê-lo manualmente em Atividades DISIA / Intervenções.",
                        "Erro ao fechar recolha", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        return true;
    }

    private void GuardarNovo_Click(object sender, RoutedEventArgs e)
    {
        // Capturado antes de Guardar()/PrepararParaNovoEquipamento() limparem o campo — para a
        // confirmação abaixo poder identificar qual equipamento ficou gravado.
        var numeroSerieGravado = TxtNumeroSerie.Text.Trim();

        if (!Guardar()) return;

        // Um "Guardar e Novo" bem sucedido conta como sucesso da janela também, para quem a tiver
        // aberto (ex.: IntervencaoEditWindow.AdicionarNovoEntregue_Click) saber que pelo menos um
        // equipamento foi mesmo gravado, mesmo que a janela continue aberta para mais do que um.
        Sucesso = true;

        PrepararParaNovoEquipamento();
        MostrarConfirmacaoGuardado($"✓ Equipamento \"{numeroSerieGravado}\" gravado com sucesso. Pronto para o próximo.");
    }

    /// <summary>Mostra, por breves segundos (ou até começar a escrever-se o equipamento seguinte),
    /// uma confirmação visual de que a gravação em "💾 Guardar e Novo" correu bem — como a janela
    /// não fecha (ao contrário do "Guardar" normal, onde fechar já é a própria confirmação), sem
    /// isto não havia nenhum sinal visível de que o equipamento anterior tinha mesmo ficado
    /// gravado.</summary>
    private void MostrarConfirmacaoGuardado(string mensagem)
    {
        _timerConfirmacaoGuardado?.Stop();

        TxtConfirmacaoGuardado.Text = mensagem;
        PainelConfirmacaoGuardado.Visibility = Visibility.Visible;

        _timerConfirmacaoGuardado = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timerConfirmacaoGuardado.Tick += (_, _) =>
        {
            PainelConfirmacaoGuardado.Visibility = Visibility.Collapsed;
            _timerConfirmacaoGuardado!.Stop();
        };
        _timerConfirmacaoGuardado.Start();
    }

    /// <summary>Repõe o formulário para inserir mais um equipamento, sem fechar a janela — usado
    /// pelo botão "💾 Guardar e Novo" (ver GuardarNovo_Click), pensado para quando há vários
    /// equipamentos a dar entrada de seguida para a mesma escola. Mantém apenas os dados de
    /// Aquisição/Localização/Estado (Escola, Data, Valor, Fornecedor, Estado) — esses sim razoáveis
    /// de se repetirem num lote entregue à mesma escola no mesmo dia. Tudo o resto que identifica
    /// ESTE equipamento em concreto (Nº de Série, Nº de Inventário, Tipo de Equipamento, Marca,
    /// Modelo, e todas as Características Específicas, fixas e definidas pelo administrador) é
    /// limpo — a lista de campos espelha exatamente a de UsarModelo_Click (a operação inversa: em
    /// vez de preencher a partir de um modelo, aqui limpa-se tudo o que lá seria preenchido).</summary>
    private void PrepararParaNovoEquipamento()
    {
        _existente = null;
        EquipamentoGravado = null;
        // Mesmo valor com que o construtor inicializa esta variável para um equipamento novo (ver
        // "if (equipamento == null)" mais acima) — sem isto, ficaria com o estado ORIGINAL do
        // equipamento anterior, o que podia acionar incorretamente a lógica de transição de
        // estado (ex.: fecho automático de recolhas) ao gravar este próximo equipamento.
        _estadoOriginal = EstadosEquipamento.EmServico;
        TxtTitulo.Text = "Novo Equipamento";

        TxtNumeroSerie.Clear();
        TxtNumeroInventario.Clear();
        TxtObservacoes.Clear();

        TxtMarca.Clear();
        TxtModelo.Clear();

        // Também o próprio Tipo de Equipamento (a pedido, depois de confirmar que o uso real é
        // vários equipamentos DIFERENTES para a mesma escola, não um lote do mesmo tipo/modelo) —
        // isto colapsa o painel de Características Específicas de volta ao estado inicial (mesmo
        // AtualizarGruposVisiveis já chamado no construtor para "equipamento == null"), e desativa
        // "Usar Modelo..."/"Adicionar como Modelo..." até haver um Tipo escolhido outra vez.
        CmbTipo.Text = "";
        AtualizarGruposVisiveis(null);

        CmbProcessador.Text = "";
        TxtFamiliaProcessador.Clear();

        // Mesma ordem (pai → AtualizarOpcoesDependentes → filho) já usada em UsarModelo_Click, pela
        // mesma razão — limpar a combo-filha antes de recarregar as suas opções arrisca ficar com
        // um valor por lá que já não corresponde às opções disponíveis.
        CmbTipoMemoria.Text = "";
        AtualizarOpcoesDependentes(CmbTipoMemoria, CmbMemoriaGB, "Tipo de Memória");
        CmbMemoriaGB.Text = "";

        CmbTipoDisco.Text = "";
        AtualizarOpcoesDependentes(CmbTipoDisco, CmbTamanhoDisco, "Tipo de Disco");
        CmbTamanhoDisco.Text = "";

        CmbSistemaOperativo.Text = "";

        CmbPolegadas.Text = "";
        CmbTipoPainel.Text = "";
        CmbResolucaoMonitor.Text = "";

        CmbTipoImpressora.Text = "";
        ChkImpressaoCor.IsChecked = false;
        CmbLigacaoImpressora.Text = "";

        CmbNumeroPortas.Text = "";
        CmbVelocidadeRede.Text = "";
        ChkGerivel.IsChecked = false;

        CmbResolucaoCamera.Text = "";
        CmbTipoCamera.Text = "";
        ChkVisaoNoturna.IsChecked = false;

        CmbLuminosidade.Text = "";
        CmbResolucaoProjetor.Text = "";

        TxtEspecificacoesAdicionais.Clear();

        foreach (var campo in _camposCaracteristicasAdicionais.Values)
        {
            switch (campo)
            {
                case TextBox caixa: caixa.Text = ""; break;
                case ComboBox combo: combo.Text = ""; break;
            }
        }

        AtualizarObsolescencia();
        TxtNumeroSerie.Focus();
    }

    /// <summary>Compara o hardware do equipamento tal como estava ao abrir esta janela
    /// (<see cref="_valoresHardwareOriginais"/>) com os valores atuais, devolvendo uma frase pronta
    /// a inserir nas Observações da Atividade DISIA (ex: "Disco: HDD 500GB → SSD 480GB"). Devolve
    /// null se nada de relevante mudou.</summary>
    private string? ConstruirResumoAlteracoesHardware(Equipamento equipamento)
    {
        var linhas = new List<string>();

        void Comparar(string rotulo, string? antigo, string? novo)
        {
            antigo = string.IsNullOrWhiteSpace(antigo) ? "—" : antigo;
            novo = string.IsNullOrWhiteSpace(novo) ? "—" : novo;
            if (antigo != novo) linhas.Add($"{rotulo}: {antigo} → {novo}");
        }

        Comparar("Processador", _valoresHardwareOriginais.GetValueOrDefault("Processador"), equipamento.Processador);

        var memoriaAntiga = FormatarMemoria(
            _valoresHardwareOriginais.GetValueOrDefault("TipoMemoria"),
            _valoresHardwareOriginais.GetValueOrDefault("QuantidadeMemoriaGB"));
        var memoriaNova = FormatarMemoria(equipamento.TipoMemoria, equipamento.QuantidadeMemoriaGB?.ToString());
        if (memoriaAntiga != memoriaNova) linhas.Add($"Memória: {memoriaAntiga} → {memoriaNova}");

        var discoAntigo = FormatarDisco(
            _valoresHardwareOriginais.GetValueOrDefault("TipoDisco"),
            _valoresHardwareOriginais.GetValueOrDefault("TamanhoDiscoGB"));
        var discoNovo = FormatarDisco(equipamento.TipoDisco, equipamento.TamanhoDiscoGB?.ToString());
        if (discoAntigo != discoNovo) linhas.Add($"Disco: {discoAntigo} → {discoNovo}");

        Comparar("Sistema Operativo", _valoresHardwareOriginais.GetValueOrDefault("SistemaOperativo"), equipamento.SistemaOperativo);

        if (linhas.Count == 0) return null;

        return $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Equipamento {equipamento.NumeroSerie} — alteração de hardware: {string.Join("; ", linhas)}.";
    }

    private static string FormatarMemoria(string? tipo, string? quantidadeGB) =>
        string.IsNullOrWhiteSpace(quantidadeGB) ? "—" : $"{quantidadeGB}GB{(string.IsNullOrWhiteSpace(tipo) ? "" : " " + tipo)}";

    private static string FormatarDisco(string? tipo, string? tamanhoGB) =>
        string.IsNullOrWhiteSpace(tipo) && string.IsNullOrWhiteSpace(tamanhoGB) ? "—" : $"{tipo} {tamanhoGB}GB".Trim();
}
