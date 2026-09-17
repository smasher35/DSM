using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeiriaDISIA.Services.Rotas;

/// <summary>
/// Gera o PDF "Plano Diário de Rota — Pedidos de Intervenção", em A4 retrato. A tabela de paragens
/// junta "Distância" e "Duração" desde a paragem anterior numa só coluna e usa larguras relativas
/// mais apertadas do que a versão anterior em paisagem, precisamente para caber em retrato sem
/// texto cortado (as linhas crescem em altura conforme o texto quebra, graças ao MinHeight em vez
/// de altura fixa — ver células da tabela mais abaixo). Reutiliza a mesma infraestrutura QuestPDF
/// (licença Community) e a mesma identidade visual (logótipo, cores) do resto da aplicação.
/// </summary>
public class PlanoRotaPdfService
{
    public void GerarPdf(string caminhoDestino, PlanoRota plano, List<PlanoRotaParagem> paragensOrdenadas,
        byte[]? imagemMapa = null, List<PassoRota>? passosRota = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var corPrimaria = Colors.Blue.Darken2;
        var corAcento = Colors.Blue.Lighten2;
        var corTextoSub = Colors.Grey.Darken1;
        var branco = Colors.White;

        var nomeEquipa = plano.CriadoPorUsuario?.NomeCompleto ?? plano.CriadoPorUsuario?.NomeUtilizador;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.0f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(9).FontColor(Colors.Grey.Darken3));

                // ---- CABEÇALHO ----
                page.Header().Element(header =>
                {
                    header.Column(col =>
                    {
                        col.Item().PaddingBottom(6).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("MUNICÍPIO DE LEIRIA").FontSize(8).FontColor(corTextoSub).LetterSpacing(0.08f);
                                c.Item().Text("Plano Diário de Rota — Pedidos de Intervenção")
                                    .FontSize(16).Bold().FontColor(corPrimaria);
                                c.Item().Text("DISIA — Divisão de Sistemas de Informação e Aplicações")
                                    .FontSize(8).FontColor(corTextoSub).Italic();
                            });
                            row.ConstantItem(36).Height(36).Image(AppAssets.LogoDisia).FitArea();
                        });

                        col.Item().Height(2.5f).Background(corPrimaria);
                        col.Item().Height(1.2f).Background(corAcento);

