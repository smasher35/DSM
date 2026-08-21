using ClosedXML.Excel;
using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Services;

/// <summary>
/// Resultado da importação, para mostrar um resumo ao utilizador.
/// </summary>
public class ImportResult
{
    public int AgrupamentosCriados { get; set; }
    public int EscolasCriadasDeGepe { get; set; }
    public int EscolasIgnoradasPorDuplicado { get; set; }
    public int ContactosImportados { get; set; }
    public int IntervencoesImportadas { get; set; }
    public int IntervencoesIgnoradas { get; set; }
    public int AtividadesDisiaImportadas { get; set; }
    public List<string> Avisos { get; } = new();
}

/// <summary>
/// Importa os dados do ficheiro_base.xlsx original para a base de dados da aplicação.
/// A aba "GEPE" é a fonte "oficial" de escolas (com os campos obrigatórios pedidos).
/// A aba "Lista de Escolas" é usada apenas para associar nomes abreviados/alternativos
/// às escolas já existentes, evitando duplicar registos quando o nome está escrito de
/// forma diferente (ex.: "EB1 Amor" vs "Escola Básica do 1.º Ciclo de Amor").
/// </summary>
public class ImportResultAgrupamentosEscolas
{
    public int AgrupamentosCriados { get; set; }
    public int AgrupamentosAtualizados { get; set; }
    public int EscolasCriadas { get; set; }
    public int EscolasAtualizadas { get; set; }
    public int EscolasIgnoradasPorDuplicado { get; set; }
    public int EscolasSemAgrupamento { get; set; }
    public List<string> Avisos { get; } = new();
}

public class ImportResultEquipamento
{
    public int EquipamentosImportados { get; set; }
    public int EquipamentosIgnoradosPorDuplicado { get; set; }
    public List<string> Avisos { get; } = new();
}

public class ImportResultEquipamentoAbatido
{
    public int AbatesImportados { get; set; }
    public int AbatesIgnoradosPorDuplicado { get; set; }
    public List<string> Avisos { get; } = new();
}

public class ImportResultEquipamentoRecolhido
{
    public int RecolhidosImportados { get; set; }
    public int RecolhidosIgnoradosPorDuplicado { get; set; }
    public List<string> Avisos { get; } = new();
}

public class ImportResultComunicacoes
{
    public int ComunicacoesImportadas { get; set; }
    public int ComunicacoesAtualizadas { get; set; }
    public List<string> Avisos { get; } = new();
}

/// <summary>9: resumo consolidado de "Importar Tudo" — agrega o resultado de cada uma das fases,
/// executadas em sequência sobre o mesmo ficheiro Excel (o mesmo formato gerado por
/// <see cref="ExcelExportService.ExportarTudo"/>). Cada fase falha de forma independente: se uma
/// aba não existir ou uma fase falhar, as restantes continuam a ser importadas na mesma.</summary>
public class ImportResultTudo
{
    public ImportResultAgrupamentosEscolas? AgrupamentosEscolas { get; set; }
    public ImportResultEquipamento? Equipamento { get; set; }
    public ImportResult? Intervencoes { get; set; }
    public ImportResultEquipamentoAbatido? EquipamentoAbatido { get; set; }
    public ImportResultEquipamentoRecolhido? EquipamentoRecolhido { get; set; }
    public ImportResult? AtividadesDisia { get; set; }
    public ImportResultComunicacoes? Comunicacoes { get; set; }
    public List<string> ErrosFatais { get; } = new();
}

/// <summary>
/// Importa os dados do ficheiro_base.xlsx original para a base de dados da aplicação.
/// A aba "GEPE" é a fonte "oficial" de escolas (com os campos obrigatórios pedidos).
/// A aba "Lista de Escolas" é usada apenas para associar nomes abreviados/alternativos
/// às escolas já existentes, evitando duplicar registos quando o nome está escrito de
/// forma diferente (ex.: "EB1 Amor" vs "Escola Básica do 1.º Ciclo de Amor").
/// </summary>
public class ExcelImportService
{
    private readonly AppDbContext _db;

