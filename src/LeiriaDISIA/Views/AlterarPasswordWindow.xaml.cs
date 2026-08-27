using System.Windows;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class AlterarPasswordWindow : Window
{
    private readonly Usuario _usuario;

    public AlterarPasswordWindow(Usuario usuario)
    {
        InitializeComponent();
        _usuario = usuario;

        // Força o tema claro para a janela (consistente com LoginWindow)
        var dicionarios = Resources.MergedDictionaries;
        for (var i = dicionarios.Count - 1; i >= 0; i--)
        {
            var origem = dicionarios[i].Source?.OriginalString ?? "";
            if (origem.EndsWith("ModernTheme.xaml") || origem.EndsWith("DarkTheme.xaml"))
                dicionarios.RemoveAt(i);
        }

        var lightThemeDictionary = new ResourceDictionary
        {
            Source = new Uri("Themes/ModernTheme.xaml", UriKind.Relative)
        };
        dicionarios.Add(lightThemeDictionary);

        TxtNovaPassword.Focus();
    }

    private void TxtConfirmarPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Alterar_Click(sender, e);
    }

    private void Alterar_Click(object sender, RoutedEventArgs e)
    {
        TxtErro.Visibility = Visibility.Collapsed;

        var novaPassword = TxtNovaPassword.Password;
        var confirmarPassword = TxtConfirmarPassword.Password;

        // Validações
        if (string.IsNullOrWhiteSpace(novaPassword))
        {
            MostrarErro("A nova palavra-passe não pode estar vazia.");
            return;
        }

        if (novaPassword.Length < 8)
        {
            MostrarErro("A palavra-passe deve ter pelo menos 8 caracteres.");
            return;
        }

        if (novaPassword != confirmarPassword)
        {
            MostrarErro("As palavras-passe não coincidem.");
            return;
        }

        // Não permitir a palavra-passe padrão
        if (novaPassword == "admin123")
        {
            MostrarErro("Não pode usar a palavra-passe padrão. Escolha uma palavra-passe diferente.");
            return;
        }

        // Atualizar a palavra-passe
        var (hash, salt) = PasswordHasher.CriarHash(novaPassword);
        _usuario.PasswordHash = hash;
        _usuario.PasswordSalt = salt;
        _usuario.RequerAlteracaoPassword = false;

        try
        {
            App.Db.SaveChanges();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao alterar a palavra-passe: {ex.Message}");
        }
    }

    private void MostrarErro(string mensagem)
    {
        TxtErro.Text = mensagem;
        TxtErro.Visibility = Visibility.Visible;
    }
}
