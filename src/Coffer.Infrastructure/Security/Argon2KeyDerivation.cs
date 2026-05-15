using System.Text;
using Coffer.Core.Security;
using Konscious.Security.Cryptography;

namespace Coffer.Infrastructure.Security;

public sealed class Argon2KeyDerivation : IMasterKeyDerivation
{
    public Task<byte[]> DeriveMasterKeyAsync(
        string password,
        byte[] salt,
        Argon2Parameters parameters,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(parameters);

        return Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();

                var passwordBytes = Encoding.UTF8.GetBytes(password);
                try
                {
                    using var argon = new Argon2id(passwordBytes)
                    {
                        Salt = salt,
                        MemorySize = parameters.MemorySizeKb,
                        Iterations = parameters.Iterations,
                        DegreeOfParallelism = parameters.Parallelism,
                    };
                    return argon.GetBytes(parameters.OutputBytes);
                }
                finally
                {
                    Array.Clear(passwordBytes, 0, passwordBytes.Length);
                }
            },
            ct);
    }
}
