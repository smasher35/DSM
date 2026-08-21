using System.IO;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace LeiriaDISIA.Services;

/// <summary>Pequeno ajudante que expõe um SKCanvas em bruto dentro de um documento QuestPDF,
/// seguindo o padrão documentado oficialmente pelo QuestPDF para integração com SkiaSharp
/// (desenha para um SKSvgCanvas e devolve o SVG resultante como conteúdo vetorial do documento).
/// Usado apenas para o gráfico circular (pie chart) do Relatório Mensal de Atividades, que não
/// tem um elemento nativo equivalente na API fluente do QuestPDF.</summary>
public static class SkiaSharpHelpers
{
    public static void SkiaSharpSvgCanvas(this IContainer container, Action<SKCanvas, QuestPDF.Infrastructure.Size> drawOnCanvas)
    {
        container.Svg(size =>
        {
            using var stream = new MemoryStream();
            using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, size.Width, size.Height), stream))
                drawOnCanvas(canvas, size);

            var svgData = stream.ToArray();
            return Encoding.UTF8.GetString(svgData);
        });
    }
}
