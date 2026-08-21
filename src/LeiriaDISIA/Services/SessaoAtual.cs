using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>Guarda o utilizador com sessão iniciada na aplicação (single-user desktop app).</summary>
public static class SessaoAtual
{
    public static Usuario? UtilizadorLogado { get; set; }

    public static bool IsAdmin => UtilizadorLogado?.Perfil == PerfilUtilizador.Administrador;

    public static void Terminar() => UtilizadorLogado = null;
}
