namespace LeiriaDISIA.Services.Rotas;

/// <summary>
/// Configuração do Planeamento de Rotas — só a chave de API do OpenRouteService, por agora.
///
/// A chave NUNCA é guardada no código-fonte nem na base de dados: é lida da variável de ambiente
/// <see cref="NomeVariavelAmbiente"/>, definida uma única vez em cada computador onde a aplicação
/// corre (Definições do Sistema → Variáveis de Ambiente, ou <c>setx DISIA_ORS_API_KEY "chave" /M</c>
/// numa consola de administrador). Obtém-se uma chave gratuita em https://openrouteservice.org/dev/#/signup.
///
/// IMPORTANTE: A chave de API é um segredo e nunca deve ser incluída em código-fonte, ficheiros de
/// configuração versionados, ou artefactos de compilação. Deve ser provisionada no momento da instalação
/// através de gestão segura de segredos (variáveis de ambiente, cofres de chaves, ou sistemas de gestão
/// de configuração). O ficheiro install_api.txt contém apenas um placeholder e instruções.
/// </summary>
public static class ConfiguracaoRotas
{
    public const string NomeVariavelAmbiente = "DISIA_ORS_API_KEY";

    /// <summary><c>null</c> quando a variável de ambiente não estiver definida — todos os pontos de
    /// chamada devem tratar isto como "geocodificação/routing indisponível" e mostrar uma mensagem
    /// clara ao utilizador (nunca rebentar nem tentar chamar a API na mesma sem chave).</summary>
    public static string? ChaveApi => Environment.GetEnvironmentVariable(NomeVariavelAmbiente);

    public static bool Configurado => !string.IsNullOrWhiteSpace(ChaveApi);
}
