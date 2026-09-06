using System.ComponentModel.DataAnnotations.Schema;

namespace LeiriaDISIA.Models;

/// <summary>
/// "Modelo base" de equipamento — um catálogo de configurações conhecidas e reutilizáveis
/// (Tipo + Marca + Modelo + características específicas), para quem está a inserir muito
/// equipamento igual (o mesmo lote de computadores, por exemplo) não ter de escrever os mesmos
/// dados uma e outra vez. Gerido em Views/ModelosEquipamentoWindow.xaml.cs (acessível a partir do
/// botão "📋 Modelos de Equipamento" em Views/EquipamentosWindow.xaml) e usado a partir de
/// Views/EquipamentoEditWindow.xaml.cs, através do botão "Usar Modelo..." junto ao Tipo de
/// Equipamento: escolhido um modelo, os seus campos são copiados para o formulário — o resto da
/// lógica de Inserir/Editar Equipamento continua exatamente igual, incluindo poder editar
/// livremente os valores copiados antes de gravar.
///
/// Espelha deliberadamente só os campos que fazem sentido serem IGUAIS entre várias unidades do
/// mesmo modelo (Marca, Modelo, Processador, Memória, etc.) — nunca os campos que são sempre
/// próprios de cada unidade física (Nº de Série, Nº de Inventário, Data de Aquisição, Fornecedor,
/// Escola/Localização, Estado, Observações, histórico de intervenções): estes continuam a ter de
/// ser preenchidos à mão para cada equipamento, tal como antes.
/// </summary>
public class ModeloEquipamento
{
    public int Id { get; set; }

    /// <summary>Tipo de Equipamento (um dos valores configurados em Administração → Dados Fixos →
    /// Tipos de Equipamento) — usado para filtrar a lista de modelos disponíveis, em
    /// Inserir/Editar Equipamento, ao tipo já escolhido nesse formulário.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Nome amigável para identificar este modelo na lista (ex.: "HP EliteBook 840 G8 —
    /// i5/16GB/512GB SSD"). Opcional — quando vazio, a lista mostra Marca + Modelo em alternativa.</summary>
    public string? Nome { get; set; }

    public string? Marca { get; set; }
    public string? Modelo { get; set; }

    /// <summary>Só aparece nos seletores enquanto marcado — um modelo descontinuado pode ser
    /// desativado em vez de eliminado, mantendo o histórico de quem já o usou sem continuar a
    /// oferecê-lo para equipamento novo.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Rótulo mostrado na lista de modelos (ver Views/ModelosEquipamentoWindow.xaml) —
    /// usa o Nome quando preenchido, ou Marca + Modelo como alternativa. Não é gravado na base de
    /// dados.</summary>
    [NotMapped]
    public string RotuloExibicao => !string.IsNullOrWhiteSpace(Nome) ? Nome! : $"{Marca} {Modelo}".Trim();

    /// <summary>Cor do texto na lista de modelos — mais esbatida para um modelo desativado, para
    /// se distinguir visualmente dos ativos sem precisar de o remover da lista. Não é gravada na
    /// base de dados.</summary>
    [NotMapped]
    public string CorTexto => Ativo ? "#111827" : "#9CA3AF";

    // ---- Características específicas: Computadores (PC/Portátil/Servidor) ----
    public string? Processador { get; set; }
    public string? FamiliaProcessador { get; set; }
    public string? TipoMemoria { get; set; }
    public int? QuantidadeMemoriaGB { get; set; }
    public string? TipoDisco { get; set; }
    public int? TamanhoDiscoGB { get; set; }
    public string? SistemaOperativo { get; set; }

    // ---- Características específicas: Monitores ----
    public double? PolegadasMonitor { get; set; }
    public string? TipoPainelMonitor { get; set; }
    public string? ResolucaoMonitor { get; set; }

    // ---- Características específicas: Impressoras / Multifunções ----
    public string? TipoImpressora { get; set; }
    public bool? ImpressaoCor { get; set; }
    public string? LigacaoImpressora { get; set; }

    // ---- Características específicas: Switch / Router / Access Point ----
    public int? NumeroPortas { get; set; }
    public string? VelocidadeRede { get; set; }
    public bool? Gerivel { get; set; }

    // ---- Características específicas: Câmaras CCTV ----
    public string? ResolucaoCamera { get; set; }
    public bool? VisaoNoturna { get; set; }
    public string? TipoCamera { get; set; }

    // ---- Características específicas: Projetores / Quadros Interativos ----
    public int? LuminosidadeLumens { get; set; }
    public string? ResolucaoProjetor { get; set; }

    // ---- Genérico (outros tipos não cobertos acima) ----
    public string? EspecificacoesAdicionais { get; set; }
}
