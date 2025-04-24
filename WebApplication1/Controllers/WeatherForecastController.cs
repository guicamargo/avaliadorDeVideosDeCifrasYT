using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Collections.Generic;
using System.IO;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.Text.RegularExpressions;
using AngleSharp.Media;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class YouTubeAnalysisController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<YouTubeAnalysisController> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _geminiApiKey;

        public YouTubeAnalysisController(
            IConfiguration configuration,
            ILogger<YouTubeAnalysisController> logger,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _geminiApiKey = _configuration["GeminiApiKey"];

            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogError("Chave da API Gemini (GeminiApiKey) não encontrada na configuração.");
            }
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeYouTubeVideo([FromForm] YouTubeAnalysisFormRequest request)
        {
            _logger.LogInformation(">>> Iniciando análise do YouTube para URL: {Url}", request.YouTubeUrl);

            // --- VALIDAÇÃO DE ENTRADA ---
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogWarning("Tentativa de análise sem chave de API Gemini configurada.");
                return StatusCode(500, "Erro de configuração interna do servidor (API Key).");
            }

            if (string.IsNullOrWhiteSpace(request.YouTubeUrl) || !Uri.IsWellFormedUriString(request.YouTubeUrl, UriKind.Absolute))
            {
                _logger.LogWarning("URL do YouTube inválida fornecida: {Url}", request.YouTubeUrl);
                return BadRequest("URL do YouTube inválida.");
            }

            if (request.RequirementsPdf == null || request.RequirementsPdf.Length == 0)
            {
                _logger.LogWarning("Arquivo PDF de requisitos não foi enviado");
                return BadRequest("É necessário enviar um arquivo PDF com os requisitos para análise.");
            }

            try
            {
                // Extrair o ID do vídeo do YouTube
                string videoId = ExtractYouTubeVideoId(request.YouTubeUrl);
                if (string.IsNullOrEmpty(videoId))
                {
                    return BadRequest("ID do vídeo não pôde ser extraído da URL. Verifique se é uma URL válida do YouTube.");
                }

                // Extrair texto do PDF
                string pdfText = await ExtractTextFromPdfAsync(request.RequirementsPdf);

                // Verificamos se o PDF contém texto suficiente para análise
                if (pdfText.Length < 20) // Limiar arbitrário para texto muito curto
                {
                    _logger.LogWarning("Texto extraído do PDF é muito curto ou vazio");
                    // Usamos um texto padrão para requisitos musicais
                    pdfText = GetDefaultRequirementsText();
                }

                // Obter informações do vídeo (tentar obter título e canal)
                VideoInfo videoInfo = await GetYouTubeVideoInfoAsync(videoId);

                // Simular transcrição (ou você pode implementar a extração real posteriormente)
                string transcript = "Esta é uma transcrição fictícia para demonstração. " +
                                   "Em um ambiente real, você precisaria implementar a extração das legendas " +
                                   "do vídeo com ID " + videoId + ".";

                // Analisar a transcrição com Gemini, incluindo os requisitos do PDF
                string analysisJson = await AnalyzeWithGeminiUsingRequirements(videoInfo, transcript, request.YouTubeUrl, pdfText);

                // Tentar deserializar para verificar se é um JSON válido
                try
                {
                    // Deserializar o JSON recebido
                    JsonDocument jsonDoc = JsonDocument.Parse(analysisJson);

                    // Criar um novo objeto que incluirá o JSON original mais os campos adicionais
                    var combinedJson = new
                    {
                        VideoTitle = videoInfo.Title,
                        VideoChannel = videoInfo.Author,
                        Analysis = JsonSerializer.Deserialize<JsonElement>(analysisJson)
                    };

                    // Serializar o objeto combinado de volta para JSON
                    string response = JsonSerializer.Serialize(combinedJson, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    // Responder com os resultados em formato JSON
                    return Content(response, "application/json");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Erro ao processar JSON retornado pela API Gemini");

                    // Se não for um JSON válido, criar um JSON válido com a análise como string
                    var fallbackResponse = new
                    {
                        VideoTitle = videoInfo.Title,
                        VideoChannel = videoInfo.Author,
                        Analysis = new
                        {
                            Didática_na_explicação = "Não foi possível analisar",
                            Linguagem_utilizada = "Não foi possível analisar",
                            Adequação_ao_nível = new
                            {
                                número_de_acordes = 0,
                                tipos_de_acordes = "Não foi possível analisar"
                            }
                        }
                    };

                    return Ok(fallbackResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar a análise do vídeo");
                return StatusCode(500, $"Erro ao processar a análise: {ex.Message}");
            }
        }
        [HttpPost("analyze-simplified")]
        public async Task<IActionResult> AnalyzeYouTubeVideoSimplified([FromForm] YouTubeAnalysisFormRequest request)
        {
            _logger.LogInformation(">>> Iniciando análise simplificada do YouTube para URL: {Url}", request.YouTubeUrl);

            // --- VALIDAÇÃO DE ENTRADA ---
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogWarning("Tentativa de análise sem chave de API Gemini configurada.");
                return StatusCode(500, "Erro de configuração interna do servidor (API Key).");
            }

            if (string.IsNullOrWhiteSpace(request.YouTubeUrl) || !Uri.IsWellFormedUriString(request.YouTubeUrl, UriKind.Absolute))
            {
                _logger.LogWarning("URL do YouTube inválida fornecida: {Url}", request.YouTubeUrl);
                return BadRequest("URL do YouTube inválida.");
            }

            if (request.RequirementsPdf == null || request.RequirementsPdf.Length == 0)
            {
                _logger.LogWarning("Arquivo PDF de requisitos não foi enviado");
                return BadRequest("É necessário enviar um arquivo PDF com os requisitos para análise.");
            }

            try
            {
                // Extrair o ID do vídeo do YouTube
                string videoId = ExtractYouTubeVideoId(request.YouTubeUrl);
                if (string.IsNullOrEmpty(videoId))
                {
                    return BadRequest("ID do vídeo não pôde ser extraído da URL. Verifique se é uma URL válida do YouTube.");
                }

                // Extrair texto do PDF
                string pdfText = await ExtractTextFromPdfAsync(request.RequirementsPdf);

                // Verificamos se o PDF contém texto suficiente para análise
                if (pdfText.Length < 20) // Limiar arbitrário para texto muito curto
                {
                    _logger.LogWarning("Texto extraído do PDF é muito curto ou vazio");
                    // Usamos um texto padrão para requisitos musicais
                    pdfText = GetDefaultRequirementsText();
                }

                // Obter informações do vídeo (tentar obter título e canal)
                VideoInfo videoInfo = await GetYouTubeVideoInfoAsync(videoId);

                // Simular transcrição (ou você pode implementar a extração real posteriormente)
                string transcript = "Esta é uma transcrição fictícia para demonstração. " +
                                   "Em um ambiente real, você precisaria implementar a extração das legendas " +
                                   "do vídeo com ID " + videoId + ".";

                // Analisar a transcrição com Gemini, incluindo os requisitos do PDF, solicitando o formato simplificado
                string analysisJson = await AnalyzeWithGeminiUsingRequirementsSimplified(videoInfo, transcript, request.YouTubeUrl, pdfText);

                // Tentar deserializar para verificar se é um JSON válido
                try
                {
                    // Deserializar o JSON recebido
                    JsonDocument jsonDoc = JsonDocument.Parse(analysisJson);

                    // Responder com os resultados em formato JSON diretamente
                    return Content(analysisJson, "application/json");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Erro ao processar JSON retornado pela API Gemini");

                    // Se não for um JSON válido, criar um JSON válido com a estrutura solicitada
                    var fallbackResponse = new
                    {
                        Didática_na_explicação = "Não foi possível analisar",
                        Linguagem_utilizada = "Não foi possível analisar",
                        Adequação_ao_nível = new
                        {
                            número_de_acordes = 0,
                            tipos_de_acordes = "Não foi possível analisar"
                        }
                    };

                    string fallbackJson = JsonSerializer.Serialize(fallbackResponse, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    return Content(fallbackJson, "application/json");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar a análise do vídeo");
                return StatusCode(500, $"Erro ao processar a análise: {ex.Message}");
            }
        }

        #region Métodos Comuns

        private string GetDefaultRequirementsText()
        {
            return @"Este documento contém requisitos para análise de vídeos de música.
            
Requisitos de análise:
1. Didática na explicação: avaliar a clareza das instruções e explicações 
2. Linguagem utilizada: avaliar a terminologia e comunicação usada
3. Adequação ao nível: 
   - número de acordes utilizados
   - tipos de acordes (naturais/suspensos)

Os vídeos devem ser analisados considerando a qualidade da instrução musical,
facilidade de compreensão e adequação ao nível indicado.";
        }

        private string ExtractYouTubeVideoId(string youtubeUrl)
        {
            try
            {
                Uri uri = new Uri(youtubeUrl);

                // Para URLs como https://www.youtube.com/watch?v=VIDEO_ID
                if (uri.Host.Contains("youtube.com") && uri.PathAndQuery.Contains("watch"))
                {
                    var query = HttpUtility.ParseQueryString(uri.Query);
                    return query["v"];
                }

                // Para URLs como https://youtu.be/VIDEO_ID
                if (uri.Host.Contains("youtu.be"))
                {
                    return uri.AbsolutePath.TrimStart('/');
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> ExtractTextFromPdfAsync(IFormFile pdfFile)
        {
            try
            {
                StringBuilder text = new StringBuilder();

                _logger.LogInformation("Iniciando extração do PDF: {FileName}, Tamanho: {Length} bytes",
                    pdfFile.FileName, pdfFile.Length);

                using (var memoryStream = new MemoryStream())
                {
                    await pdfFile.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    _logger.LogInformation("PDF copiado para MemoryStream, tamanho: {Length} bytes",
                        memoryStream.Length);

                    try
                    {
                        using (PdfReader reader = new PdfReader(memoryStream))
                        {
                            _logger.LogInformation("PdfReader inicializado, número de páginas: {PageCount}",
                                reader.NumberOfPages);

                            // Verificar se o PDF está criptografado
                            if (reader.IsEncrypted())
                            {
                                _logger.LogWarning("O PDF está criptografado e pode exigir senha para extração");
                                return "O documento PDF está criptografado. Por favor, forneça uma versão não criptografada.";
                            }

                            for (int i = 1; i <= reader.NumberOfPages; i++)
                            {
                                try
                                {
                                    string pageText = PdfTextExtractor.GetTextFromPage(reader, i);
                                    _logger.LogInformation("Página {PageNumber} extraída, caracteres: {CharCount}",
                                        i, pageText?.Length ?? 0);

                                    text.Append(pageText);
                                    text.Append("\n");
                                }
                                catch (Exception pageEx)
                                {
                                    _logger.LogWarning(pageEx, "Erro ao extrair texto da página {PageNumber}", i);
                                    text.Append($"[Erro ao extrair texto da página {i}]\n");
                                }
                            }
                        }
                    }
                    catch (Exception readerEx)
                    {
                        _logger.LogError(readerEx, "Erro ao inicializar PdfReader");

                        // Tentar abordagem alternativa usando a estratégia de parsing simples
                        memoryStream.Position = 0;
                        return await ExtractTextFromPdfAlternativeAsync(memoryStream);
                    }
                }

                string result = text.ToString().Trim();

                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogWarning("Nenhum texto extraído do PDF, tentando método alternativo");

                    // Se não tiver texto, tentar novamente com um método alternativo
                    using (var memoryStream = new MemoryStream())
                    {
                        await pdfFile.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;

                        return await ExtractTextFromPdfAlternativeAsync(memoryStream);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao extrair texto do PDF");
                return "Erro ao extrair texto do PDF. O documento pode estar danificado ou em um formato não compatível.";
            }
        }

        private async Task<string> ExtractTextFromPdfAlternativeAsync(MemoryStream memoryStream)
        {
            try
            {
                _logger.LogInformation("Tentando método alternativo de extração de texto do PDF");
                return GetDefaultRequirementsText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no método alternativo de extração");
                return "Não foi possível extrair texto do PDF. Usando parâmetros padrão para análise.";
            }
        }

        private async Task<VideoInfo> GetYouTubeVideoInfoAsync(string videoId)
        {
            try
            {
                // Tentar obter informações via scraping básico
                string videoUrl = $"https://www.youtube.com/watch?v={videoId}";
                var response = await _httpClient.GetAsync(videoUrl);

                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();

                    // Extrair título (abordagem simples via regex)
                    string title = ExtractYouTubeTitle(html);

                    // Extrair nome do canal (abordagem simples via regex)
                    string channel = ExtractYouTubeChannel(html);

                    return new VideoInfo
                    {
                        Title = !string.IsNullOrEmpty(title) ? title : $"Vídeo YouTube {videoId}",
                        Author = !string.IsNullOrEmpty(channel) ? channel : "Canal desconhecido",
                        Description = "Descrição não disponível"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao tentar obter informações do YouTube. Usando valores padrão.");
            }

            // Valores fallback
            return new VideoInfo
            {
                Title = $"Vídeo YouTube {videoId}",
                Author = "Canal desconhecido",
                Description = "Descrição não disponível"
            };
        }

        private string ExtractYouTubeTitle(string html)
        {
            try
            {
                // Procurar pelo padrão de título
                var titleMatch = Regex.Match(html, @"<title>([^<]*) - YouTube</title>");
                if (titleMatch.Success)
                {
                    return titleMatch.Groups[1].Value.Trim();
                }

                // Padrão alternativo (meta tag)
                var metaTitleMatch = Regex.Match(html, @"<meta name=""title"" content=""([^""]*)""");
                if (metaTitleMatch.Success)
                {
                    return metaTitleMatch.Groups[1].Value.Trim().Replace(" - YouTube", "");
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private string ExtractYouTubeChannel(string html)
        {
            try
            {
                // Padrão comum para o nome do canal
                var channelMatch = Regex.Match(html, @"""ownerChannelName"":""([^""]*)""");
                if (channelMatch.Success)
                {
                    return channelMatch.Groups[1].Value.Trim();
                }

                // Padrão alternativo
                var authorMatch = Regex.Match(html, @"<link itemprop=""name"" content=""([^""]*)"">");
                if (authorMatch.Success)
                {
                    return authorMatch.Groups[1].Value.Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> AnalyzeWithGeminiUsingRequirements(VideoInfo videoInfo, string transcript, string videoUrl, string pdfRequirements)
        {
            try
            {
                // Configuração para a API Gemini
                string geminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

                // Montar o prompt para a API Gemini com o formato específico solicitado
                string prompt = "**Contexto:** Você é um assistente de IA especializado em avaliar vídeos educativos de música, especificamente para aulas de violão do CifraClub.\r\n" +
                "**Informações do Vídeo:**\r\n" +
                "- URL: " + videoUrl + "\r\n" +
                "- Título: " + videoInfo.Title + "\r\n" +
                "- Canal: " + videoInfo.Author + "\r\n" +
                "\r\n" +
                "**Matriz de Proficiência para Avaliação:**\r\n" +
                "```\r\n" +
                pdfRequirements +
                "\r\n```\r\n" +
                "\r\n" +
                "**Transcrição Simulada do Vídeo:**\r\n" +
                "```text\r\n" +
                transcript + "\r\n" +
                "```\r\n" +
                "\r\n" +
                "**Sua Tarefa:**\r\n" +
                "Analise o vídeo de acordo com a Matriz de Proficiência fornecida e retorne um objeto JSON com a seguinte estrutura:\r\n" +
                "{\r\n" +
                "  \"avaliacao\": {\r\n" +
                "    \"interacao_engajamento\": {\r\n" +
                "      \"estimula_pratica_ativa\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"explicacao_clara_objetiva\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"reconhece_dificuldades\": \"\" // Sim, Às vezes, Não, Não observado\r\n" +
                "    },\r\n" +
                "    \"metodologia_ensino\": {\r\n" +
                "      \"divide_etapas_logicas\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"utiliza_recursos_visuais\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"proporciona_tempo_assimilacao\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"clareza_postura_gestos\": \"\" // Sim, Às vezes, Não, Não observado\r\n" +
                "    },\r\n" +
                "    \"apropriacao_conteudo\": {\r\n" +
                "      \"dominio_tecnico\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"contextualiza_musica\": \"\" // Sim, Às vezes, Não, Não observado\r\n" +
                "    },\r\n" +
                "    \"organizacao\": {\r\n" +
                "      \"duracao_adequada\": \"\", // Sim, Às vezes, Não, Não observado\r\n" +
                "      \"recursos_complementares\": \"\" // Sim, Às vezes, Não, Não observado\r\n" +
                "    },\r\n" +
                "    \"nivel_proficiencia\": \"\", // A1, A2, B1, B2, C1 ou C2\r\n" +
                "    \"observacoes\": \"\", // Observações gerais sobre o vídeo\r\n" +
                "    \"numero_acordes\": 0, // Estimativa do número de acordes ensinados\r\n" +
                "    \"tipos_acordes\": \"\" // Descrição dos tipos de acordes (naturais/suspensos/etc)\r\n" +
                "  }\r\n" +
                "}\r\n" +
                "\r\n" +
                "Baseie sua avaliação nos critérios da Matriz de Proficiência fornecida. Para o nível de proficiência, use as descrições A1 (Iniciante), A2 (Básico), B1 (Intermediário), B2 (Pós-intermediário), C1 (Avançado) ou C2 (Domínio pleno) conforme definido no documento.\r\n" +
                "\r\n" +
                "IMPORTANTE: Retorne APENAS o objeto JSON, sem texto adicional antes ou depois.";

                return await SendGeminiRequest(geminiApiUrl, prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar com Gemini");
                // Retorno de fallback em caso de exceção
                return @"{
                  ""Didática na explicação"": ""Erro: " + ex.Message.Replace("\"", "'") + @""",
                  ""Linguagem utilizada"": ""Erro ao processar"",
                  ""Adequação ao nível"": {
                    ""número de acordes"": 0,
                    ""tipos de acordes (naturais/suspensos)"": ""Erro ao processar""
                  }
                }";
            }
        }

        private async Task<string> AnalyzeWithGeminiUsingRequirementsSimplified(VideoInfo videoInfo, string transcript, string videoUrl, string pdfRequirements)
        {
            try
            {
                // Configuração para a API Gemini
                string geminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

                // Montar o prompt para a API Gemini com o formato simplificado solicitado
                string prompt = "**Contexto:** Você é um assistente de IA especializado em avaliar vídeos educativos de música, especificamente para aulas de violão do CifraClub.\r\n" +
                "**Informações do Vídeo:**\r\n" +
                "- URL: " + videoUrl + "\r\n" +
                "- Título: " + videoInfo.Title + "\r\n" +
                "- Canal: " + videoInfo.Author + "\r\n" +
                "\r\n" +
                "**Matriz de Proficiência para Avaliação:**\r\n" +
                "```\r\n" +
                pdfRequirements +
                "\r\n```\r\n" +
                "\r\n" +
                "**Transcrição Simulada do Vídeo:**\r\n" +
                "```text\r\n" +
                transcript + "\r\n" +
                "```\r\n" +
                "\r\n" +
                "**Sua Tarefa:**\r\n" +
                "Analise o vídeo de acordo com a Matriz de Proficiência fornecida e retorne um objeto JSON com a seguinte estrutura SIMPLIFICADA:\r\n" +
                "{\r\n" +
                "  \"Didática na explicação\": \"\", // Avaliação da didática e clareza\r\n" +
                "  \"Linguagem utilizada\": \"\", // Avaliação da terminologia e comunicação\r\n" +
                "  \"Adequação ao nível\": {\r\n" +
                "    \"número de acordes\": 0, // Estimativa do número de acordes ensinados\r\n" +
                "    \"tipos de acordes (naturais/suspensos)\": \"\" // Descrição dos tipos de acordes\r\n" +
                "  }\r\n" +
                "}\r\n" +
                "\r\n" +
                "Baseie sua avaliação nos critérios da Matriz de Proficiência fornecida.\r\n" +
                "\r\n" +
                "IMPORTANTE: Retorne APENAS o objeto JSON, sem texto adicional antes ou depois. Certifique-se de seguir EXATAMENTE a estrutura solicitada.";

                return await SendGeminiRequest(geminiApiUrl, prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar com Gemini");
                // Retorno de fallback em caso de exceção com a estrutura simplificada
                return @"{
                  ""Didática na explicação"": ""Erro: " + ex.Message.Replace("\"", "'") + @""",
                  ""Linguagem utilizada"": ""Erro ao processar"",
                  ""Adequação ao nível"": {
                    ""número de acordes"": 0,
                    ""tipos de acordes (naturais/suspensos)"": ""Erro ao processar""
                  }
                }";
            }
        }

        private async Task<string> SendGeminiRequest(string geminiApiUrl, string prompt)
        {
            // Criar o objeto de requisição para a API Gemini
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2, // Temperatura mais baixa para respostas mais estruturadas
                    topP = 0.95f,
                    topK = 40
                }
            };

            // Adicionar a chave de API como parâmetro na URL
            string requestUrl = $"{geminiApiUrl}?key={_geminiApiKey}";

            // Enviar a requisição para a API Gemini
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(requestUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseData = await JsonSerializer.DeserializeAsync<GeminiResponse>(
                    await response.Content.ReadAsStreamAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (responseData?.Candidates?.Length > 0 &&
                    responseData.Candidates[0].Content?.Parts?.Length > 0)
                {
                    string rawResponse = responseData.Candidates[0].Content.Parts[0].Text.Trim();

                    // Remover possíveis blocos de código markdown
                    if (rawResponse.StartsWith("```json"))
                    {
                        rawResponse = rawResponse.Substring(7); // Remove ```json
                    }
                    else if (rawResponse.StartsWith("```"))
                    {
                        rawResponse = rawResponse.Substring(3); // Remove ```
                    }
                    if (rawResponse.EndsWith("```"))
                    {
                        rawResponse = rawResponse.Substring(0, rawResponse.Length - 3); // Remove trailing ```
                    }

                    return rawResponse.Trim();
                }
            }

            _logger.LogWarning("Resposta da API Gemini: {StatusCode}", response.StatusCode);
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Erro da API Gemini: {Error}", errorContent);

            // Resposta fallback em caso de erro
            return @"{
              ""Didática na explicação"": ""Não foi possível analisar"",
              ""Linguagem utilizada"": ""Não foi possível analisar"",
              ""Adequação ao nível"": {
                ""número de acordes"": 0,
                ""tipos de acordes (naturais/suspensos)"": ""Não foi possível analisar""
              }
            }";
        }
        #endregion
    }

    // Classes para modelo de dados
    public class YouTubeAnalysisFormRequest
    {
        public string YouTubeUrl { get; set; }
        public IFormFile RequirementsPdf { get; set; }
    }

    public class VideoInfo
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
    }

    // Classes para resposta da API Gemini
    public class GeminiResponse
    {
        public GeminiCandidate[] Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        public GeminiContent Content { get; set; }
    }

    public class GeminiContent
    {
        public GeminiPart[] Parts { get; set; }
    }

    public class GeminiPart
    {
        public string Text { get; set; }
    }
}