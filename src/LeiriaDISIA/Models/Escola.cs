using System.ComponentModel.DataAnnotations.Schema;

namespace LeiriaDISIA.Models;

public enum TipoEscola
{
    JardimInfancia,
    EB1,
    CentroEscolar,
    EB23,
    Secundaria,
    Outro
}

/// <summary>Estados possíveis de uma <see cref="Escola"/> (campo <see cref="Escola.Estado"/>).
/// A lista completa e editável destes estados vive em Dados Fixos (grupo
/// <see cref="GruposValorFixo.EstadoEscola"/>) — o administrador pode adicionar outros estados
/// além destes três; ver <see cref="Escola.Estado"/>.
/// <see cref="Desativada"/> é o único estado tratado como "sentinela" pelo resto do código: é o
/// que faz a escola desaparecer das listas de seleção e das listagens gerais (ver aba
/// Administração → "Escolas Desativadas"). Qualquer outro estado — incluindo <see cref="EmObras"/>
/// e futuros estados criados pelo administrador — é tratado como escola em uso normal, apenas com
/// uma etiqueta/cor diferente.</summary>
public static class EstadosEscola
{
    public const string Ativa = "Ativa";
    public const string Desativada = "Desativada";
    public const string EmObras = "Em Obras";
}

/// <summary>
/// Escola / Jardim de Infância do concelho de Leiria.
/// Campos base obrigatórios (herdados da aba GEPE do ficheiro_base.xlsx):
/// CodEscola, CodDGRHE, CodGEPE, Escola, Morada, Localidade, Freguesia, CodAgrupamento, NomeDoAgrupamento.
/// Campos adicionais para melhor caracterização (critério da aplicação).
/// </summary>
public class Escola
{
    public int Id { get; set; }

    // ---- Campos obrigatórios da aba GEPE ----
    /// <summary>Código único da escola, atribuído automaticamente pela aplicação (ex.: "EB0001",
    /// "JI0001") — NÃO é o Código GEPE (esse fica em <see cref="CodGEPE"/>) e não é editável pelo
    /// utilizador. Ver <see cref="Services.CodigoEscolaService"/>.</summary>
    public string CodEscola { get; set; } = string.Empty;
    public int? CodDGRHE { get; set; }
    public int? CodGEPE { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Morada { get; set; }
    public string? CodigoPostal { get; set; }
    public string? Localidade { get; set; }
    public string? Freguesia { get; set; }

    public int? AgrupamentoId { get; set; }
    public Agrupamento? Agrupamento { get; set; }

    // ---- Contacto direto da escola (opcional) ----
    public string? Telefone { get; set; }
    public string? Email { get; set; }

    // ---- Campos adicionais de caracterização ----
    public string Tipo { get; set; } = "EB1";
    public int? NumeroAlunos { get; set; }
    public int? NumeroSalas { get; set; }
    public bool TemInternetFibra { get; set; }
    /// <summary>Velocidade contratada da fibra (só relevante quando <see cref="TemInternetFibra"/> é verdadeiro).
    /// Valores sugeridos geridos em Administração → Dados Fixos → Velocidades de Fibra
    /// (ver <see cref="GruposValorFixo.VelocidadeFibra"/>).</summary>
    public string? VelocidadeFibra { get; set; }
    public bool TemCCTV { get; set; }
    public bool TemVPN { get; set; }
    public bool TemBiblioteca { get; set; }
    public string? NomeAlternativo { get; set; }   // usado para deduplicação (ex: "EB1 Amor")

    /// <summary>Estado atual da escola. Valores sugeridos geridos em Administração → Dados Fixos
    /// → Estados de Escola (ver <see cref="GruposValorFixo.EstadoEscola"/> e <see cref="EstadosEscola"/>).
    /// Antes existia apenas um booleano "Ativa"/"Desativada"; passou a texto configurável para
    /// permitir outros estados, como "Em Obras".</summary>
    public string Estado { get; set; } = EstadosEscola.Ativa;

    /// <summary>Indica se a escola está no estado <see cref="EstadosEscola.Desativada"/> — o único
    /// estado que a remove das listas de seleção e das listagens gerais. Não é gravada na base de
    /// dados (ver <see cref="Estado"/>).</summary>
    [NotMapped]
    public bool Desativada => Estado == EstadosEscola.Desativada;

    /// <summary>Cor associada ao <see cref="Estado"/> atual, para apresentação em badges nas
    /// grelhas — ver <see cref="EstadoCores.CorEstadoEscola"/>. Não é gravada na base de dados.</summary>
    [NotMapped]
    public string CorEstado => EstadoCores.CorEstadoEscola(Estado);

    public bool Integrado { get; set; } = false;  // JI integrado num edifício de escola básica
    public string? Observacoes { get; set; }

    // ---- Coordenadas geográficas e imagem ----
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? ImagemCaminho { get; set; }  // caminho local da fotografia da escola

    public ICollection<Contacto> Contactos { get; set; } = new List<Contacto>();
    public ICollection<PedidoIntervencao> Pedidos { get; set; } = new List<PedidoIntervencao>();
    public ICollection<Intervencao> Intervencoes { get; set; } = new List<Intervencao>();
    public ICollection<Equipamento> Equipamentos { get; set; } = new List<Equipamento>();

    // ---- Geocodificação / distância à sede do Município (Planeamento de Rotas) ----
    // Reutiliza os campos Latitude/Longitude já existentes acima (também usados pelo mapa da
    // escola) — recalcular a distância grava as coordenadas obtidas por geocodificação exatamente
    // ali, em vez de duplicar um segundo par de campos. Só substitui um valor lá colocado
    // manualmente quando o utilizador pedir explicitamente "Recalcular Distância".

    /// <summary>Distância rodoviária (não em linha reta) até à sede do Município de Leiria — Largo
    /// da República —, calculada por <see cref="Services.Rotas.IRoutingService"/>. Guardada em
    /// cache aqui para nunca repetir chamadas externas desnecessariamente; só é recalculada quando
    /// o utilizador o pedir explicitamente (ver botão "Recalcular Distância").</summary>
    public double? DistanciaKmSede { get; set; }

    public DateTime? DataUltimoCalculoDistancia { get; set; }

    public override string ToString() => Nome;
}
