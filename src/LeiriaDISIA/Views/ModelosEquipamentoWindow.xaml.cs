using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
// "Control" (ao contrário de "UserControl", que já vem com alias global — ver GlobalUsings.cs), e
// pela mesma razão "ComboBox"/"TextBox", são ambíguos neste projeto por causa de
// UseWindowsForms=true (System.Windows.Forms tem os três também) — mesmo conjunto de aliases já
// usado em Views/EquipamentoEditWindow.xaml.cs pela mesma razão.
using Control = System.Windows.Controls.Control;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace LeiriaDISIA.Views;

/// <summary>
/// Gestão do catálogo de "modelos base" de equipamento (ver Models/ModeloEquipamento.cs) —
/// acessível a partir do botão "📋 Modelos de Equipamento" em Views/EquipamentosWindow.xaml, e
/// usada a partir de Views/EquipamentoEditWindow.xaml.cs através do botão "Usar Modelo..." junto
/// ao Tipo de Equipamento (ver <see cref="SelecionarModeloWindow"/>).
///
/// O painel de "Características Específicas" à direita replica deliberadamente a mesma estrutura
/// (mesmos 7 grupos, mesmas sugestões vindas de ValoresCaracteristicaEmbutida, e o mesmo painel
/// dinâmico "Características Adicionais" para as características definidas pelo administrador —
/// ver AtualizarCaracteristicasAdicionais) já usada em EquipamentoEditWindow.xaml — não partilhada
/// em código entre as duas janelas (tal como o resto desta aplicação não partilha código de UI
/// entre janelas), mas mantida propositadamente igual para quem já conhece o formulário de
/// equipamento reconhecer logo esta como a mesma coisa, só sem os campos próprios de cada unidade
/// física. Esta primeira versão não replica a relação de subtipo entre "Tipo de Memória"/"Memória
/// (GB)" (e Disco) — as duas combos ficam sempre ativas aqui, ao contrário do comportamento em
/// EquipamentoEditWindow — para manter esta janela mais simples; pode vir a ser alinhada mais
/// tarde se fizer falta.
/// </summary>
public partial class ModelosEquipamentoWindow : Window
{
    private List<ModeloEquipamento> _todos = new();
    private List<PesquisaAvancadaService.TipoEquipamentoPesquisavel> _tiposEquipamento = new();
    private ModeloEquipamento? _selecionado;

    /// <summary>Campos dinâmicos gerados por AtualizarCaracteristicasAdicionais, indexados pelo Id
    /// da característica — mesma abordagem de EquipamentoEditWindow.xaml.cs, para GravarCaracteristicasAdicionais
    /// conseguir ler o valor de cada um sem precisar de percorrer o painel à procura deles.</summary>
    private readonly Dictionary<int, Control> _camposCaracteristicasAdicionais = new();

    public ModelosEquipamentoWindow()
    {
        InitializeComponent();
        // Perfil Guest (Services/SessaoAtual.PodeEditar): não pode criar/editar/eliminar modelos -
        // fecha-se logo a seguir a abrir, tal como Views/EquipamentoEditWindow.xaml.cs.
        if (PermissoesService.BloquearAberturaSeGuest(this)) return;

        MaxHeight = SystemParameters.WorkArea.Height - 24;

        _tiposEquipamento = PesquisaAvancadaService.ObterTiposEquipamento(App.Db);
        CmbTipo.ItemsSource = _tiposEquipamento;
        CmbTipo.DisplayMemberPath = "Nome";

        CmbFiltroTipo.ItemsSource = new[] { "(Todos)" }.Concat(_tiposEquipamento.Select(t => t.Nome)).ToList();
        CmbFiltroTipo.SelectedIndex = 0;

        Recarregar();
        LimparFormulario();
    }

    private void Recarregar()
    {
        _todos = App.Db.ModelosEquipamento.OrderBy(m => m.Tipo).ThenBy(m => m.Nome).ThenBy(m => m.Marca).ToList();
        AplicarFiltro();
    }

