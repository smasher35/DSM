using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Services;

public class DashboardResumo
{
    public int TotalAgrupamentos { get; set; }
    public int TotalEscolas { get; set; }
    public int TotalEdificios { get; set; }
    public int TotalJiIntegrados { get; set; }
    public int TotalJiIsolados { get; set; }
    public int TotalIntervencoesAnoCorrente { get; set; }
    public int TotalIntervencoesMesCorrente { get; set; }
    public int TotalIntervencoesGlobal { get; set; }
    public int PedidosPendentes { get; set; }

    /// <summary>Total de pedidos de intervenção "não concluídos", ou seja, a soma dos pedidos
    /// nos estados Pendente + Em Espera + Em Andamento (exclui Concluído e Cancelado). Usado no
    /// card "Pedidos não concluídos" do Dashboard.</summary>
    public int PedidosNaoConcluidos { get; set; }

    /// <summary>Total de equipamento informático do tipo "computador" (secretária, portátil, servidor).</summary>
    public int TotalComputadores { get; set; }
    /// <summary>Total de computadores atualmente recolhidos (fora da escola, ainda não entregues).</summary>
    public int TotalComputadoresRecolhidos { get; set; }

    /// <summary>Total de TODOS os equipamentos informáticos registados (qualquer tipo/estado).
    /// Usado como denominador (100%) nos gauges "Distribuição do Parque de Equipamento".</summary>
    public int TotalEquipamentoGeral { get; set; }
    /// <summary>Total de equipamentos do tipo "Computador de Secretária" (ver <see cref="Data.DbInitializer"/>).</summary>
    public int TotalComputadoresSecretaria { get; set; }
    /// <summary>Total de equipamentos do tipo "Portátil".</summary>
    public int TotalPortateis { get; set; }
    /// <summary>Total de equipamentos do tipo "Switch".</summary>
    public int TotalSwitches { get; set; }
    /// <summary>Total de equipamentos do tipo "Access Point".</summary>
    public int TotalAccessPoints { get; set; }
    /// <summary>Total de equipamentos do tipo "Impressora".</summary>
    public int TotalImpressoras { get; set; }
    /// <summary>Total de equipamento classificado como "Obsoleto" pelo índice de obsolescência
    /// (ver Administração → Obsolescência para ajustar os pesos e limiares do cálculo).</summary>
    public int TotalEquipamentoObsoleto { get; set; }

    /// <summary>Pendentes do lado da Escola: pedidos de intervenção feitos pelas escolas que
    /// ainda aguardam ação da DISIA.</summary>
    public int PendentesEscola { get; set; }
    /// <summary>Pendentes do lado da DISIA: equipamento já recolhido para as instalações da
    /// DISIA que ainda não foi entregue de volta (pendente de reparação/entrega).</summary>
    public int PendentesDisia { get; set; }

    /// <summary>Atividades da DISIA (fora do âmbito escolar) que ainda estão com o estado "Pendente".</summary>
    public int PendentesAtividadesDisia { get; set; }

    /// <summary>Total de atividades da DISIA já iniciadas mas ainda não concluídas, ou seja, a soma
    /// das atividades nos estados Pendente + Em Progresso + Em Espera (exclui Fechada e Cancelada).
    /// Usado no card "Atividades iniciadas mas não concluídas" do Dashboard.</summary>
    public int AtividadesDisiaNaoConcluidas { get; set; }

    public string? AgrupamentoMaisIntervencionado { get; set; }
    public int AgrupamentoMaisIntervencionadoTotal { get; set; }

    public string? EscolaMaisIntervencionada { get; set; }
    public int EscolaMaisIntervencionadaTotal { get; set; }

    public List<(string Mes, int Total)> IntervencoesPorMesAnoCorrente { get; set; } = new();
    public List<(string Categoria, int Total, string Cor)> IntervencoesPorCategoria { get; set; } = new();

    /// <summary>(2.3) Igual a <see cref="IntervencoesPorCategoria"/> mas restrito ao mês corrente,
    /// usado no gráfico "Intervenções por categoria (total) - Mês Corrente".</summary>
    public List<(string Categoria, int Total, string Cor)> IntervencoesPorCategoriaMesCorrente { get; set; } = new();

    /// <summary>Total de computadores (secretária, portátil, servidor) atualmente com o estado
    /// "Aguarda Entrega" — já reparados/prontos mas ainda por entregar à escola. (2.1)</summary>
    public int TotalComputadoresAguardamEntrega { get; set; }

