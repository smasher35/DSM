using System.Diagnostics;
using System.IO;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>
/// Orquestra o fluxo completo de "Repor Password" (ver Views/AdministracaoWindow.xaml.cs):
/// gera a password temporária, grava-a de forma segura, gera o documento PDF profissional com as
/// credenciais (ver <see cref="PasswordResetPdfService"/>) e entrega-o ao utilizador — por email,
/// se o servidor SMTP já estiver configurado (ver <see cref="AppSettingsService.SmtpConfigurado"/>),
/// ou, caso contrário (ou se o envio falhar), através do cliente de email externo do próprio
/// computador, com o documento pronto para ser anexado manualmente.
/// </summary>
public class ReporPasswordFluxoService
{
    public record Resultado(bool EnviadoPorEmail, string MensagemParaAdministrador);

    /// <summary>Executa o fluxo completo para o utilizador indicado. Não lança exceção para o
    /// chamador nas falhas previsíveis (SMTP em baixo, sem cliente de email instalado, etc.) — só
    /// nas falhas de gravação na base de dados, que o chamador (ver AdministracaoWindow) já trata
    /// com o seu próprio try/catch.</summary>
    public Resultado Executar(Usuario usuario)
    {
        var passwordTemporaria = GeradorPasswordTemporaria.Gerar();
        var (hash, salt) = PasswordHasher.CriarHash(passwordTemporaria);

        usuario.PasswordHash = hash;
        usuario.PasswordSalt = salt;
        usuario.PrecisaAlterarPassword = true;
        App.Db.SaveChanges();
        AuditoriaService.Registar("ReporPassword", "Sucesso", $"{usuario.NomeCompleto} ({usuario.NomeUtilizador})");

        // Pasta própria (não a pasta temporária "raiz" do Windows, partilhada por todos os
        // programas) para ser fácil de encontrar manualmente, caso seja preciso (ver o mecanismo
        // de recurso mais abaixo, que aponta para aqui).
        var pasta = Path.Combine(Path.GetTempPath(), "DISIA_Passwords_Temporarias");
        Directory.CreateDirectory(pasta);
        var caminhoPdf = Path.Combine(pasta,
            $"Password_{SanitizarNomeFicheiro(usuario.NomeUtilizador)}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        new PasswordResetPdfService().Gerar(usuario, passwordTemporaria, caminhoPdf);

        var temEmailValido = EmailService.EmailValido(usuario.Email);

        if (AppSettingsService.SmtpConfigurado && temEmailValido)
        {
            try
            {
                EmailService.EnviarEmailPasswordTemporaria(usuario.Email!, usuario.NomeCompleto, caminhoPdf);
                ApagarComSeguranca(caminhoPdf); // já foi entregue - não faz sentido deixar a cópia local
                return new Resultado(true,
                    $"A password foi reposta e o documento com as credenciais foi enviado por email para {usuario.Email}.");
            }
            catch (Exception ex)
            {
                // O envio por SMTP falhou (servidor em baixo, credenciais erradas, etc.) - cai no
                // mecanismo de recurso (mail externo) em vez de deixar o administrador sem forma
                // nenhuma de entregar a password ao utilizador.
                return AbrirMailExterno(usuario, caminhoPdf,
                    $"A password foi reposta, mas não foi possível enviar automaticamente o email (motivo: {ex.Message}).");
            }
        }

        var motivo = !temEmailValido
            ? "A password foi reposta, mas este utilizador não tem um email válido configurado, por isso não foi possível enviar automaticamente."
            : "A password foi reposta, mas o servidor de email (SMTP) ainda não está configurado em Configurações, por isso não foi possível enviar automaticamente.";

        return AbrirMailExterno(usuario, caminhoPdf, motivo);
    }

    /// <summary>Mecanismo de recurso quando não é possível enviar o email automaticamente:
    /// mantém o PDF gravado em disco (não o apaga, ao contrário do caminho de sucesso acima, pois
    /// o administrador ainda vai precisar dele), abre o cliente de email predefinido do computador
    /// com o destinatário/assunto/corpo já preenchidos (via um link "mailto:"), e abre o
    /// explorador de ficheiros com o PDF já selecionado, pronto a anexar manualmente.</summary>
    private static Resultado AbrirMailExterno(Usuario usuario, string caminhoPdf, string motivo)
    {
        try
        {
            if (EmailService.EmailValido(usuario.Email))
            {
                var assunto = Uri.EscapeDataString("As suas credenciais temporárias de acesso - Gestão DISIA");
                var corpo = Uri.EscapeDataString(
                    $"Boa tarde, {usuario.NomeCompleto},\n\nEm anexo encontra o documento com a sua password temporária de acesso à Gestão DISIA.\n\nCumprimentos,\nDISIA");
                var mailtoUrl = $"mailto:{usuario.Email}?subject={assunto}&body={corpo}";
                if (Uri.TryCreate(mailtoUrl, UriKind.Absolute, out var uri) && uri.Scheme == "mailto")
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{caminhoPdf}\"") { UseShellExecute = true });
        }
        catch
        {
            // Sem cliente de email/explorador de ficheiros disponível (pouco provável num Windows
            // normal, mas possível). O PDF continua gravado em caminhoPdf de qualquer forma — a
            // mensagem devolvida já indica esse caminho ao administrador.
        }

        return new Resultado(false,
            motivo + $"\n\nO documento com a password foi gravado em:\n{caminhoPdf}\n\n" +
            "Foi aberto o seu cliente de email (com o destinatário já preenchido, se o utilizador tiver email " +
            "configurado) e o explorador de ficheiros, com o documento já selecionado — anexe-o manualmente ao email.");
    }

    private static void ApagarComSeguranca(string caminho)
    {
        try { File.Delete(caminho); }
        catch { /* não crítico - o ficheiro fica na pasta temporária, sem impacto na utilização normal */ }
    }

    private static string SanitizarNomeFicheiro(string nome) =>
        string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
