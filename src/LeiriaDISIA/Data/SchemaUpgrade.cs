using LeiriaDISIA.Models;
using Microsoft.Data.Sqlite;

namespace LeiriaDISIA.Data;

/// <summary>
/// A aplicação não usa EF Core Migrations (por simplicidade), pelo que os "ajustes de esquema"
/// entre versões são aplicados aqui, de forma segura e idempotente, com SQL em bruto.
/// NUNCA apaga a base de dados existente (ver histórico: essa era precisamente a causa do
/// bug crítico em que os utilizadores criados desapareciam a cada arranque da aplicação).
/// </summary>
public static class SchemaUpgrade
{
    public static void Aplicar(string caminhoDb)
    {
        using var conexao = new SqliteConnection($"Data Source={caminhoDb}");
        conexao.Open();

        // Se a base de dados ainda não tiver nenhuma tabela, não há nada para atualizar
        // (o EnsureCreated do EF Core, chamado antes desta função, já cria tudo de raiz).
        if (!TabelaExiste(conexao, "Escolas")) return;

        // Garante que a tabela ValoresFixos existe (pode ter sido adicionada numa versão posterior)
        CriarValoresFixosSePreciso(conexao);
        CriarEstadosCorPersonalizadaSePreciso(conexao);

        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Morada", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Contacto1", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Contacto2", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Contacto3", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Email1", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Email2", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Site", "TEXT");

        // Abreviatura do agrupamento, usada como rótulo compacto nos gráficos de barras do Dashboard
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Abreviatura", "TEXT");

        // Nº do pedido no sistema SIGA (Suporte), para referência cruzada opcional
        AdicionarColunaSeNaoExistir(conexao, "PedidosIntervencao", "NumeroSuporteSiga", "TEXT");

        AdicionarColunaSeNaoExistir(conexao, "Escolas", "Telefone", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "Email", "TEXT");

        // A Escola passou a poder não ter Agrupamento associado (era NOT NULL antes).
        // O SQLite não permite "ALTER COLUMN" para remover NOT NULL, pelo que, quando
        // necessário, a tabela é reconstruída preservando todos os dados existentes.
        TornarAgrupamentoIdOpcional(conexao, "Escolas");
        TornarAgrupamentoIdOpcional(conexao, "PedidosIntervencao");
        TornarAgrupamentoIdOpcional(conexao, "Intervencoes");

        // Adiciona suporte para avatares de utilizadores
        AdicionarColunaSeNaoExistir(conexao, "Usuarios", "CaminhoAvatar", "TEXT");

        // Jardins de infância integrados
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "Integrado", "INTEGER NOT NULL DEFAULT 0");

