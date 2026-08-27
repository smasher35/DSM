using System.Text.RegularExpressions;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Services;

public partial class RelatorioService
{
    // Marcadores usados para pedir ao modelo que separe claramente as quatro secções na resposta,
    // para depois se conseguir "recortar" cada uma delas do texto gerado (ver
    // ExtrairSeccao). Escolhidos por serem muito improváveis de aparecer por acaso no meio do
    // texto normal gerado pelo modelo.
    private const string MarcadorBalanco = "@@BALANCO@@";
    private const string MarcadorDesafios = "@@DESAFIOS@@";
    private const string MarcadorPropostas = "@@PROPOSTAS@@";
    private const string MarcadorNotaFinal = "@@NOTA_FINAL@@";
    private const string MarcadorFim = "###FIM###";

    /// <summary>Igual ao <see cref="GerarRascunhoReflexaoCritica"/> (baseado num modelo de texto
    /// fixo, só com números a variar), mas usando em vez disso um modelo de linguagem (LLM) que
    /// corre inteiramente na máquina local através do <see cref="IaLocalService"/> — sem qualquer
    /// dado a sair para a cloud. Como o texto é efetivamente redigido pelo modelo a partir dos
    /// dados reais do mês, cada rascunho fica diferente e adaptado ao que realmente foi feito, em
    /// vez de repetir sempre as mesmas frases.
    ///
    /// Os quatro parâmetros opcionais <paramref name="indicacaoBalanco"/>, <paramref name="indicacaoDesafios"/>,
    /// <paramref name="indicacaoPropostas"/> e <paramref name="indicacaoNotaFinal"/> correspondem
    /// ao que o utilizador já tenha escrito manualmente nos quatro campos do relatório antes de
    /// pedir o rascunho por IA (ver Views/RelatoriosWindow.xaml.cs). Quando não vazios, são
    /// tratados como ORIENTAÇÃO/CONTEXTO fornecido pelo utilizador para essa secção em concreto -
    /// a IA usa-os para elaborar um texto profissional e coerente, mas não os copia literalmente
    /// (ver <see cref="ConstruirPromptReflexaoCritica"/>). Cada campo é independente: pode vir só
    /// um preenchido e os outros três vazios, sem que isso mude o comportamento dos restantes
    /// (que continuam a ser gerados exatamente como antes, a partir só dos dados do mês). Quando
    /// os quatro vêm vazios (ou esta sobrecarga não é chamada), o comportamento é EXATAMENTE o
    /// mesmo que já existia antes desta alteração.
    ///
    /// Se, por qualquer motivo (modelo não instalado/configurado, resposta mal formatada, etc.)
    /// não for possível obter alguma das quatro secções a partir da IA, essa secção em concreto
    /// usa o rascunho do método determinístico como rede de segurança — nunca fica um campo
    /// completamente vazio por falha da IA.</summary>
    public async Task<(string Balanco, string Desafios, string Propostas, string NotaFinal)> GerarRascunhoReflexaoCriticaIaAsync(
        int ano, int mes, string? indicacaoBalanco = null, string? indicacaoDesafios = null,
        string? indicacaoPropostas = null, string? indicacaoNotaFinal = null, CancellationToken ct = default)
    {
        var mesFormatado = $"{NomesMeses[mes]} de {ano}";

        var intervencoes = _db.Intervencoes
            .Include(i => i.Agrupamento)
            .Include(i => i.Escola)
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
        var atividadesPendentesGeral = _db.AtividadesDisia
            .Count(a => a.Estado != EstadoIntervencao.Fechada && a.Estado != EstadoIntervencao.Cancelada);

        var equipamentoRecolhidoNoMes = _db.EquipamentosRecolhidos
            .Select(r => r.DataRecolha)
            .AsEnumerable()
            .Count(data => data.Year == ano && data.Month == mes);

        var dadosSiga = _db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes)
            ?? new RelatorioMensalDados { Ano = ano, Mes = mes };
        var totalTicketsSiga = dadosSiga.TotalAlteracaoTipificacao + dadosSiga.TotalEstadoTickets;

