# Gestão DISIA — Escolas e Intervenções (Município de Leiria)

Aplicação de secretária em **C# / WPF (.NET 8)** para substituir o `ficheiro_base.xlsx`,
destinada a registar:

- as intervenções técnicas realizadas nas escolas e jardins de infância do concelho de Leiria;
- os pedidos de intervenção antes de serem atendidos;
- as atividades gerais da DISIA (fora do âmbito escolar);
- o parque de equipamento informático e respetivos abates;
- os contactos das escolas;
- e que gera o relatório mensal/anual de atividades em Word, no mesmo formato do
  relatório modelo fornecido (`relatorio_atividades_disia_jun_26.pdf`).

---

## 1. Como abrir e correr o projeto

Pré-requisitos: **Visual Studio 2022** (ou superior) com a carga de trabalho
".NET desktop development", e o **.NET 8 SDK**.

1. Abrir `LeiriaDISIA.sln` no Visual Studio.
2. Compilar (`Ctrl+Shift+B`) — o NuGet restaura automaticamente os pacotes:
   - `Microsoft.EntityFrameworkCore.Sqlite` — base de dados local (ficheiro único, sem servidor);
   - `CommunityToolkit.Mvvm` — utilitários MVVM;
   - `ModernWpfUI` — controlos com visual moderno (Fluent Design);
   - `LiveChartsCore.SkiaSharpView.WPF` — gráficos do Dashboard;
   - `ClosedXML` — leitura do `ficheiro_base.xlsx` na importação inicial;
   - `DocumentFormat.OpenXml` — geração dos relatórios `.docx`;
   - `QuestPDF` — geração do relatório PDF de cada intervenção individual.
3. Correr (`F5`).

Na **primeira execução**, a aplicação cria a base de dados SQLite em
`%LOCALAPPDATA%\LeiriaDISIA\disia.db` e pergunta se deseja importar o
`ficheiro_base.xlsx` original. Escolher "Sim" e selecionar o ficheiro.

---

## 2. Mapeamento do ficheiro_base.xlsx para a aplicação

| Aba do Excel | Módulo da aplicação | Observações |
|---|---|---|
| `GEPE` | **Escolas** (fonte oficial) | Fornece os campos obrigatórios: CodEscola, CodDGRHE, CodGEPE, Escola, Morada, Localidade, Freguesia, CodAgrupamento, NomeDoAgrupamento. É a partir desta aba que os Agrupamentos e Escolas são criados. |
| `Lista de Escolas` | **Escolas** (nomes alternativos) | Nunca cria escolas novas — apenas associa um "nome alternativo/abreviado" (ex. `EB1 Amor`) a uma escola já existente vinda da aba GEPE, evitando duplicados. Ver secção 3. |
| `Contactos` | **Contactos** | Associados à escola correspondente (por nome). |
| `JAN` … `DEZ` | **Intervenções** | Cada linha de cada aba mensal gera um registo de Intervenção, com Data, Escola, Agrupamento, categorias (Hardware/Software/Redes/VPN/Audio-Visual) e "Material Recolhido/Abatido". O campo `Mes`/`Ano` de cada Intervenção é o que permite reconstruir "abas mensais" dentro da base de dados, sem precisar de tabelas separadas por mês. |
| `Totais` | Calculado automaticamente | Os totais por agrupamento/mês/categoria já não precisam de ser mantidos manualmente — são calculados em tempo real pelo `DashboardService` e pelo `RelatorioService` a partir das Intervenções. |
| `Pedidos de Intervenção` / `Casos Pendentes` | **Pedidos de Intervenção** | Módulo próprio com estado (Pendente/Em Andamento/Concluído/Cancelado) e ligação 1-para-1 a uma Intervenção quando resolvidos. |
| `Serv. DISIA` | **Atividades DISIA** | Cada linha gera uma Atividade DISIA. O padrão "(2x)" no fim da descrição é interpretado automaticamente como quantidade. |
| `Equipamento Abatido` | **Equipamento Abatido** | Módulo próprio, ligado (opcionalmente) à tabela de Equipamentos. |
| `DASHBOARD` | **Dashboard** | Recriado com KPIs e gráficos (ver secção 5). |

---

## 3. Deduplicação de escolas (muito importante)

O pedido original identificou o problema de a mesma escola aparecer com nomes
diferentes em abas diferentes (ex.: `EB1 Amor` na aba "Lista de Escolas" vs.
`Escola Básica do 1.º Ciclo de Amor` na aba "GEPE").

A classe `Services/TextNormalizer.cs` resolve isto:

1. `RemoveAccentsAndPunctuation` — remove acentuação, maiúsculas e pontuação.
2. `CanonicalSchoolKey` — remove prefixos comuns e irrelevantes para a
   identidade da escola ("Escola Básica do 1.º Ciclo de", "Centro Escolar",
   "EB1", "JI", "Jardim de Infância de", etc.), sobrando o "núcleo" do nome
   (ex.: `amor`).
