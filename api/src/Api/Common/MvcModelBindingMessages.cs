using Microsoft.AspNetCore.Mvc;

namespace Api.Common;

/// <summary>
/// Stops the model binder repeating the caller's own input back at them.
///
/// InvalidModelStateResponse only replaces a message when the ModelError carries an exception, because a
/// message written by a validation attribute is safe and the client matches on it. These four accessors are
/// the hole in that rule: they interpolate the attempted value into the message, and
/// ModelStateDictionary.TryAddModelError discards the FormatException that produced it — so the result
/// reaches the caller as "The value 'x' is not valid for Page." with no exception attached to catch it.
///
/// Every other accessor names only the field. They are deliberately left alone, RequiredAttribute's
/// "The {0} field is required." especially: client/src/views/Admin/Organisations/errors.ts matches that
/// exact phrase to choose its own copy.
/// </summary>
public static class MvcModelBindingMessages
{
    public static void Configure(MvcOptions options)
    {
        var messages = options.ModelBindingMessageProvider;

        messages.SetAttemptedValueIsInvalidAccessor((_, field) => $"Enter a valid value for {field}.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => InvalidModelStateResponse.InvalidValue);
        messages.SetValueIsInvalidAccessor(_ => InvalidModelStateResponse.InvalidValue);
        messages.SetValueMustNotBeNullAccessor(_ => InvalidModelStateResponse.InvalidValue);
    }
}