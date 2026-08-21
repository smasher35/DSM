using LeiriaDISIA.Data;

namespace LeiriaDISIA.Services;

/// <summary>
/// Atribui automaticamente o Código de Escola (<see cref="Models.Escola.CodEscola"/>) — um código
/// único e incremental gerado pela própria aplicação (ex.: "EB0001", "JI0001"), que passou a
/// substituir o antigo comportamento em que este campo era, na prática, uma cópia editável do
/// Código GEPE. O utilizador nunca o edita diretamente: é sempre calculado aqui, tanto ao criar
/// uma escola manualmente como ao importar de Excel.
/// </summary>
public static class CodigoEscolaService
{
    /// <summary>Prefixo do código consoante o tipo de estabelecimento.</summary>
    public static string Prefixo(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return "EB";
        if (tipo.Contains("Jardim", StringComparison.OrdinalIgnoreCase) || tipo.Trim().Equals("JI", StringComparison.OrdinalIgnoreCase))
            return "JI";
        if (tipo.Contains("Secund", StringComparison.OrdinalIgnoreCase)) return "SEC";
        if (tipo.Contains("Centro Escolar", StringComparison.OrdinalIgnoreCase)) return "CE";
        return "EB";
    }

    /// <summary>Calcula o próximo código disponível para o tipo indicado (ex.: "EB0007"),
    /// olhando para o maior número já atribuído com o mesmo prefixo.</summary>
    public static string ProximoCodigo(AppDbContext db, string? tipo)
    {
        var contadores = ObterContadoresIniciais(db);
        return ProximoCodigo(contadores, tipo);
    }

    /// <summary>Lê da base de dados o maior número já atribuído para cada prefixo conhecido,
    /// para servir de ponto de partida a uma série de atribuições em memória (ver
    /// <see cref="ProximoCodigo(System.Collections.Generic.Dictionary{string,int},string)"/>) —
    /// necessário ao importar várias escolas novas de uma só vez, antes de gravar cada uma
    /// individualmente na base de dados (o que impediria detetar os números já "reservados"
    /// pelas escolas anteriores do mesmo lote).</summary>
    public static Dictionary<string, int> ObterContadoresIniciais(AppDbContext db)
    {
        var contadores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var codigo in db.Escolas.Select(e => e.CodEscola).AsEnumerable())
        {
            if (string.IsNullOrEmpty(codigo)) continue;
            var prefixoLetras = new string(codigo.TakeWhile(char.IsLetter).ToArray());
            var resto = codigo[prefixoLetras.Length..];
            if (prefixoLetras.Length == 0 || resto.Length == 0 || !resto.All(char.IsDigit)) continue;

            var numero = int.Parse(resto);
            if (!contadores.TryGetValue(prefixoLetras, out var atual) || numero > atual)
                contadores[prefixoLetras] = numero;
        }
        return contadores;
    }

    /// <summary>Sobrecarga em memória de <see cref="ProximoCodigo(AppDbContext,string)"/>, para usar
    /// quando várias escolas vão ser criadas em sequência antes de qualquer uma delas ser gravada.</summary>
    public static string ProximoCodigo(Dictionary<string, int> contadores, string? tipo)
    {
        var prefixo = Prefixo(tipo);
        contadores.TryGetValue(prefixo, out var atual);
        atual++;
        contadores[prefixo] = atual;
        return $"{prefixo}{atual:D4}";
    }
}