        var escolasMaisIntervencionadas = intervencoes
            .Where(i => i.EscolaId != null)
            .GroupBy(i => i.Escola?.Nome ?? "—")
            .Select(g => new { Escola = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .Take(3)
            .ToList();

        var prompt = ConstruirPromptReflexaoCritica(
            mesFormatado, intervencoes.Count, agrupamentosEnvolvidos, porCategoria.Select(c => (c.Categoria!, c.Total)).ToList(),
            atividadesDisia.Count, atividadesDisia.Sum(a => a.Quantidade), atividadesPendentesGeral,
            equipamentoRecolhidoNoMes, totalTicketsSiga, dadosSiga.TotalAlteracaoPasswords,
            escolasMaisIntervencionadas.Select(e => (e.Escola, e.Total)).ToList(),
            indicacaoBalanco, indicacaoDesafios, indicacaoPropostas, indicacaoNotaFinal);

        string textoGerado;
        try
        {
            textoGerado = await IaLocalService.Instancia.GerarTextoAsync(prompt, ct: ct);
        }
        catch (Exception ex)
        {
            // Se a IA local falhar (modelo não configurado, ficheiro em falta, sem memória
            // suficiente, etc.), não se bloqueia o utilizador — usa-se o rascunho determinístico
            // completo como alternativa, e a mensagem de erro é propagada para quem chamou poder
            // avisar o utilizador, se quiser.
            var (balancoFallback, desafiosFallback, propostasFallback, notaFinalFallback) =
                GerarRascunhoReflexaoCritica(ano, mes);
            throw new IaLocalIndisponivelException(ex.Message, balancoFallback, desafiosFallback, propostasFallback, notaFinalFallback);
        }

        // Rede de segurança: se alguma secção não vier corretamente delimitada na resposta do
        // modelo (acontece ocasionalmente com modelos mais pequenos, que nem sempre seguem o
        // formato pedido à letra), usa-se o rascunho determinístico só para essa secção em falta.
        var (balancoBase, desafiosBase, propostasBase, notaFinalBase) = GerarRascunhoReflexaoCritica(ano, mes);

        var balanco = ExtrairSeccao(textoGerado, MarcadorBalanco, MarcadorDesafios) is { Length: > 0 } b ? b : balancoBase;
        var desafios = ExtrairSeccao(textoGerado, MarcadorDesafios, MarcadorPropostas) is { Length: > 0 } d ? d : desafiosBase;
        var propostas = ExtrairSeccao(textoGerado, MarcadorPropostas, MarcadorNotaFinal) is { Length: > 0 } p ? p : propostasBase;
        var notaFinal = ExtrairSeccao(textoGerado, MarcadorNotaFinal, MarcadorFim) is { Length: > 0 } n ? n : notaFinalBase;

        return (balanco, desafios, propostas, notaFinal);
    }

    /// <summary>Extrai o texto entre dois marcadores (o segundo pode não existir, se for a última
    /// secção — nesse caso vai até ao fim do texto), aparando espaços e o próprio marcador final
    /// caso o modelo o tenha incluído dentro do texto por engano.</summary>
    private static string ExtrairSeccao(string texto, string marcadorInicio, string marcadorFim)
    {
        var indiceInicio = texto.IndexOf(marcadorInicio, StringComparison.OrdinalIgnoreCase);
        if (indiceInicio < 0) return "";
        indiceInicio += marcadorInicio.Length;

        var indiceFim = texto.IndexOf(marcadorFim, indiceInicio, StringComparison.OrdinalIgnoreCase);
        var trecho = indiceFim >= 0 ? texto[indiceInicio..indiceFim] : texto[indiceInicio..];

        // Remove marcadores residuais e espaço em excesso, para o texto ficar limpo mesmo que o
        // modelo tenha repetido algum marcador dentro da própria secção.
        trecho = Regex.Replace(trecho, @"@@\w+@@|###\w+###", "").Trim();
        return trecho;
    }

    /// <summary>Constrói o prompt em português, com os dados reais do mês, pedindo ao modelo local
    /// que escreva as quatro secções da Reflexão Crítica num estilo formal/administrativo,
    /// delimitadas pelos marcadores acordados — para depois se conseguir separar cada secção do
    /// texto gerado (ver <see cref="ExtrairSeccao"/>).
    ///
    /// Quando o utilizador já tiver escrito alguma indicação manual num dos quatro campos (ver
    /// <see cref="GerarRascunhoReflexaoCriticaIaAsync"/>), essa indicação é acrescentada ao
    /// prompt, um bloco "CONTEXTO ADICIONAL FORNECIDO PELO UTILIZADOR" com um parágrafo por campo
    /// preenchido (secção 3.7 do pedido) — nunca um bloco com os quatro títulos completos quando só
    /// alguns estão preenchidos, para não sugerir ao modelo que os vazios também têm indicação (é
    /// isso que faz o comportamento continuar exatamente igual ao atual nos campos vazios).</summary>
    private static string ConstruirPromptReflexaoCritica(
        string mesFormatado, int totalIntervencoes, int agrupamentosEnvolvidos,
        List<(string Categoria, int Total)> porCategoria, int totalAtividadesDisia, int totalServicosDisia,
        int atividadesPendentesGeral, int equipamentoRecolhidoNoMes, int totalTicketsSiga, int totalPasswords,
        List<(string Escola, int Total)> escolasMaisIntervencionadas,
        string? indicacaoBalanco = null, string? indicacaoDesafios = null,
        string? indicacaoPropostas = null, string? indicacaoNotaFinal = null)
    {
        var listaCategorias = porCategoria.Count == 0
            ? "sem dados de categorias este mês"
            : string.Join(", ", porCategoria.Take(5).Select(c => $"{c.Categoria} ({c.Total})"));

        var listaEscolas = escolasMaisIntervencionadas.Count == 0
            ? "sem dados suficientes"
            : string.Join(", ", escolasMaisIntervencionadas.Select(e => $"{e.Escola} ({e.Total} intervenções)"));

        // Um parágrafo por campo preenchido, na ordem em que aparecem no relatório - um campo
        // vazio simplesmente não gera parágrafo nenhum aqui (ver o comentário do método).
        var indicacoes = new List<string>();
        if (!string.IsNullOrWhiteSpace(indicacaoBalanco))
            indicacoes.Add($"Balanço de Mês:\n{indicacaoBalanco.Trim()}");
        if (!string.IsNullOrWhiteSpace(indicacaoDesafios))
            indicacoes.Add($"Principais Desafios e Constrangimentos:\n{indicacaoDesafios.Trim()}");
        if (!string.IsNullOrWhiteSpace(indicacaoPropostas))
            indicacoes.Add($"Propostas de Melhoria para os Próximos Meses:\n{indicacaoPropostas.Trim()}");
        if (!string.IsNullOrWhiteSpace(indicacaoNotaFinal))
            indicacoes.Add($"Nota Final:\n{indicacaoNotaFinal.Trim()}");

        // Bloco só incluído no prompt quando existe pelo menos uma indicação - um prompt sem
        // nenhum campo preenchido fica IDÊNTICO ao que já existia antes desta alteração (ver
        // requisito de comportamento incremental).
        var blocoIndicacoes = indicacoes.Count == 0 ? "" : $"""


            CONTEXTO ADICIONAL FORNECIDO PELO UTILIZADOR

            O utilizador que está a preparar este relatório já escreveu as seguintes indicações,
            para as secções abaixo indicadas. Usa estas indicações como orientação/informação real
            sobre o que aconteceu no mês para escreveres essa secção em concreto — não as copies
            literalmente, mas redige um texto profissional e coerente com o resto do relatório a
            partir delas. Não inventes factos que as contradigam. As secções não mencionadas aqui
            (se houver) devem continuar a ser escritas apenas com base nos dados reais acima.

            {string.Join("\n\n", indicacoes)}

            """;

        return $"""
            És um técnico superior de informática da Divisão de Sistemas de Informação (DISIA) da
            Câmara Municipal de Leiria, responsável pelo apoio técnico informático às escolas do
            concelho. Escreves em português de Portugal, num registo formal e técnico, próprio de
            um relatório administrativo mensal a apresentar à hierarquia.

            Com base nos dados reais abaixo, relativos ao mês de {mesFormatado}, escreve as quatro
            secções da "Reflexão Crítica" do relatório mensal de atividades. Cada secção deve ter
            2 a 4 parágrafos, adaptados aos números fornecidos (não inventes números nem factos que
            não estejam nos dados). Varia o vocabulário e a estrutura das frases em relação a meses
            anteriores, para o texto não soar repetitivo.

            Dados reais do mês:
            - Intervenções realizadas nas escolas: {totalIntervencoes}, envolvendo {agrupamentosEnvolvidos} agrupamento(s) de escolas.
            - Categorias mais frequentes das intervenções: {listaCategorias}.
            - Escolas com mais intervenções este mês: {listaEscolas}.
            - Atividades DISIA (fora das escolas, ex: instalações municipais/juntas de freguesia) realizadas: {totalAtividadesDisia}, totalizando {totalServicosDisia} serviço(s) prestado(s).
            - Atividades DISIA ainda pendentes/por concluir (de qualquer mês, acumulado): {atividadesPendentesGeral}.
            - Equipamento informático recolhido para reparação este mês: {equipamentoRecolhidoNoMes}.
            - Pedidos tratados na plataforma SIGA (tipificação/estado): {totalTicketsSiga}; alterações de password: {totalPasswords}.
            {blocoIndicacoes}
            Responde EXATAMENTE no seguinte formato, sem nenhum texto antes do primeiro marcador
            nem comentários fora das secções:

            {MarcadorBalanco}
            (aqui o texto do Balanço Geral do Mês)
            {MarcadorDesafios}
            (aqui o texto dos Principais Desafios e Constrangimentos)
            {MarcadorPropostas}
            (aqui o texto das Propostas de Melhoria para os Próximos Meses)
            {MarcadorNotaFinal}
            (aqui o texto da Nota Final, em tom de disponibilidade e compromisso)
            {MarcadorFim}
            """;
    }
}

/// <summary>Lançada quando a geração de texto por IA local falha (modelo não configurado, sem
/// memória, ficheiro em falta, etc.). Transporta também um rascunho de alternativa (o mesmo do
/// método determinístico), para quem apanhar a exceção poder optar por usá-lo em vez de deixar o
/// utilizador sem nenhum rascunho.</summary>
public sealed class IaLocalIndisponivelException(
    string motivo, string balancoAlternativo, string desafiosAlternativo, string propostasAlternativo, string notaFinalAlternativo)
    : Exception(motivo)
{
    public string BalancoAlternativo { get; } = balancoAlternativo;
    public string DesafiosAlternativo { get; } = desafiosAlternativo;
    public string PropostasAlternativo { get; } = propostasAlternativo;
    public string NotaFinalAlternativo { get; } = notaFinalAlternativo;
}
