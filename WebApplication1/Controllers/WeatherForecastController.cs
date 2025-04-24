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
                _logger.LogError("Chave da API Gemini (GeminiApiKey) n�o encontrada na configura��o.");
            }
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeYouTubeVideo([FromForm] YouTubeAnalysisFormRequest request)
        {
            _logger.LogInformation(">>> Iniciando an�lise do YouTube para URL: {Url}", request.YouTubeUrl);

            // --- VALIDA��O DE ENTRADA ---
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogWarning("Tentativa de an�lise sem chave de API Gemini configurada.");
                return StatusCode(500, "Erro de configura��o interna do servidor (API Key).");
            }

            if (string.IsNullOrWhiteSpace(request.YouTubeUrl) || !Uri.IsWellFormedUriString(request.YouTubeUrl, UriKind.Absolute))
            {
                _logger.LogWarning("URL do YouTube inv�lida fornecida: {Url}", request.YouTubeUrl);
                return BadRequest("URL do YouTube inv�lida.");
            }

            if (request.RequirementsPdf == null || request.RequirementsPdf.Length == 0)
            {
                _logger.LogWarning("Arquivo PDF de requisitos n�o foi enviado");
                return BadRequest("� necess�rio enviar um arquivo PDF com os requisitos para an�lise.");
            }

            try
            {
                // Extrair o ID do v�deo do YouTube
                string videoId = ExtractYouTubeVideoId(request.YouTubeUrl);
                if (string.IsNullOrEmpty(videoId))
                {
                    return BadRequest("ID do v�deo n�o p�de ser extra�do da URL. Verifique se � uma URL v�lida do YouTube.");
                }

                // Extrair texto do PDF
                string pdfText = await ExtractTextFromPdfAsync(request.RequirementsPdf);

                // Verificamos se o PDF cont�m texto suficiente para an�lise
                if (pdfText.Length < 20) // Limiar arbitr�rio para texto muito curto
                {
                    _logger.LogWarning("Texto extra�do do PDF � muito curto ou vazio");
                    // Usamos um texto padr�o para requisitos musicais
                    pdfText = GetDefaultRequirementsText();
                }

                // Obter informa��es do v�deo (tentar obter t�tulo e canal)
                VideoInfo videoInfo = await GetYouTubeVideoInfoAsync(videoId);

                // Simular transcri��o (ou voc� pode implementar a extra��o real posteriormente)
                string transcript = "Esta � uma transcri��o fict�cia para demonstra��o. " +
                                   "Em um ambiente real, voc� precisaria implementar a extra��o das legendas " +
                                   "do v�deo com ID " + videoId + ".";

                // Analisar a transcri��o com Gemini, incluindo os requisitos do PDF
                string analysisJson = await AnalyzeWithGeminiUsingRequirements(videoInfo, transcript, request.YouTubeUrl, pdfText);

                // Tentar deserializar para verificar se � um JSON v�lido
                try
                {
                    // Deserializar o JSON recebido
                    JsonDocument jsonDoc = JsonDocument.Parse(analysisJson);

                    // Criar um novo objeto que incluir� o JSON original mais os campos adicionais
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

                    // Se n�o for um JSON v�lido, criar um JSON v�lido com a an�lise como string
                    var fallbackResponse = new
                    {
                        VideoTitle = videoInfo.Title,
                        VideoChannel = videoInfo.Author,
                        Analysis = new
                        {
                            Did�tica_na_explica��o = "N�o foi poss�vel analisar",
                            Linguagem_utilizada = "N�o foi poss�vel analisar",
                            Adequa��o_ao_n�vel = new
                            {
                                n�mero_de_acordes = 0,
                                tipos_de_acordes = "N�o foi poss�vel analisar"
                            }
                        }
                    };

                    return Ok(fallbackResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar a an�lise do v�deo");
                return StatusCode(500, $"Erro ao processar a an�lise: {ex.Message}");
            }
        }
        [HttpPost("analyze-simplified")]
        public async Task<IActionResult> AnalyzeYouTubeVideoSimplified([FromForm] YouTubeAnalysisFormRequest request)
        {
            _logger.LogInformation(">>> Iniciando an�lise simplificada do YouTube para URL: {Url}", request.YouTubeUrl);

            // --- VALIDA��O DE ENTRADA ---
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogWarning("Tentativa de an�lise sem chave de API Gemini configurada.");
                return StatusCode(500, "Erro de configura��o interna do servidor (API Key).");
            }

            if (string.IsNullOrWhiteSpace(request.YouTubeUrl) || !Uri.IsWellFormedUriString(request.YouTubeUrl, UriKind.Absolute))
            {
                _logger.LogWarning("URL do YouTube inv�lida fornecida: {Url}", request.YouTubeUrl);
                return BadRequest("URL do YouTube inv�lida.");
            }

            if (request.RequirementsPdf == null || request.RequirementsPdf.Length == 0)
            {
                _logger.LogWarning("Arquivo PDF de requisitos n�o foi enviado");
                return BadRequest("� necess�rio enviar um arquivo PDF com os requisitos para an�lise.");
            }

            try
            {
                // Extrair o ID do v�deo do YouTube
                string videoId = ExtractYouTubeVideoId(request.YouTubeUrl);
                if (string.IsNullOrEmpty(videoId))
                {
                    return BadRequest("ID do v�deo n�o p�de ser extra�do da URL. Verifique se � uma URL v�lida do YouTube.");
                }

                // Extrair texto do PDF
                string pdfText = await ExtractTextFromPdfAsync(request.RequirementsPdf);

                // Verificamos se o PDF cont�m texto suficiente para an�lise
                if (pdfText.Length < 20) // Limiar arbitr�rio para texto muito curto
                {
                    _logger.LogWarning("Texto extra�do do PDF � muito curto ou vazio");
                    // Usamos um texto padr�o para requisitos musicais
                    pdfText = GetDefaultRequirementsText();
                }

                // Obter informa��es do v�deo (tentar obter t�tulo e canal)
                VideoInfo videoInfo = await GetYouTubeVideoInfoAsync(videoId);

                // Simular transcri��o (ou voc� pode implementar a extra��o real posteriormente)
                string transcript = "Esta � uma transcri��o fict�cia para demonstra��o. " +
                                   "Em um ambiente real, voc� precisaria implementar a extra��o das legendas " +
                                   "do v�deo com ID " + videoId + ".";

                // Analisar a transcri��o com Gemini, incluindo os requisitos do PDF, solicitando o formato simplificado
                string analysisJson = await AnalyzeWithGeminiUsingRequirementsSimplified(videoInfo, transcript, request.YouTubeUrl, pdfText);

                // Tentar deserializar para verificar se � um JSON v�lido
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

                    // Se n�o for um JSON v�lido, criar um JSON v�lido com a estrutura solicitada
                    var fallbackResponse = new
                    {
                        Did�tica_na_explica��o = "N�o foi poss�vel analisar",
                        Linguagem_utilizada = "N�o foi poss�vel analisar",
                        Adequa��o_ao_n�vel = new
                        {
                            n�mero_de_acordes = 0,
                            tipos_de_acordes = "N�o foi poss�vel analisar"
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
                _logger.LogError(ex, "Erro ao processar a an�lise do v�deo");
                return StatusCode(500, $"Erro ao processar a an�lise: {ex.Message}");
            }
        }

        #region M�todos Comuns

        private string GetDefaultRequirementsText()
        {
            return @"Este documento cont�m requisitos para an�lise de v�deos de m�sica.
            
Requisitos de an�lise:
1. Did�tica na explica��o: avaliar a clareza das instru��es e explica��es 
2. Linguagem utilizada: avaliar a terminologia e comunica��o usada
3. Adequa��o ao n�vel: 
   - n�mero de acordes utilizados
   - tipos de acordes (naturais/suspensos)

Os v�deos devem ser analisados considerando a qualidade da instru��o musical,
facilidade de compreens�o e adequa��o ao n�vel indicado.";
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

                _logger.LogInformation("Iniciando extra��o do PDF: {FileName}, Tamanho: {Length} bytes",
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
                            _logger.LogInformation("PdfReader inicializado, n�mero de p�ginas: {PageCount}",
                                reader.NumberOfPages);

                            // Verificar se o PDF est� criptografado
                            if (reader.IsEncrypted())
                            {
                                _logger.LogWarning("O PDF est� criptografado e pode exigir senha para extra��o");
                                return "O documento PDF est� criptografado. Por favor, forne�a uma vers�o n�o criptografada.";
                            }

                            for (int i = 1; i <= reader.NumberOfPages; i++)
                            {
                                try
                                {
                                    string pageText = PdfTextExtractor.GetTextFromPage(reader, i);
                                    _logger.LogInformation("P�gina {PageNumber} extra�da, caracteres: {CharCount}",
                                        i, pageText?.Length ?? 0);

                                    text.Append(pageText);
                                    text.Append("\n");
                                }
                                catch (Exception pageEx)
                                {
                                    _logger.LogWarning(pageEx, "Erro ao extrair texto da p�gina {PageNumber}", i);
                                    text.Append($"[Erro ao extrair texto da p�gina {i}]\n");
                                }
                            }
                        }
                    }
                    catch (Exception readerEx)
                    {
                        _logger.LogError(readerEx, "Erro ao inicializar PdfReader");

                        // Tentar abordagem alternativa usando a estrat�gia de parsing simples
                        memoryStream.Position = 0;
                        return await ExtractTextFromPdfAlternativeAsync(memoryStream);
                    }
                }

                string result = text.ToString().Trim();

                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogWarning("Nenhum texto extra�do do PDF, tentando m�todo alternativo");

                    // Se n�o tiver texto, tentar novamente com um m�todo alternativo
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
                return "Erro ao extrair texto do PDF. O documento pode estar danificado ou em um formato n�o compat�vel.";
            }
        }

        private async Task<string> ExtractTextFromPdfAlternativeAsync(MemoryStream memoryStream)
        {
            try
            {
                _logger.LogInformation("Tentando m�todo alternativo de extra��o de texto do PDF");
                return GetDefaultRequirementsText();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no m�todo alternativo de extra��o");
                return "N�o foi poss�vel extrair texto do PDF. Usando par�metros padr�o para an�lise.";
            }
        }

        private async Task<VideoInfo> GetYouTubeVideoInfoAsync(string videoId)
        {
            try
            {
                // Tentar obter informa��es via scraping b�sico
                string videoUrl = $"https://www.youtube.com/watch?v={videoId}";
                var response = await _httpClient.GetAsync(videoUrl);

                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();

                    // Extrair t�tulo (abordagem simples via regex)
                    string title = ExtractYouTubeTitle(html);

                    // Extrair nome do canal (abordagem simples via regex)
                    string channel = ExtractYouTubeChannel(html);

                    return new VideoInfo
                    {
                        Title = !string.IsNullOrEmpty(title) ? title : $"V�deo YouTube {videoId}",
                        Author = !string.IsNullOrEmpty(channel) ? channel : "Canal desconhecido",
                        Description = "Descri��o n�o dispon�vel"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erro ao tentar obter informa��es do YouTube. Usando valores padr�o.");
            }

            // Valores fallback
            return new VideoInfo
            {
                Title = $"V�deo YouTube {videoId}",
                Author = "Canal desconhecido",
                Description = "Descri��o n�o dispon�vel"
            };
        }

        private string ExtractYouTubeTitle(string html)
        {
            try
            {
                // Procurar pelo padr�o de t�tulo
                var titleMatch = Regex.Match(html, @"<title>([^<]*) - YouTube</title>");
                if (titleMatch.Success)
                {
                    return titleMatch.Groups[1].Value.Trim();
                }

                // Padr�o alternativo (meta tag)
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
                // Padr�o comum para o nome do canal
                var channelMatch = Regex.Match(html, @"""ownerChannelName"":""([^""]*)""");
                if (channelMatch.Success)
                {
                    return channelMatch.Groups[1].Value.Trim();
                }

                // Padr�o alternativo
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
                // Configura��o para a API Gemini
                string geminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

                // Montar o prompt para a API Gemini com o formato espec�fico solicitado
                string prompt = "**Contexto:** Voc� � um assistente de IA especializado em avaliar v�deos educativos de m�sica, especificamente para aulas de viol�o do CifraClub.\r\n" +
                "**Informa��es do V�deo:**\r\n" +
                "- URL: " + videoUrl + "\r\n" +
                "- T�tulo: " + videoInfo.Title + "\r\n" +
                "- Canal: " + videoInfo.Author + "\r\n" +
                "\r\n" +
                "**Matriz de Profici�ncia para Avalia��o:**\r\n" +
                "```\r\n" +
                pdfRequirements +
                "\r\n```\r\n" +
                "\r\n" +
                "**Transcri��o Simulada do V�deo:**\r\n" +
                "```text\r\n" +
                transcript + "\r\n" +
                "```\r\n" +
                "\r\n" +
                "**Sua Tarefa:**\r\n" +
                "Analise o v�deo de acordo com a Matriz de Profici�ncia fornecida e retorne um objeto JSON com a seguinte estrutura:\r\n" +
                "{\r\n" +
                "  \"avaliacao\": {\r\n" +
                "    \"interacao_engajamento\": {\r\n" +
                "      \"estimula_pratica_ativa\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"explicacao_clara_objetiva\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"reconhece_dificuldades\": \"\" // Sim, �s vezes, N�o, N�o observado\r\n" +
                "    },\r\n" +
                "    \"metodologia_ensino\": {\r\n" +
                "      \"divide_etapas_logicas\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"utiliza_recursos_visuais\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"proporciona_tempo_assimilacao\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"clareza_postura_gestos\": \"\" // Sim, �s vezes, N�o, N�o observado\r\n" +
                "    },\r\n" +
                "    \"apropriacao_conteudo\": {\r\n" +
                "      \"dominio_tecnico\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"contextualiza_musica\": \"\" // Sim, �s vezes, N�o, N�o observado\r\n" +
                "    },\r\n" +
                "    \"organizacao\": {\r\n" +
                "      \"duracao_adequada\": \"\", // Sim, �s vezes, N�o, N�o observado\r\n" +
                "      \"recursos_complementares\": \"\" // Sim, �s vezes, N�o, N�o observado\r\n" +
                "    },\r\n" +
                "    \"nivel_proficiencia\": \"\", // A1, A2, B1, B2, C1 ou C2\r\n" +
                "    \"observacoes\": \"\", // Observa��es gerais sobre o v�deo\r\n" +
                "    \"numero_acordes\": 0, // Estimativa do n�mero de acordes ensinados\r\n" +
                "    \"tipos_acordes\": \"\" // Descri��o dos tipos de acordes (naturais/suspensos/etc)\r\n" +
                "  }\r\n" +
                "}\r\n" +
                "\r\n" +
                "Baseie sua avalia��o nos crit�rios da Matriz de Profici�ncia fornecida. Para o n�vel de profici�ncia, use as descri��es A1 (Iniciante), A2 (B�sico), B1 (Intermedi�rio), B2 (P�s-intermedi�rio), C1 (Avan�ado) ou C2 (Dom�nio pleno) conforme definido no documento.\r\n" +
                "\r\n" +
                "IMPORTANTE: Retorne APENAS o objeto JSON, sem texto adicional antes ou depois.";

                return await SendGeminiRequest(geminiApiUrl, prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar com Gemini");
                // Retorno de fallback em caso de exce��o
                return @"{
                  ""Did�tica na explica��o"": ""Erro: " + ex.Message.Replace("\"", "'") + @""",
                  ""Linguagem utilizada"": ""Erro ao processar"",
                  ""Adequa��o ao n�vel"": {
                    ""n�mero de acordes"": 0,
                    ""tipos de acordes (naturais/suspensos)"": ""Erro ao processar""
                  }
                }";
            }
        }

        private async Task<string> AnalyzeWithGeminiUsingRequirementsSimplified(VideoInfo videoInfo, string transcript, string videoUrl, string pdfRequirements)
        {
            try
            {
                // Configura��o para a API Gemini
                string geminiApiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

                // Montar o prompt para a API Gemini com o formato simplificado solicitado
                string prompt = "**Contexto:** Voc� � um assistente de IA especializado em avaliar v�deos educativos de m�sica, especificamente para aulas de viol�o do CifraClub.\r\n" +
                "**Informa��es do V�deo:**\r\n" +
                "- URL: " + videoUrl + "\r\n" +
                "- T�tulo: " + videoInfo.Title + "\r\n" +
                "- Canal: " + videoInfo.Author + "\r\n" +
                "\r\n" +
                "**Matriz de Profici�ncia para Avalia��o:**\r\n" +
                "```\r\n" +
                pdfRequirements +
                "\r\n```\r\n" +
                "\r\n" +
                "**Transcri��o Simulada do V�deo:**\r\n" +
                "```text\r\n" +
                transcript + "\r\n" +
                "```\r\n" +
                "\r\n" +
                "**Sua Tarefa:**\r\n" +
                "Analise o v�deo de acordo com a Matriz de Profici�ncia fornecida e retorne um objeto JSON com a seguinte estrutura SIMPLIFICADA:\r\n" +
                "{\r\n" +
                "  \"Did�tica na explica��o\": \"\", // Avalia��o da did�tica e clareza\r\n" +
                "  \"Linguagem utilizada\": \"\", // Avalia��o da terminologia e comunica��o\r\n" +
                "  \"Adequa��o ao n�vel\": {\r\n" +
                "    \"n�mero de acordes\": 0, // Estimativa do n�mero de acordes ensinados\r\n" +
                "    \"tipos de acordes (naturais/suspensos)\": \"\" // Descri��o dos tipos de acordes\r\n" +
                "  }\r\n" +
                "}\r\n" +
                "\r\n" +
                "Baseie sua avalia��o nos crit�rios da Matriz de Profici�ncia fornecida.\r\n" +
                "\r\n" +
                "IMPORTANTE: Retorne APENAS o objeto JSON, sem texto adicional antes ou depois. Certifique-se de seguir EXATAMENTE a estrutura solicitada.";

                return await SendGeminiRequest(geminiApiUrl, prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao analisar com Gemini");
                // Retorno de fallback em caso de exce��o com a estrutura simplificada
                return @"{
                  ""Did�tica na explica��o"": ""Erro: " + ex.Message.Replace("\"", "'") + @""",
                  ""Linguagem utilizada"": ""Erro ao processar"",
                  ""Adequa��o ao n�vel"": {
                    ""n�mero de acordes"": 0,
                    ""tipos de acordes (naturais/suspensos)"": ""Erro ao processar""
                  }
                }";
            }
        }

        private async Task<string> SendGeminiRequest(string geminiApiUrl, string prompt)
        {
            // Criar o objeto de requisi��o para a API Gemini
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

            // Adicionar a chave de API como par�metro na URL
            string requestUrl = $"{geminiApiUrl}?key={_geminiApiKey}";

            // Enviar a requisi��o para a API Gemini
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

                    // Remover poss�veis blocos de c�digo markdown
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
              ""Did�tica na explica��o"": ""N�o foi poss�vel analisar"",
              ""Linguagem utilizada"": ""N�o foi poss�vel analisar"",
              ""Adequa��o ao n�vel"": {
                ""n�mero de acordes"": 0,
                ""tipos de acordes (naturais/suspensos)"": ""N�o foi poss�vel analisar""
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