using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace LeiriaDISIA.Services;

/// <summary>
/// Serviço responsável pelo envio de emails da aplicação (ex: notificação de criação de conta).
/// Usa as configurações de SMTP definidas em Configurações (<see cref="AppSettingsService"/>).
/// </summary>
public static class EmailService
{
    // Validação simples e robusta de formato de email (não substitui confirmação real da caixa de correio).
    private static readonly Regex RegexEmail = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool EmailValido(string? email) =>
        !string.IsNullOrWhiteSpace(email) && RegexEmail.IsMatch(email.Trim());

    /// <summary>
    /// Envia o email de boas-vindas / criação de conta ao novo utilizador.
    /// Lança exceção em caso de falha - deve ser chamado dentro de um try/catch pelo chamador.
    /// </summary>
    public static void EnviarEmailBoasVindas(string emailDestino, string nomeCompleto, string nomeUtilizador, string perfil)
    {
        if (!AppSettingsService.SmtpConfigurado)
            throw new InvalidOperationException(
                "O servidor de email (SMTP) ainda não está configurado. Aceda a Configurações > Email para o configurar.");

        var assunto = "A sua conta na Gestão DISIA foi criada";
        var corpoHtml = ConstruirHtmlBoasVindas(nomeCompleto, nomeUtilizador, perfil);

        using var mensagem = new MailMessage
        {
            From = new MailAddress(AppSettingsService.SmtpEmailRemetente, AppSettingsService.SmtpNomeRemetente),
            Subject = assunto,
            Body = corpoHtml,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        mensagem.To.Add(new MailAddress(emailDestino));

        using var cliente = new SmtpClient(AppSettingsService.SmtpServidor, AppSettingsService.SmtpPorta)
        {
            EnableSsl = AppSettingsService.SmtpUsarSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(AppSettingsService.SmtpUtilizador))
        {
            cliente.Credentials = new NetworkCredential(
                AppSettingsService.SmtpUtilizador, AppSettingsService.SmtpPassword);
        }

        cliente.Send(mensagem);
    }

    /// <summary>Envia um email de teste simples, usado no botão "Testar Ligação" das Configurações.</summary>
    public static void EnviarEmailTeste(string emailDestino)
    {
        if (!AppSettingsService.SmtpConfigurado)
            throw new InvalidOperationException("Preencha e guarde primeiro os dados do servidor SMTP.");

        using var mensagem = new MailMessage
        {
            From = new MailAddress(AppSettingsService.SmtpEmailRemetente, AppSettingsService.SmtpNomeRemetente),
            Subject = "Teste de configuração de email - Gestão DISIA",
            Body = ConstruirHtmlTeste(),
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        mensagem.To.Add(new MailAddress(emailDestino));

        using var cliente = new SmtpClient(AppSettingsService.SmtpServidor, AppSettingsService.SmtpPorta)
        {
            EnableSsl = AppSettingsService.SmtpUsarSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(AppSettingsService.SmtpUtilizador))
        {
            cliente.Credentials = new NetworkCredential(
                AppSettingsService.SmtpUtilizador, AppSettingsService.SmtpPassword);
        }

        cliente.Send(mensagem);
    }

    /// <summary>Envia por email o documento PDF com a password temporária gerada por "Repor
    /// Password" (ver <see cref="ReporPasswordFluxoService"/>), como anexo. Lança exceção em caso
    /// de falha — deve ser chamado dentro de um try/catch pelo chamador (que já trata essa falha,
    /// caindo no mecanismo de recurso de mail externo).</summary>
    public static void EnviarEmailPasswordTemporaria(string emailDestino, string nomeCompleto, string caminhoPdf)
    {
        if (!AppSettingsService.SmtpConfigurado)
            throw new InvalidOperationException(
                "O servidor de email (SMTP) ainda não está configurado. Aceda a Configurações > Email para o configurar.");

        using var mensagem = new MailMessage
        {
            From = new MailAddress(AppSettingsService.SmtpEmailRemetente, AppSettingsService.SmtpNomeRemetente),
            Subject = "As suas credenciais temporárias de acesso — Gestão DISIA",
            Body = ConstruirHtmlPasswordTemporaria(nomeCompleto),
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        mensagem.To.Add(new MailAddress(emailDestino));
        mensagem.Attachments.Add(new Attachment(caminhoPdf, "application/pdf") { Name = "Credenciais_Temporarias.pdf" });

        using var cliente = new SmtpClient(AppSettingsService.SmtpServidor, AppSettingsService.SmtpPorta)
        {
            EnableSsl = AppSettingsService.SmtpUsarSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(AppSettingsService.SmtpUtilizador))
        {
            cliente.Credentials = new NetworkCredential(
                AppSettingsService.SmtpUtilizador, AppSettingsService.SmtpPassword);
        }

        cliente.Send(mensagem);
    }

    private static string ConstruirHtmlPasswordTemporaria(string nomeCompleto)
    {
        var primeiroNome = string.IsNullOrWhiteSpace(nomeCompleto) ? "Utilizador" : nomeCompleto.Split(' ')[0];
        return $"""
            <div style="font-family: Segoe UI, Arial, sans-serif; color: #1f2937; max-width: 560px;">
                <p>Boa tarde, {primeiroNome},</p>
                <p>Foi reposta a sua password de acesso à <strong>Gestão DISIA</strong>. Em anexo
                (documento PDF) encontra a sua nova password temporária, e as instruções para a
                alterar no seu próximo login.</p>
                <p style="color: #6b7280; font-size: 12.5px;">Por motivos de segurança, a password
                não é apresentada no corpo deste email — consulte o documento em anexo.</p>
                <p>Cumprimentos,<br/>DISIA — Divisão de Sistemas de Informação, Câmara Municipal de Leiria</p>
            </div>
            """;
    }

    private static string ConstruirHtmlBoasVindas(string nomeCompleto, string nomeUtilizador, string perfil)
    {
        var primeiroNome = string.IsNullOrWhiteSpace(nomeCompleto)
            ? nomeUtilizador
            : nomeCompleto.Trim().Split(' ')[0];

        return $$"""
        <!DOCTYPE html>
        <html lang="pt">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        </head>
        <body style="margin:0;padding:0;background-color:#F4F6F9;font-family:'Segoe UI',Arial,sans-serif;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#F4F6F9;padding:32px 0;">
            <tr>
              <td align="center">
                <table role="presentation" width="560" cellpadding="0" cellspacing="0"
                       style="background-color:#FFFFFF;border-radius:8px;overflow:hidden;box-shadow:0 2px 10px rgba(0,0,0,0.06);">

                  <!-- Cabeçalho -->
                  <tr>
                    <td style="background:linear-gradient(135deg,#1F4E79,#2AB7CA);padding:28px 32px;">
                      <p style="margin:0;color:#C7D8E8;font-size:12px;letter-spacing:1.5px;text-transform:uppercase;">
                        Câmara Municipal de Leiria
                      </p>
                      <h1 style="margin:6px 0 0;color:#FFFFFF;font-size:22px;font-weight:600;">
                        Gestão DISIA
                      </h1>
                    </td>
                  </tr>

                  <!-- Corpo -->
                  <tr>
                    <td style="padding:32px;">
                      <p style="margin:0 0 16px;color:#1E293B;font-size:15px;line-height:1.6;">
                        Olá <strong>{{primeiroNome}}</strong>,
                      </p>
                      <p style="margin:0 0 20px;color:#1E293B;font-size:15px;line-height:1.6;">
                        A sua conta de acesso à aplicação <strong>Gestão DISIA</strong> foi criada com sucesso.
                        Já pode iniciar sessão utilizando as suas credenciais.
                      </p>

                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                             style="background-color:#F8FAFC;border:1px solid #E2E8F0;border-radius:6px;margin:0 0 24px;">
                        <tr>
                          <td style="padding:16px 20px;">
                            <p style="margin:0 0 8px;color:#64748B;font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:0.5px;">
                              Dados da conta
                            </p>
                            <p style="margin:0 0 4px;color:#1E293B;font-size:14px;">
                              <strong>Nome de utilizador:</strong> {{nomeUtilizador}}
                            </p>
                            <p style="margin:0 0 4px;color:#1E293B;font-size:14px;">
                              <strong>Nome completo:</strong> {{nomeCompleto}}
                            </p>
                            <p style="margin:0;color:#1E293B;font-size:14px;">
                              <strong>Perfil de acesso:</strong> {{perfil}}
                            </p>
                          </td>
                        </tr>
                      </table>

                      <p style="margin:0 0 20px;color:#1E293B;font-size:14px;line-height:1.6;">
                        Por motivos de segurança, a palavra-passe definida não é incluída neste email.
                        Caso tenha dúvidas relativamente ao acesso, contacte o administrador da aplicação.
                      </p>

                      <p style="margin:0;color:#94A3B8;font-size:12px;line-height:1.6;">
                        Se não estava à espera deste email, ou não solicitou a criação desta conta,
                        por favor ignore esta mensagem ou contacte a DISIA.
                      </p>
                    </td>
                  </tr>

                  <!-- Rodapé -->
                  <tr>
                    <td style="background-color:#F8FAFC;border-top:1px solid #E2E8F0;padding:18px 32px;">
                      <p style="margin:0;color:#94A3B8;font-size:11px;line-height:1.6;">
                        Este é um email automático enviado pela aplicação Gestão DISIA. Não responda a esta mensagem.<br />
                        © {{DateTime.Now.Year}} Câmara Municipal de Leiria — DISIA
                      </p>
                    </td>
                  </tr>

                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
    }

    private static string ConstruirHtmlTeste() => $$"""
        <!DOCTYPE html>
        <html lang="pt">
        <body style="margin:0;padding:0;background-color:#F4F6F9;font-family:'Segoe UI',Arial,sans-serif;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#F4F6F9;padding:32px 0;">
            <tr>
              <td align="center">
                <table role="presentation" width="480" cellpadding="0" cellspacing="0"
                       style="background-color:#FFFFFF;border-radius:8px;overflow:hidden;box-shadow:0 2px 10px rgba(0,0,0,0.06);">
                  <tr>
                    <td style="background:linear-gradient(135deg,#1F4E79,#2AB7CA);padding:24px 28px;">
                      <h1 style="margin:0;color:#FFFFFF;font-size:18px;font-weight:600;">Gestão DISIA</h1>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:28px;">
                      <p style="margin:0;color:#1E293B;font-size:14px;line-height:1.6;">
                        Este é um email de teste. Se o recebeu, a configuração de SMTP está correta.
                      </p>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
}
