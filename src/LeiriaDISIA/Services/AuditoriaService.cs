using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>
/// Regista entradas explícitas no log de Auditoria (Administração → Auditoria) para ações que
/// não correspondem à criação/eliminação de um registo (e por isso não são apanhadas pela
/// auditoria automática de <see cref="Data.AppDbContext.SaveChanges"/>) — nomeadamente o login
/// (com ou sem sucesso) e a gestão de contas de utilizador.
/// </summary>
public static class AuditoriaService
{
    /// <summary>Regista uma entrada de auditoria. Nunca lança exceção — uma falha ao gravar o
    /// registo de auditoria (ex.: base de dados momentaneamente indisponível) não deve impedir a
    /// ação principal que estava a decorrer (login, gravação de um utilizador, etc.).</summary>
    public static void Registar(string acao, string resultado, string? detalhe = null, string? utilizador = null)
    {
        try
        {
            App.Db.RegistosAuditoria.Add(new RegistoAuditoria
            {
                Utilizador = utilizador ?? SessaoAtual.UtilizadorLogado?.NomeUtilizador ?? "sistema",
                Acao = acao,
                Detalhe = detalhe,
                Resultado = resultado
            });
            App.Db.SaveChanges();
        }
        catch
        {
            // Ver o comentário do método - uma falha aqui não deve propagar-se.
        }
    }
}
