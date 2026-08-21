using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LeiriaDISIA.Services;

/// <summary>
/// Normaliza nomes de escolas para permitir comparar, por exemplo,
/// "EB1 Amor" (aba "Lista de Escolas") com
/// "Escola Básica do 1.º Ciclo de Amor" (aba "GEPE"),
/// reconhecendo-os como a mesma escola.
/// </summary>
public static class TextNormalizer
{
    // Prefixos/expressões comuns que não ajudam a identificar a escola em si.
    private static readonly string[] StopWords =
    {
        "escola basica do 1 ciclo com jardim de infancia de",
        "escola basica do 1 ciclo com jardim de infancia da",
        "escola basica do 1 ciclo com jardim de infancia",
        "escola basica do 1 ciclo de",
        "escola basica do 1 ciclo da",
        "escola basica do 1 ciclo",
        "escola basica 1 ciclo de",
        "escola basica 1 ciclo da",
        "escola basica 1 ciclo",
        "jardim de infancia de",
        "jardim de infancia da",
        "jardim de infancia",
        "centro escolar de",
        "centro escolar da",
        "centro escolar",
        "escola basica de",
        "escola basica da",
        "escola basica",
        "eb1",
        "eb 1",
        "eb23",
        "eb 2,3",
        "ji",
        "ce"
    };

    /// <summary>Remove acentuação, pontuação, espaços múltiplos e passa a minúsculas.</summary>
    public static string RemoveAccentsAndPunctuation(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var result = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        result = Regex.Replace(result, @"[.,ºª'’\-\(\)]", " ");
        result = Regex.Replace(result, @"\s+", " ").Trim();
        return result;
    }

    /// <summary>
    /// Produz uma "chave canónica" do nome da escola: remove acentos/pontuação e
    /// tenta remover os prefixos habituais (Escola Básica..., Centro Escolar..., EB1...)
    /// para sobrar apenas o "núcleo" identificador (ex: "amor", "cruz de areia (barreira)").
    /// </summary>
    public static string CanonicalSchoolKey(string nomeEscola)
    {
        var texto = RemoveAccentsAndPunctuation(nomeEscola);

        foreach (var stop in StopWords.OrderByDescending(s => s.Length))
        {
            if (texto.StartsWith(stop + " ") || texto == stop)
            {
                texto = texto[stop.Length..].Trim();
                break; // só um prefixo costuma aplicar-se
            }
        }

        return texto;
    }

    /// <summary>Distância de Levenshtein simples, usada como critério de semelhança adicional.</summary>
    public static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }

    /// <summary>
    /// Verifica se dois nomes de escola provavelmente representam a mesma escola:
    /// - chave canónica igual, ou
    /// - uma chave contém a outra, ou
    /// - distância de Levenshtein pequena face ao tamanho do texto.
    /// </summary>
    public static bool AreLikelySameSchool(string nomeA, string nomeB)
    {
        var keyA = CanonicalSchoolKey(nomeA);
        var keyB = CanonicalSchoolKey(nomeB);

        if (keyA.Length == 0 || keyB.Length == 0) return false;
        if (keyA == keyB) return true;
        if (keyA.Contains(keyB) || keyB.Contains(keyA)) return true;

        var distance = LevenshteinDistance(keyA, keyB);
        var maxLen = Math.Max(keyA.Length, keyB.Length);
        var similarity = 1.0 - (double)distance / maxLen;
        return similarity >= 0.85;
    }
}
