using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services.Rotas;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

/// <summary>Wrapper de um <see cref="PedidoParaPlaneamento"/> com um estado de seleção mutável, para
/// a caixa de verificação da grelha (<see cref="Selecionado"/>). Sem <c>INotifyPropertyChanged</c>
/// de propósito — o binding do <c>DataGridCheckBoxColumn</c> funciona com um simples setter público,
/// e nada mais na janela precisa de reagir em tempo real ao alternar da caixa (só é lido quando o
/// utilizador clica em "Calcular Rota"), seguindo o padrão de code-behind já usado no resto da
/// aplicação em vez de introduzir MVVM só aqui.</summary>
public class PedidoSelecionavel
{
    public PedidoParaPlaneamento Origem { get; }
    public PedidoIntervencao Pedido => Origem.Pedido;
    public string? Bloqueio => Origem.Bloqueio;
    public bool Selecionado { get; set; }

    /// <summary>Texto curto do badge de estado, em vez da frase completa de
    /// <see cref="Bloqueio"/> (essa continua disponível como tooltip da célula, para quem quiser o
    /// motivo exato). Reconhece o motivo pelo conteúdo da mensagem devolvida pelo serviço — se o
    /// texto dessas mensagens mudar em <see cref="PlaneamentoRotaService.ObterPedidosParaPlaneamento"/>,
    /// atualizar também aqui.</summary>
    public string EstadoCurto => Bloqueio switch
    {
        null => "Coordenadas OK",
        var b when b.Contains("morada", StringComparison.OrdinalIgnoreCase) => "Sem Morada",
        var b when b.Contains("coordenadas", StringComparison.OrdinalIgnoreCase) => "Faltam Coordenadas",
        var b when b.Contains("escola associada", StringComparison.OrdinalIgnoreCase) => "Sem Escola",
        var b when b.Contains("concluída", StringComparison.OrdinalIgnoreCase) => "Já Concluído",
        var b when b.Contains("já está incluído", StringComparison.OrdinalIgnoreCase) => "Já Planeado",
        _ => "Indisponível"
    };

    /// <summary>Verde para elegível; laranja/âmbar para "já planeado/concluído noutro dia" — não é
    /// bem um erro, é só informação de que o pedido já está tratado — e só o vermelho fica
    /// reservado para problemas reais que impedem calcular a rota (sem morada/coordenadas/escola).</summary>
    public string CorEstado => Bloqueio switch
    {
        null => "#22C55E",
        var b when b.Contains("já está incluído", StringComparison.OrdinalIgnoreCase)
                   || b.Contains("concluída", StringComparison.OrdinalIgnoreCase) => "#F59E0B",
        _ => "#EF4444"
    };

    /// <summary>Só true quando o pedido está bloqueado por pertencer a um plano ainda não
    /// realizado — permite à grelha mostrar o atalho "↺ Repor" só nesse caso.</summary>
    public bool PodeRepor => Origem.PlanoRotaIdCancelavel != null;

    public PedidoSelecionavel(PedidoParaPlaneamento origem) => Origem = origem;
}

/// <summary>Objeto de exibição só para a tabela "Pré-visualização da rota" — permite acrescentar,
/// no fim da lista, uma linha visível de regresso à sede sem misturar isso com
/// <see cref="ParagemPreVisualizacao"/> (que representa só paragens reais/pedidos). Ver
/// PlanearRotaWindow.MostrarPreVisualizacao.</summary>
public class LinhaParagemExibicao
{
    public string Ordem { get; init; } = "";
    public string EscolaNome { get; init; } = "";
    public double DistanciaDesdeAnteriorKm { get; init; }
    public int DuracaoDesdeAnteriorMinutos { get; init; }
    public bool EhRegresso { get; init; }
}

/// <summary>
/// Módulo Pedidos → Planeamento de Rota (ver documento de especificação "planeamento de
/// deslocações"). Fluxo: escolher data → selecionar pedidos elegíveis → calcular rota otimizada
/// (OpenRouteService) → rever pré-visualização → confirmar (grava o plano e gera o PDF).
///
/// Nunca altera <see cref="PedidoIntervencao.Estado"/> nem faz qualquer chamada externa só por a
/// janela abrir — todas as chamadas de rede acontecem em resposta a uma ação explícita do
/// utilizador ("Calcular Rota" ou "Confirmar e Guardar").
/// </summary>
public partial class PlanearRotaWindow : Window
{
    private readonly PlaneamentoRotaService _planeamento = new(App.Db);
    private PreVisualizacaoRota? _preVisualizacaoAtual;
    private bool _aTrabalhar;

