using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

public partial class RelatorioService
{
    private static void ComposeReflexaoCritica(ColumnDescriptor col, RelatorioMensalDados dados, string mesFormatado)
    {
        col.Item().Section(SecaoReflexao).Text("Reflexão Crítica").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

        void Bloco(string titulo, string chaveSeccao, string? texto)
        {
            col.Item().Section(chaveSeccao).PaddingTop(14).PaddingBottom(6)
                .Text(titulo).FontSize(12).Bold().FontColor(Colors.Blue.Darken2);

            if (string.IsNullOrWhiteSpace(texto))
            {
                col.Item().Text("(Texto não preenchido para este mês.)")
                    .FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.32f).Italic().Justify();
                return;
            }

            foreach (var paragrafo in DividirEmParagrafos(texto))
            {
                col.Item().PaddingBottom(6).Text(paragrafo)
                    .FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.32f).Justify();
            }
        }

        Bloco("Balanço Geral do Mês", SecaoBalanco, dados.TextoBalancoGeral);
        Bloco("Principais Desafios e Constrangimentos", SecaoDesafios, dados.TextoDesafios);
        Bloco("Propostas de Melhoria para os Próximos Meses", SecaoPropostas, dados.TextoPropostas);
        Bloco("Nota Final", SecaoNotaFinal, dados.TextoNotaFinal);
    }

    /// <summary>Compõe automaticamente um rascunho bem escrito, detalhado e tecnicamente rigoroso
    /// para as quatro secções de Reflexão Crítica, a partir dos dados reais do mês (intervenções,
    /// categorias, atividades da DISIA e estatísticas da plataforma SIGA). O rascunho é pensado
    /// para ser revisto e ajustado no formulário antes de gerar o PDF final — não substitui a
    /// validação humana de um relatório oficial, mas evita começar cada mês com a folha em branco.</summary>
    public (string Balanco, string Desafios, string Propostas, string NotaFinal) GerarRascunhoReflexaoCritica(int ano, int mes)
    {
        var mesFormatado = $"{NomesMeses[mes]} de {ano}";

        var intervencoes = _db.Intervencoes
            .Include(i => i.Agrupamento)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .Where(i => i.Ano == ano && i.Mes == mes && i.Estado != EstadoIntervencao.Cancelada)
            .ToList();

        var agrupamentosEnvolvidos = intervencoes.Where(i => i.AgrupamentoId != null)
            .Select(i => i.AgrupamentoId).Distinct().Count();

        var porCategoria = intervencoes
            .SelectMany(i => i.Categorias)
            .Where(c => c.Categoria != null)
            .GroupBy(c => c.Categoria!.Nome)
            .Select(g => new { Categoria = g.Key, Total = g.Sum(x => x.Quantidade) })
            .OrderByDescending(g => g.Total)
            .ToList();

        var atividadesDisia = _db.AtividadesDisia.Where(a => a.Ano == ano && a.Mes == mes).ToList();
        var dadosSiga = _db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes)
            ?? new RelatorioMensalDados { Ano = ano, Mes = mes };
        var totalTicketsSiga = dadosSiga.TotalAlteracaoTipificacao + dadosSiga.TotalEstadoTickets;

        var categoriaTopo = porCategoria.FirstOrDefault();
        var descricaoCategorias = porCategoria.Count == 0
            ? "sem uma predominância clara de um único tipo de intervenção"
            : string.Join(", ", porCategoria.Take(3).Select(c => $"{c.Categoria} ({c.Total})")) +
              (porCategoria.Count > 3 ? ", entre outras" : "");

        // ---- Balanço Geral ----
        var balanco =
            $"O balanço do mês de {mesFormatado} é globalmente positivo, tendo sido assegurada uma intervenção " +
            $"diversificada nas escolas, na plataforma SIGA e nas restantes atividades da DISIA. " +
            (intervencoes.Count > 0
                ? $"Nas escolas foram realizadas {intervencoes.Count} intervenções, distribuídas por " +
                  $"{agrupamentosEnvolvidos} agrupamento(s), com maior incidência em {descricaoCategorias}. " +
                  "Esta diversidade demonstra capacidade de resposta a diferentes necessidades técnicas, " +
                  "frequentemente numa mesma deslocação, contribuindo para a continuidade do funcionamento dos " +
                  "recursos tecnológicos escolares. "
                : "Não foram registadas intervenções nas escolas durante este mês. ") +
            (totalTicketsSiga > 0
                ? $"Na plataforma SIGA foram efetuadas correções de workflows, tipificações e estados de pedidos, " +
                  $"envolvendo {totalTicketsSiga} ticket(s), permitindo o respetivo encaminhamento e tratamento " +
                  "pelos serviços competentes. "
                : "") +
            (atividadesDisia.Count > 0
                ? $"Paralelamente, foram executadas {atividadesDisia.Count} atividade(s) da DISIA em instalações " +
                  "municipais, juntas de freguesia e outros equipamentos do concelho, totalizando " +
                  $"{atividadesDisia.Sum(a => a.Quantidade)} serviço(s) prestado(s). "
                : "") +
            "De uma forma geral, as atividades desenvolvidas contribuíram para assegurar a operacionalidade dos " +
            "equipamentos e serviços informáticos, corrigir situações administrativas na plataforma SIGA e " +
            "preparar tecnicamente intervenções futuras.";

        // ---- Principais Desafios e Constrangimentos ----
        var desafios =
            "O principal desafio do mês resultou da diversidade técnica e geográfica das intervenções, que " +
            "exigiu capacidade de adaptação a diferentes equipamentos, infraestruturas e contextos de " +
            "funcionamento. A necessidade de conciliar assistências nas escolas com outras atividades da DISIA " +
            "implicou uma gestão cuidada das deslocações, das prioridades e do tempo disponível. " +
            (totalTicketsSiga > 0
                ? "Na plataforma SIGA, a existência de pedidos inseridos em workflows incorretos, com " +
                  "tipificações inadequadas ou estados desatualizados, continua a exigir verificação e correção " +
                  "manual, o que pode dificultar o encaminhamento dos pedidos e aumentar o tempo necessário para " +
                  "a sua resolução. "
                : "") +
            "Nas intervenções relacionadas com redes, fibra ótica e videovigilância, quando aplicável, destaca-se " +
            "ainda a necessidade de articulação com entidades externas e outros técnicos, cuja disponibilidade " +
            "pode condicionar o avanço e a conclusão dos trabalhos.";

        // ---- Propostas de Melhoria ----
        var propostas =
            "Para os próximos meses, propõe-se o reforço do planeamento preventivo das intervenções, através da " +
            "identificação antecipada dos equipamentos com maior probabilidade de avaria ou necessidade de " +
            "substituição, apoiado na classificação de obsolescência já disponível no inventário. " +
            (totalTicketsSiga > 0
                ? "Na plataforma SIGA, considera-se importante sensibilizar os serviços utilizadores para a " +
                  "correta seleção da tipificação, do workflow e do estado dos pedidos, e implementar uma revisão " +
                  "periódica dos tickets, permitindo identificar rapidamente pedidos incorretamente classificados " +
                  "ou sem atualização. "
                : "") +
            "Pretende-se continuar a atualizar e a consolidar o inventário de equipamento informático das " +
            "escolas, bem como o registo sistemático das intervenções realizadas em cada equipamento, permitindo " +
            "um acompanhamento mais rigoroso do histórico de avarias e manutenções, fundamental para apoiar a " +
            "tomada de decisão relativamente à sua substituição ou renovação.";

        // ---- Nota Final ----
        var notaFinal =
            $"As atividades desenvolvidas durante o mês de {mesFormatado} contribuíram para assegurar a " +
            "continuidade, a fiabilidade e a melhoria dos serviços informáticos prestados às escolas, às " +
            "instalações municipais e aos diferentes parceiros envolvidos. A diversidade das intervenções " +
            "realizadas permitiu responder a necessidades de suporte, manutenção, configuração e preparação de " +
            "novos projetos, reforçando a operacionalidade dos equipamentos e das infraestruturas tecnológicas. " +
            "Mantenho o compromisso de colaborar de forma proativa na otimização dos processos, na prevenção de " +
            "ocorrências e na melhoria contínua dos serviços da DISIA, permanecendo disponível para prestar " +
            "quaisquer esclarecimentos ou apoiar as iniciativas que venham a ser consideradas necessárias.";

        return (balanco, desafios, propostas, notaFinal);
    }
}
