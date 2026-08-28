using System.IO;
using System.Text.Json;

namespace LeiriaDISIA.Services;

public static class AppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "settings.json");

    private static AppSettings _settings = Load();

    public static string VersaoApp
    {
        get => _settings.VersaoApp;
        set
        {
            _settings.VersaoApp = value;
            Save();
        }
    }

    // ---- Configuração de Email (SMTP) ----

    public static string SmtpServidor
    {
        get => _settings.SmtpServidor;
        set { _settings.SmtpServidor = value; Save(); }
    }

    public static int SmtpPorta
    {
        get => _settings.SmtpPorta;
        set { _settings.SmtpPorta = value; Save(); }
    }

    public static string SmtpUtilizador
    {
        get => _settings.SmtpUtilizador;
        set { _settings.SmtpUtilizador = value; Save(); }
    }

    public static string SmtpPassword
    {
        get => _settings.SmtpPassword;
        set { _settings.SmtpPassword = value; Save(); }
    }

    public static bool SmtpUsarSsl
    {
        get => _settings.SmtpUsarSsl;
        set { _settings.SmtpUsarSsl = value; Save(); }
    }

    public static string SmtpNomeRemetente
    {
        get => _settings.SmtpNomeRemetente;
        set { _settings.SmtpNomeRemetente = value; Save(); }
    }

    public static string SmtpEmailRemetente
    {
        get => _settings.SmtpEmailRemetente;
        set { _settings.SmtpEmailRemetente = value; Save(); }
    }

    /// <summary>Indica se existe configuração mínima de SMTP definida (servidor + email remetente).</summary>
    public static bool SmtpConfigurado =>
        !string.IsNullOrWhiteSpace(_settings.SmtpServidor) && !string.IsNullOrWhiteSpace(_settings.SmtpEmailRemetente);

    // ---- Obsolescência de Equipamento (pesos e limiares configuráveis) ----

    public static int ObsolescenciaPesoIdade
    {
        get => _settings.ObsolescenciaPesoIdade;
        set { _settings.ObsolescenciaPesoIdade = value; Save(); }
    }

    public static int ObsolescenciaPesoRam
    {
        get => _settings.ObsolescenciaPesoRam;
        set { _settings.ObsolescenciaPesoRam = value; Save(); }
    }

    public static int ObsolescenciaPesoDisco
    {
        get => _settings.ObsolescenciaPesoDisco;
        set { _settings.ObsolescenciaPesoDisco = value; Save(); }
    }

    public static int ObsolescenciaPesoProcessador
    {
        get => _settings.ObsolescenciaPesoProcessador;
        set { _settings.ObsolescenciaPesoProcessador = value; Save(); }
    }

    /// <summary>A partir deste score (0-100) o equipamento passa a "A Monitorizar".</summary>
    public static int ObsolescenciaLimiarMonitorizar
    {
        get => _settings.ObsolescenciaLimiarMonitorizar;
        set { _settings.ObsolescenciaLimiarMonitorizar = value; Save(); }
    }

    /// <summary>A partir deste score (0-100) o equipamento passa a "Obsoleto".</summary>
    public static int ObsolescenciaLimiarObsoleto
    {
        get => _settings.ObsolescenciaLimiarObsoleto;
        set { _settings.ObsolescenciaLimiarObsoleto = value; Save(); }
    }

    /// <summary>Indica se a aplicação deve criar automaticamente uma cópia de segurança da base
    /// de dados sempre que é encerrada (ver <see cref="App.PastaBackupsAutomaticos"/>). Ligado por
    /// omissão; pode ser desativado em Administração → Base de Dados.</summary>
    public static bool BackupAutomaticoAtivo
    {
        get => _settings.BackupAutomaticoAtivo;
        set { _settings.BackupAutomaticoAtivo = value; Save(); }
    }

    // ---- Dashboard: resolução de visualização (FHD 1920x1080 / UHD 2560x1440) ----

    /// <summary>Disposição do Dashboard a usar: false = FHD (1920×1080, disposição original,
    /// validada), true = UHD (2560×1440, disposição compacta otimizada para ecrãs maiores).
    /// A preferência é gravada e reaplicada automaticamente ao reabrir a aplicação.</summary>
    public static bool DashboardResolucaoUhd
    {
        get => _settings.DashboardResolucaoUhd;
        set { _settings.DashboardResolucaoUhd = value; Save(); }
    }

    // ---- Modo Compacto (janelas de edição, para ecrãs pequenos/portáteis) ----

    /// <summary>Quando ativo, as janelas de edição maiores (Escola, Equipamento, Intervenção,
    /// Atividade DISIA) ajustam automaticamente o seu tamanho à área de trabalho disponível em
    /// vez de usarem sempre o tamanho fixo definido no XAML — pensado para portáteis com ecrãs
    /// pequenos (ex.: 13" a 125% de escala), onde o tamanho fixo original não cabe no ecrã e
    /// impede o acesso aos botões de "Guardar"/"Cancelar" ou ao fecho da janela. Em ecrãs normais
    /// ou grandes não tem qualquer efeito visível. A preferência é gravada e reaplicada
    /// automaticamente ao reabrir a aplicação.</summary>
    public static bool ModoCompactoAtivo
    {
        get => _settings.ModoCompactoAtivo;
        set { _settings.ModoCompactoAtivo = value; Save(); }
    }

    // ---- Segurança: Política de Palavras-passe (Administração → Segurança) ----
    // Aplica-se à criação/edição de utilizadores e à alteração da própria password — ver
    // Services/PasswordPolicy.cs, que passou a ler estes valores em vez de os ter fixos no código.

    public static int PoliticaPasswordMinCaracteres
    {
        get => _settings.PoliticaPasswordMinCaracteres;
        set { _settings.PoliticaPasswordMinCaracteres = value; Save(); }
    }

    public static bool PoliticaPasswordExigirMaiuscula
    {
        get => _settings.PoliticaPasswordExigirMaiuscula;
        set { _settings.PoliticaPasswordExigirMaiuscula = value; Save(); }
    }

    public static bool PoliticaPasswordExigirMinuscula
    {
        get => _settings.PoliticaPasswordExigirMinuscula;
        set { _settings.PoliticaPasswordExigirMinuscula = value; Save(); }
    }

    public static bool PoliticaPasswordExigirNumero
    {
        get => _settings.PoliticaPasswordExigirNumero;
        set { _settings.PoliticaPasswordExigirNumero = value; Save(); }
    }

    public static bool PoliticaPasswordExigirSimbolo
    {
        get => _settings.PoliticaPasswordExigirSimbolo;
        set { _settings.PoliticaPasswordExigirSimbolo = value; Save(); }
    }

    // ---- Segurança: Tentativas de Login (Administração → Segurança) ----
    // Ver Views/LoginWindow.xaml.cs - 0 desativa o bloqueio automático.

    public static int TentativasLoginMaximo
    {
        get => _settings.TentativasLoginMaximo;
        set { _settings.TentativasLoginMaximo = value; Save(); }
    }

    // ---- Segurança: Sessão / inatividade (Administração → Segurança) ----
    // Ver Services/SessaoInatividadeService.cs.

    public static bool SessaoTerminarPorInatividade
    {
        get => _settings.SessaoTerminarPorInatividade;
        set { _settings.SessaoTerminarPorInatividade = value; Save(); }
    }

    public static int SessaoMinutosInatividade
    {
        get => _settings.SessaoMinutosInatividade;
        set { _settings.SessaoMinutosInatividade = value; Save(); }
    }

    /// <summary>
    /// Mensagem da última falha ao gravar as configurações em disco (ex: permissões, disco cheio),
    /// ou null se a última gravação foi bem sucedida. Permite à interface avisar o utilizador em
    /// vez de reverter silenciosamente para os valores de omissão na próxima abertura da aplicação.
    /// </summary>
    public static string? UltimoErroPersistencia { get; private set; }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            UltimoErroPersistencia = ex.Message;
        }
        return new AppSettings();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
            UltimoErroPersistencia = null;
        }
        catch (Exception ex)
        {
            // Antes esta falha era ignorada silenciosamente, fazendo as alterações do utilizador
            // "desaparecerem" sem explicação na próxima abertura da aplicação. Guarda-se agora a
            // mensagem para a interface poder avisar o utilizador de imediato.
            UltimoErroPersistencia = ex.Message;
        }
    }

    private sealed class AppSettings
    {
        public string VersaoApp { get; set; } = "1.1 — 2026";

        // SMTP
        public string SmtpServidor { get; set; } = "";
        public int SmtpPorta { get; set; } = 587;
        public string SmtpUtilizador { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public bool SmtpUsarSsl { get; set; } = true;
        public string SmtpNomeRemetente { get; set; } = "DISIA - Câmara Municipal de Leiria";
        public string SmtpEmailRemetente { get; set; } = "";

        // Obsolescência de equipamento - pesos (devem somar ~100, mas são normalizados no cálculo)
        public int ObsolescenciaPesoIdade { get; set; } = 40;
        public int ObsolescenciaPesoRam { get; set; } = 20;
        public int ObsolescenciaPesoDisco { get; set; } = 20;
        public int ObsolescenciaPesoProcessador { get; set; } = 20;
        public int ObsolescenciaLimiarMonitorizar { get; set; } = 40;
        public int ObsolescenciaLimiarObsoleto { get; set; } = 70;
        public bool BackupAutomaticoAtivo { get; set; } = true;

        // Dashboard: resolução de visualização
        public bool DashboardResolucaoUhd { get; set; } = false;

        // Modo Compacto: janelas de edição adaptadas a ecrãs pequenos/portáteis
        public bool ModoCompactoAtivo { get; set; } = false;

        // Segurança: Política de Palavras-passe
        public int PoliticaPasswordMinCaracteres { get; set; } = 8;
        public bool PoliticaPasswordExigirMaiuscula { get; set; } = true;
        public bool PoliticaPasswordExigirMinuscula { get; set; } = true;
        public bool PoliticaPasswordExigirNumero { get; set; } = true;
        public bool PoliticaPasswordExigirSimbolo { get; set; } = true;

        // Segurança: Tentativas de Login (0 = bloqueio automático desativado)
        public int TentativasLoginMaximo { get; set; } = 5;

        // Segurança: Sessão / inatividade
        public bool SessaoTerminarPorInatividade { get; set; } = false;
        public int SessaoMinutosInatividade { get; set; } = 15;
    }
}
