using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Data;

public static class DbInitializer
{
    public static void Inicializar(AppDbContext db)
    {
        // CRÍTICO: nunca apagar a base de dados aqui. Anteriormente existia uma chamada a
        // db.Database.EnsureDeleted() antes do EnsureCreated(), o que apagava TODOS os dados
        // (incluindo utilizadores) sempre que a aplicação arrancava. EnsureCreated() apenas
        // cria o esquema se a base de dados ainda não existir; SchemaUpgrade aplica, de forma
        // segura e sem perda de dados, os pequenos ajustes de esquema entre versões.
        db.Database.EnsureCreated();
        SchemaUpgrade.Aplicar(AppDbContext.DbPath);

        if (!db.CategoriasIntervencao.Any())
        {
            db.CategoriasIntervencao.AddRange(
                new CategoriaIntervencao { Nome = "Redes", CorHex = "#8B5CF6" },
                new CategoriaIntervencao { Nome = "Hardware", CorHex = "#EF4444" },
                new CategoriaIntervencao { Nome = "Software", CorHex = "#22C55E" },
                new CategoriaIntervencao { Nome = "VPN", CorHex = "#3B82F6" },
                new CategoriaIntervencao { Nome = "Audio-Visual", CorHex = "#F59E0B" }
            );
        }

        if (!db.CategoriasDisia.Any())
        {
            db.CategoriasDisia.AddRange(
                new CategoriaDisia { Nome = "Videovigilância (CCTV)", CorHex = "#EF4444" },
                new CategoriaDisia { Nome = "Redes e Comunicações", CorHex = "#3B82F6" },
                new CategoriaDisia { Nome = "Equipamento Informático", CorHex = "#22C55E" },
                new CategoriaDisia { Nome = "Manutenção de Instalações", CorHex = "#F59E0B" },
                new CategoriaDisia { Nome = "Apoio a Utilizadores", CorHex = "#8B5CF6" },
                new CategoriaDisia { Nome = "Outros", CorHex = "#9CA3AF" }
            );
        }

        if (!db.Usuarios.Any())
        {
            var (hash, salt) = PasswordHasher.CriarHash("admin123");
            db.Usuarios.Add(new Usuario
            {
                NomeUtilizador = "admin",
                NomeCompleto = "Administrador",
                Perfil = PerfilUtilizador.Administrador,
                PasswordHash = hash,
                PasswordSalt = salt,
                Ativo = true
            });
        }

        if (!db.ValoresFixos.Any())
        {
            void Seed(string grupo, params string[] valores)
            {
                for (var i = 0; i < valores.Length; i++)
                    db.ValoresFixos.Add(new ValorFixo { Grupo = grupo, Valor = valores[i], Ordem = i, Ativo = true });
            }

            // (Correção de bug) Os Tipos de Equipamento por omissão já nascem ligados ao respetivo
            // grupo de características (ver GrupoCaracteristicas), em vez de dependerem de
            // SchemaUpgrade.AtribuirGrupoCaracteristicasPorOmissao para os "apanhar" mais tarde.
            // Essa correção corre ANTES deste seed (numa base de dados totalmente nova), pelo que a
            // sua atualização SQL não encontrava ainda nenhuma linha para atualizar — os Tipos de
            // Rede/Câmara/Projetor ficavam sem grupo associado logo na primeira utilização da
            // aplicação, até um segundo arranque (quando SchemaUpgrade já os encontrava e corrigia).
            void SeedTipoEquipamento(string? grupoCaracteristicas, params string[] valores)
            {
                foreach (var valor in valores)
                    db.ValoresFixos.Add(new ValorFixo
                    {
                        Grupo = GruposValorFixo.TipoEquipamento,
                        Valor = valor,
                        Ordem = 0,
                        Ativo = true,
                        GrupoCaracteristicas = grupoCaracteristicas
                    });
            }

            SeedTipoEquipamento(GruposCaracteristicasEquipamento.Computador, "Computador de Secretária", "Portátil", "Servidor");
            SeedTipoEquipamento(GruposCaracteristicasEquipamento.Monitor, "Monitor");
            SeedTipoEquipamento(GruposCaracteristicasEquipamento.Impressora, "Impressora", "Multifunções");
            SeedTipoEquipamento(GruposCaracteristicasEquipamento.Rede, "Switch", "Router", "Access Point");
            SeedTipoEquipamento(GruposCaracteristicasEquipamento.Camera, "Câmara CCTV");
            SeedTipoEquipamento(GruposCaracteristicasEquipamento.Projetor, "Projetor", "Quadro Interativo");
            SeedTipoEquipamento(null, "Tablet", "UPS/No-break", "Telefone IP", "Outro");

            // Ordem sequencial correta para o conjunto todo (a chamada acima grava tudo com Ordem=0).
            var tiposEquipamentoOrdenados = db.ValoresFixos.Local
                .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento).ToList();
            for (var i = 0; i < tiposEquipamentoOrdenados.Count; i++)
                tiposEquipamentoOrdenados[i].Ordem = i;
            Seed(GruposValorFixo.Processador,
                "Intel Core i3", "Intel Core i5", "Intel Core i7", "Intel Core i9",
                "Intel Celeron", "Intel Pentium", "AMD Ryzen 3", "AMD Ryzen 5", "AMD Ryzen 7");
            Seed(GruposValorFixo.TipoMemoria, "DDR3", "DDR4", "DDR5");
            Seed(GruposValorFixo.TipoDisco, "HDD", "SSD", "NVMe");
            Seed(GruposValorFixo.SistemaOperativo,
                "Windows 10", "Windows 10 Pro", "Windows 11", "Windows 11 Pro",
                "Windows Server", "Linux Ubuntu", "Outro");
            Seed(GruposValorFixo.TipoPainelMonitor, "LED", "LCD", "OLED");
            Seed(GruposValorFixo.TipoImpressora, "Laser", "Tinta");
            Seed(GruposValorFixo.LigacaoImpressora, "USB", "Rede", "WiFi");
            Seed(GruposValorFixo.TipoCamera, "IP", "Analógica");
            Seed(GruposValorFixo.StatusAbate, "Abatido", "Em processo de abate", "Doado", "Reciclado");
            Seed(GruposValorFixo.TipoEscola,
                "Jardim de Infância", "EB1", "Centro Escolar", "EB2/3", "Secundária", "Outro");
            Seed(GruposValorFixo.EstadoEscola,
                EstadosEscola.Ativa, EstadosEscola.Desativada, EstadosEscola.EmObras);
            Seed(GruposValorFixo.VelocidadeFibra,
                "16 Mbps", "32 Mbps", "100 Mbps", "200 Mbps", "500 Mbps", "1 Gbps");
            Seed(GruposValorFixo.EstadoRecolha,
                EstadosRecolha.Pendente, EstadosRecolha.EmReparacao, EstadosRecolha.AguardaEntrega, EstadosRecolha.Entregue);

            // NOTA (11): "CategoriaAtividadeDisia", "EstadoIntervencaoEAtividadeDisia" e
            // "EstadoPedidoIntervencao" NÃO são semeados aqui na tabela ValoresFixos — são grupos
            // "ligados" (ver GruposValorFixo), cujos valores reais vivem em CategoriasDisia e
            // EstadosCorPersonalizados (semeados mais abaixo). Semeá-los também em ValoresFixos só
            // criava registos órfãos, nunca lidos por nenhum ecrã, que confundiam o ecrã de Dados
            // Fixos (apareciam na lista mas não tinham qualquer efeito nos formulários). Ver
            // LimparValoresFixosOrfaos, chamado no fim deste método, que remove esses registos
            // também em bases de dados já existentes que os tenham herdado de versões anteriores.
        }

