using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace Nichotan.Application.Common;

/// <summary>Request validation failed; Errors maps each field to its messages.</summary>
public class RequestValidationException(IEnumerable<ValidationFailure> failures) : Exception("One or more fields are invalid.")
{
    public IDictionary<string, string[]> Errors { get; } = failures
        .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
        .ToDictionary(g => g.Key, g => g.ToArray());
}

/// <summary>Runs every FluentValidation validator for a message before its handler.</summary>
public class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TMessage>(message);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count != 0) throw new RequestValidationException(failures);
        }

        return await next(message, cancellationToken);
    }
}

public static class RuleExtensions
{
    /// <summary>Basic shape only; the identity layer enforces the full password policy.</summary>
    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(10).MaximumLength(128).WithMessage("Use at least 10 characters.");

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(100);
}
