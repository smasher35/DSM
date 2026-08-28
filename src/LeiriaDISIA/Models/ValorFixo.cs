namespace LeiriaDISIA.Models;

/// <summary>
/// Nomes dos grupos de listas fixas geridos no módulo de Administração.
/// Cada grupo alimenta um combo específico algures na aplicação.
/// </summary>
public static class GruposValorFixo
{
    public const string TipoEquipamento = "TipoEquipamento";
    public const string Processador = "Processador";
    public const string TipoMemoria = "TipoMemoria";
    public const string TipoDisco = "TipoDisco";
    public const string SistemaOperativo = "SistemaOperativo";
    public const string TipoPainelMonitor = "TipoPainelMonitor";
    public const string TipoImpressora = "TipoImpressora";
    public const string LigacaoImpressora = "LigacaoImpressora";
    public const string TipoCamera = "TipoCamera";
    public const string StatusAbate = "StatusAbate";
    public const string TipoEscola = "TipoEscola";
    public const string EstadoEscola = "EstadoEscola";
    public const string VelocidadeFibra = "VelocidadeFibra";
    public const string EstadoRecolha = "EstadoRecolha";
    public const string EstadoEquipamento = "EstadoEquipamento";

    // 4: três novas listas de referência, consolidadas em Dados Fixos. As categorias de Atividades
    // DISIA continuam a ser geridas principalmente em Administração → "Categorias de Atividades
    // DISIA" (têm cor associada e uma chave estrangeira própria); os estados de Atividades DISIA e
    // de Pedidos de Intervenção continuam a ser controlados pelos enums EstadoIntervencao/
    // EstadoPedido (necessário para as cores e regras de fluxo). Estas três listas aqui servem
    // como referência consolidada e consultável em Dados Fixos, com os mesmos valores.
    public const string CategoriaAtividadeDisia = "CategoriaAtividadeDisia";
    public const string CategoriaIntervencao = "CategoriaIntervencao";
    public const string EstadoIntervencaoEAtividadeDisia = "EstadoIntervencaoEAtividadeDisia";
    public const string EstadoPedidoIntervencao = "EstadoPedidoIntervencao";

    // Auditoria: listas geridas aqui para que um administrador possa acrescentar novos tipos de
    // Ação/Resultado sem precisar de recompilar a aplicação - ver Models/RegistoAuditoria.cs e
    // Services/AuditoriaService.cs. Ao contrário da maioria das outras listas (que restringem o
    // que pode ser escolhido num combo), estas duas são principalmente para preencher os filtros
    // do ecrã Administração → Auditoria: o mecanismo de auditoria automática (ver
    // AppDbContext.SaveChanges) regista ações para QUALQUER tipo de registo criado/eliminado,
    // mesmo que a "Ação" correspondente ainda não esteja aqui listada - a lista serve como
    // referência/documentação consultável, e para dar nomes amigáveis às ações mais comuns.
    public const string AcaoAuditoria = "AcaoAuditoria";
    public const string ResultadoAuditoria = "ResultadoAuditoria";

    /// <summary>Todos os grupos, com um rótulo amigável para apresentação no ecrã de Administração.
    /// A lista em si não precisa de estar ordenada — a UI (Administração → Dados Fixos) ordena os
    /// rótulos alfabeticamente ao apresentá-los (ver 5).
    ///
    /// Os grupos <see cref="CategoriaAtividadeDisia"/>, <see cref="CategoriaIntervencao"/>,
    /// <see cref="EstadoIntervencaoEAtividadeDisia"/> e <see cref="EstadoPedidoIntervencao"/> são
    /// grupos "ligados": os seus valores não são guardados na tabela ValoresFixos, mas sim geridos
    /// diretamente nas tabelas/registos reais (CategoriasDisia, CategoriasIntervencao,
    /// EstadosCorPersonalizados) para que fiquem sempre em sincronia com as dropdowns dos
    /// formulários de inserção/edição. Ver AdministracaoWindow.xaml.cs.
    ///
    /// (Dados Fixos v2) <see cref="Processador"/>, <see cref="TipoMemoria"/>, <see cref="TipoDisco"/>,
    /// <see cref="SistemaOperativo"/>, <see cref="TipoPainelMonitor"/> e <see cref="TipoCamera"/>
    /// deixaram de aparecer aqui como listas genéricas: passaram a ser geridas em Administração →
    /// Dados Fixos → Tipos de Equipamento → (Computador/Monitor/Câmara) → Características
    /// Específicas (à semelhança de qualquer outra característica), onde também suportam a relação
    /// de subtipo (ex.: "Tipo de Memória" → "Memória (GB)"). As constantes continuam a existir só
    /// para a migração automática (uma única vez) dos valores já configurados por um administrador
    /// — ver DbInitializer.MigrarCaracteristicasFixasEmbutidas.
    ///
    /// (12) <see cref="TipoImpressora"/> sofreu a mesma migração, para o grupo "Impressora" —
    /// deixou de aparecer aqui por essa razão. A constante mantém-se só pelo mesmo motivo dos
    /// grupos acima (migração automática). <see cref="LigacaoImpressora"/> não foi afetada e
    /// continua a ser uma lista genérica de Dados Fixos normal.</summary>
    public static readonly (string Grupo, string Rotulo)[] Todos =
    {
        (TipoEquipamento, "Tipos de Equipamento"),
        (LigacaoImpressora, "Ligações de Impressora"),
        (StatusAbate, "Status de Abate"),
        (TipoEscola, "Tipos de Escola"),
        (EstadoEscola, "Estados de Escola"),
        (VelocidadeFibra, "Velocidades de Fibra"),
        (EstadoRecolha, "Estados de Equipamento Recolhido"),
        (EstadoEquipamento, "Estados de Equipamento Informático"),
        (CategoriaAtividadeDisia, "Categorias das Atividades DISIA"),
        (CategoriaIntervencao, "Categorias de Intervenção"),
        (EstadoIntervencaoEAtividadeDisia, "Estados das Intervenções / Atividades DISIA"),
        (EstadoPedidoIntervencao, "Estados dos Pedidos de Intervenção"),
        (AcaoAuditoria, "Ações de Auditoria"),
        (ResultadoAuditoria, "Resultados de Auditoria")
    };
}

/// <summary>
/// Valor de uma lista fixa/configurável (ex: um processador disponível para seleção,
/// um tipo de equipamento, um status de abate, etc.). Gerido no módulo de Administração,
/// em "Dados Fixos", para que estas listas de sugestão possam ser alteradas sem
/// necessidade de recompilar a aplicação.
/// </summary>
public class ValorFixo
{
    public int Id { get; set; }

    /// <summary>Grupo a que pertence (ver <see cref="GruposValorFixo"/>).</summary>
    public string Grupo { get; set; } = string.Empty;

    public string Valor { get; set; } = string.Empty;

    public int Ordem { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Apenas usado no grupo <see cref="GruposValorFixo.TipoEquipamento"/>: identifica a que
    /// grupo de características específicas (ver <see cref="LeiriaDISIA.Models.GruposCaracteristicasEquipamento"/>)
    /// este tipo de equipamento pertence (Computador, Monitor, Impressora, Rede, Câmara, Projetor ou
    /// Genérico). Guardado ligado ao registo (Id) em vez de ao nome, precisamente para que continue a
    /// funcionar corretamente mesmo que o nome do tipo seja mais tarde alterado aqui em Dados Fixos —
    /// ver EquipamentoEditWindow.AtualizarGruposVisiveis.</summary>
    public string? GrupoCaracteristicas { get; set; }
}
