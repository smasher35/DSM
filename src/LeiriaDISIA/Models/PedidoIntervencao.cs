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

    /// <summary>Início do período de "Em Espera" atualmente em curso — só tem valor enquanto
    /// <see cref="Estado"/> for <see cref="EstadoPedido.EmEspera"/>; ao sair desse estado (ver
    /// Views/PedidoEditWindow.xaml.cs), a duração desse período soma-se a
    /// <see cref="DiasEmEsperaAcumulados"/> e este campo volta a null. Usado para excluir tempo em
    /// "Em Espera" do cálculo de "tempo médio até à conclusão" (ver Views/PedidosWindow.xaml.cs) —
    /// esse tempo não é da responsabilidade da DISIA (depende de aquisição de equipamento/material
    /// por terceiros), por isso não deve penalizar essa estatística.</summary>
    public DateTime? DataInicioEsperaAtual { get; set; }

    /// <summary>Soma de todos os períodos já terminados em que este pedido esteve "Em Espera" (dias)
    /// — um pedido pode entrar e sair de "Em Espera" mais do que uma vez; cada vez que sai, o
    /// período que terminou soma-se aqui. Ver <see cref="DataInicioEsperaAtual"/>.</summary>
    public int DiasEmEsperaAcumulados { get; set; }

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

    /// <summary>Nº de dias entre o pedido e a conclusão, sem contar o tempo passado "Em Espera" (ver
    /// <see cref="DiasEmEsperaAcumulados"/>/<see cref="DataInicioEsperaAtual"/>) — usado para a
    /// estatística de "tempo médio até à conclusão" em Views/PedidosWindow.xaml.cs. Só faz sentido
    /// para um pedido já concluído; devolve null nos restantes casos. Se o pedido estiver, por
    /// algum motivo, concluído mas ainda com um período de espera em aberto (não devia acontecer,
    /// já que sair de "Em Espera" fecha sempre esse período — ver Views/PedidoEditWindow.xaml.cs),
    /// esse período em curso também é descontado, para nunca inflacionar a estatística.</summary>
    public int? DiasParaConclusaoExcluindoEspera
    {
        get
        {
            if (Estado != EstadoPedido.Concluido || DataConclusao == null) return null;

            var esperaEmCurso = DataInicioEsperaAtual is { } inicio
                ? (int)(DataConclusao.Value - inicio).TotalDays
                : 0;

            var dias = (int)(DataConclusao.Value - DataPedido).TotalDays - DiasEmEsperaAcumulados - esperaEmCurso;
            return Math.Max(0, dias);
        }
    }

    public bool EstaEmAberto => Estado is EstadoPedido.Pendente or EstadoPedido.EmAndamento or EstadoPedido.EmEspera;
}
