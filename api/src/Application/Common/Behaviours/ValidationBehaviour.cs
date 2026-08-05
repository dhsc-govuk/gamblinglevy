using FluentValidation;
using MediatR;

namespace Application.Common.Behaviours;
public class ValidationBehaviour<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);

            var validationResults = await Task.WhenAll(
                validators.Select(v =>
                    v.ValidateAsync(context, cancellationToken)));

            var failures = validationResults
                .Where(r => r.Errors.Any())
                .SelectMany(r => r.Errors)
                .ToList();

            if (failures.Any())
            {
                // The validation_failed telemetry event used to be emitted here as well. It is now emitted
                // once, by InputRejectionRecorder, for the command, model-binding and query-string paths
                // alike (BLUM-7240). Emitting from both meant one command failure produced two events under
                // one name with two different property bags — doubling anything that counted them and
                // splitting any breakdown by a dimension only one of them carried. The recorder reports
                // `endpoint` where this reported `requestType`; on an HTTP API that is the more useful key
                // and it is the only one available on all three paths.
                throw new ValidationException(failures);
            }
        }

        return await next();
    }
}