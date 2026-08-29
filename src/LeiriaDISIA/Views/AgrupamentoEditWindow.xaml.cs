using System.Windows;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class AgrupamentoEditWindow : Window
{
    private readonly Agrupamento? _existente;
    public bool Sucesso { get; private set; }

    public AgrupamentoEditWindow(Agrupamento? agrupamento)
    {
        InitializeComponent();

        // Perfil Guest (Services/SessaoAtual.PodeEditar): não pode criar/editar/eliminar
        // registos - fecha-se logo a seguir a abrir, com um aviso, em vez de deixar o
        // formulário aberto só para descobrir mais tarde que não consegue gravar nada.
        if (LeiriaDISIA.Services.PermissoesService.BloquearAberturaSeGuest(this)) return;
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = agrupamento;

        if (agrupamento == null)
        {
            TxtTitulo.Text = "Novo Agrupamento";
            return;
        }

        TxtTitulo.Text = "Editar Agrupamento";
        TxtCodigo.Text = agrupamento.CodAgrupamento.ToString();
        TxtNome.Text = agrupamento.Nome;
        TxtAbreviatura.Text = agrupamento.Abreviatura;
        TxtDiretor.Text = agrupamento.Diretor;
        TxtMorada.Text = agrupamento.Morada;
        TxtContacto1.Text = agrupamento.Contacto1;
        TxtContacto2.Text = agrupamento.Contacto2;
        TxtContacto3.Text = agrupamento.Contacto3;
        TxtEmail1.Text = agrupamento.Email1;
        TxtEmail2.Text = agrupamento.Email2;
        TxtSite.Text = agrupamento.Site;
        TxtObservacoes.Text = agrupamento.Observacoes;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text) || !int.TryParse(TxtCodigo.Text, out var codigo))
        {
            MessageBox.Show("Indique um código numérico e o nome do agrupamento.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Agrupamento agrupamento;
        if (_existente == null)
        {
            agrupamento = new Agrupamento();
            App.Db.Agrupamentos.Add(agrupamento);
        }
        else
        {
            agrupamento = App.Db.Agrupamentos.First(a => a.Id == _existente.Id);
        }

        agrupamento.CodAgrupamento = codigo;
        agrupamento.Nome = TxtNome.Text.Trim();
        agrupamento.Abreviatura = string.IsNullOrWhiteSpace(TxtAbreviatura.Text) ? null : TxtAbreviatura.Text.Trim();
        agrupamento.Diretor = string.IsNullOrWhiteSpace(TxtDiretor.Text) ? null : TxtDiretor.Text.Trim();
        agrupamento.Morada = string.IsNullOrWhiteSpace(TxtMorada.Text) ? null : TxtMorada.Text;
        agrupamento.Contacto1 = string.IsNullOrWhiteSpace(TxtContacto1.Text) ? null : TxtContacto1.Text;
        agrupamento.Contacto2 = string.IsNullOrWhiteSpace(TxtContacto2.Text) ? null : TxtContacto2.Text;
        agrupamento.Contacto3 = string.IsNullOrWhiteSpace(TxtContacto3.Text) ? null : TxtContacto3.Text;
        agrupamento.Email1 = string.IsNullOrWhiteSpace(TxtEmail1.Text) ? null : TxtEmail1.Text;
        agrupamento.Email2 = string.IsNullOrWhiteSpace(TxtEmail2.Text) ? null : TxtEmail2.Text;
        agrupamento.Site = string.IsNullOrWhiteSpace(TxtSite.Text) ? null : TxtSite.Text;
        agrupamento.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();
        Sucesso = true;
        Close();
    }
}
