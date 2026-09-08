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

    /// <summary>Nº do pedido no sistema SIGA (Suporte) — quando a intervenção nasce de um Pedido
    /// que já tenha este campo preenchido (ver <see cref="PedidoIntervencao.NumeroSuporteSiga"/>),
    /// vem pré-preenchido automaticamente a partir daí; caso contrário, ou quando a intervenção é
    /// registada diretamente sem pedido associado, pode ser escrito aqui à mão. Ver
    /// Views/IntervencaoEditWindow.xaml.cs.</summary>
    public string? NumeroSuporteSiga { get; set; }

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
/// Equipamento que ainda não existia no inventário, registado e entregue a uma escola pela
/// primeira vez durante uma intervenção — ex.: um lote de computadores novos, comprado e guardado
/// na DISIA, nunca antes atribuído a nenhuma escola. Distinto de <see cref="IntervencaoEquipamento"/>
/// (que pressupõe o equipamento já estar na escola antes da intervenção, a ser
/// reparado/configurado no local) e de <see cref="EquipamentoRecolhido"/> (que pressupõe uma
/// recolha anterior desse mesmo equipamento nessa escola). O registo do próprio equipamento (já
/// com a Escola atribuída) é criado ao mesmo tempo, através do formulário "Novo Equipamento" — ver
/// Views/IntervencaoEditWindow.xaml.cs, AdicionarNovoEntregue_Click; esta tabela só liga esse
/// equipamento à intervenção durante a qual foi entregue. Tabela própria, em vez de reaproveitar
/// <see cref="IntervencaoEquipamento"/> com uma bandeira a distinguir os dois casos, para não
/// misturar este conceito nas estatísticas de "equipamento mais intervencionado"
/// (Views/EquipamentosWindow.xaml.cs) — entregar equipamento novo não é, para esse efeito, uma
/// intervenção sobre um equipamento.
/// </summary>
public class IntervencaoEquipamentoNovo
{
    public int Id { get; set; }

    public int IntervencaoId { get; set; }
    public Intervencao? Intervencao { get; set; }

    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
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
