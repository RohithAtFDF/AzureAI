public class ChatFunction
{
    private const string AgentEndpoint =
        "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

    // Answer model (Mini) — generates the final grounded answer
    private const string AnswerAgentName = "BCFS-Agent";
    private const string AnswerAgentVersion = "5";

    // Router model (Nano) — decides search vs. small talk + rewrites the query
    private const string RouterAgentName = "BCFS-Query-Agent";
    private const string RouterAgentVersion = "2";   // <-- set to your actual version

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")]
        HttpRequestData req)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

        try
        {
            // -----------------------------
            // Read Request
            // -----------------------------
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await response.WriteStringAsync("Error: Request body was empty.");
                return response;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            string question =
                data != null && data.TryGetValue("question", out var q) ? q : "";

            if (string.IsNullOrWhiteSpace(question))
            {
                await response.WriteStringAsync("Error: 'question' was missing or empty.");
                return response;
            }

            // -----------------------------
            // Shared Foundry Project Client
            // -----------------------------
            AIProjectClient projectClient = new(
                endpoint: new Uri(AgentEndpoint),
                tokenProvider: new DefaultAzureCredential()
            );

            // =========================================================
            // STEP 1: ROUTER (Nano) — decide search vs. small talk
            // =========================================================
            var routerClient = projectClient.OpenAI
                .GetProjectResponsesClientForAgent(
                    new AgentReference(RouterAgentName, RouterAgentVersion));

            ResponseResult routerResponse = routerClient.CreateResponse(question);
            string routerRaw = routerResponse.GetOutputText();

            RouterDecision decision = ParseRouterDecision(routerRaw);

            // -----------------------------
            // STEP 1a: SMALL TALK -> reply now (1 call, low latency)
            // -----------------------------
            if (decision != null && !decision.NeedsSearch)
            {
                stopwatch.Stop();

                string reply = string.IsNullOrWhiteSpace(decision.Reply)
                    ? "Hi! How can I help you with BCFS programs today?"
                    : decision.Reply;

                await response.WriteStringAsync(
                    reply +
                    $"\n\n--------------------------------------------------" +
                    $"\nPath: small-talk (no search)" +
                    $"\nExecution Time: {stopwatch.ElapsedMilliseconds} ms");

                return response;
            }

            // Use the cleaned/rewritten query for search; fall back to raw question
            string searchQuery =
                (decision != null && !string.IsNullOrWhiteSpace(decision.SearchQuery))
                    ? decision.SearchQuery
                    : question;

            // =========================================================
            // STEP 2: Azure AI Search (with rewritten query)
            // =========================================================
            string searchEndpoint = "https://cisaisearchservice.search.windows.net";
            string indexName = "bcfs-manual-indexer";
            string? searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY");

            if (string.IsNullOrWhiteSpace(searchKey))
            {
                await response.WriteStringAsync("SEARCH_KEY is missing.");
                return response;
            }

            var searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(searchKey));

            var searchOptions = new SearchOptions
            {
                SearchMode = SearchMode.Any,
                Size = 5
            };

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(searchQuery, searchOptions);

            var results = searchResults.GetResults().ToList();

            // -----------------------------
            // Build Context (may be empty)
            // -----------------------------
            StringBuilder contextBuilder = new();

            foreach (var result in results)
            {
                if (result.Document.TryGetValue("content_text", out var text))
                {
                    contextBuilder.AppendLine(text?.ToString());
                    contextBuilder.AppendLine();
                }
            }

            string context = contextBuilder.ToString();
            bool hasContext = !string.IsNullOrWhiteSpace(context);

            // =========================================================
            // STEP 3: ANSWER (Mini) — grounded answer, empty-context aware
            // =========================================================
string prompt = $@"HANDBOOK CONTEXT:
2
{(hasContext ? context : "No relevant excerpts found.")}
3
 
4
USER QUESTION:
5
{question}";

            var answerClient = projectClient.OpenAI
                .GetProjectResponsesClientForAgent(
                    new AgentReference(AnswerAgentName, AnswerAgentVersion));

            ResponseResult agentResponse = answerClient.CreateResponse(prompt);

            stopwatch.Stop();

            string answer = agentResponse.GetOutputText();

            answer +=
                $"\n\n--------------------------------------------------" +
                $"\nPath: search" +
                $"\nRewritten Query: {searchQuery}" +
                $"\nSearch Results: {results.Count}" +
                $"\nExecution Time: {stopwatch.ElapsedMilliseconds} ms";

            await response.WriteStringAsync(answer);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            response.StatusCode = HttpStatusCode.InternalServerError;

            await response.WriteStringAsync(
                ex.ToString() +
                $"\n\nExecution Time: {stopwatch.ElapsedMilliseconds} ms");

            return response;
        }
    }

    // -----------------------------
    // Router JSON parsing
    // -----------------------------
    private static RouterDecision ParseRouterDecision(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Strip ```json ... ``` fences if the model added them
        string cleaned = raw.Trim();
        if (cleaned.StartsWith("```"))
        {
            int firstBrace = cleaned.IndexOf('{');
            int lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        try
        {
            return JsonSerializer.Deserialize<RouterDecision>(
                cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            // If parsing fails, fall back to searching (safer than dropping the query)
            return new RouterDecision { NeedsSearch = true, SearchQuery = null };
        }
    }

    private class RouterDecision
    {
        [JsonPropertyName("needsSearch")]
        public bool NeedsSearch { get; set; }

        [JsonPropertyName("reply")]
        public string Reply { get; set; }

        [JsonPropertyName("searchQuery")]
        public string SearchQuery { get; set; }
    }
}