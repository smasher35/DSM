namespace LeiriaDISIA.Services;

/// <summary>
/// Regras mínimas de segurança para palavras-passe de utilizadores da aplicação:
/// mínimo 8 caracteres, pelo menos uma maiúscula, uma minúscula, um número e um símbolo.
/// </summary>
public static class PasswordPolicy
{
    public class Resultado
    {
        public bool ComprimentoOk { get; init; }
        public bool MaiusculaOk { get; init; }
        public bool MinusculaOk { get; init; }
        public bool NumeroOk { get; init; }
        public bool SimboloOk { get; init; }

        /// <summary>Número de regras cumpridas (0 a 5), usado para desenhar a barra de força.</summary>
        public int TotalCumpridas =>
            (ComprimentoOk ? 1 : 0) + (MaiusculaOk ? 1 : 0) + (MinusculaOk ? 1 : 0) +
            (NumeroOk ? 1 : 0) + (SimboloOk ? 1 : 0);

        /// <summary>Só é considerada válida quando TODAS as regras estão cumpridas.</summary>
        public bool Valida => ComprimentoOk && MaiusculaOk && MinusculaOk && NumeroOk && SimboloOk;
    }

    public static Resultado Validar(string? password)
    {
        password ??= string.Empty;

        return new Resultado
        {
            ComprimentoOk = password.Length >= 8,
            MaiusculaOk = password.Any(char.IsUpper),
            MinusculaOk = password.Any(char.IsLower),
            NumeroOk = password.Any(char.IsDigit),
            SimboloOk = password.Any(c => !char.IsLetterOrDigit(c))
        };
    }

    /// <summary>Cor associada ao nível de força atual (0-5 regras cumpridas), para a barra visual.</summary>
    public static string CorParaNivel(int totalCumpridas) => totalCumpridas switch
    {
        <= 1 => "#EF4444", // vermelho - muito fraca
        2 or 3 => "#F59E0B", // laranja - fraca/média
        4 => "#EAB308", // amarelo - boa
        5 => "#22C55E", // verde - forte
        _ => "#E5E7EB"
    };
}