        // Seed aditivo (fora do bloco "!db.ValoresFixos.Any()") para que grupos criados depois da
        // instalação inicial também passem a existir em bases de dados já existentes.
        void SeedAditivo(string grupo, params string[] valores)
        {
            if (db.ValoresFixos.Any(v => v.Grupo == grupo)) return;
            for (var i = 0; i < valores.Length; i++)
                db.ValoresFixos.Add(new ValorFixo { Grupo = grupo, Valor = valores[i], Ordem = i, Ativo = true });
        }

        // 10: a escola passou a ter um terceiro estado possível ("Em Obras"), além de
        // Ativa/Desativada. Semeado à parte (fora do bloco "!ValoresFixos.Any()") para que também
        // fique disponível em bases de dados já existentes, criadas antes deste grupo existir.
        SeedAditivo(GruposValorFixo.EstadoEscola,
            EstadosEscola.Ativa, EstadosEscola.Desativada, EstadosEscola.EmObras);

        // Seed independente (fora do bloco "!db.ValoresFixos.Any()") para que este grupo também
        // seja criado em bases de dados já existentes, que foram criadas antes deste grupo existir.
        if (!db.ValoresFixos.Any(v => v.Grupo == GruposValorFixo.EstadoEquipamento))
        {
            var valoresEstadoEquipamento = new[]
            {
                EstadosEquipamento.EmServico, EstadosEquipamento.Recolhido, EstadosEquipamento.EmReparacao,
                EstadosEquipamento.Reparado, EstadosEquipamento.AguardaEntrega,
                EstadosEquipamento.EmArmazem, EstadosEquipamento.Abatido
            };
            for (var i = 0; i < valoresEstadoEquipamento.Length; i++)
            {
                db.ValoresFixos.Add(new ValorFixo
                {
                    Grupo = GruposValorFixo.EstadoEquipamento,
                    Valor = valoresEstadoEquipamento[i],
                    Ordem = i,
                    Ativo = true
                });
            }
        }