    public ExcelImportService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Importa Agrupamentos e Escolas a partir do ficheiro dedicado "lista_escolas_agrupamento.xlsx",
    /// com as abas "Agrupamentos" e "Escolas". Os agrupamentos são sempre importados primeiro
    /// (aba "Agrupamentos"); as escolas (aba "Escolas") são depois associadas ao respetivo
    /// agrupamento através da coluna "Cod. Agrupamento". Escolas sem código de agrupamento
    /// preenchido ficam sem agrupamento associado (ver funcionalidade de Escola sem Agrupamento).
    /// Nunca duplica agrupamentos ou escolas já existentes (reutiliza o TextNormalizer).
    /// </summary>
    public ImportResultAgrupamentosEscolas ImportarAgrupamentosEEscolas(string caminhoXlsx)
    {
        var resultado = new ImportResultAgrupamentosEscolas();
        using var workbook = new XLWorkbook(caminhoXlsx);

        // ---------------------------------------------------------------
        // 1) Aba "Agrupamentos" - sempre processada primeiro
        // ---------------------------------------------------------------
        var agrupamentosPorCodigoOrigem = new Dictionary<int, Agrupamento>();

        if (workbook.Worksheets.TryGetWorksheet("Agrupamentos", out var wsAgrupamentos))
        {
            var agrupamentosExistentes = _db.Agrupamentos.ToList();

            foreach (var row in wsAgrupamentos.RowsUsed().Skip(1)) // linha 1 = cabeçalho
            {
                var codOrigemTxt = row.Cell(1).GetValue<string>();       // Id_Agrupamento
                var codGepeTxt = row.Cell(2).GetValue<string>();          // cod_gepe (informativo)
                var nome = row.Cell(3).GetString().Trim();                // Agrupamento
                var morada = row.Cell(4).GetString().Trim();
                var contacto1 = row.Cell(5).GetString().Trim();
                var contacto2 = row.Cell(6).GetString().Trim();
                var contacto3 = row.Cell(7).GetString().Trim();
                var email1 = row.Cell(8).GetString().Trim();
                var email2 = row.Cell(9).GetString().Trim();
                var site = row.Cell(10).GetString().Trim();
                var obs = row.Cell(11).GetString().Trim();

                if (string.IsNullOrWhiteSpace(nome)) continue;
                if (!int.TryParse(codOrigemTxt, out var codOrigem))
                {
                    resultado.Avisos.Add($"[Agrupamentos] Linha ignorada: código inválido para '{nome}'.");
                    continue;
                }

                // Dedupe por código OU por nome semelhante (caso os códigos não batam certo
                // entre ficheiros diferentes já importados anteriormente).
                var existente = agrupamentosExistentes.FirstOrDefault(a =>
                    a.CodAgrupamento == codOrigem ||
                    TextNormalizer.AreLikelySameSchool(a.Nome, nome));

                static string? SemVazio(string v) => string.IsNullOrWhiteSpace(v) ? null : v;

                if (existente == null)
                {
                    var novo = new Agrupamento
                    {
                        CodAgrupamento = codOrigem,
                        Nome = nome,
                        Morada = SemVazio(morada),
                        Contacto1 = SemVazio(contacto1),
                        Contacto2 = SemVazio(contacto2),
                        Contacto3 = SemVazio(contacto3),
                        Email1 = SemVazio(email1),
                        Email2 = SemVazio(email2),
                        Site = SemVazio(site),
                        Observacoes = SemVazio(obs)
                    };
                    _db.Agrupamentos.Add(novo);
                    _db.SaveChanges(); // garante Id gerado
                    agrupamentosExistentes.Add(novo);
                    agrupamentosPorCodigoOrigem[codOrigem] = novo;
                    resultado.AgrupamentosCriados++;
                }
                else
                {
                    existente.Morada ??= SemVazio(morada);
                    existente.Contacto1 ??= SemVazio(contacto1);
                    existente.Contacto2 ??= SemVazio(contacto2);
                    existente.Contacto3 ??= SemVazio(contacto3);
                    existente.Email1 ??= SemVazio(email1);
                    existente.Email2 ??= SemVazio(email2);
                    existente.Site ??= SemVazio(site);
                    existente.Observacoes ??= SemVazio(obs);
                    agrupamentosPorCodigoOrigem[codOrigem] = existente;
                    resultado.AgrupamentosAtualizados++;
                }
            }

            _db.SaveChanges();
        }
        else
        {
            resultado.Avisos.Add("Aba 'Agrupamentos' não encontrada - nenhum agrupamento foi importado.");
        }

        // ---------------------------------------------------------------
        // 2) Aba "Escolas" - associadas ao agrupamento pela coluna "Cod. Agrupamento"
        // ---------------------------------------------------------------
        if (workbook.Worksheets.TryGetWorksheet("Escolas", out var wsEscolas))
        {
            var escolasExistentes = _db.Escolas.ToList();
            Escola? ultimaEscolaProcessada = null;
            var contadoresCodigoEscola = CodigoEscolaService.ObterContadoresIniciais(_db);

            foreach (var row in wsEscolas.RowsUsed().Skip(1)) // linha 1 = cabeçalho
            {
                var freguesia = row.Cell(1).GetString().Trim();
                var codDgrheTxt = row.Cell(2).GetValue<string>();
                var codGepeTxt = row.Cell(3).GetValue<string>();
                var nomeEscola = row.Cell(4).GetString().Trim();
                var morada = row.Cell(5).GetString().Trim();
                var telefone = row.Cell(6).GetString().Trim();
                var email = row.Cell(7).GetString().Trim();
                var codAgrupamentoTxt = row.Cell(8).GetValue<string>();

                // Linhas de continuação de morada (sem nome de escola preenchido):
                // juntam-se à morada da última escola processada, em vez de serem ignoradas.
                if (string.IsNullOrWhiteSpace(nomeEscola))
                {
                    if (ultimaEscolaProcessada != null && !string.IsNullOrWhiteSpace(morada))
                        ultimaEscolaProcessada.Morada = string.Join(", ",
                            new[] { ultimaEscolaProcessada.Morada, morada }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    continue;
                }

                int? agrupamentoId = null;
                if (int.TryParse(codAgrupamentoTxt, out var codAgrupamentoOrigem) &&
                    agrupamentosPorCodigoOrigem.TryGetValue(codAgrupamentoOrigem, out var agrupamentoEncontrado))
                {
                    agrupamentoId = agrupamentoEncontrado.Id;
                }
                else if (!string.IsNullOrWhiteSpace(codAgrupamentoTxt))
                {
                    resultado.Avisos.Add($"[Escolas] '{nomeEscola}': código de agrupamento '{codAgrupamentoTxt}' não corresponde a nenhum agrupamento importado.");
                }
                else
                {
                    resultado.EscolasSemAgrupamento++;
                }

                var existente = escolasExistentes.FirstOrDefault(e => TextNormalizer.AreLikelySameSchool(e.Nome, nomeEscola));

                int.TryParse(codDgrheTxt, out var codDgrhe);
                int.TryParse(codGepeTxt, out var codGepe);

                static string? SemVazioEsc(string v) => string.IsNullOrWhiteSpace(v) ? null : v;

                if (existente != null)
                {
                    // Nunca duplica: apenas complementa dados em falta na escola já existente.
                    existente.AgrupamentoId ??= agrupamentoId;
                    existente.Freguesia ??= SemVazioEsc(freguesia);
                    existente.Morada ??= SemVazioEsc(morada);
                    existente.Telefone ??= SemVazioEsc(telefone);
                    existente.Email ??= SemVazioEsc(email);
                    existente.CodDGRHE ??= codDgrhe > 0 ? codDgrhe : null;
                    existente.CodGEPE ??= codGepe > 0 ? codGepe : null;
                    ultimaEscolaProcessada = existente;
                    resultado.EscolasAtualizadas++;
                    continue;
                }

                var tipoDetetado = nomeEscola.Contains("Jardim de Infância", StringComparison.OrdinalIgnoreCase)
                    ? "Jardim de Infância"
                    : nomeEscola.Contains("Secundária", StringComparison.OrdinalIgnoreCase)
                        ? "Secundária"
                        : "EB1";

                var nova = new Escola
                {
                    CodEscola = CodigoEscolaService.ProximoCodigo(contadoresCodigoEscola, tipoDetetado),
                    CodDGRHE = codDgrhe > 0 ? codDgrhe : null,
                    CodGEPE = codGepe > 0 ? codGepe : null,
                    Nome = nomeEscola,
                    Morada = SemVazioEsc(morada),
                    Localidade = SemVazioEsc(freguesia),
                    Freguesia = SemVazioEsc(freguesia),
                    Telefone = SemVazioEsc(telefone),
                    Email = SemVazioEsc(email),
                    AgrupamentoId = agrupamentoId,
                    Tipo = tipoDetetado
                };

                _db.Escolas.Add(nova);
                escolasExistentes.Add(nova);
                ultimaEscolaProcessada = nova;
                resultado.EscolasCriadas++;
            }

            _db.SaveChanges();
        }
        else
        {
            resultado.Avisos.Add("Aba 'Escolas' não encontrada - nenhuma escola foi importada.");
        }

        return resultado;
    }

    public ImportResult ImportarFicheiroBase(string caminhoXlsx)
    {
        var resultado = new ImportResult();
        using var workbook = new XLWorkbook(caminhoXlsx);

        ImportarAgrupamentosEEscolasGepe(workbook, resultado);
        AssociarNomesAlternativosListaEscolas(workbook, resultado);
        ImportarContactos(workbook, resultado);
        ImportarIntervencoesMensais(workbook, resultado);
        ImportarServDisia(workbook, resultado);

        _db.SaveChanges();
        return resultado;
    }

    // ---------------------------------------------------------------
    // 1) Aba GEPE => Agrupamentos + Escolas (fonte de verdade)
    // ---------------------------------------------------------------
    private void ImportarAgrupamentosEEscolasGepe(XLWorkbook wb, ImportResult resultado)
    {
        if (!wb.Worksheets.TryGetWorksheet("GEPE", out var ws))
        {
            resultado.Avisos.Add("Aba 'GEPE' não encontrada - agrupamentos/escolas não importados.");
            return;
        }

        var agrupamentosCache = _db.Agrupamentos.ToDictionary(a => a.CodAgrupamento);
        var escolasExistentes = _db.Escolas.ToList();
        var contadoresCodigoEscola = CodigoEscolaService.ObterContadoresIniciais(_db);

        foreach (var row in ws.RowsUsed().Skip(1)) // linha 1 = cabeçalho
        {
            var codDgrhe = row.Cell(2).GetValue<string>();
            var codGepe = row.Cell(3).GetValue<string>();
            var nomeEscola = row.Cell(4).GetString().Trim();
            var morada = row.Cell(5).GetString().Trim();
            var localidade = row.Cell(6).GetString().Trim();
            var freguesia = row.Cell(7).GetString().Trim();
            var codAgrupamentoTxt = row.Cell(8).GetValue<string>();
            var nomeAgrupamento = row.Cell(9).GetString().Trim();

            if (string.IsNullOrWhiteSpace(nomeEscola)) continue;
            if (!int.TryParse(codAgrupamentoTxt, out var codAgrupamento)) continue;

            if (!agrupamentosCache.TryGetValue(codAgrupamento, out var agrupamento))
            {
                agrupamento = new Agrupamento
                {
                    CodAgrupamento = codAgrupamento,
                    Nome = nomeAgrupamento
                };
                _db.Agrupamentos.Add(agrupamento);
                _db.SaveChanges(); // garante Id gerado
                agrupamentosCache[codAgrupamento] = agrupamento;
                resultado.AgrupamentosCriados++;
            }

            // Deduplicação: verifica se já existe uma escola com nome semelhante
            var jaExiste = escolasExistentes.Any(e => TextNormalizer.AreLikelySameSchool(e.Nome, nomeEscola));
            if (jaExiste)
            {
                resultado.EscolasIgnoradasPorDuplicado++;
                continue;
            }

            int? codDgrheInt = int.TryParse(codDgrhe, out var d) ? d : null;
            int? codGepeInt = int.TryParse(codGepe, out var g) ? g : null;

            var tipoDetetado = nomeEscola.Contains("Jardim de Infância", StringComparison.OrdinalIgnoreCase)
                ? "Jardim de Infância"
                : nomeEscola.Contains("Centro Escolar", StringComparison.OrdinalIgnoreCase)
                    ? "Centro Escolar"
                    : "EB1";

            var escola = new Escola
            {
                CodEscola = CodigoEscolaService.ProximoCodigo(contadoresCodigoEscola, tipoDetetado),
                CodDGRHE = codDgrheInt,
                CodGEPE = codGepeInt,
                Nome = nomeEscola,
                Morada = morada,
                Localidade = localidade,
                Freguesia = freguesia,
                AgrupamentoId = agrupamento.Id,
                Tipo = tipoDetetado
            };

            _db.Escolas.Add(escola);
            escolasExistentes.Add(escola);
            resultado.EscolasCriadasDeGepe++;
        }

        _db.SaveChanges();
    }

    // ---------------------------------------------------------------
    // 2) Aba "Lista de Escolas" => apenas associa nome alternativo/abreviado
    //    a uma escola já existente (nunca cria escola nova a partir daqui).
    // ---------------------------------------------------------------
    private void AssociarNomesAlternativosListaEscolas(XLWorkbook wb, ImportResult resultado)
    {
        if (!wb.Worksheets.TryGetWorksheet("Lista de Escolas", out var ws))
            return;

        var escolas = _db.Escolas.ToList();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var nomeAbreviado = row.Cell(3).GetString().Trim(); // coluna "Escola"
            if (string.IsNullOrWhiteSpace(nomeAbreviado)) continue;

            var correspondente = escolas.FirstOrDefault(e => TextNormalizer.AreLikelySameSchool(e.Nome, nomeAbreviado));
            if (correspondente == null)
            {
                resultado.Avisos.Add($"Sem correspondência na aba GEPE para '{nomeAbreviado}' (Lista de Escolas).");
                continue;
            }

            if (string.IsNullOrWhiteSpace(correspondente.NomeAlternativo) &&
                !string.Equals(correspondente.Nome, nomeAbreviado, StringComparison.OrdinalIgnoreCase))
            {
                correspondente.NomeAlternativo = nomeAbreviado;
            }
        }

        _db.SaveChanges();
    }

