namespace LeiriaDISIA.Models;

/// <summary>
/// Agrupamento de Escolas do concelho de Leiria.
/// </summary>
public class Agrupamento
{
    public int Id { get; set; }

    /// <summary>Código do agrupamento (CodAgrupamento no ficheiro GEPE original).</summary>
    public int CodAgrupamento { get; set; }

    public string Nome { get; set; } = string.Empty;

    /// <summary>Forma curta do nome do agrupamento (ex.: "AE Leiria"), usada nas séries e nos
    /// eixos dos gráficos de barras do Dashboard, onde o nome completo não cabe legivelmente.
    /// Se ficar vazia, o Dashboard gera uma aproximação automática a partir do nome completo.</summary>
    public string? Abreviatura { get; set; }

    /// <summary>Nome do diretor do agrupamento. Textual e opcional.</summary>
    public string? Diretor { get; set; }

    public string? Observacoes { get; set; }

    // ---- Dados de contacto (importados da aba "Agrupamentos") ----
    public string? Morada { get; set; }
    public string? Contacto1 { get; set; }
    public string? Contacto2 { get; set; }
    public string? Contacto3 { get; set; }
    public string? Email1 { get; set; }
    public string? Email2 { get; set; }
    public string? Site { get; set; }


    public ICollection<Escola> Escolas { get; set; } = new List<Escola>();

    // Propriedade calculada, útil para relatórios e grelhas (não mapeada pelo EF).
    public int TotalEscolas => Escolas?.Count ?? 0;
}
