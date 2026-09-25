using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ResourceBooking.Core.DTOs;

namespace ResourceBooking.IntegrationTests;

public class TodoEndpointsTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private async Task<HttpClient> CreateUserAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com",
            Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "Todo", LastName = "Tester"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.True(auth!.IsSuccess);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    [Theory]
    [InlineData("GET", "/api/todos")]
    [InlineData("GET", "/api/todos?isDone=true")]
    [InlineData("GET", "/api/todos?isDone=false")]
    [InlineData("POST", "/api/todos")]
    [InlineData("PATCH", "/api/todos/1/done")]
    [InlineData("DELETE", "/api/todos/1")]
    public async Task AllOperations_RequireAuthentication(string method, string url)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { title = "Task" });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Lifecycle_FiltersAndDeletesOnlyOwnedItems()
    {
        using var owner = await CreateUserAsync();
        using var other = await CreateUserAsync();
        Assert.Empty((await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos"))!);

        var response = await owner.PostAsJsonAsync("/api/todos", new { title = "  Buy milk  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<TodoResponseDto>())!;
        Assert.Equal("Buy milk", item.Title);
        Assert.False(item.IsDone);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(response.Headers.Location)).StatusCode);
        (await owner.PostAsJsonAsync("/api/todos", new { title = "Read book" })).EnsureSuccessStatusCode();

        foreach (var filter in new[] { "", "?isDone=true", "?isDone=false" })
            Assert.Empty((await other.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos" + filter))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsync($"/api/todos/{item.Id}/done", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/todos/{item.Id}")).StatusCode);
        Assert.Equal(2, (await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos?isDone=false"))!.Count);

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsync($"/api/todos/{item.Id}/done", null)).StatusCode);
        var done = Assert.Single((await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos?isDone=true"))!);
        Assert.Equal(item.Id, done.Id);
        Assert.True(done.IsDone);
        var pending = Assert.Single((await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos?isDone=false"))!);
        Assert.False(pending.IsDone);
        Assert.Equal(2, (await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos"))!.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/todos/{item.Id}")).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos?isDone=true"))!);
        Assert.Single((await owner.GetFromJsonAsync<List<TodoResponseDto>>("/api/todos"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/todos/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PatchAsync($"/api/todos/{item.Id}/done", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync("/api/todos/2147483647")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RejectsMissingOrBlankTitle(string? title)
    {
        using var client = await CreateUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/todos", new { title })).StatusCode);
    }

    [Fact]
    public async Task InvalidTitleLengthAndFilter_ReturnBadRequest()
    {
        using var client = await CreateUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/todos", new { title = new string('a', 251) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/todos?isDone=invalid")).StatusCode);
    }
}
