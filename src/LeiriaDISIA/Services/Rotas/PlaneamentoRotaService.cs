using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Services.Rotas;

/// <summary>Um pedido elegível para planeamento, com o eventual motivo de não poder entrar na
/// rota já resolvido (<see cref="Bloqueio"/>) — para a UI mostrar de forma clara porque é que um
/// pedido aparece desativado na lista de seleção, em vez de simplesmente omiti-lo.</summary>
/// <param name="PlanoRotaIdCancelavel">Preenchido apenas quando o motivo de bloqueio é "já está
/// incluído noutro plano" E esse plano ainda está no estado <see cref="EstadoPlanoRota.Planeado"/>
/// (ou seja, ainda não foi marcado como realizado). Permite à UI oferecer um atalho para cancelar
/// esse plano e libertar o pedido, sem reabrir planos já marcados como <c>Concluído</c>.</param>
public record PedidoParaPlaneamento(PedidoIntervencao Pedido, string? Bloqueio, int? PlanoRotaIdCancelavel = null)
{
    public bool PodeSerSelecionado => Bloqueio == null;
}

/// <summary>Pré-visualização de uma rota calculada, antes de ser confirmada/guardada — ver
/// <see cref="PlaneamentoRotaService.CalcularRotaAsync"/>.</summary>
public record ParagemPreVisualizacao(
    PedidoIntervencao Pedido, Escola Escola, int Ordem, double DistanciaDesdeAnteriorKm, int DuracaoDesdeAnteriorMinutos);

/// <param name="DistanciaRegressoKm">Distância/duração do troço final de regresso à sede — já
/// somadas em <see cref="DistanciaTotalKm"/>/<see cref="DuracaoTotalDeslocacaoMinutos"/>, mas
/// expostas também em separado para a UI poder mostrar esse troço como linha própria na tabela
/// (ver Views/PlanearRotaWindow.xaml.cs), em vez do total "aparecer" maior do que a soma das
/// paragens visíveis sem nenhuma explicação.</param>
public record PreVisualizacaoRota(
    bool Sucesso, string? Erro, List<ParagemPreVisualizacao> Paragens,
    double DistanciaTotalKm, int DuracaoTotalDeslocacaoMinutos, int DuracaoTotalComIntervencoesMinutos, List<string> Avisos,
    double? DistanciaRegressoKm = null, int? DuracaoRegressoMinutos = null);

/// <summary>
/// Motor de negócio do Planeamento de Rotas: elegibilidade de pedidos, prevenção de duplicados,
/// orquestração da otimização (via <see cref="IRoutingService"/>) e persistência do plano
/// confirmado. Nunca altera o <see cref="PedidoIntervencao.Estado"/> só por um pedido ter sido
/// incluído numa pré-visualização — só a confirmação explícita (<see cref="ConfirmarEGuardarAsync"/>)
/// grava alguma coisa na base de dados.
/// </summary>
public class PlaneamentoRotaService
{
    private readonly AppDbContext _db;
    private readonly IGeocodingService _geocoding;
    private readonly IRoutingService _routing;
    private readonly EscolaGeocodingService _escolaGeocoding;

    /// <summary>Duração assumida de uma intervenção quando o pedido não tem
    /// <see cref="PedidoIntervencao.DuracaoEstimadaMinutos"/> definida — valor aproximado da média
    /// real destas intervenções (a maioria são substituições/pequenas reparações rápidas, não
    /// diagnósticos longos).</summary>
    public const int DuracaoPorOmissaoMinutos = 30;

    public PlaneamentoRotaService(AppDbContext db) : this(db, new OpenRouteServiceClient())
    {
    }

    public PlaneamentoRotaService(AppDbContext db, OpenRouteServiceClient clienteOpenRouteService)
    {
        _db = db;
        _geocoding = clienteOpenRouteService;
        _routing = clienteOpenRouteService;
        _escolaGeocoding = new EscolaGeocodingService(db, clienteOpenRouteService);
    }

