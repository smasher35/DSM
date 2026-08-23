using System.Windows;

namespace LeiriaDISIA.Services;

/// <summary>
/// Ajuda janelas de edição com tamanho fixo grande (Escola, Equipamento, Intervenção, Atividade
/// DISIA — os quatro casos identificados com este problema) a caberem em ecrãs pequenos, como um
/// portátil de 13" a 125% de escala (área de trabalho efetiva de apenas ~1536×864 pixels lógicos,
/// menos ainda com a barra de tarefas).
///
/// Nesses casos, o tamanho fixo definido no XAML (pensado para um monitor normal) é maior do que o
/// ecrã disponível, e a janela nasce parcialmente fora do ecrã — tipicamente com o topo (botões de
/// fechar/minimizar) acima do limite visível e/ou os botões "Guardar"/"Cancelar" cortados no fundo,
/// sem forma óbvia de lá chegar.
///
/// Chamado a partir do construtor de cada janela afetada, DEPOIS de InitializeComponent() (para já
/// existirem os valores de Width/Height/MinWidth/MinHeight definidos no XAML a ajustar). Só atua
/// quando <see cref="JanelaCompactaService.Ativo"/> está ligado E a janela não cabe no ecrã atual —
/// em monitores normais/grandes, ou com o modo desativado, não faz qualquer alteração.
/// </summary>
public static class JanelaTamanhoHelper
{
    /// <summary>Margem de segurança (pixels) subtraída à área de trabalho, para sobrar espaço para
    /// a moldura da janela e não ficar "à justa" com o limite exato do ecrã.</summary>
    private const double Margem = 24;

    public static void AjustarSePreciso(Window janela)
    {
        if (!JanelaCompactaService.Ativo) return;

        var area = SystemParameters.WorkArea;
        var larguraDisponivel = area.Width - Margem;
        var alturaDisponivel = area.Height - Margem;

        var precisaAjuste = janela.Width > larguraDisponivel || janela.Height > alturaDisponivel;
        if (!precisaAjuste) return;

        if (janela.Width > larguraDisponivel)
            janela.Width = larguraDisponivel;
        if (janela.Height > alturaDisponivel)
            janela.Height = alturaDisponivel;

        // O tamanho mínimo (usado ao redimensionar manualmente) nunca pode ficar maior do que o
        // tamanho que acabámos de aplicar, ou o utilizador não conseguiria voltar a encolher a
        // janela para caber no ecrã depois de a esticar.
        if (janela.MinWidth > janela.Width)
            janela.MinWidth = janela.Width;
        if (janela.MinHeight > janela.Height)
            janela.MinHeight = janela.Height;

        // Centra explicitamente no ecrã (em vez de confiar em CenterOwner, que poderia herdar uma
        // posição do "pai" incompatível com o novo tamanho, mais pequeno).
        janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
