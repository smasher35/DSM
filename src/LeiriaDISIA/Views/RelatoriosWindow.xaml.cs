using System.IO;
using System.Windows;
using System.Windows.Controls;
using LeiriaDISIA.Data;
using LeiriaDISIA.Models;
using LeiriaDISIA.Services;
using Microsoft.Win32;

namespace LeiriaDISIA.Views;

public partial class RelatoriosWindow : Window
{
    private byte[]? _imagemPedidosSiga;
    private byte[]? _imagemWorkflowSiga;

    private static readonly string[] NomesMeses =
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
    };

    public RelatoriosWindow()
    {
        InitializeComponent();

        var anoAtual = DateTime.Today.Year;
        CmbAno.ItemsSource = Enumerable.Range(anoAtual - 3, 6).ToList();
        CmbAno.SelectedItem = anoAtual;

        CmbMes.ItemsSource = NomesMeses;
        CmbMes.SelectedIndex = DateTime.Today.Month - 1;

        TxtTelefone.Text = "966 589 120";
        TxtEmail.Text = "paulo@cm-leiria.pt";

        CmbAnoMes_SelectionChanged(this, null!);
    }

    /// <summary>Sempre que o Ano ou o Mês mudam, a secção "PDF Profissional" arranca sempre em
    /// branco — os dados (tickets SIGA, textos de reflexão) são diferentes em cada mês, pelo que
    /// não faz sentido pré-preencher com "0" ou com valores de outro mês, o que poderia induzir em
    /// erro. As imagens SIGA (Pedidos/Workflows) seguem exatamente a mesma lógica: NUNCA são
    /// recarregadas de um mês anterior — têm de ser sempre anexadas de novo antes de gerar, porque
    /// cada relatório usa capturas de ecrã diferentes do SIGA (ver <see cref="LimparImagensSigaAposGeracao"/>).</summary>
    private void CmbAnoMes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbAno.SelectedItem is not int ano || CmbMes.SelectedIndex < 0) return;

        TxtTotalTipificacao.Text = "";
        TxtTotalEstadoTickets.Text = "";
        TxtTotalPasswords.Text = "";
        TxtTotalUtilizadoresCriados.Text = "";

        _imagemPedidosSiga = null;
        _imagemWorkflowSiga = null;
        AtualizarEstadoImagem(null, TxtNomeImagemPedidos);
        AtualizarEstadoImagem(null, TxtNomeImagemWorkflow);

        TxtBalancoGeral.Text = "";
        TxtDesafios.Text = "";
        TxtPropostas.Text = "";
        TxtNotaFinal.Text = "";
    }

    /// <summary>Atualiza o rótulo de uma das imagens (Pedidos/Workflows SIGA) consoante haja, ou
    /// não, uma imagem anexada nesta sessão.</summary>
    private static void AtualizarEstadoImagem(byte[]? imagem, TextBlock rotulo)
    {
        var temImagem = imagem is { Length: > 0 };
        rotulo.Text = temImagem ? "✔ Imagem anexada" : "(nenhuma imagem anexada)";
    }

    /// <summary>Depois de o relatório mensal (PDF ou Word) ser gerado com sucesso, as imagens de
    /// "Pedidos SIGA" e "Workflows SIGA" voltam a ficar limpas — tanto no formulário como na base de
    /// dados — em vez de persistirem para a próxima geração. Este era o comportamento original: como
    /// estas imagens (capturas de ecrã do SIGA) são sempre diferentes a cada relatório, faz mais
    /// sentido ter de as voltar a anexar de cada vez do que arriscar reaproveitar, sem dar por isso,
    /// uma captura de um relatório anterior.</summary>
    private void LimparImagensSigaAposGeracao(int ano, int mes)
    {
        _imagemPedidosSiga = null;
        _imagemWorkflowSiga = null;
        AtualizarEstadoImagem(null, TxtNomeImagemPedidos);
        AtualizarEstadoImagem(null, TxtNomeImagemWorkflow);

        var dados = App.Db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes);
        if (dados == null) return;

        dados.ImagemPedidosSiga = null;
        dados.ImagemWorkflowSiga = null;
        App.Db.SaveChanges();
    }

    private void AnexarImagemPedidosSiga_Click(object sender, RoutedEventArgs e) =>
        AnexarImagem(ref _imagemPedidosSiga, TxtNomeImagemPedidos);

    private void AnexarImagemWorkflowSiga_Click(object sender, RoutedEventArgs e) =>
        AnexarImagem(ref _imagemWorkflowSiga, TxtNomeImagemWorkflow);

    private static void AnexarImagem(ref byte[]? campo, TextBlock rotulo)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher imagem",
            Filter = "Imagens (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Todos os ficheiros (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            campo = File.ReadAllBytes(dialog.FileName);
            rotulo.Text = $"✔ {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível ler a imagem escolhida:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Gera o rascunho da Reflexão Crítica usando um modelo de IA local (ver
    /// <see cref="IaLocalService"/>) — corre inteiramente neste computador, sem enviar dados para
    /// fora da aplicação, e produz um texto adaptado aos dados reais do mês em vez de repetir
    /// sempre as mesmas frases. Como pode demorar (sobretudo na primeira utilização, quando o
    /// modelo ainda não está carregado em memória), corre em segundo plano sem bloquear a janela.</summary>
    private async void GerarRascunhoReflexaoIA_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        var mes = CmbMes.SelectedIndex + 1;

        if (!IaLocalService.ModeloDisponivel)
        {
            MessageBox.Show(
                "Ainda não está configurado nenhum modelo de IA local. Vá a Administração → " +
                "Inteligência Artificial Local para escolher o ficheiro do modelo (.gguf) a usar.",
                "IA Local não configurada", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnGerarRascunhoIA.IsEnabled = false;
        TxtEstadoRascunhoIA.Visibility = Visibility.Visible;
        TxtEstadoRascunhoIA.Text = "⏳ A gerar o rascunho com IA local... Na primeira utilização, isto pode demorar 1-2 minutos, enquanto o modelo é carregado para memória.";

        // Se o utilizador já tiver escrito alguma coisa nestes 4 campos (ex.: uma nota rápida
        // sobre algo que aconteceu no mês), essa informação é lida AQUI, na UI thread, antes de
        // qualquer coisa - tanto porque um TextBox só pode ser acedido a partir da UI thread (o
        // resto do trabalho corre em Task.Run), como para garantir que o texto do utilizador é
        // capturado antes de os campos poderem vir a ser sobrescritos pelo resultado da IA mais
        // abaixo (nunca se perde o que o utilizador escreveu sem primeiro o aproveitar como
        // orientação - ver GerarRascunhoReflexaoCriticaIaAsync). Um campo vazio mantém exatamente
        // o comportamento atual (sem indicação nenhuma nesse campo).
        var indicacaoBalanco = TxtBalancoGeral.Text;
        var indicacaoDesafios = TxtDesafios.Text;
        var indicacaoPropostas = TxtPropostas.Text;
        var indicacaoNotaFinal = TxtNotaFinal.Text;

        try
        {
            var (balanco, desafios, propostas, notaFinal) = await Task.Run(async () =>
            {
                // Usa-se aqui um AppDbContext próprio (em vez do App.Db partilhado) porque este
                // trabalho corre numa thread de fundo, e o Entity Framework Core não é seguro para
                // ser acedido a partir de mais que uma thread ao mesmo tempo — assim evita-se
                // qualquer conflito com o resto da aplicação, que continua a usar o App.Db na UI.
                // O lambda tem de ser "async" (e não só devolver a Task) para o "using" só libertar
                // o contexto depois de todo o trabalho assíncrono (incluindo a chamada à IA) acabar.
                using var dbFundo = new AppDbContext();
                return await new RelatorioService(dbFundo).GerarRascunhoReflexaoCriticaIaAsync(
                    ano, mes, indicacaoBalanco, indicacaoDesafios, indicacaoPropostas, indicacaoNotaFinal);
            });

            TxtBalancoGeral.Text = balanco;
            TxtDesafios.Text = desafios;
            TxtPropostas.Text = propostas;
            TxtNotaFinal.Text = notaFinal;
            TxtEstadoRascunhoIA.Text = "✔ Rascunho gerado com IA local. Reveja e ajuste antes de gerar o relatório.";
        }
        catch (IaLocalIndisponivelException ex)
        {
            // A IA falhou (modelo em falta, sem memória, etc.) — em vez de deixar os campos em
            // branco, usa-se o rascunho determinístico que a própria exceção já traz pronto.
            TxtBalancoGeral.Text = ex.BalancoAlternativo;
            TxtDesafios.Text = ex.DesafiosAlternativo;
            TxtPropostas.Text = ex.PropostasAlternativo;
            TxtNotaFinal.Text = ex.NotaFinalAlternativo;
            TxtEstadoRascunhoIA.Text = $"⚠ Não foi possível usar a IA local ({ex.Message}). Foi usado o rascunho de modelo fixo em alternativa.";
            MessageBox.Show(
                $"Não foi possível gerar o rascunho com IA local:\n{ex.Message}\n\nFoi usado o rascunho de modelo fixo como alternativa.",
                "IA Local indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            TxtEstadoRascunhoIA.Text = $"⚠ Ocorreu um erro inesperado ao gerar com IA local: {ex.Message}";
            MessageBox.Show($"Ocorreu um erro inesperado ao gerar o rascunho com IA local:\n{ex.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnGerarRascunhoIA.IsEnabled = true;
        }
    }

    private void GerarRascunhoReflexao_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        var mes = CmbMes.SelectedIndex + 1;

        var (balanco, desafios, propostas, notaFinal) = new RelatorioService(App.Db).GerarRascunhoReflexaoCritica(ano, mes);
        TxtBalancoGeral.Text = balanco;
        TxtDesafios.Text = desafios;
        TxtPropostas.Text = propostas;
        TxtNotaFinal.Text = notaFinal;
    }

    /// <summary>2.1: limpa os rascunhos automatizados da Reflexão Crítica — tanto os campos em ecrã
    /// como o texto já guardado na base de dados para o mês escolhido, para se poder recomeçar do
    /// zero sem ter de apagar manualmente cada campo.</summary>
    private void LimparRascunhosReflexao_Click(object sender, RoutedEventArgs e)
    {
        if (CmbAno.SelectedItem is not int ano || CmbMes.SelectedIndex < 0) return;
        var mes = CmbMes.SelectedIndex + 1;

        var confirmar = MessageBox.Show(
            "Tem a certeza que pretende limpar os rascunhos da Reflexão Crítica deste mês? Esta ação não pode ser desfeita.",
            "Limpar Rascunhos", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmar != MessageBoxResult.Yes) return;

        TxtBalancoGeral.Text = "";
        TxtDesafios.Text = "";
        TxtPropostas.Text = "";
        TxtNotaFinal.Text = "";

        var dados = App.Db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes);
        if (dados != null)
        {
            dados.TextoBalancoGeral = "";
            dados.TextoDesafios = "";
            dados.TextoPropostas = "";
            dados.TextoNotaFinal = "";
            App.Db.SaveChanges();
        }
    }

    private void GerarMensalPdf_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        var mes = CmbMes.SelectedIndex + 1;

        int.TryParse(TxtTotalTipificacao.Text, out var totalTipificacao);
        int.TryParse(TxtTotalEstadoTickets.Text, out var totalEstadoTickets);
        int.TryParse(TxtTotalPasswords.Text, out var totalPasswords);
        int.TryParse(TxtTotalUtilizadoresCriados.Text, out var totalUtilizadoresCriados);

        // Guarda/atualiza os dados complementares deste mês (estatísticas SIGA, imagens e textos de
        // reflexão), para que reabrir o mesmo mês mais tarde já venha tudo preenchido.
        var dados = App.Db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes);
        if (dados == null)
        {
            dados = new RelatorioMensalDados { Ano = ano, Mes = mes };
            App.Db.RelatoriosMensaisDados.Add(dados);
        }

        dados.TotalAlteracaoTipificacao = totalTipificacao;
        dados.TotalEstadoTickets = totalEstadoTickets;
        dados.TotalAlteracaoPasswords = totalPasswords;
        dados.TotalUtilizadoresCriados = totalUtilizadoresCriados;
        dados.ImagemPedidosSiga = _imagemPedidosSiga;
        dados.ImagemWorkflowSiga = _imagemWorkflowSiga;
        dados.TextoBalancoGeral = TxtBalancoGeral.Text;
        dados.TextoDesafios = TxtDesafios.Text;
        dados.TextoPropostas = TxtPropostas.Text;
        dados.TextoNotaFinal = TxtNotaFinal.Text;
        App.Db.SaveChanges();

        var gerado = GerarPdf($"Relatorio_Atividades_{NomesMeses[mes - 1]}_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarRelatorioMensalPdf(caminho, ano, mes,
                TxtAutor.Text, TxtDivisao.Text, TxtTelefone.Text, TxtEmail.Text));

        if (gerado)
            LimparImagensSigaAposGeracao(ano, mes);
    }

    private void MenuPrincipal_Click(object sender, RoutedEventArgs e) => Close();

    private void GerarMensal_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        var mes = CmbMes.SelectedIndex + 1;

        int.TryParse(TxtTotalTipificacao.Text, out var totalTipificacao);
        int.TryParse(TxtTotalEstadoTickets.Text, out var totalEstadoTickets);
        int.TryParse(TxtTotalPasswords.Text, out var totalPasswords);
        int.TryParse(TxtTotalUtilizadoresCriados.Text, out var totalUtilizadoresCriados);

        // 2.2/2.4: tal como no botão do PDF, guarda/atualiza primeiro os dados complementares deste
        // mês (estatísticas SIGA, imagens e textos de reflexão) — o Word e o PDF partilham a mesma
        // secção de campos e têm de ir sempre buscar os valores mais recentes da mesma forma.
        var dados = App.Db.RelatoriosMensaisDados.FirstOrDefault(r => r.Ano == ano && r.Mes == mes);
        if (dados == null)
        {
            dados = new RelatorioMensalDados { Ano = ano, Mes = mes };
            App.Db.RelatoriosMensaisDados.Add(dados);
        }

        dados.TotalAlteracaoTipificacao = totalTipificacao;
        dados.TotalEstadoTickets = totalEstadoTickets;
        dados.TotalAlteracaoPasswords = totalPasswords;
        dados.TotalUtilizadoresCriados = totalUtilizadoresCriados;
        dados.ImagemPedidosSiga = _imagemPedidosSiga;
        dados.ImagemWorkflowSiga = _imagemWorkflowSiga;
        dados.TextoBalancoGeral = TxtBalancoGeral.Text;
        dados.TextoDesafios = TxtDesafios.Text;
        dados.TextoPropostas = TxtPropostas.Text;
        dados.TextoNotaFinal = TxtNotaFinal.Text;
        App.Db.SaveChanges();

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório mensal",
            Filter = "Documento Word (*.docx)|*.docx",
            FileName = $"Relatorio_Atividades_DISIA_{NomesMeses[mes - 1]}_{ano}.docx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            // 2.5: voltou a gerar-se o Word com conteúdo nativo (texto real, selecionável e
            // editável, tabelas, imagens) em vez de rasterizar cada página do PDF como uma imagem de
            // página inteira — essa abordagem, apesar de visualmente idêntica ao PDF, tornava o
            // documento inteiro impossível de selecionar ou editar (era só uma sequência de
            // "fotografias"), e a combinação de uma imagem a ocupar a página toda com uma quebra de
            // página manual a seguir estava também a criar uma página em branco extra a mais por
            // secção (ver GerarRelatorioMensalWord, agora sem utilização, mantido apenas para
            // referência). O Word nativo tem uma estrutura semelhante ao PDF, mas não é uma cópia
            // visual pixel a pixel — é um documento normal, à parte.
            servico.GerarRelatorioMensal(ano, mes, TxtAutor.Text, TxtDivisao.Text,
                TxtTelefone.Text, TxtEmail.Text, dialog.FileName);

            LimparImagensSigaAposGeracao(ano, mes);

            var abrir = MessageBox.Show("Relatório gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GerarAnual_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório anual",
            Filter = "Documento Word (*.docx)|*.docx",
            FileName = $"Relatorio_Anual_DISIA_{ano}.docx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            servico.GerarRelatorioAnual(ano, TxtAutor.Text, TxtDivisao.Text,
                TxtTelefone.Text, TxtEmail.Text, dialog.FileName);

            var abrir = MessageBox.Show("Relatório gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GerarListaEscolas_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar lista total de escolas",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = $"Lista_Total_Escolas_{DateTime.Today:yyyyMMdd}.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var servico = new RelatorioService(App.Db);
            servico.GerarListaTotalEscolas(dialog.FileName);

            var abrir = MessageBox.Show("Relatório PDF gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Helper partilhado por todos os relatórios PDF novos: pede onde guardar, chama o gerador,
    /// e oferece para abrir o ficheiro de imediato - evita repetir este bloco em cada botão.
    /// </summary>
    /// <summary>Devolve <c>true</c> quando o PDF chega a ser gerado com sucesso (para quem chama
    /// precisar de fazer alguma coisa a seguir, ex.: limpar as imagens SIGA do relatório mensal) —
    /// <c>false</c> se o utilizador cancelar a caixa de diálogo ou se a geração falhar.</summary>
    private static bool GerarPdf(string nomeFicheiroSugerido, Action<string> gerador)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Guardar relatório",
            Filter = "Ficheiro PDF (*.pdf)|*.pdf",
            FileName = nomeFicheiroSugerido
        };
        if (dialog.ShowDialog() != true) return false;

        try
        {
            gerador(dialog.FileName);

            var abrir = MessageBox.Show("Relatório PDF gerado com sucesso. Deseja abri-lo agora?",
                "Concluído", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (abrir == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar o relatório:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void GerarListaAgrupamentos_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Agrupamentos_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaAgrupamentos(caminho));

    private void GerarListaCodigosGepe_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Codigos_GEPE_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaCodigosGepe(caminho));

    private void GerarListaIntervencoesAno_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        GerarPdf($"Lista_Intervencoes_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaIntervencoes(caminho, ano));
    }

    private void GerarListaIntervencoesTodas_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Total_Intervencoes_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaIntervencoes(caminho, ano: null));

    private void GerarListaIntervencoesMesCorrente_Click(object sender, RoutedEventArgs e)
    {
        var hoje = DateTime.Today;
        var inicioMes = new DateTime(hoje.Year, hoje.Month, 1);
        GerarPdf($"Lista_Intervencoes_{NomesMeses[hoje.Month - 1]}_{hoje.Year}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaIntervencoes(caminho, dataInicio: inicioMes, dataFim: hoje));
    }

    private void GerarListaIntervencoesPeriodo_Click(object sender, RoutedEventArgs e)
    {
        if (DpPeriodoInicio.SelectedDate == null || DpPeriodoFim.SelectedDate == null)
        {
            MessageBox.Show("Escolha a data de início e a data de fim do período.", "Datas em falta",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (DpPeriodoInicio.SelectedDate > DpPeriodoFim.SelectedDate)
        {
            MessageBox.Show("A data de início não pode ser posterior à data de fim.", "Datas inválidas",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var inicio = DpPeriodoInicio.SelectedDate.Value;
        var fim = DpPeriodoFim.SelectedDate.Value;
        GerarPdf($"Lista_Intervencoes_{inicio:yyyyMMdd}_a_{fim:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaIntervencoes(caminho, dataInicio: inicio, dataFim: fim));
    }

    private void GerarResumoIntervencoesPorAgrupamentoAno_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        GerarPdf($"Resumo_Intervencoes_Agrupamento_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorAgrupamento(caminho, ano));
    }

    private void GerarResumoIntervencoesPorAgrupamentoMesCorrente_Click(object sender, RoutedEventArgs e)
    {
        var hoje = DateTime.Today;
        GerarPdf($"Resumo_Intervencoes_Agrupamento_{NomesMeses[hoje.Month - 1]}_{hoje.Year}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorAgrupamento(caminho, hoje.Year, hoje.Month));
    }

    private void GerarResumoIntervencoesPorAgrupamentoMesEscolhido_Click(object sender, RoutedEventArgs e)
    {
        var escolha = EscolherMesWindow.Perguntar(this, CmbAno?.SelectedItem as int?, CmbMes != null ? CmbMes.SelectedIndex + 1 : null);
        if (escolha == null) return;
        var (ano, mes) = escolha.Value;

        GerarPdf($"Resumo_Intervencoes_Agrupamento_{NomesMeses[mes - 1]}_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorAgrupamento(caminho, ano, mes));
    }

    private void GerarResumoIntervencoesPorAgrupamentoTodas_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Intervencoes_Agrupamento_Total_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorAgrupamento(caminho, ano: null));

    private void GerarResumoIntervencoesPorCategoriaAno_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        GerarPdf($"Resumo_Intervencoes_Categoria_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorCategoria(caminho, ano));
    }

    private void GerarResumoIntervencoesPorCategoriaMesCorrente_Click(object sender, RoutedEventArgs e)
    {
        var hoje = DateTime.Today;
        GerarPdf($"Resumo_Intervencoes_Categoria_{NomesMeses[hoje.Month - 1]}_{hoje.Year}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorCategoria(caminho, hoje.Year, hoje.Month));
    }

    private void GerarResumoIntervencoesPorCategoriaMesEscolhido_Click(object sender, RoutedEventArgs e)
    {
        var escolha = EscolherMesWindow.Perguntar(this, CmbAno?.SelectedItem as int?, CmbMes != null ? CmbMes.SelectedIndex + 1 : null);
        if (escolha == null) return;
        var (ano, mes) = escolha.Value;

        GerarPdf($"Resumo_Intervencoes_Categoria_{NomesMeses[mes - 1]}_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorCategoria(caminho, ano, mes));
    }

    private void GerarResumoIntervencoesPorCategoriaTodas_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Intervencoes_Categoria_Total_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorCategoria(caminho, ano: null));

    private void GerarResumoTipoAgrupamentoAno_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        GerarPdf($"Resumo_Tipo_Agrupamento_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorTipoAgrupamento(caminho, ano));
    }

    private void GerarResumoTipoAgrupamentoMesCorrente_Click(object sender, RoutedEventArgs e)
    {
        var hoje = DateTime.Today;
        GerarPdf($"Resumo_Tipo_Agrupamento_{NomesMeses[hoje.Month - 1]}_{hoje.Year}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorTipoAgrupamento(caminho, hoje.Year, hoje.Month));
    }

    private void GerarResumoTipoAgrupamentoMesEscolhido_Click(object sender, RoutedEventArgs e)
    {
        var escolha = EscolherMesWindow.Perguntar(this, CmbAno?.SelectedItem as int?, CmbMes != null ? CmbMes.SelectedIndex + 1 : null);
        if (escolha == null) return;
        var (ano, mes) = escolha.Value;

        GerarPdf($"Resumo_Tipo_Agrupamento_{NomesMeses[mes - 1]}_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorTipoAgrupamento(caminho, ano, mes));
    }

    private void GerarResumoTipoAgrupamentoTodas_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Tipo_Agrupamento_Total_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoIntervencoesPorTipoAgrupamento(caminho, ano: null));

    private void GerarListaPedidos_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Pedidos_Intervencao_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaPedidosIntervencao(caminho));

    private void GerarResumoPedidosPorEstado_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Pedidos_Estado_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoPedidosPorEstado(caminho));

    private void GerarListaAtividadesDisiaAno_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        GerarPdf($"Atividades_DISIA_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaAtividadesDisia(caminho, ano));
    }

    // O utilizador não conseguia gerar a Lista de Atividades filtrada por mês (só por ano ou sem
    // filtro nenhum) — ao contrário do "Resumo por Categoria" ao lado, que já tinha "Mês
    // Corrente"/"Mês Escolhido". Estes dois botões seguem exatamente o mesmo padrão (mesmo
    // CmbAno/CmbMes partilhados desta janela — ver comentário em Views/RelatoriosWindow.xaml sobre
    // "usados nos separadores... nos botões 'Mês Escolhido'"), agora também aqui.
    private void GerarListaAtividadesDisiaMesCorrente_Click(object sender, RoutedEventArgs e)
    {
        var hoje = DateTime.Today;
        GerarPdf($"Atividades_DISIA_{NomesMeses[hoje.Month - 1]}_{hoje.Year}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaAtividadesDisia(caminho, hoje.Year, hoje.Month));
    }

    private void GerarListaAtividadesDisiaMesEscolhido_Click(object sender, RoutedEventArgs e)
    {
        var escolha = EscolherMesWindow.Perguntar(this, CmbAno?.SelectedItem as int?, CmbMes != null ? CmbMes.SelectedIndex + 1 : null);
        if (escolha == null) return;
        var (ano, mes) = escolha.Value;

        GerarPdf($"Atividades_DISIA_{NomesMeses[mes - 1]}_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaAtividadesDisia(caminho, ano, mes));
    }

    private void GerarListaAtividadesDisiaTodas_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Total_Atividades_DISIA_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaAtividadesDisia(caminho, ano: null));

    private void GerarResumoAtividadesDisiaPorCategoriaAno_Click(object sender, RoutedEventArgs e)
    {
        var ano = (int)CmbAno.SelectedItem;
        GerarPdf($"Resumo_Atividades_DISIA_Categoria_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoAtividadesDisiaPorCategoria(caminho, ano));
    }

    private void GerarResumoAtividadesDisiaPorCategoriaMesCorrente_Click(object sender, RoutedEventArgs e)
    {
        var hoje = DateTime.Today;
        GerarPdf($"Resumo_Atividades_DISIA_Categoria_{NomesMeses[hoje.Month - 1]}_{hoje.Year}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoAtividadesDisiaPorCategoria(caminho, hoje.Year, hoje.Month));
    }

    private void GerarResumoAtividadesDisiaPorCategoriaMesEscolhido_Click(object sender, RoutedEventArgs e)
    {
        var escolha = EscolherMesWindow.Perguntar(this, CmbAno?.SelectedItem as int?, CmbMes != null ? CmbMes.SelectedIndex + 1 : null);
        if (escolha == null) return;
        var (ano, mes) = escolha.Value;

        GerarPdf($"Resumo_Atividades_DISIA_Categoria_{NomesMeses[mes - 1]}_{ano}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoAtividadesDisiaPorCategoria(caminho, ano, mes));
    }

    private void GerarResumoAtividadesDisiaPorCategoriaTodas_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Atividades_DISIA_Categoria_Total_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoAtividadesDisiaPorCategoria(caminho, ano: null));

    // Item 3.1: mesma lógica do botão equivalente em Equipamento (ver
    // PesquisaAvancadaEquipamento_Click) — abre uma janela própria em vez de gerar logo o PDF.
    private void PesquisaAvancadaAtividadeDisia_Click(object sender, RoutedEventArgs e)
    {
        var janela = new PesquisaAvancadaAtividadeDisiaWindow { Owner = this };
        janela.ShowDialog();
    }

    private void GerarListaEquipamento_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Equipamento_Informatico_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaEquipamento(caminho));

    private void GerarResumoObsolescencia_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Obsolescencia_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoObsolescencia(caminho));

    private void GerarListaEquipamentoAbatido_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Equipamento_Abatido_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaEquipamentoAbatido(caminho));

    private void GerarListaEquipamentoRecolhido_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Equipamento_Recolhido_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaEquipamentoRecolhido(caminho));

    // Item 2.1: ao contrário dos restantes botões desta janela (que geram logo o PDF via GerarPdf),
    // este abre uma janela própria — a pesquisa tem vários passos (escolher filtros, pesquisar, ver
    // uma pré-visualização) que não cabem num botão só.
    private void PesquisaAvancadaEquipamento_Click(object sender, RoutedEventArgs e)
    {
        var janela = new PesquisaAvancadaEquipamentoWindow { Owner = this };
        janela.ShowDialog();
    }

    private void GerarListaComunicacoes_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Comunicacoes_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaComunicacoes(caminho));

    private void GerarResumoComunicacoesPorEstado_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Comunicacoes_Estado_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoComunicacoesPorEstado(caminho));

    private void GerarResumoInfraestrutura_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Resumo_Infraestrutura_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarResumoInfraestrutura(caminho));

    private void GerarListaContactos_Click(object sender, RoutedEventArgs e) =>
        GerarPdf($"Lista_Contactos_{DateTime.Today:yyyyMMdd}.pdf",
            caminho => new RelatorioService(App.Db).GerarListaContactos(caminho));
}
