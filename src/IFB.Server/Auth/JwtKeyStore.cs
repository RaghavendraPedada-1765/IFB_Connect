using System.Security.Cryptography;

namespace IFB.Server.Auth;

/// <summary>One persistent key pair is shared by token signing and validation.</summary>
public sealed class JwtKeyStore : IDisposable
{
    public RSA SigningKey { get; } = RSA.Create();

    public JwtKeyStore(IConfiguration config, IHostEnvironment environment)
    {
        var path = Path.GetFullPath(config["Jwt:RsaPrivateKeyPath"] ?? "certs/jwt_rsa_private.pem",
            environment.ContentRootPath);
        if (!File.Exists(path))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Configure a persistent JWT private key before starting in production.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SigningKey.KeySize = 2048;
            File.WriteAllText(path, SigningKey.ExportRSAPrivateKeyPem());
        }
        SigningKey.ImportFromPem(File.ReadAllText(path));
    }

    public void Dispose() => SigningKey.Dispose();
}
