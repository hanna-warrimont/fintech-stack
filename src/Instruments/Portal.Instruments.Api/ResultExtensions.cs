using Portal.Instruments.Domain.Abstractions;

namespace Portal.Instruments.Api;

internal static class ResultExtensions
{
    /// <summary>Fachlicher Fehler → HTTP-Problem. Der stabile Fehlercode steht in <c>code</c>.</summary>
    public static IResult ToProblem(this Error error) => Results.Problem(
        statusCode: error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        },
        title: error.Message,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
