using System.ComponentModel.DataAnnotations.Schema;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Models;

/// <summary>Nomes fixos dos estados de equipamento informático, usados pela lógica da aplicação —
/// ver <see cref="GruposValorFixo.EstadoEquipamento"/>. Estes valores têm significado especial
/// (fluxo de recolha/reparação/devolução); evite renomeá-los em Dados Fixos.</summary>
public static class EstadosEquipamento
{
    public const string EmServico = "Em Serviço";

    /// <summary>Equipamento acabado de recolher de uma escola (numa Intervenção), ainda antes de
    /// ser aberta/associada a Atividade DISIA de reparação.</summary>
    public const string Recolhido = "Recolhido";

    public const string EmReparacao = "Em Reparação";
    public const string Reparado = "Reparado";

    /// <summary>Reparação concluída (Atividade DISIA fechada) — aguarda ser devolvido à escola
    /// através do botão "Devolver à Escola" numa nova Intervenção.</summary>
    public const string AguardaEntrega = "Aguarda Entrega";

    public const string EmArmazem = "Em Armazém";
    public const string Abatido = "Abatido";
}

/// <summary>
/// Grupos de características específicas mostrados em "Inserir/Editar Equipamento" (bloco de
/// processador/memória/disco, bloco de monitor, de impressora, etc.), consoante o Tipo de
/// Equipamento escolhido. Cada valor de "Tipo de Equipamento" gerido em Dados Fixos fica ligado
/// a um destes grupos (ver <see cref="LeiriaDISIA.Models.ValorFixo.GrupoCaracteristicas"/>), para
/// que continue a mostrar as características certas mesmo que o nome do tipo seja alterado.
/// </summary>
public static class GruposCaracteristicasEquipamento
{
    public const string Computador = "Computador";
    public const string Monitor = "Monitor";
    public const string Impressora = "Impressora";
    public const string Rede = "Rede";
    public const string Camera = "Câmara";
    public const string Projetor = "Projetor";
    public const string Generico = "Genérico";

    public static readonly string[] Todos =
        { Computador, Monitor, Impressora, Rede, Camera, Projetor, Generico };
}

/// <summary>
/// Equipamento informático. Preparado para uma grande variedade de tipos
/// (PC, portátil, monitor, impressora, switch, router, câmara CCTV, projetor, etc.)
/// Apenas Número de Série e Número de Inventário são obrigatórios; os restantes
/// campos são transversais e opcionais consoante o tipo de equipamento.
/// </summary>
public class Equipamento
{
    public int Id { get; set; }

    // ---- Obrigatórios ----
    public string NumeroSerie { get; set; } = string.Empty;
    public string NumeroInventario { get; set; } = string.Empty;

    // ---- Campos transversais (opcionais) ----
    public string? Tipo { get; set; }          // PC, Portátil, Monitor, Impressora, Switch, Router, Câmara CCTV, etc.
    public string? Marca { get; set; }
    public string? Modelo { get; set; }
    public DateTime? DataAquisicao { get; set; }
    public decimal? ValorAquisicao { get; set; }
    public string? Fornecedor { get; set; }

    // Localização: pode estar numa escola ou noutro local municipal (texto livre)
    public int? EscolaId { get; set; }
    public Escola? Escola { get; set; }
    public string? LocalNaoEscolar { get; set; }

    /// <summary>Em Serviço, Em Reparação, Reparado, Em Armazém ou Abatido — ver <see cref="EstadosEquipamento"/>
    /// e <see cref="GruposValorFixo.EstadoEquipamento"/>.</summary>
    public string Estado { get; set; } = EstadosEquipamento.EmServico;
    public string? Observacoes { get; set; }

    /// <summary>Cor associada ao <see cref="Estado"/> atual, para apresentação em badges nas
    /// grelhas — ver <see cref="EstadoCores.CorEstadoEquipamento"/>. Não é gravada na base de dados.</summary>
    [NotMapped]
    public string CorEstado => EstadoCores.CorEstadoEquipamento(Estado);

    /// <summary>Índice de obsolescência calculado a partir da idade e especificações (ver <see cref="ObsolescenciaService"/>).
    /// Não é gravado na base de dados - é recalculado sempre que é lido.</summary>
    [NotMapped]
    public ObsolescenciaResultado Obsolescencia => ObsolescenciaService.Calcular(this);

    // ---- Características específicas: Computadores (PC/Portátil/Servidor) ----
    public string? Processador { get; set; }
    public string? FamiliaProcessador { get; set; }  // Ex: "12ª Geração", "Ryzen 5 5600G"
    public string? TipoMemoria { get; set; }        // DDR3, DDR4, DDR5...
    public int? QuantidadeMemoriaGB { get; set; }
    public string? TipoDisco { get; set; }          // HDD, SSD, NVMe
    public int? TamanhoDiscoGB { get; set; }
    public string? SistemaOperativo { get; set; }

    // ---- Características específicas: Monitores ----
    public double? PolegadasMonitor { get; set; }
    public string? TipoPainelMonitor { get; set; }  // LED, LCD, OLED
    public string? ResolucaoMonitor { get; set; }   // ex: 1920x1080

    // ---- Características específicas: Impressoras / Multifunções ----
    public string? TipoImpressora { get; set; }     // Laser, Tinta
    public bool? ImpressaoCor { get; set; }
    public string? LigacaoImpressora { get; set; }  // USB, Rede, WiFi

    // ---- Características específicas: Switch / Router / Access Point ----
    public int? NumeroPortas { get; set; }
    public string? VelocidadeRede { get; set; }     // ex: 1 Gbps
    public bool? Gerivel { get; set; }

