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

    /// <summary>Verdadeiro quando a password atual é temporária (definida por um administrador
    /// através de "Repor Password" — ver Views/AdministracaoWindow.xaml.cs) e ainda não foi
    /// trocada pelo próprio utilizador. Enquanto estiver a true, o login (ver Views/LoginWindow.xaml.cs)
    /// força a apresentação da janela de alteração obrigatória (Views/AlterarPasswordObrigatorioWindow)
    /// antes de dar acesso normal à aplicação.</summary>
    public bool PrecisaAlterarPassword { get; set; } = false;

    /// <summary>Nº de tentativas de login falhadas consecutivas desde o último login bem-sucedido
    /// (ou desde a criação da conta). Reposto a 0 sempre que o login tem sucesso. Quando atinge o
    /// limite configurado em Administração → Segurança, a conta é automaticamente marcada
    /// <see cref="Ativo"/> = false (ver Views/LoginWindow.xaml.cs) - só um administrador a pode
    /// reativar depois, em Administração → Utilizadores.</summary>
    public int TentativasFalhadasConsecutivas { get; set; } = 0;

    /// <summary>
    /// Caminho relativo à pasta de avatares, ou null se sem avatar personalizado.
    /// Formato: "avatares/{UserId}.png"
    /// </summary>
    public string? CaminhoAvatar { get; set; }

    public DateTime DataCriacao { get; set; } = DateTime.Now;
    public DateTime? UltimoLogin { get; set; }
}
