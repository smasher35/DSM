using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;

namespace LeiriaDISIA.Views;

public partial class AdministracaoWindow : Window
{
    public AdministracaoWindow()
    {
        InitializeComponent();
        TxtCaminhoDb.Text = AppDbContext.DbPath;
        TxtCaminhoBackups.Text = App.PastaBackupsAutomaticos;
        ChkBackupAutomatico.IsChecked = AppSettingsService.BackupAutomaticoAtivo;
        RecarregarUtilizadores();

        ListaGrupos.ItemsSource = GruposValorFixo.Todos
            .Select(g => new GrupoItem(g.Grupo, g.Rotulo))
            .OrderBy(g => g.Rotulo, StringComparer.Create(new System.Globalization.CultureInfo("pt-PT"), ignoreCase: true))
            .ToList();

        CmbTabelaEliminar.ItemsSource = TabelasEliminaveis
            .Select(t => new TabelaEliminar { Chave = t.Chave, Rotulo = t.Rotulo })
            .OrderBy(t => t.Rotulo, StringComparer.Create(new System.Globalization.CultureInfo("pt-PT"), ignoreCase: true))
            .ToList();

        if (Services.ThemeService.TemaAtual == Services.TemaAplicacao.Escuro)
            RadioTemaEscuro.IsChecked = true;
        else
            RadioTemaClaro.IsChecked = true;

        if (Services.DashboardResolucaoService.UhdAtivo)
            RadioResolucaoUhd.IsChecked = true;
        else
            RadioResolucaoFhd.IsChecked = true;

        TxtVersaoApp.Text = Services.AppSettingsService.VersaoApp;

        // Preencher os campos de Email (SMTP) com a configuração guardada
        TxtSmtpServidor.Text = AppSettingsService.SmtpServidor;
        TxtSmtpPorta.Text = AppSettingsService.SmtpPorta.ToString();
        TxtSmtpUtilizador.Text = AppSettingsService.SmtpUtilizador;
        TxtSmtpPassword.Password = AppSettingsService.SmtpPassword;
        ChkSmtpSsl.IsChecked = AppSettingsService.SmtpUsarSsl;
        TxtSmtpNomeRemetente.Text = AppSettingsService.SmtpNomeRemetente;
        TxtSmtpEmailRemetente.Text = AppSettingsService.SmtpEmailRemetente;

        // Preencher os campos de Obsolescência com a configuração guardada
        TxtPesoIdade.Text = AppSettingsService.ObsolescenciaPesoIdade.ToString();
        TxtPesoRam.Text = AppSettingsService.ObsolescenciaPesoRam.ToString();
        TxtPesoDisco.Text = AppSettingsService.ObsolescenciaPesoDisco.ToString();
        TxtPesoProcessador.Text = AppSettingsService.ObsolescenciaPesoProcessador.ToString();
        TxtLimiarMonitorizar.Text = AppSettingsService.ObsolescenciaLimiarMonitorizar.ToString();
        TxtLimiarObsoleto.Text = AppSettingsService.ObsolescenciaLimiarObsoleto.ToString();
        AtualizarSomaPesos();

        if (AppSettingsService.UltimoErroPersistencia is { } erroConfig)
        {
            MessageBox.Show(
                "Não foi possível ler a configuração de email guardada anteriormente, pelo que estão a ser " +
                "apresentados os valores de omissão.\n\n" +
                $"Motivo: {erroConfig}",
                "Aviso — Configuração de Email", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        AtualizarPainelIaLocal();
        TxtPastaSugeridaIa.Text = $"Pasta sugerida: {IaLocalService.PastaModelosPorOmissao}";

        // Carregar escolas desativadas na inicialização
        RecarregarEscolasDesativadas();
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    // =========================================================================
    // ABA: APARÊNCIA
    // =========================================================================

    private void RadioTemaClaro_Checked(object sender, RoutedEventArgs e) =>
        Services.ThemeService.Aplicar(Services.TemaAplicacao.Claro);

    private void RadioTemaEscuro_Checked(object sender, RoutedEventArgs e) =>
        Services.ThemeService.Aplicar(Services.TemaAplicacao.Escuro);

    private void RadioResolucaoFhd_Checked(object sender, RoutedEventArgs e) =>
        Services.DashboardResolucaoService.Aplicar(uhd: false);

    private void RadioResolucaoUhd_Checked(object sender, RoutedEventArgs e) =>
        Services.DashboardResolucaoService.Aplicar(uhd: true);

    private void GuardarVersao_Click(object sender, RoutedEventArgs e)
    {
        var versao = TxtVersaoApp.Text.Trim();
        if (string.IsNullOrWhiteSpace(versao))
        {
            MessageBox.Show("Indique o texto da versão.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Services.AppSettingsService.VersaoApp = versao;
        // Atualiza a sidebar do menu principal se estiver aberto
        if (Owner is Views.MainWindow main)
            main.AtualizarVersaoSidebar();
        MessageBox.Show("Versão atualizada com sucesso.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =========================================================================
    // ABA: INTELIGÊNCIA ARTIFICIAL LOCAL
    // =========================================================================

    /// <summary>Atualiza o caminho e o estado apresentados na aba, refletindo a configuração
    /// atualmente guardada (ver <see cref="IaLocalService.CaminhoModeloConfigurado"/>).</summary>
    private void AtualizarPainelIaLocal()
    {
        var caminho = IaLocalService.CaminhoModeloConfigurado;
        TxtCaminhoModeloIa.Text = caminho ?? "(nenhum modelo configurado)";

        if (string.IsNullOrWhiteSpace(caminho))
        {
            TxtEstadoModeloIa.Text = "Ainda não escolheu nenhum ficheiro de modelo.";
        }
        else if (!File.Exists(caminho))
        {
            TxtEstadoModeloIa.Text = "⚠ O ficheiro configurado já não existe neste caminho — escolha novamente.";
        }
        else
        {
            var tamanhoMb = new FileInfo(caminho).Length / 1024.0 / 1024.0;
            TxtEstadoModeloIa.Text = $"✔ Modelo configurado ({tamanhoMb:N0} MB). Use 'Testar Modelo' para confirmar que funciona.";
        }
    }

    private void ProcurarModeloIa_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher ficheiro do modelo de IA (GGUF)",
            Filter = "Modelos GGUF (*.gguf)|*.gguf|Todos os ficheiros (*.*)|*.*",
            InitialDirectory = Directory.Exists(IaLocalService.PastaModelosPorOmissao)
                ? IaLocalService.PastaModelosPorOmissao : null
        };
        if (dialog.ShowDialog() != true) return;

        IaLocalService.CaminhoModeloConfigurado = dialog.FileName;
        AtualizarPainelIaLocal();
    }

    private void RemoverModeloIa_Click(object sender, RoutedEventArgs e)
    {
        var confirmar = MessageBox.Show(
            "Tem a certeza que pretende remover a configuração do modelo de IA local? O botão 'Gerar com IA " +
            "Local' (em Relatórios) deixa de funcionar até escolher novamente um ficheiro de modelo.",
            "Remover Configuração", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmar != MessageBoxResult.Yes) return;

        IaLocalService.CaminhoModeloConfigurado = null;
        AtualizarPainelIaLocal();
    }

    /// <summary>Carrega o modelo configurado e pede-lhe uma frase curta, só para confirmar que
    /// está tudo bem instalado antes de o usar a sério em Relatórios. Corre em segundo plano
    /// (pode demorar bastante na primeira vez, enquanto o modelo é carregado para memória).</summary>
    private async void TestarModeloIa_Click(object sender, RoutedEventArgs e)
    {
        if (!IaLocalService.ModeloDisponivel)
        {
            MessageBox.Show("Escolha primeiro um ficheiro de modelo válido.", "Nada para testar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnTestarModeloIa.IsEnabled = false;
        TxtEstadoModeloIa.Text = "⏳ A carregar o modelo e a gerar uma resposta de teste... Isto pode demorar 1-2 minutos.";

        try
        {
            var resposta = await Task.Run(() => IaLocalService.Instancia.GerarTextoAsync(
                "Escreve, em português de Portugal, uma frase curta a confirmar que estás a funcionar corretamente. ###FIM###",
                maxTokens: 60));

            TxtEstadoModeloIa.Text = $"✔ Modelo a funcionar. Resposta de teste: \u201c{resposta.Trim()}\u201d";
        }
        catch (Exception ex)
        {
            TxtEstadoModeloIa.Text = $"⚠ Falha ao testar o modelo: {ex.Message}";
            MessageBox.Show($"Não foi possível testar o modelo:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnTestarModeloIa.IsEnabled = true;
        }
    }

    // =========================================================================
    // ABA: EMAIL (SMTP)
    // =========================================================================

    private void GuardarSmtp_Click(object sender, RoutedEventArgs e)
    {
        // IMPORTANTE: ao contrário da validação de "Novo Utilizador", aqui NUNCA se deve bloquear
        // o guardar por completo - senão perdem-se campos já corretamente preenchidos só porque
        // outro campo ainda está em falta. Guarda-se sempre o que está no formulário e avisa-se,
        // à parte, se a configuração ainda não está completa para poder enviar emails.

        var porta = AppSettingsService.SmtpPorta;
        var portaInvalida = !string.IsNullOrWhiteSpace(TxtSmtpPorta.Text) && !int.TryParse(TxtSmtpPorta.Text, out porta);
        if (portaInvalida)
            porta = AppSettingsService.SmtpPorta; // mantém a última porta válida guardada

        AppSettingsService.SmtpServidor = TxtSmtpServidor.Text.Trim();
        AppSettingsService.SmtpPorta = porta;
        AppSettingsService.SmtpUtilizador = TxtSmtpUtilizador.Text.Trim();
        AppSettingsService.SmtpPassword = TxtSmtpPassword.Password;
        AppSettingsService.SmtpUsarSsl = ChkSmtpSsl.IsChecked == true;
        AppSettingsService.SmtpNomeRemetente = TxtSmtpNomeRemetente.Text.Trim();
        AppSettingsService.SmtpEmailRemetente = TxtSmtpEmailRemetente.Text.Trim();

        // Se a gravação em disco falhou por qualquer motivo (permissões, disco, etc.), avisar de imediato.
        if (AppSettingsService.UltimoErroPersistencia is { } erroDisco)
        {
            MessageBox.Show(
                "Não foi possível guardar a configuração no disco.\n\n" +
                $"Motivo: {erroDisco}\n\n" +
                "Verifique se a aplicação tem permissões de escrita na pasta de dados do utilizador.",
                "Falha ao guardar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var emFalta = new List<string>();
        if (portaInvalida)
            emFalta.Add("• a porta indicada não é válida — foi mantida a anterior");
        if (string.IsNullOrWhiteSpace(TxtSmtpServidor.Text))
            emFalta.Add("• o servidor SMTP");
        if (string.IsNullOrWhiteSpace(TxtSmtpEmailRemetente.Text))
            emFalta.Add("• o email do remetente");
        else if (!EmailService.EmailValido(TxtSmtpEmailRemetente.Text))
            emFalta.Add("• o email do remetente não é válido");

        if (emFalta.Count > 0)
        {
            MessageBox.Show(
                "As alterações foram guardadas, mas a configuração ainda está incompleta:\n\n" +
                string.Join("\n", emFalta) +
                "\n\nAté isto ficar corrigido, o envio automático de emails não vai funcionar.",
                "Guardado — configuração incompleta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show("Configuração de email guardada com sucesso.", "Concluído",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TestarSmtp_Click(object sender, RoutedEventArgs e)
    {
        // Guardar primeiro os dados atuais para o teste refletir o que está no ecrã
        GuardarSmtp_Click(sender, e);

        if (!AppSettingsService.SmtpConfigurado)
            return; // GuardarSmtp_Click já mostrou o aviso adequado

        var janela = new InputTextoWindow(
            "Introduza o endereço de email para onde pretende enviar a mensagem de teste:",
            AppSettingsService.SmtpEmailRemetente)
        { Owner = this };
        janela.ShowDialog();

        var destino = janela.TextoIntroduzido?.Trim();
        if (string.IsNullOrWhiteSpace(destino))
            return;

        if (!EmailService.EmailValido(destino))
        {
            MessageBox.Show("O email introduzido não é válido.", "Email inválido",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            EmailService.EnviarEmailTeste(destino);
            MessageBox.Show($"Email de teste enviado com sucesso para {destino}.", "Concluído",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível enviar o email de teste:\n{ex.Message}",
                "Falha no envio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void AjudaSmtp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "GMAIL\n" +
            "  Servidor: smtp.gmail.com\n" +
            "  Porta: 587      SSL/TLS: ativo\n" +
            "  Utilizador: o seu endereço Gmail completo\n" +
            "  Palavra-passe: NÃO é a palavra-passe normal da conta.\n" +
            "    1. Ative a verificação em dois passos na conta Google.\n" +
            "    2. Aceda a myaccount.google.com/apppasswords\n" +
            "    3. Crie uma nova \"palavra-passe de aplicação\" (ex: nome \"LeiriaDISIA\").\n" +
            "    4. Copie o código de 16 caracteres gerado e cole-o no campo \"Palavra-passe SMTP\".\n\n" +
            "OUTLOOK / MICROSOFT 365\n" +
            "  Servidor: smtp.office365.com\n" +
            "  Porta: 587      SSL/TLS: ativo\n" +
            "  Utilizador/Palavra-passe: os da própria conta (pode também exigir uma palavra-passe de aplicação, " +
            "consoante as políticas de segurança da organização).\n\n" +
            "SERVIDOR PRÓPRIO / OUTRO FORNECEDOR\n" +
            "  Peça ao fornecedor de email ou ao departamento de informática o nome do servidor SMTP, a porta " +
            "e as credenciais a utilizar.\n\n" +
            "Depois de preencher os campos, clique em \"Guardar Configuração\" e depois em " +
            "\"Enviar Email de Teste...\" para confirmar que tudo está a funcionar.",
            "Como configurar o envio de email", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =========================================================================
    // ABA: OBSOLESCÊNCIA DE EQUIPAMENTO
    // =========================================================================

    private void PesoObsolescencia_TextChanged(object sender, TextChangedEventArgs e) => AtualizarSomaPesos();

    /// <summary>Mostra em tempo real a soma dos 4 pesos, para ajudar o utilizador a perceber se
    /// ainda soma 100% (a soma não é bloqueada - o cálculo normaliza os pesos de qualquer forma).</summary>
    private void AtualizarSomaPesos()
    {
        if (TxtSomaPesos == null) return; // ainda a inicializar

        var soma = LerInteiro(TxtPesoIdade?.Text) + LerInteiro(TxtPesoRam?.Text) +
                   LerInteiro(TxtPesoDisco?.Text) + LerInteiro(TxtPesoProcessador?.Text);

        TxtSomaPesos.Text = $"Soma atual: {soma}%";
        TxtSomaPesos.Foreground = soma == 100
            ? (Brush)FindResource("BrushTextSecondary")
            : (Brush)FindResource("BrushWarning");
    }

    private static int LerInteiro(string? texto) => int.TryParse(texto, out var valor) ? valor : 0;

    private void GuardarObsolescencia_Click(object sender, RoutedEventArgs e)
    {
        var pesoIdade = LerInteiro(TxtPesoIdade.Text);
        var pesoRam = LerInteiro(TxtPesoRam.Text);
        var pesoDisco = LerInteiro(TxtPesoDisco.Text);
        var pesoProcessador = LerInteiro(TxtPesoProcessador.Text);
        var limiarMonitorizar = LerInteiro(TxtLimiarMonitorizar.Text);
        var limiarObsoleto = LerInteiro(TxtLimiarObsoleto.Text);

        if (pesoIdade < 0 || pesoRam < 0 || pesoDisco < 0 || pesoProcessador < 0 || (pesoIdade + pesoRam + pesoDisco + pesoProcessador) == 0)
        {
            MessageBox.Show("Os pesos têm de ser números positivos, e pelo menos um deles maior do que zero.",
                "Dados inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (limiarMonitorizar is < 0 or > 100 || limiarObsoleto is < 0 or > 100 || limiarObsoleto <= limiarMonitorizar)
        {
            MessageBox.Show("Os limiares têm de estar entre 0 e 100, e o limiar de \"Obsoleto\" tem de ser maior do que o de \"A Monitorizar\".",
                "Dados inválidos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppSettingsService.ObsolescenciaPesoIdade = pesoIdade;
        AppSettingsService.ObsolescenciaPesoRam = pesoRam;
        AppSettingsService.ObsolescenciaPesoDisco = pesoDisco;
        AppSettingsService.ObsolescenciaPesoProcessador = pesoProcessador;
        AppSettingsService.ObsolescenciaLimiarMonitorizar = limiarMonitorizar;
        AppSettingsService.ObsolescenciaLimiarObsoleto = limiarObsoleto;

        var soma = pesoIdade + pesoRam + pesoDisco + pesoProcessador;
        var aviso = soma != 100
            ? $"\n\n(Nota: os pesos somam {soma}%, não 100% - a aplicação normaliza-os automaticamente no cálculo, mas para clareza é recomendável ajustá-los para somarem 100%.)"
            : "";

        MessageBox.Show("Configuração de obsolescência guardada com sucesso." + aviso, "Concluído",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AjudaObsolescencia_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Cada equipamento recebe um score de 0 (recente/topo de gama) a 100 (claramente obsoleto), " +
            "calculado a partir de até 4 critérios:\n\n" +
            "• IDADE: idade do equipamento face à vida útil típica do seu tipo (ex: computadores ~6 anos, " +
            "monitores ~8 anos). Precisa da \"Data de Aquisição\" preenchida.\n\n" +
            "• RAM: quantidade de memória, só para computadores. Menos de 4GB pesa mais; 16GB ou mais não penaliza.\n\n" +
            "• DISCO: tipo de disco, só para computadores. HDD pesa mais; NVMe não penaliza.\n\n" +
            "• PROCESSADOR: tenta reconhecer a geração aproximada a partir do texto de \"Processador\" e " +
            "\"Família/Versão do Processador\" (ex: \"12ª Geração\", \"i5-12400\", \"Ryzen 5 5600G\"). " +
            "É uma estimativa - texto não reconhecido é ignorado (não penaliza nem beneficia).\n\n" +
            "Critérios sem dados preenchidos são excluídos do cálculo, e o respetivo peso é redistribuído " +
            "pelos restantes - um equipamento nunca é penalizado só por faltar um campo opcional.\n\n" +
            "Se não houver dados suficientes (nem idade, nem especificações), aparece como \"Sem dados\", " +
            "em vez de uma classificação enganosa.\n\n" +
            "Os limiares definem a partir de que score (%) o equipamento passa a \"A Monitorizar\" e a \"Obsoleto\".",
            "Como funciona o cálculo de obsolescência", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =========================================================================
    // ABA: UTILIZADORES
    // =========================================================================

    private void RecarregarUtilizadores()
    {
        GridUtilizadores.ItemsSource = App.Db.Usuarios.OrderBy(u => u.NomeUtilizador).ToList();
    }

    private void NovoUtilizador_Click(object sender, RoutedEventArgs e)
    {
        var janela = new UsuarioEditWindow(null) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) RecarregarUtilizadores();
    }

    private void GridUtilizadores_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AbrirEdicaoUtilizador();
    private void EditarUtilizador_Click(object sender, RoutedEventArgs e) => AbrirEdicaoUtilizador();

    private void AbrirEdicaoUtilizador()
    {
        if (GridUtilizadores.SelectedItem is not Usuario usuario)
        {
            MessageBox.Show("Selecione um utilizador para editar.", "Ação necessária",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var janela = new UsuarioEditWindow(usuario) { Owner = this };
        janela.ShowDialog();
        if (janela.Sucesso) RecarregarUtilizadores();
    }

    private void EliminarUtilizador_Click(object sender, RoutedEventArgs e)
    {
        if (GridUtilizadores.SelectedItem is not Usuario usuario) return;

        if (usuario.Id == SessaoAtual.UtilizadorLogado?.Id)
        {
            MessageBox.Show("Não pode eliminar o utilizador com a sessão atualmente iniciada.",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (App.Db.Usuarios.Count(u => u.Perfil == PerfilUtilizador.Administrador) <= 1 &&
            usuario.Perfil == PerfilUtilizador.Administrador)
        {
            MessageBox.Show("Não é possível eliminar o último utilizador com perfil Administrador.",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Eliminar o utilizador '{usuario.NomeUtilizador}'?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        App.Db.Usuarios.Remove(usuario);
        App.Db.SaveChanges();
        RecarregarUtilizadores();
    }

    // =========================================================================
    // ABA: BASE DE DADOS
    // =========================================================================

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

    private void AbrirPastaBackups_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(App.PastaBackupsAutomaticos);
            Process.Start(new ProcessStartInfo("explorer.exe", App.PastaBackupsAutomaticos) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir a pasta de backups:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopiarCaminho_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(AppDbContext.DbPath);
        MessageBox.Show("Caminho copiado para a área de transferência.", "Concluído",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopiarCaminhoBackups_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(App.PastaBackupsAutomaticos);
        MessageBox.Show("Caminho copiado para a área de transferência.", "Concluído",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ChkBackupAutomatico_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsService.BackupAutomaticoAtivo = ChkBackupAutomatico.IsChecked == true;
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
            "Esta ação vai SUBSTITUIR todos os dados atuais (incluindo utilizadores) pelos do ficheiro de backup selecionado.\n\n" +
            "Esta operação não pode ser desfeita.",
            "RESTAURAR")
        { Owner = this };
        confirmacao.ShowDialog();
        if (!confirmacao.Confirmado) return;

        try
        {
            App.RestaurarBackup(dialog.FileName);
            RecarregarUtilizadores();
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
            "Está prestes a apagar PERMANENTEMENTE todos os dados da aplicação — agrupamentos, " +
            "escolas, pedidos, intervenções, atividades da DISIA, equipamentos, abates, contactos, " +
            "comunicações e Dados Fixos.\n\n" +
            "Os Dados Fixos e as Categorias voltam de imediato aos valores por omissão, para a " +
            "aplicação continuar utilizável. Apenas os Utilizadores e respetivos acessos são " +
            "preservados. Considere fazer primeiro um backup.",
            "APAGAR")
        { Owner = this };
        confirmacao.ShowDialog();
        if (!confirmacao.Confirmado) return;

        try
        {
            App.ApagarTudo();
            MessageBox.Show("Todos os dados foram apagados. Os Dados Fixos e as Categorias foram repostos com os valores por omissão.", "Concluído",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro ao apagar a base de dados:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================================================================
    // ABA: DADOS FIXOS
    // =========================================================================

    private record GrupoItem(string Grupo, string Rotulo);

    private ValorListaItem? _valorSelecionado;
    private string? _grupoAtual;

    /// <summary>(1.3) Grupo de características atualmente exibido/gerido na secção "Adicionar /
    /// Editar Característica" e na grelha <c>GridCaracteristicas</c>, em baixo (ex.: "Computador",
    /// "Energia"). Só é relevante quando <see cref="_grupoAtual"/> é <see cref="GruposValorFixo.TipoEquipamento"/>.</summary>
    private string? _grupoCaracteristicasPainel;

    private CaracteristicaEquipamento? _caracteristicaSelecionada;

    /// <summary>Tipos de grupo geridos na aba "Dados Fixos". A maioria dos grupos são "simples"
    /// (guardados na tabela ValoresFixos); os grupos de Categorias e Estados são "ligados" às
    /// tabelas/registos reais para que fiquem sempre em sincronia com as dropdowns dos formulários
    /// de inserção/edição (ver nota em <see cref="GruposValorFixo"/>).</summary>
    private enum TipoGrupoValores { Simples, CategoriaDisia, CategoriaIntervencao, EstadoIntervencaoAtividade, EstadoPedido }

    /// <summary>Item de apresentação uniforme na grelha de valores, seja qual for a origem real
    /// dos dados (ValorFixo, CategoriaDisia, CategoriaIntervencao ou EstadoCorPersonalizada).</summary>
    private class ValorListaItem
    {
        public int Id { get; set; }
        public string Valor { get; set; } = string.Empty;
        public int Ordem { get; set; }
        public bool Ativo { get; set; } = true;

        /// <summary>(1.1) Só usado no grupo "Tipos de Equipamento" — ver <see cref="ValorFixo.GrupoCaracteristicas"/>.</summary>
        public string? GrupoCaracteristicas { get; set; }
    }

    private static TipoGrupoValores ObterTipoGrupo(string grupo) => grupo switch
    {
        GruposValorFixo.CategoriaAtividadeDisia => TipoGrupoValores.CategoriaDisia,
        GruposValorFixo.CategoriaIntervencao => TipoGrupoValores.CategoriaIntervencao,
        GruposValorFixo.EstadoIntervencaoEAtividadeDisia => TipoGrupoValores.EstadoIntervencaoAtividade,
        GruposValorFixo.EstadoPedidoIntervencao => TipoGrupoValores.EstadoPedido,
        _ => TipoGrupoValores.Simples
    };

    private void ListaGrupos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListaGrupos.SelectedItem is not GrupoItem item) return;

        _grupoAtual = item.Grupo;
        TxtTituloGrupo.Text = item.Rotulo;

        var tipo = ObterTipoGrupo(_grupoAtual);
        var ligado = tipo != TipoGrupoValores.Simples;
        var fixo = tipo is TipoGrupoValores.EstadoIntervencaoAtividade or TipoGrupoValores.EstadoPedido;

        // (1.1)/(1.3) O seletor de "Características Específicas" e a gestão inline das
        // características do grupo (em baixo) só fazem sentido no grupo Tipos de Equipamento.
        var ehTipoEquipamento = _grupoAtual == GruposValorFixo.TipoEquipamento;
        PainelGrupoCaracteristicas.Visibility = ehTipoEquipamento ? Visibility.Visible : Visibility.Collapsed;
        ColGrupoCaracteristicasValores.Visibility = ehTipoEquipamento ? Visibility.Visible : Visibility.Collapsed;
        PainelCaracteristicasGrupo.Visibility = ehTipoEquipamento ? Visibility.Visible : Visibility.Collapsed;
        PainelAdicionarCaracteristica.Visibility = ehTipoEquipamento ? Visibility.Visible : Visibility.Collapsed;
        // Nota: a altura de GridValores/GridCaracteristicas já não é definida aqui em pixels — é
        // controlada pelo layout responsivo (Grid.Row="*" / "Auto") em AdministracaoWindow.xaml,
        // que se adapta automaticamente à resolução do ecrã (1920×1080 e 2560×1440).
        // Recarrega sempre (em vez de só na primeira vez) para que grupos novos criados a partir
        // daqui (ex.: "Energia") fiquem também disponíveis para escolher noutros Tipos de Equipamento.
        if (ehTipoEquipamento)
        {
            var grupos = ObterGruposCaracteristicasDisponiveis();
            CmbGrupoCaracteristicas.ItemsSource = grupos;
            CmbGrupoCaracteristicasPainel.ItemsSource = grupos;
        }
        else
        {
            GridCaracteristicas.ItemsSource = null;
        }

        // Grupos ligados a Categorias/Estados: sem "Ordem" manual (categorias ordenam-se
        // alfabeticamente; estados mantêm a ordem fixa do fluxo de negócio).
        PainelOrdem.Visibility = ligado ? Visibility.Collapsed : Visibility.Visible;
        ColOrdemValores.Visibility = ligado ? Visibility.Collapsed : Visibility.Visible;
        // "Ativo" só faz sentido nos grupos simples e nas Categorias de Intervenção (têm um
        // campo "Ativa" próprio); nos restantes grupos ligados esconde-se a coluna/checkbox.
        var mostraAtivo = tipo is TipoGrupoValores.Simples or TipoGrupoValores.CategoriaIntervencao;
        ColAtivoValores.Visibility = mostraAtivo ? Visibility.Visible : Visibility.Collapsed;
        ChkAtivo.Visibility = mostraAtivo ? Visibility.Visible : Visibility.Collapsed;
        // Estados são um conjunto fixo (associados à lógica de negócio): só se pode renomear o
        // nome apresentado, nunca criar nem eliminar.
        BtnNovoValor.IsEnabled = !fixo;
        BtnEliminarValor.IsEnabled = !fixo;
        TxtAvisoGrupoLigado.Visibility = ligado ? Visibility.Visible : Visibility.Collapsed;
        TxtAvisoGrupoLigado.Text = fixo
            ? "Estes estados fazem parte do fluxo de negócio e não podem ser criados/eliminados aqui — apenas pode alterar o nome apresentado. A cor altera-se no respetivo botão de cores, à esquerda."
            : "Este é o nome oficial da categoria, também usado nas listas de seleção dos formulários. A cor associada altera-se no respetivo botão de cores, à esquerda.";

        RecarregarValores();
        NovoValor_Click(sender, e);
    }

    /// <summary>(1.1) Une os grupos de características "embutidos" (Computador, Monitor, etc. — que
    /// têm campos fixos próprios no formulário de Equipamento) aos grupos "personalizados" que o
    /// administrador já tenha criado ao escrever um nome novo em <see cref="CmbGrupoCaracteristicas"/>
    /// (ex.: "Energia"), quer estejam já associados a algum Tipo de Equipamento (<see cref="ValorFixo.GrupoCaracteristicas"/>)
    /// quer já tenham alguma característica definida (<see cref="CaracteristicaEquipamento.GrupoCaracteristicas"/>).
    /// Assim, um grupo novo criado uma vez fica disponível para reutilizar noutros Tipos de Equipamento,
    /// sem precisar de o voltar a escrever.</summary>
    private static List<string> ObterGruposCaracteristicasDisponiveis()
    {
        var personalizados = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento && v.GrupoCaracteristicas != null)
            .Select(v => v.GrupoCaracteristicas!)
            .Concat(App.Db.CaracteristicasEquipamento.Select(c => c.GrupoCaracteristicas))
            .ToList();

        return GruposCaracteristicasEquipamento.Todos
            .Concat(personalizados)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.Create(new System.Globalization.CultureInfo("pt-PT"), ignoreCase: true))
            .ToList();
    }

    /// <summary>Normaliza o texto escrito/escolhido em <see cref="CmbGrupoCaracteristicas"/>: se
    /// corresponder (ignorando maiúsculas/minúsculas) a um grupo já existente, devolve esse grupo
    /// tal como já está gravado (evita ficarem dois grupos quase iguais, ex.: "energia" e "Energia").
    /// Em branco, devolve o grupo "Genérico".</summary>
    private static string NormalizarGrupoCaracteristicas(string? texto)
    {
        var valor = (texto ?? string.Empty).Trim();
        if (valor.Length == 0) return GruposCaracteristicasEquipamento.Generico;

        var existente = ObterGruposCaracteristicasDisponiveis()
            .FirstOrDefault(g => string.Equals(g, valor, StringComparison.OrdinalIgnoreCase));
        return existente ?? valor;
    }

    /// <summary>(1.3) Recarrega a grelha <c>GridCaracteristicas</c> com as características do grupo
    /// indicado, e atualiza o título da secção. Chamado sempre que o grupo em gestão muda — seja por
    /// selecionar um Tipo de Equipamento na grelha acima, seja por escolher/escrever outro grupo
    /// diretamente na combo <c>CmbGrupoCaracteristicasPainel</c>.</summary>
    /// <summary>(Dados Fixos v2) Item da combo "Aplica-se apenas a": Id=null representa o valor por
    /// omissão (característica partilhada por todos os Tipos deste grupo).</summary>
    private record ItemTipoEspecifico(int? Id, string Rotulo);

    /// <summary>(Dados Fixos v2) Item da combo "É subtipo de": Id=null representa o valor por
    /// omissão (característica independente, sem subtipo).</summary>
    private record ItemCaracteristicaPai(int? Id, string Nome);

    private void RecarregarCaracteristicasGrupo(string grupo)
    {
        _grupoCaracteristicasPainel = grupo;
        TxtTituloCaracteristicasGrupo.Text = $"Características do Grupo — {grupo}";
        GridCaracteristicas.ItemsSource = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupo)
            .OrderBy(c => c.Ordem)
            .ThenBy(c => c.Nome)
            .ToList();

        // (Dados Fixos v2) "Aplica-se apenas a": só os Tipos de Equipamento já associados a este
        // grupo de características (ver ValorFixo.GrupoCaracteristicas) fazem sentido aqui.
        var tiposDoGrupo = App.Db.ValoresFixos
            .Where(v => v.Grupo == GruposValorFixo.TipoEquipamento && v.GrupoCaracteristicas == grupo)
            .OrderBy(v => v.Ordem).ThenBy(v => v.Valor)
            .Select(v => new ItemTipoEspecifico(v.Id, v.Valor))
            .ToList();
        tiposDoGrupo.Insert(0, new ItemTipoEspecifico(null, "Todos os tipos deste grupo"));
        CmbTipoEspecificoCaracteristica.ItemsSource = tiposDoGrupo;

        // (Dados Fixos v2) "É subtipo de": qualquer outra característica deste grupo que, ela
        // própria, ainda não seja uma característica-filha — mantém a hierarquia a um único nível,
        // como desenhado (Q3), em vez de permitir cadeias de subtipos dentro de subtipos.
        var caracteristicasParaPai = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupo && c.CaracteristicaPaiId == null
                        && (_caracteristicaSelecionada == null || c.Id != _caracteristicaSelecionada.Id))
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .Select(c => new ItemCaracteristicaPai(c.Id, c.Nome))
            .ToList();
        caracteristicasParaPai.Insert(0, new ItemCaracteristicaPai(null, "Nenhuma — característica independente"));
        CmbCaracteristicaPai.ItemsSource = caracteristicasParaPai;

        NovaCaracteristica_Click(this, new RoutedEventArgs());
    }

    /// <summary>Chamado quando o utilizador escolhe um item da lista da combo (não quando apenas
    /// escreve texto livre — ver <see cref="CmbGrupoCaracteristicasPainel_LostFocus"/>).</summary>
    private void CmbGrupoCaracteristicasPainel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbGrupoCaracteristicasPainel.SelectedItem is not string grupo) return;
        if (grupo == _grupoCaracteristicasPainel) return;
        RecarregarCaracteristicasGrupo(grupo);
    }

    /// <summary>Chamado quando o utilizador escreve um nome de grupo novo (ex.: "Energia") na combo
    /// e sai do campo, sem o escolher de uma lista — permite gerir características de um grupo que
    /// ainda não tenha nenhum Tipo de Equipamento associado.</summary>
    private void CmbGrupoCaracteristicasPainel_LostFocus(object sender, RoutedEventArgs e)
    {
        var grupo = NormalizarGrupoCaracteristicas(CmbGrupoCaracteristicasPainel.Text);
        if (grupo == _grupoCaracteristicasPainel) return;
        CmbGrupoCaracteristicasPainel.Text = grupo;
        RecarregarCaracteristicasGrupo(grupo);
    }

    private void GridCaracteristicas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _caracteristicaSelecionada = GridCaracteristicas.SelectedItem as CaracteristicaEquipamento;
        BtnGerirValoresCaracteristica.IsEnabled = _caracteristicaSelecionada != null;
        BtnEliminarCaracteristica.IsEnabled = _caracteristicaSelecionada != null;
        if (_caracteristicaSelecionada == null) return;

        TxtNomeCaracteristica.Text = _caracteristicaSelecionada.Nome;
        TxtOrdemCaracteristica.Text = _caracteristicaSelecionada.Ordem.ToString();
        ChkAtivoCaracteristica.IsChecked = _caracteristicaSelecionada.Ativo;
        CmbTipoEspecificoCaracteristica.SelectedValue = _caracteristicaSelecionada.TipoEquipamentoId;

        // Retira a própria característica selecionada da lista de possíveis "pais" (não pode ser
        // subtipo de si mesma) antes de preencher o valor gravado.
        var candidatosPai = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == _grupoCaracteristicasPainel && c.CaracteristicaPaiId == null
                        && c.Id != _caracteristicaSelecionada.Id)
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .Select(c => new ItemCaracteristicaPai(c.Id, c.Nome))
            .ToList();
        candidatosPai.Insert(0, new ItemCaracteristicaPai(null, "Nenhuma — característica independente"));
        CmbCaracteristicaPai.ItemsSource = candidatosPai;
        CmbCaracteristicaPai.SelectedValue = _caracteristicaSelecionada.CaracteristicaPaiId;
        CmbValorPorOmissaoCaracteristica.ItemsSource = App.Db.CaracteristicaEquipamentoOpcoes
            .Where(o => o.CaracteristicaEquipamentoId == _caracteristicaSelecionada.Id && o.Ativo)
            .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
            .Select(o => o.Valor)
            .ToList();
        CmbValorPorOmissaoCaracteristica.Text = _caracteristicaSelecionada.ValorPorOmissao ?? string.Empty;
    }

    private void NovaCaracteristica_Click(object sender, RoutedEventArgs e)
    {
        _caracteristicaSelecionada = null;
        GridCaracteristicas.SelectedItem = null;
        TxtNomeCaracteristica.Clear();
        CmbTipoEspecificoCaracteristica.SelectedValue = null;
        CmbCaracteristicaPai.SelectedValue = null;
        CmbValorPorOmissaoCaracteristica.ItemsSource = null;
        CmbValorPorOmissaoCaracteristica.Text = string.Empty;
        var proximaOrdem = (GridCaracteristicas.ItemsSource as IEnumerable<CaracteristicaEquipamento>)?.Count() ?? 0;
        TxtOrdemCaracteristica.Text = proximaOrdem.ToString();
        ChkAtivoCaracteristica.IsChecked = true;
        BtnGerirValoresCaracteristica.IsEnabled = false;
        BtnEliminarCaracteristica.IsEnabled = false;
    }

    /// <summary>Não permite nomes repetidos (ignorando maiúsculas/minúsculas e espaços extra)
    /// dentro do mesmo grupo de características.</summary>
    private bool ExisteNomeCaracteristicaRepetido(string nome, out string nomeExistente)
    {
        nomeExistente = string.Empty;
        var nomeNormalizado = nome.Trim();
        var itensAtuais = (GridCaracteristicas.ItemsSource as IEnumerable<CaracteristicaEquipamento>)
            ?? Enumerable.Empty<CaracteristicaEquipamento>();

        var duplicado = itensAtuais.FirstOrDefault(c =>
            (_caracteristicaSelecionada == null || c.Id != _caracteristicaSelecionada.Id) &&
            string.Equals(c.Nome.Trim(), nomeNormalizado, StringComparison.OrdinalIgnoreCase));

        if (duplicado == null) return false;
        nomeExistente = duplicado.Nome;
        return true;
    }

    private void GuardarCaracteristica_Click(object sender, RoutedEventArgs e)
    {
        var grupo = NormalizarGrupoCaracteristicas(CmbGrupoCaracteristicasPainel.Text);
        CmbGrupoCaracteristicasPainel.Text = grupo;

        if (string.IsNullOrWhiteSpace(TxtNomeCaracteristica.Text))
        {
            MessageBox.Show("Indique o nome da característica.", "Dados incompletos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ExisteNomeCaracteristicaRepetido(TxtNomeCaracteristica.Text, out var nomeExistente))
        {
            MessageBox.Show($"Já existe uma característica com este nome neste grupo: '{nomeExistente}'.",
                "Nome repetido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(TxtOrdemCaracteristica.Text, out var ordem);
        var nome = TxtNomeCaracteristica.Text.Trim();
        var valorPorOmissao = string.IsNullOrWhiteSpace(CmbValorPorOmissaoCaracteristica.Text)
            ? null : CmbValorPorOmissaoCaracteristica.Text.Trim();

        var tipoEspecificoId = (CmbTipoEspecificoCaracteristica.SelectedValue as int?);
        var caracteristicaPaiId = (CmbCaracteristicaPai.SelectedValue as int?);

        int idGravado;
        if (_caracteristicaSelecionada == null)
        {
            var nova = new CaracteristicaEquipamento
            {
                GrupoCaracteristicas = grupo,
                Nome = nome,
                ValorPorOmissao = valorPorOmissao,
                Ordem = ordem,
                Ativo = ChkAtivoCaracteristica.IsChecked == true,
                TipoEquipamentoId = tipoEspecificoId,
                CaracteristicaPaiId = caracteristicaPaiId
            };
            App.Db.CaracteristicasEquipamento.Add(nova);
            App.Db.SaveChanges();
            idGravado = nova.Id;
        }
        else
        {
            var entidade = App.Db.CaracteristicasEquipamento.First(c => c.Id == _caracteristicaSelecionada.Id);
            entidade.Nome = nome;
            entidade.ValorPorOmissao = valorPorOmissao;
            entidade.Ordem = ordem;
            entidade.Ativo = ChkAtivoCaracteristica.IsChecked == true;
            entidade.TipoEquipamentoId = tipoEspecificoId;
            entidade.CaracteristicaPaiId = caracteristicaPaiId;
            App.Db.SaveChanges();
            idGravado = entidade.Id;
        }

        // Mantém a característica gravada selecionada, em vez de limpar o formulário — permite
        // clicar logo a seguir em "Gerir Valores desta Característica..." sem a voltar a procurar.
        GridCaracteristicas.ItemsSource = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupo)
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .ToList();
        _caracteristicaSelecionada = (GridCaracteristicas.ItemsSource as IEnumerable<CaracteristicaEquipamento>)?
            .FirstOrDefault(c => c.Id == idGravado);
        GridCaracteristicas.SelectedItem = _caracteristicaSelecionada;
        BtnGerirValoresCaracteristica.IsEnabled = _caracteristicaSelecionada != null;
        BtnEliminarCaracteristica.IsEnabled = _caracteristicaSelecionada != null;

        // Se o grupo escrito era novo, fica já disponível nas combos de grupo (aqui e em cima).
        var grupos = ObterGruposCaracteristicasDisponiveis();
        CmbGrupoCaracteristicas.ItemsSource = grupos;
        CmbGrupoCaracteristicasPainel.ItemsSource = grupos;
        CmbGrupoCaracteristicasPainel.Text = grupo;
    }

    private void EliminarCaracteristica_Click(object sender, RoutedEventArgs e)
    {
        if (_caracteristicaSelecionada == null) return;

        if (MessageBox.Show(
                $"Eliminar a característica '{_caracteristicaSelecionada.Nome}'?\n\n" +
                "Os valores já preenchidos com esta característica em equipamentos existentes, " +
                "bem como a sua lista de valores sugeridos, também serão eliminados.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var valoresAssociados = App.Db.EquipamentoCaracteristicaValores
            .Where(v => v.CaracteristicaEquipamentoId == _caracteristicaSelecionada.Id);
        App.Db.EquipamentoCaracteristicaValores.RemoveRange(valoresAssociados);

        var opcoesAssociadas = App.Db.CaracteristicaEquipamentoOpcoes
            .Where(o => o.CaracteristicaEquipamentoId == _caracteristicaSelecionada.Id);
        App.Db.CaracteristicaEquipamentoOpcoes.RemoveRange(opcoesAssociadas);

        App.Db.CaracteristicasEquipamento.Remove(
            App.Db.CaracteristicasEquipamento.First(c => c.Id == _caracteristicaSelecionada.Id));
        App.Db.SaveChanges();

        var grupo = _grupoCaracteristicasPainel ?? GruposCaracteristicasEquipamento.Generico;
        GridCaracteristicas.ItemsSource = App.Db.CaracteristicasEquipamento
            .Where(c => c.GrupoCaracteristicas == grupo)
            .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
            .ToList();
        NovaCaracteristica_Click(sender, e);
    }

    /// <summary>(1.4) Abre a gestão da lista de valores sugeridos (opcionais) para a característica
    /// atualmente selecionada na grelha, e atualiza a combo de "Valor por Omissão" ao fechar.</summary>
    private void GerirValoresCaracteristica_Click(object sender, RoutedEventArgs e)
    {
        if (_caracteristicaSelecionada == null) return;

        var janela = new CaracteristicaOpcoesWindow(_caracteristicaSelecionada.Id, _caracteristicaSelecionada.Nome) { Owner = this };
        janela.ShowDialog();

        CmbValorPorOmissaoCaracteristica.ItemsSource = App.Db.CaracteristicaEquipamentoOpcoes
            .Where(o => o.CaracteristicaEquipamentoId == _caracteristicaSelecionada.Id && o.Ativo)
            .OrderBy(o => o.Ordem).ThenBy(o => o.Valor)
            .Select(o => o.Valor)
            .ToList();
    }

    private void RecarregarValores()
    {
        if (_grupoAtual == null) return;

        List<ValorListaItem> itens = ObterTipoGrupo(_grupoAtual) switch
        {
            TipoGrupoValores.CategoriaDisia => App.Db.CategoriasDisia
                .OrderBy(c => c.Nome)
                .Select(c => new ValorListaItem { Id = c.Id, Valor = c.Nome, Ativo = true })
                .ToList(),

            TipoGrupoValores.CategoriaIntervencao => App.Db.CategoriasIntervencao
                .OrderBy(c => c.Nome)
                .Select(c => new ValorListaItem { Id = c.Id, Valor = c.Nome, Ativo = c.Ativa })
                .ToList(),

            TipoGrupoValores.EstadoIntervencaoAtividade => App.Db.EstadosCorPersonalizados
                .Where(e => e.Grupo == GruposEstadoCor.Intervencao)
                .OrderBy(e => e.Id)
                .Select(e => new ValorListaItem { Id = e.Id, Valor = e.NomeExibicao, Ativo = true })
                .ToList(),

            TipoGrupoValores.EstadoPedido => App.Db.EstadosCorPersonalizados
                .Where(e => e.Grupo == GruposEstadoCor.Pedido)
                .OrderBy(e => e.Id)
                .Select(e => new ValorListaItem { Id = e.Id, Valor = e.NomeExibicao, Ativo = true })
                .ToList(),

            _ => App.Db.ValoresFixos
                .Where(v => v.Grupo == _grupoAtual)
                .OrderBy(v => v.Valor)
                .Select(v => new ValorListaItem
                {
                    Id = v.Id, Valor = v.Valor, Ordem = v.Ordem, Ativo = v.Ativo,
                    GrupoCaracteristicas = v.GrupoCaracteristicas
                })
                .ToList()
        };

        GridValores.ItemsSource = itens;
    }

    private void GridValores_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _valorSelecionado = GridValores.SelectedItem as ValorListaItem;
        if (_valorSelecionado == null) return;

        TxtValor.Text = _valorSelecionado.Valor;
        TxtOrdem.Text = _valorSelecionado.Ordem.ToString();
        ChkAtivo.IsChecked = _valorSelecionado.Ativo;
        CmbGrupoCaracteristicas.Text = _valorSelecionado.GrupoCaracteristicas ?? GruposCaracteristicasEquipamento.Generico;

        // (1.3) Sincroniza a gestão de características, em baixo, com o grupo deste Tipo de
        // Equipamento — evita ter de o voltar a escolher manualmente na combo de baixo.
        if (_grupoAtual == GruposValorFixo.TipoEquipamento)
        {
            var grupo = _valorSelecionado.GrupoCaracteristicas ?? GruposCaracteristicasEquipamento.Generico;
            CmbGrupoCaracteristicasPainel.Text = grupo;
            RecarregarCaracteristicasGrupo(grupo);
        }
    }

    private void NovoValor_Click(object sender, RoutedEventArgs e)
    {
        _valorSelecionado = null;
        GridValores.SelectedItem = null;
        TxtValor.Clear();
        var proximaOrdem = (GridValores.ItemsSource as IEnumerable<ValorListaItem>)?.Count() ?? 0;
        TxtOrdem.Text = proximaOrdem.ToString();
        ChkAtivo.IsChecked = true;
        CmbGrupoCaracteristicas.Text = GruposCaracteristicasEquipamento.Generico;

        if (_grupoAtual == GruposValorFixo.TipoEquipamento)
        {
            CmbGrupoCaracteristicasPainel.Text = GruposCaracteristicasEquipamento.Generico;
            RecarregarCaracteristicasGrupo(GruposCaracteristicasEquipamento.Generico);
        }
    }

    /// <summary>Verifica se já existe um valor igual (ignorando maiúsculas/minúsculas e espaços
    /// extra) no grupo atual, excluindo o próprio item quando se está a editar. (5)</summary>
    private bool ExisteValorRepetido(string valor, out string valorExistente)
    {
        valorExistente = string.Empty;
        if (_grupoAtual == null) return false;

        var valorNormalizado = valor.Trim();
        var itensAtuais = (GridValores.ItemsSource as IEnumerable<ValorListaItem>) ?? Enumerable.Empty<ValorListaItem>();

        var duplicado = itensAtuais.FirstOrDefault(i =>
            (_valorSelecionado == null || i.Id != _valorSelecionado.Id) &&
            string.Equals(i.Valor.Trim(), valorNormalizado, StringComparison.OrdinalIgnoreCase));

        if (duplicado == null) return false;
        valorExistente = duplicado.Valor;
        return true;
    }

    private void GuardarValor_Click(object sender, RoutedEventArgs e)
    {
        if (_grupoAtual == null)
        {
            MessageBox.Show("Selecione primeiro uma lista à esquerda.", "Ação necessária",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(TxtValor.Text))
        {
            MessageBox.Show("Indique o valor.", "Dados incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // (5) Não permitir valores repetidos na mesma lista.
        if (ExisteValorRepetido(TxtValor.Text, out var valorExistente))
        {
            MessageBox.Show($"Já existe um valor igual nesta lista: '{valorExistente}'.\nNão é possível inserir ou guardar valores repetidos.",
                "Valor repetido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(TxtOrdem.Text, out var ordem);
        var novoValor = TxtValor.Text.Trim();
        var tipo = ObterTipoGrupo(_grupoAtual);

        switch (tipo)
        {
            case TipoGrupoValores.CategoriaDisia:
                if (_valorSelecionado == null)
                    App.Db.CategoriasDisia.Add(new CategoriaDisia { Nome = novoValor, CorHex = "#6366F1" });
                else
                    App.Db.CategoriasDisia.First(c => c.Id == _valorSelecionado.Id).Nome = novoValor;
                break;

            case TipoGrupoValores.CategoriaIntervencao:
                if (_valorSelecionado == null)
                    App.Db.CategoriasIntervencao.Add(new CategoriaIntervencao { Nome = novoValor, CorHex = "#3B82F6", Ativa = ChkAtivo.IsChecked == true });
                else
                {
                    var entidadeCat = App.Db.CategoriasIntervencao.First(c => c.Id == _valorSelecionado.Id);
                    entidadeCat.Nome = novoValor;
                    entidadeCat.Ativa = ChkAtivo.IsChecked == true;
                }
                break;

            case TipoGrupoValores.EstadoIntervencaoAtividade:
            case TipoGrupoValores.EstadoPedido:
                if (_valorSelecionado == null)
                {
                    MessageBox.Show("Não é possível criar novos estados: selecione um estado existente na lista para lhe alterar o nome apresentado.",
                        "Não permitido", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                App.Db.EstadosCorPersonalizados.First(es => es.Id == _valorSelecionado.Id).NomeExibicao = novoValor;
                break;

            default:
                // (1.1) Para Tipos de Equipamento, grava também o grupo de características associado,
                // ligado ao registo — é isto que evita que renomear o tipo faça "perder" as
                // características específicas mostradas em Inserir/Editar Equipamento.
                var grupoCaracteristicas = _grupoAtual == GruposValorFixo.TipoEquipamento
                    ? NormalizarGrupoCaracteristicas(CmbGrupoCaracteristicas.Text)
                    : null;

                if (_valorSelecionado == null)
                {
                    App.Db.ValoresFixos.Add(new ValorFixo
                    {
                        Grupo = _grupoAtual,
                        Valor = novoValor,
                        Ordem = ordem,
                        Ativo = ChkAtivo.IsChecked == true,
                        GrupoCaracteristicas = grupoCaracteristicas
                    });
                }
                else
                {
                    var entidade = App.Db.ValoresFixos.First(v => v.Id == _valorSelecionado.Id);
                    entidade.Valor = novoValor;
                    entidade.Ordem = ordem;
                    entidade.Ativo = ChkAtivo.IsChecked == true;
                    if (_grupoAtual == GruposValorFixo.TipoEquipamento)
                        entidade.GrupoCaracteristicas = grupoCaracteristicas;
                }
                break;
        }

        App.Db.SaveChanges();
        RecarregarValores();
        NovoValor_Click(sender, e);

        // Se foi criado um grupo de características novo (ex.: "Energia"), fica já disponível para
        // escolher noutros Tipos de Equipamento e na secção de características, em baixo.
        if (_grupoAtual == GruposValorFixo.TipoEquipamento)
        {
            var grupos = ObterGruposCaracteristicasDisponiveis();
            CmbGrupoCaracteristicas.ItemsSource = grupos;
            CmbGrupoCaracteristicasPainel.ItemsSource = grupos;
        }
    }

    private void EliminarValor_Click(object sender, RoutedEventArgs e)
    {
        if (_valorSelecionado == null || _grupoAtual == null) return;

        var tipo = ObterTipoGrupo(_grupoAtual);

        if (tipo is TipoGrupoValores.EstadoIntervencaoAtividade or TipoGrupoValores.EstadoPedido)
        {
            MessageBox.Show("Não é possível eliminar estados: fazem parte do fluxo de negócio da aplicação.",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (tipo == TipoGrupoValores.CategoriaDisia &&
            App.Db.AtividadesDisia.Any(a => a.CategoriaDisiaId == _valorSelecionado.Id))
        {
            MessageBox.Show("Não é possível eliminar: já existem atividades DISIA registadas com esta categoria.",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (tipo == TipoGrupoValores.CategoriaIntervencao &&
            App.Db.IntervencaoCategorias.Any(ic => ic.CategoriaIntervencaoId == _valorSelecionado.Id))
        {
            MessageBox.Show("Não é possível eliminar: esta categoria já está associada a intervenções. " +
                "Pode desativá-la em vez de eliminar (desmarque a caixa \"Ativo\").",
                "Não permitido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show($"Eliminar o valor '{_valorSelecionado.Valor}'?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        switch (tipo)
        {
            case TipoGrupoValores.CategoriaDisia:
                App.Db.CategoriasDisia.Remove(App.Db.CategoriasDisia.First(c => c.Id == _valorSelecionado.Id));
                break;
            case TipoGrupoValores.CategoriaIntervencao:
                App.Db.CategoriasIntervencao.Remove(App.Db.CategoriasIntervencao.First(c => c.Id == _valorSelecionado.Id));
                break;
            default:
                App.Db.ValoresFixos.Remove(App.Db.ValoresFixos.First(v => v.Id == _valorSelecionado.Id));
                break;
        }

        App.Db.SaveChanges();
        RecarregarValores();
        NovoValor_Click(sender, e);
    }

    private void CategoriasIntervencao_Click(object sender, RoutedEventArgs e)
    {
        var janela = new CategoriasIntervencaoWindow { Owner = this };
        janela.ShowDialog();
    }

    private void CategoriasDisia_Click(object sender, RoutedEventArgs e)
    {
        var janela = new CategoriasDisiaWindow { Owner = this };
        janela.ShowDialog();
    }

    private void EstadosIntervencao_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EstadosCorWindow(GruposEstadoCor.Intervencao, "Estados das Intervenções") { Owner = this };
        janela.ShowDialog();
    }

    private void EstadosPedido_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EstadosCorWindow(GruposEstadoCor.Pedido, "Estados dos Pedidos") { Owner = this };
        janela.ShowDialog();
    }

    private void EstadosEquipamento_Click(object sender, RoutedEventArgs e)
    {
        var janela = new EstadosCorWindow(GruposEstadoCor.Equipamento, "Estados dos Equipamentos") { Owner = this };
        janela.ShowDialog();
    }

    // =========================================================================
    // ABA: IMPORTAÇÃO DE DADOS
    // =========================================================================

    /// <summary>
    /// Gera um template Excel (via <paramref name="gerador"/>) e pergunta ao utilizador onde
    /// o guardar, oferecendo depois para o abrir de imediato na aplicação Excel por omissão.
    /// </summary>
    private static void GerarESalvarTemplate(string nomeSugerido, Action<string> gerador)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar template de importação",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx",
            FileName = nomeSugerido
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            gerador(dialog.FileName);

            var resposta = MessageBox.Show(
                $"Template criado com sucesso em:\n{dialog.FileName}\n\n" +
                "Já inclui os cabeçalhos corretos, uma linha de exemplo e uma aba de instruções. " +
                "Deseja abrir o ficheiro agora?",
                "Template criado", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (resposta == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível criar o template:\n{ex.Message}",
                "Erro ao criar template", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TemplateAgrupamentosEscolas_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Agrupamentos_Escolas.xlsx", TemplateExcelService.GerarTemplateAgrupamentosEscolas);

    private void TemplateEquipamento_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Equipamento.xlsx", TemplateExcelService.GerarTemplateEquipamento);

    private void TemplateIntervencoes_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Intervencoes.xlsx", TemplateExcelService.GerarTemplateIntervencoes);

    private void TemplateEquipamentoAbatido_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Equipamento_Abatido.xlsx", TemplateExcelService.GerarTemplateEquipamentoAbatido);

    private void TemplateEquipamentoRecolhido_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Equipamento_Recolhido.xlsx", TemplateExcelService.GerarTemplateEquipamentoRecolhido);

    private void TemplateAtividadesDisia_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Atividades_DISIA.xlsx", TemplateExcelService.GerarTemplateAtividadesDisia);

    private void TemplateComunicacoes_Click(object sender, RoutedEventArgs e) =>
        GerarESalvarTemplate("Template_Comunicacoes.xlsx", TemplateExcelService.GerarTemplateComunicacoes);

    private void AjudaAgrupamentosEscolas_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx) com duas abas:\n\n" +
            "ABA \"Agrupamentos\" (processada sempre primeiro), com as colunas por esta ordem:\n" +
            "  1. Id_Agrupamento (código numérico)\n" +
            "  2. cod_gepe (opcional)\n" +
            "  3. Agrupamento (nome)\n" +
            "  4. Morada\n" +
            "  5-7. Contacto 1, Contacto 2, Contacto 3\n" +
            "  8-9. Email 1, Email 2\n" +
            "  10. Site\n" +
            "  11. Observações\n\n" +
            "ABA \"Escolas\", com as colunas por esta ordem:\n" +
            "  1. Freguesia\n" +
            "  2. Código DGRH\n" +
            "  3. Código GEPE\n" +
            "  4. Estabelecimento de Ensino (nome da escola)\n" +
            "  5. Morada\n" +
            "  6. Telefone\n" +
            "  7. E-mail\n" +
            "  8. Cod. Agrupamento (tem de corresponder ao Id_Agrupamento da aba Agrupamentos)\n\n" +
            "Escolas sem \"Cod. Agrupamento\" preenchido são importadas sem agrupamento associado. " +
            "Escolas ou agrupamentos com nome muito semelhante a um já existente não são duplicados; " +
            "os dados em falta são complementados.",
            "Formato esperado do ficheiro", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AjudaFicheiroBase_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx) com as seguintes abas:\n\n" +
            "  • GEPE — escolas e agrupamentos (fonte oficial)\n" +
            "  • Lista de Escolas — nomes alternativos/abreviados das escolas\n" +
            "  • Contactos — contactos por escola\n" +
            "  • JAN a DEZ — intervenções mensais\n" +
            "  • Serv. DISIA — atividades gerais da DISIA\n\n" +
            "Este é o mesmo formato do ficheiro_base.xlsx original. Registos já existentes " +
            "(agrupamentos, escolas) não são duplicados.",
            "Formato esperado do ficheiro", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportarAgrupamentosEscolas_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de Agrupamentos e Escolas",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarAgrupamentosEEscolas(dialog.FileName);

            MessageBox.Show(
                "Importação concluída:\n\n" +
                $"Agrupamentos criados: {resultado.AgrupamentosCriados}\n" +
                $"Agrupamentos atualizados: {resultado.AgrupamentosAtualizados}\n" +
                $"Escolas criadas: {resultado.EscolasCriadas}\n" +
                $"Escolas atualizadas (dados complementados): {resultado.EscolasAtualizadas}\n" +
                $"Escolas sem agrupamento no ficheiro: {resultado.EscolasSemAgrupamento}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}):\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportarFicheiroBase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro_base.xlsx",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarFicheiroBase(dialog.FileName);

            MessageBox.Show(
                "Importação concluída:\n\n" +
                $"Agrupamentos criados: {resultado.AgrupamentosCriados}\n" +
                $"Escolas criadas: {resultado.EscolasCriadasDeGepe}\n" +
                $"Escolas ignoradas (duplicadas): {resultado.EscolasIgnoradasPorDuplicado}\n" +
                $"Contactos importados: {resultado.ContactosImportados}\n" +
                $"Intervenções importadas: {resultado.IntervencoesImportadas}\n" +
                $"Atividades DISIA importadas: {resultado.AtividadesDisiaImportadas}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AjudaAtividadesDisia_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx) com o nome \"serviços_disia.xlsx\" e conter as atividades desempenhadas na DISIA.\n\n" +
            "O ficheiro deve ter as seguintes colunas (por esta ordem):\n" +
            "  1. Data (formato dd/mm/yyyy)\n" +
            "  2. Descrição da atividade\n" +
            "  3. Categoria\n" +
            "  4. Local\n" +
            "  5. Divisão / Serviço envolvido\n" +
            "  6. Suporte prestado\n" +
            "  7. Quantidade (número de vezes que o serviço foi prestado)\n\n" +
            "Cada linha representa um registo de atividade. Registos com datas ou descrições " +
            "muito semelhantes às já existentes não são duplicados.",
            "Formato esperado do ficheiro serviços_disia.xlsx", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportarAtividadesDisia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro serviços_disia.xlsx",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx",
            FileName = "serviços_disia.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarAtividadesDisia(dialog.FileName);

            MessageBox.Show(
                "Importação de Atividades da DISIA concluída:\n\n" +
                $"Atividades importadas: {resultado.AtividadesDisiaImportadas}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AjudaIntervencoesDedicado_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser o \"Modelo de Importação de Intervenções\" (uma linha por intervenção), " +
            "na aba \"Intervenções\" (se não existir, é usada a primeira aba).\n\n" +
            "O ficheiro deve ter as seguintes colunas (por esta ordem):\n" +
            "  1. Data (formato dd-mm-aaaa)\n" +
            "  2. Escola (nome; a app faz correspondência aproximada de nomes)\n" +
            "  3. Código GEPE (opcional — se preenchido, é usado como chave exata)\n" +
            "  4. Descrição\n" +
            "  5. Categorias (nomes separados por ';', ex: \"Hardware; Redes\")\n" +
            "  6. Material Recolhido/Abatido (opcional)\n" +
            "  7. Estado (Fechada/Pendente/Em Progresso/Em Espera/Cancelada; vazio = Fechada)\n" +
            "  8. Motivo Pendente (opcional)\n\n" +
            "Os nomes de Categorias e Estados têm de corresponder aos configurados em " +
            "Administração → Dados Fixos. Intervenções com a mesma data, escola e descrição de " +
            "uma já existente não são duplicadas.",
            "Formato esperado do Modelo de Importação de Intervenções", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportarIntervencoesDedicado_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar o Modelo de Importação de Intervenções",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx",
            FileName = "Modelo_Importacao_Intervencoes.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarIntervencoesDedicado(dialog.FileName);

            MessageBox.Show(
                "Importação de Intervenções concluída:\n\n" +
                $"Intervenções importadas: {resultado.IntervencoesImportadas}\n" +
                $"Intervenções ignoradas (já existentes): {resultado.IntervencoesIgnoradas}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AjudaEquipamento_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx), de preferência com a aba \"Equipamento\" (se não existir, é usada a primeira aba).\n\n" +
            "O ficheiro deve ter as seguintes colunas (por esta ordem):\n" +
            "  1. Nº Série (obrigatório)\n" +
            "  2. Nº Inventário\n" +
            "  3. Tipo (ex: Computador de Secretária, Portátil, Servidor, Monitor...)\n" +
            "  4. Marca\n" +
            "  5. Modelo\n" +
            "  6. Escola (nome; a app faz correspondência aproximada de nomes)\n" +
            "  7. Código GEPE (opcional — se preenchido, é usado como chave exata)\n" +
            "  8. Estado (Em Serviço/Em Reparação/Em Armazém/Abatido; vazio = Em Serviço)\n" +
            "  9. Observações (opcional)\n\n" +
            "Equipamento com o mesmo Nº Série de um registo já existente não é duplicado.",
            "Formato esperado do ficheiro de Equipamento", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>9: "Importar Tudo" — lê um único ficheiro Excel com uma aba por fase (o mesmo
    /// formato de "Exportar Tudo") e corre todas as fases de importação em sequência.</summary>
    private void ImportarTudo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de Importação Tudo (o mesmo formato do \"Exportar Tudo\")",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        var confirmar = MessageBox.Show(
            "Vai importar todas as fases de uma só vez (Agrupamentos/Escolas, Equipamento, Intervenções, " +
            "Atividades DISIA, Equipamento Abatido, Equipamento Recolhido e Comunicações) a partir do ficheiro " +
            "escolhido. Registos já existentes não são duplicados. Pretende continuar?",
            "Confirmar Importar Tudo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmar != MessageBoxResult.Yes) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var r = servico.ImportarTudo(dialog.FileName);

            var linhas = new List<string>
            {
                $"Agrupamentos + Escolas: {r.AgrupamentosEscolas?.AgrupamentosCriados ?? 0} agrupamento(s) e {r.AgrupamentosEscolas?.EscolasCriadas ?? 0} escola(s) criados.",
                $"Equipamento: {r.Equipamento?.EquipamentosImportados ?? 0} importado(s), {r.Equipamento?.EquipamentosIgnoradosPorDuplicado ?? 0} já existente(s).",
                $"Intervenções: {r.Intervencoes?.IntervencoesImportadas ?? 0} importada(s), {r.Intervencoes?.IntervencoesIgnoradas ?? 0} já existente(s).",
                $"Atividades DISIA: {r.AtividadesDisia?.AtividadesDisiaImportadas ?? 0} importada(s).",
                $"Equipamento Abatido: {r.EquipamentoAbatido?.AbatesImportados ?? 0} importado(s), {r.EquipamentoAbatido?.AbatesIgnoradosPorDuplicado ?? 0} já existente(s).",
                $"Equipamento Recolhido: {r.EquipamentoRecolhido?.RecolhidosImportados ?? 0} importado(s), {r.EquipamentoRecolhido?.RecolhidosIgnoradosPorDuplicado ?? 0} já existente(s).",
                $"Comunicações: {r.Comunicacoes?.ComunicacoesImportadas ?? 0} importada(s), {r.Comunicacoes?.ComunicacoesAtualizadas ?? 0} atualizada(s).",
            };

            var totalAvisos = (r.AgrupamentosEscolas?.Avisos.Count ?? 0) + (r.Equipamento?.Avisos.Count ?? 0) +
                (r.Intervencoes?.Avisos.Count ?? 0) + (r.AtividadesDisia?.Avisos.Count ?? 0) +
                (r.EquipamentoAbatido?.Avisos.Count ?? 0) + (r.EquipamentoRecolhido?.Avisos.Count ?? 0) +
                (r.Comunicacoes?.Avisos.Count ?? 0);

            var mensagem = "Importação Tudo concluída:\n\n" + string.Join("\n", linhas) +
                $"\n\nTotal de avisos: {totalAvisos} (linhas ignoradas por duplicado ou dados inválidos — normal em reimportações).";

            if (r.ErrosFatais.Count > 0)
                mensagem += "\n\n⚠️ Fases com erro (não impediram as restantes):\n" + string.Join("\n", r.ErrosFatais);

            MessageBox.Show(mensagem, "Importar Tudo — concluído", MessageBoxButton.OK,
                r.ErrosFatais.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportarEquipamento_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de Equipamento",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarEquipamento(dialog.FileName);

            MessageBox.Show(
                "Importação de Equipamento concluída:\n\n" +
                $"Equipamentos importados: {resultado.EquipamentosImportados}\n" +
                $"Equipamentos ignorados (já existentes): {resultado.EquipamentosIgnoradosPorDuplicado}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AjudaEquipamentoAbatido_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx), de preferência com a aba \"Equipamento Abatido\" (se não existir, é usada a primeira aba).\n\n" +
            "O ficheiro deve ter as seguintes colunas (por esta ordem):\n" +
            "  1. Nº Série (obrigatório — tem de já existir no inventário de Equipamento)\n" +
            "  2. Data de Abate\n" +
            "  3. Status (Abatido/Em processo de abate/Doado/Reciclado...)\n" +
            "  4. Escola/Local\n" +
            "  5. Descrição\n" +
            "  6. Observações (opcional)\n\n" +
            "Registos com o mesmo Nº Série e Data de Abate de um já existente não são duplicados.",
            "Formato esperado do ficheiro de Equipamento Abatido", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportarEquipamentoAbatido_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de Equipamento Abatido",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarEquipamentoAbatido(dialog.FileName);

            MessageBox.Show(
                "Importação de Equipamento Abatido concluída:\n\n" +
                $"Abates importados: {resultado.AbatesImportados}\n" +
                $"Abates ignorados (já existentes): {resultado.AbatesIgnoradosPorDuplicado}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AjudaEquipamentoRecolhido_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx), de preferência com a aba \"Equipamento Recolhido\" (se não existir, é usada a primeira aba).\n\n" +
            "O ficheiro deve ter as seguintes colunas (por esta ordem):\n" +
            "  1. Nº Série (obrigatório — tem de já existir no inventário de Equipamento)\n" +
            "  2. Data de Recolha\n" +
            "  3. Estado (Pendente/Em Reparação/Reparado/Entregue; vazio = Pendente)\n" +
            "  4. Data de Entrega (opcional — só preencher se já tiver sido entregue)\n" +
            "  5. Observações (opcional)\n\n" +
            "Registos com o mesmo Nº Série e Data de Recolha de um já existente não são duplicados.",
            "Formato esperado do ficheiro de Equipamento Recolhido", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportarEquipamentoRecolhido_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de Equipamento Recolhido",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarEquipamentoRecolhido(dialog.FileName);

            MessageBox.Show(
                "Importação de Equipamento Recolhido concluída:\n\n" +
                $"Recolhas importadas: {resultado.RecolhidosImportados}\n" +
                $"Recolhas ignoradas (já existentes): {resultado.RecolhidosIgnoradosPorDuplicado}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AjudaComunicacoes_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O ficheiro deve ser um Excel (.xlsx), de preferência com a aba \"Comunicações\" (se não existir, é usada a primeira aba).\n\n" +
            "O ficheiro deve ter as seguintes colunas (por esta ordem):\n" +
            "  1. Escola (nome; a app faz correspondência aproximada de nomes)\n" +
            "  2. Código GEPE (opcional — se preenchido, é usado como chave exata)\n" +
            "  3. Tipo de Ligação (Fibra/ADSL/4G-5G/Satélite/Outro; vazio = Fibra)\n" +
            "  4. Velocidade de Fibra (opcional, ex: 100 Mbps, 1 Gbps)\n" +
            "  5. Operadora\n" +
            "  6. Nº Contrato\n" +
            "  7. Data de Instalação\n" +
            "  8. Integrado (Sim/Não)\n" +
            "  9. Estado (Ativa/Inativa/Pendente de Instalação/Pendente de Integração...)\n" +
            "  10. Observações (opcional)\n\n" +
            "Cada linha é associada/atualizada pela combinação Escola + Nº Contrato.",
            "Formato esperado do ficheiro de Comunicações", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportarComunicacoes_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar ficheiro de Comunicações",
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new ExcelImportService(App.Db);
            var resultado = servico.ImportarComunicacoes(dialog.FileName);

            MessageBox.Show(
                "Importação de Comunicações concluída:\n\n" +
                $"Registos criados: {resultado.ComunicacoesImportadas}\n" +
                $"Registos atualizados: {resultado.ComunicacoesAtualizadas}\n\n" +
                (resultado.Avisos.Count > 0
                    ? $"Avisos ({resultado.Avisos.Count}) - ver os primeiros abaixo:\n" + string.Join("\n", resultado.Avisos.Take(15))
                    : "Sem avisos."),
                "Importação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a importação:\n{ex.Message}",
                "Erro de importação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================================================================
    // ABA: ESCOLAS DESATIVADAS
    // =========================================================================

    private void TabsAdministracao_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Recarregar escolas desativadas quando a aba é selecionada
        if (TabsAdministracao.SelectedIndex == 4) // Índice da aba "Escolas Desativadas"
            RecarregarEscolasDesativadas();

        if (TabsAdministracao.SelectedIndex == 8) // Índice da aba "Eliminar Registos"
            CarregarGridEliminar();
    }

    private void RecarregarEscolasDesativadas()
    {
        // Só o estado "Desativada" esconde a escola das listas/combos da aplicação — uma escola
        // "Em Obras" (ou outro estado que o administrador venha a criar em Dados Fixos) continua
        // a aparecer normalmente e não precisa de ser "reativada" aqui; o estado altera-se
        // diretamente na ficha da escola (Escolas → duplo-clique → Estado da Escola).
        var escolasDesativadas = App.Db.Escolas
            .Include(e => e.Agrupamento)
            .Where(e => e.Estado == EstadosEscola.Desativada)
            .OrderBy(e => e.Nome)
            .ToList();

        GridEscolasDesativadas.ItemsSource = escolasDesativadas;
        TxtEscolasDesativadasCount.Text = $"Total: {escolasDesativadas.Count} {(escolasDesativadas.Count == 1 ? "escola desativada" : "escolas desativadas")}";
    }

    private void ReativarEscola_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not Escola escola)
            return;

        var resultado = MessageBox.Show(
            $"Reativar a escola \"{escola.Nome}\"?\n\n" +
            "A escola voltará a aparecer em todas as listas e poderá ser novamente utilizada.",
            "Confirmar ativação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resultado != MessageBoxResult.Yes) return;

        try
        {
            escola.Estado = EstadosEscola.Ativa;
            App.Db.Escolas.Update(escola);
            App.Db.SaveChanges();

            MessageBox.Show($"Escola \"{escola.Nome}\" reativada com sucesso!", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            RecarregarEscolasDesativadas();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao reativar escola:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================================================================
    // ABA: ELIMINAR REGISTOS
    // =========================================================================

    private class TabelaEliminar
    {
        public string Rotulo { get; set; } = "";
        public string Chave { get; set; } = "";
    }

    /// <summary>Tabelas de dados disponíveis para eliminação direta de registos nesta aba.
    /// Ficam de fora as tabelas de junção puramente internas (ex.: IntervencaoCategorias,
    /// IntervencaoEquipamentos), que são geridas a partir das respetivas entidades principais,
    /// e a tabela de Utilizadores, que já tem uma aba própria com validações de segurança
    /// (não deixar eliminar o próprio utilizador nem o último Administrador).</summary>
    private static readonly (string Chave, string Rotulo)[] TabelasEliminaveis =
    {
        ("Agrupamentos", "Agrupamentos"),
        ("Escolas", "Escolas"),
        ("Contactos", "Contactos das Escolas"),
        ("PedidosIntervencao", "Pedidos de Intervenção"),
        ("Intervencoes", "Intervenções"),
        ("CategoriasIntervencao", "Categorias de Intervenção"),
        ("Equipamentos", "Equipamentos"),
        ("EquipamentosAbatidos", "Equipamento Abatido"),
        ("EquipamentosRecolhidos", "Equipamento Recolhido"),
        ("Comunicacoes", "Comunicações"),
        ("CategoriasDisia", "Categorias DISIA"),
        ("AtividadesDisia", "Atividades DISIA"),
        ("ValoresFixos", "Dados Fixos"),
        ("EstadosCorPersonalizados", "Cores de Estados Personalizadas")
    };

    private void CmbTabelaEliminar_SelectionChanged(object sender, SelectionChangedEventArgs e) => CarregarGridEliminar();

    private void AtualizarTabelaEliminar_Click(object sender, RoutedEventArgs e) => CarregarGridEliminar();

    private void CarregarGridEliminar()
    {
        if (CmbTabelaEliminar.SelectedItem is not TabelaEliminar tabela)
        {
            GridEliminarRegistos.ItemsSource = null;
            GridEliminarRegistos.Columns.Clear();
            TxtEliminarRegistosCount.Text = "0 registos";
            return;
        }

        // Cada ramo é convertido explicitamente para IList (não genérico) para que o switch
        // tenha um único tipo de retorno comum, apesar de as entidades serem todas diferentes.
        IList lista = tabela.Chave switch
        {
            "Agrupamentos" => (IList)App.Db.Agrupamentos.OrderBy(a => a.Nome).ToList(),
            "Escolas" => (IList)App.Db.Escolas.Include(e => e.Agrupamento).OrderBy(e => e.Nome).ToList(),
            "Contactos" => (IList)App.Db.Contactos.Include(c => c.Escola).OrderBy(c => c.Id).ToList(),
            "PedidosIntervencao" => (IList)App.Db.PedidosIntervencao.Include(p => p.Escola).OrderByDescending(p => p.Id).ToList(),
            "Intervencoes" => (IList)App.Db.Intervencoes.Include(i => i.Escola).OrderByDescending(i => i.Id).ToList(),
            "CategoriasIntervencao" => (IList)App.Db.CategoriasIntervencao.OrderBy(c => c.Nome).ToList(),
            "Equipamentos" => (IList)App.Db.Equipamentos.Include(eq => eq.Escola).OrderBy(eq => eq.NumeroInventario).ToList(),
            "EquipamentosAbatidos" => (IList)App.Db.EquipamentosAbatidos.OrderByDescending(a => a.Id).ToList(),
            "EquipamentosRecolhidos" => (IList)App.Db.EquipamentosRecolhidos.Include(r => r.Equipamento).OrderByDescending(r => r.Id).ToList(),
            "Comunicacoes" => (IList)App.Db.Comunicacoes.Include(c => c.Escola).OrderBy(c => c.Id).ToList(),
            "CategoriasDisia" => (IList)App.Db.CategoriasDisia.OrderBy(c => c.Nome).ToList(),
            "AtividadesDisia" => (IList)App.Db.AtividadesDisia.OrderByDescending(a => a.Id).ToList(),
            "ValoresFixos" => (IList)App.Db.ValoresFixos.OrderBy(v => v.Grupo).ThenBy(v => v.Valor).ToList(),
            "EstadosCorPersonalizados" => (IList)App.Db.EstadosCorPersonalizados.OrderBy(v => v.Grupo).ToList(),
            _ => new List<object>()
        };

        DefinirColunasEliminar(tabela.Chave);
        GridEliminarRegistos.ItemsSource = lista;
        TxtEliminarRegistosCount.Text = $"{lista.Count} registo(s)";
    }

    /// <summary>Define explicitamente as colunas da grelha de eliminação, tabela a tabela, em vez
    /// de gerar colunas por reflexão (<c>AutoGenerateColumns</c>). A abordagem anterior — cancelar
    /// por nome/tipo as propriedades calculadas ou de navegação em <c>AutoGeneratingColumn</c> —
    /// continuava a deixar a grelha (e a aplicação) a parecer encravada ao abrir tabelas maiores
    /// como Intervenções e Atividades DISIA, provavelmente por o próprio motor de reflexão do
    /// DataGrid ter de inspecionar todas as propriedades (incluindo as calculadas, antes de as
    /// poder cancelar) para cada linha/tipo. Usar apenas um punhado de colunas fixas e leves por
    /// tabela evita esse risco por completo e torna o carregamento instantâneo, independentemente
    /// do número de linhas.</summary>
    private void DefinirColunasEliminar(string chave)
    {
        GridEliminarRegistos.Columns.Clear();

        void Col(string cabecalho, string caminho, double largura = 120, string? formato = null)
        {
            var binding = new System.Windows.Data.Binding(caminho) { Mode = System.Windows.Data.BindingMode.OneWay };
            if (formato != null) binding.StringFormat = formato;
            GridEliminarRegistos.Columns.Add(new DataGridTextColumn
            {
                Header = cabecalho,
                Binding = binding,
                Width = largura
            });
        }

        switch (chave)
        {
            case "Agrupamentos":
                Col("Código", nameof(Agrupamento.CodAgrupamento), 80);
                Col("Nome", nameof(Agrupamento.Nome), 220);
                Col("Morada", nameof(Agrupamento.Morada), 200);
                Col("Email", nameof(Agrupamento.Email1), 180);
                break;

            case "Escolas":
                Col("Cód.", nameof(Escola.CodEscola), 70);
                Col("Nome", nameof(Escola.Nome), 220);
                Col("Localidade", nameof(Escola.Localidade), 140);
                Col("Tipo", nameof(Escola.Tipo), 100);
                Col("Agrupamento", "Agrupamento.Nome", 180);
                Col("Estado", nameof(Escola.Estado), 90);
                break;

            case "Contactos":
                Col("Nome", nameof(Contacto.Nome), 180);
                Col("Função", nameof(Contacto.Funcao), 120);
                Col("Telefone", nameof(Contacto.Telefone), 110);
                Col("Telemóvel", nameof(Contacto.Telemovel), 110);
                Col("Email", nameof(Contacto.Email), 180);
                Col("Escola", "Escola.Nome", 200);
                break;

            case "PedidosIntervencao":
                Col("Nº", nameof(PedidoIntervencao.Id), 50);
                Col("Data", nameof(PedidoIntervencao.DataPedido), 90, "dd/MM/yyyy");
                Col("Escola", "Escola.Nome", 200);
                Col("Solicitante", nameof(PedidoIntervencao.Solicitante), 150);
                Col("Razão", nameof(PedidoIntervencao.Razao), 220);
                Col("Estado", nameof(PedidoIntervencao.Estado), 100);
                break;

            case "Intervencoes":
                Col("Nº", nameof(Intervencao.Id), 50);
                Col("Data", nameof(Intervencao.Data), 90, "dd/MM/yyyy");
                Col("Escola", "Escola.Nome", 200);
                Col("Descrição", nameof(Intervencao.Descricao), 220);
                Col("Estado", nameof(Intervencao.Estado), 100);
                break;

            case "CategoriasIntervencao":
                Col("Nome", nameof(CategoriaIntervencao.Nome), 200);
                Col("Cor", nameof(CategoriaIntervencao.CorHex), 90);
                Col("Ativa", nameof(CategoriaIntervencao.Ativa), 60);
                break;

            case "Equipamentos":
                Col("Nº Inventário", nameof(Equipamento.NumeroInventario), 110);
                Col("Nº Série", nameof(Equipamento.NumeroSerie), 130);
                Col("Tipo", nameof(Equipamento.Tipo), 100);
                Col("Marca", nameof(Equipamento.Marca), 100);
                Col("Modelo", nameof(Equipamento.Modelo), 130);
                Col("Escola", "Escola.Nome", 180);
                Col("Estado", nameof(Equipamento.Estado), 110);
                break;

            case "EquipamentosAbatidos":
                Col("Nº", nameof(EquipamentoAbatido.Id), 50);
                Col("Data Abate", nameof(EquipamentoAbatido.DataAbate), 90, "dd/MM/yyyy");
                Col("Escola/Local", nameof(EquipamentoAbatido.EscolaOuLocal), 180);
                Col("Descrição", nameof(EquipamentoAbatido.DescricaoEquipamento), 220);
                Col("Status", nameof(EquipamentoAbatido.Status), 130);
                break;

            case "EquipamentosRecolhidos":
                Col("Nº", nameof(EquipamentoRecolhido.Id), 50);
                Col("Data Recolha", nameof(EquipamentoRecolhido.DataRecolha), 100, "dd/MM/yyyy");
                Col("Equipamento", "Equipamento.NumeroInventario", 130);
                Col("Estado", nameof(EquipamentoRecolhido.Estado), 120);
                Col("Data Entrega", nameof(EquipamentoRecolhido.DataEntrega), 100, "dd/MM/yyyy");
                break;

            case "Comunicacoes":
                Col("Escola", "Escola.Nome", 200);
                Col("Tipo Ligação", nameof(Comunicacao.TipoLigacao), 110);
                Col("Operadora", nameof(Comunicacao.Operadora), 130);
                Col("Estado", nameof(Comunicacao.Estado), 110);
                break;

            case "CategoriasDisia":
                Col("Nome", nameof(CategoriaDisia.Nome), 220);
                Col("Cor", nameof(CategoriaDisia.CorHex), 90);
                break;

            case "AtividadesDisia":
                Col("Nº", nameof(AtividadeDisia.Id), 50);
                Col("Data", nameof(AtividadeDisia.Data), 90, "dd/MM/yyyy");
                Col("Descrição", nameof(AtividadeDisia.Descricao), 220);
                Col("Local", nameof(AtividadeDisia.Local), 160);
                Col("Divisão", nameof(AtividadeDisia.Divisao), 140);
                Col("Estado", nameof(AtividadeDisia.Estado), 110);
                break;

            case "ValoresFixos":
                Col("Grupo", nameof(ValorFixo.Grupo), 180);
                Col("Valor", nameof(ValorFixo.Valor), 200);
                Col("Ordem", nameof(ValorFixo.Ordem), 70);
                Col("Ativo", nameof(ValorFixo.Ativo), 60);
                break;

            case "EstadosCorPersonalizados":
                Col("Grupo", nameof(EstadoCorPersonalizada.Grupo), 160);
                Col("Estado", nameof(EstadoCorPersonalizada.NomeEstado), 160);
                Col("Nome a Exibir", nameof(EstadoCorPersonalizada.NomeExibicao), 160);
                Col("Cor", nameof(EstadoCorPersonalizada.Cor), 100);
                break;
        }
    }

    /// <summary>(2) Apaga silenciosamente meras "ligações" (registos de tabelas de junção
    /// N:N, ex.: <see cref="IntervencaoCategoria"/>, <see cref="IntervencaoEquipamento"/>) que
    /// referenciem o registo indicado, ANTES de qualquer verificação/eliminação. Estas ligações
    /// não têm significado próprio — são apenas o "elo" entre duas entidades reais (uma
    /// Intervenção e uma Categoria, ou uma Intervenção e um Equipamento) — pelo que não faz
    /// sentido bloquear a eliminação do registo principal por causa delas (o utilizador
    /// relatou precisamente este caso: um Equipamento "preso" por uma ligação deste tipo). A
    /// eliminação do lado "Intervenção" já as arrastava sempre em cascata a nível da base de
    /// dados (ver AppDbContext, DeleteBehavior.Cascade); isto só torna esse comportamento
    /// também eficaz a partir do lado "Categoria"/"Equipamento", que tinham FK Restrict.</summary>
    private static void LimparLigacoesSemRegistoProprio(string chave, object registo)
    {
        switch (chave)
        {
            case "Intervencoes" when registo is Intervencao intervencao:
                App.Db.IntervencaoCategorias.RemoveRange(App.Db.IntervencaoCategorias.Where(ic => ic.IntervencaoId == intervencao.Id));
                App.Db.IntervencaoEquipamentos.RemoveRange(App.Db.IntervencaoEquipamentos.Where(ie => ie.IntervencaoId == intervencao.Id));
                break;

            case "CategoriasIntervencao" when registo is CategoriaIntervencao categoria:
                App.Db.IntervencaoCategorias.RemoveRange(App.Db.IntervencaoCategorias.Where(ic => ic.CategoriaIntervencaoId == categoria.Id));
                break;

            case "Equipamentos" when registo is Equipamento equipamento:
                App.Db.IntervencaoEquipamentos.RemoveRange(App.Db.IntervencaoEquipamentos.Where(ie => ie.EquipamentoId == equipamento.Id));
                break;
        }
    }

    /// <summary>Descreve, para um registo selecionado numa dada tabela, os dados dependentes que
    /// impedem a sua eliminação direta (ex.: uma Escola com Pedidos/Intervenções/Equipamento
    /// associados, um Agrupamento com Escolas, etc.). Devolve uma lista vazia se o registo não
    /// tiver nenhuma dependência e puder ser eliminado sem problemas. Verificado ANTES de tentar
    /// eliminar, para se poder avisar o utilizador e indicar concretamente quais os registos
    /// descendentes existem — e, desde (2), oferecer a opção de os eliminar também em cascata,
    /// em vez de só recusar a eliminação depois de a base de dados rejeitar o pedido.
    ///
    /// (2) Já NÃO verifica ligações puras de junção (ver <see cref="LimparLigacoesSemRegistoProprio"/>,
    /// chamado antes desta função) nem o caso invertido que causava um bloqueio circular:
    /// "Equipamento Recolhido" deixou de ser bloqueado por ter uma "Atividade DISIA" associada —
    /// essa relação é o inverso do que faz sentido (é a Atividade DISIA que tem o Equipamento
    /// Recolhido como descendente, através de <see cref="EquipamentoRecolhido.AtividadeDisiaId"/>,
    /// e não o contrário), e era precisamente essa verificação a mais que impedia eliminar tanto
    /// o Equipamento Recolhido (por ter uma Atividade DISIA) como a Atividade DISIA (por ter o
    /// Equipamento Recolhido) — um impasse sem saída possível.</summary>
    private List<string> ObterDependencias(string chave, object registo)
    {
        var dependencias = new List<string>();

        void Add(int count, string singular, string plural)
        {
            if (count > 0) dependencias.Add($"{count} {(count == 1 ? singular : plural)}");
        }

        switch (chave)
        {
            case "Agrupamentos" when registo is Agrupamento agrupamento:
                Add(App.Db.Escolas.Count(e => e.AgrupamentoId == agrupamento.Id), "escola associada", "escolas associadas");
                Add(App.Db.PedidosIntervencao.Count(p => p.AgrupamentoId == agrupamento.Id), "pedido de intervenção associado", "pedidos de intervenção associados");
                Add(App.Db.Intervencoes.Count(i => i.AgrupamentoId == agrupamento.Id), "intervenção associada", "intervenções associadas");
                break;

            case "Escolas" when registo is Escola escola:
                Add(App.Db.Contactos.Count(c => c.EscolaId == escola.Id), "contacto associado", "contactos associados");
                Add(App.Db.PedidosIntervencao.Count(p => p.EscolaId == escola.Id), "pedido de intervenção associado", "pedidos de intervenção associados");
                Add(App.Db.Intervencoes.Count(i => i.EscolaId == escola.Id), "intervenção associada", "intervenções associadas");
                Add(App.Db.Equipamentos.Count(eq => eq.EscolaId == escola.Id), "equipamento associado", "equipamentos associados");
                Add(App.Db.Comunicacoes.Count(c => c.EscolaId == escola.Id), "registo de comunicação associado", "registos de comunicação associados");
                break;

            case "PedidosIntervencao" when registo is PedidoIntervencao pedido:
                Add(App.Db.Intervencoes.Count(i => i.PedidoOrigemId == pedido.Id), "intervenção gerada a partir deste pedido", "intervenções geradas a partir deste pedido");
                break;

            case "Intervencoes" when registo is Intervencao intervencao:
                Add(App.Db.EquipamentosRecolhidos.Count(r => r.IntervencaoId == intervencao.Id), "recolha de equipamento associada", "recolhas de equipamento associadas");
                Add(App.Db.EquipamentosAbatidos.Count(a => a.IntervencaoId == intervencao.Id), "abate de equipamento associado", "abates de equipamento associados");
                break;

            case "CategoriasIntervencao" when registo is CategoriaIntervencao categoria:
                Add(App.Db.SubCategoriasIntervencao.Count(s => s.CategoriaIntervencaoId == categoria.Id), "subcategoria associada", "subcategorias associadas");
                break;

            case "Equipamentos" when registo is Equipamento equipamento:
                Add(App.Db.EquipamentosRecolhidos.Count(r => r.EquipamentoId == equipamento.Id), "recolha associada", "recolhas associadas");
                Add(App.Db.EquipamentosAbatidos.Count(a => a.EquipamentoId == equipamento.Id), "abate associado", "abates associados");
                break;

            case "CategoriasDisia" when registo is CategoriaDisia categoriaDisia:
                Add(App.Db.AtividadesDisia.Count(a => a.CategoriaDisiaId == categoriaDisia.Id), "atividade DISIA associada", "atividades DISIA associadas");
                break;

            case "AtividadesDisia" when registo is AtividadeDisia atividade:
                Add(App.Db.EquipamentosRecolhidos.Count(r => r.AtividadeDisiaId == atividade.Id), "recolha de equipamento associada", "recolhas de equipamento associadas");
                break;
        }

        return dependencias;
    }

    /// <summary>(2) Devolve os registos dependentes reais (não meras ligações — ver
    /// <see cref="LimparLigacoesSemRegistoProprio"/>) de um registo, já como objetos (e não só a
    /// contagem, ao contrário de <see cref="ObterDependencias"/>), para poderem ser eliminados em
    /// cascata quando o utilizador confirmar essa opção — ver <see cref="EliminarComDependentes"/>.
    /// Espelha exatamente os mesmos pares tabela/campo verificados em <see cref="ObterDependencias"/>.
    /// Nota: cada consulta termina em <c>.ToList()</c> antes do <c>.Select(o => (chave, o))</c> —
    /// o EF Core traduziria a consulta para uma expression tree, que não pode conter um tuple
    /// literal (erro do compilador CS8143); com o <c>.ToList()</c>, os dados já vêm para memória
    /// antes desse passo, que passa a ser LINQ-to-Objects normal.</summary>
    private static List<(string Chave, object Registo)> ObterRegistosDependentes(string chave, object registo)
    {
        var filhos = new List<(string, object)>();

        switch (chave)
        {
            case "Agrupamentos" when registo is Agrupamento agrupamento:
                filhos.AddRange(App.Db.Escolas.Where(e => e.AgrupamentoId == agrupamento.Id).ToList().Cast<object>().Select(o => ("Escolas", o)));
                filhos.AddRange(App.Db.PedidosIntervencao.Where(p => p.AgrupamentoId == agrupamento.Id).ToList().Cast<object>().Select(o => ("PedidosIntervencao", o)));
                filhos.AddRange(App.Db.Intervencoes.Where(i => i.AgrupamentoId == agrupamento.Id).ToList().Cast<object>().Select(o => ("Intervencoes", o)));
                break;

            case "Escolas" when registo is Escola escola:
                filhos.AddRange(App.Db.Contactos.Where(c => c.EscolaId == escola.Id).ToList().Cast<object>().Select(o => ("Contactos", o)));
                filhos.AddRange(App.Db.PedidosIntervencao.Where(p => p.EscolaId == escola.Id).ToList().Cast<object>().Select(o => ("PedidosIntervencao", o)));
                filhos.AddRange(App.Db.Intervencoes.Where(i => i.EscolaId == escola.Id).ToList().Cast<object>().Select(o => ("Intervencoes", o)));
                filhos.AddRange(App.Db.Equipamentos.Where(eq => eq.EscolaId == escola.Id).ToList().Cast<object>().Select(o => ("Equipamentos", o)));
                filhos.AddRange(App.Db.Comunicacoes.Where(c => c.EscolaId == escola.Id).ToList().Cast<object>().Select(o => ("Comunicacoes", o)));
                break;

            case "PedidosIntervencao" when registo is PedidoIntervencao pedido:
                filhos.AddRange(App.Db.Intervencoes.Where(i => i.PedidoOrigemId == pedido.Id).ToList().Cast<object>().Select(o => ("Intervencoes", o)));
                break;

            case "Intervencoes" when registo is Intervencao intervencao:
                filhos.AddRange(App.Db.EquipamentosRecolhidos.Where(r => r.IntervencaoId == intervencao.Id).ToList().Cast<object>().Select(o => ("EquipamentosRecolhidos", o)));
                filhos.AddRange(App.Db.EquipamentosAbatidos.Where(a => a.IntervencaoId == intervencao.Id).ToList().Cast<object>().Select(o => ("EquipamentosAbatidos", o)));
                break;

            case "CategoriasIntervencao" when registo is CategoriaIntervencao categoria:
                filhos.AddRange(App.Db.SubCategoriasIntervencao.Where(s => s.CategoriaIntervencaoId == categoria.Id).ToList().Cast<object>().Select(o => ("SubCategoriasIntervencao", o)));
                break;

            case "Equipamentos" when registo is Equipamento equipamento:
                filhos.AddRange(App.Db.EquipamentosRecolhidos.Where(r => r.EquipamentoId == equipamento.Id).ToList().Cast<object>().Select(o => ("EquipamentosRecolhidos", o)));
                filhos.AddRange(App.Db.EquipamentosAbatidos.Where(a => a.EquipamentoId == equipamento.Id).ToList().Cast<object>().Select(o => ("EquipamentosAbatidos", o)));
                break;

            case "CategoriasDisia" when registo is CategoriaDisia categoriaDisia:
                filhos.AddRange(App.Db.AtividadesDisia.Where(a => a.CategoriaDisiaId == categoriaDisia.Id).ToList().Cast<object>().Select(o => ("AtividadesDisia", o)));
                break;

            case "AtividadesDisia" when registo is AtividadeDisia atividade:
                filhos.AddRange(App.Db.EquipamentosRecolhidos.Where(r => r.AtividadeDisiaId == atividade.Id).ToList().Cast<object>().Select(o => ("EquipamentosRecolhidos", o)));
                break;
        }

        return filhos;
    }

    /// <summary>Id do registo (todas as entidades desta aplicação têm uma propriedade "Id" int),
    /// usado apenas para marcar registos já visitados durante a eliminação em cascata (ver
    /// <see cref="EliminarComDependentes"/>) e assim nunca tentar eliminar o mesmo registo duas
    /// vezes nem entrar em ciclo, mesmo que dois caminhos diferentes cheguem ao mesmo descendente.</summary>
    private static int ObterId(object registo) => (int)registo.GetType().GetProperty("Id")!.GetValue(registo)!;

    /// <summary>(2) Elimina um registo e, recursivamente, todos os seus descendentes reais (ver
    /// <see cref="ObterRegistosDependentes"/>), depois de o utilizador ter confirmado
    /// explicitamente esta opção em <see cref="EliminarRegistosSelecionados_Click"/> — a
    /// alternativa, sugerida pelo utilizador, a simplesmente recusar a eliminação sempre que
    /// existirem dados dependentes. Também limpa, a cada nível, as meras ligações de junção (ver
    /// <see cref="LimparLigacoesSemRegistoProprio"/>). <paramref name="visitados"/> evita eliminar
    /// (ou visitar) o mesmo registo mais de uma vez.</summary>
    private static void EliminarComDependentes(string chave, object registo, HashSet<string> visitados)
    {
        var marcador = $"{chave}#{ObterId(registo)}";
        if (!visitados.Add(marcador)) return;

        LimparLigacoesSemRegistoProprio(chave, registo);

        foreach (var (chaveFilha, filho) in ObterRegistosDependentes(chave, registo))
            EliminarComDependentes(chaveFilha, filho, visitados);

        App.Db.Remove(registo);
    }

    private void EliminarRegistosSelecionados_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTabelaEliminar.SelectedItem is not TabelaEliminar tabela) return;

        var selecionados = GridEliminarRegistos.SelectedItems.Cast<object>().ToList();
        if (selecionados.Count == 0)
        {
            MessageBox.Show("Selecione pelo menos um registo para eliminar.", "Nada selecionado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // (2) Antes de mais, limpa sempre as meras ligações de junção (ver
        // LimparLigacoesSemRegistoProprio) — nunca bloqueiam nem pedem confirmação, porque não
        // têm significado próprio e são sempre seguras de remover junto com o registo principal.
        foreach (var item in selecionados)
            LimparLigacoesSemRegistoProprio(tabela.Chave, item);

        // 4: verificar ligações a outros dados ANTES de tentar eliminar, para se poder avisar o
        // utilizador de forma concreta (que descendentes existem), em vez de só se limitar a
        // recusar a eliminação depois de a base de dados a rejeitar.
        var bloqueados = new List<string>();
        foreach (var item in selecionados)
        {
            var dependencias = ObterDependencias(tabela.Chave, item);
            if (dependencias.Count > 0)
                bloqueados.Add($"• {DescricaoRegisto(tabela.Chave, item)}\n     tem: {string.Join(", ", dependencias)}");
        }

        if (bloqueados.Count > 0)
        {
            // (2) Em vez de recusar sempre a eliminação, oferece agora a opção de eliminar também
            // — em cascata, depois de avisar exatamente o que vai ser arrastado — todos os
            // registos dependentes, tal como sugerido: "ao selecionar um registo que tenha
            // ligações (filhos) a aplicação poder apagar todas essas ligações (perguntando
            // primeiro ao utilizador) pois iria-se perder dados".
            var resposta = MessageBox.Show(
                "O(s) seguinte(s) registo(s) têm outros dados dependentes associados:\n\n" +
                string.Join("\n\n", bloqueados) +
                "\n\nDeseja eliminar também, em cascata, todos esses registos dependentes?\n" +
                "Esta ação elimina tudo definitivamente e não pode ser desfeita.\n\n" +
                "Sim = eliminar tudo em cascata　　Não = cancelar e não eliminar nada",
                "Existem dados dependentes — eliminar em cascata?", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (resposta != MessageBoxResult.Yes) return;

            var visitados = new HashSet<string>();
            foreach (var item in selecionados)
                EliminarComDependentes(tabela.Chave, item, visitados);
        }
        else
        {
            var confirmar = MessageBox.Show(
                $"Tem a certeza que deseja eliminar definitivamente {selecionados.Count} registo(s)?\n\n" +
                "Esta ação não pode ser desfeita.",
                "Confirmar eliminação", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmar != MessageBoxResult.Yes) return;

            foreach (var item in selecionados)
                App.Db.Remove(item);
        }

        try
        {
            App.Db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Repõe as remoções pendentes no ChangeTracker para não deixar o contexto num
            // estado inconsistente depois de uma eliminação recusada pela base de dados. Continua
            // a servir de rede de segurança para qualquer dependência não coberta acima.
            foreach (var entrada in App.Db.ChangeTracker.Entries().Where(en => en.State == EntityState.Deleted).ToList())
                entrada.State = EntityState.Unchanged;

            MessageBox.Show(
                "Não foi possível eliminar um ou mais registos porque existem outros dados dependentes " +
                "associados que não foi possível eliminar automaticamente. Verifique manualmente os " +
                "dados relacionados e tente novamente.",
                "Eliminação recusada", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        CarregarGridEliminar();
    }

    /// <summary>Texto curto que identifica um registo (usado apenas na mensagem de eliminação
    /// recusada, para o utilizador perceber a que registo concreto se refere cada aviso).</summary>
    private static string DescricaoRegisto(string chave, object registo) => (chave, registo) switch
    {
        ("Agrupamentos", Agrupamento a) => $"Agrupamento \"{a.Nome}\"",
        ("Escolas", Escola e) => $"Escola \"{e.Nome}\"",
        ("PedidosIntervencao", PedidoIntervencao p) => $"Pedido nº {p.Id} ({p.Razao})",
        ("Intervencoes", Intervencao i) => $"Intervenção nº {i.Id} ({i.Descricao})",
        ("CategoriasIntervencao", CategoriaIntervencao c) => $"Categoria \"{c.Nome}\"",
        ("Equipamentos", Equipamento eq) => $"Equipamento nº série \"{eq.NumeroSerie}\" / inv. \"{eq.NumeroInventario}\"",
        ("EquipamentosRecolhidos", EquipamentoRecolhido r) => $"Recolha nº {r.Id}",
        ("CategoriasDisia", CategoriaDisia cd) => $"Categoria DISIA \"{cd.Nome}\"",
        ("AtividadesDisia", AtividadeDisia at) => $"Atividade DISIA nº {at.Id} ({at.Descricao})",
        _ => "Registo"
    };

    // -------------------------------------------------------------------
    // Exportar Dados (item 7) — operação inversa da Importação: gera Excel
    // com os dados atuais da aplicação, no mesmo formato de cabeçalhos.
    // -------------------------------------------------------------------
    private void ExportarComDialogo(string tituloDialogo, string nomeSugerido, Action<string> exportar)
    {
        var dialog = new SaveFileDialog
        {
            Title = tituloDialogo,
            Filter = "Ficheiros Excel (*.xlsx)|*.xlsx",
            FileName = nomeSugerido
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            exportar(dialog.FileName);
            MessageBox.Show("Exportação concluída com sucesso.", "Exportar Dados",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocorreu um erro durante a exportação:\n{ex.Message}",
                "Erro de exportação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportarTudo_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar todos os dados", $"DISIA_Exportacao_{DateTime.Now:yyyyMMdd}.xlsx",
            caminho => ExcelExportService.ExportarTudo(App.Db, caminho));

    private void ExportarAgrupamentosEscolas_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Agrupamentos e Escolas", "Agrupamentos_Escolas.xlsx",
            caminho => ExcelExportService.ExportarAgrupamentosEscolas(App.Db, caminho));

    private void ExportarEquipamento_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Equipamento", "Equipamento.xlsx",
            caminho => ExcelExportService.ExportarEquipamento(App.Db, caminho));

    private void ExportarIntervencoes_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Intervenções", "Intervencoes.xlsx",
            caminho => ExcelExportService.ExportarIntervencoes(App.Db, caminho));

    private void ExportarEquipamentoAbatido_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Equipamento Abatido", "Equipamento_Abatido.xlsx",
            caminho => ExcelExportService.ExportarEquipamentoAbatido(App.Db, caminho));

    private void ExportarEquipamentoRecolhido_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Equipamento Recolhido", "Equipamento_Recolhido.xlsx",
            caminho => ExcelExportService.ExportarEquipamentoRecolhido(App.Db, caminho));

    private void ExportarAtividadesDisia_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Atividades DISIA", "Atividades_DISIA.xlsx",
            caminho => ExcelExportService.ExportarAtividadesDisia(App.Db, caminho));

    private void ExportarComunicacoes_Click(object sender, RoutedEventArgs e) =>
        ExportarComDialogo("Exportar Comunicações", "Comunicacoes.xlsx",
            caminho => ExcelExportService.ExportarComunicacoes(App.Db, caminho));
}