    // ---- Características específicas: Câmaras CCTV ----
    public string? ResolucaoCamera { get; set; }    // ex: 4MP, 1080p
    public bool? VisaoNoturna { get; set; }
    public string? TipoCamera { get; set; }         // IP, Analógica

    // ---- Características específicas: Projetores / Quadros Interativos ----
    public int? LuminosidadeLumens { get; set; }
    public string? ResolucaoProjetor { get; set; }

    // ---- Genérico (outros tipos não cobertos acima) ----
    public string? EspecificacoesAdicionais { get; set; }

    public EquipamentoAbatido? Abate { get; set; }
}

/// <summary>
/// Registo de abate de equipamento, ligado à tabela de Equipamentos.
/// </summary>
public class EquipamentoAbatido
{
    public int Id { get; set; }

    public int? EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }

    // Caso o equipamento abatido não estivesse ainda cadastrado individualmente
    // (ex.: registos antigos vindos apenas do texto "Material Recolhido/Abatido"),
    // guardamos aqui a informação em texto livre.
    public string? EscolaOuLocal { get; set; }
    public string? DescricaoEquipamento { get; set; }

    /// <summary>N.º de série do equipamento abatido. Textual e opcional — ver migração
    /// que separou este campo do antigo "Número de Série / Inventário" combinado.</summary>
    public string? NumeroSerie { get; set; }

    /// <summary>N.º de inventário do equipamento abatido. Textual e opcional.</summary>
    public string? NumeroInventario { get; set; }

    public DateTime DataAbate { get; set; } = DateTime.Today;
    public string Status { get; set; } = "Abatido";  // Abatido, Em processo de abate, Doado, Reciclado...
    public string? Observacoes { get; set; }

    /// <summary>Intervenção que originou este abate, quando registado diretamente a partir
    /// da janela de Intervenções (opcional — abates também podem ser registados avulsos).</summary>
    public int? IntervencaoId { get; set; }
    public Intervencao? Intervencao { get; set; }
}

/// <summary>
/// Equipamento recolhido de uma escola para ser intervencionado nas instalações da DISIA.
/// É uma mudança TEMPORÁRIA de local: o equipamento continua a existir no inventário e a
/// contar para os totais normais (não é abatido nem desativado), apenas fica associado a este
/// registo enquanto estiver fora da escola. Ao ser entregue de volta, o registo mantém-se
/// para efeitos de histórico mas deixa de aparecer na lista de equipamento atualmente fora.
/// </summary>
public class EquipamentoRecolhido
{
    public int Id { get; set; }

    /// <summary>Só pode ser recolhido equipamento já existente no inventário.</summary>
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }

    /// <summary>Intervenção que originou a recolha (opcional — pode também ser registada avulsa).</summary>
    public int? IntervencaoId { get; set; }
    public Intervencao? Intervencao { get; set; }

    /// <summary>[Obsoleto — mantido apenas para compatibilidade com registos criados por versões
    /// anteriores.] "Intervenção DISIA" criada automaticamente para acompanhar a reparação deste
    /// equipamento. Novas recolhas passaram a usar <see cref="AtividadeDisiaId"/> em vez de uma
    /// Intervenção, para que a reparação apareça no módulo de Atividades DISIA em vez do de
    /// Intervenções.</summary>
    public int? IntervencaoDisiaId { get; set; }
    public Intervencao? IntervencaoDisia { get; set; }

    /// <summary>Atividade DISIA (estado "Em Progresso") criada automaticamente para agregar e
    /// acompanhar a reparação deste equipamento. É esta atividade que, ao ser fechada, atualiza o
    /// estado do equipamento para "Aguarda Entrega" e este registo de recolha para o mesmo estado,
    /// ativando o botão "Devolver à Escola".</summary>
    public int? AtividadeDisiaId { get; set; }
    public AtividadeDisia? AtividadeDisia { get; set; }

    public DateTime DataRecolha { get; set; } = DateTime.Today;

    /// <summary>Pendente, Em Reparação, Aguarda Entrega ou Entregue — ver
    /// <see cref="GruposValorFixo.EstadoRecolha"/>. Estes valores têm significado especial
    /// para a aplicação (cor, filtros, botão de entrega); evite renomeá-los em Dados Fixos.</summary>
    public string Estado { get; set; } = EstadosRecolha.Pendente;

    public DateTime? DataEntrega { get; set; }
    public string? Observacoes { get; set; }

    /// <summary>Dias decorridos desde a recolha (até à entrega, ou até hoje se ainda não foi entregue).</summary>
    public int DiasEmRecolha => (int)((DataEntrega ?? DateTime.Today) - DataRecolha).TotalDays;

    /// <summary>Cor semafórica consoante o tempo de recolha (reutiliza a mesma escala dos Pedidos).</summary>
    public string CorTempo => EstadoCores.CorTempoEmAberto(DiasEmRecolha);

    public bool EstaEntregue => Estado.Equals(EstadosRecolha.Entregue, StringComparison.OrdinalIgnoreCase);

    /// <summary>Cor do badge de Estado (ver <see cref="EstadoCores.CorEstadoRecolha"/>). Não é
    /// gravada na base de dados.</summary>
    public string CorEstado => EstadoCores.CorEstadoRecolha(Estado);

    /// <summary>Só pode ser devolvido à escola depois de a Atividade DISIA que trata da
    /// reparação ser fechada, altura em que este estado passa automaticamente a Aguarda Entrega.</summary>
    public bool PodeSerEntregue => Estado.Equals(EstadosRecolha.AguardaEntrega, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Nomes fixos dos estados de equipamento recolhido, usados pela lógica da aplicação.</summary>
public static class EstadosRecolha
{
    public const string Pendente = "Pendente";
    public const string EmReparacao = "Em Reparação";
    public const string AguardaEntrega = "Aguarda Entrega";
    public const string Entregue = "Entregue";
}
