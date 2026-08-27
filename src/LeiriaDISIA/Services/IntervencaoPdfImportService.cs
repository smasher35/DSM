using System.Globalization;
using System.Text.RegularExpressions;
using LeiriaDISIA.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LeiriaDISIA.Services;

/// <summary>
/// Lê um Relatório de Intervenção em PDF, gerado anteriormente pela própria aplicação (ver
/// <see cref="IntervencaoPdfService"/>), e extrai dele os dados necessários para pré-preencher o
/// formulário "Inserir Intervenção" (<see cref="Views.IntervencaoEditWindow"/>).
///
/// PRINCÍPIO FUNDAMENTAL: o PDF de origem é sempre gerado pelo QuestPDF (ver
/// <see cref="IntervencaoPdfService"/>), com texto real e selecionável — nunca uma imagem
/// digitalizada. Por isso a leitura é feita por EXTRAÇÃO DIRETA DE TEXTO (biblioteca PdfPig),
/// nunca por OCR: é mais fiável, mais rápida e determinística, e evita qualquer dependência de
/// reconhecimento de imagem.
///
/// Em vez de depender de coordenadas fixas no documento, a leitura localiza cada campo pelo
/// respetivo título/rótulo (ex.: a palavra "ESCOLA" tal como aparece impressa no PDF - ver
/// <see cref="IntervencaoPdfService.ComposeInfoCard"/>), reconstruindo linhas de texto a partir da
/// posição (coordenadas Y) de cada palavra na página - ver <see cref="ExtrairLinhas"/>. Isto torna
/// a leitura tolerante a pequenos ajustes futuros de layout do PDF (larguras, espaçamentos, etc.),
/// desde que os rótulos em si se mantenham.
/// </summary>
public class IntervencaoPdfImportService
{
    /// <summary>Uma linha de equipamento encontrada na tabela "Equipamento Intervencionado no
    /// Local" do PDF. <see cref="EquipamentoId"/> só é preenchido quando o Nº de Série lido
    /// corresponde a um equipamento já existente na base de dados (ver
    /// <see cref="Resolver(string)"/>) — caso contrário fica null, e a linha é apresentada apenas
    /// como informação de referência (o utilizador tem de a associar manualmente, se aplicável).</summary>
    public record LinhaEquipamentoImportado(string Descricao, string NumeroSerie, string NumeroInventario, string Observacoes, int? EquipamentoId);

    public record Resultado(
        bool Sucesso,
        string? MensagemErro,
        int? EscolaId,
        string? EscolaNomeLido,
        DateTime? Data,
        EstadoIntervencao? Estado,
        string? Descricao,
        List<int> CategoriaIds,
        List<LinhaEquipamentoImportado> Equipamentos)
    {
        public static Resultado Falha(string mensagem) =>
            new(false, mensagem, null, null, null, null, null, new List<int>(), new List<LinhaEquipamentoImportado>());
    }

