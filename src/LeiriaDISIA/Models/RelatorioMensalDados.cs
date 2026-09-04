namespace LeiriaDISIA.Models;

/// <summary>Dados mensais complementares usados apenas pelo Relatório Mensal de Atividades em PDF
/// — informação que não existe em mais nenhum lado da aplicação (vem de um sistema externo, a
/// plataforma SIGA) ou que é texto de reflexão redigido/revisto manualmente todos os meses. Um
/// registo por Ano+Mês; ao gerar novamente o relatório do mesmo mês, os valores ficam guardados e
/// prontos a rever/editar, em vez de terem de ser reintroduzidos.</summary>
public class RelatorioMensalDados
{
    public int Id { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }

    // ---- Plataforma SIGA (dados que não existem no LeiriaDISIA, vêm de um sistema externo) ----
    public int TotalAlteracaoTipificacao { get; set; }
    public int TotalEstadoTickets { get; set; }
    public int TotalAlteracaoPasswords { get; set; }

    /// <summary>Total de utilizadores criados na plataforma SIGA durante o mês — atividade que, tal
    /// como as restantes deste bloco, não fica registada em mais nenhum lado da aplicação.</summary>
    public int TotalUtilizadoresCriados { get; set; }

    /// <summary>Capturas de ecrã da plataforma SIGA (lista de pedidos / workflows), anexadas
    /// manualmente todos os meses — guardadas na base de dados para o relatório ficar
    /// autossuficiente e reprodutível sem depender de ficheiros externos.</summary>
    public byte[]? ImagemPedidosSiga { get; set; }
    public byte[]? ImagemWorkflowSiga { get; set; }

    // ---- Reflexão Crítica — rascunho gerado automaticamente a partir dos dados do mês, e depois
    // revisto/editado manualmente antes de gerar o PDF final. ----
    public string? TextoBalancoGeral { get; set; }
    public string? TextoDesafios { get; set; }
    public string? TextoPropostas { get; set; }
    public string? TextoNotaFinal { get; set; }
}
