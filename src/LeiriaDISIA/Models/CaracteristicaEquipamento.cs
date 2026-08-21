namespace LeiriaDISIA.Models;

/// <summary>
/// (1.3) Definição de uma característica específica adicional, criada pelo próprio
/// administrador em Administração → Dados Fixos → Tipos de Equipamento → "Gerir Características",
/// para um grupo de características (<see cref="GruposCaracteristicasEquipamento"/>) — ex.: para o
/// grupo "Computador" pode existir "Processador", "Memória (GB)", mas também qualquer outra que o
/// administrador queira acrescentar (ex.: "Nº de Patrimônio Antigo", "Licença Office").
///
/// (Dados Fixos v2) Desde então, os próprios campos "fixos" de Computador (Processador, Tipo de
/// Memória, Memória (GB), Tipo de Disco, Tamanho do Disco (GB), Sistema Operativo) passaram também
/// a ser geridos aqui — ver <see cref="TipoEquipamentoId"/> e <see cref="CaracteristicaPaiId"/> —
/// mas continuam a gravar o valor escolhido nas mesmas propriedades de sempre em
/// <see cref="Equipamento"/> (Processador, TipoMemoria, etc., que têm lógica própria associada:
/// cálculo de obsolescência, relatórios, exportação Excel, resumo de alterações de hardware nas
/// Atividades DISIA). Só a origem da lista de valores sugeridos passou a vir daqui — o
/// armazenamento não muda. As características aqui definidas aparecem como campos extra, dinâmicos,
/// na secção "Características Adicionais" de Inserir/Editar Equipamento, consoante o grupo do Tipo
/// de Equipamento escolhido — ver <see cref="EquipamentoCaracteristicaValor"/> para onde ficam
/// gravados os valores efetivamente preenchidos em cada equipamento (para as restantes
/// características, que não sejam os seis campos fixos acima).
/// </summary>
public class CaracteristicaEquipamento
{
    public int Id { get; set; }

    /// <summary>A que grupo de características pertence — ver <see cref="GruposCaracteristicasEquipamento"/>.
    /// Um Tipo de Equipamento (em Dados Fixos) está ligado a um destes grupos através de
    /// <see cref="ValorFixo.GrupoCaracteristicas"/>; ao selecioná-lo em Inserir/Editar Equipamento,
    /// mostram-se todas as características aqui definidas para esse grupo.</summary>
    public string GrupoCaracteristicas { get; set; } = GruposCaracteristicasEquipamento.Generico;

    /// <summary>Nome da característica, apresentado como rótulo do campo (ex.: "Nº de Patrimônio Antigo").</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Valor sugerido/por omissão (opcional). Quando definido, é pré-preenchido ao
    /// escolher o Tipo de Equipamento num equipamento novo — o utilizador pode sempre alterá-lo
    /// ou apagá-lo antes de gravar.</summary>
    public string? ValorPorOmissao { get; set; }

    public int Ordem { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>(Dados Fixos v2) Quando preenchido, esta característica só se aplica a este Tipo de
    /// Equipamento específico — o Id de um <see cref="ValorFixo"/> do grupo
    /// <see cref="GruposValorFixo.TipoEquipamento"/> (ex.: só a "Portátil", como uma característica
    /// "Autonomia da Bateria"). Quando <c>null</c> (o normal), a característica é partilhada por
    /// todos os Tipos deste <see cref="GrupoCaracteristicas"/> (ex.: "Processador" aparece tanto em
    /// "Computador de Secretária" como em "Portátil" e "Servidor") — comportamento inalterado desde
    /// sempre. Tal como <see cref="ValorFixo.GrupoCaracteristicas"/>, é uma referência solta (sem FK
    /// declarada no EF Core), resolvida por consulta direta ao Id, e nunca aplicada em cascata.</summary>
    public int? TipoEquipamentoId { get; set; }

    /// <summary>(Dados Fixos v2) Quando preenchido, esta característica é uma "característica-filha":
    /// só aparece no formulário depois de se escolher, na característica-pai indicada aqui, uma
    /// opção que a referencie (ver <see cref="CaracteristicaEquipamentoOpcao.CaracteristicaFilhaId"/>)
    /// — ex.: "Memória (GB)" só surge depois de se escolher "DDR4" em "Tipo de Memória". Uma
    /// característica-filha nunca aparece isolada na lista principal de características do grupo.
    /// <c>null</c> (o normal) = característica de nível único, sem subtipo — comportamento
    /// inalterado. Referência solta, tal como <see cref="TipoEquipamentoId"/>.</summary>
    public int? CaracteristicaPaiId { get; set; }
}

/// <summary>
/// (1.3) Valor efetivamente preenchido, para um equipamento concreto, de uma característica
/// adicional definida em <see cref="CaracteristicaEquipamento"/>.
/// </summary>
public class EquipamentoCaracteristicaValor
{
    public int Id { get; set; }

    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }

    public int CaracteristicaEquipamentoId { get; set; }
    public CaracteristicaEquipamento? CaracteristicaEquipamento { get; set; }

    public string? Valor { get; set; }
}

/// <summary>
/// (1.4) Um dos valores pré-definidos (opcionais) de uma lista de sugestão para uma característica
/// específica adicional — geridos em Administração → Dados Fixos → Tipos de Equipamento → "Gerir
/// Características deste Grupo..." → "Gerir Valores desta Característica...", à semelhança do que já
/// acontece para os restantes campos de Dados Fixos (<see cref="ValorFixo"/>).
///
/// Quando uma característica (<see cref="CaracteristicaEquipamento"/>) tiver pelo menos um valor
/// ativo aqui definido, o campo correspondente em Inserir/Editar Equipamento passa a mostrar uma
/// caixa de seleção editável (em vez de uma simples caixa de texto livre), pré-preenchida com estas
/// sugestões — mas continuando a permitir escrever um valor livre não incluído na lista, tal como
/// as restantes combos da aplicação (Processador, Tipo de Memória, etc.). Quando não existir nenhum
/// valor ativo definido, o campo mantém-se uma caixa de texto livre, como até aqui.
/// </summary>
public class CaracteristicaEquipamentoOpcao
{
    public int Id { get; set; }

    public int CaracteristicaEquipamentoId { get; set; }
    public CaracteristicaEquipamento? CaracteristicaEquipamento { get; set; }

    public string Valor { get; set; } = string.Empty;

    public int Ordem { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>(Dados Fixos v2) Quando preenchido, escolher este valor abre uma segunda caixa de
    /// seleção dependente, com as opções da característica-filha indicada aqui — ex.: a opção
    /// "DDR4" da característica "Tipo de Memória" aponta para a característica-filha
    /// "Memória (GB)" (ver <see cref="CaracteristicaEquipamento.CaracteristicaPaiId"/>). <c>null</c>
    /// (o normal) = valor simples, sem subtipo — comportamento inalterado.</summary>
    public int? CaracteristicaFilhaId { get; set; }
}