    /// <summary>Abreviatura usada como rótulo/série no gráfico de barras (ver <see cref="Models.Agrupamento.Abreviatura"/>);
    /// Agrupamento guarda o nome completo, usado na legenda apresentada por baixo do gráfico.</summary>
    public List<(string Agrupamento, string Abreviatura, int Total)> IntervencoesPorAgrupamentoMesCorrente { get; set; } = new();
    public List<(string Agrupamento, string Abreviatura, int Total)> IntervencoesPorAgrupamentoAnoCorrente { get; set; } = new();

    /// <summary>(2.5) Intervenções por categoria, agrupadas por Agrupamento — total anual. Cada item
    /// da lista é uma categoria (com a sua cor) e os totais alinhados, posição a posição, com
    /// <see cref="AgrupamentosAbreviaturasAno"/>, para desenhar um gráfico de barras empilhadas.</summary>
    public List<string> AgrupamentosAbreviaturasAno { get; set; } = new();
    public string LegendaAgrupamentosAno { get; set; } = string.Empty;
    public List<(string Categoria, string Cor, List<int> Totais)> IntervencoesPorCategoriaEAgrupamentoAno { get; set; } = new();

    /// <summary>(2.5) Igual ao anterior, mas para o mês corrente.</summary>
    public List<string> AgrupamentosAbreviaturasMes { get; set; } = new();
    public string LegendaAgrupamentosMes { get; set; } = string.Empty;
    public List<(string Categoria, string Cor, List<int> Totais)> IntervencoesPorCategoriaEAgrupamentoMes { get; set; } = new();
}

public class DashboardService
{
    private readonly AppDbContext _db;
    public DashboardService(AppDbContext db) => _db = db;

    private static readonly string[] NomesMeses =
    {
        "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out", "Nov", "Dez"
    };

    private static bool IsJardimInfancia(string? tipo) =>
        tipo != null && tipo.Contains("Jardim", StringComparison.OrdinalIgnoreCase);

    private static bool IsComputador(string? tipo) =>
        tipo != null && (
            tipo.Contains("Computador", StringComparison.OrdinalIgnoreCase) ||
            tipo.Contains("Portátil", StringComparison.OrdinalIgnoreCase) ||
            tipo.Contains("Servidor", StringComparison.OrdinalIgnoreCase));

    /// <summary>Palavras que não entram na abreviatura automática (ver <see cref="GerarAbreviaturaAutomatica"/>).</summary>
    private static readonly HashSet<string> PreposicoesAbreviatura = new(StringComparer.OrdinalIgnoreCase)
        { "de", "do", "da", "dos", "das", "e" };

