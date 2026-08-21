namespace LeiriaDISIA.Models;

public enum PerfilUtilizador
{
    Administrador,
    Utilizador
}

/// <summary>
/// Conta de acesso à aplicação. Palavras-passe nunca são guardadas em texto simples
/// (ver <see cref="Services.PasswordHasher"/>).
/// </summary>
public class Usuario
{
    public int Id { get; set; }

    public string NomeUtilizador { get; set; } = string.Empty; // login
    public string NomeCompleto { get; set; } = string.Empty;
    public string? Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;

    public PerfilUtilizador Perfil { get; set; } = PerfilUtilizador.Utilizador;
    public bool Ativo { get; set; } = true;

    /// <summary>
    /// Caminho relativo à pasta de avatares, ou null se sem avatar personalizado.
    /// Formato: "avatares/{UserId}.png"
    /// </summary>
    public string? CaminhoAvatar { get; set; }

    public DateTime DataCriacao { get; set; } = DateTime.Now;
    public DateTime? UltimoLogin { get; set; }
}
