namespace LeiriaDISIA.Models;

/// <summary>
/// Categoria principal de uma intervenção técnica.
/// As 5 categorias base pedidas: Redes, Hardware, Software, VPN, Audio-Visual.
/// Podem ser adicionadas mais no futuro, por isso é mantida como tabela (não enum fixo).
/// </summary>
public class CategoriaIntervencao
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string CorHex { get; set; } = "#3B82F6"; // cor usada nos gráficos
    public bool Ativa { get; set; } = true;

    public ICollection<SubCategoriaIntervencao> SubCategorias { get; set; } = new List<SubCategoriaIntervencao>();
}

public class SubCategoriaIntervencao
{
    public int Id { get; set; }
    public int CategoriaIntervencaoId { get; set; }
    public CategoriaIntervencao? Categoria { get; set; }
    public string Nome { get; set; } = string.Empty;
}

public enum EstadoIntervencao
{
    Fechada,
    Pendente,
    EmProgresso,
    EmEspera,   // aguarda aquisição de equipamento/material
    Cancelada
}

public enum EstadoPedido
{
    Pendente,
    EmAndamento,
    EmEspera,   // aguarda aquisição de equipamento/material
    Concluido,
    Cancelado
}

public static class EstadoCores
{
    private static string CorPersonalizada(string grupo, string nomeEstado, string corPorOmissao)
    {
        try
        {
            var db = LeiriaDISIA.App.Db;
            if (db == null) return corPorOmissao;

            var registo = db.EstadosCorPersonalizados
                .FirstOrDefault(e => e.Grupo == grupo && e.NomeEstado == nomeEstado);
            return string.IsNullOrWhiteSpace(registo?.Cor) ? corPorOmissao : registo.Cor;
        }
        catch
        {
            // Em qualquer situação em que a base de dados ainda não esteja disponível
            // (ex.: durante o arranque), usa-se sempre a cor por omissão.
            return corPorOmissao;
        }
    }

    public static string CorEstadoIntervencao(EstadoIntervencao estado)
    {
        var corPorOmissao = estado switch
        {
            EstadoIntervencao.Fechada => "#22C55E",     // verde
            EstadoIntervencao.Pendente => "#EF4444",    // vermelho
            EstadoIntervencao.EmProgresso => "#F59E0B",  // laranja/amarelo
            EstadoIntervencao.EmEspera => "#6366F1",     // roxo/azulado
            EstadoIntervencao.Cancelada => "#9CA3AF",   // cinza
            _ => "#9CA3AF"
        };
        return CorPersonalizada(GruposEstadoCor.Intervencao, estado.ToString(), corPorOmissao);
    }

    public static string CorEstadoPedido(EstadoPedido estado)
    {
        var corPorOmissao = estado switch
        {
            EstadoPedido.Pendente => "#EF4444",
            EstadoPedido.EmAndamento => "#F59E0B",
            EstadoPedido.EmEspera => "#6366F1",
            EstadoPedido.Concluido => "#22C55E",
            EstadoPedido.Cancelado => "#9CA3AF",
            _ => "#9CA3AF"
        };
        return CorPersonalizada(GruposEstadoCor.Pedido, estado.ToString(), corPorOmissao);
    }

    /// <summary>Cor por estado de equipamento — ver <see cref="LeiriaDISIA.Models.EstadosEquipamento"/>.
    /// O nome do estado (usado como chave de personalização) é o próprio valor apresentado,
    /// já que <see cref="LeiriaDISIA.Models.Equipamento.Estado"/> não é um enum.</summary>
    public static string CorEstadoEquipamento(string? estado)
    {
        var corPorOmissao = estado switch
        {
            LeiriaDISIA.Models.EstadosEquipamento.EmServico => "#22C55E",      // verde
            LeiriaDISIA.Models.EstadosEquipamento.Recolhido => "#F59E0B",      // laranja
            LeiriaDISIA.Models.EstadosEquipamento.EmReparacao => "#6366F1",    // roxo/azulado
            LeiriaDISIA.Models.EstadosEquipamento.Reparado => "#22C55E",       // verde
            LeiriaDISIA.Models.EstadosEquipamento.AguardaEntrega => "#F59E0B", // laranja
            LeiriaDISIA.Models.EstadosEquipamento.EmArmazem => "#9CA3AF",      // cinza
            LeiriaDISIA.Models.EstadosEquipamento.Abatido => "#EF4444",        // vermelho
            _ => "#9CA3AF"
        };
        return CorPersonalizada(GruposEstadoCor.Equipamento, estado ?? string.Empty, corPorOmissao);
    }

