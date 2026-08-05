public class RecentQuestions
{
    [Function("RecentQuestions")]
    public async Task<HttpResponseData> GetRecentQuestions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recent-questions")]
        HttpRequestData req)
    {
        var user = AuthUserExtractor.GetUser(req);

        var tableClient = _tableServiceClient.GetTableClient("ChatFeedback");

        var questions = new List<string>();

        await foreach (var entity in tableClient.QueryAsync<TableEntity>(
            e => e["Email"].ToString() == user.Email))
        {
            if (entity.TryGetValue("Question", out var question))
            {
                questions.Add(question?.ToString());
            }
        }

        var latest = questions
            .Distinct()
            .TakeLast(10)
            .Reverse()
            .ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new
        {
            queries = latest
        });

        return response;
    }
}
