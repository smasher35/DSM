using System.Text.RegularExpressions;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>Classificação de obsolescência de um equipamento.</summary>
public enum NivelObsolescencia
{
    /// <summary>Não há dados suficientes (idade e especificações todas em branco) para calcular um score fiável.</summary>
    SemDados,
    Atual,
    AMonitorizar,
    Obsoleto
}

/// <summary>Resultado do cálculo de obsolescência de um equipamento, pronto a mostrar na interface.</summary>
public class ObsolescenciaResultado
{
    /// <summary>Score de 0 (topo de gama / recente) a 100 (claramente obsoleto), ou null se <see cref="Nivel"/> for SemDados.</summary>
    public int? Score { get; set; }
    public NivelObsolescencia Nivel { get; set; }
    public string Classificacao => Nivel switch
    {
        NivelObsolescencia.Atual => "Atual",
        NivelObsolescencia.AMonitorizar => "A Monitorizar",
        NivelObsolescencia.Obsoleto => "Obsoleto",
        _ => "Sem dados"
    };
    public string CorHex => Nivel switch
    {
        NivelObsolescencia.Atual => "#22C55E",
        NivelObsolescencia.AMonitorizar => "#F59E0B",
        NivelObsolescencia.Obsoleto => "#EF4444",
        _ => "#94A3B8"
    };
    /// <summary>Explicação linha-a-linha do cálculo, pronta a mostrar num tooltip.</summary>
    public string Detalhe { get; set; } = "";
}

/// <summary>
/// Calcula um índice de obsolescência (0-100) para equipamento informático, combinando três
/// critérios com pesos configuráveis em Administração → Obsolescência:
///   1. Idade do equipamento face à vida útil típica do seu tipo;
///   2. Especificações técnicas (RAM e tipo de disco), só aplicável a computadores;
///   3. Geração aproximada do processador (heurística, ver <see cref="EstimarFatorProcessador"/>),
///      também só aplicável a computadores.
/// Critérios sem dados (ex: sem data de aquisição, sem RAM preenchida) são excluídos do cálculo
/// e o respetivo peso é redistribuído proporcionalmente pelos critérios que têm valor - um
/// equipamento nunca é penalizado só por faltar preencher um campo opcional.
/// </summary>
public static class ObsolescenciaService
{
    /// <summary>Vida útil típica, em anos, por tipo de equipamento (usada como referência - ver Administração).</summary>
    private static readonly (string Palavra, int Anos)[] VidaUtilPorTipo =
    {
        ("Servidor", 5),
        ("Secretária", 6),
        ("Portátil", 6),
        ("Monitor", 8),
        ("Impressora", 6),
        ("Multifunções", 6),
        ("Switch", 7),
        ("Router", 7),
        ("Access Point", 7),
        ("Câmara", 7),
        ("Projetor", 5),
    };
    private const int VidaUtilPadrao = 6; // anos, para tipos não reconhecidos na lista acima

    private static bool EhComputador(string? tipo) =>
        tipo != null && (
            tipo.Contains("Computador", StringComparison.OrdinalIgnoreCase) ||
            tipo.Contains("Portátil", StringComparison.OrdinalIgnoreCase) ||
            tipo.Contains("Servidor", StringComparison.OrdinalIgnoreCase));

