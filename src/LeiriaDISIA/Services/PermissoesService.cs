using System.Windows;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace LeiriaDISIA.Services;

/// <summary>
/// Ponto único onde cada ecrã aplica a restrição de "só leitura" do perfil Guest (ver
/// <see cref="SessaoAtual.PodeEditar"/>) — em vez de cada janela repetir a mesma verificação
/// "if (SessaoAtual.IsGuest) { botao.IsEnabled = false; ... }" para cada botão, chama-se aqui uma
/// única vez, no construtor, com os botões de inserir/editar/eliminar/etc. desse ecrã em concreto.
/// </summary>
public static class PermissoesService
{
    /// <summary>Desativa os botões indicados (tipicamente "Inserir X", "Editar X Selecionado",
    /// "Eliminar X Selecionado", "Importar...") quando a sessão atual é Guest — sem qualquer efeito
    /// para Administrador ou Utilizador. Acrescenta também uma dica (tooltip) a explicar o motivo,
    /// para não parecer só um botão avariado.</summary>
    public static void AplicarSomenteLeituraSeGuest(params ButtonBase[] botoes)
    {
        if (!SessaoAtual.IsGuest) return;

        foreach (var botao in botoes)
        {
            botao.IsEnabled = false;
            botao.ToolTip = "Não disponível para o perfil Guest (acesso só de leitura).";
        }
    }

    /// <summary>Variante para janelas de edição (ex.: EscolaEditWindow) que, no seu conjunto, não
    /// devem sequer poder ser abertas em modo de edição/criação por um Guest — fecha a janela
    /// imediatamente após ser construída, com um aviso, em vez de a deixar aberta só para o
    /// utilizador descobrir mais tarde que não consegue gravar nada. Chamar logo a seguir a
    /// InitializeComponent(), no construtor da janela.</summary>
    public static bool BloquearAberturaSeGuest(Window janela)
    {
        if (!SessaoAtual.IsGuest) return false;

        MessageBox.Show(
            "O seu perfil (Guest) só tem acesso de consulta — não é possível criar, editar ou eliminar registos.",
            "Acesso só de leitura", MessageBoxButton.OK, MessageBoxImage.Information);

        janela.Loaded += (_, _) => janela.Close();
        return true;
    }
}
