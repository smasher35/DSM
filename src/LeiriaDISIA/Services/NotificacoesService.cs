using LeiriaDISIA.Data;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>Nível de urgência de uma notificação, usado para escolher a cor do indicador.</summary>
public enum SeveridadeNotificacao { Info, Aviso, Urgente }

/// <summary>Uma situação pendente detetada na aplicação (ex.: computadores por entregar,
/// pedidos há muito tempo em aberto). Ver <see cref="NotificacoesService"/>.</summary>
public class NotificacaoItem
{
    public string Mensagem { get; set; } = string.Empty;
    public SeveridadeNotificacao Severidade { get; set; } = SeveridadeNotificacao.Info;
    /// <summary>Nome do módulo a abrir quando o utilizador clica na notificação (ver
    /// MainWindow.Nav_Checked — "Recolhido", "Pedidos", "Disia", etc.).</summary>
    public string? Modulo { get; set; }

    public string CorHex => Severidade switch
    {
        SeveridadeNotificacao.Urgente => "#EF4444",
        SeveridadeNotificacao.Aviso => "#F59E0B",
        _ => "#3B82F6"
    };

    public string Icone => Severidade switch
    {
        SeveridadeNotificacao.Urgente => "🔴",
        SeveridadeNotificacao.Aviso => "🟠",
        _ => "🔵"
    };
}

/// <summary>
/// (8.1) Analisa o estado atual da aplicação e produz uma lista de lembretes sobre situações
/// pendentes que precisam de atenção — computadores prontos mas por entregar, pedidos há muito
/// tempo em aberto, equipamento recolhido há muito tempo sem resolução, atividades/intervenções
/// pendentes, etc.
///
/// Optou-se por um centro de notificações interno (sino na barra de estado, com contador) em vez
/// de notificações nativas do Windows (toasts). Motivos: (1) não é preciso registar a aplicação
/// como "AppUserModelID" nem adicionar dependências novas ao projeto para funcionar de forma
/// fiável; (2) fica sempre visível e consultável a qualquer momento, sem depender de o
/// Windows/anti-vírus não bloquear notificações da app; (3) permite navegar diretamente para o
/// módulo relevante ao clicar; (4) não interrompe o utilizador com popups — fica disponível mas
/// discreto, conforme pedido ("sem ser demasiado intrusivo").
/// </summary>
public class NotificacoesService
{
    private readonly AppDbContext _db;
    public NotificacoesService(AppDbContext db) => _db = db;

    /// <summary>Dias em aberto a partir dos quais um pedido é considerado urgente (mesmo limiar
    /// visual usado na legenda de "Tempo em Aberto" do módulo Pedidos).</summary>
    private const int DiasPedidoUrgente = 21;
    private const int DiasPedidoAviso = 7;

    /// <summary>Dias a partir dos quais equipamento recolhido e ainda não entregue/tratado passa
    /// a gerar aviso (evita alertar logo no dia seguinte à recolha).</summary>
    private const int DiasRecolhidoAviso = 10;

    public List<NotificacaoItem> Gerar()
    {
        var itens = new List<NotificacaoItem>();
        var hoje = DateTime.Today;

        // Computadores já reparados/prontos mas ainda por entregar à escola.
        var aguardamEntrega = _db.Equipamentos
            .Where(e => e.Estado == EstadosEquipamento.AguardaEntrega)
            .Select(e => e.Tipo).ToList()
            .Count(tipo => tipo != null && (
                tipo.Contains("Computador", StringComparison.OrdinalIgnoreCase) ||
                tipo.Contains("Portátil", StringComparison.OrdinalIgnoreCase) ||
                tipo.Contains("Servidor", StringComparison.OrdinalIgnoreCase)));
        if (aguardamEntrega > 0)
        {
            itens.Add(new NotificacaoItem
            {
                Severidade = SeveridadeNotificacao.Urgente,
                Modulo = "Equipamentos",
                Mensagem = aguardamEntrega == 1
                    ? "1 computador está reparado e aguarda entrega à escola."
                    : $"{aguardamEntrega} computadores estão reparados e aguardam entrega às escolas."
            });
        }

        // Equipamento recolhido há muito tempo sem ser entregue de volta.
        var recolhidosDemorados = _db.EquipamentosRecolhidos
            .Where(r => r.DataEntrega == null)
            .ToList()
            .Count(r => (hoje - r.DataRecolha).TotalDays >= DiasRecolhidoAviso);
        if (recolhidosDemorados > 0)
        {
            itens.Add(new NotificacaoItem
            {
                Severidade = SeveridadeNotificacao.Aviso,
                Modulo = "Recolhido",
                Mensagem = $"{recolhidosDemorados} equipamento(s) recolhido(s) há mais de {DiasRecolhidoAviso} dias, ainda sem entrega/resolução."
            });
        }

        // Pedidos de intervenção há muito tempo em aberto.
        var pedidosAbertos = _db.PedidosIntervencao
            .Where(p => p.Estado == EstadoPedido.Pendente || p.Estado == EstadoPedido.EmAndamento || p.Estado == EstadoPedido.EmEspera)
            .ToList();
        var pedidosUrgentes = pedidosAbertos.Count(p => p.DiasEmAberto > DiasPedidoUrgente);
        var pedidosAviso = pedidosAbertos.Count(p => p.DiasEmAberto > DiasPedidoAviso && p.DiasEmAberto <= DiasPedidoUrgente);
        if (pedidosUrgentes > 0)
        {
            itens.Add(new NotificacaoItem
            {
                Severidade = SeveridadeNotificacao.Urgente,
                Modulo = "Pedidos",
                Mensagem = $"{pedidosUrgentes} pedido(s) de intervenção há mais de {DiasPedidoUrgente} dias em aberto."
            });
        }
        if (pedidosAviso > 0)
        {
            itens.Add(new NotificacaoItem
            {
                Severidade = SeveridadeNotificacao.Aviso,
                Modulo = "Pedidos",
                Mensagem = $"{pedidosAviso} pedido(s) de intervenção entre {DiasPedidoAviso} e {DiasPedidoUrgente} dias em aberto."
            });
        }

        // Atividades da DISIA com estado "Pendente".
        var atividadesPendentes = _db.AtividadesDisia.Count(a => a.Estado == EstadoIntervencao.Pendente);
        if (atividadesPendentes > 0)
        {
            itens.Add(new NotificacaoItem
            {
                Severidade = SeveridadeNotificacao.Info,
                Modulo = "Disia",
                Mensagem = $"{atividadesPendentes} atividade(s) da DISIA com o estado \"Pendente\"."
            });
        }

        // Intervenções com estado "Pendente" (ex.: aguardam peça/equipamento).
        var intervencoesPendentes = _db.Intervencoes.Count(i => i.Estado == EstadoIntervencao.Pendente);
        if (intervencoesPendentes > 0)
        {
            itens.Add(new NotificacaoItem
            {
                Severidade = SeveridadeNotificacao.Info,
                Modulo = "Intervencoes",
                Mensagem = $"{intervencoesPendentes} intervenção(ões) com o estado \"Pendente\"."
            });
        }

        // Mais urgente primeiro.
        return itens.OrderByDescending(i => i.Severidade).ToList();
    }
}
