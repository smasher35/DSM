using LeiriaDISIA.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services;

/// <summary>
/// Gera um documento PDF com a password temporária de um utilizador, para lhe ser entregue depois
/// de um "Repor Password" (ver Views/AdministracaoWindow.xaml.cs). Segue a mesma linguagem visual
/// (cores, tipografia, cabeçalho e rodapé) do Relatório de Intervenção e do Relatório Mensal de
/// Atividades — ver <see cref="IntervencaoPdfService"/> — para que todos os PDFs da aplicação
/// pareçam pertencer ao mesmo "produto".
///
/// Nota de segurança: este PDF contém uma password em texto simples. Isso é uma exceção deliberada
/// à regra geral da aplicação de nunca mostrar passwords (ver o email de boas-vindas em
/// <see cref="EmailService"/>, que nunca inclui a password escolhida) — mas é uma exceção
/// justificada aqui: a password é sempre TEMPORÁRIA, de utilização única, e o titular é obrigado a
/// substituí-la por uma da sua escolha no primeiro login seguinte (ver
/// <see cref="Views.AlterarPasswordObrigatorioWindow"/>), tal como pedido explicitamente para esta
/// funcionalidade. Ver <see cref="ReporPasswordFluxoService"/> para o ciclo de vida do ficheiro:
/// é apagado automaticamente assim que é enviado com sucesso por email; só fica gravado em disco
/// (numa pasta própria, dentro da pasta temporária do Windows) quando não foi possível enviá-lo
/// automaticamente, para o administrador o poder anexar manualmente a um email.
/// </summary>
public class PasswordResetPdfService
{
    private const string CorNavy = "#1F4E79";
    private const string CorNavyEscuro = "#16334D";
    private const string CorTeal = "#2AB7CA";
    private const string CorFundoCaixa = "#F4F6F9";
    private const string CorBorda = "#E2E8F0";
    private const string CorAviso = "#B45309";
    private const string CorFundoAviso = "#FEF3C7";