3. `AreLikelySameSchool` — compara duas escolas por:
   - igualdade da chave canónica;
   - uma chave conter a outra;
   - distância de Levenshtein (tolerância a pequenas diferenças de escrita).

Esta função é usada:
- na importação inicial (`ExcelImportService`), para nunca duplicar uma escola
  da aba GEPE com uma da aba "Lista de Escolas";
- no módulo de Escolas (janela de edição de escola), avisando o utilizador
  sempre que tenta guardar manualmente uma escola com nome muito semelhante
  a uma já existente.

---

## 4. Estrutura do código

```
src/LeiriaDISIA/
 ├─ Models/              # Entidades (Agrupamento, Escola, Intervencao, PedidoIntervencao,
 │                       #  CategoriaIntervencao, Equipamento, EquipamentoAbatido,
 │                       #  AtividadeDisia, Contacto)
 ├─ Data/
 │   ├─ AppDbContext.cs      # Contexto EF Core (SQLite)
 │   └─ DbInitializer.cs     # Criação da BD + seed de categorias base
 ├─ Services/
 │   ├─ TextNormalizer.cs        # Normalização/deduplicação de nomes de escolas
 │   ├─ ExcelImportService.cs    # Importação do ficheiro_base.xlsx (1ª execução)
 │   ├─ DashboardService.cs      # Agregações para o Dashboard
 │   └─ RelatorioService.cs      # Geração dos relatórios mensal/anual em .docx
 ├─ Views/               # Um UserControl por módulo (ver secção 6) + MainWindow
 └─ Themes/ModernTheme.xaml  # Paleta de cores e estilos (cartões, KPIs, botões, grelhas)
```

A aplicação não usa um IoC container: `App.Db` é um `AppDbContext` único,
partilhado por todos os módulos (aplicação de posto único, mono-utilizador).

---

## 5. Dashboard

Ecrã inicial (`Views/DashboardView`), com:

- KPIs: total de intervenções no ano corrente, total histórico, nº de
  agrupamentos, nº de escolas ativas, nº de pedidos pendentes, agrupamento
  mais intervencionado de sempre e escola mais intervencionada de sempre;
- Gráfico de barras: intervenções por mês (ano corrente);
- Gráfico circular: intervenções por categoria (Hardware/Software/Redes/VPN/Audio-Visual);
- Gráfico de barras: intervenções por agrupamento (mês corrente);
- Gráfico de barras: intervenções por agrupamento (ano corrente).

---

## 6. Autenticação e Perfis de Utilizador

A aplicação exige início de sessão. Existem dois perfis:

- **Administrador** — acesso total, incluindo o módulo de Administração;
- **Utilizador** — acesso a todos os módulos de dados, mas **sem** acesso ao
  módulo de Administração (gestão de utilizadores, backup/restauro e apagar
  dados), que fica automaticamente oculto na barra lateral.

Utilizador criado por omissão na primeira execução: **admin / admin123**
(deve ser alterado ou substituído depois do primeiro acesso, em Administração
→ Gestão de Utilizadores).

## 7. Arquitetura de Janelas

- A **janela principal** (Menu Principal) mostra apenas o Dashboard e a
  barra lateral de navegação, e abre sempre **maximizada**.
- Cada módulo (Agrupamentos, Escolas, Pedidos, Intervenções, Atividades
  DISIA, Equipamentos, Equipamento Abatido, Contactos, Relatórios,
  Administração) abre a partir da barra lateral **na sua própria janela,
  também maximizada**, com uma barra de topo com três ações: **🏠 Menu
  Principal** (fecha o módulo e regressa ao ecrã inicial), **➕ Inserir**
  (abre o formulário de criação) e **📄 Relatório do Módulo** (gera um
  relatório do que está a ser consultado).
- **Todas as janelas de inserção/edição de dados são janelas modais**
  (`ShowDialog`), sobre a janela do módulo que as abriu — nunca é possível
  editar diretamente numa grelha; edita-se sempre através do formulário
  modal correspondente (duplo-clique numa linha ou botão "Inserir").

## 8. Módulos disponíveis (barra lateral)

1. **Dashboard** — visão geral (secção 5).
2. **Agrupamentos** — lista + sub-lista de escolas do agrupamento
   selecionado (duplo-clique para editar uma escola, ou adicionar uma nova
   já associada a esse agrupamento) + relatório em `.txt`.
3. **Escolas / JI** — lista com filtro por agrupamento e pesquisa; edição
   modal com todos os campos obrigatórios da aba GEPE, campos adicionais de
   caracterização, **coordenadas geográficas (latitude/longitude)** e
   **fotografia da escola** (escolhida do disco e copiada para a pasta de
   dados da aplicação).
4. **Pedidos de Intervenção** — registo do pedido (escola, agrupamento,
   razão, solicitante, estado — incluindo **"Em Espera"**, quando depende de
   aquisição de equipamento); mostra o **tempo em aberto em dias**, com
   indicador semafórico (verde ≤ 7 dias, amarelo 8–21 dias, vermelho > 21
   dias); botão para criar a Intervenção correspondente, que fecha
   automaticamente o pedido.