        // Velocidade de fibra contratada pela escola
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "VelocidadeFibra", "TEXT");

        // Ligação de um abate a uma intervenção de origem (quando registado a partir da janela de Intervenções)
        AdicionarColunaSeNaoExistir(conexao, "EquipamentosAbatidos", "IntervencaoId", "INTEGER");

        // Módulo de Equipamento Recolhido
        CriarEquipamentosRecolhidosSePreciso(conexao);

        // Junção Intervenção <-> Equipamento intervencionado no local
        CriarIntervencaoEquipamentosSePreciso(conexao);

        // Módulo de Comunicações
        CriarComunicacoesSePreciso(conexao);

        CriarRelatorioMensalDadosSePreciso(conexao);

        // Família / versão do processador (ex: "12ª Geração", "Ryzen 5 5600G"), a par do campo Processador
        AdicionarColunaSeNaoExistir(conexao, "Equipamentos", "FamiliaProcessador", "TEXT");

        // Ligação da recolha à "Intervenção DISIA" criada automaticamente para acompanhar a reparação
        AdicionarColunaSeNaoExistir(conexao, "EquipamentosRecolhidos", "IntervencaoDisiaId", "INTEGER");

        // Fluxo de reparação passou a usar uma Atividade DISIA (em vez de uma nova Intervenção)
        // para agregar e acompanhar o equipamento recolhido.
        AdicionarColunaSeNaoExistir(conexao, "EquipamentosRecolhidos", "AtividadeDisiaId", "INTEGER");

        // Novos estados de equipamento introduzidos pelo fluxo Recolhido → Em Reparação →
        // Aguarda Entrega → Em Serviço. Adiciona-os a Dados Fixos em bases de dados já existentes
        // que ainda não os tenham (instalações novas já os recebem através do DbInitializer).
        GarantirValorFixo(conexao, GruposValorFixo.EstadoEquipamento, EstadosEquipamento.Recolhido, 1);
        GarantirValorFixo(conexao, GruposValorFixo.EstadoEquipamento, EstadosEquipamento.AguardaEntrega, 4);

        // O estado do equipamento deixou de ser um enum fixo (EmServico/EmReparacao/EmArmazem/Abatido)
        // e passou a texto configurável em Dados Fixos, com um novo estado intermédio "Reparado".
        // Atualiza os valores antigos gravados pelo enum para o texto de exibição correspondente.
        AtualizarTextoSeExistir(conexao, "Equipamentos", "Estado", "EmServico", "Em Serviço");
        AtualizarTextoSeExistir(conexao, "Equipamentos", "Estado", "EmReparacao", "Em Reparação");
        AtualizarTextoSeExistir(conexao, "Equipamentos", "Estado", "EmArmazem", "Em Armazém");

        // O estado "Reparado" do equipamento recolhido passou a ter outro significado (agora é
        // um estado do próprio Equipamento); no registo de recolha o equivalente passou a
        // "Aguarda Entrega". Atualiza registos antigos e a respetiva lista em Dados Fixos.
        AtualizarTextoSeExistir(conexao, "EquipamentosRecolhidos", "Estado", "Reparado", "Aguarda Entrega");
        using (var cmd = conexao.CreateCommand())
        {
            cmd.CommandText = @"UPDATE ""ValoresFixos"" SET ""Valor"" = 'Aguarda Entrega'
                                 WHERE ""Grupo"" = 'EstadoRecolha' AND ""Valor"" = 'Reparado'";
            cmd.ExecuteNonQuery();
        }

        // Item 8: o Código da Escola deixou de poder ser editado pelo utilizador e de ser, na
        // prática, uma cópia do Código GEPE — passou a ser um código único atribuído pela própria
        // aplicação (ex.: "EB0001", "JI0001"). Substitui, uma única vez, os códigos antigos
        // (puramente numéricos) por códigos no novo formato.
        MigrarCodigoEscolaParaFormatoNovo(conexao);

        // Item 10: a escola deixou de ter apenas um booleano "Ativa" e passou a ter um estado em
        // texto (Ativa / Desativada / Em Obras / outros que o administrador venha a criar em
        // Dados Fixos). Acrescenta a nova coluna e migra o valor da antiga coluna booleana.
        MigrarEstadoEscola(conexao);

        // A escola passou a poder indicar se tem biblioteca. Registos existentes ficam a "false"
        // (valor seguro, não assume dados que a aplicação nunca chegou a recolher) e continuam a
        // abrir normalmente — o campo é sempre opcional/editável depois.
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "TemBiblioteca", "INTEGER NOT NULL DEFAULT 0");

        // Diretor do Agrupamento: campo textual e opcional, registos existentes ficam a NULL.
        AdicionarColunaSeNaoExistir(conexao, "Agrupamentos", "Diretor", "TEXT");

        // O campo único "Número de Série / Inventário" do Equipamento Abatido passa a dois
        // campos independentes. A coluna antiga não é apagada (SQLite obriga a reconstruir a
        // tabela para remover colunas, o que é mais arriscado do que necessário); fica apenas
        // sem uso a partir desta versão. Ver MigrarNumeroSerieInventarioAbatido para a estratégia
        // de separação dos dados existentes.
        MigrarNumeroSerieInventarioAbatido(conexao);

        // Entidade Externa do Contacto: campo textual e opcional, registos existentes ficam a NULL.
        AdicionarColunaSeNaoExistir(conexao, "Contactos", "EntidadeExterna", "TEXT");

        // Correção do bug em que renomear um "Tipo de Equipamento" em Dados Fixos fazia
        // desaparecer as características específicas associadas (o grupo de características era
        // determinado por comparação direta do NOME, que deixava de coincidir depois de renomeado).
        // Cada valor de Tipo de Equipamento passa a guardar o seu grupo de características ligado
        // ao próprio registo (Id), sobrevivendo a qualquer futura alteração do nome apresentado.
        AdicionarColunaSeNaoExistir(conexao, "ValoresFixos", "GrupoCaracteristicas", "TEXT");
        AtribuirGrupoCaracteristicasPorOmissao(conexao);

        // Item 12: características específicas de equipamento passam a poder ser geridas pelo
        // administrador (nome + valor por omissão opcional) em vez de estarem só fixas no código.
        CriarCaracteristicasEquipamentoSePreciso(conexao);
        CriarEquipamentoCaracteristicaValoresSePreciso(conexao);

        // Item 13: cada característica específica passa a poder ter, opcionalmente, uma lista de
        // valores sugeridos (à semelhança das restantes listas de Dados Fixos), que aparece como
        // caixa de seleção editável em Inserir/Editar Equipamento em vez de texto livre.
        CriarCaracteristicaEquipamentoOpcoesSePreciso(conexao);

        // Dados Fixos v2: (a) uma característica pode passar a aplicar-se só a um Tipo de
        // Equipamento específico (em vez de sempre a todo o GrupoCaracteristicas) — ex.: "Bateria"
        // só em "Portátil"; (b) uma característica pode ser "filha" de outra, criando uma segunda
        // caixa de seleção dependente — ex.: "Tipo de Memória" → "Memória (GB)". Todas as colunas
        // são opcionais (NULL = comportamento anterior, inalterado), pelo que esta alteração nunca
        // afeta características já existentes.
        AdicionarColunaSeNaoExistir(conexao, "CaracteristicasEquipamento", "TipoEquipamentoId", "INTEGER");
        AdicionarColunaSeNaoExistir(conexao, "CaracteristicasEquipamento", "CaracteristicaPaiId", "INTEGER");
        AdicionarColunaSeNaoExistir(conexao, "CaracteristicaEquipamentoOpcoes", "CaracteristicaFilhaId", "INTEGER");

        // Item 11: limpa da tabela ValoresFixos os registos órfãos dos grupos "ligados"
        // (CategoriaAtividadeDisia, EstadoIntervencaoEAtividadeDisia, EstadoPedidoIntervencao —
        // cujos valores reais vivem noutras tabelas) e de quaisquer nomes de grupo antigos e já
        // descontinuados (ex.: "EstadoAtividadeDisia"), que nunca são lidos por nenhum ecrã mas
        // continuavam visíveis na grelha de Dados Fixos sem qualquer efeito nos formulários.
        LimparValoresFixosOrfaos(conexao);

        // ---- Planeamento de Rotas ----
        // Código postal + distância à sede: registos existentes ficam a NULL (nenhuma escola tinha
        // isto calculado antes) — nunca impede a app de abrir, e nunca calcula nada sozinho aqui: o
        // cálculo só acontece quando o utilizador o pedir explicitamente (ver Views/EscolaEditWindow
        // "Recalcular Distância"). Latitude/Longitude já existiam antes desta funcionalidade (usadas
        // pelo mapa da escola) — são reutilizadas tal e qual, sem precisar de mais nenhuma coluna.
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "CodigoPostal", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "DistanciaKmSede", "REAL");
        AdicionarColunaSeNaoExistir(conexao, "Escolas", "DataUltimoCalculoDistancia", "TEXT");

        // Campos novos do Pedido para o Planeamento de Rota — todos opcionais/com omissão segura,
        // não alteram o comportamento de nenhum ecrã existente de Pedidos.
        AdicionarColunaSeNaoExistir(conexao, "PedidosIntervencao", "DuracaoEstimadaMinutos", "INTEGER");
        AdicionarColunaSeNaoExistir(conexao, "PedidosIntervencao", "Prioridade", "TEXT NOT NULL DEFAULT 'Normal'");
        AdicionarColunaSeNaoExistir(conexao, "PedidosIntervencao", "ObrigatorioNaRota", "INTEGER NOT NULL DEFAULT 0");

        // "Repor Password" (Administração → Utilizadores): marca a conta como tendo uma password
        // temporária, obrigando à sua alteração no próximo login (ver Views/LoginWindow.xaml.cs e
        // Views/AlterarPasswordObrigatorioWindow.xaml.cs). Por omissão false para todas as contas
        // já existentes - não obriga ninguém a mudar a password ao atualizar a aplicação.
        AdicionarColunaSeNaoExistir(conexao, "Usuarios", "PrecisaAlterarPassword", "INTEGER NOT NULL DEFAULT 0");

        // Auditoria: tentativas de login falhadas consecutivas, para o bloqueio automático de
        // conta (ver Administração → Segurança) - reutiliza o campo Ativo já existente em vez de
        // um novo conceito de "bloqueado": a conta fica marcada Inativa, e só um administrador a
        // reativa em Administração → Utilizadores, exatamente como já acontecia para qualquer
        // outro motivo de desativação.
        AdicionarColunaSeNaoExistir(conexao, "Usuarios", "TentativasFalhadasConsecutivas", "INTEGER NOT NULL DEFAULT 0");

        // Relatório Mensal — Plataforma SIGA: além da tipificação, estado de tickets e passwords já
        // existentes, faltava contemplar a criação de utilizadores (ver Views/RelatoriosWindow.xaml,
        // bloco "Plataforma SIGA — dados do mês").
        AdicionarColunaSeNaoExistir(conexao, "RelatoriosMensaisDados", "TotalUtilizadoresCriados", "INTEGER NOT NULL DEFAULT 0");

        CriarRegistosAuditoriaSePreciso(conexao);

        CriarPlanoRotaSePreciso(conexao);
    }

    /// <summary>Ver <see cref="Models.RegistoAuditoria"/>. Numa base de dados nova, o EnsureCreated
    /// do EF Core já cria esta tabela sozinho; isto só é preciso para bases de dados já em uso
    /// antes desta versão.</summary>
    private static void CriarRegistosAuditoriaSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "RegistosAuditoria")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""RegistosAuditoria"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""DataHora"" TEXT NOT NULL,
                ""Utilizador"" TEXT NOT NULL DEFAULT 'sistema',
                ""Acao"" TEXT NOT NULL,
                ""Detalhe"" TEXT NULL,
                ""Resultado"" TEXT NOT NULL DEFAULT 'Sucesso'
            )";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Planeamento de Rotas: plano diário + paragens ordenadas — ver
    /// <see cref="LeiriaDISIA.Models.PlanoRota"/> e <see cref="LeiriaDISIA.Models.PlanoRotaParagem"/>.
    /// Numa base de dados nova, o EnsureCreated do EF Core já cria estas tabelas sozinho (por isso o
    /// TabelaExiste abaixo); isto só é preciso para bases de dados já em uso antes desta versão.</summary>
    private static void CriarPlanoRotaSePreciso(SqliteConnection conexao)
    {
        if (!TabelaExiste(conexao, "PlanosRota"))
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE ""PlanosRota"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    ""Data"" TEXT NOT NULL,
                    ""CriadoPorUsuarioId"" INTEGER NULL,
                    ""DataCriacao"" TEXT NOT NULL,
                    ""PontoPartida"" TEXT NOT NULL,
                    ""PontoRegresso"" TEXT NOT NULL,
                    ""HoraPartida"" TEXT NOT NULL,
                    ""LimiteHorasEquipa"" TEXT NULL,
                    ""DistanciaTotalKm"" REAL NOT NULL DEFAULT 0,
                    ""DuracaoTotalMinutos"" INTEGER NOT NULL DEFAULT 0,
                    ""Estado"" TEXT NOT NULL DEFAULT 'Planeado',
                    ""CaminhoPdf"" TEXT NULL
                )";
            cmd.ExecuteNonQuery();
        }

        if (!TabelaExiste(conexao, "PlanoRotaParagens"))
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE ""PlanoRotaParagens"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                    ""PlanoRotaId"" INTEGER NOT NULL,
                    ""PedidoIntervencaoId"" INTEGER NOT NULL,
                    ""EscolaId"" INTEGER NOT NULL,
                    ""Ordem"" INTEGER NOT NULL,
                    ""DistanciaDesdeAnteriorKm"" REAL NOT NULL DEFAULT 0,
                    ""DuracaoDesdeAnteriorMinutos"" INTEGER NOT NULL DEFAULT 0
                )";
            cmd.ExecuteNonQuery();

            using var cmdIndice = conexao.CreateCommand();
            cmdIndice.CommandText = @"
                CREATE UNIQUE INDEX ""IX_PlanoRotaParagens_PlanoRotaId_PedidoIntervencaoId""
                ON ""PlanoRotaParagens"" (""PlanoRotaId"", ""PedidoIntervencaoId"")";
            cmdIndice.ExecuteNonQuery();
        }
    }

    private static readonly string[] GruposLigados =
        { "CategoriaAtividadeDisia", "EstadoIntervencaoEAtividadeDisia", "EstadoPedidoIntervencao" };

    private static readonly string[] GruposValidos =
    {
        "TipoEquipamento", "Processador", "TipoMemoria", "TipoDisco", "SistemaOperativo",
        "TipoPainelMonitor", "TipoImpressora", "LigacaoImpressora", "TipoCamera", "StatusAbate",
        "TipoEscola", "EstadoEscola", "VelocidadeFibra", "EstadoRecolha", "EstadoEquipamento",
        "CategoriaAtividadeDisia", "CategoriaIntervencao", "EstadoIntervencaoEAtividadeDisia",
        "EstadoPedidoIntervencao"
    };

    private static void LimparValoresFixosOrfaos(SqliteConnection conexao)
    {
        if (!TabelaExiste(conexao, "ValoresFixos")) return;

        using var cmd = conexao.CreateCommand();
        var placeholdersLigados = string.Join(",", GruposLigados.Select((_, i) => $"$l{i}"));
        var placeholdersValidos = string.Join(",", GruposValidos.Select((_, i) => $"$v{i}"));
        cmd.CommandText = $@"DELETE FROM ""ValoresFixos""
                              WHERE ""Grupo"" IN ({placeholdersLigados})
                                 OR ""Grupo"" NOT IN ({placeholdersValidos})";
        for (var i = 0; i < GruposLigados.Length; i++)
            cmd.Parameters.AddWithValue($"$l{i}", GruposLigados[i]);
        for (var i = 0; i < GruposValidos.Length; i++)
            cmd.Parameters.AddWithValue($"$v{i}", GruposValidos[i]);
        cmd.ExecuteNonQuery();
    }

    private static void CriarCaracteristicasEquipamentoSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "CaracteristicasEquipamento")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""CaracteristicasEquipamento"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""GrupoCaracteristicas"" TEXT NOT NULL,
                ""Nome"" TEXT NOT NULL,
                ""ValorPorOmissao"" TEXT NULL,
                ""Ordem"" INTEGER NOT NULL DEFAULT 0,
                ""Ativo"" INTEGER NOT NULL DEFAULT 1
            )";
        cmd.ExecuteNonQuery();
    }

    private static void CriarEquipamentoCaracteristicaValoresSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "EquipamentoCaracteristicaValores")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""EquipamentoCaracteristicaValores"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""EquipamentoId"" INTEGER NOT NULL,
                ""CaracteristicaEquipamentoId"" INTEGER NOT NULL,
                ""Valor"" TEXT NULL
            )";
        cmd.ExecuteNonQuery();

        using var cmdIndice = conexao.CreateCommand();
        cmdIndice.CommandText = @"
            CREATE UNIQUE INDEX ""IX_EquipamentoCaracteristicaValores_EquipamentoId_CaracteristicaEquipamentoId""
            ON ""EquipamentoCaracteristicaValores"" (""EquipamentoId"", ""CaracteristicaEquipamentoId"")";
        cmdIndice.ExecuteNonQuery();
    }

    /// <summary>(1.4) Lista de valores sugeridos (opcionais) de cada característica específica —
    /// ver <see cref="LeiriaDISIA.Models.CaracteristicaEquipamentoOpcao"/>.</summary>
    private static void CriarCaracteristicaEquipamentoOpcoesSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "CaracteristicaEquipamentoOpcoes")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""CaracteristicaEquipamentoOpcoes"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""CaracteristicaEquipamentoId"" INTEGER NOT NULL,
                ""Valor"" TEXT NOT NULL,
                ""Ordem"" INTEGER NOT NULL DEFAULT 0,
                ""Ativo"" INTEGER NOT NULL DEFAULT 1
            )";
        cmd.ExecuteNonQuery();

        using var cmdIndice = conexao.CreateCommand();
        cmdIndice.CommandText = @"
            CREATE INDEX ""IX_CaracteristicaEquipamentoOpcoes_CaracteristicaEquipamentoId""
            ON ""CaracteristicaEquipamentoOpcoes"" (""CaracteristicaEquipamentoId"")";
        cmdIndice.ExecuteNonQuery();
    }

    /// <summary>Preenche, uma única vez, o grupo de características dos valores de "Tipo de
    /// Equipamento" já existentes (criados antes desta correção), com base no nome atual, para que
    /// continuem a funcionar exatamente como antes em bases de dados já em uso. Só atribui a
    /// registos que ainda não tenham grupo definido — nunca sobrepõe uma escolha já feita pelo
    /// administrador em Dados Fixos.</summary>
    private static void AtribuirGrupoCaracteristicasPorOmissao(SqliteConnection conexao)
    {
        if (!TabelaExiste(conexao, "ValoresFixos")) return;
        if (!ColunaExiste(conexao, "ValoresFixos", "GrupoCaracteristicas")) return;

        void Atribuir(string grupo, params string[] nomes)
        {
            foreach (var nome in nomes)
            {
                using var cmd = conexao.CreateCommand();
                cmd.CommandText = @"UPDATE ""ValoresFixos"" SET ""GrupoCaracteristicas"" = $grupo
                                     WHERE ""Grupo"" = 'TipoEquipamento' AND ""Valor"" = $valor
                                     AND (""GrupoCaracteristicas"" IS NULL OR ""GrupoCaracteristicas"" = '')";
                cmd.Parameters.AddWithValue("$grupo", grupo);
                cmd.Parameters.AddWithValue("$valor", nome);
                cmd.ExecuteNonQuery();
            }
        }

        Atribuir(GruposCaracteristicasEquipamento.Computador, "Computador de Secretária", "Portátil", "Servidor");
        Atribuir(GruposCaracteristicasEquipamento.Monitor, "Monitor");
        Atribuir(GruposCaracteristicasEquipamento.Impressora, "Impressora", "Multifunções");
        Atribuir(GruposCaracteristicasEquipamento.Rede, "Switch", "Router", "Access Point");
        Atribuir(GruposCaracteristicasEquipamento.Camera, "Câmara CCTV");
        Atribuir(GruposCaracteristicasEquipamento.Projetor, "Projetor", "Quadro Interativo");
    }

    /// <summary>Acrescenta a coluna "Estado" (texto) à tabela Escolas e migra, uma única vez, o
    /// valor da antiga coluna booleana "Ativa" (1 → "Ativa", 0 → "Desativada"), preservando o
    /// estado atual de cada escola. A coluna antiga "Ativa" fica na base de dados, mas deixa de
    /// ser lida/escrita pelo código a partir desta versão (à semelhança dos restantes ajustes de
    /// esquema desta aplicação, colunas antigas não são removidas). Bases de dados totalmente
    /// novas nunca chegam a ter a coluna "Ativa": nascem já com "Estado" através do EnsureCreated.</summary>
    private static void MigrarEstadoEscola(SqliteConnection conexao)
    {
        if (!TabelaExiste(conexao, "Escolas")) return;
        if (ColunaExiste(conexao, "Escolas", "Estado")) return;

        using (var addCol = conexao.CreateCommand())
        {
            addCol.CommandText = "ALTER TABLE \"Escolas\" ADD COLUMN \"Estado\" TEXT NOT NULL DEFAULT 'Ativa'";
            addCol.ExecuteNonQuery();
        }

        if (ColunaExiste(conexao, "Escolas", "Ativa"))
        {
            using var migrar = conexao.CreateCommand();
            migrar.CommandText = @"UPDATE ""Escolas""
                                    SET ""Estado"" = CASE WHEN ""Ativa"" = 1 THEN 'Ativa' ELSE 'Desativada' END";
            migrar.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Separa o antigo campo único "NumeroSerieInventario" de EquipamentosAbatidos em dois campos
    /// independentes: "NumeroSerie" e "NumeroInventario".
    ///
    /// Estratégia de migração (segura, sem assumir formatos que não podem ser confirmados):
    /// - Quando o utilizador escolhia um equipamento já cadastrado, a aplicação preenchia
    ///   automaticamente o campo antigo exatamente como "{NumeroSerie} / {NumeroInventario}"
    ///   (ver antigo EquipamentoAbatidoEditWindow/EquipamentoAbatidoView). Só nesses casos,
    ///   reconhecíveis por conterem exatamente um separador " / ", separamos automaticamente.
    /// - Em todos os outros casos (registo manual/avulso, texto livre, sem separador ou com mais
    ///   do que um) não é seguro adivinhar o formato: o valor completo é preservado tal como
    ///   estava, movido para "NumeroSerie", e "NumeroInventario" fica NULL. Nenhum dado é perdido;
    ///   o utilizador pode reorganizar manualmente os poucos casos ambíguos, se existirem.
    /// </summary>
    private static void MigrarNumeroSerieInventarioAbatido(SqliteConnection conexao)
    {
        if (!TabelaExiste(conexao, "EquipamentosAbatidos")) return;
        if (!ColunaExiste(conexao, "EquipamentosAbatidos", "NumeroSerieInventario")) return;

        AdicionarColunaSeNaoExistir(conexao, "EquipamentosAbatidos", "NumeroSerie", "TEXT");
        AdicionarColunaSeNaoExistir(conexao, "EquipamentosAbatidos", "NumeroInventario", "TEXT");

        // Já migrado nesta base de dados (evita repetir/sobrepor edições manuais posteriores)?
        using (var jaMigrado = conexao.CreateCommand())
        {
            jaMigrado.CommandText = @"SELECT COUNT(*) FROM ""EquipamentosAbatidos""
                                       WHERE ""NumeroSerieInventario"" IS NOT NULL
                                         AND TRIM(""NumeroSerieInventario"") <> ''
                                         AND (""NumeroSerie"" IS NOT NULL OR ""NumeroInventario"" IS NOT NULL)";
            if (Convert.ToInt64(jaMigrado.ExecuteScalar()) > 0) return;
        }

        // Caso reconhecível: exatamente um " / " no valor -> separa nos dois novos campos.
        using (var separar = conexao.CreateCommand())
        {
            separar.CommandText = @"UPDATE ""EquipamentosAbatidos""
                SET ""NumeroSerie"" = TRIM(SUBSTR(""NumeroSerieInventario"", 1, INSTR(""NumeroSerieInventario"", ' / ') - 1)),
                    ""NumeroInventario"" = TRIM(SUBSTR(""NumeroSerieInventario"", INSTR(""NumeroSerieInventario"", ' / ') + 3))
                WHERE ""NumeroSerieInventario"" IS NOT NULL
                  AND TRIM(""NumeroSerieInventario"") <> ''
                  AND INSTR(""NumeroSerieInventario"", ' / ') > 0
                  AND INSTR(SUBSTR(""NumeroSerieInventario"", INSTR(""NumeroSerieInventario"", ' / ') + 3), ' / ') = 0";
            separar.ExecuteNonQuery();
        }

        // Restantes casos (sem separador reconhecível, ou ambíguos): preserva tudo em NumeroSerie.
        using (var preservar = conexao.CreateCommand())
        {
            preservar.CommandText = @"UPDATE ""EquipamentosAbatidos""
                SET ""NumeroSerie"" = TRIM(""NumeroSerieInventario"")
                WHERE ""NumeroSerieInventario"" IS NOT NULL
                  AND TRIM(""NumeroSerieInventario"") <> ''
                  AND ""NumeroSerie"" IS NULL
                  AND ""NumeroInventario"" IS NULL";
            preservar.ExecuteNonQuery();
        }
    }

    /// <summary>Substitui os códigos de escola no formato antigo (puramente numéricos, herdados do
    /// Código GEPE, ou vazios) pelo novo formato automático "PREFIXO0000". É idempotente: uma
    /// escola cujo código já esteja no formato novo (letras seguidas de dígitos) nunca é tocada,
    /// pelo que corre em segurança em todos os arranques sem gerar códigos novos repetidamente.</summary>
    private static void MigrarCodigoEscolaParaFormatoNovo(SqliteConnection conexao)
    {
        if (!TabelaExiste(conexao, "Escolas")) return;

        var linhas = new List<(int Id, string? CodAtual, string? Tipo)>();
        using (var ler = conexao.CreateCommand())
        {
            ler.CommandText = "SELECT \"Id\", \"CodEscola\", \"Tipo\" FROM \"Escolas\" ORDER BY \"Id\"";
            using var reader = ler.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var codAtual = reader.IsDBNull(1) ? null : reader.GetValue(1)?.ToString();
                var tipo = reader.IsDBNull(2) ? null : reader.GetValue(2)?.ToString();
                linhas.Add((id, codAtual, tipo));
            }
        }

        var contadores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Primeira passagem: regista os contadores mais altos já em uso pelos códigos que já
        // estiverem no formato novo, para nunca atribuir um número repetido a seguir.
        foreach (var (_, codAtual, _) in linhas)
        {
            if (!PareceFormatoNovo(codAtual)) continue;
            var prefixoLetras = new string(codAtual!.TakeWhile(char.IsLetter).ToArray());
            var numero = int.Parse(codAtual[prefixoLetras.Length..]);
            if (!contadores.TryGetValue(prefixoLetras, out var atual) || numero > atual)
                contadores[prefixoLetras] = numero;
        }

        foreach (var (id, codAtual, tipo) in linhas)
        {
            if (PareceFormatoNovo(codAtual)) continue;

            var prefixo = PrefixoCodigoEscola(tipo);
            contadores.TryGetValue(prefixo, out var atualPrefixo);
            atualPrefixo++;
            contadores[prefixo] = atualPrefixo;
            var novoCodigo = $"{prefixo}{atualPrefixo:D4}";

            using var atualizar = conexao.CreateCommand();
            atualizar.CommandText = "UPDATE \"Escolas\" SET \"CodEscola\" = $cod WHERE \"Id\" = $id";
            atualizar.Parameters.AddWithValue("$cod", novoCodigo);
            atualizar.Parameters.AddWithValue("$id", id);
            atualizar.ExecuteNonQuery();
        }
    }

    private static bool PareceFormatoNovo(string? codigo) =>
        !string.IsNullOrEmpty(codigo) && codigo.Length > 1 && char.IsLetter(codigo[0]) &&
        System.Text.RegularExpressions.Regex.IsMatch(codigo, "^[A-Za-z]+[0-9]+$");

    private static string PrefixoCodigoEscola(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return "EB";
        if (tipo.Contains("Jardim", StringComparison.OrdinalIgnoreCase)) return "JI";
        if (tipo.Contains("Secund", StringComparison.OrdinalIgnoreCase)) return "SEC";
        if (tipo.Contains("Centro Escolar", StringComparison.OrdinalIgnoreCase)) return "CE";
        return "EB";
    }

    /// <summary>Garante que um determinado valor existe na tabela ValoresFixos para o grupo
    /// indicado, inserindo-o (ativo) se ainda não existir. Não faz nada se já existir uma linha
    /// com o mesmo Grupo+Valor (case-insensitive), para nunca duplicar em atualizações repetidas.</summary>
    private static void GarantirValorFixo(SqliteConnection conexao, string grupo, string valor, int ordem)
    {
        if (!TabelaExiste(conexao, "ValoresFixos")) return;

        using (var verificar = conexao.CreateCommand())
        {
            verificar.CommandText = @"SELECT COUNT(*) FROM ""ValoresFixos""
                                       WHERE ""Grupo"" = $grupo AND LOWER(""Valor"") = LOWER($valor)";
            verificar.Parameters.AddWithValue("$grupo", grupo);
            verificar.Parameters.AddWithValue("$valor", valor);
            var existe = Convert.ToInt64(verificar.ExecuteScalar()) > 0;
            if (existe) return;
        }

        using var inserir = conexao.CreateCommand();
        inserir.CommandText = @"INSERT INTO ""ValoresFixos"" (""Grupo"", ""Valor"", ""Ordem"", ""Ativo"")
                                 VALUES ($grupo, $valor, $ordem, 1)";
        inserir.Parameters.AddWithValue("$grupo", grupo);
        inserir.Parameters.AddWithValue("$valor", valor);
        inserir.Parameters.AddWithValue("$ordem", ordem);
        inserir.ExecuteNonQuery();
    }

    /// <summary>Atualiza todas as ocorrências de um valor de texto antigo para o novo, numa coluna
    /// de uma tabela — usado para migrar dados gravados com nomes antigos (ex.: nomes de enum)
    /// para o texto de exibição atual. Não faz nada se a tabela/coluna ainda não existir.</summary>
    private static void AtualizarTextoSeExistir(SqliteConnection conexao, string tabela, string coluna, string valorAntigo, string valorNovo)
    {
        if (!TabelaExiste(conexao, tabela)) return;
        if (!ColunaExiste(conexao, tabela, coluna)) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = $"UPDATE \"{tabela}\" SET \"{coluna}\" = $novo WHERE \"{coluna}\" = $antigo";
        cmd.Parameters.AddWithValue("$novo", valorNovo);
        cmd.Parameters.AddWithValue("$antigo", valorAntigo);
        cmd.ExecuteNonQuery();
    }

    private static void CriarEquipamentosRecolhidosSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "EquipamentosRecolhidos")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""EquipamentosRecolhidos"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""EquipamentoId"" INTEGER NOT NULL,
                ""IntervencaoId"" INTEGER NULL,
                ""DataRecolha"" TEXT NOT NULL,
                ""Estado"" TEXT NOT NULL DEFAULT 'Pendente',
                ""DataEntrega"" TEXT NULL,
                ""Observacoes"" TEXT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private static void CriarIntervencaoEquipamentosSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "IntervencaoEquipamentos")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""IntervencaoEquipamentos"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""IntervencaoId"" INTEGER NOT NULL,
                ""EquipamentoId"" INTEGER NOT NULL,
                ""Observacoes"" TEXT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private static void CriarComunicacoesSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "Comunicacoes")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""Comunicacoes"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""EscolaId"" INTEGER NOT NULL,
                ""TipoLigacao"" TEXT NOT NULL DEFAULT 'Fibra',
                ""VelocidadeFibra"" TEXT NULL,
                ""Operadora"" TEXT NULL,
                ""NumeroContrato"" TEXT NULL,
                ""DataInstalacao"" TEXT NULL,
                ""Integrado"" INTEGER NOT NULL DEFAULT 0,
                ""Estado"" TEXT NOT NULL DEFAULT 'Ativa',
                ""Observacoes"" TEXT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private static void CriarRelatorioMensalDadosSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "RelatoriosMensaisDados")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""RelatoriosMensaisDados"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""Ano"" INTEGER NOT NULL,
                ""Mes"" INTEGER NOT NULL,
                ""TotalAlteracaoTipificacao"" INTEGER NOT NULL DEFAULT 0,
                ""TotalEstadoTickets"" INTEGER NOT NULL DEFAULT 0,
                ""TotalAlteracaoPasswords"" INTEGER NOT NULL DEFAULT 0,
                ""TotalUtilizadoresCriados"" INTEGER NOT NULL DEFAULT 0,
                ""ImagemPedidosSiga"" BLOB NULL,
                ""ImagemWorkflowSiga"" BLOB NULL,
                ""TextoBalancoGeral"" TEXT NULL,
                ""TextoDesafios"" TEXT NULL,
                ""TextoPropostas"" TEXT NULL,
                ""TextoNotaFinal"" TEXT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private static bool TabelaExiste(SqliteConnection conexao, string tabela)
    {
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$nome";
        cmd.Parameters.AddWithValue("$nome", tabela);
        return cmd.ExecuteScalar() != null;
    }

    private static void CriarValoresFixosSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "ValoresFixos")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""ValoresFixos"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""Grupo"" TEXT NOT NULL,
                ""Valor"" TEXT NOT NULL,
                ""Ordem"" INTEGER NOT NULL DEFAULT 0,
                ""Ativo"" INTEGER NOT NULL DEFAULT 1
            )";
        cmd.ExecuteNonQuery();
    }

    private static void CriarEstadosCorPersonalizadaSePreciso(SqliteConnection conexao)
    {
        if (TabelaExiste(conexao, "EstadosCorPersonalizados")) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""EstadosCorPersonalizados"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                ""Grupo"" TEXT NOT NULL,
                ""NomeEstado"" TEXT NOT NULL,
                ""NomeExibicao"" TEXT NOT NULL,
                ""Cor"" TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private static bool ColunaExiste(SqliteConnection conexao, string tabela, string coluna)
    {
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tabela}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var nomeColuna = reader.GetString(reader.GetOrdinal("name"));
            if (string.Equals(nomeColuna, coluna, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool ColunaENotNull(SqliteConnection conexao, string tabela, string coluna)
    {
        using var cmd = conexao.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tabela}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var nomeColuna = reader.GetString(reader.GetOrdinal("name"));
            if (string.Equals(nomeColuna, coluna, StringComparison.OrdinalIgnoreCase))
                return reader.GetInt32(reader.GetOrdinal("notnull")) == 1;
        }
        return false;
    }

    private static void AdicionarColunaSeNaoExistir(SqliteConnection conexao, string tabela, string coluna, string tipoSql)
    {
        if (!TabelaExiste(conexao, tabela)) return;
        if (ColunaExiste(conexao, tabela, coluna)) return;

        using var cmd = conexao.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{tabela}\" ADD COLUMN \"{coluna}\" {tipoSql} NULL";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Se a coluna AgrupamentoId ainda existir como NOT NULL na tabela indicada, reconstrói
    /// a tabela (procedimento padrão do SQLite para alterar restrições de coluna),
    /// preservando todos os registos e relações existentes.
    /// </summary>
    private static void TornarAgrupamentoIdOpcional(SqliteConnection conexao, string tabela)
    {
        if (!TabelaExiste(conexao, tabela)) return;
        if (!ColunaExiste(conexao, tabela, "AgrupamentoId")) return;
        if (!ColunaENotNull(conexao, tabela, "AgrupamentoId")) return; // já é opcional, nada a fazer

        // Lê a definição de colunas atual para reconstruir a tabela com o mesmo esquema,
        // apenas sem o NOT NULL em AgrupamentoId.
        var colunas = new List<(string Nome, string Tipo, bool NotNull, bool PrimaryKey)>();
        using (var cmd = conexao.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tabela}\")";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                colunas.Add((
                    reader.GetString(reader.GetOrdinal("name")),
                    reader.GetString(reader.GetOrdinal("type")),
                    reader.GetInt32(reader.GetOrdinal("notnull")) == 1,
                    reader.GetInt32(reader.GetOrdinal("pk")) > 0));
            }
        }

        using var transacao = conexao.BeginTransaction();

        var nomeTemp = tabela + "_upgrade_tmp";
        var definicoes = colunas.Select(c =>
        {
            var notNull = c.NotNull && c.Nome != "AgrupamentoId" && !c.PrimaryKey ? " NOT NULL" : "";
            var pk = c.PrimaryKey ? " PRIMARY KEY AUTOINCREMENT" : "";
            return $"\"{c.Nome}\" {c.Tipo}{pk}{notNull}";
        });

        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = transacao;
            cmd.CommandText = $"CREATE TABLE \"{nomeTemp}\" ({string.Join(", ", definicoes)})";
            cmd.ExecuteNonQuery();
        }

        var nomesColunas = string.Join(", ", colunas.Select(c => $"\"{c.Nome}\""));
        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = transacao;
            cmd.CommandText = $"INSERT INTO \"{nomeTemp}\" ({nomesColunas}) SELECT {nomesColunas} FROM \"{tabela}\"";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = transacao;
            cmd.CommandText = $"DROP TABLE \"{tabela}\"";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conexao.CreateCommand())
        {
            cmd.Transaction = transacao;
            cmd.CommandText = $"ALTER TABLE \"{nomeTemp}\" RENAME TO \"{tabela}\"";
            cmd.ExecuteNonQuery();
        }

        transacao.Commit();
    }
}
