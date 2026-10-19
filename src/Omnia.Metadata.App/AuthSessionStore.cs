using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Omnia.Metadata.App;

public sealed record StoredAuthSession(
    string Email,
    string Role,
    string SessionToken,
    DateTime ExpiresUtc,
    string Provider,
    string Plan,
    int MonthlyFreeQueriesUsed,
    int OneOffQueriesAvailable,
    PurchaseSummary[]? Purchases);

/// <summary>
/// Stores the desktop session encrypted for the current Windows user.
/// The token is never written in clear text and is not included in logs.
/// </summary>
public sealed class AuthSessionStore
{
    const string FileName = "session.dat";
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Omnia Metadata");

    static string FilePath => Path.Combine(DirectoryPath, FileName);

    public StoredAuthSession? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);
            var json = Encoding.UTF8.GetString(Unprotect(encrypted));
            var session = JsonSerializer.Deserialize<StoredAuthSession>(json, JsonOptions);
            if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
            {
                Clear();
                return null;
            }

            if (session.ExpiresUtc <= DateTime.UtcNow)
            {
                Clear();
                return null;
            }

            return session;
        }
        catch
        {
            // A corrupt or machine-mismatched vault must never block the app.
            Clear();
            return null;
        }
    }

    public void Save(AuthResult result)
    {
        if (string.IsNullOrWhiteSpace(result.SessionToken)) return;

        var session = new StoredAuthSession(
            result.Email,
            result.Role,
            result.SessionToken,
            result.ExpiresUtc,
            result.Provider,
            result.Plan,
            result.MonthlyFreeQueriesUsed,
            result.OneOffQueriesAvailable,
            result.Purchases);
        var json = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        var encrypted = Protect(json);

        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = Path.Combine(DirectoryPath, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // A failed cleanup should not prevent the user from using the app.
        }
    }

    static byte[] Protect(byte[] data)
    {
        var input = new DataBlob(data);
        var output = default(DATA_BLOB);
        try
        {
            if (!CryptProtectData(ref input.Blob, "Omnia Metadata session", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return CopyBlob(output);
        }
        finally
        {
            input.Dispose();
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    static byte[] Unprotect(byte[] data)
    {
        var input = new DataBlob(data);
        var output = default(DATA_BLOB);
        try
        {
            if (!CryptUnprotectData(ref input.Blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return CopyBlob(output);
        }
        finally
        {
            input.Dispose();
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    static byte[] CopyBlob(DATA_BLOB blob)
    {
        if (blob.cbData == 0 || blob.pbData == IntPtr.Zero) return [];
        var result = new byte[checked((int)blob.cbData)];
        Marshal.Copy(blob.pbData, result, 0, result.Length);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DATA_BLOB
    {
        public uint cbData;
        public IntPtr pbData;
    }

    sealed class DataBlob : IDisposable
    {
        public DATA_BLOB Blob;

        public DataBlob(byte[] data)
        {
            Blob.cbData = checked((uint)data.Length);
            Blob.pbData = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, Blob.pbData, data.Length);
        }

        public void Dispose()
        {
            if (Blob.pbData == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Blob.pbData);
            Blob.pbData = IntPtr.Zero;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr hMem);
}
