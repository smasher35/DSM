using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services;

/// <summary>
/// Centraliza a lógica de negócio da "recolha" de equipamento — usada quando um ou mais
/// equipamentos passam a estar fisicamente fora da escola, a caminho de reparação pela DISIA.
/// Esta lógica existia apenas dentro de <see cref="LeiriaDISIA.Views.IntervencaoEditWindow"/>
/// (secção "Equipamento a Recolher (vai para a DISIA)"); foi extraída para aqui para poder ser
/// reutilizada também a partir de Equipamentos → Novo Equipamento, quando um equipamento é criado
/// já associado a uma escola e com o estado "Recolhido" (ver <see cref="LeiriaDISIA.Views.EquipamentoEditWindow"/>),
/// sem alterar o resultado do fluxo original em Intervenções.
/// </summary>
public static class RecolhaEquipamentoService
{
    /// <summary>Dados mínimos de um equipamento a recolher — id (para ligar o registo e atualizar o
    /// estado) e a descrição/nº de série já formatados tal como o chamador os apresenta na sua UI,
    /// para o texto da Atividade DISIA criada ficar exatamente igual ao que já era produzido.</summary>
    public readonly record struct EquipamentoARecolher(int EquipamentoId, string Descricao, string NumeroSerie);

    /// <summary>
    /// Regista a recolha de um ou mais equipamentos já existentes, associados a uma escola: cria
    /// UMA Atividade DISIA no estado "Em Progresso" que agrega a reparação de todos os
    /// equipamentos indicados, cria o registo <see cref="EquipamentoRecolhido"/> de cada um
    /// (ligado a essa atividade) com estado "Pendente", e marca cada <see cref="Equipamento"/> com
    /// o estado "Recolhido". É exatamente a mesma sequência de operações que já era feita, inline,
    /// em <see cref="LeiriaDISIA.Views.IntervencaoEditWindow"/>.
    ///
    /// Não faz commit/gere transação — grava as alterações no <see cref="App.Db"/> através de
    /// <c>SaveChanges()</c> (necessário aqui apenas para obter o Id gerado da Atividade DISIA antes
    /// de criar os registos de recolha que dela dependem); cabe a quem chama envolver isto numa
    /// transação mais ampla, se for esse o caso (ver <see cref="LeiriaDISIA.Views.EquipamentoEditWindow"/>).
    /// </summary>
    /// <param name="equipamentos">Os equipamentos a recolher (não pode ser vazio).</param>
    /// <param name="escola">Escola de onde o equipamento está a ser recolhido.</param>
    /// <param name="data">Data da recolha.</param>
    /// <param name="intervencaoId">Id da Intervenção de origem, quando aplicável (opcional — ao
    /// criar um equipamento novo já "Recolhido" não existe nenhuma Intervenção associada).</param>
    /// <returns>A Atividade DISIA criada.</returns>
    public static AtividadeDisia RegistarRecolha(
        IReadOnlyCollection<EquipamentoARecolher> equipamentos,
        Escola escola,
        DateTime data,
        int? intervencaoId = null)
    {
        if (equipamentos == null || equipamentos.Count == 0)
            throw new ArgumentException("É necessário indicar pelo menos um equipamento a recolher.", nameof(equipamentos));
        if (escola == null)
            throw new ArgumentNullException(nameof(escola));

        // Cria uma Atividade DISIA (módulo "Atividades DISIA", não uma nova Intervenção) que agrega
        // todo o equipamento recolhido nesta operação, para acompanhar a reparação nas instalações
        // da DISIA. Fica "Em Progresso"; só quando for fechada é que o equipamento avança para
        // "Aguarda Entrega" e liberta a devolução à escola (ver AtividadeDisiaEditWindow).
        var descricaoEquipamentos = string.Join("; ", equipamentos.Select(eq =>
            $"{eq.Descricao} (Nº Série {eq.NumeroSerie})".Trim()));

        var atividadeDisia = new AtividadeDisia
        {
            Data = data,
            Mes = data.Month,
            Ano = data.Year,
            Local = escola.Nome,
            Descricao = $"Reparação de equipamento recolhido em {escola.Nome}: {descricaoEquipamentos}",
            Estado = EstadoIntervencao.EmProgresso
        };
        App.Db.AtividadesDisia.Add(atividadeDisia);
        App.Db.SaveChanges();

        foreach (var eq in equipamentos)
        {
            App.Db.EquipamentosRecolhidos.Add(new EquipamentoRecolhido
            {
                EquipamentoId = eq.EquipamentoId,
                IntervencaoId = intervencaoId,
                AtividadeDisiaId = atividadeDisia.Id,
                DataRecolha = data,
                Estado = EstadosRecolha.Pendente
            });

            // O estado "Em Reparação" só é atribuído manualmente, no módulo de Equipamentos,
            // enquanto a Atividade DISIA associada estiver em curso — aqui só passa a "Recolhido".
            var equipamentoEntidade = App.Db.Equipamentos.Find(eq.EquipamentoId);
            if (equipamentoEntidade != null) equipamentoEntidade.Estado = EstadosEquipamento.Recolhido;
        }

        return atividadeDisia;
    }