        // Seeds aditivos (fora do bloco "!Any()" de cada tabela) para que bases de dados já
        // existentes, criadas antes destes valores existirem, também passem a tê-los.
        if (!db.CategoriasDisia.Any(c => c.Nome == "Formatação e Instalação de Software"))
            db.CategoriasDisia.Add(new CategoriaDisia { Nome = "Formatação e Instalação de Software", CorHex = "#0EA5E9" });

        if (!db.CategoriasDisia.Any(c => c.Nome == "Substituição de Hardware"))
            db.CategoriasDisia.Add(new CategoriaDisia { Nome = "Substituição de Hardware", CorHex = "#EC4899" });

        if (!db.ValoresFixos.Any(v => v.Grupo == GruposValorFixo.StatusAbate && v.Valor == "Cancelado"))
        {
            var proximaOrdem = db.ValoresFixos.Where(v => v.Grupo == GruposValorFixo.StatusAbate)
                .Select(v => (int?)v.Ordem).Max() ?? -1;
            db.ValoresFixos.Add(new ValorFixo
            {
                Grupo = GruposValorFixo.StatusAbate,
                Valor = "Cancelado",
                Ordem = proximaOrdem + 1,
                Ativo = true
            });
        }

        if (!db.EstadosCorPersonalizados.Any())
        {
            void SeedEstado(string grupo, string nomeEstado, string nomeExibicao, string cor) =>
                db.EstadosCorPersonalizados.Add(new EstadoCorPersonalizada
                {
                    Grupo = grupo,
                    NomeEstado = nomeEstado,
                    NomeExibicao = nomeExibicao,
                    Cor = cor
                });

            SeedEstado(GruposEstadoCor.Intervencao, nameof(EstadoIntervencao.Fechada), "Fechada", "#22C55E");
            SeedEstado(GruposEstadoCor.Intervencao, nameof(EstadoIntervencao.Pendente), "Pendente", "#EF4444");
            SeedEstado(GruposEstadoCor.Intervencao, nameof(EstadoIntervencao.EmProgresso), "Em Progresso", "#F59E0B");
            SeedEstado(GruposEstadoCor.Intervencao, nameof(EstadoIntervencao.EmEspera), "Em Espera", "#6366F1");
            SeedEstado(GruposEstadoCor.Intervencao, nameof(EstadoIntervencao.Cancelada), "Cancelada", "#9CA3AF");

            SeedEstado(GruposEstadoCor.Pedido, nameof(EstadoPedido.Pendente), "Pendente", "#EF4444");
            SeedEstado(GruposEstadoCor.Pedido, nameof(EstadoPedido.EmAndamento), "Em Andamento", "#F59E0B");
            SeedEstado(GruposEstadoCor.Pedido, nameof(EstadoPedido.EmEspera), "Em Espera", "#6366F1");
            SeedEstado(GruposEstadoCor.Pedido, nameof(EstadoPedido.Concluido), "Concluído", "#22C55E");
            SeedEstado(GruposEstadoCor.Pedido, nameof(EstadoPedido.Cancelado), "Cancelado", "#9CA3AF");
        }