    /// <summary>Gera uma abreviatura aproximada a partir das iniciais do nome (ignorando
    /// preposições comuns), para agrupamentos que ainda não têm uma Abreviatura definida em
    /// Agrupamentos → Editar. Ex.: "Agrupamento de Escolas de Leiria" → "AEL".</summary>
    private static string GerarAbreviaturaAutomatica(string nome)
    {
        var iniciais = nome
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !PreposicoesAbreviatura.Contains(p))
            .Select(p => char.ToUpperInvariant(p[0]))
            .ToArray();
        return iniciais.Length > 0 ? new string(iniciais) : nome;
    }

    /// <summary>Devolve o nome completo e a abreviatura a usar nos gráficos para um agrupamento
    /// (ou para "sem agrupamento", quando a intervenção não tem nenhum associado).</summary>
    private static (string Nome, string Abreviatura) NomeEAbreviatura(Agrupamento? agrupamento)
    {
        if (agrupamento == null) return ("(Sem Agrupamento)", "(S/A)");
        var abreviatura = string.IsNullOrWhiteSpace(agrupamento.Abreviatura)
            ? GerarAbreviaturaAutomatica(agrupamento.Nome)
            : agrupamento.Abreviatura;
        return (agrupamento.Nome, abreviatura);
    }

    /// <summary>Constrói o texto de legenda apresentado por baixo dos gráficos de barras por
    /// agrupamento, mapeando cada abreviatura usada no gráfico ao nome completo do agrupamento.
    /// Entradas em que a abreviatura é igual ao nome (ex.: "(Sem Agrupamento)") são omitidas,
    /// já que não precisam de tradução.</summary>
    private static string ConstruirLegendaAbreviaturas(IEnumerable<(string Agrupamento, string Abreviatura, int Total)> dados)
    {
        var pares = dados
            .Where(d => !string.Equals(d.Abreviatura, d.Agrupamento, StringComparison.OrdinalIgnoreCase))
            .Select(d => $"{d.Abreviatura} = {d.Agrupamento}");
        return string.Join("     ", pares);
    }

    public DashboardResumo Gerar(int ano)
    {
        var escolasAtivas = _db.Escolas.Where(e => e.Estado != EstadosEscola.Desativada).ToList();
        var jiIntegrados = escolasAtivas.Count(e => IsJardimInfancia(e.Tipo) && e.Integrado);
        var jiIsolados = escolasAtivas.Count(e => IsJardimInfancia(e.Tipo) && !e.Integrado);

        // Materializa os Tipos uma única vez e reaproveita-se tanto para o total de computadores
        // como para a distribuição por tipo de equipamento (gauges do Dashboard).
        var todosOsTipos = _db.Equipamentos.Select(e => e.Tipo).ToList();
        var totalComputadores = todosOsTipos.Count(IsComputador);
        var totalComputadoresSecretaria = todosOsTipos.Count(t => string.Equals(t, "Computador de Secretária", StringComparison.OrdinalIgnoreCase));
        var totalPortateis = todosOsTipos.Count(t => string.Equals(t, "Portátil", StringComparison.OrdinalIgnoreCase));
        var totalSwitches = todosOsTipos.Count(t => string.Equals(t, "Switch", StringComparison.OrdinalIgnoreCase));
        var totalAccessPoints = todosOsTipos.Count(t => string.Equals(t, "Access Point", StringComparison.OrdinalIgnoreCase));
        var totalImpressoras = todosOsTipos.Count(t => string.Equals(t, "Impressora", StringComparison.OrdinalIgnoreCase));

        var recolhidosPendentesTipos = _db.EquipamentosRecolhidos
            .Where(r => r.DataEntrega == null && r.Equipamento != null)
            .Select(r => r.Equipamento!.Tipo)
            .ToList();
        var computadoresRecolhidosPendentes = recolhidosPendentesTipos.Count(IsComputador);

        var pendentesDisia = _db.EquipamentosRecolhidos.Count(r => r.DataEntrega == null);
        var hoje = DateTime.Today;

        // (2.1) Computadores já reparados/prontos mas ainda por entregar à escola.
        var totalComputadoresAguardamEntrega = _db.Equipamentos
            .Where(e => e.Estado == EstadosEquipamento.AguardaEntrega)
            .Select(e => e.Tipo).ToList().Count(IsComputador);

        // A classificação de obsolescência é uma propriedade calculada em memória (não traduzível
        // para SQL), por isso é preciso materializar a lista antes de a usar no Count().
        var totalObsoleto = _db.Equipamentos.ToList().Count(e => e.Obsolescencia.Nivel == NivelObsolescencia.Obsoleto);

        // Intervenções "Canceladas" não foram efetivamente executadas, pelo que não devem
        // contar para nenhum dos totais/estatísticas de intervenções do dashboard.
        var intervencoesValidas = _db.Intervencoes.Where(i => i.Estado != EstadoIntervencao.Cancelada);

        // Usado para resolver Nome/Abreviatura a partir do AgrupamentoId nos gráficos por agrupamento.
        var agrupamentosPorId = _db.Agrupamentos.ToList().ToDictionary(a => a.Id);

        var resumo = new DashboardResumo
        {
            TotalAgrupamentos = _db.Agrupamentos.Count(),
            TotalEscolas = escolasAtivas.Count,
            TotalEdificios = escolasAtivas.Count - jiIntegrados,
            TotalJiIntegrados = jiIntegrados,
            TotalJiIsolados = jiIsolados,
            PedidosPendentes = _db.PedidosIntervencao.Count(p => p.Estado == EstadoPedido.Pendente),
            PedidosNaoConcluidos = _db.PedidosIntervencao.Count(p =>
                p.Estado == EstadoPedido.Pendente ||
                p.Estado == EstadoPedido.EmEspera ||
                p.Estado == EstadoPedido.EmAndamento),
            TotalIntervencoesGlobal = intervencoesValidas.Count(),
            TotalIntervencoesAnoCorrente = intervencoesValidas.Count(i => i.Ano == ano),
            TotalIntervencoesMesCorrente = intervencoesValidas.Count(i => i.Ano == hoje.Year && i.Mes == hoje.Month),
            TotalComputadores = totalComputadores,
            TotalComputadoresRecolhidos = computadoresRecolhidosPendentes,
            TotalEquipamentoGeral = todosOsTipos.Count,
            TotalComputadoresSecretaria = totalComputadoresSecretaria,
            TotalPortateis = totalPortateis,
            TotalSwitches = totalSwitches,
            TotalAccessPoints = totalAccessPoints,
            TotalImpressoras = totalImpressoras,
            TotalComputadoresAguardamEntrega = totalComputadoresAguardamEntrega,
            TotalEquipamentoObsoleto = totalObsoleto,
            PendentesEscola = _db.PedidosIntervencao.Count(p => p.Estado == EstadoPedido.Pendente),
            PendentesDisia = pendentesDisia,
            PendentesAtividadesDisia = _db.AtividadesDisia.Count(a =>
                            a.Estado == EstadoIntervencao.Pendente ||
                            a.Estado == EstadoIntervencao.EmProgresso ||
                            a.Estado == EstadoIntervencao.EmEspera),
            AtividadesDisiaNaoConcluidas = _db.AtividadesDisia.Count(a =>
                a.Estado == EstadoIntervencao.Pendente ||
                a.Estado == EstadoIntervencao.EmProgresso ||
                a.Estado == EstadoIntervencao.EmEspera)
        };

        // Agrupamento mais intervencionado de sempre
        var porAgrupamentoGlobal = intervencoesValidas
            .Include(i => i.Agrupamento)
            .GroupBy(i => i.Agrupamento == null ? "(Sem Agrupamento)" : i.Agrupamento.Nome)
            .Select(g => new { Agrupamento = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();
        var topAgrupamento = porAgrupamentoGlobal.FirstOrDefault();
        resumo.AgrupamentoMaisIntervencionado = topAgrupamento?.Agrupamento;
        resumo.AgrupamentoMaisIntervencionadoTotal = topAgrupamento?.Total ?? 0;

        // Escola mais intervencionada de sempre
        var porEscolaGlobal = intervencoesValidas
            .Include(i => i.Escola)
            .GroupBy(i => i.Escola!.Nome)
            .Select(g => new { Escola = g.Key, Total = g.Count() })
            .OrderByDescending(g => g.Total)
            .ToList();
        var topEscola = porEscolaGlobal.FirstOrDefault();
        resumo.EscolaMaisIntervencionada = topEscola?.Escola;
        resumo.EscolaMaisIntervencionadaTotal = topEscola?.Total ?? 0;

        // Intervenções por mês (ano corrente)
        var porMes = intervencoesValidas
            .Where(i => i.Ano == ano)
            .GroupBy(i => i.Mes)
            .Select(g => new { Mes = g.Key, Total = g.Count() })
            .ToDictionary(g => g.Mes, g => g.Total);

        for (var m = 1; m <= 12; m++)
            resumo.IntervencoesPorMesAnoCorrente.Add((NomesMeses[m - 1], porMes.GetValueOrDefault(m, 0)));

        // Intervenções por categoria (global)
        var categorias = _db.CategoriasIntervencao.ToList();
        foreach (var cat in categorias)
        {
            var total = _db.IntervencaoCategorias.Count(ic =>
                ic.CategoriaIntervencaoId == cat.Id && ic.Intervencao!.Estado != EstadoIntervencao.Cancelada);
            resumo.IntervencoesPorCategoria.Add((cat.Nome, total, cat.CorHex));
        }

        // (2.3) Intervenções por categoria — mês corrente
        foreach (var cat in categorias)
        {
            var total = _db.IntervencaoCategorias.Count(ic =>
                ic.CategoriaIntervencaoId == cat.Id &&
                ic.Intervencao!.Estado != EstadoIntervencao.Cancelada &&
                ic.Intervencao.Ano == hoje.Year && ic.Intervencao.Mes == hoje.Month);
            resumo.IntervencoesPorCategoriaMesCorrente.Add((cat.Nome, total, cat.CorHex));
        }

        // Intervenções por agrupamento - mês corrente
        var porAgrupamentoMes = intervencoesValidas
            .Where(i => i.Ano == hoje.Year && i.Mes == hoje.Month)
            .GroupBy(i => i.AgrupamentoId)
            .Select(g => new { AgrupamentoId = g.Key, Total = g.Count() })
            .ToList();
        resumo.IntervencoesPorAgrupamentoMesCorrente = porAgrupamentoMes
            .Select(x =>
            {
                var agrupamento = x.AgrupamentoId.HasValue && agrupamentosPorId.TryGetValue(x.AgrupamentoId.Value, out var a) ? a : null;
                var (nome, abreviatura) = NomeEAbreviatura(agrupamento);
                return (nome, abreviatura, x.Total);
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        // Intervenções por agrupamento - ano corrente
        var porAgrupamentoAno = intervencoesValidas
            .Where(i => i.Ano == ano)
            .GroupBy(i => i.AgrupamentoId)
            .Select(g => new { AgrupamentoId = g.Key, Total = g.Count() })
            .ToList();
        resumo.IntervencoesPorAgrupamentoAnoCorrente = porAgrupamentoAno
            .Select(x =>
            {
                var agrupamento = x.AgrupamentoId.HasValue && agrupamentosPorId.TryGetValue(x.AgrupamentoId.Value, out var a) ? a : null;
                var (nome, abreviatura) = NomeEAbreviatura(agrupamento);
                return (nome, abreviatura, x.Total);
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        // (2.5) Intervenções por categoria, por agrupamento — total anual e mês corrente.
        // Reaproveita a lista/ordem de agrupamentos já calculada acima para os gráficos simples,
        // para que o eixo dos gráficos de barras por categoria fique consistente com eles.
        void PreencherCategoriaPorAgrupamento(
            IReadOnlyList<(string Agrupamento, string Abreviatura, int Total)> agrupamentosOrdenados,
            IQueryable<IntervencaoCategoria> baseQuery,
            List<string> destinoAbreviaturas, out string legenda,
            List<(string Categoria, string Cor, List<int> Totais)> destinoSeries)
        {
            destinoAbreviaturas.AddRange(agrupamentosOrdenados.Select(a => a.Abreviatura));
            legenda = ConstruirLegendaAbreviaturas(agrupamentosOrdenados);

            var contagens = baseQuery
                .GroupBy(ic => new { ic.CategoriaIntervencaoId, ic.Intervencao!.AgrupamentoId })
                .Select(g => new { g.Key.CategoriaIntervencaoId, g.Key.AgrupamentoId, Total = g.Count() })
                .ToList();

            foreach (var cat in categorias)
            {
                var totais = new List<int>();
                foreach (var agr in agrupamentosOrdenados)
                {
                    // Reconstrói o Id do agrupamento a partir do nome (já resolvido acima) para
                    // procurar na tabela de contagens; "(Sem Agrupamento)" corresponde a AgrupamentoId nulo.
                    var idAgrupamento = agrupamentosPorId.Values
                        .FirstOrDefault(a => a.Nome == agr.Agrupamento)?.Id;
                    var total = contagens.FirstOrDefault(c =>
                        c.CategoriaIntervencaoId == cat.Id && c.AgrupamentoId == idAgrupamento)?.Total ?? 0;
                    totais.Add(total);
                }
                destinoSeries.Add((cat.Nome, cat.CorHex, totais));
            }
        }

        PreencherCategoriaPorAgrupamento(
            resumo.IntervencoesPorAgrupamentoAnoCorrente,
            _db.IntervencaoCategorias.Where(ic => ic.Intervencao!.Estado != EstadoIntervencao.Cancelada && ic.Intervencao.Ano == ano),
            resumo.AgrupamentosAbreviaturasAno, out var legendaAno, resumo.IntervencoesPorCategoriaEAgrupamentoAno);
        resumo.LegendaAgrupamentosAno = legendaAno;

        PreencherCategoriaPorAgrupamento(
            resumo.IntervencoesPorAgrupamentoMesCorrente,
            _db.IntervencaoCategorias.Where(ic => ic.Intervencao!.Estado != EstadoIntervencao.Cancelada &&
                ic.Intervencao.Ano == hoje.Year && ic.Intervencao.Mes == hoje.Month),
            resumo.AgrupamentosAbreviaturasMes, out var legendaMes, resumo.IntervencoesPorCategoriaEAgrupamentoMes);
        resumo.LegendaAgrupamentosMes = legendaMes;

        return resumo;
    }
}