    // ---------------------------------------------------------------
    // 3) Aba "Contactos"
    // ---------------------------------------------------------------
    private void ImportarContactos(XLWorkbook wb, ImportResult resultado)
    {
        if (!wb.Worksheets.TryGetWorksheet("Contactos", out var ws)) return;

        var escolas = _db.Escolas.ToList();
        string? agrupamentoAtual = null;

        foreach (var row in ws.RowsUsed())
        {
            var col1 = row.Cell(1).GetString().Trim();
            if (col1.StartsWith("Agrupamento", StringComparison.OrdinalIgnoreCase))
            {
                agrupamentoAtual = col1;
                continue;
            }
            if (col1 == "Escola" || string.IsNullOrWhiteSpace(col1) || col1 == "Contactos nas Escolas")
                continue;

            var nome = row.Cell(2).GetString().Trim();
            var telefone = row.Cell(3).GetString().Trim();
            var telemovel = row.Cell(4).GetString().Trim();
            var email = row.Cell(5).GetString().Trim();
            var funcao = row.Cell(6).GetString().Trim();

            var escola = escolas.FirstOrDefault(e => TextNormalizer.AreLikelySameSchool(e.Nome, col1));

            _db.Contactos.Add(new Contacto
            {
                EscolaId = escola?.Id,
                Nome = string.IsNullOrWhiteSpace(nome) ? "(sem nome registado)" : nome,
                Telefone = string.IsNullOrWhiteSpace(telefone) ? null : telefone,
                Telemovel = string.IsNullOrWhiteSpace(telemovel) ? null : telemovel,
                Email = string.IsNullOrWhiteSpace(email) ? null : email,
                Funcao = string.IsNullOrWhiteSpace(funcao) ? null : funcao,
                Observacoes = escola == null ? $"Escola no ficheiro original: {col1}" : null
            });
            resultado.ContactosImportados++;
        }

        _db.SaveChanges();
    }

    // ---------------------------------------------------------------
    // 4) Abas mensais (JAN..DEZ) => Intervenções
    // ---------------------------------------------------------------
    private static readonly (string Aba, int Mes)[] MesesAbas =
    {
        ("JAN", 1), ("FEV", 2), ("MAR", 3), ("ABR", 4), ("MAI", 5), ("JUN", 6),
        ("JUL", 7), ("AGO", 8), ("SET", 9), ("OUT", 10), ("NOV", 11), ("DEZ", 12)
    };

