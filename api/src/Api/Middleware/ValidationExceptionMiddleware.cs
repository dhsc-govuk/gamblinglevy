using Api.Common;
using Application.Common.Security;
using FluentValidation;

namespace Api.Middleware;

/// <summary>
/// Turns a FluentValidation failure — raised by ValidationBehaviour for a command, or by QueryGuard for a
/// query string — into the API's single 400 body.
///
/// Model-binding and JSON failures do not arrive here: the [ApiController] filter returns a result rather
/// than throwing, so it never unwinds this far. That path builds the same envelope in
/// InvalidModelStateResponse instead.
/// </summary>
public class ValidationExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            // Recorded before the response is written: the response is streamed, so the caller can observe
            // the 400 the moment the body is flushed, whether or not this method has finished. Recording
            // first makes the security trail deterministic rather than dependent on how fast the caller
            // reads (BLUM-7240 AC8).
            await RecordAsync(context, ex);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            var envelope = ValidationErrorEnvelope.From(
                ex.Errors.Select(e => new ValidationErrorItem(e.PropertyName, e.ErrorMessage)));

            await context.Response.WriteAsync(envelope.ToJson());
        }
    }

    /// <summary>
    /// Nothing here may throw. The 400 is already decided, and turning it into a 500 because the audit trail
    /// misfired would be strictly worse than the missing row.
    ///
    /// IInputRejectionRecorder promises that of itself, but the promise only covers the body of RecordAsync:
    /// resolving the service and building the context happen before control reaches that try block, and
    /// GetService throws rather than returning null when anything in the dependency graph fails to construct
    /// — or when the request scope has already been torn down because the client aborted. Hence the belt as
    /// well as the braces.
    /// </summary>
    private static async Task RecordAsync(HttpContext context, ValidationException exception)
    {
        if (InputRejectionContext.IsExempt(context))
        {
            return;
        }

        try
        {
            // Resolved from the request scope rather than injected: this middleware is a singleton in the
            // pipeline, and the recorder depends on the scoped audit repository.
            var recorder = context.RequestServices.GetService<IInputRejectionRecorder>();
            if (recorder is null)
            {
                return;
            }

            var reasons = exception.Errors
                .Select(failure => new InputRejectionReason(
                    failure.PropertyName,
                    InputRejectionClassifier.KindForErrorCode(failure.ErrorCode)))
                .ToList();

            // Not context.RequestAborted — see InputRejectionRecorder.RecordAsync.
            await recorder.RecordAsync(InputRejectionContext.Build(context, reasons), CancellationToken.None);
        }
        catch (Exception recordingFailure)
        {
            context.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger<ValidationExceptionMiddleware>()
                .LogError(recordingFailure, "Failed to record a rejected request for {RequestPath}", context.Request.Path);
        }
    }
}

public static class ValidationExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseValidationExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ValidationExceptionMiddleware>();
    }
}