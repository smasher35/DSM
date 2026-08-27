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
        if (utilizador == null || !utilizador.Ativo ||
            !PasswordHasher.Validar(password, utilizador.PasswordHash, utilizador.PasswordSalt))
        {
            MostrarErro("Utilizador ou palavra-passe inválidos.");
            return;
        }

        // Verificar se é necessário alterar a palavra-passe
        if (utilizador.RequerAlteracaoPassword)
        {
            var alterarPasswordWindow = new AlterarPasswordWindow(utilizador) { Owner = this };
            var alterado = alterarPasswordWindow.ShowDialog();

            if (alterado != true)
            {
                MostrarErro("Deve alterar a palavra-passe para continuar.");
                return;
            }
        }

        utilizador.UltimoLogin = DateTime.Now;
        App.Db.SaveChanges();

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