    /// <summary>Indica se já existe um registo de recolha pendente (ainda não entregue) para este
    /// equipamento — usado como salvaguarda para nunca criar recolhas/Atividades DISIA duplicadas
    /// para o mesmo equipamento.</summary>
    public static bool TemRecolhaPendente(int equipamentoId) =>
        App.Db.EquipamentosRecolhidos.Any(r => r.EquipamentoId == equipamentoId && r.DataEntrega == null);

    /// <summary>
    /// Cria, sozinho e sem depender de mais nada, o registo <see cref="EquipamentoRecolhido"/> (já
    /// no estado "Aguarda Entrega", sem Atividade DISIA associada) para um equipamento gravado
    /// diretamente com o estado "Aguarda Entrega" em Equipamentos → Inserir/Editar — normalmente
    /// equipamento novo, que nunca chegou a estar fisicamente na escola, já preparado para a
    /// primeira entrega.
    ///
    /// Existe em separado de <see cref="CriarAtividadeAcompanhamento"/> porque "Aguarda Entrega"
    /// significa, por definição, "pronto a entregar a uma escola", e é o registo de
    /// <see cref="EquipamentoRecolhido"/> — não a Atividade DISIA — que faz o equipamento aparecer
    /// em "Equipamento Recolhido" ao criar uma Intervenção normal para essa escola (ver
    /// <see cref="Views.IntervencaoEditWindow"/>, que filtra apenas por
    /// <c>Equipamento.EscolaId</c> e <c>DataEntrega == null</c>) e poder ser entregue com o botão
    /// "Devolver à Escola" (que exige <see cref="EquipamentoRecolhido.Estado"/> = "Aguarda
    /// Entrega" — ver <see cref="EquipamentoRecolhido.PodeSerEntregue"/>). Uma Atividade DISIA
    /// nem sempre faz sentido para este caso (não houve nenhuma reparação a acompanhar), pelo que
    /// não deve ser obrigatória para o equipamento ficar corretamente rastreável.
    /// </summary>
    /// <param name="equipamento">O equipamento já gravado (com Id válido) e já com o estado
    /// "Aguarda Entrega".</param>
    public static EquipamentoRecolhido RegistarAguardaEntregaSemAtividade(Equipamento equipamento)
    {
        if (equipamento == null) throw new ArgumentNullException(nameof(equipamento));

        var recolha = new EquipamentoRecolhido
        {
            EquipamentoId = equipamento.Id,
            DataRecolha = DateTime.Today,
            Estado = EstadosRecolha.AguardaEntrega
        };
        App.Db.EquipamentosRecolhidos.Add(recolha);
        App.Db.SaveChanges();
        return recolha;
    }

