using System.IO;
using System.Windows;
using LeiriaDISIA.Data;
using LeiriaDISIA.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace LeiriaDISIA;

public partial class App : Application
{
    /// <summary>DbContext único e partilhado, criado no arranque (aplicação de secretária, single-user).</summary>
    public static AppDbContext Db { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Services.ThemeService.AplicarTemaGuardado();

        // Inicializar pasta de avatares
        Services.AvatarService.InicializarPasta();

        Db = new AppDbContext();
        DbInitializer.Inicializar(Db);

        // Se ainda não existirem escolas, propõe importar o ficheiro_base.xlsx original.
        if (!Db.Escolas.Any())
        {
            var resposta = MessageBox.Show(
                "Ainda não existem escolas registadas.\n\n" +
                "Deseja importar agora os dados do ficheiro_base.xlsx (agrupamentos, escolas, contactos, " +
                "intervenções mensais e atividades da DISIA)?",
                "Importação inicial de dados",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resposta == MessageBoxResult.Yes)
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Selecionar ficheiro_base.xlsx",
                    Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
                };

                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var servico = new Services.ExcelImportService(Db);
                        var resultado = servico.ImportarFicheiroBase(dialog.FileName);

                        MessageBox.Show(
                            $"Importação concluída:\n\n" +
                            $"Agrupamentos criados: {resultado.AgrupamentosCriados}\n" +
                            $"Escolas criadas: {resultado.EscolasCriadasDeGepe}\n" +
                            $"Escolas ignoradas (duplicadas): {resultado.EscolasIgnoradasPorDuplicado}\n" +
                            $"Contactos importados: {resultado.ContactosImportados}\n" +
                            $"Intervenções importadas: {resultado.IntervencoesImportadas}\n" +
                            $"Atividades DISIA importadas: {resultado.AtividadesDisiaImportadas}\n\n" +
                            (resultado.Avisos.Count > 0
                                ? $"Avisos ({resultado.Avisos.Count}) - ver relatório de importação para detalhe."
                                : "Sem avisos."),
                            "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                            "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ---- Login ----
        var login = new Views.LoginWindow();
        var autenticado = login.ShowDialog();

        if (autenticado != true)
        {
            Shutdown();
            return;
        }

        var main = new Views.MainWindow();
        MainWindow = main;
        main.Show();

        Services.SessaoInatividadeService.Iniciar();
    }

    /// <summary>Pasta por omissão onde é guardado um backup automático sempre que a aplicação é encerrada.</summary>
    public static string PastaBackupsAutomaticos { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "Backups");

    protected override void OnExit(ExitEventArgs e)
    {
        RealizarBackupAutomatico();
        Db.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Cria uma cópia de segurança da base de dados atual (utilizadores incluídos) na pasta
    /// de backups por omissão, sempre que a aplicação é encerrada — exceto se o utilizador tiver
    /// desativado os backups automáticos em Administração → Base de Dados. Falhas neste processo
    /// nunca impedem o encerramento normal da aplicação.
    ///
    /// Para não acumular um ficheiro novo por cada encerramento (o que ao fim de meses gera
    /// centenas de cópias quase idênticas), o nome do ficheiro roda apenas entre "par" e "ímpar"
    /// consoante o dia do mês: há sempre no máximo 2 backups automáticos guardados, cada um
    /// substituído a cada 2 dias, mantendo ainda assim uma cópia recente e uma do dia anterior.
    /// </summary>
    private static void RealizarBackupAutomatico()
    {
        if (!AppSettingsService.BackupAutomaticoAtivo) return;

        try
        {
            Directory.CreateDirectory(PastaBackupsAutomaticos);
            var sufixoParidade = DateTime.Now.Day % 2 == 0 ? "par" : "impar";
            var destino = Path.Combine(PastaBackupsAutomaticos, $"Backup_auto_{sufixoParidade}.db");

            Db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(AppDbContext.DbPath))
                File.Copy(AppDbContext.DbPath, destino, overwrite: true);

            Db = new AppDbContext(); // religa para que o resto do OnExit (Db.Dispose()) não falhe
        }
        catch
        {
            // O backup automático é uma cortesia, não uma operação crítica: uma falha aqui
            // (ex.: disco cheio, sem permissões) nunca deve impedir o encerramento da aplicação.
        }
    }

    // -----------------------------------------------------------------
    // Gestão da base de dados (usado pelo módulo de Configurações)
    // -----------------------------------------------------------------

    /// <summary>Fecha a ligação atual à base de dados (necessário antes de copiar/substituir o ficheiro).</summary>
    public static void FecharLigacaoDb()
    {
        Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>Reabre a ligação à base de dados (ficheiro existente) e garante que o esquema está criado.</summary>
    public static void ReabrirLigacaoDb()
    {
        Db = new AppDbContext();
        DbInitializer.Inicializar(Db);
    }

    /// <summary>Substitui o ficheiro de base de dados atual por outro (restauro de cópia de segurança).</summary>
    public static void RestaurarBackup(string caminhoFicheiroBackup)
    {
        FecharLigacaoDb();
        File.Copy(caminhoFicheiroBackup, AppDbContext.DbPath, overwrite: true);
        ReabrirLigacaoDb();
    }

    /// <summary>
    /// Apaga PERMANENTEMENTE todos os dados de todas as tabelas, exceto a de Utilizadores (para
    /// que ninguém fique bloqueado fora da aplicação) — agrupamentos, escolas, contactos, pedidos,
    /// intervenções, atividades da DISIA, equipamentos, abates, comunicações, dados de relatório
    /// mensal e Dados Fixos (que são automaticamente repostos com os valores por omissão a seguir,
    /// ver <see cref="DbInitializer.Inicializar"/>).
    ///
    /// Em vez de apagar tabela a tabela pela ordem exigida pelas relações entre elas (frágil: uma
    /// tabela nova ou uma relação nova pode voltar a bloquear o apagamento, como aconteceu antes),
    /// desliga-se temporariamente a verificação de chaves estrangeiras do SQLite, apaga-se cada
    /// tabela e volta a ligar-se a verificação. Isto garante que o botão "Apagar Toda a Base de
    /// Dados" funciona sempre, independentemente de quantas ligações existam entre as tabelas.
    ///
    /// Tal como em <see cref="RestaurarBackup"/>, a ligação do EF Core é fechada por completo
    /// antes de se mexer diretamente no ficheiro, e só reaberta no fim: misturar SQL em bruto com
    /// a mesma ligação que o EF Core está a gerir internamente pode fazer com que o SaveChanges
    /// seguinte falhe (foi exatamente isso que aconteceu numa versão anterior desta função).
    /// </summary>
    public static void ApagarTudo()
    {
        FecharLigacaoDb();

        try
        {
            using var conexao = new SqliteConnection($"Data Source={AppDbContext.DbPath}");
            conexao.Open();

            // PRAGMA foreign_keys só pode ser alterado fora de uma transação — por isso corre
            // antes do BeginTransaction, não dentro dele.
            using (var pragmaOff = conexao.CreateCommand())
            {
                pragmaOff.CommandText = "PRAGMA foreign_keys = OFF;";
                pragmaOff.ExecuteNonQuery();
            }

            using (var transacao = conexao.BeginTransaction())
            {
                var nomesTabelas = new List<string>();
                bool temSqliteSequence;
                using (var listar = conexao.CreateCommand())
                {
                    listar.Transaction = transacao;
                    // Todas as tabelas de dados da aplicação, exceto Utilizadores e a tabela
                    // interna de histórico de migrações do EF Core.
                    listar.CommandText = @"SELECT name FROM sqlite_master
                                            WHERE type = 'table'
                                              AND name NOT LIKE 'sqlite_%'
                                              AND name NOT IN ('Usuarios', '__EFMigrationsHistory')";
                    using var reader = listar.ExecuteReader();
                    while (reader.Read()) nomesTabelas.Add(reader.GetString(0));
                }

                using (var verificarSeq = conexao.CreateCommand())
                {
                    verificarSeq.Transaction = transacao;
                    // "sqlite_sequence" só existe se alguma tabela usar AUTOINCREMENT; tentar
                    // apagar dela sem essa verificação lançaria erro numa base sem essa tabela.
                    verificarSeq.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence'";
                    temSqliteSequence = verificarSeq.ExecuteScalar() != null;
                }

                foreach (var tabela in nomesTabelas)
                {
                    using (var apagar = conexao.CreateCommand())
                    {
                        apagar.Transaction = transacao;
                        apagar.CommandText = $"DELETE FROM \"{tabela}\";";
                        apagar.ExecuteNonQuery();
                    }

                    if (!temSqliteSequence) continue;

                    // Repõe o contador de autoincremento, para os IDs voltarem a começar em 1.
                    using var resetSeq = conexao.CreateCommand();
                    resetSeq.Transaction = transacao;
                    resetSeq.CommandText = "DELETE FROM \"sqlite_sequence\" WHERE name = @tabela;";
                    resetSeq.Parameters.AddWithValue("@tabela", tabela);
                    resetSeq.ExecuteNonQuery();
                }

                transacao.Commit();
            }

            using (var pragmaOn = conexao.CreateCommand())
            {
                pragmaOn.CommandText = "PRAGMA foreign_keys = ON;";
                pragmaOn.ExecuteNonQuery();
            }
        }
        finally
        {
            // Reabre a ligação do EF Core e repõe de imediato os Dados Fixos e as Categorias com
            // os valores por omissão, para a aplicação continuar utilizável (dropdowns
            // preenchidas) em vez de ficar com listas vazias — mesmo que o apagamento acima
            // tenha falhado a meio, nunca se deve deixar a aplicação sem ligação à base de dados.
            ReabrirLigacaoDb();
        }
    }
}