    /// <summary>Pedidos candidatos a entrar num plano de rota para <paramref name="data"/>, cada um
    /// já indicando se pode ou não ser selecionado e porquê (ver <see cref="PedidoParaPlaneamento"/>).
    /// Não filtra silenciosamente os inelegíveis — mostrar o motivo é mais transparente do que
    /// simplesmente escondê-los, e evita que o utilizador ache que o pedido desapareceu.</summary>
    public List<PedidoParaPlaneamento> ObterPedidosParaPlaneamento(DateTime data)
    {
        var pedidosEmAberto = _db.PedidosIntervencao
            .Include(p => p.Escola)
            .Where(p => p.Estado == EstadoPedido.Pendente || p.Estado == EstadoPedido.EmAndamento || p.Estado == EstadoPedido.EmEspera)
            .OrderByDescending(p => p.Prioridade)
            .ThenBy(p => p.DataPedido)
            .ToList();

        // Pedidos já incluídos num plano ATIVO (não cancelado) para esta MESMA data — nunca podem
        // ser selecionados outra vez para essa data (evita duas equipas planeadas para a mesma
        // escola/pedido no mesmo dia). Um plano cancelado liberta os seus pedidos novamente.
        var dataSoData = data.Date;
        var pedidosJaPlaneados = _db.PlanoRotaParagens
            .Include(pp => pp.PlanoRota)
            .Where(pp => pp.PlanoRota!.Data.Date == dataSoData && pp.PlanoRota.Estado != EstadoPlanoRota.Cancelado)
            .Select(pp => new { pp.PedidoIntervencaoId, PlanoRotaId = pp.PlanoRota!.Id, pp.PlanoRota.Estado })
            .ToDictionary(x => x.PedidoIntervencaoId, x => (x.PlanoRotaId, x.Estado));

        return pedidosEmAberto.Select(p =>
        {
            string? bloqueio = null;
            int? planoIdCancelavel = null;

            if (p.Escola == null)
                bloqueio = "Pedido sem escola associada.";
            else if (string.IsNullOrWhiteSpace(p.Escola.Morada))
                bloqueio = "A escola não tem morada preenchida.";
            else if (p.Escola.Latitude == null || p.Escola.Longitude == null)
                bloqueio = "A escola ainda não tem coordenadas calculadas (use \"Recalcular Distância\" em Editar Escola).";
            else if (pedidosJaPlaneados.TryGetValue(p.Id, out var planoExistente))
            {
                bloqueio = planoExistente.Estado == EstadoPlanoRota.Concluido
                    ? "Este pedido já foi atendido numa rota concluída neste dia."
                    : "Este pedido já está incluído noutro plano de rota para este dia (ainda não realizado).";

                // Só se oferece o atalho de "repor" quando o plano que bloqueia ainda está por
                // realizar — nunca para um plano já marcado como Concluído.
                if (planoExistente.Estado == EstadoPlanoRota.Planeado)
                    planoIdCancelavel = planoExistente.PlanoRotaId;
            }

            return new PedidoParaPlaneamento(p, bloqueio, planoIdCancelavel);
        }).ToList();
    }

