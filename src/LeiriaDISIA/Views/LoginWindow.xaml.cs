using System.Windows;
using System.Windows.Input;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;

namespace LeiriaDISIA.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();

        // Força o tema claro para a janela de login (independentemente do tema global)
        // Remove qualquer dicionário de tema que possa ter herdado
        var dicionarios = Resources.MergedDictionaries;
        for (var i = dicionarios.Count - 1; i >= 0; i--)
        {
            var origem = dicionarios[i].Source?.OriginalString ?? "";
            if (origem.EndsWith("ModernTheme.xaml") || origem.EndsWith("DarkTheme.xaml"))
                dicionarios.RemoveAt(i);
        }

        // Adiciona apenas o tema claro
        var lightThemeDictionary = new ResourceDictionary
        {
            Source = new Uri("Themes/ModernTheme.xaml", UriKind.Relative)
        };
        dicionarios.Add(lightThemeDictionary);

        TxtUtilizador.Focus();
    }

    private void TxtPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Entrar_Click(sender, e);
    }

    private void Entrar_Click(object sender, RoutedEventArgs e)
    {
        TxtErro.Visibility = Visibility.Collapsed;

        var nomeUtilizador = TxtUtilizador.Text.Trim();
        var password = TxtPassword.Password;

        if (string.IsNullOrWhiteSpace(nomeUtilizador) || string.IsNullOrWhiteSpace(password))
        {
            MostrarErro("Indique o utilizador e a palavra-passe.");
            return;
        }

        var utilizador = App.Db.Usuarios.FirstOrDefault(u => u.NomeUtilizador == nomeUtilizador);

        // Cada motivo de falha é registado em Auditoria com um detalhe diferente (ver
        // Administração → Auditoria) - "utilizador" no registo é sempre o nome introduzido no
        // formulário, mesmo quando não corresponde a nenhuma conta real, para se conseguir ver em
        // auditoria tentativas de login com nomes de utilizador inexistentes.
        if (utilizador == null)
        {
            AuditoriaService.Registar("Login", "Falha", "Utilizador não encontrado.", nomeUtilizador);
            MostrarErro("Utilizador ou palavra-passe inválidos.");
            return;
        }

        if (!utilizador.Ativo)
        {
            AuditoriaService.Registar("Login", "Falha", "Conta inativa/bloqueada.", nomeUtilizador);
            MostrarErro("Utilizador ou palavra-passe inválidos.");
            return;
        }

        if (!PasswordHasher.Validar(password, utilizador.PasswordHash, utilizador.PasswordSalt))
        {
            // Bloqueio automático (Administração → Segurança, "Tentativas de Login") - reutiliza o
            // campo Ativo já existente (0 = desativado): ao atingir o limite configurado, a conta
            // fica Inativa e só um administrador a pode reativar, em Administração → Utilizadores.
            utilizador.TentativasFalhadasConsecutivas++;
            var limite = AppSettingsService.TentativasLoginMaximo;
            var detalheFalha = "Password incorreta.";

            if (limite > 0 && utilizador.TentativasFalhadasConsecutivas >= limite)
            {
                utilizador.Ativo = false;
                detalheFalha += $" Conta bloqueada automaticamente após {utilizador.TentativasFalhadasConsecutivas} tentativas falhadas consecutivas.";
            }

            App.Db.SaveChanges();
            AuditoriaService.Registar("Login", "Falha", detalheFalha, nomeUtilizador);
            MostrarErro("Utilizador ou palavra-passe inválidos.");
            return;
        }

        // "Repor Password" (Administração → Utilizadores) marca a conta assim - a autenticação em
        // si já foi validada acima com a password temporária, mas o acesso normal só é dado depois
        // de o próprio utilizador a substituir por uma da sua escolha (ver
        // AlterarPasswordObrigatorioWindow, que bloqueia o fecho até isso acontecer).
        if (utilizador.PrecisaAlterarPassword)
        {
            var janelaAlterarPassword = new AlterarPasswordObrigatorioWindow(utilizador) { Owner = this };
            janelaAlterarPassword.ShowDialog();
            // ShowDialog só retorna quando a password foi alterada com sucesso (a janela impede o
            // fecho por qualquer outra via - ver o Closing dessa janela), por isso não é preciso
            // verificar aqui o resultado nem interromper o login.
        }

        utilizador.UltimoLogin = DateTime.Now;
        utilizador.TentativasFalhadasConsecutivas = 0;
        App.Db.SaveChanges();
        AuditoriaService.Registar("Login", "Sucesso", utilizador: nomeUtilizador);

        SessaoAtual.UtilizadorLogado = utilizador;
        DialogResult = true;
        Close();
    }

    private void MostrarErro(string mensagem)
    {
        TxtErro.Text = mensagem;
        TxtErro.Visibility = Visibility.Visible;
    }
}
