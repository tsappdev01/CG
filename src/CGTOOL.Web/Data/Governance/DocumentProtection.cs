using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace CGTOOL.Web.Data.Governance;

/// <summary>The names stored in <see cref="DocumentBlob.ProtectionScheme"/>.</summary>
public static class DocumentProtection
{
    /// <summary>The file as uploaded. A varbinary(max) that opens in any tool -- and that a stolen
    /// database backup hands over in full.</summary>
    public const string None = "None";

    /// <summary>Encrypted with ASP.NET Data Protection, keyed on the application's key ring.</summary>
    public const string DataProtectionV1 = "DataProtection:v1";
}

/// <summary>Turns an uploaded file into what goes in the column, and back.</summary>
public interface IDocumentProtector
{
    /// <summary>The scheme new documents are written under.</summary>
    string Scheme { get; }

    byte[] Protect(byte[] content);

    /// <summary>Reads a stored document back. Takes the scheme from the row rather than assuming
    /// the current one, so documents written before encryption was switched on keep opening.</summary>
    byte[] Unprotect(byte[] stored, string scheme);

    /// <summary>Lowercase hex SHA-256 of the file as uploaded.</summary>
    static string Sha256Hex(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}

/// <summary>Stores the file as it arrived. This is the default, and it matches the IPO system's
/// dbo.EmiratesIdDocuments, whose ContentProtected column holds unencrypted JPEG and PDF bytes.
///
/// It is the default because switching it is an operational commitment, not a code change: once
/// documents are encrypted, losing the Data Protection key ring loses the documents, and that ring
/// lives outside the database (C:\ProgramData\CGTOOL\DataProtectionKeys) so a database backup alone
/// no longer restores them. Set Documents:Encrypt once the key ring is in the backup plan.</summary>
public sealed class UnprotectedDocumentStore : IDocumentProtector
{
    public string Scheme => DocumentProtection.None;

    public byte[] Protect(byte[] content) => content;

    public byte[] Unprotect(byte[] stored, string scheme) => scheme switch
    {
        DocumentProtection.None => stored,

        // A row written while encryption was on, read back after it was switched off. Refusing is
        // the honest answer: the bytes are there and this process cannot read them, which is a
        // configuration mistake to fix rather than a corrupt document to report to the user.
        _ => throw new InvalidOperationException(
            $"This document was stored with protection scheme '{scheme}', but document encryption is " +
            "switched off (Documents:Encrypt). Switch it back on to read documents written while it was enabled."),
    };
}

/// <summary>Encrypts documents with the application's Data Protection key ring, so a database file
/// or backup taken on its own does not yield the documents.
///
/// The key ring is the whole of the protection: it is not in the database, and without it these
/// rows cannot be read by anybody, this application included. Back it up, and keep that backup
/// somewhere the database backup is not.</summary>
public sealed class DataProtectionDocumentProtector(IDataProtectionProvider provider) : IDocumentProtector
{
    // A purpose string of its own: a document ciphertext cannot then be swapped in anywhere else
    // that uses the same key ring (the acting-as cookie, for one), and vice versa.
    private readonly IDataProtector _protector = provider.CreateProtector("DocumentContent");

    public string Scheme => DocumentProtection.DataProtectionV1;

    public byte[] Protect(byte[] content) => _protector.Protect(content);

    public byte[] Unprotect(byte[] stored, string scheme) => scheme switch
    {
        DocumentProtection.DataProtectionV1 => _protector.Unprotect(stored),

        // Documents written before encryption was switched on. They are still readable, and saying
        // so is the point of storing the scheme per row -- otherwise turning encryption on would
        // mean a migration that rewrites every document before the site can start.
        DocumentProtection.None => stored,

        _ => throw new InvalidOperationException($"Unknown document protection scheme '{scheme}'."),
    };
}
