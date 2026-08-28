using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace LeiriaDISIA.Services;

/// <summary>
/// Termina a sessão automaticamente (fecha todas as janelas e volta ao ecrã de login, em qualquer
/// parte da aplicação) ao fim do tempo de inatividade configurado em Administração → Segurança
/// ("Sessão"). Desligado por omissão (<see cref="AppSettingsService.SessaoTerminarPorInatividade"/>).
///
/// Funciona com um único temporizador global (não um por janela): qualquer input do rato ou
/// teclado, em qualquer janela da aplicação, é apanhado por <see cref="InputManager.PreProcessInput"/>
/// (um evento a nível da aplicação inteira, não de uma janela específica) e reinicia a contagem.
/// </summary>
public static class SessaoInatividadeService
{
    private static DateTime _ultimaAtividade = DateTime.Now;
    private static DispatcherTimer? _temporizador;
    private static bool _iniciado;

    /// <summary>Chamado uma única vez, depois do primeiro login com sucesso (ver App.xaml.cs) —
    /// chamadas repetidas não têm efeito (o hook de atividade e o temporizador só arrancam da
    /// primeira vez). Continua a "correr" mesmo durante uma sessão terminada manualmente ou por
    /// inatividade, mas <see cref="VerificarInatividade"/> só atua quando existe mesmo uma sessão
    /// iniciada (<see cref="SessaoAtual.UtilizadorLogado"/> não nulo) - por isso não interfere com
    /// o próprio ecrã de login.</summary>
    public static void Iniciar()
    {
        if (_iniciado) return;
        _iniciado = true;

        RegistarAtividade();
        InputManager.Current.PreProcessInput += (_, _) => RegistarAtividade();

        // Verifica com uma frequência bem menor do que o mínimo de inatividade configurável (que
        // nunca deve fazer sentido definir abaixo de 1 minuto) - suficiente para reagir com uma
        // margem de poucos segundos, sem gastar recursos a verificar constantemente.
        _temporizador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _temporizador.Tick += (_, _) => VerificarInatividade();
        _temporizador.Start();
    }

    public static void RegistarAtividade() => _ultimaAtividade = DateTime.Now;

    private static void VerificarInatividade()
    {
        if (!AppSettingsService.SessaoTerminarPorInatividade) return;
        if (SessaoAtual.UtilizadorLogado == null) return; // já sem sessão (ecrã de login já aberto)

        var minutosInativo = (DateTime.Now - _ultimaAtividade).TotalMinutes;
        if (minutosInativo < AppSettingsService.SessaoMinutosInatividade) return;

        TerminarSessaoPorInatividade();
    }

    private static void TerminarSessaoPorInatividade()
    {
        // Reinicia já a contagem, antes de mais nada - evita disparar outra vez em sucessão
        // enquanto a janela de login (que também gera algum input) ainda está a abrir.
        RegistarAtividade();

        var nomeUtilizador = SessaoAtual.UtilizadorLogado?.NomeUtilizador;
        AuditoriaService.Registar("SessaoExpirada", "Sucesso",
            $"Sessão de \"{nomeUtilizador}\" terminada automaticamente por inatividade " +
            $"({AppSettingsService.SessaoMinutosInatividade} minuto(s)).", nomeUtilizador);

        SessaoAtual.Terminar();

        var principal = Application.Current.MainWindow;

        // Fecha todas as outras janelas abertas (edição de escolas, intervenções, relatórios,
        // etc.) - "em qualquer parte da aplicação", tal como pedido: a sessão termina mesmo que o
        // utilizador estivesse a meio de uma janela secundária, não só no ecrã principal.
        foreach (var janela in Application.Current.Windows.Cast<Window>().Where(w => w != principal).ToList())
        {
            try { janela.Close(); } catch { /* já pode estar a fechar por outra via */ }
        }

        if (principal == null) return;

        // Desfoca o conteúdo atual (pode conter dados sensíveis) antes de mostrar o ecrã de login
        // por cima - mesmo mecanismo já usado em MainWindow.TerminarSessao_Click.
        var blurAnterior = principal.Effect;
        principal.Effect = new BlurEffect { Radius = 35 };

        var login = new Views.LoginWindow { Owner = principal };
        var autenticado = login.ShowDialog();

        principal.Effect = blurAnterior;

        if (autenticado != true)
            Application.Current.Shutdown();

        // Se autenticado com sucesso, SessaoAtual.UtilizadorLogado já ficou definido dentro do
        // próprio LoginWindow - a janela principal existente é reaproveitada tal como estava
        // (ao contrário de "Terminar Sessão" manual, que a recria de raiz), o que é suficiente
        // para reganhar acesso à aplicação em segurança.
    }
}