    private void AplicarFiltro()
    {
        IEnumerable<ModeloEquipamento> resultado = _todos;

        if (CmbFiltroTipo.SelectedItem is string tipo && tipo != "(Todos)")
            resultado = resultado.Where(m => m.Tipo == tipo);

        var termo = TxtPesquisa?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            resultado = resultado.Where(m =>
                (m.Nome != null && m.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (m.Marca != null && m.Marca.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                (m.Modelo != null && m.Modelo.Contains(termo, StringComparison.OrdinalIgnoreCase)));
        }

        ListaModelos.ItemsSource = resultado.ToList();
    }

    private void Filtro_Changed(object sender, RoutedEventArgs e) => AplicarFiltro();

    private void ListaModelos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaModelos.SelectedItem is ModeloEquipamento modelo) CarregarModelo(modelo);
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        ListaModelos.SelectedItem = null;
        LimparFormulario();
    }

    private void LimparFormulario()
    {
        _selecionado = null;
        TxtTituloEdicao.Text = "Novo Modelo";
        BtnEliminar.IsEnabled = false;

        CmbTipo.SelectedItem = null;
        TxtNome.Text = "";
        TxtMarca.Text = "";
        TxtModelo.Text = "";
        ChkAtivo.IsChecked = true;

        LimparCaracteristicas();
        AtualizarGruposVisiveis(null);
    }

    private void LimparCaracteristicas()
    {
        CmbProcessador.Text = ""; TxtFamiliaProcessador.Text = ""; CmbTipoMemoria.Text = ""; CmbMemoriaGB.Text = "";
        CmbTipoDisco.Text = ""; CmbTamanhoDisco.Text = ""; CmbSistemaOperativo.Text = "";
        CmbPolegadas.Text = ""; CmbTipoPainel.Text = ""; CmbResolucaoMonitor.Text = "";
        CmbTipoImpressora.Text = ""; ChkImpressaoCor.IsChecked = false; CmbLigacaoImpressora.Text = "";
        CmbNumeroPortas.Text = ""; CmbVelocidadeRede.Text = ""; ChkGerivel.IsChecked = false;
        CmbResolucaoCamera.Text = ""; CmbTipoCamera.Text = ""; ChkVisaoNoturna.IsChecked = false;
        CmbLuminosidade.Text = ""; CmbResolucaoProjetor.Text = "";
        TxtEspecificacoesAdicionais.Text = "";
    }

    private void CarregarModelo(ModeloEquipamento modelo)
    {
        _selecionado = modelo;
        TxtTituloEdicao.Text = $"Editar Modelo — {modelo.RotuloExibicao}";
        BtnEliminar.IsEnabled = true;

        CmbTipo.SelectedItem = _tiposEquipamento.FirstOrDefault(t => t.Nome == modelo.Tipo);
        TxtNome.Text = modelo.Nome;
        TxtMarca.Text = modelo.Marca;
        TxtModelo.Text = modelo.Modelo;
        ChkAtivo.IsChecked = modelo.Ativo;

        CmbProcessador.Text = modelo.Processador;
        TxtFamiliaProcessador.Text = modelo.FamiliaProcessador;
        CmbTipoMemoria.Text = modelo.TipoMemoria;
        CmbMemoriaGB.Text = modelo.QuantidadeMemoriaGB?.ToString();
        CmbTipoDisco.Text = modelo.TipoDisco;
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

        AtualizarGruposVisiveis(modelo.Tipo);
    }

    private void CmbTipo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // TipoEquipamentoPesquisavel é um "record struct" (tipo valor) — "as" só é válido em tipos
        // referência ou Nullable<T>, por isso usa-se aqui correspondência de padrões ("is ... var")
        // em vez de "as" seguido de "?.".
        var tipo = CmbTipo.SelectedItem is PesquisaAvancadaService.TipoEquipamentoPesquisavel item ? item.Nome : null;
        AtualizarGruposVisiveis(tipo);
    }

    /// <summary>Mostra só o grupo de características do Tipo escolhido (mesma lógica de
    /// AtualizarGruposVisiveis em EquipamentoEditWindow.xaml.cs) e popula as combos desse grupo com
    /// as mesmas sugestões já configuradas em Administração → Dados Fixos — ver
    /// ValoresCaracteristicaEmbutida abaixo.</summary>
    private void AtualizarGruposVisiveis(string? tipoNome)
    {
        // Idem — FirstOrDefault() sobre uma lista de um "record struct" devolve default(...), não
        // null, por isso não se pode encadear "?." diretamente a seguir; projeta-se primeiro para
        // GrupoCaracteristicas (esse sim, um tipo referência), e só depois é que FirstOrDefault()
        // devolve null quando não há nenhuma correspondência.
        var grupo = _tiposEquipamento.Where(t => t.Nome == tipoNome).Select(t => t.GrupoCaracteristicas).FirstOrDefault();

        GrupoComputador.Visibility = grupo == GruposCaracteristicasEquipamento.Computador ? Visibility.Visible : Visibility.Collapsed;
        GrupoMonitor.Visibility = grupo == GruposCaracteristicasEquipamento.Monitor ? Visibility.Visible : Visibility.Collapsed;
        GrupoImpressora.Visibility = grupo == GruposCaracteristicasEquipamento.Impressora ? Visibility.Visible : Visibility.Collapsed;
        GrupoRede.Visibility = grupo == GruposCaracteristicasEquipamento.Rede ? Visibility.Visible : Visibility.Collapsed;
        GrupoCamera.Visibility = grupo == GruposCaracteristicasEquipamento.Camera ? Visibility.Visible : Visibility.Collapsed;
        GrupoProjetor.Visibility = grupo == GruposCaracteristicasEquipamento.Projetor ? Visibility.Visible : Visibility.Collapsed;
        GrupoGenerico.Visibility = grupo == GruposCaracteristicasEquipamento.Generico || grupo == null ? Visibility.Visible : Visibility.Collapsed;

        if (grupo == null)
        {
            AtualizarCaracteristicasAdicionais(GruposCaracteristicasEquipamento.Generico, tipoNome);
            return;
        }

        if (grupo == GruposCaracteristicasEquipamento.Computador)
        {
            CmbProcessador.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Processador", Array.Empty<string>());
            CmbTipoMemoria.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Tipo de Memória", new[] { "DDR3", "DDR4", "DDR5" });
            CmbTipoDisco.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Tipo de Disco", new[] { "HDD", "SSD", "NVMe" });
            CmbSistemaOperativo.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Sistema Operativo", Array.Empty<string>());
        }
        else if (grupo == GruposCaracteristicasEquipamento.Monitor)
        {
            CmbPolegadas.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Polegadas", new[] { "19", "21", "24", "27", "32" });
            CmbTipoPainel.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Tipo de Painel", new[] { "LED", "LCD", "OLED" });
            CmbResolucaoMonitor.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Resolução", new[] { "1366x768", "1920x1080", "2560x1440", "3840x2160" });
        }
        else if (grupo == GruposCaracteristicasEquipamento.Impressora)
        {
            CmbTipoImpressora.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Tipo de Impressora", new[] { "Laser", "Tinta" });
            CmbLigacaoImpressora.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Tipo de Ligação", new[] { "USB", "Rede", "WiFi" });
        }
        else if (grupo == GruposCaracteristicasEquipamento.Rede)
        {
            CmbNumeroPortas.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Nº de Portas", new[] { "4", "5", "8", "16", "24", "48" });
            CmbVelocidadeRede.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Velocidade", new[] { "100 Mbps", "1 Gbps", "2.5 Gbps", "10 Gbps" });
        }
        else if (grupo == GruposCaracteristicasEquipamento.Camera)
        {
            CmbResolucaoCamera.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Resolução", new[] { "2MP", "4MP", "1080p", "4K" });
            CmbTipoCamera.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Tipo", new[] { "IP", "Analógica" });
        }
        else if (grupo == GruposCaracteristicasEquipamento.Projetor)
        {
            CmbLuminosidade.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Luminosidade (Lumens)", new[] { "2000", "3000", "4000", "5000", "6000" });
            CmbResolucaoProjetor.ItemsSource = ValoresCaracteristicaEmbutida(grupo, "Resolução", new[] { "1280x800", "1920x1080", "3840x2160" });
        }

        AtualizarCaracteristicasAdicionais(grupo, tipoNome);
    }

    /// <summary>Réplica de EquipamentoEditWindow.ValoresCaracteristicaEmbutida (privada nessa
    /// classe, por isso duplicada aqui, tal como PesquisaAvancadaService também tem a sua própria
    /// cópia): valores sugeridos configurados pelo administrador para uma característica embutida
    /// de um grupo, com valores por omissão para quando ainda não há nenhum configurado.</summary>
    private static string[] ValoresCaracteristicaEmbutida(string grupoCaracteristicas, string nome, string[] valoresPorOmissao)
    {
        var valores = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupoCaracteristicas && c.Nome == nome)
            .Join(App.Db.CaracteristicaEquipamentoOpcoes.Where(o => o.Ativo),
                c => c.Id, o => o.CaracteristicaEquipamentoId, (c, o) => o)
            .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
            .Select(o => o.Valor)
            .ToArray();

        return valores.Length > 0 ? valores : valoresPorOmissao;
    }

    /// <summary>Réplica de EquipamentoEditWindow.NomesCaracteristicasEmbutidasPorGrupo (privado
    /// nessa classe): nomes das características que já têm campo fixo próprio no painel de
    /// "Características Específicas" acima, para serem excluídas do painel dinâmico
    /// "Características Adicionais" e não aparecerem duplicadas.</summary>
    private static readonly Dictionary<string, HashSet<string>> NomesCaracteristicasEmbutidasPorGrupo = new()
    {
        [GruposCaracteristicasEquipamento.Computador] = new() { "Processador", "Tipo de Memória", "Memória (GB)", "Tipo de Disco", "Tamanho do Disco (GB)", "Sistema Operativo" },
        [GruposCaracteristicasEquipamento.Rede] = new() { "Nº de Portas", "Velocidade" },
        [GruposCaracteristicasEquipamento.Camera] = new() { "Tipo", "Resolução" },
        [GruposCaracteristicasEquipamento.Monitor] = new() { "Tipo de Painel", "Polegadas", "Resolução" },
        [GruposCaracteristicasEquipamento.Projetor] = new() { "Luminosidade (Lumens)", "Resolução" },
        [GruposCaracteristicasEquipamento.Impressora] = new() { "Tipo de Impressora", "Tipo de Ligação", "Impressão a cores" }
    };

    /// <summary>Réplica de EquipamentoEditWindow.AtualizarCaracteristicasAdicionais (privado nessa
    /// classe) — gera, em <see cref="PainelCaracteristicasAdicionais"/>, um rótulo + campo para
    /// cada característica ativa definida pelo administrador para o grupo indicado, com as mesmas
    /// regras de lá: exclui as que já têm campo fixo próprio acima
    /// (<see cref="NomesCaracteristicasEmbutidasPorGrupo"/>), exclui características-filha (só
    /// aparecem através da característica-pai), e respeita "Aplica-se apenas a" (partilhada, ou
    /// exclusiva do Tipo escolhido). Pré-preenche com o valor já gravado para o modelo em edição,
    /// e mostra uma caixa de seleção editável em vez de uma simples caixa de texto quando a
    /// característica tiver valores sugeridos configurados.</summary>
    private void AtualizarCaracteristicasAdicionais(string grupoCaracteristicas, string? tipo)
    {
        PainelCaracteristicasAdicionais.Children.Clear();
        _camposCaracteristicasAdicionais.Clear();

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
                        && (nomesEmbutidosDesteGrupo == null || !nomesEmbutidosDesteGrupo.Contains(c.Nome))
                        && c.CaracteristicaPaiId == null
                        && (c.TipoEquipamentoId == null || c.TipoEquipamentoId == idTipoAtual))
            .OrderBy(c => c.Ordem)
            .ThenBy(c => c.Nome)
            .ToList();

        if (caracteristicas.Count == 0)
        {
            GrupoCaracteristicasAdicionais.Visibility = Visibility.Collapsed;
            return;
        }

        var valoresExistentes = _selecionado == null
            ? new Dictionary<int, string?>()
            : App.Db.ModeloEquipamentoCaracteristicaValores
                .Where(v => v.ModeloEquipamentoId == _selecionado.Id)
                .ToDictionary(v => v.CaracteristicaEquipamentoId, v => v.Valor);

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
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2)
            };

            valoresExistentes.TryGetValue(caracteristica.Id, out var valorGravado);
            var valorInicial = valorGravado ?? (_selecionado == null ? caracteristica.ValorPorOmissao : null);

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

    private static string? ObterTextoCampoCaracteristica(Control campo) => campo switch
    {
        TextBox caixa => caixa.Text,
        ComboBox combo => combo.Text,
        _ => null
    };

    /// <summary>Réplica de EquipamentoEditWindow.GravarCaracteristicasAdicionais (privado nessa
    /// classe) — grava (cria/atualiza/remove) os valores preenchidos nos campos dinâmicos gerados
    /// por <see cref="AtualizarCaracteristicasAdicionais"/>. Usa a propriedade de navegação
    /// ModeloEquipamento em vez do Id diretamente, para também funcionar com um modelo novo ainda
    /// sem Id atribuído — o EF Core resolve a chave estrangeira sozinho no mesmo SaveChanges() que
    /// grava o modelo.</summary>
    private void GravarCaracteristicasAdicionais(ModeloEquipamento modelo)
    {
        foreach (var (caracteristicaId, campo) in _camposCaracteristicasAdicionais)
        {
            var texto = ObterTextoCampoCaracteristica(campo);
            var valor = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

            var existente = _selecionado == null
                ? null
                : App.Db.ModeloEquipamentoCaracteristicaValores.FirstOrDefault(v =>
                    v.ModeloEquipamentoId == _selecionado.Id && v.CaracteristicaEquipamentoId == caracteristicaId);

            if (existente != null)
            {
                if (valor == null)
                    App.Db.ModeloEquipamentoCaracteristicaValores.Remove(existente);
                else
                    existente.Valor = valor;
            }
            else if (valor != null)
            {
                App.Db.ModeloEquipamentoCaracteristicaValores.Add(new ModeloEquipamentoCaracteristicaValor
                {
                    ModeloEquipamento = modelo,
                    CaracteristicaEquipamentoId = caracteristicaId,
                    Valor = valor
                });
            }
        }
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTipo.SelectedItem is not PesquisaAvancadaService.TipoEquipamentoPesquisavel tipo)
        {
            MessageBox.Show("Escolha o Tipo de Equipamento.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var modelo = _selecionado;
        var novo = modelo == null;
        modelo ??= new ModeloEquipamento();

        modelo.Tipo = tipo.Nome;
        modelo.Nome = TextoOuNulo(TxtNome.Text);
        modelo.Marca = TextoOuNulo(TxtMarca.Text);
        modelo.Modelo = TextoOuNulo(TxtModelo.Text);
        modelo.Ativo = ChkAtivo.IsChecked == true;

        modelo.Processador = TextoOuNulo(CmbProcessador.Text);
        modelo.FamiliaProcessador = TextoOuNulo(TxtFamiliaProcessador.Text);
        modelo.TipoMemoria = TextoOuNulo(CmbTipoMemoria.Text);
        modelo.QuantidadeMemoriaGB = ParseInt(CmbMemoriaGB.Text);
        modelo.TipoDisco = TextoOuNulo(CmbTipoDisco.Text);
        modelo.TamanhoDiscoGB = ParseInt(CmbTamanhoDisco.Text);
        modelo.SistemaOperativo = TextoOuNulo(CmbSistemaOperativo.Text);

        modelo.PolegadasMonitor = ParseDouble(CmbPolegadas.Text);
        modelo.TipoPainelMonitor = TextoOuNulo(CmbTipoPainel.Text);
        modelo.ResolucaoMonitor = TextoOuNulo(CmbResolucaoMonitor.Text);

        modelo.TipoImpressora = TextoOuNulo(CmbTipoImpressora.Text);
        modelo.ImpressaoCor = ChkImpressaoCor.IsChecked;
        modelo.LigacaoImpressora = TextoOuNulo(CmbLigacaoImpressora.Text);

        modelo.NumeroPortas = ParseInt(CmbNumeroPortas.Text);
        modelo.VelocidadeRede = TextoOuNulo(CmbVelocidadeRede.Text);
        modelo.Gerivel = ChkGerivel.IsChecked;

        modelo.ResolucaoCamera = TextoOuNulo(CmbResolucaoCamera.Text);
        modelo.TipoCamera = TextoOuNulo(CmbTipoCamera.Text);
        modelo.VisaoNoturna = ChkVisaoNoturna.IsChecked;

        modelo.LuminosidadeLumens = ParseInt(CmbLuminosidade.Text);
        modelo.ResolucaoProjetor = TextoOuNulo(CmbResolucaoProjetor.Text);

        modelo.EspecificacoesAdicionais = TextoOuNulo(TxtEspecificacoesAdicionais.Text);

        if (novo) App.Db.ModelosEquipamento.Add(modelo);

        GravarCaracteristicasAdicionais(modelo);

        try
        {
            App.Db.SaveChanges();
        }
        catch (Exception ex)
        {
            var causaRaiz = ex;
            while (causaRaiz.InnerException != null) causaRaiz = causaRaiz.InnerException;
            MessageBox.Show($"Não foi possível gravar o modelo:\n{causaRaiz.Message}", "Erro ao gravar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var idGravado = modelo.Id;
        Recarregar();
        ListaModelos.SelectedItem = _todos.FirstOrDefault(m => m.Id == idGravado);
    }

    private static string? TextoOuNulo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    private static int? ParseInt(string? texto) => int.TryParse(texto, out var v) ? v : null;
    private static double? ParseDouble(string? texto) => double.TryParse(texto, out var v) ? v : null;

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionado == null) return;

        var confirmar = MessageBox.Show(
            $"Eliminar o modelo \"{_selecionado.RotuloExibicao}\"?\n\nEquipamento já criado a partir deste modelo não é afetado — só deixa de estar disponível para escolher em equipamento novo.",
            "Confirmar eliminação", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmar != MessageBoxResult.Yes) return;

        App.Db.ModelosEquipamento.Remove(_selecionado);
        App.Db.SaveChanges();

        Recarregar();
        LimparFormulario();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