        // Cores dos estados de equipamento — adicionado depois da criação inicial da tabela
        // EstadosCorPersonalizados, por isso é semeado à parte (registo a registo) para que também
        // fique disponível em bases de dados já existentes, não só em instalações novas.
        void SeedEstadoEquipamentoSeNaoExiste(string nomeEstado, string nomeExibicao, string cor)
        {
            if (!db.EstadosCorPersonalizados.Any(e => e.Grupo == GruposEstadoCor.Equipamento && e.NomeEstado == nomeEstado))
            {
                db.EstadosCorPersonalizados.Add(new EstadoCorPersonalizada
                {
                    Grupo = GruposEstadoCor.Equipamento,
                    NomeEstado = nomeEstado,
                    NomeExibicao = nomeExibicao,
                    Cor = cor
                });
            }
        }

        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.EmServico, EstadosEquipamento.EmServico, "#22C55E");
        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.Recolhido, EstadosEquipamento.Recolhido, "#F59E0B");
        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.EmReparacao, EstadosEquipamento.EmReparacao, "#6366F1");
        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.Reparado, EstadosEquipamento.Reparado, "#22C55E");
        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.AguardaEntrega, EstadosEquipamento.AguardaEntrega, "#F59E0B");
        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.EmArmazem, EstadosEquipamento.EmArmazem, "#9CA3AF");
        SeedEstadoEquipamentoSeNaoExiste(EstadosEquipamento.Abatido, EstadosEquipamento.Abatido, "#EF4444");

        // Dados Fixos v2: cria (uma única vez) as características embutidas de "Computador",
        // "Rede", "Câmara", "Monitor" e "Projetor" — campos que antes eram fixos, geridos em
        // listas genéricas de Dados Fixos ou até só como texto/número livre. TEM de correr antes
        // de LimparValoresFixosOrfaos: os grupos migrados deixaram de constar em
        // GruposValorFixo.Todos (passaram a Características Específicas), pelo que a limpeza a
        // seguir os trata como resíduo e apaga-os — só depois de os ler para cá é que deixam de
        // fazer falta.
        MigrarCaracteristicasFixasEmbutidas(db);

        LimparValoresFixosOrfaos(db);

        db.SaveChanges();
    }

    /// <summary>
    /// (Dados Fixos v2) Cria, uma única vez, as características embutidas dos campos que antes
    /// eram fixos no formulário de Equipamento (painéis "Características do Computador/Rede/
    /// Câmara/Monitor/Projetor" em EquipamentoEditWindow.xaml): Processador, Tipo de Memória (→
    /// Memória (GB)), Tipo de Disco (→ Tamanho do Disco (GB)), Sistema Operativo, Nº de Portas,
    /// Velocidade, Tipo (Câmara), Resolução (Câmara), Tipo de Painel, Polegadas, Resolução
    /// (Monitor), Luminosidade e Resolução (Projetor). Nenhuma continua a aparecer na lista de
    /// "Dados Fixos" genérica (ver <see cref="GruposValorFixo.Todos"/>) — passam a ser geridas em
    /// Administração → Dados Fixos → Tipos de Equipamento → (grupo correspondente), tal como
    /// qualquer outra característica.
    ///
    /// Importante: só a ORIGEM da lista de valores sugeridos muda. O valor escolhido por
    /// equipamento continua a ser gravado exatamente como sempre foi, nas mesmas propriedades de
    /// <see cref="Equipamento"/> — por isso esta migração NUNCA precisa de tocar em nenhum
    /// equipamento já existente, só nas listas de valores sugeridos (ver
    /// Views/EquipamentoEditWindow.xaml.cs).
    ///
    /// Idempotente por grupo: cada bloco só cria as suas características se a primeira delas
    /// ainda não existir para esse grupo — uma versão anterior desta aplicação pode já ter
    /// migrado "Computador" sem ainda ter migrado "Rede", por exemplo, e este método trata bem
    /// esse caso.
    /// </summary>
    private static void MigrarCaracteristicasFixasEmbutidas(AppDbContext db)
    {
        // Lê os valores já configurados pelo administrador na antiga lista genérica de Dados
        // Fixos (se alguma vez os personalizou); sem nenhum valor configurado, usa a mesma lista
        // por omissão que a aplicação já usava para este campo.
        List<string> ValoresAntigos(string grupoAntigo, string[] porOmissao)
        {
            var valores = db.ValoresFixos
                .Where(v => v.Grupo == grupoAntigo && v.Ativo)
                .OrderBy(v => v.Ordem).ThenBy(v => v.Valor)
                .Select(v => v.Valor)
                .ToList();
            return valores.Count > 0 ? valores : porOmissao.ToList();
        }

        // Cria uma característica embutida (sem Tipo específico — partilhada por todos os Tipos
        // deste grupo) e devolve já o Id gravado, necessário para a poder referenciar de seguida
        // a partir das suas opções ou de uma característica-filha.
        int CriarCaracteristica(string grupo, string nome, int ordem, int? caracteristicaPaiId = null)
        {
            var caracteristica = new CaracteristicaEquipamento
            {
                GrupoCaracteristicas = grupo,
                Nome = nome,
                Ordem = ordem,
                Ativo = true,
                CaracteristicaPaiId = caracteristicaPaiId
            };
            db.CaracteristicasEquipamento.Add(caracteristica);
            db.SaveChanges(); // necessário para obter o Id, usado a seguir
            return caracteristica.Id;
        }

        void CriarOpcoes(int caracteristicaId, IEnumerable<string> valores, int? caracteristicaFilhaId = null)
        {
            var ordem = 0;
            foreach (var valor in valores)
            {
                db.CaracteristicaEquipamentoOpcoes.Add(new CaracteristicaEquipamentoOpcao
                {
                    CaracteristicaEquipamentoId = caracteristicaId,
                    Valor = valor,
                    Ordem = ordem++,
                    Ativo = true,
                    CaracteristicaFilhaId = caracteristicaFilhaId
                });
            }
        }

        // ==================== Computador ====================
        if (!db.CaracteristicasEquipamento.Any(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Computador && c.Nome == "Processador"))
        {
            const string grupo = GruposCaracteristicasEquipamento.Computador;

            // --- Processador (sem subtipo) ---
            var idProcessador = CriarCaracteristica(grupo, "Processador", 0);
            CriarOpcoes(idProcessador, ValoresAntigos(GruposValorFixo.Processador, Array.Empty<string>()));

            // --- Tipo de Memória → Memória (GB) ---
            // A característica-filha é criada primeiro para já ter Id atribuído quando as opções
            // da característica-pai a forem referenciar a seguir.
            var idMemoriaGb = CriarCaracteristica(grupo, "Memória (GB)", 2);
            CriarOpcoes(idMemoriaGb, new[] { "4", "8", "16", "32", "64", "128" });
            var idTipoMemoria = CriarCaracteristica(grupo, "Tipo de Memória", 1);
            CriarOpcoes(idTipoMemoria, ValoresAntigos(GruposValorFixo.TipoMemoria, new[] { "DDR3", "DDR4", "DDR5" }), idMemoriaGb);

            // --- Tipo de Disco → Tamanho do Disco (GB) ---
            var idTamanhoDisco = CriarCaracteristica(grupo, "Tamanho do Disco (GB)", 4);
            CriarOpcoes(idTamanhoDisco, new[] { "128", "256", "512", "1024", "2048" });
            var idTipoDisco = CriarCaracteristica(grupo, "Tipo de Disco", 3);
            CriarOpcoes(idTipoDisco, ValoresAntigos(GruposValorFixo.TipoDisco, new[] { "HDD", "SSD", "NVMe" }), idTamanhoDisco);

            // --- Sistema Operativo (sem subtipo) ---
            var idSistemaOperativo = CriarCaracteristica(grupo, "Sistema Operativo", 5);
            CriarOpcoes(idSistemaOperativo, ValoresAntigos(GruposValorFixo.SistemaOperativo, Array.Empty<string>()));
        }

        // ==================== Rede ====================
        // "Layer" e "Num. Antenas" já eram características dinâmicas antes desta migração — só
        // "Nº de Portas" e "Velocidade" continuavam fixos no código (TxtNumeroPortas/
        // TxtVelocidadeRede em EquipamentoEditWindow.xaml), sem nenhuma lista de Dados Fixos.
        if (!db.CaracteristicasEquipamento.Any(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Rede && c.Nome == "Nº de Portas"))
        {
            const string grupo = GruposCaracteristicasEquipamento.Rede;

            var idPortas = CriarCaracteristica(grupo, "Nº de Portas", 100);
            CriarOpcoes(idPortas, new[] { "4", "5", "8", "16", "24", "48" });

            var idVelocidade = CriarCaracteristica(grupo, "Velocidade", 101);
            CriarOpcoes(idVelocidade, new[] { "100 Mbps", "1 Gbps", "2.5 Gbps", "10 Gbps" });
        }

        // ==================== Câmara CCTV ====================
        if (!db.CaracteristicasEquipamento.Any(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Camera && c.Nome == "Tipo"))
        {
            const string grupo = GruposCaracteristicasEquipamento.Camera;

            var idTipo = CriarCaracteristica(grupo, "Tipo", 0);
            CriarOpcoes(idTipo, ValoresAntigos(GruposValorFixo.TipoCamera, new[] { "IP", "Analógica" }));

            var idResolucao = CriarCaracteristica(grupo, "Resolução", 1);
            CriarOpcoes(idResolucao, new[] { "2MP", "4MP", "1080p", "4K" });
        }

        // ==================== Monitor ====================
        if (!db.CaracteristicasEquipamento.Any(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Monitor && c.Nome == "Tipo de Painel"))
        {
            const string grupo = GruposCaracteristicasEquipamento.Monitor;

            var idTipoPainel = CriarCaracteristica(grupo, "Tipo de Painel", 0);
            CriarOpcoes(idTipoPainel, ValoresAntigos(GruposValorFixo.TipoPainelMonitor, new[] { "LED", "LCD", "OLED" }));

            var idPolegadas = CriarCaracteristica(grupo, "Polegadas", 1);
            CriarOpcoes(idPolegadas, new[] { "19", "21", "24", "27", "32" });

            var idResolucao = CriarCaracteristica(grupo, "Resolução", 2);
            CriarOpcoes(idResolucao, new[] { "1366x768", "1920x1080", "2560x1440", "3840x2160" });
        }

        // ==================== Projetor ====================
        if (!db.CaracteristicasEquipamento.Any(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Projetor && c.Nome == "Luminosidade (Lumens)"))
        {
            const string grupo = GruposCaracteristicasEquipamento.Projetor;

            var idLuminosidade = CriarCaracteristica(grupo, "Luminosidade (Lumens)", 0);
            CriarOpcoes(idLuminosidade, new[] { "2000", "3000", "4000", "5000", "6000" });

            var idResolucao = CriarCaracteristica(grupo, "Resolução", 1);
            CriarOpcoes(idResolucao, new[] { "1280x800", "1920x1080", "3840x2160" });
        }

        // ==================== Impressora ====================
        // (12) A lista genérica de Dados Fixos "Tipos de Impressora" deixa de existir como lista
        // à parte: passa a ser uma característica embutida do grupo "Impressora", exatamente
        // como já acontece para Computador/Rede/Câmara/Monitor/Projetor acima — geridas em
        // Administração → Dados Fixos → Tipos de Equipamento → Impressora, e visível no formulário
        // de Inserir/Editar Equipamento através de ValoresCaracteristicaEmbutida (ver
        // EquipamentoEditWindow.xaml.cs). "Ligação da Impressora" (USB/Rede/WiFi) não faz parte
        // deste pedido e continua a vir da lista genérica GruposValorFixo.LigacaoImpressora.
        if (!db.CaracteristicasEquipamento.Any(c => c.GrupoCaracteristicas == GruposCaracteristicasEquipamento.Impressora && c.Nome == "Tipo de Impressora"))
        {
            const string grupo = GruposCaracteristicasEquipamento.Impressora;

            var idTipoImpressora = CriarCaracteristica(grupo, "Tipo de Impressora", 0);
            CriarOpcoes(idTipoImpressora, ValoresAntigos(GruposValorFixo.TipoImpressora, new[] { "Laser", "Tinta" }));
        }
    }

    /// <summary>
    /// (11) Remove da tabela ValoresFixos quaisquer registos de grupos "ligados" — cujos valores
    /// reais vivem noutras tabelas (CategoriasDisia, EstadosCorPersonalizados) — bem como de
    /// grupos que já nem sequer existem (nomes antigos, entretanto renomeados/descontinuados).
    /// Estes registos são resíduo de versões anteriores da aplicação: nunca são lidos por nenhum
    /// ecrã (Dados Fixos lê CategoriasDisia/EstadosCorPersonalizados para estes grupos), mas
    /// continuavam visíveis na grelha do ecrã "Dados Fixos" à moda de dados fantasma, sem qualquer
    /// efeito nos formulários de inserção — daí a confusão relatada de "valores que aparecem numa
    /// lista mas não no formulário". Corre em cada arranque; é barato (a tabela é pequena) e
    /// idempotente (não faz nada assim que a limpeza já tiver sido feita uma vez).
    /// </summary>
    private static void LimparValoresFixosOrfaos(AppDbContext db)
    {
        var gruposLigados = new List<string>
        {
            GruposValorFixo.CategoriaAtividadeDisia,
            GruposValorFixo.EstadoIntervencaoEAtividadeDisia,
            GruposValorFixo.EstadoPedidoIntervencao
        };
        var gruposValidos = GruposValorFixo.Todos.Select(g => g.Grupo).ToHashSet();

        var orfaos = db.ValoresFixos
            .Where(v => gruposLigados.Contains(v.Grupo) || !gruposValidos.Contains(v.Grupo))
            .ToList();

        if (orfaos.Count > 0)
            db.ValoresFixos.RemoveRange(orfaos);
    }
}
