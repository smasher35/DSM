using System.Windows;
using System.Windows.Controls;
// O projeto tem UseWindowsForms=true (para o seletor de cor noutro módulo — ver
// Services/ColorPickerHelper.cs), o que torna estes tipos ambíguos entre WPF e WinForms sempre que
// ambos os namespaces ficam disponíveis no mesmo ficheiro. Aliases explícitos resolvem a ambiguidade
// sem ter de qualificar o nome completo em cada utilização — mesmo padrão já usado em
// Views/EquipamentoRecolhidoWindow.xaml.cs.
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

/// <summary>
/// Item 2.1: pesquisa avançada de equipamento. Cada linha de filtro segue sempre a mesma
/// sequência, do mais geral para o mais específico:
///
/// 1. <b>Tipo de Equipamento</b> — combo com os tipos configurados em Administração → Dados Fixos
///    → Tipos de Equipamento (Computador de Secretária, Monitor, Impressora, Access Point, etc.) —
///    escolha prioritária, sempre a primeira.
/// 2. <b>Comparador do tipo</b> — "=" ou "≠".
/// 3. <b>Subtipo/Característica</b> — combo com os campos relevantes para o tipo escolhido: os
///    transversais (Marca, Modelo, Nº de Série, Escola, Estado, Fornecedor, etc., disponíveis para
///    qualquer tipo) juntamente com os específicos desse tipo em concreto (Processador, Memória,
///    Disco, etc. para Computador; Polegadas, Painel, Resolução para Monitor; ...). Inclui sempre
///    a opção "(Apenas o tipo — sem subtipo)" no topo, para filtrar só pelo Tipo sem escolher mais
///    nada.
/// 4. <b>Sub-subtipo (valores sugeridos)</b> — só aparece quando o Subtipo escolhido tem uma lista
///    fechada de valores conhecidos (ex.: Tipo de Disco → SSD/HDD/NVMe, Estado → Em Serviço/
///    Recolhido/..., os mesmos valores já configurados/usados em Inserir/Editar Equipamento — ver
///    <see cref="PesquisaAvancadaService.ObterCamposEquipamento"/>): mostra esses valores
///    diretamente numa combo, com um comparador restrito a "="/"≠" (não faz sentido "conter" um
///    valor escolhido de uma lista fechada).
/// 5. <b>Comparador + Valor</b> — só aparece quando o Subtipo NÃO tem valores sugeridos (ex.:
///    Modelo, Nº de Série, Memória (GB), Data de Aquisição): comparador completo consoante o tipo
///    de dado (=, ≠, &gt;, &gt;=, &lt;, &lt;=, contém/não contém) + caixa de valor livre (ou
///    Sim/Não para campos Booleanos).
///
/// Uma linha completa (Tipo + Subtipo escolhidos) representa DUAS condições combinadas com E
/// ("Tipo = Computador de Secretária E Tipo de Disco = SSD") — ver <see cref="ObterFiltros"/>, que
/// as traduz para o motor de comparação partilhado com a Pesquisa Avançada de Atividades DISIA
/// (item 3.1) — ver <see cref="PesquisaAvancadaService"/>. Sem Subtipo escolhido, representa só
/// "Tipo = &lt;valor&gt;".
///
/// Tal como os relatórios de módulo (item 1.1), não deixa gerar um PDF sem que a pesquisa tenha
/// devolvido pelo menos um resultado — o botão "Gerar PDF" só é ativado depois de uma pesquisa com
/// resultados (ver <see cref="Pesquisar_Click"/>).
/// </summary>
public partial class PesquisaAvancadaEquipamentoWindow : Window
{
    /// <summary>Item "placeholder" da combo Subtipo, para representar "sem subtipo — só o tipo
    /// principal" sem precisar de um SelectedItem nulo (que a ComboBox mostraria em branco).</summary>
    private static readonly CampoPesquisavel<Equipamento> SubtipoVazio = new()
    {
        Chave = "__nenhum__",
        Rotulo = "(Apenas o tipo — sem subtipo)",
        Tipo = TipoDadoCampoPesquisa.Texto,
        ObterValor = _ => null
    };