    public static ObsolescenciaResultado Calcular(Equipamento eq)
    {
        var criterios = new List<(double Peso, double Fator, string Texto)>();

        // ---- 1. Idade ----
        if (eq.DataAquisicao is { } dataAquisicao)
        {
            var vidaUtil = VidaUtilPorTipo.FirstOrDefault(v =>
                eq.Tipo != null && eq.Tipo.Contains(v.Palavra, StringComparison.OrdinalIgnoreCase)).Anos;
            if (vidaUtil == 0) vidaUtil = VidaUtilPadrao;

            var idadeAnos = (DateTime.Today - dataAquisicao).TotalDays / 365.25;
            var fatorIdade = Math.Clamp(idadeAnos / vidaUtil, 0, 1);
            criterios.Add((AppSettingsService.ObsolescenciaPesoIdade, fatorIdade,
                $"Idade: {idadeAnos:F1} anos de {vidaUtil} esperados ({fatorIdade * 100:F0}%)"));
        }

        // ---- 2. RAM e 3. Disco e 4. Processador (só computadores) ----
        if (EhComputador(eq.Tipo))
        {
            if (eq.QuantidadeMemoriaGB is { } ram)
            {
                var fatorRam = ram switch { < 4 => 1.0, < 8 => 0.65, < 16 => 0.3, _ => 0.0 };
                criterios.Add((AppSettingsService.ObsolescenciaPesoRam, fatorRam, $"RAM: {ram} GB"));
            }

            if (!string.IsNullOrWhiteSpace(eq.TipoDisco))
            {
                var fatorDisco = eq.TipoDisco switch
                {
                    var t when t.Contains("NVMe", StringComparison.OrdinalIgnoreCase) => 0.0,
                    var t when t.Contains("SSD", StringComparison.OrdinalIgnoreCase) => 0.3,
                    var t when t.Contains("HDD", StringComparison.OrdinalIgnoreCase) => 1.0,
                    _ => 0.5
                };
                criterios.Add((AppSettingsService.ObsolescenciaPesoDisco, fatorDisco, $"Disco: {eq.TipoDisco}"));
            }

            var fatorProcessador = EstimarFatorProcessador(eq.FamiliaProcessador, eq.Processador, out var textoProcessador);
            if (fatorProcessador is { } fp)
                criterios.Add((AppSettingsService.ObsolescenciaPesoProcessador, fp, textoProcessador));
        }

        if (criterios.Count == 0)
        {
            return new ObsolescenciaResultado
            {
                Score = null,
                Nivel = NivelObsolescencia.SemDados,
                Detalhe = "Sem data de aquisição nem especificações preenchidas - não é possível calcular."
            };
        }

        var pesoTotal = criterios.Sum(c => c.Peso);
        var scoreDecimal = pesoTotal <= 0
            ? 0
            : criterios.Sum(c => (c.Peso / pesoTotal) * c.Fator);
        var score = (int)Math.Round(scoreDecimal * 100);

        var nivel = score >= AppSettingsService.ObsolescenciaLimiarObsoleto ? NivelObsolescencia.Obsoleto
            : score >= AppSettingsService.ObsolescenciaLimiarMonitorizar ? NivelObsolescencia.AMonitorizar
            : NivelObsolescencia.Atual;

        var detalhe = string.Join("\n", criterios.Select(c => $"• {c.Texto}")) + $"\n\nScore final: {score}%";

        return new ObsolescenciaResultado { Score = score, Nivel = nivel, Detalhe = detalhe };
    }

    /// <summary>
    /// Heurística para estimar o fator de obsolescência (0=recente, 1=antigo) a partir do texto
    /// livre da família/geração do processador. Isto é uma ESTIMATIVA, não uma leitura garantida:
    /// tenta reconhecer padrões comuns ("12ª Geração", "12th Gen", "i5-12400", "Ryzen 5 5600G"),
    /// mas texto fora destes padrões (ou em branco) resulta em "não determinado" (null), para não
    /// penalizar equipamento só por o campo estar escrito de forma diferente do esperado.
    /// </summary>
    private static double? EstimarFatorProcessador(string? familia, string? processador, out string textoDetalhe)
    {
        var texto = $"{familia} {processador}".Trim();
        textoDetalhe = string.IsNullOrWhiteSpace(texto) ? "Processador: não indicado" : $"Processador: {texto}";

        if (string.IsNullOrWhiteSpace(texto)) return null;

        // "12ª Geração", "12ª Ger", "12th Gen", "Gen 12"
        var matchGeracao = Regex.Match(texto, @"(\d{1,2})\s*[ªº]?\s*(?:Gera[cç][aã]o|Gen)|Gen\D{0,3}(\d{1,2})", RegexOptions.IgnoreCase);
        if (matchGeracao.Success)
        {
            var geracao = int.Parse(matchGeracao.Groups[1].Success ? matchGeracao.Groups[1].Value : matchGeracao.Groups[2].Value);
            return ClassificarGeracaoIntel(geracao);
        }

        // Intel Core no formato "i5-12400", "i7 8700K", "i3-3220"
        var matchIntelModelo = Regex.Match(texto, @"i[3579]-?\s*(\d{4,5})", RegexOptions.IgnoreCase);
        if (matchIntelModelo.Success)
        {
            var numeroModelo = matchIntelModelo.Groups[1].Value;
            // Os primeiros 1-2 dígitos do número de modelo correspondem à geração (ex: 12400 → 12ª geração)
            var geracao = int.Parse(numeroModelo.Length >= 5 ? numeroModelo[..2] : numeroModelo[..1]);
            return ClassificarGeracaoIntel(geracao);
        }

        // AMD Ryzen no formato "Ryzen 5 5600G", "Ryzen 7 3700X"
        var matchRyzen = Regex.Match(texto, @"Ryzen\s*[3579]?\s*(\d)\d{3}", RegexOptions.IgnoreCase);
        if (matchRyzen.Success)
        {
            var serie = int.Parse(matchRyzen.Groups[1].Value);
            return serie switch
            {
                >= 6 => 0.0,  // série 6000+ - recente
                >= 4 => 0.3,  // série 4000-5000
                >= 2 => 0.65, // série 2000-3000
                _ => 1.0
            };
        }

        // Não foi possível reconhecer o padrão - não penalizar, apenas não contar este critério
        return null;
    }

    private static double ClassificarGeracaoIntel(int geracao) => geracao switch
    {
        >= 12 => 0.0,
        >= 8 => 0.3,
        >= 5 => 0.65,
        _ => 1.0
    };
}
