using System.Security.Cryptography;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera palavras-passe temporárias aleatórias — usadas por "Repor Password" (ver
/// Views/AdministracaoWindow.xaml.cs) para dar a um utilizador uma nova password sem o
/// administrador ter de escolher/digitar uma. Usa <see cref="RandomNumberGenerator"/>
/// (criptograficamente seguro) — nunca a classe <c>Random</c>, que não é adequada para gerar
/// segredos (é previsível a partir de outputs anteriores).
/// </summary>
public static class GeradorPasswordTemporaria
{
    // Conjuntos de caracteres escolhidos para satisfazer directamente cada uma das 5 regras de
    // PasswordPolicy (maiúscula, minúscula, número, símbolo, comprimento mínimo) e, ao mesmo tempo,
    // evitar caracteres visualmente ambíguos (0/O, 1/l/I) que dificultariam ditar a password ao
    // utilizador ao telefone, ou este a transcrever à mão.
    private const string Maiusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Minusculas = "abcdefghijkmnpqrstuvwxyz";
    private const string Numeros = "23456789";
    private const string Simbolos = "!@#$%*?-_";
    private const string Todos = Maiusculas + Minusculas + Numeros + Simbolos;

    /// <summary>Comprimento total da password gerada — acima do mínimo de 8 exigido por
    /// <see cref="PasswordPolicy"/>, para dar alguma margem extra de segurança a uma password que,
    /// por definição, vai ser comunicada em voz alta ou por escrito a outra pessoa.</summary>
    private const int Comprimento = 12;

    /// <summary>Gera uma nova password aleatória que cumpre sempre <see cref="PasswordPolicy"/>
    /// (garantido por construção: inclui pelo menos um caráter de cada categoria antes de
    /// completar o resto aleatoriamente), usando amostragem sem enviesamento
    /// (<see cref="RandomNumberGenerator.GetInt32(int,int)"/>) e uma baralhada final também
    /// criptograficamente aleatória, para as categorias garantidas não ficarem sempre nas
    /// primeiras posições.</summary>
    public static string Gerar()
    {
        var caracteres = new List<char>(Comprimento)
        {
            Maiusculas[RandomNumberGenerator.GetInt32(Maiusculas.Length)],
            Minusculas[RandomNumberGenerator.GetInt32(Minusculas.Length)],
            Numeros[RandomNumberGenerator.GetInt32(Numeros.Length)],
            Simbolos[RandomNumberGenerator.GetInt32(Simbolos.Length)]
        };

        while (caracteres.Count < Comprimento)
            caracteres.Add(Todos[RandomNumberGenerator.GetInt32(Todos.Length)]);

        // Baralha (Fisher-Yates) com o mesmo gerador criptográfico, para as 4 categorias
        // garantidas acima não ficarem sempre previsivelmente nas primeiras 4 posições.
        for (var i = caracteres.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }

        var password = new string(caracteres.ToArray());

        // Salvaguarda: por construção isto deve ser sempre verdade, mas confirma-se mesmo assim -
        // uma password temporária que não cumprisse a própria política de segurança da aplicação
        // seria um erro grave a passar despercebido.
        System.Diagnostics.Debug.Assert(PasswordPolicy.Validar(password).Valida,
            "GeradorPasswordTemporaria produziu uma password que não cumpre PasswordPolicy.");

        return password;
    }
}
