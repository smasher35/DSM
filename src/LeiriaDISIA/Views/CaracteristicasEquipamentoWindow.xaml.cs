using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

/// <summary>
/// (1.3) Gestão das características específicas adicionais de um grupo de características de
/// equipamento (ver <see cref="GruposCaracteristicasEquipamento"/>), acessível a partir de
/// Administração → Dados Fixos → Tipos de Equipamento → "Gerir Características deste Grupo...".
/// Ao contrário dos Estados (fixos, ligados à lógica de negócio), estas características são
/// totalmente livres: o administrador pode criar, editar ou eliminar quantas quiser, cada uma com
/// um valor por omissão opcional.
/// </summary>
public partial class CaracteristicasEquipamentoWindow : Window
{
    private readonly string _grupoCaracteristicas;
    private CaracteristicaEquipamento? _selecionada;

    public CaracteristicasEquipamentoWindow(string grupoCaracteristicas)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _grupoCaracteristicas = grupoCaracteristicas;
        // (1.1) O grupo pode ser um dos "embutidos" (Computador, Monitor, ...) ou um grupo
        // personalizado criado pelo administrador (ex.: "Energia") — em qualquer dos casos, mostra-se
        // o nome real do grupo no título, para que fique claro a que grupo estas características
        // pertencem.
        var rotulo = string.IsNullOrWhiteSpace(grupoCaracteristicas)
            ? GruposCaracteristicasEquipamento.Generico
            : grupoCaracteristicas;
        Title = $"Características Específicas — {rotulo}";
        TxtTitulo.Text = $"Características Específicas — {rotulo}";
        Recarregar();
        LimparFormulario();
    }

    private void Recarregar()
    {
        Grid.ItemsSource = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == _grupoCaracteristicas)
            .OrderBy(c => c.Ordem)
            .ThenBy(c => c.Nome)
            .ToList();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selecionada = Grid.SelectedItem as CaracteristicaEquipamento;
        BtnGerirValores.IsEnabled = _selecionada != null;
        if (_selecionada == null) return;

        TxtNome.Text = _selecionada.Nome;
        TxtValorPorOmissao.Text = _selecionada.ValorPorOmissao;
        TxtOrdem.Text = _selecionada.Ordem.ToString();
        ChkAtivo.IsChecked = _selecionada.Ativo;
    }

    private void Novo_Click(object sender, RoutedEventArgs e) => LimparFormulario();

    private void LimparFormulario()
    {
        _selecionada = null;
        Grid.SelectedItem = null;
        BtnGerirValores.IsEnabled = false;
        TxtNome.Clear();
        TxtValorPorOmissao.Clear();
        var proximaOrdem = (Grid.ItemsSource as IEnumerable<CaracteristicaEquipamento>)?.Count() ?? 0;
        TxtOrdem.Text = proximaOrdem.ToString();
        ChkAtivo.IsChecked = true;
    }

    /// <summary>Não permite nomes repetidos (ignorando maiúsculas/minúsculas e espaços extra)
    /// dentro do mesmo grupo — à semelhança do que já acontece em Dados Fixos para as restantes
    /// listas de valores.</summary>
    private bool ExisteNomeRepetido(string nome, out string nomeExistente)
    {
        nomeExistente = string.Empty;
        var nomeNormalizado = nome.Trim();
        var itensAtuais = (Grid.ItemsSource as IEnumerable<CaracteristicaEquipamento>) ?? Enumerable.Empty<CaracteristicaEquipamento>();

        var duplicado = itensAtuais.FirstOrDefault(c =>
            (_selecionada == null || c.Id != _selecionada.Id) &&
            string.Equals(c.Nome.Trim(), nomeNormalizado, StringComparison.OrdinalIgnoreCase));

        if (duplicado == null) return false;
        nomeExistente = duplicado.Nome;
        return true;
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text))
        {
            MessageBox.Show("Indique o nome da característica.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ExisteNomeRepetido(TxtNome.Text, out var nomeExistente))
        {
            MessageBox.Show($"Já existe uma característica com este nome neste grupo: '{nomeExistente}'.",
                "Nome repetido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(TxtOrdem.Text, out var ordem);
        var nome = TxtNome.Text.Trim();
        var valorPorOmissao = string.IsNullOrWhiteSpace(TxtValorPorOmissao.Text) ? null : TxtValorPorOmissao.Text.Trim();

        int idGravado;
        if (_selecionada == null)
        {
            var nova = new CaracteristicaEquipamento
            {
                GrupoCaracteristicas = _grupoCaracteristicas,
                Nome = nome,
                ValorPorOmissao = valorPorOmissao,
                Ordem = ordem,
                Ativo = ChkAtivo.IsChecked == true
            };
            App.Db.CaracteristicasEquipamento.Add(nova);
            App.Db.SaveChanges();
            idGravado = nova.Id;
        }
        else
        {
            var entidade = App.Db.CaracteristicasEquipamento.First(c => c.Id == _selecionada.Id);
            entidade.Nome = nome;
            entidade.ValorPorOmissao = valorPorOmissao;
            entidade.Ordem = ordem;
            entidade.Ativo = ChkAtivo.IsChecked == true;
            App.Db.SaveChanges();
            idGravado = entidade.Id;
        }

        // (1.4) Mantém a característica gravada selecionada, em vez de limpar o formulário —
        // permite ao administrador clicar logo a seguir em "Gerir Valores desta Característica..."
        // sem ter de a voltar a procurar e selecionar na grelha.
        Recarregar();
        _selecionada = (Grid.ItemsSource as IEnumerable<CaracteristicaEquipamento>)?.FirstOrDefault(c => c.Id == idGravado);
        Grid.SelectedItem = _selecionada;
        BtnGerirValores.IsEnabled = _selecionada != null;
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null) return;

        if (MessageBox.Show(
                $"Eliminar a característica '{_selecionada.Nome}'?\n\n" +
                "Os valores já preenchidos com esta característica em equipamentos existentes, " +
                "bem como a sua lista de valores sugeridos, também serão eliminados.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var valoresAssociados = App.Db.EquipamentoCaracteristicaValores
            .Where(v => v.CaracteristicaEquipamentoId == _selecionada.Id);
        App.Db.EquipamentoCaracteristicaValores.RemoveRange(valoresAssociados);

        // (1.4) A lista de valores sugeridos desta característica também deixa de fazer sentido.
        var opcoesAssociadas = App.Db.CaracteristicaEquipamentoOpcoes
            .Where(o => o.CaracteristicaEquipamentoId == _selecionada.Id);
        App.Db.CaracteristicaEquipamentoOpcoes.RemoveRange(opcoesAssociadas);

        App.Db.CaracteristicasEquipamento.Remove(App.Db.CaracteristicasEquipamento.First(c => c.Id == _selecionada.Id));
        App.Db.SaveChanges();

        Recarregar();
        LimparFormulario();
    }

    /// <summary>(1.4) Abre a gestão da lista de valores sugeridos (opcionais) para a característica
    /// atualmente selecionada na grelha.</summary>
    private void GerirValores_Click(object sender, RoutedEventArgs e)
    {
        if (_selecionada == null) return;

        var janela = new CaracteristicaOpcoesWindow(_selecionada.Id, _selecionada.Nome) { Owner = this };
        janela.ShowDialog();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();
}
