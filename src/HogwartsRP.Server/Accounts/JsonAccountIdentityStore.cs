using System.Security.Cryptography;
using System.Text.Json;

namespace HogwartsRP.Server.Accounts;

/// <summary>External identity providers supported by the central HogwartsRP account system.</summary>
public enum ExternalIdentityProvider
{
    Discord,
    Steam,
    EpicGames
}

/// <summary>A central HogwartsRP player account. No provider credentials are stored here.</summary>
public sealed record HogwartsAccount(
    Guid Id,
    string DisplayName,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// A verified external identity attached to a central HogwartsRP account.
/// ProviderSubject must come from a server-verified provider response, never browser input.
/// </summary>
public sealed record ExternalAccountLink(
    Guid AccountId,
    ExternalIdentityProvider Provider,
    string ProviderSubject,
    string? ProviderDisplayName,
    DateTimeOffset LinkedAtUtc);

/// <summary>
/// Single-process JSON persistence for central account identities and external account links.
/// This is a storage foundation, not an authentication endpoint or OAuth implementation.
/// </summary>
public sealed class JsonAccountIdentityStore
{
    private sealed class StoreDocument
    {
        public int Version { get; set; } = 1;
        public List<HogwartsAccount> Accounts { get; set; } = [];
        public List<ExternalAccountLink> Links { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StoreDocument? _document;
    private readonly TimeProvider _timeProvider;

    public JsonAccountIdentityStore(string filePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<HogwartsAccount> CreateAccountAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = ValidateDisplayName(displayName);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var account = new HogwartsAccount(Guid.NewGuid(), normalizedName, _timeProvider.GetUtcNow());
            _document!.Accounts.Add(account);
            try { await SaveAsync(cancellationToken); }
            catch { _document.Accounts.Remove(account); throw; }
            return account;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Attaches a provider identity after its provider-specific adapter has verified it.
    /// One provider identity and one link per provider are allowed per central account.
    /// </summary>
    public async ValueTask<ExternalAccountLink> LinkVerifiedIdentityAsync(
        Guid accountId,
        ExternalIdentityProvider provider,
        string providerSubject,
        string? providerDisplayName = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        var subject = ValidateSubject(providerSubject);
        var safeDisplayName = NormalizeOptionalDisplayName(providerDisplayName);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (!_document!.Accounts.Any(account => account.Id == accountId))
                throw new KeyNotFoundException("The HogwartsRP account does not exist.");

            var claimed = _document.Links.SingleOrDefault(link =>
                link.Provider == provider && string.Equals(link.ProviderSubject, subject, StringComparison.Ordinal));
            if (claimed is not null)
            {
                if (claimed.AccountId == accountId) return claimed;
                throw new InvalidOperationException("This external account is already linked to another HogwartsRP account.");
            }

            if (_document.Links.Any(link => link.AccountId == accountId && link.Provider == provider))
                throw new InvalidOperationException("This HogwartsRP account already has an identity linked for this provider.");

            var link = new ExternalAccountLink(accountId, provider, subject, safeDisplayName, _timeProvider.GetUtcNow());
            _document.Links.Add(link);
            try { await SaveAsync(cancellationToken); }
            catch { _document.Links.Remove(link); throw; }
            return link;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Returns provider links for the specified account only.</summary>
    public async ValueTask<IReadOnlyList<ExternalAccountLink>> GetLinksAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _document!.Links
                .Where(link => link.AccountId == accountId)
                .OrderBy(link => link.Provider)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<bool> UnlinkAsync(
        Guid accountId,
        ExternalIdentityProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Account ID must not be empty.", nameof(accountId));
        if (!Enum.IsDefined(provider)) throw new ArgumentOutOfRangeException(nameof(provider));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var link = _document!.Links.SingleOrDefault(item => item.AccountId == accountId && item.Provider == provider);
            if (link is null) return false;
            _document.Links.Remove(link);
            try { await SaveAsync(cancellationToken); }
            catch { _document.Links.Add(link); throw; }
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_document is not null) return;
        if (!File.Exists(_filePath))
        {
            _document = new StoreDocument();
            return;
        }

        try
        {
            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            _document = await JsonSerializer.DeserializeAsync<StoreDocument>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The account identity store is empty.");
            ValidateDocument(_document);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The account identity store contains invalid JSON and was not overwritten.", exception);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_filePath)}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, _document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidateDocument(StoreDocument document)
    {
        if (document.Version != 1 || document.Accounts is null || document.Links is null)
            throw new InvalidDataException("The account identity store has an unsupported or incomplete schema.");
        if (document.Accounts.Any(account => account.Id == Guid.Empty)
            || document.Accounts.Select(account => account.Id).Distinct().Count() != document.Accounts.Count)
            throw new InvalidDataException("The account identity store contains invalid or duplicate account IDs.");
        if (document.Links.Any(link => !document.Accounts.Any(account => account.Id == link.AccountId)
            || !Enum.IsDefined(link.Provider) || string.IsNullOrWhiteSpace(link.ProviderSubject)))
            throw new InvalidDataException("The account identity store contains an invalid external link.");
        if (document.Links.GroupBy(link => (link.Provider, link.ProviderSubject)).Any(group => group.Count() > 1)
            || document.Links.GroupBy(link => (link.AccountId, link.Provider)).Any(group => group.Count() > 1))
            throw new InvalidDataException("The account identity store contains duplicate external links.");
    }

    private static string ValidateDisplayName(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64 || normalized.Any(char.IsControl))
            throw new ArgumentException("Display names must be 1 to 64 printable characters.", nameof(value));
        return normalized;
    }

    private static string ValidateSubject(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 256 || normalized.Any(char.IsControl))
            throw new ArgumentException("Provider subject IDs must be 1 to 256 printable characters.", nameof(value));
        return normalized;
    }

    private static string? NormalizeOptionalDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Provider display names must be at most 128 printable characters.", nameof(value));
        return normalized;
    }
}
