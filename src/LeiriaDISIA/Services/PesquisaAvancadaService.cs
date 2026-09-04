using System.Globalization;
using LeiriaDISIA.Data;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>Tipo de dado de um campo pesquisável — determina que comparadores fazem sentido (ver
/// <see cref="PesquisaAvancadaService.ComparadoresPorTipo"/>) e como o valor introduzido pelo
/// utilizador é interpretado ao comparar (ver <see cref="PesquisaAvancadaService.Corresponde"/>).</summary>
public enum TipoDadoCampoPesquisa
{
    Texto,
    Numero,
    Data,
    Booleano
}

/// <summary>Um campo pesquisável, com o valor (já em texto) extraído de um registo concreto do
/// tipo <typeparamref name="T"/>. Usado tanto para os campos fixos de Equipamento/Atividade DISIA
/// como, no caso de Equipamento, para as características adicionais definidas pelo administrador
/// (EAV — ver <see cref="CaracteristicaEquipamento"/>), que entram na mesma lista, lado a lado com
/// os campos fixos, para o utilizador não ter de saber a diferença entre uns e outros.</summary>
public class CampoPesquisavel<T>
{
    public string Chave { get; init; } = "";
    public string Rotulo { get; init; } = "";
    public TipoDadoCampoPesquisa Tipo { get; init; }
    public Func<T, string?> ObterValor { get; init; } = _ => null;

    /// <summary>(Só Equipamento) A que grupo de características este campo pertence — ver
    /// <see cref="GruposCaracteristicasEquipamento"/> — <c>null</c> para campos transversais
    /// (Marca, Modelo, Estado, etc.), disponíveis para qualquer Tipo de Equipamento. Usado para
    /// filtrar as opções da combo "Subtipo/Característica" consoante o Tipo de Equipamento
    /// escolhido na pesquisa avançada — ver <see cref="Views.PesquisaAvancadaEquipamentoWindow"/>.</summary>
    public string? GrupoCaracteristicas { get; init; }

    /// <summary>(Só Equipamento, só características adicionais definidas pelo administrador) Quando
    /// preenchido, este campo só é relevante para este Tipo de Equipamento específico (Id de um
    /// <see cref="ValorFixo"/> do grupo <see cref="GruposValorFixo.TipoEquipamento"/>) — ver
    /// <see cref="CaracteristicaEquipamento.TipoEquipamentoId"/>. <c>null</c> = aplica-se a todo o
    /// <see cref="GrupoCaracteristicas"/>, não a um único tipo.</summary>
    public int? TipoEquipamentoId { get; init; }

    /// <summary>Valores sugeridos/conhecidos para este campo (ex.: "SSD", "HDD", "NVMe" para Tipo de
    /// Disco), quando existir uma lista fechada de valores configurada — os mesmos valores já
    /// usados nas combos equivalentes de Inserir/Editar Equipamento (ver
    /// <see cref="Views.EquipamentoEditWindow.ValoresCaracteristicaEmbutida"/> e
    /// <see cref="Views.EquipamentoEditWindow.ValoresAtivos"/>). Quando preenchido, a pesquisa
    /// avançada mostra estes valores diretamente numa combo ("sub-subtipo") em vez de obrigar a
    /// escrevê-los à mão — ver <see cref="Views.PesquisaAvancadaEquipamentoWindow"/>. <c>null</c> ou
    /// vazio = sem lista fechada, entrada de texto/número livre, como até aqui.</summary>
    public string[]? ValoresSugeridos { get; init; }

    /// <summary>Usado pelas ComboBox de campo (DisplayMemberPath não é necessário quando o próprio
    /// ToString já devolve o rótulo a mostrar).</summary>
    public override string ToString() => Rotulo;
}

/// <summary>Uma linha de filtro tal como construída na janela de pesquisa avançada: um campo, um
/// comparador (ver <see cref="PesquisaAvancadaService.ComparadoresPorTipo"/>, sempre um dos válidos
/// para o tipo do campo escolhido) e o valor a comparar, tal como escrito pelo utilizador.</summary>
public class FiltroPesquisa<T>
{
    public CampoPesquisavel<T>? Campo { get; set; }
    public string? Comparador { get; set; }
    public string? Valor { get; set; }

    /// <summary>Só uma linha completamente preenchida (campo + comparador + valor) entra na
    /// pesquisa — uma linha ainda a meio de preencher é simplesmente ignorada, em vez de bloquear a
    /// pesquisa ou ser tratada como erro.</summary>
    public bool EstaCompleto => Campo != null && !string.IsNullOrWhiteSpace(Comparador) && !string.IsNullOrWhiteSpace(Valor);
}

/// <summary>
/// Motor de pesquisa avançada partilhado pelos itens 2.1 (Equipamento) e 3.1 (Atividades DISIA) do
/// módulo Relatórios: permite construir uma lista de filtros — campo, comparador (com suporte a
/// "&lt;", "&gt;", "=", etc., não só igualdade) e valor — combinados com E lógico, sobre uma lista
/// de registos já carregada em memória.
///
/// A avaliação é feita em memória (LINQ-to-Objects), não traduzida para SQL: tal como já acontece
/// noutros filtros de pesquisa da aplicação (ver comentário em DisiaWindow.Recarregar sobre
/// "string.Contains(texto, StringComparison) ... could not be translated"), comparar dinamicamente
/// um campo escolhido em runtime com um comparador escolhido em runtime não é algo que o SQLite/EF
/// Core consiga traduzir para SQL de qualquer forma — e os volumes desta aplicação (equipamento e
/// atividades de um município) são pequenos o suficiente para isto ser perfeitamente adequado.
/// </summary>
public static class PesquisaAvancadaService
{
    /// <summary>Comparadores válidos para cada tipo de dado. Texto não ganha "&gt;"/"&lt;" (não faz
    /// sentido comparar ordinalmente texto livre no contexto desta pesquisa) mas ganha
    /// "contém"/"não contém", para pesquisas como "Modelo contém 'ThinkPad'".</summary>
    public static readonly IReadOnlyDictionary<TipoDadoCampoPesquisa, string[]> ComparadoresPorTipo = new Dictionary<TipoDadoCampoPesquisa, string[]>
    {
        [TipoDadoCampoPesquisa.Texto] = new[] { "=", "≠", "contém", "não contém" },
        [TipoDadoCampoPesquisa.Numero] = new[] { "=", "≠", ">", ">=", "<", "<=" },
        [TipoDadoCampoPesquisa.Data] = new[] { "=", "≠", ">", ">=", "<", "<=" },
        [TipoDadoCampoPesquisa.Booleano] = new[] { "=" },
    };

    /// <summary>Aplica todos os filtros completos (ver <see cref="FiltroPesquisa{T}.EstaCompleto"/>)
    /// a uma lista já carregada, combinando-os com E lógico. Filtros incompletos são ignorados. Sem
    /// nenhum filtro completo, devolve a lista completa (sem filtrar) — a janela chamadora é
    /// responsável por decidir se isso é ou não aceitável (ver "não deixar imprimir se não houver
    /// resultados", item 1.1 — aplica-se aqui de forma equivalente).</summary>
    public static List<T> Aplicar<T>(IEnumerable<T> registos, IEnumerable<FiltroPesquisa<T>> filtros)
    {
        var filtrosCompletos = filtros.Where(f => f.EstaCompleto).ToList();
        var resultado = registos;

        foreach (var filtro in filtrosCompletos)
        {
            var campo = filtro.Campo!;
            var comparador = filtro.Comparador!;
            var valor = filtro.Valor!;
            resultado = resultado.Where(r => Corresponde(campo.ObterValor(r), campo.Tipo, comparador, valor));
        }

        return resultado.ToList();
    }

    /// <summary>Descrição legível dos filtros completos aplicados (ex.: "Tipo de Disco = SSD E
    /// Memória (GB) &gt; 8"), para mostrar no topo do relatório gerado — para quem o lê mais tarde
    /// saber exatamente que critério originou a lista, sem ter de adivinhar.</summary>
    public static string Descrever<T>(IEnumerable<FiltroPesquisa<T>> filtros)
    {
        var partes = filtros.Where(f => f.EstaCompleto)
            .Select(f => $"{f.Campo!.Rotulo} {f.Comparador} \"{f.Valor}\"");
        var texto = string.Join("  E  ", partes);
        return string.IsNullOrWhiteSpace(texto) ? "(sem filtros — todos os registos)" : texto;
    }

    private static bool Corresponde(string? valorReal, TipoDadoCampoPesquisa tipo, string comparador, string valorPesquisa)
    {
        switch (tipo)
        {
            case TipoDadoCampoPesquisa.Texto:
            {
                var real = valorReal ?? "";
                return comparador switch
                {
                    "=" => real.Equals(valorPesquisa, StringComparison.OrdinalIgnoreCase),
                    "≠" => !real.Equals(valorPesquisa, StringComparison.OrdinalIgnoreCase),
                    "contém" => real.Contains(valorPesquisa, StringComparison.OrdinalIgnoreCase),
                    "não contém" => !real.Contains(valorPesquisa, StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
            }
            case TipoDadoCampoPesquisa.Numero:
            {
                if (!TentarConverterNumero(valorPesquisa, out var alvo)) return false;
                if (!TentarConverterNumero(valorReal, out var real)) return false;
                return comparador switch
                {
                    "=" => Math.Abs(real - alvo) < 0.0001,
                    "≠" => Math.Abs(real - alvo) >= 0.0001,
                    ">" => real > alvo,
                    ">=" => real >= alvo,
                    "<" => real < alvo,
                    "<=" => real <= alvo,
                    _ => false
                };
            }
            case TipoDadoCampoPesquisa.Data:
            {
                if (!TentarConverterData(valorPesquisa, out var alvo)) return false;
                if (!TentarConverterData(valorReal, out var real)) return false;
                return comparador switch
                {
                    "=" => real.Date == alvo.Date,
                    "≠" => real.Date != alvo.Date,
                    ">" => real.Date > alvo.Date,
                    ">=" => real.Date >= alvo.Date,
                    "<" => real.Date < alvo.Date,
                    "<=" => real.Date <= alvo.Date,
                    _ => false
                };
            }
            case TipoDadoCampoPesquisa.Booleano:
            {
                var alvoBool = valorPesquisa.Equals("Sim", StringComparison.OrdinalIgnoreCase)
                    || valorPesquisa.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || valorPesquisa.Equals("1", StringComparison.OrdinalIgnoreCase);
                if (!bool.TryParse(valorReal, out var real)) return false;
                return real == alvoBool;
            }
            default:
                return false;
        }
    }

    private static bool TentarConverterNumero(string? texto, out double valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        // Aceita tanto "8.5" como "8,5" (separador decimal português), tal como as restantes
        // caixas numéricas da aplicação.
        return double.TryParse(texto.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out valor);
    }

    private static bool TentarConverterData(string? texto, out DateTime valor)
    {
        valor = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        return DateTime.TryParse(texto.Trim(), CultureInfo.GetCultureInfo("pt-PT"), DateTimeStyles.None, out valor)
            || DateTime.TryParse(texto.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out valor);
    }

    /// <summary>Catálogo de campos pesquisáveis para Equipamento (item 2.1): os campos fixos mais
    /// relevantes (tipo, marca, modelo, todas as características específicas por família de
    /// equipamento — processador/memória/disco, monitor, impressora, rede, câmara, projetor) e,
    /// juntamente com eles, as características adicionais definidas pelo administrador em
    /// Administração → Dados Fixos → Tipos de Equipamento → "Gerir Características" (ver
    /// <see cref="CaracteristicaEquipamento"/>) — para o utilizador poder pesquisar por qualquer
    /// campo que exista, "fixo" ou personalizado, sem ter de saber a diferença entre um e outro.</summary>
    public static List<CampoPesquisavel<Equipamento>> ObterCamposEquipamento(AppDbContext db)
    {
        // Função local (não genérica) em vez do antigo método genérico Campo<T>: com T já fixo em
        // Equipamento aqui dentro, o compilador consegue inferir o tipo do parâmetro das lambdas
        // implícitas (ex.: "e => e.Tipo") sem ambiguidade. Um método Campo<T> genérico partilhado
        // por Equipamento e AtividadeDisia não permite essa inferência (CS0411) — T só aparece
        // dentro do tipo do parâmetro Func, e o C# não consegue inferir um parâmetro de tipo
        // genérico só a partir do corpo de uma lambda implicitamente tipada.
        CampoPesquisavel<Equipamento> Campo(string chave, string rotulo, TipoDadoCampoPesquisa tipo, Func<Equipamento, string?> obterValor,
            string? grupo = null, string[]? sugestoes = null) =>
            new() { Chave = chave, Rotulo = rotulo, Tipo = tipo, ObterValor = obterValor, GrupoCaracteristicas = grupo, ValoresSugeridos = sugestoes };

        // Valores sugeridos: mesma fonte e mesmos valores por omissão já usados nas combos
        // equivalentes de Inserir/Editar Equipamento (ver EquipamentoEditWindow, construtor, e
        // ValoresCaracteristicaEmbutida/ValoresAtivos) — para a pesquisa avançada oferecer
        // exatamente os mesmos valores reais configurados, em vez de listas inventadas à parte.
        const string grpComputador = GruposCaracteristicasEquipamento.Computador;
        const string grpRede = GruposCaracteristicasEquipamento.Rede;
        const string grpCamera = GruposCaracteristicasEquipamento.Camera;
        const string grpMonitor = GruposCaracteristicasEquipamento.Monitor;
        const string grpProjetor = GruposCaracteristicasEquipamento.Projetor;
        const string grpImpressora = GruposCaracteristicasEquipamento.Impressora;

        var sugProcessador = ValoresCaracteristicaEmbutida(db, grpComputador, "Processador");
        var sugTipoMemoria = ValoresCaracteristicaEmbutida(db, grpComputador, "Tipo de Memória", "DDR3", "DDR4", "DDR5");
        var sugTipoDisco = ValoresCaracteristicaEmbutida(db, grpComputador, "Tipo de Disco", "HDD", "SSD", "NVMe");
        var sugSistemaOperativo = ValoresCaracteristicaEmbutida(db, grpComputador, "Sistema Operativo");
        var sugNumeroPortas = ValoresCaracteristicaEmbutida(db, grpRede, "Nº de Portas", "4", "5", "8", "16", "24", "48");
        var sugVelocidadeRede = ValoresCaracteristicaEmbutida(db, grpRede, "Velocidade", "100 Mbps", "1 Gbps", "2.5 Gbps", "10 Gbps");
        var sugTipoCamera = ValoresCaracteristicaEmbutida(db, grpCamera, "Tipo", "IP", "Analógica");
        var sugResolucaoCamera = ValoresCaracteristicaEmbutida(db, grpCamera, "Resolução", "2MP", "4MP", "1080p", "4K");
        var sugTipoPainel = ValoresCaracteristicaEmbutida(db, grpMonitor, "Tipo de Painel", "LED", "LCD", "OLED");
        var sugPolegadas = ValoresCaracteristicaEmbutida(db, grpMonitor, "Polegadas", "19", "21", "24", "27", "32");
        var sugResolucaoMonitor = ValoresCaracteristicaEmbutida(db, grpMonitor, "Resolução", "1366x768", "1920x1080", "2560x1440", "3840x2160");
        var sugLuminosidade = ValoresCaracteristicaEmbutida(db, grpProjetor, "Luminosidade (Lumens)", "2000", "3000", "4000", "5000", "6000");
        var sugResolucaoProjetor = ValoresCaracteristicaEmbutida(db, grpProjetor, "Resolução", "1280x800", "1920x1080", "3840x2160");
        var sugTipoImpressora = ValoresCaracteristicaEmbutida(db, grpImpressora, "Tipo de Impressora", "Laser", "Tinta");
        var sugLigacaoImpressora = ValoresAtivos(db, GruposValorFixo.LigacaoImpressora, "USB", "Rede", "WiFi");
        var sugEstado = ValoresAtivos(db, GruposValorFixo.EstadoEquipamento,
            EstadosEquipamento.EmServico, EstadosEquipamento.Recolhido, EstadosEquipamento.EmReparacao,
            EstadosEquipamento.Reparado, EstadosEquipamento.AguardaEntrega, EstadosEquipamento.EmArmazem, EstadosEquipamento.Abatido);
        var sugObsolescencia = new[]
        {
            NivelObsolescencia.Atual.ToString(), NivelObsolescencia.AMonitorizar.ToString(), NivelObsolescencia.Obsoleto.ToString()
        };

        // Campos transversais (GrupoCaracteristicas = null): disponíveis para qualquer Tipo de
        // Equipamento, na combo "Subtipo/Característica" da pesquisa avançada (ver
        // Views/PesquisaAvancadaEquipamentoWindow.xaml.cs) — juntamente com os campos específicos do
        // tipo escolhido, mais abaixo.
        var campos = new List<CampoPesquisavel<Equipamento>>
        {
            Campo("Tipo", "Tipo de Equipamento", TipoDadoCampoPesquisa.Texto, e => e.Tipo),
            Campo("Marca", "Marca", TipoDadoCampoPesquisa.Texto, e => e.Marca),
            Campo("Modelo", "Modelo", TipoDadoCampoPesquisa.Texto, e => e.Modelo),
            Campo("NumeroSerie", "Nº de Série", TipoDadoCampoPesquisa.Texto, e => e.NumeroSerie),
            Campo("NumeroInventario", "Nº de Inventário (GEPE)", TipoDadoCampoPesquisa.Texto, e => e.NumeroInventario),
            Campo("Escola", "Escola / Local", TipoDadoCampoPesquisa.Texto, e => e.Escola?.Nome ?? e.LocalNaoEscolar),
            Campo("Estado", "Estado", TipoDadoCampoPesquisa.Texto, e => e.Estado, sugestoes: sugEstado),
            Campo("Fornecedor", "Fornecedor", TipoDadoCampoPesquisa.Texto, e => e.Fornecedor),
            Campo("DataAquisicao", "Data de Aquisição", TipoDadoCampoPesquisa.Data, e => e.DataAquisicao?.ToString("yyyy-MM-dd")),
            Campo("ValorAquisicao", "Valor de Aquisição (€)", TipoDadoCampoPesquisa.Numero, e => e.ValorAquisicao?.ToString(CultureInfo.InvariantCulture)),
            Campo("ObsolescenciaNivel", "Nível de Obsolescência", TipoDadoCampoPesquisa.Texto, e => e.Obsolescencia.Nivel.ToString(), sugestoes: sugObsolescencia),
        };

        // Campos específicos por grupo de características (ver GruposCaracteristicasEquipamento):
        // não entram diretamente na 1ª combo — só aparecem na combo "Subtipo/Característica" depois
        // de escolher "Tipo de Equipamento" e um tipo concreto desse grupo (ver
        // Views/PesquisaAvancadaEquipamentoWindow.xaml.cs) — os mesmos grupos/campos usados no
        // formulário de Inserir/Editar Equipamento (ver EquipamentoEditWindow.AtualizarGruposVisiveis).
        campos.AddRange(new[]
        {
            Campo("Processador", "Processador", TipoDadoCampoPesquisa.Texto, e => e.Processador, grpComputador, sugProcessador),
            Campo("FamiliaProcessador", "Família do Processador", TipoDadoCampoPesquisa.Texto, e => e.FamiliaProcessador, grpComputador),
            Campo("TipoMemoria", "Tipo de Memória", TipoDadoCampoPesquisa.Texto, e => e.TipoMemoria, grpComputador, sugTipoMemoria),
            Campo("QuantidadeMemoriaGB", "Memória (GB)", TipoDadoCampoPesquisa.Numero, e => e.QuantidadeMemoriaGB?.ToString(CultureInfo.InvariantCulture), grpComputador),
            Campo("TipoDisco", "Tipo de Disco", TipoDadoCampoPesquisa.Texto, e => e.TipoDisco, grpComputador, sugTipoDisco),
            Campo("TamanhoDiscoGB", "Tamanho do Disco (GB)", TipoDadoCampoPesquisa.Numero, e => e.TamanhoDiscoGB?.ToString(CultureInfo.InvariantCulture), grpComputador),
            Campo("SistemaOperativo", "Sistema Operativo", TipoDadoCampoPesquisa.Texto, e => e.SistemaOperativo, grpComputador, sugSistemaOperativo),

            Campo("PolegadasMonitor", "Polegadas (Monitor)", TipoDadoCampoPesquisa.Numero, e => e.PolegadasMonitor?.ToString(CultureInfo.InvariantCulture), grpMonitor, sugPolegadas),
            Campo("TipoPainelMonitor", "Tipo de Painel (Monitor)", TipoDadoCampoPesquisa.Texto, e => e.TipoPainelMonitor, grpMonitor, sugTipoPainel),
            Campo("ResolucaoMonitor", "Resolução (Monitor)", TipoDadoCampoPesquisa.Texto, e => e.ResolucaoMonitor, grpMonitor, sugResolucaoMonitor),

            Campo("TipoImpressora", "Tipo de Impressora", TipoDadoCampoPesquisa.Texto, e => e.TipoImpressora, grpImpressora, sugTipoImpressora),
            Campo("ImpressaoCor", "Impressão a Cor (Sim/Não)", TipoDadoCampoPesquisa.Booleano, e => e.ImpressaoCor?.ToString(), grpImpressora),
            Campo("LigacaoImpressora", "Ligação da Impressora", TipoDadoCampoPesquisa.Texto, e => e.LigacaoImpressora, grpImpressora, sugLigacaoImpressora),

            Campo("NumeroPortas", "Nº de Portas (Rede)", TipoDadoCampoPesquisa.Numero, e => e.NumeroPortas?.ToString(CultureInfo.InvariantCulture), grpRede, sugNumeroPortas),
            Campo("VelocidadeRede", "Velocidade (Rede)", TipoDadoCampoPesquisa.Texto, e => e.VelocidadeRede, grpRede, sugVelocidadeRede),
            Campo("Gerivel", "Gerível (Sim/Não)", TipoDadoCampoPesquisa.Booleano, e => e.Gerivel?.ToString(), grpRede),

            Campo("ResolucaoCamera", "Resolução (Câmara)", TipoDadoCampoPesquisa.Texto, e => e.ResolucaoCamera, grpCamera, sugResolucaoCamera),
            Campo("TipoCamera", "Tipo de Câmara", TipoDadoCampoPesquisa.Texto, e => e.TipoCamera, grpCamera, sugTipoCamera),
            Campo("VisaoNoturna", "Visão Noturna (Sim/Não)", TipoDadoCampoPesquisa.Booleano, e => e.VisaoNoturna?.ToString(), grpCamera),

            Campo("LuminosidadeLumens", "Luminosidade (Lumens)", TipoDadoCampoPesquisa.Numero, e => e.LuminosidadeLumens?.ToString(CultureInfo.InvariantCulture), grpProjetor, sugLuminosidade),
            Campo("ResolucaoProjetor", "Resolução (Projetor)", TipoDadoCampoPesquisa.Texto, e => e.ResolucaoProjetor, grpProjetor, sugResolucaoProjetor),

            Campo("EspecificacoesAdicionais", "Especificações Adicionais", TipoDadoCampoPesquisa.Texto, e => e.EspecificacoesAdicionais, GruposCaracteristicasEquipamento.Generico),
        });

        // Características adicionais definidas pelo administrador (EAV): pré-carregadas de uma só
        // vez para um dicionário em memória, para o campo dinâmico não repetir uma consulta à base
        // de dados por cada equipamento avaliado (evita N+1 consultas ao aplicar o filtro). Herdam
        // o GrupoCaracteristicas da própria característica e, quando específicas de um único Tipo de
        // Equipamento (ver CaracteristicaEquipamento.TipoEquipamentoId), ficam também marcadas com
        // esse Id, para só aparecerem como subtipo desse tipo em concreto. Se o administrador tiver
        // configurado valores sugeridos para esta característica (Administração → Dados Fixos →
        // Tipos de Equipamento → (grupo) → "Gerir Valores desta Característica"), esses valores
        // também aparecem como "sub-subtipo" na pesquisa avançada, tal como os campos fixos acima.
        var caracteristicas = db.CaracteristicasEquipamento.Where(c => c.Ativo).OrderBy(c => c.Nome).ToList();
        if (caracteristicas.Count > 0)
        {
            var valoresPorChave = db.EquipamentoCaracteristicaValores
                .ToLookup(v => (v.EquipamentoId, v.CaracteristicaEquipamentoId), v => v.Valor);
            var opcoesPorCaracteristica = db.CaracteristicaEquipamentoOpcoes
                .Where(o => o.Ativo)
                .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
                .ToLookup(o => o.CaracteristicaEquipamentoId, o => o.Valor);

            foreach (var carac in caracteristicas)
            {
                var caracId = carac.Id;
                var sugeridos = opcoesPorCaracteristica[caracId].ToArray();
                campos.Add(new CampoPesquisavel<Equipamento>
                {
                    Chave = $"carac:{caracId}",
                    Rotulo = $"{carac.Nome} (característica adicional)",
                    Tipo = TipoDadoCampoPesquisa.Texto,
                    ObterValor = e => valoresPorChave[(e.Id, caracId)].FirstOrDefault(),
                    GrupoCaracteristicas = carac.GrupoCaracteristicas,
                    TipoEquipamentoId = carac.TipoEquipamentoId,
                    ValoresSugeridos = sugeridos.Length > 0 ? sugeridos : null
                });
            }
        }

        return campos;
    }

    /// <summary>Réplica de <c>Views.EquipamentoEditWindow.ValoresCaracteristicaEmbutida</c> (privada
    /// nessa classe, por isso duplicada aqui): valores sugeridos configurados pelo administrador
    /// para uma característica embutida de um grupo (Administração → Dados Fixos → Tipos de
    /// Equipamento → (grupo)), com valores por omissão para quando ainda não há nenhum configurado —
    /// os mesmos valores por omissão já usados no formulário de Inserir/Editar Equipamento, para a
    /// pesquisa avançada oferecer sempre exatamente as mesmas opções.</summary>
    private static string[] ValoresCaracteristicaEmbutida(AppDbContext db, string grupoCaracteristicas, string nome, params string[] valoresPorOmissao)
    {
        var valores = db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupoCaracteristicas && c.Nome == nome)
            .Join(db.CaracteristicaEquipamentoOpcoes.Where(o => o.Ativo),
                c => c.Id, o => o.CaracteristicaEquipamentoId, (c, o) => o)
            .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
            .Select(o => o.Valor)
            .ToArray();

        return valores.Length > 0 ? valores : valoresPorOmissao;
    }

    /// <summary>Réplica de <c>Views.EquipamentoEditWindow.ValoresAtivos</c> (privada nessa classe):
    /// valores ativos de um grupo genérico de Dados Fixos (não uma característica embutida), com
    /// valores por omissão para quando ainda não há nenhum configurado.</summary>
    private static string[] ValoresAtivos(AppDbContext db, string grupo, params string[] valoresPorOmissao)
    {
        var valores = db.ValoresFixos
            .Where(v => v.Grupo == grupo && v.Ativo)
            .OrderBy(v => v.Valor)
            .Select(v => v.Valor)
            .ToArray();

        return valores.Length > 0 ? valores : valoresPorOmissao;
    }

    /// <summary>Item de "Tipo de Equipamento" para a combo dedicada da pesquisa avançada (ver
    /// <see cref="Views.PesquisaAvancadaEquipamentoWindow"/>), já com o grupo de características
    /// resolvido (para saber que campos mostrar na combo "Subtipo" a seguir).</summary>
    public readonly record struct TipoEquipamentoPesquisavel(int Id, string Nome, string GrupoCaracteristicas)
    {
        public override string ToString() => Nome;
    }

    /// <summary>Tipos de equipamento configurados em Administração → Dados Fixos → Tipos de
    /// Equipamento (grupo <see cref="GruposValorFixo.TipoEquipamento"/>), ordenados alfabeticamente
    /// — mesma ordem já usada nas restantes combos de Tipo da aplicação (ver
    /// EquipamentoEditWindow.ValoresAtivos). Só tipos ativos.</summary>
    public static List<TipoEquipamentoPesquisavel> ObterTiposEquipamento(AppDbContext db)
    {
        return db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento && v.Ativo)
            .OrderBy(v => v.Valor)
            .AsEnumerable()
            .Select(v => new TipoEquipamentoPesquisavel(v.Id, v.Valor, ResolverGrupoCaracteristicas(v.Valor, v.GrupoCaracteristicas)))
            .ToList();
    }

    /// <summary>Réplica exata da mesma lógica/listas de
    /// <c>Views.EquipamentoEditWindow.ObterGrupoCaracteristicas</c> (privada nessa classe, por isso
    /// duplicada aqui): usa o grupo gravado no próprio <see cref="ValorFixo"/> quando definido
    /// (Dados Fixos v2) e, para tipos antigos sem essa associação explícita, cai num
    /// reconhecimento pelo nome — para nunca ficar um tipo sem nenhum grupo (e por isso sem
    /// nenhum campo "Subtipo" disponível) só por ter sido criado antes dessa migração.</summary>
    private static string ResolverGrupoCaracteristicas(string tipo, string? grupoGravado)
    {
        if (!string.IsNullOrWhiteSpace(grupoGravado)) return grupoGravado;

        if (new[] { "Computador de Secretária", "Portátil", "Servidor" }.Contains(tipo)) return GruposCaracteristicasEquipamento.Computador;
        if (new[] { "Monitor" }.Contains(tipo)) return GruposCaracteristicasEquipamento.Monitor;
        if (new[] { "Impressora", "Multifunções" }.Contains(tipo)) return GruposCaracteristicasEquipamento.Impressora;
        if (new[] { "Switch", "Router", "Access Point" }.Contains(tipo)) return GruposCaracteristicasEquipamento.Rede;
        if (new[] { "Câmara CCTV" }.Contains(tipo)) return GruposCaracteristicasEquipamento.Camera;
        if (new[] { "Projetor", "Quadro Interativo" }.Contains(tipo)) return GruposCaracteristicasEquipamento.Projetor;
        return GruposCaracteristicasEquipamento.Generico;
    }

    /// <summary>Catálogo de campos pesquisáveis para Atividades DISIA (item 3.1) — os mesmos campos
    /// disponíveis no formulário de Atividade DISIA (ver AtividadeDisiaEditWindow), sem
    /// características adicionais (esta entidade não tem sistema EAV).</summary>
    public static List<CampoPesquisavel<AtividadeDisia>> ObterCamposAtividadeDisia()
    {
        // Mesma razão da função local em ObterCamposEquipamento (ver comentário lá) — T fixo em
        // AtividadeDisia aqui dentro, para o compilador conseguir inferir o tipo das lambdas.
        CampoPesquisavel<AtividadeDisia> Campo(string chave, string rotulo, TipoDadoCampoPesquisa tipo, Func<AtividadeDisia, string?> obterValor) =>
            new() { Chave = chave, Rotulo = rotulo, Tipo = tipo, ObterValor = obterValor };

        return new List<CampoPesquisavel<AtividadeDisia>>
        {
            Campo("Data", "Data", TipoDadoCampoPesquisa.Data, a => a.Data.ToString("yyyy-MM-dd")),
            Campo("Ano", "Ano", TipoDadoCampoPesquisa.Numero, a => a.Ano.ToString(CultureInfo.InvariantCulture)),
            Campo("Mes", "Mês", TipoDadoCampoPesquisa.Numero, a => a.Mes.ToString(CultureInfo.InvariantCulture)),
            Campo("Categoria", "Categoria", TipoDadoCampoPesquisa.Texto, a => a.Categoria?.Nome),
            Campo("Local", "Local", TipoDadoCampoPesquisa.Texto, a => a.Local),
            Campo("Divisao", "Divisão/Serviço", TipoDadoCampoPesquisa.Texto, a => a.Divisao),
            Campo("Suporte", "Tipo de Suporte", TipoDadoCampoPesquisa.Texto, a => a.Suporte),
            Campo("Descricao", "Descrição", TipoDadoCampoPesquisa.Texto, a => a.Descricao),
            Campo("Quantidade", "Quantidade", TipoDadoCampoPesquisa.Numero, a => a.Quantidade.ToString(CultureInfo.InvariantCulture)),
            Campo("Estado", "Estado", TipoDadoCampoPesquisa.Texto, a => a.Estado.ToString()),
            Campo("Observacoes", "Observações", TipoDadoCampoPesquisa.Texto, a => a.Observacoes),
        };
    }

}
