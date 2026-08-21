using System.Windows;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;

namespace LeiriaDISIA.Views;

public partial class ContactoEditWindow : Window
{
    private readonly Contacto? _existente;
    public bool Sucesso { get; private set; }

    public ContactoEditWindow(Contacto? contacto)
    {
        InitializeComponent();
        // 1.2.1: tinge a barra de titulo nativa com um tom azul sobrio, consistente com a
        // identidade da aplicacao - ver Services/TitleBarService.cs. A janela continua nativa;
        // mover, minimizar, maximizar, fechar e o comportamento modal nao sao afetados.
        SourceInitialized += (_, _) => TitleBarService.AplicarCorSobria(this);

        _existente = contacto;

        CmbEscola.ItemsSource = App.Db.Escolas.Where(e => e.Estado != EstadosEscola.Desativada).OrderBy(e => e.Nome).ToList();

        if (contacto == null)
        {
            TxtTitulo.Text = "Novo Contacto";
            return;
        }

        TxtTitulo.Text = "Editar Contacto";
        TxtNome.Text = contacto.Nome;
        CmbEscola.SelectedItem = ((List<Escola>)CmbEscola.ItemsSource).FirstOrDefault(x => x.Id == contacto.EscolaId);
        TxtEntidadeExterna.Text = contacto.EntidadeExterna;
        TxtFuncao.Text = contacto.Funcao;
        TxtTelefone.Text = contacto.Telefone;
        TxtTelemovel.Text = contacto.Telemovel;
        TxtEmail.Text = contacto.Email;
        TxtObservacoes.Text = contacto.Observacoes;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = false;
        Close();
    }

    private void Guardar_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text))
        {
            MessageBox.Show("Indique o nome do contacto.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Contacto contacto;
        if (_existente == null)
        {
            contacto = new Contacto();
            App.Db.Contactos.Add(contacto);
        }
        else
        {
            contacto = App.Db.Contactos.First(c => c.Id == _existente.Id);
        }

        contacto.Nome = TxtNome.Text.Trim();
        contacto.EscolaId = (CmbEscola.SelectedItem as Escola)?.Id;
        contacto.EntidadeExterna = string.IsNullOrWhiteSpace(TxtEntidadeExterna.Text) ? null : TxtEntidadeExterna.Text.Trim();
        contacto.Funcao = TxtFuncao.Text;
        contacto.Telefone = TxtTelefone.Text;
        contacto.Telemovel = TxtTelemovel.Text;
        contacto.Email = TxtEmail.Text;
        contacto.Observacoes = TxtObservacoes.Text;

        App.Db.SaveChanges();
        Sucesso = true;
        Close();
    }
}
