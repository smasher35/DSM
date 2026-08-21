namespace LeiriaDISIA.Models;

/// <summary>Prioridade de um <see cref="PedidoIntervencao"/> — só entra em jogo como critério de
/// desempate no Planeamento de Rota, quando nem todos os pedidos selecionados cabem no limite de
/// horas da equipa; nunca força uma ordem específica sozinha (ver
/// <see cref="Services.Rotas.PlaneamentoRotaService"/>).</summary>
public enum PrioridadePedido
{
    Baixa,
    Normal,
    Alta
}

/// <summary>
/// Pedido de intervenção registado numa escola/agrupamento, antes de ser convertido
/// (ou não) numa Intervenção efetivamente realizada.
/// Corresponde ao módulo "Casos Pendentes" / "Pedidos de Intervenção" do ficheiro base.
/// </summary>
public class PedidoIntervencao
{
    public int Id { get; set; }

    public DateTime DataPedido { get; set; } = DateTime.Today;

    public int EscolaId { get; set; }
    public Escola? Escola { get; set; }

    // Guardado também aqui (desnormalizado) para facilitar filtros/relatórios rápidos,
    // mas é sempre sincronizado a partir da Escola escolhida.
    public int? AgrupamentoId { get; set; }
    public Agrupamento? Agrupamento { get; set; }

    /// <summary>Nº do pedido no sistema SIGA (Suporte), quando o pedido tiver entrado também
    /// por aquela via — apenas texto livre de referência, não é validado nem obrigatório.</summary>
    public string? NumeroSuporteSiga { get; set; }

    public string Solicitante { get; set; } = string.Empty;   // nome de quem pediu (ex: professor, auxiliar)
    public string? ContactoSolicitante { get; set; }

    public string Razao { get; set; } = string.Empty;         // descrição do pedido

    public EstadoPedido Estado { get; set; } = EstadoPedido.EmAndamento;
    public string? MotivoPendente { get; set; }

    public DateTime? DataConclusao { get; set; }

    /// <summary>Quando o pedido é convertido numa intervenção, guarda a referência.</summary>
    public int? IntervencaoId { get; set; }
    public Intervencao? Intervencao { get; set; }

    public string? Observacoes { get; set; }

    // ---- Planeamento de Rotas ----
    /// <summary>Duração estimada da intervenção, em minutos. Quando não definida (a maioria dos
    /// pedidos, por omissão), o Planeamento de Rota assume 60 minutos ao calcular a duração total
    /// do dia — ver <see cref="Services.Rotas.PlaneamentoRotaService"/>.</summary>
    public int? DuracaoEstimadaMinutos { get; set; }

    public PrioridadePedido Prioridade { get; set; } = PrioridadePedido.Normal;

    /// <summary>Quando <c>true</c>, este pedido nunca é excluído automaticamente de uma rota por
    /// falta de tempo (limite de horas da equipa) — se não couber, a aplicação avisa em vez de o
    /// deixar de fora silenciosamente. Não fixa a posição na rota; a ordem continua a ser decidida
    /// pelo otimizador.</summary>
    public bool ObrigatorioNaRota { get; set; }

    public string CorEstado => EstadoCores.CorEstadoPedido(Estado);

    /// <summary>Nº de dias desde o pedido até à conclusão (ou até hoje, se ainda aberto).</summary>
    public int DiasEmAberto =>
        (int)((DataConclusao ?? DateTime.Today) - DataPedido).TotalDays;

    /// <summary>Cor semafórica do tempo em aberto (só relevante enquanto não está concluído/cancelado).</summary>
    public string CorTempoEmAberto => EstadoCores.CorTempoEmAberto(DiasEmAberto);

    public bool EstaEmAberto => Estado is EstadoPedido.Pendente or EstadoPedido.EmAndamento or EstadoPedido.EmEspera;
}