5. **Intervenções** — filtros por ano/mês/agrupamento, categorias múltiplas,
   estado com cor associada (fechada = verde, pendente = vermelho, em
   progresso = laranja, **em espera = roxo**, cancelada = cinzento) e botão
   para **imprimir um relatório em PDF** de cada intervenção individual.
6. **Atividades DISIA** — equivalente à aba "Serv. DISIA".
7. **Equipamento Informático** — nº de série e nº de inventário obrigatórios
   e únicos; **campos dinâmicos consoante o tipo de equipamento**:
   processador/memória/disco/SO para computadores, polegadas/painel/
   resolução para monitores, tipo/cor/ligação para impressoras, portas/
   velocidade/gestão para equipamento de rede, resolução/visão noturna/tipo
   para câmaras CCTV, luminosidade/resolução para projetores, e um campo de
   especificações livres para outros tipos.
8. **Equipamento Abatido** — ligado à tabela de Equipamentos.
9. **Contactos** — CRUD de contactos por escola.
10. **Relatórios** — relatório mensal/anual em `.docx`.
11. **Administração** *(apenas Administradores)* — gestão de utilizadores e
    perfis, localização da base de dados, backup, restauro e eliminação de
    todos os dados de negócio (preservando sempre os utilizadores).

---

## 9. Extensibilidade

- **Novas categorias de intervenção**: basta adicionar registos à tabela
  `CategoriaIntervencao` (não é necessário alterar código) — os módulos de
  Intervenções e o Dashboard já as apresentam automaticamente.
- **Subcategorias**: a tabela `SubCategoriaIntervencao` já está preparada,
  bastando expor um combo adicional nos formulários quando for necessário.
- **Vários anos civis**: todas as consultas são filtradas por `Ano`/`Mes`,
  pelo que a aplicação funciona da mesma forma em qualquer ano, sem
  necessidade de criar novas tabelas por ano.

---

## 10. Configuração da API de Planeamento de Rotas

A funcionalidade de Planeamento de Rotas (geocodificação e cálculo de distâncias
rodoviárias) requer uma chave de API do **OpenRouteService**.

**⚠️ IMPORTANTE:** Consulte o ficheiro [SECURITY.md](SECURITY.md) para informações
detalhadas sobre gestão segura de credenciais de API.

### Obter uma chave de API (gratuita)

1. Aceder a https://openrouteservice.org/dev/#/signup
2. Criar uma conta e gerar uma chave de API
3. A conta gratuita permite até 2.500 pedidos por dia (suficiente para o uso
   normal da aplicação)

### Configurar a chave no sistema

A chave deve ser definida como variável de ambiente de sistema (`DISIA_ORS_API_KEY`)
**em cada computador onde a aplicação vai correr**.

**Opção 1: Via linha de comandos (requer privilégios de Administrador)**

1. Abrir uma linha de comandos como Administrador
2. Executar (substituindo `SUA_CHAVE_AQUI` pela chave real):
   ```
   setx DISIA_ORS_API_KEY "SUA_CHAVE_AQUI" /M
   ```
3. Reiniciar a aplicação para que a nova variável seja reconhecida

**Opção 2: Via interface gráfica do Windows**

1. Painel de Controlo → Sistema → Definições avançadas do sistema
2. Variáveis de Ambiente → Variáveis do sistema → Nova
3. Nome: `DISIA_ORS_API_KEY`
4. Valor: a chave obtida no OpenRouteService
5. Reiniciar a aplicação

### Segurança

**IMPORTANTE:** A chave de API é um segredo e **nunca deve ser incluída em
código-fonte, ficheiros de configuração versionados, ou artefactos de compilação**.

- O ficheiro `install_api.txt` contém apenas um placeholder e instruções
- Este ficheiro está incluído no `.gitignore` para evitar commits acidentais
  de chaves reais
- Se uma chave for comprometida (exposta publicamente), deve ser imediatamente
  revogada no portal do OpenRouteService e substituída por uma nova

Sem a chave configurada, a aplicação continua a funcionar normalmente, mas as
funcionalidades de geocodificação e planeamento de rotas ficam indisponíveis
(a aplicação mostra uma mensagem clara ao utilizador quando tenta usar essas
funcionalidades).

---

## 11. Notas finais

- O ficheiro `disia.db` (SQLite) fica em `%LOCALAPPDATA%\LeiriaDISIA\`.
  Convém incluir este ficheiro no plano de cópias de segurança do posto de
  trabalho (ou migrar para SQL Server/Azure SQL no futuro, bastando trocar o
  provider em `AppDbContext.OnConfiguring`).
- As fotografias das escolas ficam em
  `%LOCALAPPDATA%\LeiriaDISIA\Imagens\Escolas\`.
- Este projeto é entregue como código-fonte completo (não como executável),
  para poder ser revisto, ajustado e compilado em Visual Studio de acordo com
  as necessidades específicas da DISIA.
