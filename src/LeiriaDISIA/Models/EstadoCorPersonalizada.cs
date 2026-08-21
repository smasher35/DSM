namespace LeiriaDISIA.Models;

/// <summary>Grupos de estados cuja cor pode ser personalizada em Administração → Dados Fixos.</summary>
public static class GruposEstadoCor
{
    public const string Intervencao = "EstadoIntervencao";
    public const string Pedido = "EstadoPedido";
    public const string Equipamento = "EstadoEquipamento";
    public const string Recolha = "EstadoRecolha";
}

/// <summary>
/// Personalização de cor de um estado fixo (os estados em si — Fechada, Pendente, etc. —
/// não podem ser criados/eliminados pois estão associados a lógica de negócio no código,
/// mas a sua cor de apresentação pode ser ajustada livremente aqui).
/// </summary>
public class EstadoCorPersonalizada
{
    public int Id { get; set; }

    /// <summary>Ver <see cref="GruposEstadoCor"/>.</summary>
    public string Grupo { get; set; } = string.Empty;

    /// <summary>Nome do valor do enum (ex: "Fechada", "Pendente").</summary>
    public string NomeEstado { get; set; } = string.Empty;

    /// <summary>Nome amigável apresentado ao utilizador (pode divergir do nome técnico do enum).</summary>
    public string NomeExibicao { get; set; } = string.Empty;

    public string Cor { get; set; } = "#9CA3AF";
}