    private static readonly string[] ComparadoresTipo = { "=", "≠" };

    /// <summary>Comparadores para um Subtipo com valores sugeridos (sub-subtipo): só "="/"≠" — o
    /// valor já vem escolhido de uma lista fechada, "conter"/"não conter" não faz sentido aqui.</summary>
    private static readonly string[] ComparadoresValorSugerido = { "=", "≠" };

    private readonly List<Equipamento> _todosEquipamentos;
    private readonly List<CampoPesquisavel<Equipamento>> _todosOsCampos;
    private readonly List<PesquisaAvancadaService.TipoEquipamentoPesquisavel> _tiposEquipamento;
    private readonly List<LinhaFiltroUi> _linhas = new();
    private List<Equipamento> _resultado = new();

    /// <summary>Controlos WPF de uma linha de filtro construída dinamicamente — agrupados aqui para
    /// não precisar de percorrer <see cref="PainelFiltros"/> à procura deles sempre que é preciso
    /// ler o filtro atual (ver <see cref="ObterFiltros"/>).</summary>
    private class LinhaFiltroUi
    {
        public StackPanel Painel = null!;
        public ComboBox CmbTipo = null!;
        public ComboBox CmbComparadorTipo = null!;
        public ComboBox CmbSubtipo = null!;
        public ComboBox CmbComparadorValor = null!;
        // Um dos três seguintes fica visível de cada vez, consoante o Subtipo escolhido (ver
        // AtualizarControloDeValor): valores sugeridos (lista fechada), Sim/Não (Booleano), ou
        // texto/número livre.
        public ComboBox CmbValorSugerido = null!;
        public ComboBox CmbValorBool = null!;
        public TextBox TxtValor = null!;
    }

    /// <summary>Linha simplificada para a grelha de pré-visualização — evita bindings complexos
    /// (concatenar Marca+Modelo, escolher entre Escola e Local não escolar) diretamente em XAML.</summary>
    private class LinhaResultado
    {
        public string Tipo { get; set; } = "";
        public string MarcaModelo { get; set; } = "";
        public string NumeroSerie { get; set; } = "";
        public string EscolaOuLocal { get; set; } = "";
        public string Estado { get; set; } = "";
    }

    public PesquisaAvancadaEquipamentoWindow()
    {
        InitializeComponent();

        _todosEquipamentos = App.Db.Equipamentos.Include(e => e.Escola).ToList();
        _todosOsCampos = PesquisaAvancadaService.ObterCamposEquipamento(App.Db);
        _tiposEquipamento = PesquisaAvancadaService.ObterTiposEquipamento(App.Db);

        AdicionarLinhaFiltro();
    }

    private void AdicionarFiltro_Click(object sender, RoutedEventArgs e) => AdicionarLinhaFiltro();

