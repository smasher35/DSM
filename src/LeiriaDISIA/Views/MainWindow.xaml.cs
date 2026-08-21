using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class MainWindow : Window
{
    private bool _estaReiniciando;
    private readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        ContentArea.Content = new DashboardView();

        var utilizador = SessaoAtual.UtilizadorLogado;
        TxtUtilizadorAtual.Text = utilizador?.NomeCompleto ?? "—";
        TxtPerfilAtual.Text = utilizador?.Perfil.ToString() ?? "—";

        // Carregar avatar do utilizador
        if (utilizador != null)
        {
            var avatar = AvatarService.CarregarAvatar(utilizador.Id);
            if (avatar != null)
            {
                AvatarImage.Source = avatar;
                AvatarInitials.Visibility = Visibility.Collapsed;
            }
            else
            {
                AvatarInitials.Text = AvatarService.ObterIniciaisNome(utilizador.NomeCompleto);
                AvatarInitials.Visibility = Visibility.Visible;
                AvatarImage.Source = null;
            }
        }

        // O módulo de Administração só é visível para utilizadores com perfil Administrador.
        NavAdministracao.Visibility = SessaoAtual.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        // Versão da aplicação
        AtualizarVersaoSidebar();

        // Relógio na barra de status
        _relogio.Tick += (_, _) => TxtStatusRelogio.Text = DateTime.Now.ToString("dd/MM/yyyy  HH:mm:ss");
        _relogio.Start();
        TxtStatusRelogio.Text = DateTime.Now.ToString("dd/MM/yyyy  HH:mm:ss");

        // Listener para quando o tema muda - re-renderiza o Dashboard (ContentArea)
        ThemeService.TemaMudou += (s, e) =>
        {
            if (ContentArea.Content is DashboardView)
            {
                ContentArea.Content = new DashboardView();
            }
        };

        // (8.1) Centro de notificações: primeira carga + atualização periódica (a cada 15 min),
        // sem popups automáticos — fica disponível no sino, discreto, até o utilizador consultar.
        AtualizarNotificacoes();
        _notificacoes.Tick += (_, _) => AtualizarNotificacoes();
        _notificacoes.Start();

        Closed += (_, _) =>
        {
            _relogio.Stop();
            _notificacoes.Stop();
            if (!_estaReiniciando) Application.Current.Shutdown();
        };
    }

    private readonly DispatcherTimer _notificacoes = new() { Interval = TimeSpan.FromMinutes(15) };

    private void AtualizarNotificacoes()
    {
        var itens = new NotificacoesService(App.Db).Gerar();
        ListaNotificacoes.ItemsSource = itens;
        TxtSemNotificacoes.Visibility = itens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (itens.Count == 0)
        {
            BadgeNotificacoes.Visibility = Visibility.Collapsed;
        }
        else
        {
            BadgeNotificacoes.Visibility = Visibility.Visible;
            TxtBadgeNotificacoes.Text = itens.Count > 9 ? "9+" : itens.Count.ToString();
        }
    }

    private void Notificacoes_Click(object sender, RoutedEventArgs e)
    {
        AtualizarNotificacoes();
        PopupNotificacoes.IsOpen = !PopupNotificacoes.IsOpen;
    }

    private void NotificacaoItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        PopupNotificacoes.IsOpen = false;
        if (sender is not FrameworkElement { Tag: string modulo } || string.IsNullOrEmpty(modulo)) return;

        // Reaproveita a mesma navegação dos botões da barra lateral.
        var rb = FindName("Nav" + modulo) as RadioButton;
        if (rb != null)
        {
            rb.IsChecked = true;
        }
    }

    public void AtualizarVersaoSidebar()
    {
        TxtVersaoApp.Text = $"v{AppSettingsService.VersaoApp}";
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentArea == null) return;
        if (sender is not RadioButton rb) return;

        if (rb.Tag as string == "Dashboard")
        {
            ContentArea.Content = new DashboardView();
            return;
        }

        // Cada módulo abre a sua própria janela maximizada, modal em relação ao Menu Principal.
        // Ao fechar, o Dashboard é recarregado e a seleção volta a "Dashboard".
        Window? janela = rb.Tag as string switch
        {
            "Agrupamentos" => new AgrupamentosWindow(),
            "Escolas" => new EscolasWindow(),
            "Pedidos" => new PedidosWindow(),
            "Intervencoes" => new IntervencoesWindow(),
            "Disia" => new DisiaWindow(),
            "Equipamentos" => new EquipamentosWindow(),
            "Abatido" => new EquipamentoAbatidoWindow(),
            "Recolhido" => new EquipamentoRecolhidoWindow(),
            "Comunicacoes" => new ComunicacoesWindow(),
            "Contactos" => new ContactosWindow(),
            "Relatorios" => new RelatoriosWindow(),
            "Administracao" => SessaoAtual.IsAdmin ? new AdministracaoWindow() : null,
            _ => null
        };

        if (janela != null)
        {
            janela.Owner = this;
            janela.ShowDialog();
        }


        // Depois de fechar o módulo, regressa ao Dashboard (menu principal)
        NavDashboard.IsChecked = true;
        ContentArea.Content = new DashboardView();
    }

    private void TerminarSessao_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Deseja terminar a sessão atual?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        SessaoAtual.Terminar();

        // Desfoca o conteúdo atual (que pode conter dados sensíveis) antes de mostrar o ecrã
        // de login, para que nada fique visível por baixo da janela de autenticação.
        var blurAnterior = Effect;
        Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 35 };

        var login = new LoginWindow { Owner = this };
        var autenticado = login.ShowDialog();

        Effect = blurAnterior;

        if (autenticado == true)
        {
            var novaJanela = new MainWindow();
            Application.Current.MainWindow = novaJanela;
            novaJanela.Show();
            _estaReiniciando = true;
            Close();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private void SairAplicacao_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Deseja sair da aplicação?\n\nSerá criada automaticamente uma cópia de segurança da base de dados.",
                "Confirmar Saída", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        Application.Current.Shutdown();
    }
}
