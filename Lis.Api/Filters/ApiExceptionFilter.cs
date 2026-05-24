using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Lis.Api.Filters;

/// <summary>
/// Central exception → ProblemDetails translator for REST controllers.
/// Pattern lifted from sibling project RickAi/Api/Filters/ApiExceptionFilter.cs.
/// Controllers throw typed exceptions; this filter maps them to status codes,
/// logs 5xx as Error and 4xx as Warning, and includes the active trace id so
/// the response can be correlated with OpenTelemetry spans.
/// </summary>
public sealed class ApiExceptionFilter(
	ILogger<ApiExceptionFilter> logger,
	ProblemDetailsFactory problemDetailsFactory
) :IExceptionFilter {
	public void OnException(ExceptionContext context) {
		int status = MapStatusCode(context.Exception);

		if (status >= 500)
			logger.LogError(context.Exception, "Unhandled exception");
		else
			logger.LogWarning(context.Exception, "Request failed with {StatusCode}", status);

		string traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
		ProblemDetails problemDetails = problemDetailsFactory
			.CreateProblemDetails(
				context.HttpContext,
				statusCode: status,
				title: "An error occurred while processing your request",
				detail: context.Exception.Message
			);
		problemDetails.Extensions["traceId"] = traceId;

		context.Result           = new ObjectResult(problemDetails) { StatusCode = status };
		context.ExceptionHandled = true;
	}

	private static int MapStatusCode(Exception ex) => ex switch {
		OperationCanceledException  => StatusCodes.Status499ClientClosedRequest,
		BadHttpRequestException     => StatusCodes.Status400BadRequest,
		ArgumentException           => StatusCodes.Status400BadRequest,
		KeyNotFoundException        => StatusCodes.Status404NotFound,
		UnauthorizedAccessException => StatusCodes.Status403Forbidden,
		_                           => StatusCodes.Status500InternalServerError
	};
}
