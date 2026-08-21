using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeiriaDISIA.Data;
using LeiriaDISIA.Services;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class ConfiguracoesView : UserControl
{
    public ConfiguracoesView()
    {
        InitializeComponent();
        TxtCaminhoDb.Text = AppDbContext.DbPath;

        // Definir o radio button selecionado com base no tema atual
        if (ThemeService.TemaAtual == TemaAplicacao.Escuro)
            RbTemaEscuro.IsChecked = true;
        else
            RbTemaClaro.IsChecked = true;

        // Listener para quando o tema muda - força re-renderização do UserControl
        ThemeService.TemaMudou += (s, e) =>
        {
            // Atualiza o tema selecionado
            if (e == TemaAplicacao.Escuro)
                RbTemaEscuro.IsChecked = true;
            else
                RbTemaClaro.IsChecked = true;

            // Force update dos controlos para aplicar o novo tema visualmente
            InvalidateVisual();
        };
    }

    private void TemaClaro_Checked(object sender, RoutedEventArgs e)
    {
        ThemeService.Aplicar(TemaAplicacao.Claro);
    }

    private void TemaEscuro_Checked(object sender, RoutedEventArgs e)
    {
        ThemeService.Aplicar(TemaAplicacao.Escuro);
    }

    private void AbrirLocalizacao_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppDbContext.DbPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir a localização:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopiarCaminho_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(AppDbContext.DbPath);
        MessageBox.Show("Caminho copiado para a área de transferência.", "Concluído",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar cópia de segurança",
            Filter = "Base de dados (*.db)|*.db",
            FileName = $"Backup_LeiriaDISIA_{DateTime.Now:yyyyMMdd_HHmm}.db"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            App.FecharLigacaoDb();
            File.Copy(AppDbContext.DbPath, dialog.FileName, overwrite: true);
            App.ReabrirLigacaoDb();

            MessageBox.Show("Backup criado com sucesso.", "Concluído",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.ReabrirLigacaoDb();
            MessageBox.Show($"Ocorreu um erro ao criar o backup:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Restaurar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de backup",
            Filter = "Base de dados (*.db)|*.db|Todos os ficheiros (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var confirmacao = new ConfirmacaoTextoWindow(
            "Esta ação vai SUBSTITUIR todos os dados atuais pelos do ficheiro de backup selecionado.\n\n" +
            "Esta operação não pode ser desfeita.",
            "RESTAURAR")
        { Owner = Window.GetWindow(this) };
        confirmacao.ShowDialog();
        if (!confirmacao.Confirmado) return;

        try
        {
            App.RestaurarBackup(dialog.FileName);
            MessageBox.Show(
                "Backup restaurado com sucesso.\n\nRecomenda-se fechar e reabrir a aplicação para garantir que " +
                "todos os ecrãs refletem os dados restaurados.",
                "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.ReabrirLigacaoDb();
            MessageBox.Show($"Ocorreu um erro ao restaurar o backup:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApagarTudo_Click(object sender, RoutedEventArgs e)
    {
        var confirmacao = new ConfirmacaoTextoWindow(
            "Está prestes a apagar PERMANENTEMENTE todos os dados da aplicação " +
            "(agrupamentos, escolas, pedidos, intervenções, atividades da DISIA, equipamentos, abates e contactos).\n\n" +
            "Considere fazer primeiro um backup. Esta operação não pode ser desfeita.",
            "APAGAR")
        { Owner = Window.GetWindow(this) };
        confirmacao.ShowDialog();
        if (!confirmacao.Confirmado) return;

        try
        {
            App.ApagarTudo();
            MessageBox.Show(
                "Todos os dados foram apagados. A base de dados foi recriada vazia.\n\n" +
                "Recomenda-se fechar e reabrir a aplicação.",
                "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            App.ReabrirLigacaoDb();
            MessageBox.Show($"Ocorreu um erro ao apagar a base de dados:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
