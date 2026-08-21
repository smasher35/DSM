using System.Security.Cryptography;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera e valida hashes de palavra-passe usando PBKDF2 (Rfc2898DeriveBytes),
/// evitando qualquer dependência externa.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iteracoes = 100_000;

    public static (string Hash, string Salt) CriarHash(string palavraPasse)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(palavraPasse, salt, Iteracoes, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Validar(string palavraPasse, string hashGuardado, string saltGuardado)
    {
        var salt = Convert.FromBase64String(saltGuardado);
        var hashCalculado = Rfc2898DeriveBytes.Pbkdf2(palavraPasse, salt, Iteracoes, HashAlgorithmName.SHA256, HashSize);
        var hashEsperado = Convert.FromBase64String(hashGuardado);
        return CryptographicOperations.FixedTimeEquals(hashCalculado, hashEsperado);
    }
}
