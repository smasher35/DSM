namespace LeiriaDISIA.Models;

/// <summary>
/// Categoria das atividades gerais da DISIA (fora do âmbito escolar),
/// para agrupar itens como "Videovigilância", "Redes/Comunicações",
/// "Equipamento Informático", "Manutenção de Instalações", etc.
/// </summary>
public class CategoriaDisia
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string CorHex { get; set; } = "#6366F1";
}

/// <summary>
/// Atividade desenvolvida no âmbito da DISIA fora das escolas
/// (juntas de freguesia, instalações municipais, etc.) - equivalente
/// à aba "Serv. DISIA" do ficheiro_base.xlsx.
/// </summary>
public class AtividadeDisia
{
    public int Id { get; set; }

    public DateTime Data { get; set; } = DateTime.Today;
    public int Mes { get; set; }
    public int Ano { get; set; }

    public string Descricao { get; set; } = string.Empty;
    public string? Local { get; set; }           // ex: Junta de Freguesia de Amor, Museu de Leiria...
    public string? Divisao { get; set; }         // divisão/serviço envolvido
    public string? Suporte { get; set; }         // tipo de suporte prestado

    public int? CategoriaDisiaId { get; set; }
    public CategoriaDisia? Categoria { get; set; }

    public int Quantidade { get; set; } = 1;     // permite registar repetições (ex: "2x")

    public EstadoIntervencao Estado { get; set; } = EstadoIntervencao.Fechada;
    public string? Observacoes { get; set; }

    public string CorEstado => EstadoCores.CorEstadoIntervencao(Estado);

    /// <summary>Equipamento(s) recolhido(s) de uma escola cuja reparação esta atividade agrega e
    /// acompanha (ver fluxo automático em <see cref="Views.IntervencaoEditWindow"/>). Enquanto esta
    /// atividade não estiver Fechada, o estado do equipamento pode ser alterado manualmente para
    /// "Em Reparação"; ao fechar, passa automaticamente a "Aguarda Entrega".</summary>
    public ICollection<EquipamentoRecolhido> EquipamentosRecolhidos { get; set; } = new List<EquipamentoRecolhido>();
}
