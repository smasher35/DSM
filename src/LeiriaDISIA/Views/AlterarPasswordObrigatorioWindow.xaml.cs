using System.Windows;
using System.Windows.Input;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

/// <summary>
/// Janela de alteração obrigatória de password, apresentada pelo <see cref="LoginWindow"/>
/// imediatamente após uma autenticação bem-sucedida, sempre que <see cref="Usuario.PrecisaAlterarPassword"/>
/// estiver a true (ver "Repor Password" em <see cref="AdministracaoWindow"/>). Só fecha com
/// sucesso — não tem botão de Cancelar, e o fecho pela barra de título/Alt+F4 é bloqueado
/// enquanto a password não for alterada com sucesso (ver <see cref="Window_Closing"/>), para não
/// haver forma de contornar a obrigatoriedade.
/// </summary>
public partial class AlterarPasswordObrigatorioWindow : Window
{
    private readonly Usuario _usuario;
    private bool _passwordAlterada;

    public AlterarPasswordObrigatorioWindow(Usuario usuario)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _usuario = usuario;
        TxtNovaPassword.Focus();
    }

    private void TxtConfirmarNovaPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        TxtErro.Visibility = Visibility.Collapsed;

        var novaPassword = TxtNovaPassword.Password;
        var confirmacao = TxtConfirmarNovaPassword.Password;

        if (string.IsNullOrEmpty(novaPassword) || string.IsNullOrEmpty(confirmacao))
        {
            MostrarErro("Preencha os dois campos de password.");
            return;
        }

        if (novaPassword != confirmacao)
        {
            MostrarErro("As passwords introduzidas não são iguais.");
            return;
        }

        var validacao = PasswordPolicy.Validar(novaPassword);
        if (!validacao.Valida)
        {
            MostrarErro(
                "A password não cumpre os requisitos mínimos de segurança: mínimo de 8 caracteres, " +
                "com pelo menos uma maiúscula, uma minúscula, um número e um símbolo.");
            return;
        }

        try
        {
            // Recarrega o utilizador a partir do contexto partilhado (App.Db), tal como o resto da
            // aplicação faz, para gravar sobre a entidade realmente monitorizada pelo EF Core.
            var usuario = App.Db.Usuarios.First(u => u.Id == _usuario.Id);
            var (hash, salt) = PasswordHasher.CriarHash(novaPassword);
            usuario.PasswordHash = hash;
            usuario.PasswordSalt = salt;
            usuario.PrecisaAlterarPassword = false;
            App.Db.SaveChanges();

            _passwordAlterada = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MostrarErro($"Não foi possível gravar a nova password: {ex.Message}");
        }
    }

    private void MostrarErro(string mensagem)
    {
        TxtErro.Text = mensagem;
        TxtErro.Visibility = Visibility.Visible;
    }

    /// <summary>Impede fechar esta janela (barra de título, Alt+F4, etc.) antes de a password ter
    /// sido alterada com sucesso — é assim que a alteração se torna verdadeiramente "obrigatória",
    /// e não apenas uma sugestão que dá para ignorar fechando a janela.</summary>
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_passwordAlterada) return;

        e.Cancel = true;
        MostrarErro("Tem de definir uma nova password para poder continuar.");
    }
}
