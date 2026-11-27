using Microsoft.AspNetCore.DataProtection;
using Shush.Data;

namespace Shush.Design.Services;

/// <summary>Secret variable values, encrypted with ASP.NET Data Protection (key ring in the data folder).</summary>
public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Shush.Variables.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
