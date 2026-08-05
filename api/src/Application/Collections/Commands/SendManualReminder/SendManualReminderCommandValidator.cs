using FluentValidation;

namespace Application.Collections.Commands.SendManualReminder;

public class SendManualReminderCommandValidator : AbstractValidator<SendManualReminderCommand>
{
    public SendManualReminderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}