namespace LeiriaDISIA.Models;

/// <summary>
/// Um registo de auditoria: uma ação relevante para a segurança/integridade da aplicação (login,
/// criação/edição/eliminação de registos, reposição de password, etc.), com quem a fez, quando, e
/// se teve sucesso ou falhou.
///
/// <see cref="Acao"/> e <see cref="Resultado"/> são texto livre (não um enum fixo) porque a
/// aplicação foi desenhada para permitir a um administrador acrescentar novos tipos de ação/
/// resultado em Administração → Dados Fixos (grupos <see cref="GruposValorFixo.AcaoAuditoria"/> e
/// <see cref="GruposValorFixo.ResultadoAuditoria"/>), sem precisar de alterar/recompilar código -
/// ver <see cref="Services.AuditoriaService"/> para o mecanismo que já regista automaticamente a
/// criação/eliminação de qualquer tipo de registo em qualquer módulo (Data.AppDbContext.SaveChanges),
/// mesmo para tipos de registo que ainda não tenham sido explicitamente pensados aqui.
/// </summary>
public class RegistoAuditoria
{
    public int Id { get; set; }

    public DateTime DataHora { get; set; } = DateTime.Now;

    /// <summary>Nome de utilizador (ver <see cref="Usuario.NomeUtilizador"/>) de quem executou a
    /// ação — "sistema" quando não há ninguém autenticado no momento (ex.: uma tentativa de login
    /// falhada com um nome de utilizador que nem sequer existe).</summary>
    public string Utilizador { get; set; } = "sistema";

    /// <summary>Ex.: "Login", "CriarUtilizador", "EliminarEscola", "ReporPassword" - ver
    /// <see cref="GruposValorFixo.AcaoAuditoria"/> para os valores geridos em Dados Fixos.</summary>
    public string Acao { get; set; } = string.Empty;

    /// <summary>Informação adicional em texto livre (ex.: "Password incorreta.", ou uma breve
    /// descrição do registo criado/eliminado) - pode ficar vazio.</summary>
    public string? Detalhe { get; set; }

    /// <summary>Ex.: "Sucesso", "Falha" - ver <see cref="GruposValorFixo.ResultadoAuditoria"/>.</summary>
    public string Resultado { get; set; } = "Sucesso";
}