    private void ImportarIntervencoesMensais(XLWorkbook wb, ImportResult resultado)
    {
        var escolas = _db.Escolas.ToList();
        var agrupamentos = _db.Agrupamentos.ToList();
        var categorias = GarantirCategoriasBase();

        foreach (var (aba, mes) in MesesAbas)
        {
            if (!wb.Worksheets.TryGetWorksheet(aba, out var ws)) continue;

            // Encontra a linha de cabeçalho (contém "Data Pedido")
            var headerRow = ws.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString().Trim() == "Data Pedido");
            if (headerRow == null) continue;

            foreach (var row in ws.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
            {
                var dataCell = row.Cell(2);
                if (!dataCell.TryGetValue<DateTime>(out var data)) continue;

                var estabelecimento = row.Cell(3).GetString().Trim();
                var nomeAgrupamento = row.Cell(5).GetString().Trim();
                var hardware = row.Cell(6).GetString().Trim();
                var software = row.Cell(7).GetString().Trim();
                var redes = row.Cell(8).GetString().Trim();
                var vpn = row.Cell(9).GetString().Trim();
                var audioVisual = row.Cell(10).GetString().Trim();
                var tipoIntervencao = row.Cell(11).GetString().Trim();
                var materialAbatido = row.Cell(12).GetString().Trim();

                if (string.IsNullOrWhiteSpace(estabelecimento)) continue;

                var escola = escolas.FirstOrDefault(e => TextNormalizer.AreLikelySameSchool(e.Nome, estabelecimento));
                var agrupamento = agrupamentos.FirstOrDefault(a =>
                    nomeAgrupamento.Contains(a.Nome, StringComparison.OrdinalIgnoreCase) ||
                    a.Nome.Contains(nomeAgrupamento, StringComparison.OrdinalIgnoreCase));

                if (escola == null || agrupamento == null)
                {
                    resultado.Avisos.Add($"[{aba}] Escola/Agrupamento não encontrado para '{estabelecimento}'.");
                    continue;
                }

                var intervencao = new Intervencao
                {
                    Data = data,
                    Mes = mes,
                    Ano = data.Year,
                    EscolaId = escola.Id,
                    AgrupamentoId = agrupamento.Id,
                    Descricao = tipoIntervencao,
                    MaterialRecolhidoAbatido = string.IsNullOrWhiteSpace(materialAbatido) ? null : materialAbatido,
                    Estado = EstadoIntervencao.Fechada
                };

                void AdicionarCategoria(string valor, string nomeCategoria)
                {
                    if (string.IsNullOrWhiteSpace(valor)) return;
                    if (!int.TryParse(valor, out var qtd) || qtd <= 0) qtd = 1;
                    var cat = categorias[nomeCategoria];
                    intervencao.Categorias.Add(new IntervencaoCategoria
                    {
                        CategoriaIntervencaoId = cat.Id,
                        Quantidade = qtd
                    });
                }

                AdicionarCategoria(hardware, "Hardware");
                AdicionarCategoria(software, "Software");
                AdicionarCategoria(redes, "Redes");
                AdicionarCategoria(vpn, "VPN");
                AdicionarCategoria(audioVisual, "Audio-Visual");

                _db.Intervencoes.Add(intervencao);
                resultado.IntervencoesImportadas++;
            }
        }

        _db.SaveChanges();
    }

    private Dictionary<string, CategoriaIntervencao> GarantirCategoriasBase()
    {
        var nomes = new (string Nome, string Cor)[]
        {
            ("Redes", "#8B5CF6"),
            ("Hardware", "#EF4444"),
            ("Software", "#22C55E"),
            ("VPN", "#3B82F6"),
            ("Audio-Visual", "#F59E0B")
        };

        var existentes = _db.CategoriasIntervencao.ToDictionary(c => c.Nome);
        foreach (var (nome, cor) in nomes)
        {
            if (!existentes.ContainsKey(nome))
            {
                var nova = new CategoriaIntervencao { Nome = nome, CorHex = cor };
                _db.CategoriasIntervencao.Add(nova);
                _db.SaveChanges();
                existentes[nome] = nova;
            }
        }

        return existentes;
    }

    // ---------------------------------------------------------------
    // 5) Aba "Serv. DISIA"
    // ---------------------------------------------------------------
    private void ImportarServDisia(XLWorkbook wb, ImportResult resultado)
    {
        if (!wb.Worksheets.TryGetWorksheet("Serv. DISIA", out var ws)) return;

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var descricao = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(descricao)) continue;

            var divisao = row.Cell(3).GetString().Trim();
            var suporte = row.Cell(4).GetString().Trim();

            // Deteta padrão "(2x)" no fim da descrição para preencher a quantidade
            var quantidade = 1;
            var match = System.Text.RegularExpressions.Regex.Match(descricao, @"\((\d+)x\)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                quantidade = int.Parse(match.Groups[1].Value);
                descricao = descricao[..match.Index].Trim();
            }

