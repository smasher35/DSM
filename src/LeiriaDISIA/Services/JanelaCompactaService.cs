namespace LeiriaDISIA.Services;

/// <summary>
/// Permite alternar, a partir de Administração → Aparência, o "Modo Compacto": quando ativo, as
/// janelas de edição maiores (Escola, Equipamento, Intervenção, Atividade DISIA — ver
/// <see cref="JanelaTamanhoHelper"/>) ajustam o seu tamanho à área de trabalho realmente
/// disponível, em vez de usarem sempre o tamanho fixo original, pensado para monitores maiores.
///
/// Existe porque a aplicação corre em computadores com ecrãs muito diferentes (monitores normais
/// vs. portáteis pequenos, por vezes com escala do Windows a 125%/150%) — tal como o
/// <see cref="ThemeService"/> e o <see cref="DashboardResolucaoService"/>, a preferência é gravada
/// localmente por computador, não na base de dados.
/// </summary>
public static class JanelaCompactaService
{
    public static bool Ativo { get; private set; } = AppSettingsService.ModoCompactoAtivo;

    /// <summary>Aplica a preferência indicada e grava-a para sessões futuras. Não afeta janelas já
    /// abertas (o ajuste de tamanho só é feito uma vez, na abertura de cada janela) — apenas as
    /// próximas janelas de edição a abrir.</summary>
    public static void Aplicar(bool ativo)
    {
        Ativo = ativo;
        AppSettingsService.ModoCompactoAtivo = ativo;
    }
}
