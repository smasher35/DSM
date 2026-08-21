using ClosedXML.Excel;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera ficheiros Excel (.xlsx) de template/modelo para cada fase de importação de dados,
/// já com os cabeçalhos corretos (na mesma ordem e com o mesmo nome de aba esperados por
/// <see cref="ExcelImportService"/>), uma linha de exemplo e uma aba de instruções.
/// </summary>
public static class TemplateExcelService
{
    private static readonly XLColor CorCabecalho = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor CorExemplo = XLColor.FromHtml("#FFF7E0");

    // -------------------------------------------------------------------
    // Fase 1 — Agrupamentos + Escolas
    // -------------------------------------------------------------------
    public static void GerarTemplateAgrupamentosEscolas(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var wsAgrupamentos = CriarAbaDados(wb, "Agrupamentos",
            new[] { "Id_Agrupamento", "cod_gepe", "Agrupamento", "Morada", "Contacto 1", "Contacto 2", "Contacto 3", "Email 1", "Email 2", "Site", "Observações" },
            new object[][]
            {
                new object[] { 1, "", "Agrupamento de Escolas Exemplo", "Rua Principal, 1, Leiria", "244000000", "", "", "geral@aeexemplo.pt", "", "www.aeexemplo.pt", "" }
            });

        var wsEscolas = CriarAbaDados(wb, "Escolas",
            new[] { "Freguesia", "Código DGRH", "Código GEPE", "Estabelecimento de Ensino", "Morada", "Telefone", "E-mail", "Cod. Agrupamento" },
            new object[][]
            {
                new object[] { "Leiria", "", 123456, "EB1 de Exemplo", "Rua da Escola, 2, Leiria", "244000001", "escola@exemplo.pt", 1 }
            });

        CriarAbaInstrucoes(wb,
            "Agrupamentos e Escolas",
            "Ficheiro Excel (.xlsx) com DUAS abas: 'Agrupamentos' (processada sempre primeiro) e 'Escolas'.",
            new (string, string)[]
            {
                ("Aba \"Agrupamentos\"", ""),
                ("1. Id_Agrupamento", "Código numérico único, atribuído por si. É este número que a coluna \"Cod. Agrupamento\" da aba Escolas tem de referenciar."),
                ("2. cod_gepe", "Opcional, apenas informativo."),
                ("3. Agrupamento", "Nome do agrupamento de escolas."),
                ("4. Morada", "Morada da sede do agrupamento."),
                ("5-7. Contacto 1 / 2 / 3", "Telefones de contacto (opcional)."),
                ("8-9. Email 1 / 2", "Emails de contacto (opcional)."),
                ("10. Site", "Website do agrupamento (opcional)."),
                ("11. Observações", "Notas gerais (opcional)."),
                ("", ""),
                ("Aba \"Escolas\"", ""),
                ("1. Freguesia", "Freguesia onde a escola se localiza."),
                ("2. Código DGRH", "Opcional."),
                ("3. Código GEPE", "Código oficial GEPE da escola, se conhecido (opcional, mas recomendado)."),
                ("4. Estabelecimento de Ensino", "Nome completo da escola."),
                ("5. Morada", "Morada da escola."),
                ("6. Telefone", "Contacto telefónico (opcional)."),
                ("7. E-mail", "Email da escola (opcional)."),
                ("8. Cod. Agrupamento", "Tem de corresponder exatamente ao \"Id_Agrupamento\" indicado na aba Agrupamentos. Deixe em branco se a escola não pertencer a nenhum agrupamento."),
            },
            "Escolas ou agrupamentos com nome muito semelhante a um já existente na aplicação não são duplicados - os dados em falta são apenas complementados.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 2 — Equipamento
    // -------------------------------------------------------------------
    public static void GerarTemplateEquipamento(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        CriarAbaDados(wb, "Equipamento",
            new[] { "Nº Série", "Nº Inventário", "Tipo", "Marca", "Modelo", "Escola", "Código GEPE", "Estado", "Observações" },
            new object[][]
            {
                new object[] { "SN-000123", "INV-2026-001", "Computador de Secretária", "Dell", "OptiPlex 3080", "EB1 de Exemplo", 123456, "Em Serviço", "" }
            });

        CriarAbaInstrucoes(wb,
            "Equipamento",
            "Ficheiro Excel (.xlsx) com a aba \"Equipamento\" (se não existir esse nome, é usada a primeira aba).",
            new (string, string)[]
            {
                ("1. Nº Série", "Obrigatório. Identifica de forma única o equipamento — não pode repetir-se."),
                ("2. Nº Inventário", "Número de inventário municipal (opcional)."),
                ("3. Tipo", "Ex: Computador de Secretária, Portátil, Servidor, Monitor, Impressora, Projetor, etc."),
                ("4. Marca", "Ex: Dell, HP, Lenovo..."),
                ("5. Modelo", "Modelo do equipamento."),
                ("6. Escola", "Nome da escola onde o equipamento se encontra (a aplicação faz correspondência aproximada de nomes)."),
                ("7. Código GEPE", "Opcional — se preenchido, é usado como chave exata para encontrar a escola."),
                ("8. Estado", "Em Serviço / Em Reparação / Em Armazém / Abatido. Se ficar em branco, assume-se \"Em Serviço\"."),
                ("9. Observações", "Notas gerais (opcional)."),
            },
            "Equipamento com o mesmo Nº Série de um registo já existente na aplicação não é duplicado.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 3 — Intervenções (Modelo de Importação)
    // -------------------------------------------------------------------
    public static void GerarTemplateIntervencoes(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        CriarAbaDados(wb, "Intervenções",
            new[] { "Data", "Escola", "Código GEPE", "Descrição", "Categorias", "Material Recolhido/Abatido", "Estado", "Motivo Pendente" },
            new object[][]
            {
                new object[] { "23-07-2026", "EB1 de Exemplo", 123456, "Substituição de switch de rede", "Redes e Comunicações", "", "Fechada", "" }
            });

        CriarAbaInstrucoes(wb,
            "Intervenções",
            "Ficheiro Excel (.xlsx) com a aba \"Intervenções\" (se não existir esse nome, é usada a primeira aba). Uma linha por intervenção.",
            new (string, string)[]
            {
                ("1. Data", "Formato dd-mm-aaaa."),
                ("2. Escola", "Nome da escola (a aplicação faz correspondência aproximada de nomes)."),
                ("3. Código GEPE", "Opcional — se preenchido, é usado como chave exata para encontrar a escola."),
                ("4. Descrição", "Descrição da intervenção realizada."),
                ("5. Categorias", "Uma ou mais categorias separadas por ponto e vírgula, ex: \"Hardware; Redes\". Os nomes têm de corresponder exatamente às categorias configuradas em Administração → Dados Fixos."),
                ("6. Material Recolhido/Abatido", "Opcional."),
                ("7. Estado", "Fechada / Pendente / Em Progresso / Em Espera / Cancelada. Se ficar em branco, assume-se \"Fechada\"."),
                ("8. Motivo Pendente", "Preencher apenas quando o Estado for \"Pendente\" (opcional)."),
            },
            "Intervenções com a mesma data, escola e descrição de uma já existente na aplicação não são duplicadas.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 4 — Equipamento Abatido
    // -------------------------------------------------------------------
    public static void GerarTemplateEquipamentoAbatido(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        CriarAbaDados(wb, "Equipamento Abatido",
            new[] { "Nº Série", "Data de Abate", "Status", "Escola/Local", "Descrição", "Observações" },
            new object[][]
            {
                new object[] { "SN-000123", "15-06-2026", "Abatido", "EB1 de Exemplo", "Equipamento obsoleto, substituído", "" }
            });

        CriarAbaInstrucoes(wb,
            "Equipamento Abatido",
            "Ficheiro Excel (.xlsx) com a aba \"Equipamento Abatido\" (se não existir esse nome, é usada a primeira aba).",
            new (string, string)[]
            {
                ("1. Nº Série", "Obrigatório — tem de corresponder a um equipamento já existente no inventário de Equipamento."),
                ("2. Data de Abate", "Data em que o equipamento foi abatido."),
                ("3. Status", "Ex: Abatido / Em processo de abate / Doado / Reciclado..."),
                ("4. Escola/Local", "Escola ou local onde o equipamento se encontrava."),
                ("5. Descrição", "Motivo/descrição do abate."),
                ("6. Observações", "Notas gerais (opcional)."),
            },
            "Registos com o mesmo Nº Série e Data de Abate de um já existente na aplicação não são duplicados.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 5 — Equipamento Recolhido
    // -------------------------------------------------------------------
    public static void GerarTemplateEquipamentoRecolhido(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        CriarAbaDados(wb, "Equipamento Recolhido",
            new[] { "Nº Série", "Data de Recolha", "Estado", "Data de Entrega", "Observações" },
            new object[][]
            {
                new object[] { "SN-000123", "10-07-2026", "Pendente", "", "Recolhido para reparação" }
            });

        CriarAbaInstrucoes(wb,
            "Equipamento Recolhido",
            "Ficheiro Excel (.xlsx) com a aba \"Equipamento Recolhido\" (se não existir esse nome, é usada a primeira aba).",
            new (string, string)[]
            {
                ("1. Nº Série", "Obrigatório — tem de corresponder a um equipamento já existente no inventário de Equipamento."),
                ("2. Data de Recolha", "Data em que o equipamento foi recolhido."),
                ("3. Estado", "Pendente / Em Reparação / Reparado / Entregue. Se ficar em branco, assume-se \"Pendente\"."),
                ("4. Data de Entrega", "Preencher apenas se o equipamento já tiver sido entregue de volta (opcional)."),
                ("5. Observações", "Notas gerais (opcional)."),
            },
            "Registos com o mesmo Nº Série e Data de Recolha de um já existente na aplicação não são duplicados.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Atividades da DISIA
    // -------------------------------------------------------------------
    public static void GerarTemplateAtividadesDisia(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        // Esta importação usa sempre a primeira aba do ficheiro, independentemente do nome -
        // por isso a aba de dados tem de ficar antes da aba de instruções neste ficheiro.
        CriarAbaDados(wb, "Atividades DISIA",
            new[] { "Data", "Descrição", "Categoria", "Local", "Divisão / Serviço", "Suporte Prestado", "Quantidade" },
            new object[][]
            {
                new object[] { "23-07-2026", "Verificação do acesso de fibra MEO no espaço de cidadão", "Redes e Comunicações", "Junta de Freguesia de Exemplo", "DISIA", "Suporte técnico presencial", 1 }
            });

        CriarAbaInstrucoes(wb,
            "Atividades da DISIA",
            "Ficheiro Excel (.xlsx) - é sempre usada a primeira aba do ficheiro (mantenha a aba \"Atividades DISIA\" em primeiro lugar).",
            new (string, string)[]
            {
                ("1. Data", "Formato dd/mm/aaaa."),
                ("2. Descrição", "Descrição da atividade realizada."),
                ("3. Categoria", "Ex: Videovigilância (CCTV), Redes e Comunicações, Equipamento Informático..."),
                ("4. Local", "Ex: Junta de Freguesia de..., Museu de Leiria, Mercado Municipal..."),
                ("5. Divisão / Serviço", "Divisão ou serviço municipal envolvido (opcional)."),
                ("6. Suporte Prestado", "Tipo de suporte prestado (opcional)."),
                ("7. Quantidade", "Número de vezes que o serviço foi prestado (deixe 1 se for uma ocorrência única)."),
            },
            "Registos com datas e descrições muito semelhantes às já existentes na aplicação não são duplicados.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Comunicações
    // -------------------------------------------------------------------
    public static void GerarTemplateComunicacoes(string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        CriarAbaDados(wb, "Comunicações",
            new[] { "Escola", "Código GEPE", "Tipo de Ligação", "Velocidade de Fibra", "Operadora", "Nº Contrato", "Data de Instalação", "Integrado", "Estado", "Observações" },
            new object[][]
            {
                new object[] { "EB1 de Exemplo", 123456, "Fibra", "100 Mbps", "MEO", "CT-2026-001", "01-03-2026", "Sim", "Ativa", "" }
            });

        CriarAbaInstrucoes(wb,
            "Comunicações",
            "Ficheiro Excel (.xlsx) com a aba \"Comunicações\" (se não existir esse nome, é usada a primeira aba).",
            new (string, string)[]
            {
                ("1. Escola", "Nome da escola (a aplicação faz correspondência aproximada de nomes)."),
                ("2. Código GEPE", "Opcional — se preenchido, é usado como chave exata para encontrar a escola."),
                ("3. Tipo de Ligação", "Fibra / ADSL / 4G-5G / Satélite / Outro. Se ficar em branco, assume-se \"Fibra\"."),
                ("4. Velocidade de Fibra", "Ex: 100 Mbps, 1 Gbps (opcional)."),
                ("5. Operadora", "Ex: MEO, NOS, Vodafone..."),
                ("6. Nº Contrato", "Número do contrato com a operadora."),
                ("7. Data de Instalação", "Data em que a ligação foi instalada."),
                ("8. Integrado", "Sim / Não."),
                ("9. Estado", "Ativa / Inativa / Pendente de Instalação / Pendente de Integração..."),
                ("10. Observações", "Notas gerais (opcional)."),
            },
            "Cada linha é associada/atualizada pela combinação Escola + Nº Contrato — já não duplica registos existentes.");

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Auxiliares
    // -------------------------------------------------------------------

    private static IXLWorksheet CriarAbaDados(XLWorkbook wb, string nomeAba, string[] cabecalhos, object[][] linhasExemplo)
    {
        var ws = wb.Worksheets.Add(nomeAba);

        for (var c = 0; c < cabecalhos.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cabecalhos[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = CorCabecalho;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        for (var r = 0; r < linhasExemplo.Length; r++)
        {
            for (var c = 0; c < linhasExemplo[r].Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                DefinirValorCelula(cell, linhasExemplo[r][c]);
                cell.Style.Fill.BackgroundColor = CorExemplo;
                cell.Style.Font.Italic = true;
            }
        }

        // Nota junto à primeira célula de exemplo, a lembrar que a linha é apenas ilustrativa.
        if (linhasExemplo.Length > 0)
        {
            ws.Cell(linhasExemplo.Length + 2, 1).Value = "↑ linha de exemplo — pode substituir ou apagar antes de importar";
            ws.Cell(linhasExemplo.Length + 2, 1).Style.Font.FontColor = XLColor.FromHtml("#94A3B8");
            ws.Cell(linhasExemplo.Length + 2, 1).Style.Font.Italic = true;
            ws.Cell(linhasExemplo.Length + 2, 1).Style.Font.FontSize = 9;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        return ws;
    }

    /// <summary>Atribui um valor de tipo variado (string, int, etc.) a uma célula ClosedXML,
    /// já que <see cref="IXLCell.Value"/> não converte implicitamente a partir de <c>object</c>.</summary>
    private static void DefinirValorCelula(IXLCell cell, object valor)
    {
        switch (valor)
        {
            case null:
                cell.Value = "";
                break;
            case string s:
                cell.Value = s;
                break;
            case int i:
                cell.Value = i;
                break;
            case double d:
                cell.Value = d;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case bool b:
                cell.Value = b;
                break;
            default:
                cell.Value = valor.ToString() ?? "";
                break;
        }
    }

    private static void CriarAbaInstrucoes(XLWorkbook wb, string titulo, string introducao,
        (string Coluna, string Explicacao)[] colunas, string notaFinal)
    {
        var ws = wb.Worksheets.Add("Instruções");

        ws.Cell(1, 1).Value = $"Como preencher — {titulo}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Font.FontColor = CorCabecalho;

        ws.Cell(2, 1).Value = introducao;
        ws.Cell(2, 1).Style.Alignment.WrapText = true;
        ws.Range(2, 1, 2, 2).Merge();

        var linha = 4;
        ws.Cell(linha, 1).Value = "Coluna";
        ws.Cell(linha, 2).Value = "O que preencher";
        ws.Range(linha, 1, linha, 2).Style.Font.Bold = true;
        ws.Range(linha, 1, linha, 2).Style.Fill.BackgroundColor = CorCabecalho;
        ws.Range(linha, 1, linha, 2).Style.Font.FontColor = XLColor.White;
        linha++;

        foreach (var (coluna, explicacao) in colunas)
        {
            ws.Cell(linha, 1).Value = coluna;
            // Linhas de "título de secção" (ex: "Aba \"Agrupamentos\"") não têm explicação -
            // são destacadas a negrito para se distinguirem das linhas de coluna normais.
            var ehTituloDeSeccao = string.IsNullOrWhiteSpace(explicacao) && !string.IsNullOrWhiteSpace(coluna);
            ws.Cell(linha, 1).Style.Font.Bold = ehTituloDeSeccao;
            ws.Cell(linha, 2).Value = explicacao;
            ws.Cell(linha, 2).Style.Alignment.WrapText = true;
            linha++;
        }

        linha++;
        ws.Cell(linha, 1).Value = "Nota:";
        ws.Cell(linha, 1).Style.Font.Bold = true;
        ws.Cell(linha, 2).Value = notaFinal;
        ws.Cell(linha, 2).Style.Alignment.WrapText = true;

        ws.Column(1).Width = 34;
        ws.Column(2).Width = 70;
    }
}
