namespace LeiriaDISIA.Models;

/// <summary>
/// Ligação de comunicações (fibra, ADSL, 4G/5G, satélite, etc.) associada a um Jardim de
/// Infância / Escola. Serve para gerir ligações que ainda NÃO estão integradas na rede
/// municipal (e/ou registar algumas das já integradas, para efeitos de inventário completo),
/// independentemente do que já está assinalado no cartão "Tem internet por fibra" da escola.
/// </summary>
public class Comunicacao
{
    public int Id { get; set; }

    public int EscolaId { get; set; }
    public Escola? Escola { get; set; }

    /// <summary>Fibra, ADSL, 4G/5G, Satélite, Outro...</summary>
    public string TipoLigacao { get; set; } = "Fibra";

    /// <summary>Velocidade contratada (só aplicável a ligações de fibra).
    /// Ver <see cref="GruposValorFixo.VelocidadeFibra"/>.</summary>
    public string? VelocidadeFibra { get; set; }

    public string? Operadora { get; set; }
    public string? NumeroContrato { get; set; }
    public DateTime? DataInstalacao { get; set; }

    /// <summary>Indica se esta ligação já está integrada na rede/gestão centralizada da DISIA.</summary>
    public bool Integrado { get; set; }

    /// <summary>Ativa, Inativa, Pendente de Instalação, Pendente de Integração...</summary>
    public string Estado { get; set; } = "Ativa";

    public string? Observacoes { get; set; }
}