    private static readonly Regex RegexData = new(
        @"\b(\d{1,2})\s+de\s+([A-Za-zçÇãÃõÕáÁéÉíÍóÓúÚâÂêÊôÔ]+)\s+de\s+(\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly CultureInfo CulturaPt = new("pt-PT");

    /// <summary>Lê o PDF indicado e tenta extrair dele os dados de uma intervenção. Nunca lança
    /// exceção para o chamador — qualquer falha (ficheiro corrompido, não é um PDF desta
    /// aplicação, etc.) é devolvida em <see cref="Resultado.MensagemErro"/>.</summary>
    public Resultado Importar(string caminhoPdf)
    {
        try
        {
            using var documento = PdfDocument.Open(caminhoPdf);

            var linhas = new List<string>();
            var todasAsPalavras = new List<Word>();
            foreach (var pagina in documento.GetPages())
            {
                linhas.AddRange(ExtrairLinhas(pagina));
                todasAsPalavras.AddRange(pagina.GetWords());
            }

            // Impressão digital do documento: só continua se isto for mesmo um Relatório de
            // Intervenção desta aplicação (ver ComposeCabecalho em IntervencaoPdfService) - evita
            // preencher o formulário com dados sem sentido a partir de um PDF qualquer.
            var éRelatorioValido =
                linhas.Any(l => l.Contains("MUNICÍPIO DE LEIRIA", StringComparison.OrdinalIgnoreCase)) &&
                linhas.Any(l => l.Contains("Relatório de Intervenção", StringComparison.OrdinalIgnoreCase));

            if (!éRelatorioValido)
                return Resultado.Falha(
                    "O ficheiro selecionado não corresponde a um Relatório de Intervenção válido da aplicação.");

            var escolaNomeLido = ValorAposRotulo(linhas, "ESCOLA");
            var estadoLido = ExtrairEstado(ValorAposRotulo(linhas, "ESTADO"));
            var data = ExtrairData(linhas);
            var descricao = ExtrairDescricao(linhas);
            var categoriaIds = ExtrairCategorias(linhas);
            var equipamentos = ExtrairEquipamentoIntervencionado(todasAsPalavras);

            var escola = string.IsNullOrWhiteSpace(escolaNomeLido) ? null : ResolverEscola(escolaNomeLido);

            return new Resultado(
                Sucesso: true,
                MensagemErro: null,
                EscolaId: escola?.Id,
                EscolaNomeLido: escolaNomeLido,
                Data: data,
                Estado: estadoLido,
                Descricao: descricao,
                CategoriaIds: categoriaIds,
                Equipamentos: equipamentos);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Cobre ficheiros corrompidos, PDFs protegidos/encriptados, ou qualquer outra falha de
            // leitura - nunca deixa uma exceção de parsing chegar ao utilizador como um crash (ver
            // requisito de tratamento de erros do pedido original).
            return Resultado.Falha(
                "Não foi possível ler o ficheiro PDF selecionado. Confirme que não está corrompido " +
                $"e que corresponde a um Relatório de Intervenção gerado por esta aplicação.\n\nDetalhe técnico: {ex.Message}");
        }
    }

    /// <summary>Reconstrói as linhas de texto visual de uma página a partir da posição de cada
    /// palavra (agrupando por banda vertical/Y aproximada, depois ordenando cada grupo da esquerda
    /// para a direita) — em vez de usar a extração de texto "simples" da biblioteca, que nem
    /// sempre preserva de forma fiável a quebra de linha entre elementos empilhados verticalmente
    /// (rótulo numa linha, valor na linha seguinte) tal como o PDF os desenha.</summary>
    private static List<string> ExtrairLinhas(Page pagina)
    {
        const double toleranciaY = 3.0;
        var bandas = new List<(double Y, List<Word> Palavras)>();

        foreach (var palavra in pagina.GetWords())
        {
            var y = palavra.BoundingBox.Bottom;
            var indiceBanda = bandas.FindIndex(b => Math.Abs(b.Y - y) <= toleranciaY);
            if (indiceBanda == -1)
                bandas.Add((y, new List<Word> { palavra }));
            else
                bandas[indiceBanda].Palavras.Add(palavra);
        }

        return bandas
            .OrderByDescending(b => b.Y) // topo da página primeiro
            .Select(b => string.Join(" ", b.Palavras.OrderBy(p => p.BoundingBox.Left).Select(p => p.Text)))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    /// <summary>Devolve o conteúdo da linha imediatamente a seguir a uma linha cujo texto seja
    /// exatamente igual a <paramref name="rotulo"/> (ex.: "ESCOLA", "ESTADO") - o padrão usado em
    /// <see cref="IntervencaoPdfService.ComposeInfoCard"/> para todos os campos simples de uma só
    /// linha (rótulo em maiúsculas pequenas, valor por baixo).</summary>
    private static string? ValorAposRotulo(List<string> linhas, string rotulo)
    {
        var indice = linhas.FindIndex(l => l.Trim().Equals(rotulo, StringComparison.OrdinalIgnoreCase));
        return indice == -1 || indice + 1 >= linhas.Count ? null : linhas[indice + 1].Trim();
    }

    private static DateTime? ExtrairData(List<string> linhas)
    {
        foreach (var linha in linhas)
        {
            var match = RegexData.Match(linha);
            if (!match.Success) continue;

            var texto = match.Value;
            if (DateTime.TryParseExact(texto, "d 'de' MMMM 'de' yyyy", CulturaPt, DateTimeStyles.None, out var data))
                return data;
        }
        return null;
    }

    /// <summary>O texto correspondente ao estado é escrito exatamente como
    /// <see cref="IntervencaoPdfService.FormatarEstado"/> o produz - esta função faz o mapeamento
    /// inverso. "Em Progresso"/"Em Espera" têm espaço no PDF (não coincidem com o nome do enum),
    /// os restantes ("Fechada", "Pendente", "Cancelada") coincidem exatamente com o nome do valor
    /// do enum <see cref="EstadoIntervencao"/>.</summary>
    private static EstadoIntervencao? ExtrairEstado(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        texto = texto.Trim();

        if (texto.Equals("Em Progresso", StringComparison.OrdinalIgnoreCase)) return EstadoIntervencao.EmProgresso;
        if (texto.Equals("Em Espera", StringComparison.OrdinalIgnoreCase)) return EstadoIntervencao.EmEspera;
        return Enum.TryParse<EstadoIntervencao>(texto, ignoreCase: true, out var estado) ? estado : null;
    }

    /// <summary>Junta todas as linhas entre o título "Descrição / Tipo de Intervenção" e o início
    /// da secção seguinte (qualquer um dos títulos conhecidos, ou o fim do documento) - a descrição
    /// pode ocupar várias linhas quando é longa (ver <see cref="IntervencaoPdfService.ComposeCaixaTexto"/>,
    /// que não trunca o texto).</summary>
    private static string? ExtrairDescricao(List<string> linhas)
    {
        var indiceTitulo = linhas.FindIndex(l => l.Contains("Descrição / Tipo de Intervenção", StringComparison.OrdinalIgnoreCase));
        if (indiceTitulo == -1) return null;

        string[] titulosSeguintes =
        {
            "Equipamento Intervencionado no Local",
            "Equipamento Recolhido para a DISIA",
            "Equipamento Abatido",
            "Notas Adicionais (registo histórico)"
        };

        var linhasDescricao = new List<string>();
        for (var i = indiceTitulo + 1; i < linhas.Count; i++)
        {
            if (titulosSeguintes.Any(t => linhas[i].Contains(t, StringComparison.OrdinalIgnoreCase))) break;
            linhasDescricao.Add(linhas[i]);
        }

        var texto = string.Join(" ", linhasDescricao).Trim();
        return texto == "-" || string.IsNullOrWhiteSpace(texto) ? null : texto;
    }

    /// <summary>As categorias aparecem lado a lado na mesma linha visual (ver
    /// <see cref="IntervencaoPdfService.ComposeInfoCard"/>, "chipsRow"), o que torna ambíguo
    /// separá-las por palavra sempre que um nome de categoria tenha mais do que uma palavra (ex.:
    /// "Redes e Comunicações"). Em vez de tentar separar essa linha às cegas, verifica-se, para
    /// cada categoria realmente configurada na aplicação (<see cref="App.Db"/>), se o respetivo
    /// nome aparece como texto dentro dessa linha - isto continua a ser uma correspondência
    /// exclusivamente textual (não usa nenhuma coordenada), só que ancorada num conjunto de nomes
    /// conhecido em vez de tentar adivinhar fronteiras entre palavras.</summary>
    private static List<int> ExtrairCategorias(List<string> linhas)
    {
        var linhaCategorias = ValorAposRotulo(linhas, "CATEGORIAS");
        if (string.IsNullOrWhiteSpace(linhaCategorias) || linhaCategorias == "-")
            return new List<int>();

        return App.Db.CategoriasIntervencao
            .Where(c => c.Ativa)
            .AsEnumerable()
            .Where(c => linhaCategorias.Contains(c.Nome, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToList();
    }

    /// <summary>Localiza e reconstrói a tabela "Equipamento Intervencionado no Local" a partir das
    /// posições X/Y das palavras (não de coordenadas fixas): encontra a linha de cabeçalho da
    /// tabela pelos títulos das colunas ("Equipamento", "Nº Série", "Nº Inventário", "Observações"),
    /// usa a posição X de cada título de coluna como fronteira, e depois agrupa as palavras de cada
    /// linha seguinte pela coluna em que caem, até encontrar a linha "Total: N equipamento(s)" (fim
    /// da tabela) ou o início da tabela seguinte. Devolve uma lista vazia (sem erro) se a tabela não
    /// for encontrada ou não puder ser reconstruída com confiança - o utilizador preenche esses
    /// dados manualmente no formulário, tal como qualquer outro campo não encontrado.</summary>
    private static List<LinhaEquipamentoImportado> ExtrairEquipamentoIntervencionado(List<Word> todasAsPalavras)
    {
        var resultado = new List<LinhaEquipamentoImportado>();

        try
        {
            const double toleranciaY = 3.0;

            // Agrupa TODAS as palavras da(s) página(s) em bandas horizontais (mesma técnica de
            // ExtrairLinhas, mas mantendo a referência às Word individuais, necessária aqui para
            // conhecer a posição X de cada uma).
            var bandas = new List<(double Y, List<Word> Palavras)>();
            foreach (var palavra in todasAsPalavras)
            {
                var y = palavra.BoundingBox.Bottom;
                var indiceBanda = bandas.FindIndex(b => Math.Abs(b.Y - y) <= toleranciaY);
                if (indiceBanda == -1) bandas.Add((y, new List<Word> { palavra }));
                else bandas[indiceBanda].Palavras.Add(palavra);
            }
            var bandasOrdenadas = bandas.OrderByDescending(b => b.Y).ToList();

            // A linha de cabeçalho da tabela é a única banda que contém, em simultâneo, os 4
            // títulos de coluna conhecidos.
            var indiceCabecalho = bandasOrdenadas.FindIndex(b =>
            {
                var textoBanda = string.Join(" ", b.Palavras.Select(p => p.Text));
                return textoBanda.Contains("Equipamento") && textoBanda.Contains("Série") &&
                       textoBanda.Contains("Inventário") && textoBanda.Contains("Observações");
            });
            if (indiceCabecalho == -1) return resultado; // tabela não encontrada - devolve vazio

            Word ProcurarPalavra(List<Word> palavras, string texto) =>
                palavras.First(p => p.Text.Equals(texto, StringComparison.OrdinalIgnoreCase));

            var palavrasCabecalho = bandasOrdenadas[indiceCabecalho].Palavras;
            var xEquipamento = palavrasCabecalho.Min(p => p.BoundingBox.Left);
            var xSerie = ProcurarPalavra(palavrasCabecalho, "Nº").BoundingBox.Left;
            // A 2ª ocorrência de "Nº" (a de "Nº Inventário") é a que fica mais à direita das duas.
            var xInventario = palavrasCabecalho.Where(p => p.Text.Equals("Nº", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.BoundingBox.Left).OrderByDescending(x => x).First();
            var xObservacoes = ProcurarPalavra(palavrasCabecalho, "Observações").BoundingBox.Left;

            for (var i = indiceCabecalho + 1; i < bandasOrdenadas.Count; i++)
            {
                var banda = bandasOrdenadas[i];
                var textoBanda = string.Join(" ", banda.Palavras.OrderBy(p => p.BoundingBox.Left).Select(p => p.Text));

                // Fim da tabela: linha "Total: N equipamento(s)" (ver ComposeTabelaEquipamento) ou
                // o título de uma secção seguinte.
                if (textoBanda.StartsWith("Total:", StringComparison.OrdinalIgnoreCase)) break;
                if (textoBanda.Contains("Equipamento Recolhido") || textoBanda.Contains("Equipamento Abatido") ||
                    textoBanda.Contains("Notas Adicionais"))
                    break;

                string TextoNaColuna(double xInicio, double xFim) => string.Join(" ", banda.Palavras
                    .Where(p => p.BoundingBox.Left >= xInicio - 2 && p.BoundingBox.Left < xFim - 2)
                    .OrderBy(p => p.BoundingBox.Left)
                    .Select(p => p.Text));

                var descricao = TextoNaColuna(xEquipamento, xSerie).Trim();
                var numeroSerie = TextoNaColuna(xSerie, xInventario).Trim();
                var numeroInventario = TextoNaColuna(xInventario, xObservacoes).Trim();
                var observacoes = TextoNaColuna(xObservacoes, double.MaxValue).Trim();

                if (string.IsNullOrWhiteSpace(descricao) && string.IsNullOrWhiteSpace(numeroSerie)) continue;

                var equipamentoId = string.IsNullOrWhiteSpace(numeroSerie) || numeroSerie == "-"
                    ? (int?)null
                    : App.Db.Equipamentos.FirstOrDefault(eq => eq.NumeroSerie == numeroSerie)?.Id;

                resultado.Add(new LinhaEquipamentoImportado(
                    descricao == "-" ? "" : descricao,
                    numeroSerie == "-" ? "" : numeroSerie,
                    numeroInventario == "-" ? "" : numeroInventario,
                    observacoes == "-" ? "" : observacoes,
                    equipamentoId));
            }
        }
        catch
        {
            // Qualquer falha na reconstrução da tabela (layout inesperado, tabela ausente, etc.)
            // não deve impedir a importação dos restantes campos - devolve simplesmente o que já
            // tiver sido reconhecido com confiança até ao momento da falha.
        }

        return resultado;
    }

    /// <summary>Tenta encontrar a escola correspondente ao nome lido do PDF: primeiro por
    /// igualdade exata (ignorando maiúsculas/minúsculas), e, falhando essa, pela escola cujo nome
    /// esteja contido no texto lido ou vice-versa (tolera pequenas diferenças de espaçamento/acentuação
    /// introduzidas pela extração de texto do PDF).</summary>
    private static Escola? ResolverEscola(string nomeLido)
    {
        var todas = App.Db.Escolas.ToList();
        var exata = todas.FirstOrDefault(e => string.Equals(e.Nome.Trim(), nomeLido.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exata != null) return exata;

        return todas.FirstOrDefault(e =>
            nomeLido.Contains(e.Nome.Trim(), StringComparison.OrdinalIgnoreCase) ||
            e.Nome.Contains(nomeLido.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
