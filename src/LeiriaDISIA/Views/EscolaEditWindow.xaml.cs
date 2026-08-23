using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class EscolaEditWindow : Window
{
    private readonly Escola? _escolaExistente;
    private readonly int? _agrupamentoPreSelecionado;
    private string? _imagemCaminhoAtual;
    private bool _imagemRemovida;

    public bool Sucesso { get; private set; }

    /// <param name="escola">Escola a editar; ou null para criar uma nova escola.</param>
    /// <param name="agrupamentoPreSelecionado">Agrupamento sugerido quando se cria uma escola nova a partir do módulo de Agrupamentos.</param>
    public EscolaEditWindow(Escola? escola, int? agrupamentoPreSelecionado = null)
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

        _escolaExistente = escola;
        _agrupamentoPreSelecionado = agrupamentoPreSelecionado;

        var agrupamentosDisponiveis = new List<Agrupamento> { new() { Id = 0, Nome = "(Sem Agrupamento)" } };
        agrupamentosDisponiveis.AddRange(App.Db.Agrupamentos.OrderBy(a => a.Nome));
        CmbAgrupamento.ItemsSource = agrupamentosDisponiveis;

        // Carrega os tipos de escola dos Valores Fixos
        var tiposEscola = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.TipoEscola && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToList();
        CmbTipo.ItemsSource = tiposEscola;
        CmbTipo.SelectionChanged += CmbTipo_SelectionChanged;

        // Carrega os estados de escola dos Valores Fixos (Administração → Dados Fixos). Vêm por
        // Ordem (e não alfabética) para manter "Ativa" sempre como primeira opção da lista.
        var estadosEscola = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.EstadoEscola && v.Ativo)
            .OrderBy(v => v.Ordem)
            .Select(v => v.Valor)
            .ToList();
        CmbEstado.ItemsSource = estadosEscola;

        // Carrega as velocidades de fibra dos Valores Fixos (Administração → Dados Fixos)
        var velocidadesFibra = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.VelocidadeFibra && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToList();
        CmbVelocidadeFibra.ItemsSource = velocidadesFibra;
        ChkFibra.Checked += (_, _) => AtualizarEstadoVelocidadeFibra();
        ChkFibra.Unchecked += (_, _) => AtualizarEstadoVelocidadeFibra();

        if (escola == null)
        {
            TxtTitulo.Text = "Nova Escola";
            TxtCodEscola.Text = "(atribuído automaticamente ao gravar)";
            CmbTipo.SelectedItem = tiposEscola.FirstOrDefault() ?? "EB1"; // Seleciona o primeiro tipo disponível
            CmbEstado.SelectedItem = estadosEscola.FirstOrDefault(v => v == EstadosEscola.Ativa) ?? estadosEscola.FirstOrDefault();
            ChkIntegrado.IsChecked = false;
            AtualizarEstadoIntegrado();
            AtualizarEstadoVelocidadeFibra();
            CmbAgrupamento.SelectedItem = _agrupamentoPreSelecionado != null
                ? agrupamentosDisponiveis.FirstOrDefault(a => a.Id == _agrupamentoPreSelecionado)
                : agrupamentosDisponiveis[0];

            // Escola ainda não existe, por isso não há equipamento para listar.
            GridEquipamento.Visibility = Visibility.Collapsed;
            TxtTotalEquipamento.Text = "0 equipamentos";
            TxtSemEquipamento.Text = "Grave a escola primeiro; depois de criada, o equipamento associado aparece aqui.";
            TxtSemEquipamento.Visibility = Visibility.Visible;
            MapaEscola.Visibility = Visibility.Collapsed;
            TxtMapaIndisponivel.Visibility = Visibility.Visible;

            // Planeamento de Rotas: só faz sentido geocodificar uma escola que já exista (precisa
            // de um Id para gravar o resultado) — fica disponível depois de gravar pela primeira vez.
            BtnRecalcularDistancia.IsEnabled = false;
            TxtDistanciaSede.Text = "disponível depois de gravar a escola";
            return;
        }

        TxtTitulo.Text = $"Editar Escola — {escola.Nome}";
        TxtCodEscola.Text = escola.CodEscola;
        TxtCodDgrhe.Text = escola.CodDGRHE?.ToString();
        TxtCodGepe.Text = escola.CodGEPE?.ToString();
        TxtNome.Text = escola.Nome;
        TxtNomeAlternativo.Text = escola.NomeAlternativo;
        TxtMorada.Text = escola.Morada;
        TxtCodigoPostal.Text = escola.CodigoPostal;
        TxtLocalidade.Text = escola.Localidade;
        TxtFreguesia.Text = escola.Freguesia;
        AtualizarPainelDistancia(escola);
        TxtTelefone.Text = escola.Telefone;
        TxtEmail.Text = escola.Email;
        CmbAgrupamento.SelectedItem = agrupamentosDisponiveis.FirstOrDefault(a => a.Id == (escola.AgrupamentoId ?? 0));
        CmbTipo.SelectedItem = escola.Tipo;
        TxtNumAlunos.Text = escola.NumeroAlunos?.ToString();
        TxtNumSalas.Text = escola.NumeroSalas?.ToString();
        ChkFibra.IsChecked = escola.TemInternetFibra;
        CmbVelocidadeFibra.Text = escola.VelocidadeFibra;
        AtualizarEstadoVelocidadeFibra();
        ChkCCTV.IsChecked = escola.TemCCTV;
        ChkVPN.IsChecked = escola.TemVPN;
        ChkBiblioteca.IsChecked = escola.TemBiblioteca;
        CmbEstado.SelectedItem = escola.Estado;
        if (CmbEstado.SelectedItem == null)
        {
            // A escola tem um estado que já não existe em Dados Fixos (ex.: foi renomeado ou
            // eliminado por engano) — mostra-se na mesma para o utilizador não perder a informação,
            // acrescentando-o temporariamente à lista.
            CmbEstado.ItemsSource = estadosEscola.Append(escola.Estado).ToList();
            CmbEstado.SelectedItem = escola.Estado;
        }
        ChkIntegrado.IsChecked = escola.Integrado;
        AtualizarEstadoIntegrado();
        TxtObservacoes.Text = escola.Observacoes;
        TxtLatitude.Text = escola.Latitude?.ToString("F6", CultureInfo.InvariantCulture);
        TxtLongitude.Text = escola.Longitude?.ToString("F6", CultureInfo.InvariantCulture);
        _imagemCaminhoAtual = escola.ImagemCaminho;
        CarregarImagemPreview(_imagemCaminhoAtual);
        CarregarEquipamento(escola.Id);
        _ = AtualizarMapaAsync();
    }

    /// <summary>Carrega, na coluna da direita, o equipamento informático associado a esta escola.</summary>
    private void CarregarEquipamento(int escolaId)
    {
        var lista = App.Db.Equipamentos
            .Where(eq => eq.EscolaId == escolaId)
            .OrderBy(eq => eq.Tipo)
            .ThenBy(eq => eq.Marca)
            .ToList();

        GridEquipamento.ItemsSource = lista;
        TxtTotalEquipamento.Text = lista.Count == 1 ? "1 equipamento" : $"{lista.Count} equipamentos";
        TxtSemEquipamento.Visibility = lista.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GridEquipamento.Visibility = lista.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AtualizarMapa_Click(object sender, RoutedEventArgs e) => _ = AtualizarMapaAsync();

    /// <summary>
    /// Mostra a localização da escola num mapa incorporado do Google Maps: usa as coordenadas
    /// (Latitude/Longitude) se estiverem preenchidas, caso contrário usa a Morada/Localidade/Freguesia.
    /// </summary>
    private async Task AtualizarMapaAsync()
    {
        var url = ConstruirUrlMapa();
        if (url == null)
        {
            MapaEscola.Visibility = Visibility.Collapsed;
            TxtMapaIndisponivel.Text = "Sem coordenadas ou morada suficientes para mostrar o mapa.";
            TxtMapaIndisponivel.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            await MapaEscola.EnsureCoreWebView2Async();

            // A API de "embed" do Google Maps exige explicitamente que o URL seja carregado
            // dentro de um <iframe> - navegar diretamente para o URL (Navigate) faz o Google
            // mostrar o erro "The Google Maps Embed API must be used in an iframe.", porque o
            // conteúdo do WebView2 é, por si só, a janela de topo. A solução é envolver o URL
            // num pequeno documento HTML com um iframe lá dentro, e carregar esse HTML.
            var urlParaAtributoHtml = url.Replace("&", "&amp;").Replace("\"", "&quot;");
            var html = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8" />
                    <style>
                        html, body { margin: 0; padding: 0; height: 100%; overflow: hidden; }
                        iframe { width: 100%; height: 100%; border: 0; }
                    </style>
                </head>
                <body>
                    <iframe src="{{urlParaAtributoHtml}}" allowfullscreen loading="lazy"></iframe>
                </body>
                </html>
                """;

            MapaEscola.CoreWebView2.NavigateToString(html);
            TxtMapaIndisponivel.Visibility = Visibility.Collapsed;
            MapaEscola.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            // Acontece, por exemplo, se o WebView2 Runtime não estiver instalado no computador
            // ou não houver ligação à internet - mostra-se um aviso em vez de deixar em branco.
            MapaEscola.Visibility = Visibility.Collapsed;
            TxtMapaIndisponivel.Text = "Não foi possível carregar o mapa.\n" +
                "Verifique a ligação à internet ou se o \"WebView2 Runtime\" está instalado neste computador.\n\n" +
                $"({ex.Message})";
            TxtMapaIndisponivel.Visibility = Visibility.Visible;
        }
    }

    private string? ConstruirUrlMapa()
    {
        if (double.TryParse(TxtLatitude.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(TxtLongitude.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
        {
            return $"https://maps.google.com/maps?q={lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}&z=16&output=embed";
        }

        var endereco = string.Join(", ", new[] { TxtMorada.Text, TxtLocalidade.Text, TxtFreguesia.Text, "Leiria" }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return string.IsNullOrWhiteSpace(endereco) ? null : $"https://maps.google.com/maps?q={Uri.EscapeDataString(endereco)}&z=15&output=embed";
    }

    private static bool IsJardimInfancia(string? tipo) =>
        tipo != null && tipo.Contains("Jardim", StringComparison.OrdinalIgnoreCase);

    private void AtualizarEstadoIntegrado()
    {
        var isJi = IsJardimInfancia(CmbTipo.SelectedItem as string);
        ChkIntegrado.IsEnabled = isJi;
        if (!isJi) ChkIntegrado.IsChecked = false;
    }

    private void CmbTipo_SelectionChanged(object sender, SelectionChangedEventArgs e) => AtualizarEstadoIntegrado();

    private void AtualizarEstadoVelocidadeFibra()
    {
        var temFibra = ChkFibra.IsChecked == true;
        PanelVelocidadeFibra.Visibility = temFibra ? Visibility.Visible : Visibility.Collapsed;
        CmbVelocidadeFibra.IsEnabled = temFibra;
        if (!temFibra) CmbVelocidadeFibra.Text = string.Empty;
    }

    private void CarregarImagemPreview(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
        {
            ImgEscola.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(caminho, UriKind.Absolute);
            bitmap.EndInit();
            ImgEscola.Source = bitmap;
        }
        catch
        {
            ImgEscola.Source = null;
        }
    }

    private void EscolherImagem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher fotografia da escola",
            Filter = "Imagens (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var pastaDestino = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeiriaDISIA", "Imagens", "Escolas");
            Directory.CreateDirectory(pastaDestino);

            var nomeFicheiro = $"{Guid.NewGuid():N}{Path.GetExtension(dialog.FileName)}";
            var destino = Path.Combine(pastaDestino, nomeFicheiro);
            File.Copy(dialog.FileName, destino, overwrite: true);

            _imagemCaminhoAtual = destino;
            _imagemRemovida = false;
            CarregarImagemPreview(destino);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar a imagem:\n{ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoverImagem_Click(object sender, RoutedEventArgs e)
    {
        _imagemCaminhoAtual = null;
        _imagemRemovida = true;
        ImgEscola.Source = null;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text) || CmbAgrupamento.SelectedItem is not Agrupamento agrupamentoSelecionado)
        {
            MessageBox.Show("Indique pelo menos o nome da escola.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(CmbTipo.SelectedItem as string ?? CmbTipo.Text))
        {
            MessageBox.Show("Selecione o tipo da escola.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var todasAsEscolas = App.Db.Escolas.ToList();
        var possivelDuplicado = todasAsEscolas.FirstOrDefault(e =>
            (_escolaExistente == null || e.Id != _escolaExistente.Id) &&
            TextNormalizer.AreLikelySameSchool(e.Nome, TxtNome.Text));

        if (possivelDuplicado != null)
        {
            var continuar = MessageBox.Show(
                $"Já existe uma escola com nome muito semelhante: '{possivelDuplicado.Nome}'.\n" +
                "Pode tratar-se da mesma escola com nome diferente.\n\nDeseja continuar e guardar mesmo assim?",
                "Possível escola duplicada", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (continuar != MessageBoxResult.Yes) return;
        }

        int? codDgrhe = int.TryParse(TxtCodDgrhe.Text, out var d) ? d : null;
        int? codGepe = int.TryParse(TxtCodGepe.Text, out var g) ? g : null;
        int? numAlunos = int.TryParse(TxtNumAlunos.Text, out var na) ? na : null;
        int? numSalas = int.TryParse(TxtNumSalas.Text, out var ns) ? ns : null;

        Escola escola;
        if (_escolaExistente == null)
        {
            escola = new Escola { CodEscola = CodigoEscolaService.ProximoCodigo(App.Db, CmbTipo.SelectedItem as string) };
            App.Db.Escolas.Add(escola);
        }
        else
        {
            escola = App.Db.Escolas.First(x => x.Id == _escolaExistente.Id);
        }

        escola.CodDGRHE = codDgrhe;
        escola.CodGEPE = codGepe;
        escola.Nome = TxtNome.Text.Trim();
        escola.NomeAlternativo = string.IsNullOrWhiteSpace(TxtNomeAlternativo.Text) ? null : TxtNomeAlternativo.Text.Trim();
        escola.Morada = TxtMorada.Text;
        escola.CodigoPostal = string.IsNullOrWhiteSpace(TxtCodigoPostal.Text) ? null : TxtCodigoPostal.Text.Trim();
        escola.Localidade = TxtLocalidade.Text;
        escola.Freguesia = TxtFreguesia.Text;
        escola.Telefone = string.IsNullOrWhiteSpace(TxtTelefone.Text) ? null : TxtTelefone.Text.Trim();
        escola.Email = string.IsNullOrWhiteSpace(TxtEmail.Text) ? null : TxtEmail.Text.Trim();
        escola.AgrupamentoId = agrupamentoSelecionado.Id == 0 ? null : agrupamentoSelecionado.Id;
        escola.Tipo = CmbTipo.SelectedItem?.ToString() ?? "EB1";
        escola.NumeroAlunos = numAlunos;
        escola.NumeroSalas = numSalas;
        escola.TemInternetFibra = ChkFibra.IsChecked == true;
        escola.VelocidadeFibra = ChkFibra.IsChecked == true && !string.IsNullOrWhiteSpace(CmbVelocidadeFibra.Text)
            ? CmbVelocidadeFibra.Text.Trim() : null;
        escola.TemCCTV = ChkCCTV.IsChecked == true;
        escola.TemVPN = ChkVPN.IsChecked == true;
        escola.TemBiblioteca = ChkBiblioteca.IsChecked == true;
        escola.Estado = CmbEstado.SelectedItem as string ?? EstadosEscola.Ativa;
        escola.Integrado = IsJardimInfancia(CmbTipo.SelectedItem as string) && ChkIntegrado.IsChecked == true;
        escola.Observacoes = TxtObservacoes.Text;

        // As coordenadas são sempre gravadas com ponto decimal (CultureInfo.InvariantCulture),
        // independentemente da configuração regional do Windows. Se o texto vier com vírgula
        // (por exemplo colado diretamente do Google Maps num sistema em português, ou escrito
        // no teclado numérico), a vírgula é tratada como separador decimal e normalizada para
        // ponto antes de interpretar — nunca como separador de milhares (por isso usa-se
        // NumberStyles.Float em vez de Any), para não corromper o valor gravado.
        var latTexto = TxtLatitude.Text?.Trim().Replace(',', '.');
        var lonTexto = TxtLongitude.Text?.Trim().Replace(',', '.');

        escola.Latitude = double.TryParse(latTexto, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var lat) ? lat : null;
        escola.Longitude = double.TryParse(lonTexto, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var lon) ? lon : null;

        if (_imagemRemovida)
            escola.ImagemCaminho = null;
        else if (!string.IsNullOrWhiteSpace(_imagemCaminhoAtual))
            escola.ImagemCaminho = _imagemCaminhoAtual;

        App.Db.SaveChanges();
        Sucesso = true;
        Close();
    }

    private void TxtMorada_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Não recalcula nada sozinho (isso só acontece por ação explícita do botão) — só avisa que
        // a distância guardada pode já não corresponder à morada atual.
        if (_escolaExistente?.DistanciaKmSede != null)
            TxtDataCalculoDistancia.Text = "⚠ a morada foi alterada — pode já não corresponder à distância acima";
    }

    private void AtualizarPainelDistancia(Escola escola)
    {
        if (escola.DistanciaKmSede is { } distancia)
        {
            TxtDistanciaSede.Text = $"{distancia:0.#} km";
            TxtDataCalculoDistancia.Text = escola.DataUltimoCalculoDistancia is { } data
                ? $"calculada em {data:dd/MM/yyyy HH:mm}"
                : "";
        }
        else
        {
            TxtDistanciaSede.Text = "ainda não calculada";
            TxtDataCalculoDistancia.Text = "";
        }
    }

    private async void RecalcularDistancia_Click(object sender, RoutedEventArgs e)
    {
        if (_escolaExistente == null) return;

        // Coordenadas GPS exatas (fornecidas diretamente pelo utilizador — ex.: coladas do Google
        // Maps) são uma fonte muito mais fiável do que geocodificar a partir do texto da morada,
        // que pode ser ambíguo (o mesmo nome de rua existe, por vezes, em vários concelhos —
        // ver o comentário completo em EscolaGeocodingService.RecalcularAPartirDeCoordenadasAsync).
        // Por isso: se já houver coordenadas válidas nas caixas (mesmo que ainda não gravadas),
        // usa-se sempre esse caminho primeiro, e só se recorre à morada quando não há coordenadas
        // nenhumas para usar. Lê-se das CAIXAS DE TEXTO (não da Escola gravada) porque é
        // precisamente aqui — colar/escrever coordenadas antes de calcular — que o utilizador as
        // fornece; a morada, pelo contrário, continua a ler-se sempre da versão já gravada (ver
        // comentário mais abaixo), para não geocodificar um valor ainda por confirmar.
        var latTexto = TxtLatitude.Text?.Trim().Replace(',', '.');
        var lonTexto = TxtLongitude.Text?.Trim().Replace(',', '.');
        // Inicializadas a 0 (em vez de "out var") só para satisfazer a análise de atribuição
        // definitiva do compilador: ele não consegue provar, mais abaixo, que "temCoordenadas"
        // true implica que ambos os TryParse correram (essa informação perde-se ao guardar o
        // resultado do "&&" numa variável à parte) — o valor 0 nunca chega a ser usado, porque só
        // se lê latCoordenada/lonCoordenada quando temCoordenadas é true.
        double latCoordenada = 0, lonCoordenada = 0;
        var temCoordenadas =
            double.TryParse(latTexto, NumberStyles.Float, CultureInfo.InvariantCulture, out latCoordenada) &&
            double.TryParse(lonTexto, NumberStyles.Float, CultureInfo.InvariantCulture, out lonCoordenada);

        if (!temCoordenadas && string.IsNullOrWhiteSpace(_escolaExistente.Morada))
        {
            MessageBox.Show(
                "Esta escola ainda não tem morada nem coordenadas preenchidas. Preencha a morada (ou a Latitude/Longitude), " +
                "grave a escola, e só depois recalcule a distância.",
                "Morada em falta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnRecalcularDistancia.IsEnabled = false;
        TxtEstadoRecalculo.Visibility = Visibility.Visible;
        TxtEstadoRecalculo.Text = "A calcular, aguarde…";
        try
        {
            var servico = new LeiriaDISIA.Services.Rotas.EscolaGeocodingService(App.Db);

            (bool Sucesso, string? Erro) resultado;
            if (temCoordenadas)
            {
                var coordenada = new LeiriaDISIA.Services.Rotas.CoordenadaGeografica(latCoordenada, lonCoordenada);
                resultado = await servico.RecalcularAPartirDeCoordenadasAsync(_escolaExistente, coordenada);
            }
            else
            {
                // A morada usada é sempre a gravada na base de dados (não a que possa estar a meio
                // de edição na caixa de texto e ainda não guardada) — evita geocodificar um valor
                // que o utilizador pode ainda vir a descartar sem gravar.
                resultado = await servico.RecalcularAsync(_escolaExistente);
            }

            if (!resultado.Sucesso)
            {
                MessageBox.Show(resultado.Erro ?? "Não foi possível calcular a distância.", "Erro ao calcular distância",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            App.Db.SaveChanges();

            TxtLatitude.Text = _escolaExistente.Latitude?.ToString("F6", CultureInfo.InvariantCulture);
            TxtLongitude.Text = _escolaExistente.Longitude?.ToString("F6", CultureInfo.InvariantCulture);
            // Só relevante no fluxo por coordenadas (RecalcularAPartirDeCoordenadasAsync pode ter
            // preenchido a morada/código postal/localidade por geocodificação inversa); no fluxo
            // por morada, estes três já estavam corretos (é a fonte que geocodificou), por isso
            // atualizá-los aqui não muda nada visualmente nesse caso.
            TxtMorada.Text = _escolaExistente.Morada;
            TxtCodigoPostal.Text = _escolaExistente.CodigoPostal;
            TxtLocalidade.Text = _escolaExistente.Localidade;
            AtualizarPainelDistancia(_escolaExistente);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro inesperado ao calcular a distância:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRecalcularDistancia.IsEnabled = true;
            TxtEstadoRecalculo.Visibility = Visibility.Collapsed;
        }
    }
}
