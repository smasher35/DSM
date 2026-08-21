namespace LeiriaDISIA.Models;

/// <summary>
/// Intervenção técnica realizada numa escola, sempre associada a um mês/ano
/// (à semelhança das abas JAN..DEZ do ficheiro_base.xlsx).
/// </summary>
public class Intervencao
{
    public int Id { get; set; }

    public DateTime Data { get; set; } = DateTime.Today;

    // Desnormalizados para permitir agrupar rapidamente por mês/ano (tal como as abas do Excel)
    public int Mes { get; set; }   // 1-12
    public int Ano { get; set; }

    public int EscolaId { get; set; }
    public Escola? Escola { get; set; }

    public int? AgrupamentoId { get; set; }
    public Agrupamento? Agrupamento { get; set; }

    public string Descricao { get; set; } = string.Empty;        // "Tipo de Intervenção" no Excel
    public string? MaterialRecolhidoAbatido { get; set; }

    public EstadoIntervencao Estado { get; set; } = EstadoIntervencao.Fechada;
    public string? MotivoPendente { get; set; }

    public int? PedidoOrigemId { get; set; }   // se nasceu de um pedido
    public PedidoIntervencao? PedidoOrigem { get; set; }

    public ICollection<IntervencaoCategoria> Categorias { get; set; } = new List<IntervencaoCategoria>();

    /// <summary>Equipamentos reparados/intervencionados no local (não recolhidos nem abatidos).</summary>
    public ICollection<IntervencaoEquipamento> EquipamentosIntervencionados { get; set; } = new List<IntervencaoEquipamento>();

    public string CorEstado => EstadoCores.CorEstadoIntervencao(Estado);
}

/// <summary>
/// Junção entre uma Intervenção e um Equipamento reparado/tratado diretamente no local
/// (isto é, sem necessidade de o recolher para a DISIA nem de o abater).
/// </summary>
public class IntervencaoEquipamento
{
    public int Id { get; set; }

    public int IntervencaoId { get; set; }
    public Intervencao? Intervencao { get; set; }

    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }

    public string? Observacoes { get; set; }
}

/// <summary>
/// Junção N:N entre Intervencao e CategoriaIntervencao, permitindo ainda registar
/// uma subcategoria e uma quantidade (uma visita pode ter várias áreas, tal como
/// referido na nota do relatório original).
/// </summary>
public class IntervencaoCategoria
{
    public int Id { get; set; }

    public int IntervencaoId { get; set; }
    public Intervencao? Intervencao { get; set; }

    public int CategoriaIntervencaoId { get; set; }
    public CategoriaIntervencao? Categoria { get; set; }

    public int? SubCategoriaIntervencaoId { get; set; }
    public SubCategoriaIntervencao? SubCategoria { get; set; }

    public int Quantidade { get; set; } = 1;
}