    public PlanearRotaWindow()
    {
        InitializeComponent();
        Closing += Window_Closing;
        DpData.SelectedDate = DateTime.Today.AddDays(1); // sugestão razoável: planear para amanhã
        CarregarPedidos();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_aTrabalhar) return;
        e.Cancel = true;
        MessageBox.Show("Aguarde a conclusão da operação em curso antes de fechar esta janela.",
            "A trabalhar…", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Cancela o plano de rota (ainda não realizado) que está a bloquear este pedido, para
    /// ele voltar a ficar disponível para um novo planeamento — atalho para o caso comum de um
    /// plano ter sido criado mas, por alguma razão, a equipa não ter chegado a sair (ver botão
    /// "↺ Repor" na coluna Estado, só visível quando <see cref="PedidoSelecionavel.PodeRepor"/> é
    /// verdadeiro).</summary>
    private async void RepordPedido_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PedidoSelecionavel item } || item.Origem.PlanoRotaIdCancelavel is not { } planoId)
            return;

        var confirmar = MessageBox.Show(
            $"O pedido \"{item.Pedido.Escola?.Nome}\" está incluído num plano de rota que ainda não foi marcado como realizado.\n\n" +
            "Cancelar esse plano para poder voltar a incluir este pedido numa nova rota?",
            "Repor pedido", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmar != MessageBoxResult.Yes) return;

        DefinirATrabalhar(true, "A cancelar plano anterior…");
        try
        {
            var (sucesso, erro) = await new PlaneamentoRotaService(App.Db).CancelarPlanoAsync(planoId);
            if (!sucesso)
            {
                MessageBox.Show(erro ?? "Não foi possível cancelar o plano.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            CarregarPedidos();
        }
        finally
        {
            DefinirATrabalhar(false, null);
        }
    }

    private void DpData_SelectedDateChanged(object sender, EventArgs e) => CarregarPedidos();

    /// <summary>Permite corrigir logo aqui a escola de um pedido que não tenha coordenadas,
    /// sem obrigar o utilizador a abandonar o planeamento da rota.</summary>
    private void GridPedidos_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridPedidos.SelectedItem is not PedidoSelecionavel { Pedido.Escola: { } escola }) return;

        var janela = new EscolaEditWindow(escola) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) CarregarPedidos();
    }

    private void CarregarPedidos()
    {
        if (DpData.SelectedDate is not { } data) return;

        GridPedidos.ItemsSource = _planeamento.ObterPedidosParaPlaneamento(data)
            .Select(p => new PedidoSelecionavel(p))
            .ToList();

        // Mudar de data invalida qualquer pré-visualização anterior (foi calculada para outro dia).
        LimparPreVisualizacao();
    }

    private void LimparPreVisualizacao()
    {
        _preVisualizacaoAtual = null;
        PainelResumo.Visibility = Visibility.Collapsed;
        PainelAvisos.Visibility = Visibility.Collapsed;
        GridParagens.ItemsSource = null;
        BtnConfirmar.IsEnabled = false;
    }

    private async void CalcularRota_Click(object sender, RoutedEventArgs e)
    {
        if (DpData.SelectedDate is not { } data)
        {
            MessageBox.Show("Escolha a data da rota.", "Data em falta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selecionados = (GridPedidos.ItemsSource as List<PedidoSelecionavel> ?? new())
            .Where(p => p.Selecionado)
            .ToList();

        if (selecionados.Count == 0)
        {
            MessageBox.Show("Selecione pelo menos um pedido elegível.", "Nenhum pedido selecionado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (selecionados.Any(p => !p.Origem.PodeSerSelecionado))
        {
            MessageBox.Show(
                "Um ou mais pedidos selecionados não estão elegíveis (ver coluna \"Estado\"). Desmarque-os antes de calcular a rota.",
                "Pedidos não elegíveis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TimeSpan.TryParseExact(TxtHoraPartida.Text, @"hh\:mm", CultureInfo.InvariantCulture, out _))
        {
            MessageBox.Show("A hora de partida deve estar no formato HH:mm (ex.: 09:00).", "Hora inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        decimal? limiteHoras = null;
        if (!string.IsNullOrWhiteSpace(TxtLimiteHoras.Text))
        {
            if (!decimal.TryParse(TxtLimiteHoras.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var limite) || limite <= 0)
            {
                MessageBox.Show("O limite de horas da equipa deve ser um número positivo (ex.: 7 ou 7,5).", "Valor inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            limiteHoras = limite;
        }

        DefinirATrabalhar(true, "A geocodificar/otimizar a rota, aguarde…");
        try
        {
            var pedidos = selecionados.Select(p => p.Pedido).ToList();
            _preVisualizacaoAtual = await _planeamento.CalcularRotaAsync(pedidos, ChkRegressarSede.IsChecked == true, limiteHoras);

            if (!_preVisualizacaoAtual.Sucesso)
            {
                MessageBox.Show(_preVisualizacaoAtual.Erro ?? "Não foi possível calcular a rota.",
                    "Erro ao calcular rota", MessageBoxButton.OK, MessageBoxImage.Error);
                LimparPreVisualizacao();
                return;
            }

            MostrarPreVisualizacao(_preVisualizacaoAtual);

            // A geocodificação de escolas que ainda não tinham coordenadas foi gravada dentro de
            // CalcularRotaAsync — recarrega a grelha da esquerda para refletir a distância à sede
            // já calculada (informação só de leitura ali, mas útil ver atualizada).
            CarregarPedidosSemLimparPreVisualizacao();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro inesperado ao calcular a rota:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DefinirATrabalhar(false, null);
        }
    }

    private void CarregarPedidosSemLimparPreVisualizacao()
    {
        if (DpData.SelectedDate is not { } data) return;
        var selecionadosAntes = (GridPedidos.ItemsSource as List<PedidoSelecionavel> ?? new())
            .Where(p => p.Selecionado).Select(p => p.Pedido.Id).ToHashSet();

        var nova = _planeamento.ObterPedidosParaPlaneamento(data).Select(p => new PedidoSelecionavel(p)
        {
            Selecionado = selecionadosAntes.Contains(p.Pedido.Id)
        }).ToList();

        GridPedidos.ItemsSource = nova;
    }

    private void MostrarPreVisualizacao(PreVisualizacaoRota preVisualizacao)
    {
        PainelResumo.Visibility = Visibility.Visible;
        TxtResumoPedidos.Text = preVisualizacao.Paragens.Count.ToString();
        TxtResumoDistancia.Text = $"{preVisualizacao.DistanciaTotalKm:0.#} km";
        TxtResumoDuracao.Text = $"{preVisualizacao.DuracaoTotalComIntervencoesMinutos / 60}h{preVisualizacao.DuracaoTotalComIntervencoesMinutos % 60:00}";

        // O total de km/duração inclui o regresso à sede (quando pedido) e, na duração, também o
        // tempo estimado de cada intervenção — nenhum dos dois aparece como linha própria na
        // tabela, por isso a diferença entre "soma das paragens visíveis" e o total pode parecer um
        // erro se não for explicada. As dicas (tooltip) fazem essa ponte.
        var minutosDeslocacao = preVisualizacao.DuracaoTotalDeslocacaoMinutos;
        var minutosIntervencoes = preVisualizacao.DuracaoTotalComIntervencoesMinutos - minutosDeslocacao;
        TxtResumoDuracao.ToolTip =
            $"{minutosDeslocacao / 60}h{minutosDeslocacao % 60:00} de deslocação (inclui regresso à sede, se aplicável) " +
            $"+ {minutosIntervencoes / 60}h{minutosIntervencoes % 60:00} de intervenções nas escolas ({preVisualizacao.Paragens.Count} pedidos).";
        TxtResumoDistancia.ToolTip = preVisualizacao.DistanciaRegressoKm is { } distRegresso
            ? $"Inclui {distRegresso:0.#} km de regresso à sede no final (linha \"↩ Regresso à Sede\" na tabela)."
            : null;

        if (preVisualizacao.Avisos.Count > 0)
        {
            PainelAvisos.Visibility = Visibility.Visible;
            TxtAvisos.Text = "⚠ " + string.Join("\n⚠ ", preVisualizacao.Avisos);
        }
        else
        {
            PainelAvisos.Visibility = Visibility.Collapsed;
        }

        // A tabela é alimentada por um pequeno objeto de exibição (não os records de domínio
        // diretamente) só para poder acrescentar, no fim, uma linha visível de "Regresso à Sede" —
        // ver PreVisualizacaoRota.DistanciaRegressoKm/DuracaoRegressoMinutos.
        var linhas = preVisualizacao.Paragens.Select(p => new LinhaParagemExibicao
        {
            Ordem = p.Ordem.ToString(),
            EscolaNome = p.Escola.Nome,
            DistanciaDesdeAnteriorKm = p.DistanciaDesdeAnteriorKm,
            DuracaoDesdeAnteriorMinutos = p.DuracaoDesdeAnteriorMinutos
        }).ToList();

        if (preVisualizacao.DistanciaRegressoKm is { } distanciaRegresso && preVisualizacao.DuracaoRegressoMinutos is { } duracaoRegresso)
        {
            linhas.Add(new LinhaParagemExibicao
            {
                Ordem = "↩",
                EscolaNome = "Regresso à Sede",
                DistanciaDesdeAnteriorKm = distanciaRegresso,
                DuracaoDesdeAnteriorMinutos = duracaoRegresso,
                EhRegresso = true
            });
        }

        GridParagens.ItemsSource = linhas;
        BtnConfirmar.IsEnabled = true;
    }

    private async void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        if (_preVisualizacaoAtual is not { Sucesso: true }) return;
        if (DpData.SelectedDate is not { } data) return;

        var confirmar = MessageBox.Show(
            $"Confirma a criação deste plano de rota para {data:dd/MM/yyyy}, com {_preVisualizacaoAtual.Paragens.Count} paragem(ns)?\n\n" +
            "Esta ação grava o plano na base de dados e gera o PDF. Os pedidos incluídos ficam associados a este " +
            "plano e não poderão ser selecionados noutro plano ativo para o mesmo dia.",
            "Confirmar Plano de Rota", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmar != MessageBoxResult.Yes) return;

        var horaPartida = TimeSpan.ParseExact(TxtHoraPartida.Text, @"hh\:mm", CultureInfo.InvariantCulture);
        decimal? limiteHoras = decimal.TryParse(TxtLimiteHoras.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var l) ? l : null;

        DefinirATrabalhar(true, "A guardar o plano, aguarde…");
        try
        {
            var (sucesso, erro, plano) = await _planeamento.ConfirmarEGuardarAsync(
                data, horaPartida, limiteHoras, ChkRegressarSede.IsChecked == true, _preVisualizacaoAtual);

            if (!sucesso || plano == null)
            {
                MessageBox.Show(erro ?? "Não foi possível guardar o plano.", "Erro ao guardar", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await GerarEGuardarPdfAsync(plano);

            MessageBox.Show("Plano de rota guardado e PDF gerado com sucesso.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            LimparPreVisualizacao();
            CarregarPedidos();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro inesperado ao guardar o plano:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DefinirATrabalhar(false, null);
        }
    }

    /// <summary>Recarrega o plano com todas as relações necessárias ao PDF já materializadas em
    /// memória (a app não usa proxies de lazy-loading — ver AppDbContext —, por isso sem Include
    /// explícito estas propriedades ficariam a null dentro do Task.Run abaixo), e só depois desenha
    /// o PDF numa thread em segundo plano, tal como a Folha de Inventário (ver
    /// Views/FolhaInventarioWindow.xaml.cs) — para nunca bloquear a janela enquanto o QuestPDF
    /// trabalha.</summary>
    private async Task GerarEGuardarPdfAsync(PlanoRota planoRecemCriado)
    {
        var plano = await App.Db.PlanosRota
            .Include(p => p.CriadoPorUsuario)
            .Include(p => p.Paragens).ThenInclude(pp => pp.PedidoIntervencao)
            .Include(p => p.Paragens).ThenInclude(pp => pp.Escola).ThenInclude(e => e!.Contactos)
            .FirstAsync(p => p.Id == planoRecemCriado.Id);

        var paragensOrdenadas = plano.Paragens.OrderBy(pp => pp.Ordem).ToList();

        var pastaDestino = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeiriaDISIA", "PlanosRota");
        System.IO.Directory.CreateDirectory(pastaDestino);
        var caminhoPdf = System.IO.Path.Combine(pastaDestino, $"PlanoRota_{plano.Data:yyyyMMdd}_{plano.Id}.pdf");

        await Task.Run(() => new PlanoRotaPdfService().GerarPdf(caminhoPdf, plano, paragensOrdenadas));

        plano.CaminhoPdf = caminhoPdf;
        await App.Db.SaveChangesAsync();

        var abrir = MessageBox.Show("Deseja abrir o PDF gerado agora?", "PDF gerado", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (abrir == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(caminhoPdf) { UseShellExecute = true });
    }

    private void DefinirATrabalhar(bool aTrabalhar, string? mensagem)
    {
        _aTrabalhar = aTrabalhar;
        TxtEstadoCalculo.Visibility = aTrabalhar ? Visibility.Visible : Visibility.Collapsed;
        TxtEstadoCalculo.Text = mensagem ?? "";
        IsEnabled = true; // a janela continua interativa; só bloqueamos as ações relevantes abaixo
        GridPedidos.IsEnabled = !aTrabalhar;
        DpData.IsEnabled = !aTrabalhar;
        TxtHoraPartida.IsEnabled = !aTrabalhar;
        TxtLimiteHoras.IsEnabled = !aTrabalhar;
        ChkRegressarSede.IsEnabled = !aTrabalhar;
        BtnCalcularRota.IsEnabled = !aTrabalhar;
        BtnConfirmar.IsEnabled = !aTrabalhar && _preVisualizacaoAtual is { Sucesso: true };
    }
}
