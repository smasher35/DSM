namespace LeiriaDISIA.Models;

/// <summary>Estado de um <see cref="PlanoRota"/>. Um plano só existe depois de o utilizador
/// confirmar explicitamente a pré-visualização — nunca é criado "a rascunho" na base de dados
/// enquanto o utilizador está só a experimentar combinações de pedidos (ver
/// Views/PlanearRotaWindow.xaml.cs), por isso não há um estado "Rascunho" aqui.</summary>
public enum EstadoPlanoRota
{
    /// <summary>Plano confirmado, ainda não realizado — é o estado normal enquanto a data da rota
    /// ainda não passou.</summary>
    Planeado,

    /// <summary>Marcado manualmente pelo utilizador depois de a equipa ter percorrido a rota.
    /// Não é automático — a aplicação nunca assume que uma rota foi feita só porque a data já
    /// passou.</summary>
    Concluido,

    /// <summary>Cancelado — os pedidos associados voltam a ficar disponíveis para um novo plano
    /// nesse dia (ver regra de "não duplicar pedido em planos ativos" em
    /// <see cref="Services.Rotas.PlaneamentoRotaService"/>).</summary>
    Cancelado
}

/// <summary>
/// Plano de rota diário para deslocações da equipa DISIA a escolas, para atender a um conjunto de
/// <see cref="PedidoIntervencao"/> selecionados — ver Views/PlanearRotaWindow.xaml.cs. Criado só
/// depois de o utilizador confirmar a pré-visualização da rota otimizada; nunca altera o
/// <see cref="PedidoIntervencao.Estado"/> dos pedidos incluídos (ver <see cref="Paragens"/>), só os
/// associa à rota através de <see cref="PlanoRotaParagem"/>.
/// </summary>
public class PlanoRota
{
    public int Id { get; set; }

    public DateTime Data { get; set; }

    public int? CriadoPorUsuarioId { get; set; }
    public Usuario? CriadoPorUsuario { get; set; }

    public DateTime DataCriacao { get; set; } = DateTime.Now;

    /// <summary>Morada de partida — por omissão, a sede do Município de Leiria (Largo da
    /// República), mas guardada como texto em vez de constante fixa, para o dia em que seja preciso
    /// partir de outro local sem alterar código.</summary>
    public string PontoPartida { get; set; } = EnderecoSedeMunicipio.Morada;

    /// <summary>Normalmente igual a <see cref="PontoPartida"/> (a equipa regressa à sede), mas
    /// mantido como campo próprio para o caso de um dia a rota terminar noutro local.</summary>
    public string PontoRegresso { get; set; } = EnderecoSedeMunicipio.Morada;

    public TimeSpan HoraPartida { get; set; } = new(9, 0, 0);

    /// <summary>Limite máximo de horas da equipa nesse dia. <c>null</c> = sem limite (inclui todos
    /// os pedidos selecionados, avisando apenas se algum não tiver escola/morada geocodificável).</summary>
    public decimal? LimiteHorasEquipa { get; set; }

    public double DistanciaTotalKm { get; set; }
    public int DuracaoTotalMinutos { get; set; }

    public EstadoPlanoRota Estado { get; set; } = EstadoPlanoRota.Planeado;

    /// <summary>Caminho do PDF gerado no momento da confirmação (ver
    /// Services/Rotas/PlanoRotaPdfService.cs) — só de referência; o ficheiro em si vive fora da
    /// base de dados, tal como os restantes PDFs gerados pela aplicação.</summary>
    public string? CaminhoPdf { get; set; }

    public ICollection<PlanoRotaParagem> Paragens { get; set; } = new List<PlanoRotaParagem>();
}

/// <summary>
/// Uma paragem (escola) dentro de um <see cref="PlanoRota"/>, na ordem otimizada calculada por
/// <see cref="Services.Rotas.IRoutingService"/>.
/// </summary>
public class PlanoRotaParagem
{
    public int Id { get; set; }

    public int PlanoRotaId { get; set; }
    public PlanoRota? PlanoRota { get; set; }

    public int PedidoIntervencaoId { get; set; }
    public PedidoIntervencao? PedidoIntervencao { get; set; }

    public int EscolaId { get; set; }
    public Escola? Escola { get; set; }

    /// <summary>Posição na rota, a partir de 1 (1ª paragem depois de sair da sede).</summary>
    public int Ordem { get; set; }

    /// <summary>Distância/duração desde a paragem anterior (ou desde o ponto de partida, na
    /// primeira paragem) — não desde a sede em linha reta, exatamente o que se percorre naquele
    /// troço da rota.</summary>
    public double DistanciaDesdeAnteriorKm { get; set; }
    public int DuracaoDesdeAnteriorMinutos { get; set; }
}

/// <summary>Morada da sede do Município de Leiria, ponto de partida/regresso por omissão do
/// Planeamento de Rotas.</summary>
public static class EnderecoSedeMunicipio
{
    public const string Morada = "Largo da República, 2414-006 Leiria, Portugal";

    // Coordenadas fixadas manualmente (fonte: base de dados de códigos postais dos CTT para este
    // arruamento — não uma geocodificação automática). Foram fixadas de propósito, em vez de se
    // continuar a geocodificar este endereço a cada sessão da aplicação: como "Largo da República"
    // é um nome de rua comum a muitas terras em Portugal, o motor de geocodificação por vezes
    // devolvia um resultado errado (ex.: uma localidade a 200+ km de Leiria), o que desviava TODAS
    // as distâncias à sede calculadas nessa sessão. Como esta morada nunca muda, fixar as
    // coordenadas elimina esse risco por completo — e evita um pedido HTTP desnecessário sempre
    // que a app arranca. Ver EscolaGeocodingService.ObterCoordenadaSedeAsync.
    public const double Latitude = 39.741;
    public const double Longitude = -8.8102;
}
