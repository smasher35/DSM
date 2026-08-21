using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

/// <summary>
/// (1.4) Gestão da lista de valores sugeridos (opcionais) de uma característica específica
/// (<see cref="CaracteristicaEquipamento"/>), acessível a partir de Administração → Dados Fixos →
/// Tipos de Equipamento → "Gerir Características deste Grupo..." → "Gerir Valores desta
/// Característica...". Tal como as restantes listas de Dados Fixos, o administrador pode criar,
/// editar ou eliminar quantos valores quiser — mas a lista é sempre opcional: uma característica
/// sem nenhum valor ativo aqui continua a aparecer como caixa de texto livre em Inserir/Editar
/// Equipamento, exatamente como antes desta funcionalidade existir.
/// </summary>
public partial class CaracteristicaOpcoesWindow : Window
{
    private readonly int _caracteristicaEquipamentoId;
    private CaracteristicaEquipamentoOpcao? _selecionada;

    public CaracteristicaOpcoesWindow(int caracteristicaEquipamentoId, string nomeCaracteristica)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _caracteristicaEquipamentoId = caracteristicaEquipamentoId;
        Title = $"Valores da Característica — {nomeCaracteristica}";
        TxtTitulo.Text = $"Valores da Característica — {nomeCaracteristica}";
        CarregarCaracteristicasFilha();
        Recarregar();
        LimparFormulario();
    }

    /// <summary>(Dados Fixos v2) Lista, na combo "Abre a característica", só as características já
    /// configuradas como "É subtipo de" desta (ver CmbCaracteristicaPai em AdministracaoWindow) —
    /// evita listar características não relacionadas, que não fariam sentido aqui.</summary>
    private void CarregarCaracteristicasFilha()
    {
        var filhas = App.Db.CaracteristicasEquipamento
            .Where(c => c.CaracteristicaPaiId == _caracteristicaEquipamentoId)
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .Select(c => new { c.Id, c.Nome })
            .ToList()
            .Select(c => new ItemCaracteristicaFilha(c.Id, c.Nome))
            .ToList();
        filhas.Insert(0, new ItemCaracteristicaFilha(null, "Nenhuma — valor simples"));
        CmbCaracteristicaFilha.ItemsSource = filhas;
    }

    /// <summary>Item da combo "Abre a característica": Id=null representa o valor por omissão
    /// (valor simples, sem subtipo).</summary>
    private record ItemCaracteristicaFilha(int? Id, string Nome);

    private void Recarregar()
    {
        Grid.ItemsSource = App.Db.CaracteristicaEquipamentoOpcoes
            .Where(o => o.CaracteristicaEquipamentoId == _caracteristicaEquipamentoId)
            .OrderBy(o => o.Ordem)
            .ThenBy(o => o.Valor)
            .ToList();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionada = Grid.SelectedItem as CaracteristicaEquipamentoOpcao;
        if (_selecionada == null) return;

        TxtValor.Text = _selecionada.Valor;
        TxtOrdem.Text = _selecionada.Ordem.ToString();
        ChkAtivo.IsChecked = _selecionada.Ativo;
        CmbCaracteristicaFilha.SelectedValue = _selecionada.CaracteristicaFilhaId;
    }

    private void Novo_Click(object sender, RoutedEventArgs e) => LimparFormulario();

    private void LimparFormulario()
    {
        _selecionada = null;
        Grid.SelectedItem = null;
        TxtValor.Clear();
        CmbCaracteristicaFilha.SelectedValue = null;
        var proximaOrdem = (Grid.ItemsSource as IEnumerable<CaracteristicaEquipamentoOpcao>)?.Count() ?? 0;
        TxtOrdem.Text = proximaOrdem.ToString();
        ChkAtivo.IsChecked = true;
    }

    /// <summary>Não permite valores repetidos (ignorando maiúsculas/minúsculas e espaços extra)
    /// dentro da mesma característica — à semelhança do que já acontece nas restantes listas de
    /// Dados Fixos e nas próprias características.</summary>
    private bool ExisteValorRepetido(string valor, out string valorExistente)
    {
        valorExistente = string.Empty;
        var valorNormalizado = valor.Trim();
        var itensAtuais = (Grid.ItemsSource as IEnumerable<CaracteristicaEquipamentoOpcao>) ?? Enumerable.Empty<CaracteristicaEquipamentoOpcao>();

        var duplicado = itensAtuais.FirstOrDefault(o =>
            (_selecionada == null || o.Id != _selecionada.Id) &&
            string.Equals(o.Valor.Trim(), valorNormalizado, StringComparison.OrdinalIgnoreCase));

        if (duplicado == null) return false;
        valorExistente = duplicado.Valor;
        return true;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtValor.Text))
        {
            MessageBox.Show("Indique o valor.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ExisteValorRepetido(TxtValor.Text, out var valorExistente))
        {
            MessageBox.Show($"Já existe um valor igual nesta lista: '{valorExistente}'.",
                "Valor repetido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(TxtOrdem.Text, out var ordem);
        var valor = TxtValor.Text.Trim();
        var caracteristicaFilhaId = CmbCaracteristicaFilha.SelectedValue as int?;

        if (_selecionada == null)
        {
            App.Db.CaracteristicaEquipamentoOpcoes.Add(new CaracteristicaEquipamentoOpcao
            {
                CaracteristicaEquipamentoId = _caracteristicaEquipamentoId,
                Valor = valor,
                Ordem = ordem,
                Ativo = ChkAtivo.IsChecked == true,
                CaracteristicaFilhaId = caracteristicaFilhaId
            });
        }
        else
        {
            var entidade = App.Db.CaracteristicaEquipamentoOpcoes.First(o => o.Id == _selecionada.Id);
            entidade.Valor = valor;
            entidade.Ordem = ordem;
            entidade.Ativo = ChkAtivo.IsChecked == true;
            entidade.CaracteristicaFilhaId = caracteristicaFilhaId;
        }

        App.Db.SaveChanges();
        Recarregar();
        LimparFormulario();
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null) return;

        if (MessageBox.Show(
                $"Eliminar o valor '{_selecionada.Valor}'?",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.CaracteristicaEquipamentoOpcoes.Remove(
            App.Db.CaracteristicaEquipamentoOpcoes.First(o => o.Id == _selecionada.Id));
        App.Db.SaveChanges();

        Recarregar();
        LimparFormulario();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
