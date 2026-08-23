using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeiriaDISIA.Models;

namespace LeiriaDISIA.Services.Rotas;

/// <summary>
/// Implementação de <see cref="IGeocodingService"/> e <see cref="IRoutingService"/> para o
/// OpenRouteService (openrouteservice.org) — geocodificação (Pelias), distância/duração rodoviária
/// e cálculo de rota com múltiplas paragens, ambos com a API de Directions (ver
/// <see cref="OtimizarRotaAsync"/> para a razão de não se usar a API de Matriz, mais rápida mas sem
/// suporte para "evitar autoestrada"), tudo na mesma plataforma gratuita (até 2.500 pedidos/dia —
/// muito acima do volume esperado desta aplicação).
///
/// Nunca lança exceção para erros "esperados" (morada inválida, sem rede, limite excedido, chave em
/// falta) — todos voltam como um resultado com <c>Sucesso = false</c> e
/// <c>MensagemErro</c> em português, para o chamador poder mostrar diretamente ao utilizador. Só
/// deixa propagar exceções verdadeiramente inesperadas (bug de programação).
/// </summary>
public class OpenRouteServiceClient : IGeocodingService, IRoutingService
{
    private const string BaseUrl = "https://api.openrouteservice.org";

    // HttpClient é seguro para reutilizar entre pedidos (é isso que evita esgotar sockets) — por
    // isso é estático e criado uma única vez, não um "new HttpClient()" por pedido.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<ResultadoGeocodificacao> GeocodificarAsync(string moradaCompleta, CancellationToken ct = default)
    {
        if (!ConfiguracaoRotas.Configurado)
            return ResultadoGeocodificacao.Falha(MensagemChaveEmFalta);

        if (string.IsNullOrWhiteSpace(moradaCompleta))
            return ResultadoGeocodificacao.Falha("A escola não tem morada preenchida — não é possível geocodificar.");

        try
        {
            // "focus.point" pesa a favor de resultados perto de Leiria (não filtra os outros, só
            // desempata a favor destes) — sem isto, moradas/nomes de lugar comuns a várias terras de
            // Portugal (ex.: "Marinheiros" existe fora do concelho de Leiria) podiam ser
            // geocodificados para o sítio errado, dando depois distâncias completamente absurdas.
            // As coordenadas usadas são as da própria sede (ver EnderecoSedeMunicipio), como
            // aproximação razoável ao centro da área de atuação da DISIA.
            using var pedido = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/geocode/search?text={Uri.EscapeDataString(moradaCompleta)}&boundary.country=PT&size=1" +
                $"&focus.point.lat={EnderecoSedeMunicipio.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $"&focus.point.lon={EnderecoSedeMunicipio.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            pedido.Headers.TryAddWithoutValidation("Authorization", ConfiguracaoRotas.ChaveApi);

            using var resposta = await Http.SendAsync(pedido, ct);

            if (!resposta.IsSuccessStatusCode)
                return ResultadoGeocodificacao.Falha(await MensagemErroHttp(resposta, ct));

            var dados = await resposta.Content.ReadFromJsonAsync<GeocodeResponse>(JsonOpcoes, ct);
            var coordenadas = dados?.Features?.FirstOrDefault()?.Geometry?.Coordinates;

            if (coordenadas is not { Length: 2 })
                return ResultadoGeocodificacao.Falha(
                    $"Não foi possível encontrar a morada \"{moradaCompleta}\" — confirme se está bem escrita " +
                    "(idealmente com código postal) e tente novamente.");

            // GeoJSON devolve sempre [longitude, latitude], não [latitude, longitude].
            return ResultadoGeocodificacao.Ok(new CoordenadaGeografica(coordenadas[1], coordenadas[0]));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoGeocodificacao.Falha("O serviço de geocodificação demorou demasiado tempo a responder. Tente novamente.");
        }
        catch (HttpRequestException ex)
        {
            return ResultadoGeocodificacao.Falha($"Sem ligação ao serviço de geocodificação: {ex.Message}");
        }
    }

    public async Task<ResultadoEnderecoInverso> GeocodificarInversoAsync(CoordenadaGeografica coordenada, CancellationToken ct = default)
    {
        if (!ConfiguracaoRotas.Configurado)
            return ResultadoEnderecoInverso.Falha(MensagemChaveEmFalta);

        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/geocode/reverse?point.lat={coordenada.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                $"&point.lon={coordenada.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                "&boundary.country=PT&size=1");
            pedido.Headers.TryAddWithoutValidation("Authorization", ConfiguracaoRotas.ChaveApi);

            using var resposta = await Http.SendAsync(pedido, ct);

            if (!resposta.IsSuccessStatusCode)
                return ResultadoEnderecoInverso.Falha(await MensagemErroHttp(resposta, ct));

            var dados = await resposta.Content.ReadFromJsonAsync<GeocodeResponse>(JsonOpcoes, ct);
            var propriedades = dados?.Features?.FirstOrDefault()?.Properties;

            // Sem resultado nenhum (coordenada em zona isolada, sem morada mapeada) não é um erro —
            // a coordenada em si continua válida, só não há morada para atribuir automaticamente; o
            // chamador decide se avança sem preencher a morada (ver EscolaGeocodingService).
            if (propriedades == null)
                return ResultadoEnderecoInverso.Ok(morada: null, codigoPostal: null, localidade: null);

            // "Rua + nº porta" quando ambos existem; cai para só um dos dois (ou para o "label"
            // genérico do Pelias) quando faltar alguma parte — mais fiável do que montar sempre
            // "rua, nº" e arriscar uma vírgula a mais quando o nº não existe.
            var morada = (propriedades.Street, propriedades.HouseNumber) switch
            {
                (not null and not "", not null and not "") => $"{propriedades.Street} {propriedades.HouseNumber}",
                (not null and not "", _) => propriedades.Street,
                _ => propriedades.Label
            };

            return ResultadoEnderecoInverso.Ok(morada, propriedades.PostalCode, propriedades.Locality);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoEnderecoInverso.Falha("O serviço de geocodificação demorou demasiado tempo a responder. Tente novamente.");
        }
        catch (HttpRequestException ex)
        {
            return ResultadoEnderecoInverso.Falha($"Sem ligação ao serviço de geocodificação: {ex.Message}");
        }
    }

    public async Task<ResultadoDistancia> CalcularDistanciaAsync(CoordenadaGeografica origem, CoordenadaGeografica destino, CancellationToken ct = default)
    {
        if (!ConfiguracaoRotas.Configurado)
            return ResultadoDistancia.Falha(MensagemChaveEmFalta);

        try
        {
            var corpo = new
            {
                coordinates = new[]
                {
                    new[] { origem.Longitude, origem.Latitude },
                    new[] { destino.Longitude, destino.Latitude }
                },
                // Pedido explícito: nunca sugerir autoestrada/via rápida — as equipas fazem estas
                // deslocações em viatura de serviço por estradas normais, e a rota "mais rápida"
                // por autoestrada dava distâncias bastante diferentes da realidade destas viagens.
                options = new { avoid_features = new[] { "highways" } }
            };

            using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/directions/driving-car");
            pedido.Headers.TryAddWithoutValidation("Authorization", ConfiguracaoRotas.ChaveApi);
            pedido.Content = JsonContent.Create(corpo);

            using var resposta = await Http.SendAsync(pedido, ct);

            if (!resposta.IsSuccessStatusCode)
                return ResultadoDistancia.Falha(await MensagemErroHttp(resposta, ct));

            var dados = await resposta.Content.ReadFromJsonAsync<DirectionsResponse>(JsonOpcoes, ct);
            var resumo = dados?.Routes?.FirstOrDefault()?.Summary;

            if (resumo == null)
                return ResultadoDistancia.Falha("Não foi possível calcular a rota rodoviária entre os dois pontos.");

            return ResultadoDistancia.Ok(
                Math.Round(resumo.Distance / 1000.0, 1),
                (int)Math.Round(resumo.Duration / 60.0));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoDistancia.Falha("O serviço de rotas demorou demasiado tempo a responder. Tente novamente.");
        }
        catch (HttpRequestException ex)
        {
            return ResultadoDistancia.Falha($"Sem ligação ao serviço de rotas: {ex.Message}");
        }
    }

    /// <summary>Nº máximo de paragens aceites numa única chamada de Planeamento de Rotas. A ordem é
    /// decidida por "vizinho mais próximo" (ver <see cref="OtimizarRotaAsync"/>), o que pede
    /// distâncias par-a-par à API de Directions à medida que avança — no pior caso, N(N+1)/2
    /// pedidos. Com este limite, o pior caso são ~1275 pedidos, seguros dentro da quota diária
    /// gratuita (2.500/dia) e ainda razoáveis em tempo com os pedidos em paralelo (ver
    /// <see cref="ConcorrenciaMaxima"/>) — mas rotas grandes vão demorar mais a calcular do que as
    /// pequenas (uso normal: poucas escolas por dia).</summary>
    public const int LimiteMaximoParagens = 50;

    /// <summary>Nº de pedidos HTTP simultâneos permitidos ao decidir a ordem da rota (ver
    /// <see cref="OtimizarRotaAsync"/>) — um valor conservador, para não arriscar exceder o limite
    /// de pedidos por minuto da conta gratuita do OpenRouteService.</summary>
    private const int ConcorrenciaMaxima = 5;

    /// <summary>Calcula a ordem da rota com um critério de negócio específico (não é o que a API de
    /// Otimização/VROOM calcularia, que minimiza a distância/duração total — aqui o pedido foi
    /// deliberadamente outro): a 1ª paragem é sempre a escola mais LONGE da sede, e a partir daí
    /// cada paragem seguinte é sempre a mais PERTO da paragem anterior (vizinho mais próximo), até
    /// esgotar a seleção. Nunca passa pela sede entre paragens — só no fim, se
    /// <paramref name="regresso"/> não for <c>null</c>.
    ///
    /// Todas as distâncias usadas para decidir a ordem (e as que ficam no plano/PDF) vêm da API de
    /// Directions com "evitar autoestrada" (ver <see cref="CalcularDistanciaAsync"/>) — já não se
    /// usa a API de Matriz aqui: essa é mais rápida (1 só pedido), mas não respeita essa opção, o
    /// que dava uma ordem decidida com um critério diferente do que os números finais mostravam
    /// (podendo escolher uma paragem "mais perto por autoestrada" que na prática, sem autoestrada,
    /// não era a mais perto). Ao calcular tudo com o mesmo critério, a ordem fica consistente com as
    /// distâncias apresentadas — ao preço de mais pedidos HTTP (por isso o paralelismo limitado, ver
    /// <see cref="ConcorrenciaMaxima"/>, e o aviso em <see cref="LimiteMaximoParagens"/>).</summary>
    public async Task<ResultadoOtimizacaoRota> OtimizarRotaAsync(
        CoordenadaGeografica origem, List<CoordenadaGeografica> paragens, CoordenadaGeografica? regresso, CancellationToken ct = default)
    {
        if (!ConfiguracaoRotas.Configurado)
            return ResultadoOtimizacaoRota.Falha(MensagemChaveEmFalta);

        if (paragens.Count == 0)
            return ResultadoOtimizacaoRota.Falha("Selecione pelo menos um pedido para planear a rota.");

        if (paragens.Count > LimiteMaximoParagens)
            return ResultadoOtimizacaoRota.Falha(
                $"Selecionou {paragens.Count} paragens, mas o serviço de rotas só aceita até " +
                $"{LimiteMaximoParagens} de cada vez. Reduza a seleção ou divida por mais do que um dia.");

        try
        {
            using var limitador = new SemaphoreSlim(ConcorrenciaMaxima);

            // Calcula, em paralelo (mas limitado), a distância/duração de `partida` a cada um dos
            // `candidatos` (índices em `paragens`) — devolve o índice do candidato mais PERTO e o
            // respetivo resultado, para reaproveitar (sem pedir outra vez) quando esse candidato for
            // mesmo escolhido como próxima paragem.
            async Task<(int Indice, ResultadoDistancia Resultado)> DistanciaParaAsync(CoordenadaGeografica partida, int candidato)
            {
                await limitador.WaitAsync(ct);
                try { return (candidato, await CalcularDistanciaAsync(partida, paragens[candidato], ct)); }
                finally { limitador.Release(); }
            }

            var porVisitar = Enumerable.Range(0, paragens.Count).ToList(); // índices em `paragens`
            var ordem = new List<int>();
            var resultado = new List<ParagemRotaOtimizada>();
            double distanciaTotalKm = 0;
            var duracaoTotalMin = 0;
            var pontoAtual = origem; // começa na sede

            // A cada iteração: pede a distância de `pontoAtual` a TODOS os candidatos ainda por
            // visitar, de uma vez (em paralelo), escolhe o mais perto, e reaproveita esse resultado
            // como o troço da rota — não pede a mesma distância duas vezes.
            while (porVisitar.Count > 0)
            {
                var resultados = await Task.WhenAll(porVisitar.Select(c => DistanciaParaAsync(pontoAtual, c)));

                var falha = resultados.FirstOrDefault(r => !r.Resultado.Sucesso);
                if (falha.Resultado != null)
                    return ResultadoOtimizacaoRota.Falha(falha.Resultado.MensagemErro ?? "Não foi possível calcular um dos troços da rota.");

                // Na 1ª iteração (pontoAtual = sede) escolhe-se o mais LONGE; nas seguintes, o mais
                // PERTO — é a única diferença de critério entre a 1ª paragem e as restantes.
                var escolhido = ordem.Count == 0
                    ? resultados.OrderByDescending(r => r.Resultado.DistanciaKm ?? 0).First()
                    : resultados.OrderBy(r => r.Resultado.DistanciaKm ?? 0).First();

                ordem.Add(escolhido.Indice);
                porVisitar.Remove(escolhido.Indice);

                var distanciaTrocoKm = escolhido.Resultado.DistanciaKm ?? 0;
                var duracaoTrocoMin = escolhido.Resultado.DuracaoMinutos ?? 0;

                resultado.Add(new ParagemRotaOtimizada(
                    IndiceOriginal: escolhido.Indice,
                    DistanciaDesdeAnteriorKm: distanciaTrocoKm,
                    DuracaoDesdeAnteriorMinutos: duracaoTrocoMin));

                distanciaTotalKm += distanciaTrocoKm;
                duracaoTotalMin += duracaoTrocoMin;
                pontoAtual = paragens[escolhido.Indice];
            }

            // Regresso à sede, se aplicável — troço extra no fim, fora do critério de "vizinho mais
            // próximo" (o regresso é sempre para a sede, nunca para outra paragem). Se este pedido
            // extra falhar, não invalida a rota toda: só o total fica sem esse último troço.
            double? distanciaRegressoKm = null;
            int? duracaoRegressoMin = null;
            if (regresso != null)
            {
                var regressoResultado = await CalcularDistanciaAsync(pontoAtual, regresso, ct);
                if (regressoResultado.Sucesso)
                {
                    distanciaRegressoKm = regressoResultado.DistanciaKm ?? 0;
                    duracaoRegressoMin = regressoResultado.DuracaoMinutos ?? 0;
                    distanciaTotalKm += distanciaRegressoKm.Value;
                    duracaoTotalMin += duracaoRegressoMin.Value;
                }
            }

            return ResultadoOtimizacaoRota.Ok(resultado, Math.Round(distanciaTotalKm, 1), duracaoTotalMin, distanciaRegressoKm, duracaoRegressoMin);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ResultadoOtimizacaoRota.Falha("O serviço de rotas demorou demasiado tempo a responder. Tente novamente.");
        }
        catch (HttpRequestException ex)
        {
            return ResultadoOtimizacaoRota.Falha($"Sem ligação ao serviço de rotas: {ex.Message}");
        }
    }

    private const string MensagemChaveEmFalta =
        "O Planeamento de Rotas ainda não está configurado neste computador — falta definir a variável de " +
        "ambiente " + ConfiguracaoRotas.NomeVariavelAmbiente + " com a chave do OpenRouteService. " +
        "Contacte o administrador da aplicação.";

    private static async Task<string> MensagemErroHttp(HttpResponseMessage resposta, CancellationToken ct)
    {
        var corpo = await SafeReadAsync(resposta, ct);
        return (int)resposta.StatusCode switch
        {
            401 or 403 => "A chave de API do serviço de rotas é inválida ou expirou. Contacte o administrador da aplicação.",
            404 => "Não foi encontrada nenhuma rota rodoviária para o percurso pedido.",
            429 => "O limite diário/por minuto de pedidos ao serviço de rotas foi atingido. Tente novamente dentro de alguns minutos.",
            >= 500 => "O serviço de rotas está temporariamente indisponível. Tente novamente mais tarde.",
            _ => $"O serviço de rotas devolveu um erro inesperado ({(int)resposta.StatusCode}).{(string.IsNullOrWhiteSpace(corpo) ? "" : $" Detalhe: {corpo}")}"
        };
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try { return await resposta.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private static readonly JsonSerializerOptions JsonOpcoes = new(JsonSerializerDefaults.Web);

    // ---- DTOs mínimos de resposta da API (só os campos de que a aplicação precisa) ----

    private class GeocodeResponse { [JsonPropertyName("features")] public List<GeocodeFeature>? Features { get; set; } }
    private class GeocodeFeature
    {
        [JsonPropertyName("geometry")] public GeocodeGeometry? Geometry { get; set; }
        [JsonPropertyName("properties")] public GeocodeProperties? Properties { get; set; }
    }
    private class GeocodeGeometry { [JsonPropertyName("coordinates")] public double[]? Coordinates { get; set; } }

    /// <summary>Campos de endereço devolvidos pelo Pelias (o motor de geocodificação usado pelo
    /// OpenRouteService) — só usados na geocodificação inversa (ver
    /// <see cref="GeocodificarInversoAsync"/>); a geocodificação normal (morada → coordenadas) só
    /// precisa de <see cref="GeocodeGeometry"/>.</summary>
    private class GeocodeProperties
    {
        [JsonPropertyName("street")] public string? Street { get; set; }
        [JsonPropertyName("housenumber")] public string? HouseNumber { get; set; }
        [JsonPropertyName("postalcode")] public string? PostalCode { get; set; }
        [JsonPropertyName("locality")] public string? Locality { get; set; }

        /// <summary>Descrição completa e legível (ex.: "Rua X, 2400-000 Leiria, Portugal") — usada
        /// como último recurso quando não há "street" (ex.: coordenada em zona rural, sem rua
        /// mapeada, mas com um nome de lugar).</summary>
        [JsonPropertyName("label")] public string? Label { get; set; }
    }

    private class DirectionsResponse { [JsonPropertyName("routes")] public List<DirectionsRoute>? Routes { get; set; } }
    private class DirectionsRoute { [JsonPropertyName("summary")] public DirectionsSummary? Summary { get; set; } }
    private class DirectionsSummary
    {
        [JsonPropertyName("distance")] public double Distance { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
    }
}
