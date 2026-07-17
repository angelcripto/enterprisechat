namespace EnterpriseChat.Server.Auth.Hashers;

/// <summary>
/// Mapea <see cref="HashAlgorithm"/> a la implementación concreta de
/// <see cref="IPasswordHashVerifier"/>. Registro estático: los verifiers
/// no tienen estado mutable y son stateless, así que reutilizar la misma
/// instancia entre peticiones es seguro y evita alocaciones.
/// </summary>
public sealed class HashVerifierRegistry
{
    private readonly IReadOnlyDictionary<HashAlgorithm, IPasswordHashVerifier> _map;

    public HashVerifierRegistry()
    {
        _map = new Dictionary<HashAlgorithm, IPasswordHashVerifier>
        {
            [HashAlgorithm.Bcrypt] = new BcryptVerifier(),
            [HashAlgorithm.Argon2id] = new Argon2idVerifier(),
            [HashAlgorithm.Sha256Salted] = new Sha256SaltedVerifier(),
            [HashAlgorithm.Sha256] = HexHashVerifier.Sha256,
            [HashAlgorithm.Sha1] = HexHashVerifier.Sha1,
            [HashAlgorithm.Md5] = HexHashVerifier.Md5,
            [HashAlgorithm.Plaintext] = new PlaintextVerifier(),
        };
    }

    public IPasswordHashVerifier Get(HashAlgorithm algorithm)
    {
        if (!_map.TryGetValue(algorithm, out var verifier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                algorithm,
                $"No hay verifier registrado para el algoritmo {algorithm}.");
        }
        return verifier;
    }

    public bool TryGet(HashAlgorithm algorithm, out IPasswordHashVerifier verifier)
        => _map.TryGetValue(algorithm, out verifier!);
}
