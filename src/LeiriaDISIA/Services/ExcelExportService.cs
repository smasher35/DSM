using ClosedXML.Excel;
using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera ficheiros Excel (.xlsx) com os dados atualmente na aplicação, no mesmo formato de
/// cabeçalhos usado por <see cref="ExcelImportService"/> e <see cref="TemplateExcelService"/> —
/// ou seja, a operação inversa de cada uma das fases de importação. Útil para tirar um retrato
/// dos dados num determinado momento, ou como ponto de partida para reimportar depois de editar
/// em Excel.
/// </summary>
public static class ExcelExportService
{
    private static readonly XLColor CorCabecalho = XLColor.FromHtml("#1F4E79");

    // -------------------------------------------------------------------
    // Fase 1 — Agrupamentos + Escolas
    // -------------------------------------------------------------------
    public static void ExportarAgrupamentosEscolas(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var agrupamentos = db.Agrupamentos.OrderBy(a => a.Nome).ToList();
        CriarAba(wb, "Agrupamentos",
            new[] { "Id_Agrupamento", "cod_gepe", "Agrupamento", "Morada", "Contacto 1", "Contacto 2", "Contacto 3", "Email 1", "Email 2", "Site", "Observações" },
            agrupamentos.Select(a => new object?[]
            {
                a.Id, a.CodAgrupamento == 0 ? null : a.CodAgrupamento, a.Nome, a.Morada,
                a.Contacto1, a.Contacto2, a.Contacto3, a.Email1, a.Email2, a.Site, a.Observacoes
            }));

        var escolas = db.Escolas.Include(e => e.Agrupamento).OrderBy(e => e.Nome).ToList();
        CriarAba(wb, "Escolas",
            new[] { "Freguesia", "Código DGRH", "Código GEPE", "Estabelecimento de Ensino", "Morada", "Telefone", "E-mail", "Cod. Agrupamento" },
            escolas.Select(e => new object?[]
            {
                e.Freguesia, e.CodDGRHE, e.CodGEPE, e.Nome, e.Morada, e.Telefone, e.Email, e.AgrupamentoId
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 2 — Equipamento
    // -------------------------------------------------------------------
    public static void ExportarEquipamento(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var lista = db.Equipamentos.Include(e => e.Escola).OrderBy(e => e.NumeroSerie).ToList();
        CriarAba(wb, "Equipamento",
            new[] { "Nº Série", "Nº Inventário", "Tipo", "Marca", "Modelo", "Escola", "Código GEPE", "Estado", "Observações" },
            lista.Select(e => new object?[]
            {
                e.NumeroSerie, e.NumeroInventario, e.Tipo, e.Marca, e.Modelo,
                e.Escola?.Nome ?? e.LocalNaoEscolar, e.Escola?.CodGEPE, e.Estado, e.Observacoes
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 3 — Intervenções
    // -------------------------------------------------------------------
    public static void ExportarIntervencoes(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var lista = db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .OrderBy(i => i.Data).ToList();

        CriarAba(wb, "Intervenções",
            new[] { "Data", "Escola", "Código GEPE", "Descrição", "Categorias", "Material Recolhido/Abatido", "Estado", "Motivo Pendente" },
            lista.Select(i => new object?[]
            {
                i.Data.ToString("dd-MM-yyyy"), i.Escola?.Nome, i.Escola?.CodGEPE, i.Descricao,
                string.Join("; ", i.Categorias.Select(c => c.Categoria?.Nome).Where(n => !string.IsNullOrWhiteSpace(n))),
                i.MaterialRecolhidoAbatido, i.Estado.ToString(), i.MotivoPendente
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 4 — Equipamento Abatido
    // -------------------------------------------------------------------
    public static void ExportarEquipamentoAbatido(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var lista = db.EquipamentosAbatidos.Include(a => a.Equipamento).OrderBy(a => a.DataAbate).ToList();
        CriarAba(wb, "Equipamento Abatido",
            new[] { "Nº Série", "Nº Inventário", "Data de Abate", "Status", "Escola/Local", "Descrição", "Observações" },
            lista.Select(a => new object?[]
            {
                a.Equipamento?.NumeroSerie ?? a.NumeroSerie,
                a.Equipamento?.NumeroInventario ?? a.NumeroInventario,
                a.DataAbate.ToString("dd-MM-yyyy"),
                a.Status, a.EscolaOuLocal, a.DescricaoEquipamento, a.Observacoes
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Fase 5 — Equipamento Recolhido
    // -------------------------------------------------------------------
    public static void ExportarEquipamentoRecolhido(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var lista = db.EquipamentosRecolhidos.Include(r => r.Equipamento).OrderBy(r => r.DataRecolha).ToList();
        CriarAba(wb, "Equipamento Recolhido",
            new[] { "Nº Série", "Data de Recolha", "Estado", "Data de Entrega", "Observações" },
            lista.Select(r => new object?[]
            {
                r.Equipamento?.NumeroSerie, r.DataRecolha.ToString("dd-MM-yyyy"), r.Estado,
                r.DataEntrega?.ToString("dd-MM-yyyy"), r.Observacoes
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Atividades da DISIA
    // -------------------------------------------------------------------
    public static void ExportarAtividadesDisia(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var lista = db.AtividadesDisia.Include(a => a.Categoria).OrderBy(a => a.Data).ToList();
        CriarAba(wb, "Atividades DISIA",
            new[] { "Data", "Descrição", "Categoria", "Local", "Divisão / Serviço", "Suporte Prestado", "Quantidade" },
            lista.Select(a => new object?[]
            {
                a.Data.ToString("dd-MM-yyyy"), a.Descricao, a.Categoria?.Nome, a.Local, a.Divisao, a.Suporte, a.Quantidade
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Comunicações
    // -------------------------------------------------------------------
    public static void ExportarComunicacoes(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var lista = db.Comunicacoes.Include(c => c.Escola).OrderBy(c => c.Escola!.Nome).ToList();
        CriarAba(wb, "Comunicações",
            new[] { "Escola", "Código GEPE", "Tipo de Ligação", "Velocidade de Fibra", "Operadora", "Nº Contrato", "Data de Instalação", "Integrado", "Estado", "Observações" },
            lista.Select(c => new object?[]
            {
                c.Escola?.Nome, c.Escola?.CodGEPE, c.TipoLigacao, c.VelocidadeFibra, c.Operadora, c.NumeroContrato,
                c.DataInstalacao?.ToString("dd-MM-yyyy"), c.Integrado ? "Sim" : "Não", c.Estado, c.Observacoes
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Exporta tudo para um único ficheiro, com uma aba por cada fase.
    // -------------------------------------------------------------------
    public static void ExportarTudo(AppDbContext db, string caminhoDestino)
    {
        using var wb = new XLWorkbook();

        var agrupamentos = db.Agrupamentos.OrderBy(a => a.Nome).ToList();
        CriarAba(wb, "Agrupamentos",
            new[] { "Id_Agrupamento", "cod_gepe", "Agrupamento", "Morada", "Contacto 1", "Contacto 2", "Contacto 3", "Email 1", "Email 2", "Site", "Observações" },
            agrupamentos.Select(a => new object?[]
            {
                a.Id, a.CodAgrupamento == 0 ? null : a.CodAgrupamento, a.Nome, a.Morada,
                a.Contacto1, a.Contacto2, a.Contacto3, a.Email1, a.Email2, a.Site, a.Observacoes
            }));

        var escolas = db.Escolas.Include(e => e.Agrupamento).OrderBy(e => e.Nome).ToList();
        CriarAba(wb, "Escolas",
            new[] { "Freguesia", "Código DGRH", "Código GEPE", "Estabelecimento de Ensino", "Morada", "Telefone", "E-mail", "Cod. Agrupamento" },
            escolas.Select(e => new object?[]
            {
                e.Freguesia, e.CodDGRHE, e.CodGEPE, e.Nome, e.Morada, e.Telefone, e.Email, e.AgrupamentoId
            }));

        var equipamentos = db.Equipamentos.Include(e => e.Escola).OrderBy(e => e.NumeroSerie).ToList();
        CriarAba(wb, "Equipamento",
            new[] { "Nº Série", "Nº Inventário", "Tipo", "Marca", "Modelo", "Escola", "Código GEPE", "Estado", "Observações" },
            equipamentos.Select(e => new object?[]
            {
                e.NumeroSerie, e.NumeroInventario, e.Tipo, e.Marca, e.Modelo,
                e.Escola?.Nome ?? e.LocalNaoEscolar, e.Escola?.CodGEPE, e.Estado, e.Observacoes
            }));

        var intervencoes = db.Intervencoes
            .Include(i => i.Escola)
            .Include(i => i.Categorias).ThenInclude(c => c.Categoria)
            .OrderBy(i => i.Data).ToList();
        CriarAba(wb, "Intervenções",
            new[] { "Data", "Escola", "Código GEPE", "Descrição", "Categorias", "Material Recolhido/Abatido", "Estado", "Motivo Pendente" },
            intervencoes.Select(i => new object?[]
            {
                i.Data.ToString("dd-MM-yyyy"), i.Escola?.Nome, i.Escola?.CodGEPE, i.Descricao,
                string.Join("; ", i.Categorias.Select(c => c.Categoria?.Nome).Where(n => !string.IsNullOrWhiteSpace(n))),
                i.MaterialRecolhidoAbatido, i.Estado.ToString(), i.MotivoPendente
            }));

        var abatidos = db.EquipamentosAbatidos.Include(a => a.Equipamento).OrderBy(a => a.DataAbate).ToList();
        CriarAba(wb, "Equipamento Abatido",
            new[] { "Nº Série", "Nº Inventário", "Data de Abate", "Status", "Escola/Local", "Descrição", "Observações" },
            abatidos.Select(a => new object?[]
            {
                a.Equipamento?.NumeroSerie ?? a.NumeroSerie,
                a.Equipamento?.NumeroInventario ?? a.NumeroInventario,
                a.DataAbate.ToString("dd-MM-yyyy"), a.Status, a.EscolaOuLocal, a.DescricaoEquipamento, a.Observacoes
            }));

        var recolhidos = db.EquipamentosRecolhidos.Include(r => r.Equipamento).OrderBy(r => r.DataRecolha).ToList();
        CriarAba(wb, "Equipamento Recolhido",
            new[] { "Nº Série", "Data de Recolha", "Estado", "Data de Entrega", "Observações" },
            recolhidos.Select(r => new object?[]
            {
                r.Equipamento?.NumeroSerie, r.DataRecolha.ToString("dd-MM-yyyy"), r.Estado,
                r.DataEntrega?.ToString("dd-MM-yyyy"), r.Observacoes
            }));

        var atividades = db.AtividadesDisia.Include(a => a.Categoria).OrderBy(a => a.Data).ToList();
        CriarAba(wb, "Atividades DISIA",
            new[] { "Data", "Descrição", "Categoria", "Local", "Divisão / Serviço", "Suporte Prestado", "Quantidade" },
            atividades.Select(a => new object?[]
            {
                a.Data.ToString("dd-MM-yyyy"), a.Descricao, a.Categoria?.Nome, a.Local, a.Divisao, a.Suporte, a.Quantidade
            }));

        var comunicacoes = db.Comunicacoes.Include(c => c.Escola).OrderBy(c => c.Escola!.Nome).ToList();
        CriarAba(wb, "Comunicações",
            new[] { "Escola", "Código GEPE", "Tipo de Ligação", "Velocidade de Fibra", "Operadora", "Nº Contrato", "Data de Instalação", "Integrado", "Estado", "Observações" },
            comunicacoes.Select(c => new object?[]
            {
                c.Escola?.Nome, c.Escola?.CodGEPE, c.TipoLigacao, c.VelocidadeFibra, c.Operadora, c.NumeroContrato,
                c.DataInstalacao?.ToString("dd-MM-yyyy"), c.Integrado ? "Sim" : "Não", c.Estado, c.Observacoes
            }));

        wb.SaveAs(caminhoDestino);
    }

    // -------------------------------------------------------------------
    // Auxiliares
    // -------------------------------------------------------------------
    private static void CriarAba(XLWorkbook wb, string nomeAba, string[] cabecalhos, IEnumerable<object?[]> linhas)
    {
        var ws = wb.Worksheets.Add(nomeAba);

        for (var c = 0; c < cabecalhos.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cabecalhos[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = CorCabecalho;
        }

        var linha = 2;
        foreach (var valores in linhas)
        {
            for (var c = 0; c < valores.Length; c++)
                DefinirValorCelula(ws.Cell(linha, c + 1), valores[c]);
            linha++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        if (linha == 2) ws.Cell(2, 1).Value = "(sem registos)";
    }

    private static void DefinirValorCelula(IXLCell cell, object? valor)
    {
        switch (valor)
        {
            case null:
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
}