    /// <summary>
    /// Cria uma Atividade DISIA de acompanhamento para um equipamento cujo estado foi definido
    /// diretamente para "Recolhido" ou "Aguarda Entrega" em Equipamentos → Inserir/Editar (ver
    /// <see cref="Views.EquipamentoEditWindow"/>), fora do fluxo normal de recolha via
    /// Intervenção. Ao contrário de <see cref="RegistarRecolha"/>, NÃO altera o estado do
    /// equipamento (já foi gravado tal como o utilizador escolheu) — cria apenas a Atividade DISIA
    /// e o registo <see cref="EquipamentoRecolhido"/> que a liga ao equipamento, para aparecer no
    /// separador "Histórico de Intervenções" e para o mecanismo de "Devolver à Escola" continuar a
    /// funcionar tal como para uma recolha normal.
    ///
    /// Quando <paramref name="recolhaExistente"/> é indicado (caso "Aguarda Entrega", em que o
    /// registo de recolha já foi criado antecipadamente por
    /// <see cref="RegistarAguardaEntregaSemAtividade"/>, antes mesmo de se perguntar se se quer uma
    /// Atividade DISIA), a Atividade DISIA criada aqui é associada a esse registo já existente em
    /// vez de se criar um duplicado. Caso contrário, cria os dois registos juntos, numa única
    /// chamada a <c>SaveChanges()</c> (a associação é feita pela propriedade de navegação, não por
    /// um Id já gravado), para nunca poder ficar uma Atividade DISIA criada sem o respetivo
    /// registo de recolha, ou vice-versa, em caso de falha.
    ///
    /// Quem chama deve validar antes com <see cref="TemRecolhaPendente"/> se já existe uma recolha
    /// pendente para este equipamento, para nunca duplicar (a não ser que essa recolha pendente
    /// seja precisamente a passada em <paramref name="recolhaExistente"/>).
    /// </summary>
    /// <param name="equipamento">O equipamento já gravado, com o estado atual já atualizado.</param>
    /// <param name="escola">A escola atualmente associada ao equipamento, se aplicável — passada
    /// explicitamente (em vez de se ler <c>equipamento.Escola</c>) porque essa propriedade de
    /// navegação pode não estar carregada neste ponto.</param>
    /// <param name="estadoEquipamento">O estado atual do equipamento — "Recolhido" ou "Aguarda
    /// Entrega" (ver <see cref="EstadosEquipamento"/>) — usado para descrever a atividade e, quando
    /// não há <paramref name="recolhaExistente"/>, para decidir o estado inicial do novo registo de
    /// recolha.</param>
    /// <param name="recolhaExistente">Registo de recolha já criado a que associar esta Atividade
    /// DISIA, em vez de criar um novo (opcional).</param>
    public static AtividadeDisia CriarAtividadeAcompanhamento(Equipamento equipamento, Escola? escola,
        string estadoEquipamento, EquipamentoRecolhido? recolhaExistente = null)
    {
        if (equipamento == null) throw new ArgumentNullException(nameof(equipamento));

        var local = escola?.Nome ?? equipamento.LocalNaoEscolar ?? "local não indicado";
        var hoje = DateTime.Today;

        var atividadeDisia = new AtividadeDisia
        {
            Data = hoje,
            Mes = hoje.Month,
            Ano = hoje.Year,
            Local = escola?.Nome,
            Descricao = $"Acompanhamento de equipamento {equipamento.NumeroSerie} (\"{estadoEquipamento}\") em {local}",
            Estado = EstadoIntervencao.EmProgresso
        };

        if (recolhaExistente != null)
        {
            recolhaExistente.AtividadeDisia = atividadeDisia; // liga a atividade ao registo já existente
            App.Db.AtividadesDisia.Add(atividadeDisia);
            App.Db.SaveChanges();
            return atividadeDisia;
        }

        // O estado inicial do registo de recolha acompanha o estado com que o equipamento acabou
        // de ser gravado, para os dois ficarem sempre coerentes entre si.
        var estadoRecolha = estadoEquipamento == EstadosEquipamento.AguardaEntrega
            ? EstadosRecolha.AguardaEntrega
            : EstadosRecolha.Pendente;

        var recolha = new EquipamentoRecolhido
        {
            EquipamentoId = equipamento.Id,
            AtividadeDisia = atividadeDisia, // liga pela navegação — grava tudo numa só transação
            DataRecolha = hoje,
            Estado = estadoRecolha
        };

        App.Db.AtividadesDisia.Add(atividadeDisia);
        App.Db.EquipamentosRecolhidos.Add(recolha);
        App.Db.SaveChanges();

        return atividadeDisia;
    }
}
