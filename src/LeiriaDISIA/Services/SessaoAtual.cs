using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>Guarda o utilizador com sessão iniciada na aplicação (single-user desktop app).</summary>
public static class SessaoAtual
{
    public static Usuario? UtilizadorLogado { get; set; }

    public static bool IsAdmin => UtilizadorLogado?.Perfil == PerfilUtilizador.Administrador;

    /// <summary>Perfil "Guest": acesso só de leitura a todos os módulos, sem acesso nenhum ao
    /// menu Administração (tal como um Utilizador comum) — ver <see cref="PodeEditar"/>.</summary>
    public static bool IsGuest => UtilizadorLogado?.Perfil == PerfilUtilizador.Guest;

    /// <summary>False só para o perfil Guest — usado por cada ecrã para decidir se os botões de
    /// inserir/editar/eliminar ficam ativos ou desativados (ver Services/PermissoesService.cs).
    /// Administrador e Utilizador continuam ambos com acesso total de edição, tal como antes de o
    /// perfil Guest existir.</summary>
    public static bool PodeEditar => !IsGuest;

    public static void Terminar() => UtilizadorLogado = null;
}