                        // Larguras RELATIVAS (nunca ConstantItem em pontos fixos) — ver histórico da
                        // Folha de Inventário para o porquê: larguras fixas já causaram um layout
                        // impossível de resolver, que travava a geração do PDF indefinidamente.
                        col.Item().PaddingTop(8).Row(row =>
                        {
                            void Campo(string rotulo, string valor, float peso)
                            {
                                row.RelativeItem(peso).Row(rr =>
                                {
                                    rr.AutoItem().Text($"{rotulo}: ").FontSize(9).Bold();
                                    rr.RelativeItem().Text(valor).FontSize(9);
                                });
                            }

                            Campo("Data", plano.Data.ToString("dd/MM/yyyy"), 1.4f);
                            row.ConstantItem(10);
                            Campo("Hora de Partida", plano.HoraPartida.ToString(@"hh\:mm"), 1.4f);
                            row.ConstantItem(10);
                            Campo("Equipa/Técnico", string.IsNullOrWhiteSpace(nomeEquipa) ? "—" : nomeEquipa, 2.0f);
                            row.ConstantItem(10);
                            Campo("Limite de Horas", plano.LimiteHorasEquipa.HasValue ? $"{plano.LimiteHorasEquipa}h" : "Sem limite definido", 1.6f);
                        });
                    });
                });

                // ---- CONTEÚDO ----
                page.Content().PaddingTop(6).Column(mainCol =>
                {
                    // Resumo
                    mainCol.Item().PaddingBottom(10).Row(row =>
                    {
                        void Resumo(string valor, string rotulo, float peso)
                        {
                            row.RelativeItem(peso).Background("#F1F5F9").Padding(8).Column(c =>
                            {
                                c.Item().AlignCenter().Text(valor).FontSize(15).Bold().FontColor(corPrimaria);
                                c.Item().AlignCenter().Text(rotulo).FontSize(8).FontColor(corTextoSub);
                            });
                        }

                        Resumo(paragensOrdenadas.Count.ToString(), "Pedidos", 1f);
                        row.ConstantItem(6);
                        Resumo(paragensOrdenadas.Select(p => p.EscolaId).Distinct().Count().ToString(), "Escolas", 1f);
                        row.ConstantItem(6);
                        Resumo($"{plano.DistanciaTotalKm:0.#} km", "Distância Total", 1.2f);
                        row.ConstantItem(6);
                        Resumo($"{plano.DuracaoTotalMinutos / 60}h{plano.DuracaoTotalMinutos % 60:00}", "Duração Total Estimada", 1.4f);
                        row.ConstantItem(6);
                        Resumo("Sede do Município", "Partida", 1.6f);
                        row.ConstantItem(6);
                        Resumo(plano.PontoRegresso == EnderecoSedeMunicipio.Morada ? "Sede do Município" : "Sem regresso", "Regresso", 1.6f);
                    });

                    // Mapa com a rota traçada e as paragens assinaladas (captura do mesmo mapa já
                    // mostrado na pré-visualização — ver AtualizarMapaRotaAsync/CapturarMapaAsync em
                    // Views/PlanearRotaWindow.xaml.cs), com o resumo das estradas do trajeto ao lado
                    // direito (a pedido, para quem não conhece bem o território — ver
                    // ComposeResumoPassos). Qualquer um dos dois, em falta (captura do mapa falhou,
                    // ou a rota não tem troços com via mapeada), a secção correspondente
                    // simplesmente não aparece, sem deixar um espaço em branco por preencher; com
                    // os dois em falta, a linha toda desaparece. A agregação por estrada acontece já
                    // aqui (não dentro de ComposeResumoPassos) precisamente para esta verificação
                    // bater certo com o que lá é desenhado.
                    var estradasRota = passosRota != null ? AgruparPorEstrada(passosRota) : new();
                    if (imagemMapa != null || estradasRota.Count > 0)
                    {
                        mainCol.Item().PaddingBottom(10).Row(row =>
                        {
                            if (imagemMapa != null)
                            {
                                row.RelativeItem(1.1f).MaxHeight(220).Image(imagemMapa).FitArea();
                            }

                            if (estradasRota.Count > 0)
                            {
                                if (imagemMapa != null) row.ConstantItem(12);
                                row.RelativeItem(1.5f).Element(c => ComposeResumoPassos(c, estradasRota));
                            }
                        });
                    }

                    // Tabela de paragens
                    mainCol.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(0.4f);  // Ordem
                            cols.RelativeColumn(1.5f);  // Escola
                            cols.RelativeColumn(1.9f);  // Morada
                            cols.RelativeColumn(2.1f);  // Pedido / Descrição
                            cols.RelativeColumn(1.1f);  // Contacto
                            cols.RelativeColumn(1.1f);  // Dist./Duração desde anterior
                            cols.RelativeColumn(1.5f);  // Observações / confirmação manual
                        });

                        table.Header(h =>
                        {
                            void Cabecalho(string texto) =>
                                h.Cell().Background(corPrimaria).Padding(4).AlignMiddle()
                                    .Text(texto).FontSize(7).Bold().FontColor(branco);

                            Cabecalho("Nº");
                            Cabecalho("Escola");
                            Cabecalho("Morada");
                            Cabecalho("Pedido / Descrição");
                            Cabecalho("Contacto");
                            Cabecalho("Dist. / Duração");
                            Cabecalho("Observações / Confirmação");
                        });

                        for (var i = 0; i < paragensOrdenadas.Count; i++)
                        {
                            var paragem = paragensOrdenadas[i];
                            var pedido = paragem.PedidoIntervencao;
                            var escola = paragem.Escola;
                            var bg = i % 2 == 0 ? branco : Colors.Grey.Lighten5;

                            IContainer Cel(IContainer c) => c.Background(bg).Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                                .Padding(3).MinHeight(28).AlignMiddle();

                            var contacto = escola?.Contactos?.FirstOrDefault()?.Telefone
                                           ?? escola?.Contactos?.FirstOrDefault()?.Telemovel
                                           ?? escola?.Email;

                            table.Cell().Element(Cel).AlignCenter().Text(paragem.Ordem.ToString()).FontSize(8).Bold();
                            table.Cell().Element(Cel).Text(escola?.Nome ?? "—").FontSize(7.5f);
                            table.Cell().Element(Cel).Text(EscolaGeocodingService.MontarMoradaCompleta(escola!)).FontSize(7f);
                            table.Cell().Element(Cel).Text($"{pedido?.Razao}\n({pedido?.Solicitante})").FontSize(7f);
                            table.Cell().Element(Cel).Text(contacto ?? "—").FontSize(7f);
                            table.Cell().Element(Cel).AlignCenter().Text($"{paragem.DistanciaDesdeAnteriorKm:0.#} km\n{paragem.DuracaoDesdeAnteriorMinutos} min").FontSize(7.5f);
                            table.Cell().Element(Cel).Text("").FontSize(7.5f); // espaço em branco para preenchimento manual
                        }
                    });
                });

                // ---- RODAPÉ ----
                page.Footer().PaddingTop(4).Column(col =>
                {
                    col.Item().Height(1).Background(corAcento);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text($"DISIA — Município de Leiria  |  Gerado em {DateTime.Now:dd/MM/yyyy HH:mm}")
                            .FontSize(7).FontColor(corTextoSub).Italic();
                        row.ConstantItem(100).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(7).FontColor(corTextoSub));
                            t.Span("Pág. ");
                            t.CurrentPageNumber();
                            t.Span(" / ");
                            t.TotalPages();
                        });
                    });
                });
            });
        }).GeneratePdf(caminhoDestino);
    }

    /// <summary>Resumo simplificado da rota — só os números das estradas usadas em cada troço, com
    /// a distância percorrida em cada uma (ex.: "1. N109 — 3.2 km"), sem as instruções de navegação
    /// palavra-a-palavra ("vire à esquerda", etc.), que se revelaram detalhe a mais para o que se
    /// pretendia aqui. Recebe a lista já agregada por <see cref="AgruparPorEstrada"/> (chamado antes
    /// deste método, para a decisão de mostrar ou não esta secção bater certo com o que aqui é
    /// desenhado) — troços consecutivos da mesma via já vêm juntos numa só entrada, e troços sem
    /// nome de via mapeado (rotundas, acessos, etc.) já não constam. O essencial aqui é dar uma
    /// ideia das estradas principais do trajeto, não uma reconciliação exata da distância total
    /// (essa já está nos cartões de resumo acima). Vem da mesma chamada à API de Directions já
    /// feita para calcular as distâncias apresentadas na tabela de paragens (ver
    /// OpenRouteServiceClient.CalcularDistanciaAsync) — não é um pedido extra nem uma segunda fonte
    /// de dados que possa divergir da tabela.
    ///
    /// Duas colunas lado a lado em vez de uma única lista vertical — para uma rota com muitos
    /// troços de estrada (ex.: 40 entradas), uma só coluna estreita ficava demasiado alta e acabava
    /// por transbordar para a página seguinte, mesmo havendo largura de página de sobra à direita
    /// por preencher. Numeração contínua (1..metade na coluna esquerda, o resto na direita, nunca
    /// reiniciada a meio) — não é uma lista nova a começar, é a mesma lista só dividida visualmente
    /// ao meio.</summary>
    private static void ComposeResumoPassos(IContainer container, List<(string NomeVia, double DistanciaKm)> estradas)
    {
        var metade = (estradas.Count + 1) / 2; // arredondado para cima — a coluna esquerda fica com 1 a mais quando o total é ímpar
        var colunaEsquerda = estradas.Take(metade).ToList();
        var colunaDireita = estradas.Skip(metade).ToList();

        void ComposeColuna(ColumnDescriptor col, List<(string NomeVia, double DistanciaKm)> itens, int primeiroNumero)
        {
            col.Spacing(4);
            for (var i = 0; i < itens.Count; i++)
            {
                var (nomeVia, distanciaKm) = itens[i];
                col.Item().Row(linha =>
                {
                    linha.ConstantItem(16).Text($"{primeiroNumero + i}.").FontSize(8.5f).Bold().FontColor(Colors.Grey.Darken1);
                    linha.RelativeItem().Text(texto =>
                    {
                        texto.Span(nomeVia).FontSize(8.5f).FontColor(Colors.Grey.Darken3);
                        texto.Span($" — {distanciaKm:0.#} km").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                    });
                });
            }
        }

        container.Background("#F1F5F9").Padding(10).Column(col =>
        {
            col.Item().PaddingBottom(6).Text("Resumo da Rota").FontSize(10).Bold().FontColor(Colors.Blue.Darken2);

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c => ComposeColuna(c, colunaEsquerda, 1));
                row.ConstantItem(14);
                row.RelativeItem().Column(c => ComposeColuna(c, colunaDireita, metade + 1));
            });
        });
    }

    /// <summary>Junta troços consecutivos com o mesmo nome de via numa só entrada, somando as
    /// distâncias — ver <see cref="ComposeResumoPassos"/>. Descarta troços sem nome de via mapeado.</summary>
    private static List<(string NomeVia, double DistanciaKm)> AgruparPorEstrada(List<PassoRota> passos)
    {
        var resultado = new List<(string NomeVia, double DistanciaKm)>();

        foreach (var passo in passos)
        {
            if (string.IsNullOrWhiteSpace(passo.NomeVia)) continue;

            if (resultado.Count > 0 && resultado[^1].NomeVia == passo.NomeVia)
                resultado[^1] = (passo.NomeVia, resultado[^1].DistanciaKm + passo.DistanciaKm);
            else
                resultado.Add((passo.NomeVia, passo.DistanciaKm));
        }

        return resultado;
    }
}