    private void AdicionarLinhaFiltro()
    {
        var cmbTipo = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 6, 0), ItemsSource = _tiposEquipamento };
        var cmbComparadorTipo = new ComboBox { Width = 55, Margin = new Thickness(0, 0, 6, 0), ItemsSource = ComparadoresTipo };
        var cmbSubtipo = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 6, 0) };
        var cmbComparadorValor = new ComboBox { Width = 80, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
        var cmbValorSugerido = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Collapsed };
        var cmbValorBool = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 6, 0), ItemsSource = new[] { "Sim", "Não" }, Visibility = Visibility.Collapsed };
        var txtValor = new TextBox { Width = 160, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var btnRemover = new Button { Content = "✕", Width = 28, ToolTip = "Remover este filtro" };

        var painel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        painel.Children.Add(cmbTipo);
        painel.Children.Add(cmbComparadorTipo);
        painel.Children.Add(cmbSubtipo);
        painel.Children.Add(cmbComparadorValor);
        painel.Children.Add(cmbValorSugerido);
        painel.Children.Add(cmbValorBool);
        painel.Children.Add(txtValor);
        painel.Children.Add(btnRemover);

        var linha = new LinhaFiltroUi
        {
            Painel = painel,
            CmbTipo = cmbTipo,
            CmbComparadorTipo = cmbComparadorTipo,
            CmbSubtipo = cmbSubtipo,
            CmbComparadorValor = cmbComparadorValor,
            CmbValorSugerido = cmbValorSugerido,
            CmbValorBool = cmbValorBool,
            TxtValor = txtValor
        };

        cmbTipo.SelectionChanged += (_, _) => AoMudarTipo(linha);
        cmbSubtipo.SelectionChanged += (_, _) => AoMudarSubtipo(linha);
        btnRemover.Click += (_, _) =>
        {
            PainelFiltros.Children.Remove(painel);
            _linhas.Remove(linha);
        };

        _linhas.Add(linha);
        PainelFiltros.Children.Add(painel);

        // Pré-seleciona logo o primeiro tipo e o primeiro comparador, para a linha ficar
        // imediatamente utilizável.
        if (cmbComparadorTipo.Items.Count > 0) cmbComparadorTipo.SelectedIndex = 0;
        if (_tiposEquipamento.Count > 0) cmbTipo.SelectedIndex = 0;
    }

    /// <summary>Ao escolher (ou mudar) o Tipo de Equipamento: repopula a combo Subtipo com os
    /// campos transversais (disponíveis para qualquer tipo) mais os específicos do grupo de
    /// características deste tipo em concreto — sempre com a opção "(Apenas o tipo — sem subtipo)"
    /// no topo.</summary>
    private void AoMudarTipo(LinhaFiltroUi linha)
    {
        if (linha.CmbTipo.SelectedItem is not PesquisaAvancadaService.TipoEquipamentoPesquisavel tipoEscolhido)
        {
            linha.CmbSubtipo.ItemsSource = null;
            return;
        }

        var opcoesSubtipo = new List<CampoPesquisavel<Equipamento>> { SubtipoVazio };
        opcoesSubtipo.AddRange(_todosOsCampos.Where(c =>
            c.Chave != "Tipo" // o próprio Tipo já está a ser escolhido na 1ª combo — não faz sentido repeti-lo aqui
            && (c.GrupoCaracteristicas == null // campo transversal — disponível para qualquer tipo
                || (c.GrupoCaracteristicas == tipoEscolhido.GrupoCaracteristicas
                    && (c.TipoEquipamentoId == null || c.TipoEquipamentoId == tipoEscolhido.Id)))));

        var subtipoAnterior = linha.CmbSubtipo.SelectedItem as CampoPesquisavel<Equipamento>;
        linha.CmbSubtipo.ItemsSource = opcoesSubtipo;
        // Mantém o mesmo subtipo escolhido, se continuar disponível para o novo tipo (ex.: "Marca"
        // continua válido ao trocar de Computador para Monitor); caso contrário, volta ao
        // placeholder em vez de ficar com um subtipo que já não faz sentido para este tipo.
        linha.CmbSubtipo.SelectedItem = subtipoAnterior != null && opcoesSubtipo.Any(c => c.Chave == subtipoAnterior.Chave)
            ? opcoesSubtipo.First(c => c.Chave == subtipoAnterior.Chave)
            : opcoesSubtipo[0];
    }

    /// <summary>Ao escolher (ou limpar) um Subtipo: mostra o controlo de valor certo — combo de
    /// valores sugeridos (sub-subtipo), combo Sim/Não, ou texto/número livre — com o comparador
    /// correspondente, ou esconde tudo quando o Subtipo volta ao placeholder "(Apenas o tipo)".</summary>
    private void AoMudarSubtipo(LinhaFiltroUi linha)
    {
        var subtipo = linha.CmbSubtipo.SelectedItem as CampoPesquisavel<Equipamento>;
        var temSubtipoReal = subtipo != null && subtipo.Chave != SubtipoVazio.Chave;

        if (!temSubtipoReal)
        {
            linha.CmbComparadorValor.Visibility = Visibility.Collapsed;
            linha.CmbValorSugerido.Visibility = Visibility.Collapsed;
            linha.CmbValorBool.Visibility = Visibility.Collapsed;
            linha.TxtValor.Visibility = Visibility.Collapsed;
            return;
        }

        AtualizarControloDeValor(linha, subtipo!);
    }

    /// <summary>Escolhe e configura, para o campo indicado (o Subtipo escolhido), qual dos três
    /// controlos de valor mostrar:
    /// - <see cref="LinhaFiltroUi.CmbValorSugerido"/>, quando o campo tem valores sugeridos
    ///   conhecidos (ex.: Tipo de Disco → SSD/HDD/NVMe) — comparador restrito a "="/"≠";
    /// - <see cref="LinhaFiltroUi.CmbValorBool"/>, quando o campo é Booleano (Sim/Não);
    /// - <see cref="LinhaFiltroUi.TxtValor"/>, caso contrário — com o comparador completo
    ///   consoante o tipo de dado (ver <see cref="PesquisaAvancadaService.ComparadoresPorTipo"/>).</summary>
    private void AtualizarControloDeValor(LinhaFiltroUi linha, CampoPesquisavel<Equipamento> campo)
    {
        linha.CmbComparadorValor.Visibility = Visibility.Visible;

        var temValoresSugeridos = campo.ValoresSugeridos is { Length: > 0 };
        var ehBooleano = !temValoresSugeridos && campo.Tipo == TipoDadoCampoPesquisa.Booleano;

        linha.CmbValorSugerido.Visibility = temValoresSugeridos ? Visibility.Visible : Visibility.Collapsed;
        linha.CmbValorBool.Visibility = ehBooleano ? Visibility.Visible : Visibility.Collapsed;
        linha.TxtValor.Visibility = !temValoresSugeridos && !ehBooleano ? Visibility.Visible : Visibility.Collapsed;

        if (temValoresSugeridos)
        {
            linha.CmbValorSugerido.ItemsSource = campo.ValoresSugeridos;
            if (linha.CmbValorSugerido.SelectedIndex < 0) linha.CmbValorSugerido.SelectedIndex = 0;
            linha.CmbComparadorValor.ItemsSource = ComparadoresValorSugerido;
            if (linha.CmbComparadorValor.SelectedIndex < 0) linha.CmbComparadorValor.SelectedIndex = 0;
            return;
        }

        if (ehBooleano)
        {
            if (linha.CmbValorBool.SelectedIndex < 0) linha.CmbValorBool.SelectedIndex = 0;
            linha.CmbComparadorValor.ItemsSource = PesquisaAvancadaService.ComparadoresPorTipo[TipoDadoCampoPesquisa.Booleano];
            linha.CmbComparadorValor.SelectedIndex = 0;
            return;
        }

        var comparadorAnterior = linha.CmbComparadorValor.SelectedItem as string;
        var comparadoresValidos = PesquisaAvancadaService.ComparadoresPorTipo[campo.Tipo];
        linha.CmbComparadorValor.ItemsSource = comparadoresValidos;
        linha.CmbComparadorValor.SelectedItem = comparadorAnterior != null && comparadoresValidos.Contains(comparadorAnterior)
            ? comparadorAnterior
            : comparadoresValidos.FirstOrDefault();

        linha.TxtValor.ToolTip = campo.Tipo switch
        {
            TipoDadoCampoPesquisa.Data => "Formato: AAAA-MM-DD (ex.: 2026-08-15)",
            TipoDadoCampoPesquisa.Numero => "Valor numérico (ex.: 8 ou 8.5)",
            _ => null
        };
    }

    /// <summary>Traduz cada linha de filtro em 0, 1 ou 2 <see cref="FiltroPesquisa{T}"/> (0 se ainda
    /// incompleta, 2 quando há um Subtipo escolhido — as duas condições combinam-se com E através
    /// do motor partilhado, sem precisar de nenhuma lógica especial em
    /// <see cref="PesquisaAvancadaService.Aplicar{T}"/>).</summary>
    private List<FiltroPesquisa<Equipamento>> ObterFiltros()
    {
        var resultado = new List<FiltroPesquisa<Equipamento>>();
        var campoTipo = _todosOsCampos.FirstOrDefault(c => c.Chave == "Tipo");

        foreach (var l in _linhas)
        {
            if (l.CmbTipo.SelectedItem is not PesquisaAvancadaService.TipoEquipamentoPesquisavel tipoEscolhido)
                continue; // linha ainda sem tipo escolhido — incompleta, ignorada

            resultado.Add(new FiltroPesquisa<Equipamento>
            {
                Campo = campoTipo,
                Comparador = l.CmbComparadorTipo.SelectedItem as string ?? "=",
                Valor = tipoEscolhido.Nome
            });

            var subtipo = l.CmbSubtipo.SelectedItem as CampoPesquisavel<Equipamento>;
            if (subtipo == null || subtipo.Chave == SubtipoVazio.Chave) continue;

            var temValoresSugeridos = subtipo.ValoresSugeridos is { Length: > 0 };
            var ehBooleano = !temValoresSugeridos && subtipo.Tipo == TipoDadoCampoPesquisa.Booleano;

            resultado.Add(new FiltroPesquisa<Equipamento>
            {
                Campo = subtipo,
                Comparador = l.CmbComparadorValor.SelectedItem as string,
                Valor = temValoresSugeridos ? l.CmbValorSugerido.SelectedItem as string
                    : ehBooleano ? l.CmbValorBool.SelectedItem as string
                    : l.TxtValor.Text
            });
        }

        return resultado;
    }

    private void Pesquisar_Click(object sender, RoutedEventArgs e)
    {
        var filtros = ObterFiltros();
        _resultado = PesquisaAvancadaService.Aplicar(_todosEquipamentos, filtros);

        Grid.ItemsSource = _resultado.Select(eq => new LinhaResultado
        {
            Tipo = eq.Tipo ?? "",
            MarcaModelo = string.Join(" ", new[] { eq.Marca, eq.Modelo }.Where(s => !string.IsNullOrWhiteSpace(s))),
            NumeroSerie = eq.NumeroSerie,
            EscolaOuLocal = eq.Escola?.Nome ?? eq.LocalNaoEscolar ?? "",
            Estado = eq.Estado
        }).ToList();

        TxtResultado.Text = _resultado.Count switch
        {
            0 => "Nenhum equipamento corresponde aos filtros indicados.",
            1 => "1 equipamento encontrado.",
            _ => $"{_resultado.Count} equipamentos encontrados."
        };

        BtnGerarPdf.IsEnabled = _resultado.Count > 0;
    }

    private void GerarPdf_Click(object sender, RoutedEventArgs e)
    {
        // O botão já fica desativado sem resultados (ver Pesquisar_Click); esta verificação é só
        // uma segunda salvaguarda, tal como nos relatórios de módulo (item 1.1).
        if (_resultado.Count == 0)
        {
            MessageBox.Show("Pesquise primeiro — não há resultados para incluir no relatório.",
                "Sem dados para o relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Guardar pesquisa avançada de equipamento",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Pesquisa_Avancada_Equipamento_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var descricaoFiltros = PesquisaAvancadaService.Descrever(ObterFiltros());
            var servico = new RelatorioService(App.Db);
            servico.GerarListaEquipamento(dialog.FileName, _resultado.Select(eq => eq.Id).ToList(),
                tituloPersonalizado: "Pesquisa Avançada de Equipamento",
                subtituloPersonalizado: $"Filtros aplicados: {descricaoFiltros}");

            var abrir = MessageBox.Show("Relatório PDF gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
