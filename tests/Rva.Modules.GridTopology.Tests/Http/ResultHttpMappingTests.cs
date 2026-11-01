using Microsoft.AspNetCore.Http;
using Rva.Modules.GridTopology.Http;
using Rva.SharedKernel.Results;

namespace Rva.Modules.GridTopology.Tests.Http;

/// <summary>Each error kind maps to its status (plan p1-s01 section 4), and the problem carries the error code.</summary>
public class ResultHttpMappingTests
{
    [Theory]
    [InlineData(ErrorKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorKind.Rule, StatusCodes.Status422UnprocessableEntity)]
    public void Error_kind_maps_to_its_status(ErrorKind kind, int status)
    {
        var problem = new Error("GridTopology.Test", "Something is wrong.", kind).ToProblem();

        Assert.Equal(status, problem.StatusCode);
        Assert.Equal("Something is wrong.", problem.ProblemDetails.Detail);
        Assert.Equal("GridTopology.Test", problem.ProblemDetails.Extensions["code"]);
    }
}
