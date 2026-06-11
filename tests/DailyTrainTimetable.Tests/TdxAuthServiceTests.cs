using DailyTrainTimetable.Services;
using NSubstitute;
using Xunit;

namespace DailyTrainTimetable.Tests;

public sealed class TdxAuthServiceTests
{
    private readonly ITdxAuthService _sut;

    public TdxAuthServiceTests()
    {
        _sut = Substitute.For<ITdxAuthService>();
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_return_token_on_success()
    {
        _sut.GetAccessTokenAsync(default)
            .ReturnsForAnyArgs(Task.FromResult("test-access-token"));

        var token = await _sut.GetAccessTokenAsync();

        Assert.NotNull(token);
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_throw_on_invalid_credentials()
    {
        _sut.GetAccessTokenAsync(default)
            .ReturnsForAnyArgs(Task.FromException<string>(new HttpRequestException("401 Unauthorized")));

        await Assert.ThrowsAsync<HttpRequestException>(() => _sut.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_should_throw_when_response_missing_token()
    {
        _sut.GetAccessTokenAsync(default)
            .ReturnsForAnyArgs(Task.FromException<string>(
                new InvalidOperationException("TDX token response did not contain access_token.")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetAccessTokenAsync());
    }
}
