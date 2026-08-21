using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class UsuarioEditWindow : Window
{
    private readonly Usuario? _existente;
    private string? _caminhoAvatarTemporario;
    private bool _avatarAlterado = false; // Flag para rastrear se avatar foi alterado
    public bool Sucesso { get; private set; }

    public UsuarioEditWindow(Usuario? usuario)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = usuario;

        CmbPerfil.ItemsSource = Enum.GetValues<PerfilUtilizador>();
        AtualizarForcaPassword();

        if (usuario == null)
        {
            TxtTitulo.Text = "Novo Utilizador";
            CmbPerfil.SelectedItem = PerfilUtilizador.Utilizador;
            TxtAvisoPassword.Text = "Obrigatória para um novo utilizador.";
            return;
        }

        TxtTitulo.Text = "Editar Utilizador";
        TxtNomeUtilizador.Text = usuario.NomeUtilizador;
        TxtNomeUtilizador.IsEnabled = false; // login não é editável após criação
        TxtNomeCompleto.Text = usuario.NomeCompleto;
        TxtEmail.Text = usuario.Email;
        CmbPerfil.SelectedItem = usuario.Perfil;
        ChkAtivo.IsChecked = usuario.Ativo;

        // Carregar avatar existente
        CarregarAvatarAtual(usuario.Id);
    }

    private void CarregarAvatarAtual(int usuarioId)
    {
        var avatar = AvatarService.CarregarAvatar(usuarioId);
        if (avatar != null)
        {
            AvatarPreview.Source = avatar;
            AvatarInitialsPreview.Visibility = Visibility.Collapsed;
            BtnRemoverAvatar.IsEnabled = true;
            _avatarAlterado = false;
            return;
        }

        // Mostrar iniciais
        AvatarInitialsPreview.Text = AvatarService.ObterIniciaisNome(TxtNomeCompleto.Text);
        AvatarInitialsPreview.Visibility = Visibility.Visible;
        AvatarPreview.Source = null;
        BtnRemoverAvatar.IsEnabled = false;
        _avatarAlterado = false;
    }

    private void CarregarAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os ficheiros (*.*)|*.*",
            Title = "Selecionar Avatar"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _caminhoAvatarTemporario = dialog.FileName;
            _avatarAlterado = true;

            // Carregar pré-visualização com URI absoluta em formato file://
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(_caminhoAvatarTemporario, UriKind.Absolute);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 200; // Limitar tamanho para performance
            bitmap.EndInit();
            bitmap.Freeze();

            AvatarPreview.Source = bitmap;
            AvatarInitialsPreview.Visibility = Visibility.Collapsed;
            BtnRemoverAvatar.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar a imagem: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            _avatarAlterado = false;
        }
    }

    private void RemoverAvatar_Click(object sender, RoutedEventArgs e)
    {
        AvatarPreview.Source = null;
        AvatarInitialsPreview.Text = AvatarService.ObterIniciaisNome(TxtNomeCompleto.Text);
        AvatarInitialsPreview.Visibility = Visibility.Visible;
        BtnRemoverAvatar.IsEnabled = false;
        _caminhoAvatarTemporario = null;
        _avatarAlterado = true; // Marcar como alterado (remoção)
    }

    private void TxtPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        AtualizarForcaPassword();
        AtualizarFeedbackConfirmacao();
    }

    private void TxtConfirmarPassword_PasswordChanged(object sender, RoutedEventArgs e) => AtualizarFeedbackConfirmacao();

    /// <summary>Atualiza a barra de força (5 segmentos) e a checklist de regras, em tempo real.</summary>
    private void AtualizarForcaPassword()
    {
        var resultado = PasswordPolicy.Validar(TxtPassword.Password);
        var cor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
            PasswordPolicy.CorParaNivel(resultado.TotalCumpridas));
        var brush = new System.Windows.Media.SolidColorBrush(cor);
        var vazio = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E5E7EB"));

        var segmentos = new[] { SegBar1, SegBar2, SegBar3, SegBar4, SegBar5 };
        for (var i = 0; i < segmentos.Length; i++)
            segmentos[i].Background = i < resultado.TotalCumpridas ? brush : vazio;

        void AtualizarRegra(System.Windows.Controls.TextBlock bloco, bool cumprida, string texto)
        {
            bloco.Text = (cumprida ? "✓  " : "○  ") + texto;
            bloco.Foreground = new System.Windows.Media.SolidColorBrush(cumprida
                ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#22C55E")
                : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#9CA3AF"));
        }

        AtualizarRegra(RegraComprimento, resultado.ComprimentoOk, "Mínimo de 8 caracteres");
        AtualizarRegra(RegraMaiuscula, resultado.MaiusculaOk, "Pelo menos uma letra maiúscula (A-Z)");
        AtualizarRegra(RegraMinuscula, resultado.MinusculaOk, "Pelo menos uma letra minúscula (a-z)");
        AtualizarRegra(RegraNumero, resultado.NumeroOk, "Pelo menos um número (0-9)");
        AtualizarRegra(RegraSimbolo, resultado.SimboloOk, "Pelo menos um símbolo (ex: ! @ # $ % *)");
    }

    /// <summary>Mostra se as duas palavras-passe introduzidas coincidem.</summary>
    private void AtualizarFeedbackConfirmacao()
    {
        if (string.IsNullOrEmpty(TxtPassword.Password) && string.IsNullOrEmpty(TxtConfirmarPassword.Password))
        {
            TxtFeedbackConfirmacao.Text = "";
            return;
        }

        if (string.IsNullOrEmpty(TxtConfirmarPassword.Password))
        {
            TxtFeedbackConfirmacao.Text = "Confirme a palavra-passe.";
            TxtFeedbackConfirmacao.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#9CA3AF")!;
            return;
        }

        var coincide = TxtPassword.Password == TxtConfirmarPassword.Password;
        TxtFeedbackConfirmacao.Text = coincide ? "✓ As palavras-passe coincidem." : "✗ As palavras-passe não coincidem.";
        TxtFeedbackConfirmacao.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
            .ConvertFrom(coincide ? "#22C55E" : "#EF4444")!;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNomeUtilizador.Text))
        {
            MessageBox.Show("O nome de utilizador (login) é obrigatório.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtNomeUtilizador.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtEmail.Text))
        {
            MessageBox.Show("O email é obrigatório. É para este endereço que será enviada a notificação de criação de conta.",
                "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtEmail.Focus();
            return;
        }

        if (!EmailService.EmailValido(TxtEmail.Text))
        {
            MessageBox.Show("O email introduzido não é válido. Verifique o endereço e tente novamente.",
                "Email inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtEmail.Focus();
            return;
        }

        if (_existente == null && string.IsNullOrWhiteSpace(TxtPassword.Password))
        {
            MessageBox.Show("A palavra-passe é obrigatória para um novo utilizador.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPassword.Focus();
            return;
        }

        // Só valida a palavra-passe se o utilizador estiver a defini-la ou a alterá-la
        // (na edição, deixar ambos os campos em branco mantém a palavra-passe atual).
        var pretendeDefinirPassword = !string.IsNullOrEmpty(TxtPassword.Password) || !string.IsNullOrEmpty(TxtConfirmarPassword.Password);
        if (pretendeDefinirPassword)
        {
            if (string.IsNullOrEmpty(TxtPassword.Password) || string.IsNullOrEmpty(TxtConfirmarPassword.Password))
            {
                MessageBox.Show("Preencha a palavra-passe e a respetiva confirmação.", "Dados incompletos",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TxtPassword.Password != TxtConfirmarPassword.Password)
            {
                MessageBox.Show("As duas palavras-passe introduzidas não coincidem. Verifique e tente novamente.",
                    "Palavras-passe diferentes", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var validacao = PasswordPolicy.Validar(TxtPassword.Password);
            if (!validacao.Valida)
            {
                MessageBox.Show(
                    "A palavra-passe não cumpre os requisitos mínimos de segurança.\n\n" +
                    "Tem de ter pelo menos 8 caracteres e incluir uma letra maiúscula, uma letra " +
                    "minúscula, um número e um símbolo (consulte a lista de regras na janela).",
                    "Palavra-passe fraca", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var duplicado = App.Db.Usuarios.Any(u =>
            (_existente == null || u.Id != _existente.Id) &&
            u.NomeUtilizador == TxtNomeUtilizador.Text.Trim());
        if (duplicado)
        {
            MessageBox.Show("Já existe um utilizador com esse nome de utilizador.", "Duplicado",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var eraNovoUtilizador = _existente == null;

        Usuario usuario;
        if (_existente == null)
        {
            usuario = new Usuario { NomeUtilizador = TxtNomeUtilizador.Text.Trim() };
            App.Db.Usuarios.Add(usuario);
            App.Db.SaveChanges(); // Salvar primeiro para obter o ID
        }
        else
        {
            usuario = App.Db.Usuarios.First(u => u.Id == _existente.Id);
        }

        usuario.NomeCompleto = TxtNomeCompleto.Text.Trim();
        usuario.Email = TxtEmail.Text;
        usuario.Perfil = (PerfilUtilizador)(CmbPerfil.SelectedItem ?? PerfilUtilizador.Utilizador);
        usuario.Ativo = ChkAtivo.IsChecked == true;

        if (!string.IsNullOrWhiteSpace(TxtPassword.Password))
        {
            var (hash, salt) = PasswordHasher.CriarHash(TxtPassword.Password);
            usuario.PasswordHash = hash;
            usuario.PasswordSalt = salt;
        }

        App.Db.SaveChanges();

        // Processar avatar
        if (_avatarAlterado)
        {
            if (_caminhoAvatarTemporario != null)
            {
                // Novo avatar foi carregado
                try
                {
                    AvatarService.GuardarAvatar(usuario.Id, _caminhoAvatarTemporario);
                    usuario.CaminhoAvatar = $"avatares/{usuario.Id}.png";
                    App.Db.SaveChanges();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Aviso: Utilizador guardado, mas falha ao guardar avatar: {ex.Message}",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (AvatarPreview.Source == null)
            {
                // Avatar foi removido
                AvatarService.RemoverAvatar(usuario.Id);
                usuario.CaminhoAvatar = null;
                App.Db.SaveChanges();
            }
        }

        // Enviar email de boas-vindas / notificação de criação de conta ao novo utilizador.
        if (eraNovoUtilizador && !string.IsNullOrWhiteSpace(usuario.Email))
        {
            EnviarEmailBoasVindas(usuario);
        }

        Sucesso = true;
        Close();
    }

    /// <summary>
    /// Envia o email de boas-vindas para o email introduzido, já validado quanto ao formato.
    /// Qualquer falha de envio (servidor não configurado, sem rede, credenciais inválidas, etc.)
    /// é reportada ao utilizador através de uma notificação, sem impedir a criação da conta.
    /// </summary>
    private void EnviarEmailBoasVindas(Usuario usuario)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            EmailService.EnviarEmailBoasVindas(
                usuario.Email!.Trim(),
                usuario.NomeCompleto,
                usuario.NomeUtilizador,
                usuario.Perfil.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "O utilizador foi criado com sucesso, mas não foi possível enviar o email de notificação para " +
                $"{usuario.Email}.\n\nMotivo: {ex.Message}",
                "Falha no envio do email", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