    /// <summary>Cor por estado de escola — ver <see cref="LeiriaDISIA.Models.EstadosEscola"/>.
    /// Tal como em <see cref="CorEstadoEquipamento"/>, o estado da escola não é um enum (é texto
    /// configurável em Dados Fixos), pelo que qualquer estado criado pelo administrador além dos
    /// três iniciais recebe a cor cinzenta por omissão.</summary>
    public static string CorEstadoEscola(string? estado) => estado switch
    {
        LeiriaDISIA.Models.EstadosEscola.Ativa => "#22C55E",      // verde
        LeiriaDISIA.Models.EstadosEscola.EmObras => "#F59E0B",    // laranja
        LeiriaDISIA.Models.EstadosEscola.Desativada => "#9CA3AF", // cinza
        _ => "#9CA3AF"
    };

    /// <summary>Cor por estado de equipamento recolhido — ver <see cref="LeiriaDISIA.Models.EstadosRecolha"/>.</summary>
    public static string CorEstadoRecolha(string? estado)
    {
        var corPorOmissao = estado switch
        {
            LeiriaDISIA.Models.EstadosRecolha.Pendente => "#EF4444",       // vermelho
            LeiriaDISIA.Models.EstadosRecolha.EmReparacao => "#6366F1",    // roxo/azulado
            LeiriaDISIA.Models.EstadosRecolha.AguardaEntrega => "#F59E0B", // laranja
            LeiriaDISIA.Models.EstadosRecolha.Entregue => "#22C55E",       // verde
            _ => "#9CA3AF"
        };
        return CorPersonalizada(GruposEstadoCor.Recolha, estado ?? string.Empty, corPorOmissao);
    }

    /// <summary>
    /// Cor semafórica consoante o nº de dias em que um pedido está em aberto:
    /// verde (&lt;= 7 dias), amarelo (8 a 21 dias), vermelho (&gt; 21 dias).
    /// </summary>
    public static string CorTempoEmAberto(int dias) => dias switch
    {
        <= 7 => "#22C55E",
        <= 21 => "#F59E0B",
        _ => "#EF4444"
    };

    public static string NomeExibicaoEstadoIntervencao(EstadoIntervencao estado)
    {
        try
        {
            var db = LeiriaDISIA.App.Db;
            if (db != null)
            {
                var registo = db.EstadosCorPersonalizados
                    .FirstOrDefault(e => e.Grupo == GruposEstadoCor.Intervencao && e.NomeEstado == estado.ToString());
                if (!string.IsNullOrWhiteSpace(registo?.NomeExibicao))
                    return registo.NomeExibicao;
            }
        }
        catch { }
        return estado.ToString();
    }

    public static string NomeExibicaoEstadoPedido(EstadoPedido estado)
    {
        try
        {
            var db = LeiriaDISIA.App.Db;
            if (db != null)
            {
                var registo = db.EstadosCorPersonalizados
                    .FirstOrDefault(e => e.Grupo == GruposEstadoCor.Pedido && e.NomeEstado == estado.ToString());
                if (!string.IsNullOrWhiteSpace(registo?.NomeExibicao))
                    return registo.NomeExibicao;
            }
        }
        catch { }
        return estado.ToString();
    }
}
