namespace Currency.Services;

// The portable UI receives native operations from its platform entry point.
public sealed class CurrencySession(
    Func<string, CancellationToken, Task<string>> lookup,
    Func<CancellationToken, Task<string>> verify
)
{
    public Task<string> GetSymbolAsync(string code, CancellationToken cancellationToken) =>
        lookup(code, cancellationToken);

    public Task<string> VerifyAsync(CancellationToken cancellationToken) => verify(cancellationToken);
}
