using Application.Common.Interfaces;
using Domain.Common;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

// A no-op email transport that logs the recipients and subject rather than sending. Used until a real
// transactional-email integration for data-collection reminders is wired up; lets the reminder flow run
// end-to-end in dev and tests without an external dependency.
public class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        Guard.NotNull(logger, nameof(logger));

        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(message, nameof(message));

        _logger.LogInformation(
            "[Email] (logging transport) to {Recipients} subject {Subject}",
            string.Join(", ", message.To),
            message.Subject);

        return Task.CompletedTask;
    }
}