    /// <summary>Cancela um plano de rota que ficou "Planeado" mas, por qualquer razão, não chegou a
    /// ser realizado — liberta imediatamente os pedidos associados para poderem entrar num novo
    /// plano (ver <see cref="ObterPedidosParaPlaneamento"/>). Recusa-se a cancelar um plano já
    /// marcado como <see cref="EstadoPlanoRota.Concluido"/>, para não desfazer por engano uma rota
    /// que já foi efetivamente percorrida.</summary>
    public async Task<(bool Sucesso, string? Erro)> CancelarPlanoAsync(int planoRotaId, CancellationToken ct = default)
    {
        var plano = await _db.PlanosRota.FirstOrDefaultAsync(p => p.Id == planoRotaId, ct);
        if (plano == null)
            return (false, "Este plano de rota já não existe (pode ter sido cancelado entretanto).");

        if (plano.Estado == EstadoPlanoRota.Concluido)
            return (false, "Este plano já está marcado como Concluído e não pode ser cancelado.");

        if (plano.Estado == EstadoPlanoRota.Cancelado)
            return (true, null); // já estava cancelado — nada a fazer, mas não é um erro

        plano.Estado = EstadoPlanoRota.Cancelado;
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Calcula uma rota otimizada para os pedidos selecionados, partindo da sede do
    /// Município. Não grava nada na base de dados — é só a pré-visualização que o utilizador vê
    /// antes de confirmar (ver <see cref="ConfirmarEGuardarAsync"/>). Geocodifica escolas que ainda
    /// não tenham coordenadas (e grava esse resultado desde já — geocodificar é caro e não há razão
    /// para descartar o resultado só porque o utilizador ainda não confirmou o plano).</summary>
    public async Task<PreVisualizacaoRota> CalcularRotaAsync(
        List<PedidoIntervencao> pedidosSelecionados, bool regressarASede, decimal? limiteHorasEquipa, CancellationToken ct = default)
    {
        if (pedidosSelecionados.Count == 0)
            return new PreVisualizacaoRota(false, "Selecione pelo menos um pedido.", new(), 0, 0, 0, new());

        // Geocodifica, se preciso, as escolas dos pedidos selecionados que ainda não tenham
        // coordenadas — para o utilizador não ter de sair deste ecrã só para calcular uma escola
        // que se esqueceu de atualizar antes.
        foreach (var pedido in pedidosSelecionados)
        {
            if (pedido.Escola == null) continue;
            if (pedido.Escola.Latitude != null && pedido.Escola.Longitude != null) continue;

            var (sucesso, erro) = await _escolaGeocoding.RecalcularAsync(pedido.Escola, ct);
            if (!sucesso)
                return new PreVisualizacaoRota(false, $"Escola \"{pedido.Escola.Nome}\": {erro}", new(), 0, 0, 0, new());
        }
        await _db.SaveChangesAsync(ct);

        var (coordenadaSede, erroSede) = await _escolaGeocoding.ObterCoordenadaSedeAsync(ct);
        if (coordenadaSede == null)
            return new PreVisualizacaoRota(false, erroSede, new(), 0, 0, 0, new());

        var coordenadasParagens = pedidosSelecionados
            .Select(p => new CoordenadaGeografica(p.Escola!.Latitude!.Value, p.Escola.Longitude!.Value))
            .ToList();

        // Validação de sanidade antes de chamar a API: uma coordenada fora de Portugal continental
        // quase sempre significa um valor antigo/errado (ex.: introduzido manualmente antes de
        // existir esta funcionalidade, ou latitude/longitude trocadas por engano) — nunca é enviada
        // ao serviço de rotas, que devolveria só um erro genérico ("a distância aproximada da rota
        // excede o limite do servidor") sem dizer qual das escolas é o problema.
        foreach (var pedido in pedidosSelecionados)
        {
            var (lat, lon) = (pedido.Escola!.Latitude!.Value, pedido.Escola.Longitude!.Value);
            if (!EstaDentroDePortugalContinental(lat, lon))
                return new PreVisualizacaoRota(false,
                    $"A escola \"{pedido.Escola.Nome}\" tem coordenadas gravadas fora de Portugal continental " +
                    $"(Latitude {lat:F4}, Longitude {lon:F4}) — provavelmente um valor antigo, de antes de existir " +
                    "o cálculo automático de distância. Abra \"Editar Escola\" e clique em \"Recalcular Distância\" " +
                    "para corrigir antes de voltar a tentar.",
                    new(), 0, 0, 0, new());
        }

        var otimizacao = await _routing.OtimizarRotaAsync(
            coordenadaSede, coordenadasParagens, regressarASede ? coordenadaSede : null, ct);

        if (!otimizacao.Sucesso)
            return new PreVisualizacaoRota(false, otimizacao.MensagemErro, new(), 0, 0, 0, new());

        var paragens = otimizacao.Paragens.Select((p, i) => new ParagemPreVisualizacao(
            Pedido: pedidosSelecionados[p.IndiceOriginal],
            Escola: pedidosSelecionados[p.IndiceOriginal].Escola!,
            Ordem: i + 1,
            DistanciaDesdeAnteriorKm: p.DistanciaDesdeAnteriorKm,
            DuracaoDesdeAnteriorMinutos: p.DuracaoDesdeAnteriorMinutos)).ToList();

        var duracaoIntervencoesMinutos = pedidosSelecionados.Sum(p => p.DuracaoEstimadaMinutos ?? DuracaoPorOmissaoMinutos);
        var duracaoTotalComIntervencoes = otimizacao.DuracaoTotalMinutos + duracaoIntervencoesMinutos;

        var avisos = new List<string>();
        if (limiteHorasEquipa is { } limite && duracaoTotalComIntervencoes > limite * 60)
        {
            var pedidosNaoObrigatorios = pedidosSelecionados
                .Where(p => !p.ObrigatorioNaRota)
                .OrderBy(p => p.Prioridade)
                .Select(p => $"{p.Escola?.Nome} ({p.Razao})")
                .ToList();

            avisos.Add(
                $"A duração total estimada ({duracaoTotalComIntervencoes / 60}h{duracaoTotalComIntervencoes % 60:00}min) " +
                $"ultrapassa o limite definido para a equipa ({limite}h). A aplicação NÃO remove nenhum pedido " +
                "automaticamente — reveja a seleção manualmente." +
                (pedidosNaoObrigatorios.Count > 0
                    ? $" Pedidos não marcados como obrigatórios, por ordem de menor prioridade: {string.Join(", ", pedidosNaoObrigatorios)}."
                    : " Todos os pedidos selecionados estão marcados como obrigatórios."));
        }

        return new PreVisualizacaoRota(true, null, paragens, otimizacao.DistanciaTotalKm,
            otimizacao.DuracaoTotalMinutos, duracaoTotalComIntervencoes, avisos,
            otimizacao.DistanciaRegressoKm, otimizacao.DuracaoRegressoMinutos);
    }

    /// <summary>Persiste o plano confirmado pelo utilizador. Volta a validar, no momento de gravar,
    /// que nenhum dos pedidos foi entretanto incluído noutro plano ativo para a mesma data
    /// (proteção best-effort contra corrida — esta é uma aplicação desktop de utilizador único de
    /// cada vez, mas a base de dados pode ser partilhada por mais do que uma instalação).</summary>
    public async Task<(bool Sucesso, string? Erro, PlanoRota? Plano)> ConfirmarEGuardarAsync(
        DateTime data, TimeSpan horaPartida, decimal? limiteHorasEquipa, bool regressarASede,
        PreVisualizacaoRota preVisualizacao, CancellationToken ct = default)
    {
        if (!preVisualizacao.Sucesso || preVisualizacao.Paragens.Count == 0)
            return (false, "Não há uma rota calculada válida para guardar.", null);

        var dataSoData = data.Date;
        var idsPedidos = preVisualizacao.Paragens.Select(p => p.Pedido.Id).ToList();

        var jaPlaneados = await _db.PlanoRotaParagens
            .Include(pp => pp.PlanoRota)
            .Where(pp => pp.PlanoRota!.Data.Date == dataSoData && pp.PlanoRota.Estado != EstadoPlanoRota.Cancelado
                         && idsPedidos.Contains(pp.PedidoIntervencaoId))
            .Select(pp => pp.PedidoIntervencaoId)
            .ToListAsync(ct);

        if (jaPlaneados.Count > 0)
            return (false,
                "Um ou mais pedidos selecionados foram entretanto incluídos noutro plano de rota para este dia. " +
                "Feche esta janela e volte a abrir o planeamento para ver a seleção atualizada.", null);

        var plano = new PlanoRota
        {
            Data = dataSoData,
            CriadoPorUsuarioId = SessaoAtual.UtilizadorLogado?.Id,
            HoraPartida = horaPartida,
            LimiteHorasEquipa = limiteHorasEquipa,
            PontoPartida = EnderecoSedeMunicipio.Morada,
            PontoRegresso = regressarASede ? EnderecoSedeMunicipio.Morada : "Sem regresso planeado — equipa termina na última paragem",
            DistanciaTotalKm = preVisualizacao.DistanciaTotalKm,
            DuracaoTotalMinutos = preVisualizacao.DuracaoTotalComIntervencoesMinutos,
            Estado = EstadoPlanoRota.Planeado
        };

        foreach (var paragem in preVisualizacao.Paragens)
        {
            plano.Paragens.Add(new PlanoRotaParagem
            {
                PedidoIntervencaoId = paragem.Pedido.Id,
                EscolaId = paragem.Escola.Id,
                Ordem = paragem.Ordem,
                DistanciaDesdeAnteriorKm = paragem.DistanciaDesdeAnteriorKm,
                DuracaoDesdeAnteriorMinutos = paragem.DuracaoDesdeAnteriorMinutos
            });
        }

        _db.PlanosRota.Add(plano);
        await _db.SaveChangesAsync(ct);

        return (true, null, plano);
    }

    /// <summary>Verificação de sanidade grosseira (não precisa de ser exata) — só para apanhar
    /// valores claramente errados (ex.: um resto de dados antigos de antes desta funcionalidade
    /// existir, ou latitude/longitude trocadas por engano) antes de os enviar ao serviço de rotas.
    /// A caixa cobre generosamente Portugal continental (incluindo uma margem de segurança); não
    /// cobre Açores/Madeira de propósito, já que esta aplicação só serve escolas do concelho de
    /// Leiria.</summary>
    private static bool EstaDentroDePortugalContinental(double latitude, double longitude) =>
        latitude is >= 36.8 and <= 42.2 && longitude is >= -9.6 and <= -6.1;
}
