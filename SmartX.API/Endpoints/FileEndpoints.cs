using System.Security.Cryptography;

namespace SmartX.Api.Endpoints;

public static class FileEndpoints
{
    private static readonly string UploadsPath =
        Path.Combine(Directory.GetCurrentDirectory(), "Uploads");

    // Fixed key for a student project — in a real system this would come
    // from configuration/secrets, not be hardcoded.
    // "MySecretKey12345" is 16 chars, repeated twice = 32 bytes for AES-256.
    private static readonly byte[] EncryptionKey =
        "MySecretKey12345MySecretKey12345"u8.ToArray();

    public static void MapFileEndpoints(this WebApplication app)
    {
        Directory.CreateDirectory(UploadsPath);

        app.MapPost("/api/sensors/{macAddress}/files", async (string macAddress, IFormFile file) =>
        {
            if (file.Length == 0)
                return Results.BadRequest("No file was provided.");

            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            aes.GenerateIV();

            string safeName = $"{macAddress.Replace(":", "")}_{Guid.NewGuid()}_{file.FileName}.enc";
            string fullPath = Path.Combine(UploadsPath, safeName);

            await using var outputStream = File.Create(fullPath);

            // Write the IV first — needed to decrypt this file later.
            await outputStream.WriteAsync(aes.IV);

            await using var cryptoStream = new CryptoStream(
                outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);

            await using var inputStream = file.OpenReadStream();
            await inputStream.CopyToAsync(cryptoStream);

            return Results.Ok(new { fileName = safeName, macAddress, sizeBytes = file.Length });
        }).DisableAntiforgery();

        app.MapGet("/api/sensors/{macAddress}/files", (string macAddress) =>
        {
            string prefix = macAddress.Replace(":", "");
            var files = Directory.GetFiles(UploadsPath, $"{prefix}_*")
                .Select(Path.GetFileName);
            return Results.Ok(files);
        });
    }
}