            _db.AtividadesDisia.Add(new AtividadeDisia
            {
                Data = DateTime.Today,
                Mes = DateTime.Today.Month,
                Ano = DateTime.Today.Year,
                Descricao = descricao,
                Divisao = string.IsNullOrWhiteSpace(divisao) ? null : divisao,
                Suporte = string.IsNullOrWhiteSpace(suporte) ? null : suporte,
                Quantidade = quantidade,
                Estado = EstadoIntervencao.Fechada
            });
            resultado.AtividadesDisiaImportadas++;
        }

        _db.SaveChanges();
    }

    public ImportResult ImportarAtividadesDisia(string caminhoFicheiro)
    {
        var resultado = new ImportResult();

        using var wb = new XLWorkbook(caminhoFicheiro);

        // 9: procura primeiro a aba "Atividades DISIA" (nome usado por ExcelExportService.ExportarTudo
        // e por "Importar Tudo"); se não existir, usa a primeira aba do ficheiro, como antes —
        // preserva o comportamento para quem importa um ficheiro serviços_disia.xlsx isolado.
        var worksheet = wb.Worksheets.TryGetWorksheet("Atividades DISIA", out var wsNomeada)
            ? wsNomeada : wb.Worksheets.FirstOrDefault();
        if (worksheet == null)
        {
            resultado.Avisos.Add("O ficheiro não contém nenhuma aba.");
            return resultado;
        }

        var rowsUsed = worksheet.RowsUsed().ToList();
        if (rowsUsed.Count < 2)
        {
            resultado.Avisos.Add("O ficheiro está vazio ou não contém dados suficientes.");
            return resultado;
        }

        var categorias = _db.CategoriasDisia.ToList();

        // Trazer para memória as atividades já existentes para evitar problemas com LINQ to SQL
        var atividadesExistentes = _db.AtividadesDisia
            .Select(a => new { a.Data, a.Descricao })
            .ToList();

        // Pula o cabeçalho (primeira linha)
        foreach (var row in rowsUsed.Skip(1))
        {
            try
            {
                // Coluna 1: Data (formato dd/mm/yyyy)
                var dataCell = row.Cell(1).GetString().Trim();
                if (!DateTime.TryParse(dataCell, out var data))
                {
                    // Tenta como DateTime do Excel
                    if (!row.Cell(1).TryGetValue<DateTime>(out data))
                    {
                        resultado.Avisos.Add($"Linha {row.RowNumber()}: Data inválida '{dataCell}'.");
                        continue;
                    }
                }

                // Coluna 2: Descrição
                var descricao = row.Cell(2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(descricao))
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Descrição vazia. Linha ignorada.");
                    continue;
                }

                // Coluna 3: Categoria
                var categoriaNome = row.Cell(3).GetString().Trim();
                var categoria = categorias.FirstOrDefault(c =>
                    c.Nome.Equals(categoriaNome, StringComparison.OrdinalIgnoreCase)) ?? categorias.FirstOrDefault();

                // Coluna 4: Local
                var local = row.Cell(4).GetString().Trim();

                // Coluna 5: Divisão / Serviço envolvido
                var divisao = row.Cell(5).GetString().Trim();

                // Coluna 6: Suporte prestado
                var suporte = row.Cell(6).GetString().Trim();

                // Coluna 7: Quantidade
                int quantidade = 1;
                var qtdCell = row.Cell(7).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(qtdCell) && int.TryParse(qtdCell, out var qtdParsed))
                    quantidade = qtdParsed > 0 ? qtdParsed : 1;

                // Verifica se já existe atividade muito semelhante (mesmo dia e descrição)
                // Comparação em memória (já foi feito ToList())
                var jáExiste = atividadesExistentes.Any(a =>
                    a.Data == data &&
                    a.Descricao.Equals(descricao, StringComparison.OrdinalIgnoreCase));

                if (jáExiste)
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Atividade de {data:dd/MM/yyyy} '{descricao}' já existe. Ignorada.");
                    continue;
                }

                _db.AtividadesDisia.Add(new AtividadeDisia
                {
                    Data = data,
                    Mes = data.Month,
                    Ano = data.Year,
                    Descricao = descricao,
                    CategoriaDisiaId = categoria?.Id,
                    Local = string.IsNullOrWhiteSpace(local) ? null : local,
                    Divisao = string.IsNullOrWhiteSpace(divisao) ? null : divisao,
                    Suporte = string.IsNullOrWhiteSpace(suporte) ? null : suporte,
                    Quantidade = quantidade,
                    Estado = EstadoIntervencao.Fechada,
                    Observacoes = $"Importado de serviços_disia.xlsx em {DateTime.Now:dd/MM/yyyy HH:mm}"
                });

                resultado.AtividadesDisiaImportadas++;
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Linha {row.RowNumber()}: Erro ao processar - {ex.Message}");
            }
        }

        _db.SaveChanges();
        return resultado;
    }

    /// <summary>
    /// Importa intervenções a partir do "Modelo de Importação de Intervenções" (uma linha por
    /// intervenção): Data, Escola, Código GEPE (opcional), Descrição, Categorias (separadas por
    /// ";"), Material Recolhido/Abatido, Estado e Motivo Pendente. Usa a aba "Intervenções" se
    /// existir; caso contrário usa a primeira aba do ficheiro. Nunca duplica registos já
    /// existentes (mesma data + escola + descrição).
    /// </summary>
    public ImportResult ImportarIntervencoesDedicado(string caminhoXlsx)
    {
        var resultado = new ImportResult();

        using var wb = new XLWorkbook(caminhoXlsx);
        var worksheet = wb.Worksheets.TryGetWorksheet("Intervenções", out var wsNomeada)
            ? wsNomeada
            : wb.Worksheets.FirstOrDefault();

        if (worksheet == null)
        {
            resultado.Avisos.Add("O ficheiro não contém nenhuma aba.");
            return resultado;
        }

        // Encontra a linha de cabeçalho pela coluna "Escola" (a 2ª coluna do modelo),
        // em vez de assumir um número de linha fixo, para tolerar título/legenda no topo.
        var headerRow = worksheet.RowsUsed()
            .FirstOrDefault(r => r.Cell(2).GetString().Trim().Equals("Escola", StringComparison.OrdinalIgnoreCase));
        if (headerRow == null)
        {
            resultado.Avisos.Add("Não foi possível encontrar a linha de cabeçalho (coluna 'Escola'). Verifique se está a usar o modelo fornecido.");
            return resultado;
        }

        var escolas = _db.Escolas.ToList();
        var categorias = _db.CategoriasIntervencao.ToList();

        var estadosPersonalizados = _db.EstadosCorPersonalizados
            .Where(e => e.Grupo == GruposEstadoCor.Intervencao)
            .ToList();

        EstadoIntervencao ResolverEstado(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return EstadoIntervencao.Fechada;

            var porNomeExibicao = estadosPersonalizados
                .FirstOrDefault(e => e.NomeExibicao.Equals(texto, StringComparison.OrdinalIgnoreCase));
            if (porNomeExibicao != null && Enum.TryParse<EstadoIntervencao>(porNomeExibicao.NomeEstado, out var viaExibicao))
                return viaExibicao;

            var semEspacos = texto.Replace(" ", "");
            if (Enum.TryParse<EstadoIntervencao>(semEspacos, true, out var viaEnum))
                return viaEnum;

            return EstadoIntervencao.Fechada;
        }

        // Regista intervenções já existentes (data + escola + descrição) para nunca duplicar.
        var existentes = _db.Intervencoes
            .Select(i => new { i.Data, i.EscolaId, i.Descricao })
            .ToList();

        foreach (var row in worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            try
            {
                if (!row.Cell(2).TryGetValue<string>(out var nomeEscolaBruto) || string.IsNullOrWhiteSpace(nomeEscolaBruto))
                    continue; // linha em branco

                var dataTexto = row.Cell(1).GetString().Trim();
                DateTime data;
                if (!row.Cell(1).TryGetValue<DateTime>(out data) && !DateTime.TryParse(dataTexto, out data))
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Data inválida ou em falta.");
                    continue;
                }

                var nomeEscola = nomeEscolaBruto.Trim();
                var codGepeTexto = row.Cell(3).GetString().Trim();
                var descricao = row.Cell(4).GetString().Trim();
                var categoriasTexto = row.Cell(5).GetString().Trim();
                var materialAbatido = row.Cell(6).GetString().Trim();
                var estadoTexto = row.Cell(7).GetString().Trim();
                var motivoPendente = row.Cell(8).GetString().Trim();

                if (string.IsNullOrWhiteSpace(descricao))
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Descrição em falta. Linha ignorada.");
                    continue;
                }

                // Correspondência por Código GEPE (exata) tem prioridade; caso contrário,
                // usa a mesma comparação aproximada de nomes usada no resto da aplicação.
                Escola? escola = null;
                if (int.TryParse(codGepeTexto, out var codGepe) && codGepe > 0)
                    escola = escolas.FirstOrDefault(e => e.CodGEPE == codGepe);
                escola ??= escolas.FirstOrDefault(e => TextNormalizer.AreLikelySameSchool(e.Nome, nomeEscola));

                if (escola == null)
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Escola '{nomeEscola}' não encontrada. Linha ignorada.");
                    continue;
                }

                var jaExiste = existentes.Any(e =>
                    e.Data == data.Date && e.EscolaId == escola.Id &&
                    e.Descricao.Equals(descricao, StringComparison.OrdinalIgnoreCase));
                if (jaExiste)
                {
                    resultado.IntervencoesIgnoradas++;
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Intervenção de {data:dd/MM/yyyy} em '{escola.Nome}' já existe. Ignorada.");
                    continue;
                }

                var intervencao = new Intervencao
                {
                    Data = data.Date,
                    Mes = data.Month,
                    Ano = data.Year,
                    EscolaId = escola.Id,
                    AgrupamentoId = escola.AgrupamentoId,
                    Descricao = descricao,
                    MaterialRecolhidoAbatido = string.IsNullOrWhiteSpace(materialAbatido) ? null : materialAbatido,
                    Estado = ResolverEstado(estadoTexto),
                    MotivoPendente = string.IsNullOrWhiteSpace(motivoPendente) ? null : motivoPendente
                };

                foreach (var nomeCategoria in categoriasTexto.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var nomeLimpo = nomeCategoria.Trim();
                    if (nomeLimpo.Length == 0) continue;

                    var categoria = categorias.FirstOrDefault(c => c.Nome.Equals(nomeLimpo, StringComparison.OrdinalIgnoreCase));
                    if (categoria == null)
                    {
                        resultado.Avisos.Add($"Linha {row.RowNumber()}: Categoria '{nomeLimpo}' não reconhecida (verifique Administração → Dados Fixos → Categorias de Intervenção).");
                        continue;
                    }

                    intervencao.Categorias.Add(new IntervencaoCategoria { CategoriaIntervencaoId = categoria.Id, Quantidade = 1 });
                }

                _db.Intervencoes.Add(intervencao);
                resultado.IntervencoesImportadas++;
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Linha {row.RowNumber()}: Erro ao processar - {ex.Message}");
            }
        }

        _db.SaveChanges();
        return resultado;
    }

    /// <summary>
    /// Localiza a linha de cabeçalho de uma folha à procura de uma célula com o texto indicado,
    /// devolvendo null se não for encontrada (usado por todos os importadores "por fases" para
    /// tolerar títulos/legendas antes da tabela propriamente dita).
    /// </summary>
    private static IXLRow? EncontrarCabecalho(IXLWorksheet worksheet, string textoColuna, int coluna = 1)
    {
        return worksheet.RowsUsed()
            .FirstOrDefault(r => r.Cell(coluna).GetString().Trim().Equals(textoColuna, StringComparison.OrdinalIgnoreCase));
    }

    private Escola? ResolverEscola(List<Escola> escolas, string nomeEscola, string codGepeTexto)
    {
        Escola? escola = null;
        if (int.TryParse(codGepeTexto, out var codGepe) && codGepe > 0)
            escola = escolas.FirstOrDefault(e => e.CodGEPE == codGepe);
        escola ??= escolas.FirstOrDefault(e => TextNormalizer.AreLikelySameSchool(e.Nome, nomeEscola));
        return escola;
    }

    /// <summary>
    /// FASE 2 — Importa Equipamento a partir de uma folha Excel (uma linha por equipamento).
    /// Colunas (por esta ordem): Nº Série, Nº Inventário, Tipo, Marca, Modelo, Escola, Código GEPE
    /// (opcional), Estado (opcional; vazio = "Em Serviço"), Observações (opcional).
    /// Nunca duplica (chave: Nº Série).
    /// </summary>
    public ImportResultEquipamento ImportarEquipamento(string caminhoXlsx)
    {
        var resultado = new ImportResultEquipamento();
        using var wb = new XLWorkbook(caminhoXlsx);
        var worksheet = wb.Worksheets.TryGetWorksheet("Equipamento", out var wsNomeada) ? wsNomeada : wb.Worksheets.FirstOrDefault();
        if (worksheet == null) { resultado.Avisos.Add("O ficheiro não contém nenhuma aba."); return resultado; }

        var headerRow = EncontrarCabecalho(worksheet, "Nº Série");
        if (headerRow == null)
        {
            resultado.Avisos.Add("Não foi possível encontrar a linha de cabeçalho (coluna 'Nº Série'). Verifique se está a usar o modelo fornecido.");
            return resultado;
        }

        var escolas = _db.Escolas.ToList();
        var existentesPorSerie = _db.Equipamentos.Select(e => e.NumeroSerie).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            try
            {
                var numeroSerie = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroSerie)) continue; // linha em branco

                var numeroInventario = row.Cell(2).GetString().Trim();
                var tipo = row.Cell(3).GetString().Trim();
                var marca = row.Cell(4).GetString().Trim();
                var modelo = row.Cell(5).GetString().Trim();
                var nomeEscola = row.Cell(6).GetString().Trim();
                var codGepeTexto = row.Cell(7).GetString().Trim();
                var estadoTexto = row.Cell(8).GetString().Trim();
                var observacoes = row.Cell(9).GetString().Trim();

                if (existentesPorSerie.Contains(numeroSerie))
                {
                    resultado.EquipamentosIgnoradosPorDuplicado++;
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Equipamento com Nº Série '{numeroSerie}' já existe. Ignorado.");
                    continue;
                }

                Escola? escola = string.IsNullOrWhiteSpace(nomeEscola) ? null : ResolverEscola(escolas, nomeEscola, codGepeTexto);
                if (!string.IsNullOrWhiteSpace(nomeEscola) && escola == null)
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Escola '{nomeEscola}' não encontrada. Equipamento importado sem escola associada.");

                var estadoNormalizado = estadoTexto.Replace(" ", "").ToLowerInvariant();
                var estado = estadoNormalizado switch
                {
                    "recolhido" => EstadosEquipamento.Recolhido,
                    "emreparacao" or "emreparação" => EstadosEquipamento.EmReparacao,
                    "reparado" => EstadosEquipamento.Reparado,
                    "aguardaentrega" => EstadosEquipamento.AguardaEntrega,
                    "emarmazem" or "emarmazém" => EstadosEquipamento.EmArmazem,
                    "abatido" => EstadosEquipamento.Abatido,
                    _ => EstadosEquipamento.EmServico
                };

                _db.Equipamentos.Add(new Equipamento
                {
                    NumeroSerie = numeroSerie,
                    NumeroInventario = string.IsNullOrWhiteSpace(numeroInventario) ? numeroSerie : numeroInventario,
                    Tipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo,
                    Marca = string.IsNullOrWhiteSpace(marca) ? null : marca,
                    Modelo = string.IsNullOrWhiteSpace(modelo) ? null : modelo,
                    EscolaId = escola?.Id,
                    Estado = estado,
                    Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes
                });
                existentesPorSerie.Add(numeroSerie);
                resultado.EquipamentosImportados++;
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Linha {row.RowNumber()}: Erro ao processar - {ex.Message}");
            }
        }

        _db.SaveChanges();
        return resultado;
    }

    /// <summary>
    /// FASE 4 — Importa registos de Equipamento Abatido. Colunas: Nº Série (do equipamento já
    /// existente no inventário; obrigatório), Data de Abate, Status, Escola/Local, Descrição,
    /// Observações. Nunca duplica (chave: Nº Série + Data de Abate).
    /// </summary>
    public ImportResultEquipamentoAbatido ImportarEquipamentoAbatido(string caminhoXlsx)
    {
        var resultado = new ImportResultEquipamentoAbatido();
        using var wb = new XLWorkbook(caminhoXlsx);
        var worksheet = wb.Worksheets.TryGetWorksheet("Equipamento Abatido", out var wsNomeada) ? wsNomeada : wb.Worksheets.FirstOrDefault();
        if (worksheet == null) { resultado.Avisos.Add("O ficheiro não contém nenhuma aba."); return resultado; }

        var headerRow = EncontrarCabecalho(worksheet, "Nº Série");
        if (headerRow == null)
        {
            resultado.Avisos.Add("Não foi possível encontrar a linha de cabeçalho (coluna 'Nº Série'). Verifique se está a usar o modelo fornecido.");
            return resultado;
        }

        var equipamentos = _db.Equipamentos.Include(e => e.Escola).ToList();
        var existentes = _db.EquipamentosAbatidos.Include(a => a.Equipamento)
            .Select(a => new { Serie = a.Equipamento != null ? a.Equipamento.NumeroSerie : null, a.DataAbate }).ToList();

        foreach (var row in worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            try
            {
                var numeroSerie = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroSerie)) continue;

                if (!row.Cell(2).TryGetValue<DateTime>(out var dataAbate) && !DateTime.TryParse(row.Cell(2).GetString(), out dataAbate))
                    dataAbate = DateTime.Today;

                var status = row.Cell(3).GetString().Trim();
                var escolaOuLocal = row.Cell(4).GetString().Trim();
                var descricao = row.Cell(5).GetString().Trim();
                var observacoes = row.Cell(6).GetString().Trim();

                var equipamento = equipamentos.FirstOrDefault(eq => eq.NumeroSerie.Equals(numeroSerie, StringComparison.OrdinalIgnoreCase));
                if (equipamento == null)
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Equipamento com Nº Série '{numeroSerie}' não encontrado no inventário. Linha ignorada.");
                    continue;
                }

                var jaExiste = existentes.Any(a => a.Serie != null &&
                    a.Serie.Equals(numeroSerie, StringComparison.OrdinalIgnoreCase) && a.DataAbate.Date == dataAbate.Date);
                if (jaExiste)
                {
                    resultado.AbatesIgnoradosPorDuplicado++;
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Abate de '{numeroSerie}' em {dataAbate:dd/MM/yyyy} já existe. Ignorado.");
                    continue;
                }

                _db.EquipamentosAbatidos.Add(new EquipamentoAbatido
                {
                    EquipamentoId = equipamento.Id,
                    DataAbate = dataAbate.Date,
                    Status = string.IsNullOrWhiteSpace(status) ? "Abatido" : status,
                    EscolaOuLocal = string.IsNullOrWhiteSpace(escolaOuLocal) ? equipamento.Escola?.Nome : escolaOuLocal,
                    DescricaoEquipamento = string.IsNullOrWhiteSpace(descricao) ? $"{equipamento.Tipo} {equipamento.Marca} {equipamento.Modelo}".Trim() : descricao,
                    NumeroSerie = equipamento.NumeroSerie,
                    NumeroInventario = equipamento.NumeroInventario,
                    Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes
                });
                equipamento.Estado = EstadosEquipamento.Abatido;
                resultado.AbatesImportados++;
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Linha {row.RowNumber()}: Erro ao processar - {ex.Message}");
            }
        }

        _db.SaveChanges();
        return resultado;
    }

    /// <summary>
    /// FASE 5 — Importa registos de Equipamento Recolhido. Colunas: Nº Série (do equipamento já
    /// existente no inventário; obrigatório), Data de Recolha, Estado (Pendente/Em Reparação/
    /// Reparado/Entregue; vazio = Pendente), Data de Entrega (opcional), Observações.
    /// Nunca duplica (chave: Nº Série + Data de Recolha).
    /// </summary>
    public ImportResultEquipamentoRecolhido ImportarEquipamentoRecolhido(string caminhoXlsx)
    {
        var resultado = new ImportResultEquipamentoRecolhido();
        using var wb = new XLWorkbook(caminhoXlsx);
        var worksheet = wb.Worksheets.TryGetWorksheet("Equipamento Recolhido", out var wsNomeada) ? wsNomeada : wb.Worksheets.FirstOrDefault();
        if (worksheet == null) { resultado.Avisos.Add("O ficheiro não contém nenhuma aba."); return resultado; }

        var headerRow = EncontrarCabecalho(worksheet, "Nº Série");
        if (headerRow == null)
        {
            resultado.Avisos.Add("Não foi possível encontrar a linha de cabeçalho (coluna 'Nº Série'). Verifique se está a usar o modelo fornecido.");
            return resultado;
        }

        var equipamentos = _db.Equipamentos.ToList();
        var existentes = _db.EquipamentosRecolhidos.Include(r => r.Equipamento)
            .Select(r => new { Serie = r.Equipamento != null ? r.Equipamento.NumeroSerie : null, r.DataRecolha }).ToList();

        var estadosValidos = new[] { EstadosRecolha.Pendente, EstadosRecolha.EmReparacao, EstadosRecolha.AguardaEntrega, EstadosRecolha.Entregue };

        foreach (var row in worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            try
            {
                var numeroSerie = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(numeroSerie)) continue;

                if (!row.Cell(2).TryGetValue<DateTime>(out var dataRecolha) && !DateTime.TryParse(row.Cell(2).GetString(), out dataRecolha))
                    dataRecolha = DateTime.Today;

                var estadoTexto = row.Cell(3).GetString().Trim();
                var temDataEntrega = row.Cell(4).TryGetValue<DateTime>(out var dataEntrega) || DateTime.TryParse(row.Cell(4).GetString(), out dataEntrega);
                var observacoes = row.Cell(5).GetString().Trim();

                var equipamento = equipamentos.FirstOrDefault(eq => eq.NumeroSerie.Equals(numeroSerie, StringComparison.OrdinalIgnoreCase));
                if (equipamento == null)
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Equipamento com Nº Série '{numeroSerie}' não encontrado no inventário. Só é possível recolher equipamento já existente. Linha ignorada.");
                    continue;
                }

                var jaExiste = existentes.Any(r => r.Serie != null &&
                    r.Serie.Equals(numeroSerie, StringComparison.OrdinalIgnoreCase) && r.DataRecolha.Date == dataRecolha.Date);
                if (jaExiste)
                {
                    resultado.RecolhidosIgnoradosPorDuplicado++;
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Recolha de '{numeroSerie}' em {dataRecolha:dd/MM/yyyy} já existe. Ignorada.");
                    continue;
                }

                var estado = estadosValidos.FirstOrDefault(v => v.Equals(estadoTexto, StringComparison.OrdinalIgnoreCase)) ?? EstadosRecolha.Pendente;

                _db.EquipamentosRecolhidos.Add(new EquipamentoRecolhido
                {
                    EquipamentoId = equipamento.Id,
                    DataRecolha = dataRecolha.Date,
                    Estado = estado,
                    DataEntrega = temDataEntrega ? dataEntrega.Date : null,
                    Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes
                });
                resultado.RecolhidosImportados++;
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Linha {row.RowNumber()}: Erro ao processar - {ex.Message}");
            }
        }

        _db.SaveChanges();
        return resultado;
    }

    /// <summary>
    /// DISIA — Importa Comunicações (ligações de fibra/outras dos jardins-escola). Colunas:
    /// Escola, Código GEPE (opcional), Tipo de Ligação, Velocidade de Fibra (opcional),
    /// Operadora, Nº Contrato, Data de Instalação, Integrado (Sim/Não), Estado, Observações.
    /// Nunca duplica (chave: Escola + Nº Contrato, quando preenchido); caso contrário atualiza
    /// o registo mais recente da mesma escola.
    /// </summary>
    public ImportResultComunicacoes ImportarComunicacoes(string caminhoXlsx)
    {
        var resultado = new ImportResultComunicacoes();
        using var wb = new XLWorkbook(caminhoXlsx);
        var worksheet = wb.Worksheets.TryGetWorksheet("Comunicações", out var wsNomeada) ? wsNomeada : wb.Worksheets.FirstOrDefault();
        if (worksheet == null) { resultado.Avisos.Add("O ficheiro não contém nenhuma aba."); return resultado; }

        var headerRow = EncontrarCabecalho(worksheet, "Escola");
        if (headerRow == null)
        {
            resultado.Avisos.Add("Não foi possível encontrar a linha de cabeçalho (coluna 'Escola'). Verifique se está a usar o modelo fornecido.");
            return resultado;
        }

        var escolas = _db.Escolas.ToList();
        var existentes = _db.Comunicacoes.ToList();

        foreach (var row in worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()))
        {
            try
            {
                var nomeEscola = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(nomeEscola)) continue;

                var codGepeTexto = row.Cell(2).GetString().Trim();
                var tipoLigacao = row.Cell(3).GetString().Trim();
                var velocidade = row.Cell(4).GetString().Trim();
                var operadora = row.Cell(5).GetString().Trim();
                var numContrato = row.Cell(6).GetString().Trim();
                var temDataInstalacao = row.Cell(7).TryGetValue<DateTime>(out var dataInstalacao) || DateTime.TryParse(row.Cell(7).GetString(), out dataInstalacao);
                var integradoTexto = row.Cell(8).GetString().Trim();
                var estado = row.Cell(9).GetString().Trim();
                var observacoes = row.Cell(10).GetString().Trim();

                var escola = ResolverEscola(escolas, nomeEscola, codGepeTexto);
                if (escola == null)
                {
                    resultado.Avisos.Add($"Linha {row.RowNumber()}: Escola '{nomeEscola}' não encontrada. Linha ignorada.");
                    continue;
                }

                var integrado = integradoTexto.Equals("Sim", StringComparison.OrdinalIgnoreCase) ||
                                 integradoTexto.Equals("Integrado", StringComparison.OrdinalIgnoreCase) ||
                                 integradoTexto.Equals("1", StringComparison.OrdinalIgnoreCase);

                var existente = !string.IsNullOrWhiteSpace(numContrato)
                    ? existentes.FirstOrDefault(c => c.EscolaId == escola.Id &&
                        (c.NumeroContrato ?? "").Equals(numContrato, StringComparison.OrdinalIgnoreCase))
                    : existentes.FirstOrDefault(c => c.EscolaId == escola.Id && string.IsNullOrWhiteSpace(c.NumeroContrato));

                if (existente == null)
                {
                    existente = new Comunicacao { EscolaId = escola.Id };
                    _db.Comunicacoes.Add(existente);
                    existentes.Add(existente);
                    resultado.ComunicacoesImportadas++;
                }
                else
                {
                    resultado.ComunicacoesAtualizadas++;
                }

                existente.TipoLigacao = string.IsNullOrWhiteSpace(tipoLigacao) ? "Fibra" : tipoLigacao;
                existente.VelocidadeFibra = string.IsNullOrWhiteSpace(velocidade) ? null : velocidade;
                existente.Operadora = string.IsNullOrWhiteSpace(operadora) ? null : operadora;
                existente.NumeroContrato = string.IsNullOrWhiteSpace(numContrato) ? null : numContrato;
                existente.DataInstalacao = temDataInstalacao ? dataInstalacao.Date : existente.DataInstalacao;
                existente.Integrado = integrado;
                existente.Estado = string.IsNullOrWhiteSpace(estado) ? "Ativa" : estado;
                existente.Observacoes = string.IsNullOrWhiteSpace(observacoes) ? null : observacoes;
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"Linha {row.RowNumber()}: Erro ao processar - {ex.Message}");
            }
        }

        _db.SaveChanges();
        return resultado;
    }

    /// <summary>9: importa todas as fases de uma só vez, a partir de um único ficheiro Excel — o
    /// mesmo formato (uma aba por fase, com os mesmos nomes e cabeçalhos) produzido por
    /// <see cref="ExcelExportService.ExportarTudo"/>, para que a importação fique alinhada/normalizada
    /// com a exportação "Tudo". A ordem respeita as dependências entre fases (Agrupamentos/Escolas
    /// primeiro; Atividades DISIA antes de Equipamento Recolhido, já que uma recolha pode ligar a
    /// uma Atividade DISIA). Cada fase corre de forma independente: se uma aba não existir, essa
    /// fase simplesmente não importa nada (as suas próprias mensagens de aviso explicam-no); se uma
    /// fase gerar um erro inesperado, esse erro fica registado e as restantes fases continuam.</summary>
    public ImportResultTudo ImportarTudo(string caminhoXlsx)
    {
        var resumo = new ImportResultTudo();

        void Fase(string nomeFase, Action correr)
        {
            try
            {
                correr();
            }
            catch (Exception ex)
            {
                resumo.ErrosFatais.Add($"{nomeFase}: {ex.Message}");
            }
        }

        Fase("Agrupamentos + Escolas", () => resumo.AgrupamentosEscolas = ImportarAgrupamentosEEscolas(caminhoXlsx));
        Fase("Equipamento", () => resumo.Equipamento = ImportarEquipamento(caminhoXlsx));
        Fase("Intervenções", () => resumo.Intervencoes = ImportarIntervencoesDedicado(caminhoXlsx));
        Fase("Atividades DISIA", () => resumo.AtividadesDisia = ImportarAtividadesDisia(caminhoXlsx));
        Fase("Equipamento Abatido", () => resumo.EquipamentoAbatido = ImportarEquipamentoAbatido(caminhoXlsx));
        Fase("Equipamento Recolhido", () => resumo.EquipamentoRecolhido = ImportarEquipamentoRecolhido(caminhoXlsx));
        Fase("Comunicações", () => resumo.Comunicacoes = ImportarComunicacoes(caminhoXlsx));

        return resumo;
    }
}
