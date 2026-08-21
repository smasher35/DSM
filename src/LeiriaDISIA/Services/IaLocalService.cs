using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LeiriaDISIA.Services;

/// <summary>
/// Geração de texto com um modelo de linguagem (LLM) que corre inteiramente na máquina local,
/// através da biblioteca LLamaSharp (baseada em llama.cpp) — nenhum dado sai do computador, ao
/// contrário de usar uma API de IA na cloud (ex: ChatGPT/Claude). É por isto que os rascunhos
/// podem variar de mês para mês e adaptar-se ao que realmente foi feito, em vez de seguirem
/// sempre o mesmo texto fixo com apenas alguns números trocados.
///
/// Usa-se um ficheiro de modelo no formato GGUF (por exemplo, "Phi-3-mini-4k-instruct-q4.gguf",
/// ~2,4GB), que tem de ser descarregado manualmente UMA VEZ (ex: a partir do Hugging Face) e
/// colocado no caminho configurado em Administração → Inteligência Artificial Local. O modelo
/// não vem incluído na aplicação porque seria demasiado grande para o instalador.
///
/// A geração de um texto de algumas centenas de palavras em CPU pode demorar entre poucos
/// segundos e 1-2 minutos, consoante o computador — por isso todo o trabalho pesado (carregar o
/// modelo e gerar o texto) deve ser sempre chamado a partir de uma thread de fundo
/// (<see cref="System.Threading.Tasks.Task.Run(Action)"/>), nunca diretamente na UI thread.
/// </summary>
public sealed class IaLocalService : IDisposable
{
    private static readonly Lazy<IaLocalService> _instancia = new(() => new IaLocalService());

    /// <summary>Instância única e partilhada, para o modelo só ser carregado uma vez em memória e
    /// reaproveitado entre pedidos (carregar o modelo é a parte mais lenta de todo o processo).</summary>
    public static IaLocalService Instancia => _instancia.Value;

    private readonly object _lock = new();
    private LLamaWeights? _pesosModelo;
    private StatelessExecutor? _executor;
    private string? _caminhoModeloCarregado;

    private IaLocalService()
    {
    }

    private class ConfiguracaoIa
    {
        public string? CaminhoModelo { get; set; }
    }

    private static string CaminhoFicheiroConfiguracao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LeiriaDISIA", "ia-local-config.json");

    /// <summary>Pasta sugerida por omissão para lá se colocar o ficheiro do modelo (.gguf), caso o
    /// utilizador ainda não tenha escolhido um caminho — ver Administração → Inteligência
    /// Artificial Local. É criada automaticamente se ainda não existir.</summary>
    public static string PastaModelosPorOmissao
    {
        get
        {
            var pasta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeiriaDISIA", "ModelosIA");
            Directory.CreateDirectory(pasta);
            return pasta;
        }
    }

    /// <summary>Caminho do ficheiro .gguf configurado pelo utilizador (guardado em disco, para se
    /// manter entre arranques da aplicação). Null se ainda não foi configurado nenhum.</summary>
    public static string? CaminhoModeloConfigurado
    {
        get
        {
            try
            {
                if (!File.Exists(CaminhoFicheiroConfiguracao)) return null;
                var json = File.ReadAllText(CaminhoFicheiroConfiguracao);
                return JsonSerializer.Deserialize<ConfiguracaoIa>(json)?.CaminhoModelo;
            }
            catch
            {
                // Ficheiro de configuração corrompido ou ilegível — trata-se como "não configurado"
                // em vez de rebentar a aplicação; o utilizador volta a escolher o modelo.
                return null;
            }
        }
        set
        {
            var pasta = Path.GetDirectoryName(CaminhoFicheiroConfiguracao);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            var json = JsonSerializer.Serialize(new ConfiguracaoIa { CaminhoModelo = value });
            File.WriteAllText(CaminhoFicheiroConfiguracao, json);
        }
    }

    /// <summary>True se há um modelo configurado e o respetivo ficheiro existe em disco — usado
    /// para decidir se se mostra o botão "Gerar com IA Local" ou uma mensagem a pedir configuração.</summary>
    public static bool ModeloDisponivel
    {
        get
        {
            var caminho = CaminhoModeloConfigurado;
            return !string.IsNullOrWhiteSpace(caminho) && File.Exists(caminho);
        }
    }

    /// <summary>Garante que o modelo configurado está carregado em memória, carregando-o (ou
    /// recarregando-o, se o caminho configurado tiver mudado) se necessário. Lança
    /// <see cref="InvalidOperationException"/>, com uma mensagem já pronta a mostrar ao utilizador,
    /// se não houver nenhum modelo configurado/válido.</summary>
    private void GarantirModeloCarregado()
    {
        var caminho = CaminhoModeloConfigurado;
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
        {
            throw new InvalidOperationException(
                "Não está configurado nenhum modelo de IA local válido. Vá a Administração → " +
                "Inteligência Artificial Local para escolher o ficheiro do modelo (.gguf).");
        }

        lock (_lock)
        {
            if (_executor != null && _caminhoModeloCarregado == caminho) return;

            // Se já havia um modelo (diferente) carregado, liberta a memória antes de carregar o novo.
            _pesosModelo?.Dispose();

            var parametros = new ModelParams(caminho)
            {
                ContextSize = 4096,
                GpuLayerCount = 0, // CPU apenas — ver nota no .csproj sobre backend CUDA opcional
            };

            _pesosModelo = LLamaWeights.LoadFromFile(parametros);
            _executor = new StatelessExecutor(_pesosModelo, parametros);
            _caminhoModeloCarregado = caminho;
        }
    }

    /// <summary>Gera texto a partir de um prompt, correndo inteiramente na máquina local (sem
    /// qualquer ligação à internet durante a geração). Deve ser chamado a partir de uma thread de
    /// fundo — ver nota na documentação da classe.</summary>
    public async Task<string> GerarTextoAsync(
        string prompt, int maxTokens = 900, float temperatura = 0.75f, CancellationToken ct = default)
    {
        GarantirModeloCarregado();

        var parametrosInferencia = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new List<string> { "###FIM###" },
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = temperatura },
        };

        var resultado = new StringBuilder();
        await foreach (var fragmento in _executor!.InferAsync(prompt, parametrosInferencia, ct))
            resultado.Append(fragmento);

        return resultado.ToString();
    }

    public void Dispose()
    {
        _pesosModelo?.Dispose();
    }
}
