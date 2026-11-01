using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Tests.SharedKernel;

/// <summary>A-01: Result and Result&lt;T&gt; success and failure.</summary>
public class ResultTests
{
    private static readonly Error SomeError = Error.NotFound("Test.Missing", "Not there.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Success_with_value_exposes_the_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Value_of_a_failure_throws_naming_the_error_code()
    {
        Result<int> result = SomeError;

        Assert.True(result.IsFailure);
        Assert.Equal(SomeError, result.Error);
        var thrown = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Test.Missing", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_requires_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));
    }

    [Theory]
    [InlineData(ErrorKind.Validation)]
    [InlineData(ErrorKind.NotFound)]
    [InlineData(ErrorKind.Conflict)]
    [InlineData(ErrorKind.Rule)]
    public void Error_factories_set_their_kind(ErrorKind kind)
    {
        var error = kind switch
        {
            ErrorKind.Validation => Error.Validation("C", "m"),
            ErrorKind.NotFound => Error.NotFound("C", "m"),
            ErrorKind.Conflict => Error.Conflict("C", "m"),
            _ => Error.Rule("C", "m"),
        };

        Assert.Equal((kind, "C", "m"), (error.Kind, error.Code, error.Message));
    }

    [Fact]
    public void Error_needs_a_code_and_message()
    {
        Assert.Throws<ArgumentException>(() => new Error(" ", "m", ErrorKind.Validation));
        Assert.Throws<ArgumentException>(() => new Error("C", "", ErrorKind.Validation));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Error("C", "m", (ErrorKind)99));
    }
}
