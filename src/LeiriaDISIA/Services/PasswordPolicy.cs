namespace LeiriaDISIA.Services;

/// <summary>
/// Regras de segurança para palavras-passe de utilizadores da aplicação — configuráveis em
/// Administração → Segurança (ver <see cref="AppSettingsService"/>); por omissão, mínimo de 8
/// caracteres com pelo menos uma maiúscula, uma minúscula, um número e um símbolo.
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

        /// <summary>Número de regras cumpridas, entre as que estão atualmente ativas em
        /// Administração → Segurança (ver <see cref="TotalRegrasAtivas"/>) — usado para desenhar a
        /// barra de força.</summary>
        public int TotalCumpridas =>
            (ComprimentoOk ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirMaiuscula && MaiusculaOk ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirMinuscula && MinusculaOk ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirNumero && NumeroOk ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirSimbolo && SimboloOk ? 1 : 0);

        /// <summary>Nº de regras atualmente exigidas (o comprimento conta sempre; as restantes só
        /// se estiverem ativas em Administração → Segurança) — usado como escala máxima da barra
        /// de força, para esta continuar a fazer sentido visualmente mesmo que algumas regras
        /// estejam desativadas.</summary>
        public static int TotalRegrasAtivas => 1 +
            (AppSettingsService.PoliticaPasswordExigirMaiuscula ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirMinuscula ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirNumero ? 1 : 0) +
            (AppSettingsService.PoliticaPasswordExigirSimbolo ? 1 : 0);

        /// <summary>Só é considerada válida quando todas as regras ATUALMENTE ATIVAS (ver
        /// Administração → Segurança) estão cumpridas — uma regra desativada não impede a
        /// validação, seja qual for o seu resultado.</summary>
        public bool Valida =>
            ComprimentoOk &&
            (!AppSettingsService.PoliticaPasswordExigirMaiuscula || MaiusculaOk) &&
            (!AppSettingsService.PoliticaPasswordExigirMinuscula || MinusculaOk) &&
            (!AppSettingsService.PoliticaPasswordExigirNumero || NumeroOk) &&
            (!AppSettingsService.PoliticaPasswordExigirSimbolo || SimboloOk);
    }

    public static Resultado Validar(string? password)
    {
        password ??= string.Empty;

        return new Resultado
        {
            ComprimentoOk = password.Length >= AppSettingsService.PoliticaPasswordMinCaracteres,
            MaiusculaOk = password.Any(char.IsUpper),
            MinusculaOk = password.Any(char.IsLower),
            NumeroOk = password.Any(char.IsDigit),
            SimboloOk = password.Any(c => !char.IsLetterOrDigit(c))
        };
    }

    /// <summary>Cor associada ao nível de força atual, para a barra visual — a escala acompanha
    /// <see cref="Resultado.TotalRegrasAtivas"/>, para continuar proporcional mesmo que algumas
    /// regras estejam desativadas em Administração → Segurança.</summary>
    public static string CorParaNivel(int totalCumpridas)
    {
        var maximo = Resultado.TotalRegrasAtivas;
        var proporcao = maximo == 0 ? 0 : (double)totalCumpridas / maximo;

        return proporcao switch
        {
            <= 0.34 => "#EF4444", // vermelho - muito fraca
            <= 0.67 => "#F59E0B", // laranja - fraca/média
            < 1.0 => "#EAB308",   // amarelo - boa
            _ => "#22C55E"        // verde - forte
        };
    }
}