    static PasswordResetPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Gera o PDF em <paramref name="caminhoDestino"/> com as credenciais temporárias de
    /// <paramref name="usuario"/>.</summary>
    public void Gerar(Usuario usuario, string passwordTemporaria, string caminhoDestino)
    {
        var agora = DateTime.Now;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Element(c => ComposeCabecalho(c, agora));
                page.Content().PaddingTop(16).Column(col =>
                {
                    ComposeInfoUtilizador(col, usuario);

                    col.Item().PaddingTop(20);
                    ComposeCaixaPassword(col, passwordTemporaria);

                    col.Item().PaddingTop(20);
                    ComposeAvisoSeguranca(col);
                });
                page.Footer().Element(ComposeRodape);
            });
        }).GeneratePdf(caminhoDestino);
    }

    private static void ComposeCabecalho(IContainer container, DateTime agora)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(40).Height(40).Image(AppAssets.LogoDisia).FitArea();
                row.RelativeItem().PaddingLeft(12).Column(c =>
                {
                    c.Item().Text("MUNICÍPIO DE LEIRIA — DISIA").FontSize(8).Bold()
                        .FontColor(Colors.Grey.Darken1).LetterSpacing(0.06f);
                    c.Item().Text("Credenciais Temporárias de Acesso").FontSize(19).Bold().FontColor(CorNavy);
                });
                row.ConstantItem(150).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text("Gestão DISIA").FontSize(10).Bold().FontColor(CorNavyEscuro);
                    c.Item().AlignRight().Text(agora.ToString("dd 'de' MMMM 'de' yyyy"))
                        .FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                });
            });
            col.Item().PaddingTop(8).Height(3).Background(CorNavy);
            col.Item().Height(1.4f).Background(CorTeal);
            col.Item().PaddingBottom(4);
        });
    }

    private static void ComposeRodape(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Height(1).Background(Colors.Grey.Lighten2);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("Câmara Municipal de Leiria — DISIA").FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
                row.ConstantItem(160).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                    t.Span("Gerado em ");
                    t.Span(DateTime.Now.ToString("dd-MM-yyyy HH:mm"));
                });
            });
            col.Item().PaddingTop(1).AlignCenter().Text(
                "Documento confidencial — destinado exclusivamente ao titular da conta acima identificada.")
                .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
        });
    }

    private static void TituloSeccao(ColumnDescriptor col, string titulo, string corHex)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(4).Height(15).Background(corHex);
            row.RelativeItem().PaddingLeft(8).AlignMiddle().Text(titulo).FontSize(12.5f).Bold().FontColor(CorNavyEscuro);
        });
    }

    private static void ComposeInfoUtilizador(ColumnDescriptor col, Usuario usuario)
    {
        TituloSeccao(col, "Conta de Utilizador", CorNavy);
        col.Item().PaddingTop(6).Background(CorFundoCaixa).Border(1).BorderColor(CorBorda).Padding(14).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("NOME").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                c.Item().PaddingTop(1).Text(usuario.NomeCompleto).FontSize(11).Bold().FontColor(CorNavyEscuro);
                c.Item().PaddingTop(8).Text("PERFIL").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                c.Item().PaddingTop(1).Text(usuario.Perfil.ToString()).FontSize(11);
            });
            row.RelativeItem().Column(c =>
            {
                c.Item().Text("UTILIZADOR").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                c.Item().PaddingTop(1).Text(usuario.NomeUtilizador).FontSize(11).Bold().FontColor(CorNavyEscuro);
                c.Item().PaddingTop(8).Text("EMAIL").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f);
                c.Item().PaddingTop(1).Text(string.IsNullOrWhiteSpace(usuario.Email) ? "-" : usuario.Email).FontSize(11);
            });
        });
    }

    /// <summary>Caixa de destaque com a password, em tipografia monoespaçada e tamanho grande, para
    /// ser lida (ou transcrita à mão) sem ambiguidade — é o elemento central do documento.</summary>
    private static void ComposeCaixaPassword(ColumnDescriptor col, string passwordTemporaria)
    {
        TituloSeccao(col, "Password Temporária", CorTeal);
        col.Item().PaddingTop(6).Background(CorNavy).Padding(18).Column(c =>
        {
            c.Item().AlignCenter().Text("A SUA NOVA PASSWORD TEMPORÁRIA").FontSize(8).Bold()
                .FontColor(Colors.White).LetterSpacing(0.08f);
            c.Item().PaddingTop(8).AlignCenter().Text(passwordTemporaria)
                .FontFamily("Courier New").FontSize(22).Bold().FontColor(Colors.White).LetterSpacing(0.04f);
        });
    }

    private static void ComposeAvisoSeguranca(ColumnDescriptor col)
    {
        TituloSeccao(col, "Instruções", CorAviso);
        col.Item().PaddingTop(6).Background(CorFundoAviso).Border(1).BorderColor(CorAviso).Padding(14)
            .Column(c =>
            {
                void Ponto(string texto)
                {
                    c.Item().PaddingBottom(4).Row(r =>
                    {
                        r.ConstantItem(14).Text("•").FontSize(10).Bold().FontColor(CorAviso);
                        r.RelativeItem().Text(texto).FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                    });
                }

                Ponto("Esta password é TEMPORÁRIA e de utilização única — só é válida para o próximo login.");
                Ponto("No primeiro acesso com esta password, a aplicação vai pedir imediatamente para definir uma password nova, à sua escolha.");
                Ponto("A password anterior desta conta deixou de ser válida a partir do momento em que este documento foi gerado.");
                Ponto("Não reencaminhe nem partilhe este documento — destina-se exclusivamente ao titular da conta.");
                Ponto("Depois de definir a sua nova password, pode destruir este documento com segurança.");
            });
    }
}
