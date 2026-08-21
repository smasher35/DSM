namespace LeiriaDISIA.Models;

/// <summary>
/// Contacto associado a uma escola (funcionário, professor, coordenador, etc.)
/// - equivalente à aba "Contactos" do ficheiro_base.xlsx.
/// </summary>
public class Contacto
{
    public int Id { get; set; }

    public int? EscolaId { get; set; }
    public Escola? Escola { get; set; }

    public string Nome { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public string? Telemovel { get; set; }
    public string? Email { get; set; }
    public string? Funcao { get; set; }   // ex: Auxiliar, Coordenador, Professor

    /// <summary>Entidade externa a que o contacto pertence (ex.: empresa, câmara municipal,
    /// fornecedor), quando não está diretamente ligado a uma escola do sistema ou quando essa
    /// informação é relevante para além da escola. Textual e opcional.</summary>
    public string? EntidadeExterna { get; set; }

    public string? Observacoes { get; set; }
}
