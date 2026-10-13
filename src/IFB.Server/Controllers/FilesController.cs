using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mime;
using System.Security.Cryptography;

namespace IFB.Server.Controllers;

/// <summary>
/// File upload/download with SHA-256 integrity verification and conversation access control.
/// Max size enforced by Kestrel (50 MB default), with a configurable antivirus scanner.
/// </summary>
[ApiController]
[Route("api/v1/files")]
[Authorize]
public sealed class FilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<FilesController> _logger;

    public FilesController(AppDbContext db, IConfiguration config,
        ILogger<FilesController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    private string CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    // POST /api/v1/files/upload
    // multipart/form-data: file + conversationId
    [HttpPost("upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_073_741_824)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string conversationId,
        CancellationToken ct)
    {
        var userId = CurrentUserId;

        // Verify membership
        var isMember = await _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId, ct);
        if (!isMember) return Forbid();

        if (file.Length == 0) return BadRequest("Empty file.");
        var allowed = _config.GetSection("FileStorage:AllowedExtensions").Get<string[]>() ?? [".pdf", ".txt", ".png", ".jpg"];
        if (!allowed.Contains(Path.GetExtension(file.FileName), StringComparer.OrdinalIgnoreCase)) return BadRequest("This file type is not allowed.");

        var maxBytes = _config.GetValue<long>("FileStorage:MaxFileSizeBytes", 52_428_800);
        if (file.Length > maxBytes)
            return BadRequest($"File exceeds maximum size of {maxBytes / 1_048_576} MB.");

        // Sanitize filename
        var safeName = Path.GetFileName(file.FileName);
        safeName = string.Concat(safeName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (string.IsNullOrEmpty(safeName)) safeName = "upload";
        safeName = safeName.Length > 180 ? safeName[^180..] : safeName;

        // Build storage path
        var basePath = _config["FileStorage:BasePath"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IFBConnect", "files");

        var datePart = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var folder = Path.Combine(basePath, datePart);
        Directory.CreateDirectory(folder);

        var fileId = Guid.NewGuid().ToString();
        var storagePath = Path.Combine(folder, $"{fileId}_{safeName}");

        // Stream to disk + compute SHA-256 simultaneously
        string sha256Hash;
        try
        {
        await using (var fs = System.IO.File.Create(storagePath))
        using (var sha = SHA256.Create())
        using (var cs = new CryptoStream(fs, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            await file.CopyToAsync(cs, ct);
            cs.FlushFinalBlock();
            sha256Hash = Convert.ToHexString(sha.Hash!).ToLower();
        }
        }
        catch { System.IO.File.Delete(storagePath); throw; }

        bool scanned = false;
        try
        {
            var command = _config["FileStorage:AntivirusCommand"];
            if (string.IsNullOrWhiteSpace(command) || !System.IO.File.Exists(command))
            {
                if (!_config.GetValue("FileStorage:SkipAvScanIfMissing", false))
                    throw new InvalidOperationException("Antivirus scanner is unavailable.");
                _logger.LogWarning("Upload scan skipped by explicit configuration for file {FileId}", fileId);
            }
            else
            {
                var start = new System.Diagnostics.ProcessStartInfo(command,
                    (_config["FileStorage:AntivirusArgs"] ?? "-Scan -ScanType 3 -File \"{filePath}\"").Replace("{filePath}", Path.GetFullPath(storagePath)))
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
                using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Scanner could not start.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { if (!process.HasExited) process.Kill(true); throw; }
                if (process.ExitCode != 0) throw new InvalidOperationException("File was rejected by the antivirus scanner.");
                scanned = true;
            }
        }
        catch (OperationCanceledException) { System.IO.File.Delete(storagePath); throw; }
        catch (Exception ex)
        { System.IO.File.Delete(storagePath); _logger.LogWarning("Upload {FileId} rejected: {Reason}", fileId, ex.Message); return UnprocessableEntity("File safety check failed."); }

        new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider().TryGetContentType(safeName, out var contentType);
        contentType ??= MediaTypeNames.Application.Octet;
        var attachment = new FileAttachment
        {
            Id = fileId,
            OriginalFileName = safeName,
            ContentType = contentType,
            SizeBytes = file.Length,
            StoragePath = storagePath,
            Sha256Hash = sha256Hash,
            UploadedByUserId = userId,
            ConversationId = conversationId,
            IsAvScanPassed = scanned,
            UploadedAt = DateTime.UtcNow
        };

        _db.FileAttachments.Add(attachment);
        try { await _db.SaveChangesAsync(ct); }
        catch { System.IO.File.Delete(storagePath); throw; }

        _logger.LogInformation("File uploaded: {Id} ({Name}) by {User}",
            fileId, safeName, userId);

        return Ok(new FileUploadResultDto(
            fileId, safeName, contentType, file.Length, sha256Hash));
    }

    // GET /api/v1/files/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> Download(string id)
    {
        var userId = CurrentUserId;

        var attachment = await _db.FileAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id);

        if (attachment == null) return NotFound();

        // Access check — must be member of conversation the file was shared in
        if (attachment.ConversationId == null) return Forbid();
        if (attachment.ConversationId != null)
        {
            var isMember = await _db.ConversationMembers
                .AnyAsync(m => m.ConversationId == attachment.ConversationId && m.UserId == userId);
            if (!isMember) return Forbid();
        }

        if (!System.IO.File.Exists(attachment.StoragePath))
        {
            _logger.LogError("File missing from disk: {Path}", attachment.StoragePath);
            return StatusCode(500, "File unavailable.");
        }

        _logger.LogInformation("File downloaded: {Id} ({Name}) by {User}",
            id, attachment.OriginalFileName, userId);

        // Serve with content-disposition attachment for browser safety
        var stream = System.IO.File.OpenRead(attachment.StoragePath);
        var cd = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
        {
            FileName = attachment.OriginalFileName
        };
        Response.Headers["Content-Disposition"] = cd.ToString();
        Response.Headers["X-Content-SHA256"] = attachment.Sha256Hash;

        return File(stream, string.IsNullOrWhiteSpace(attachment.ContentType) ? MediaTypeNames.Application.Octet : attachment.ContentType);
    }

    // GET /api/v1/files/{id}/info
    [HttpGet("{id}/info")]
    public async Task<IActionResult> GetInfo(string id)
    {
        var userId = CurrentUserId;
        var attachment = await _db.FileAttachments
            .Include(f => f.UploadedBy)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id);

        if (attachment == null) return NotFound();

        if (attachment.ConversationId != null)
        {
            var isMember = await _db.ConversationMembers
                .AnyAsync(m => m.ConversationId == attachment.ConversationId && m.UserId == userId);
            if (!isMember) return Forbid();
        }

        return Ok(new FileInfoDto(
            attachment.Id,
            attachment.OriginalFileName,
            attachment.ContentType ?? "application/octet-stream",
            attachment.SizeBytes,
            attachment.Sha256Hash,
            attachment.UploadedBy?.DisplayName ?? "Unknown",
            attachment.UploadedAt));
    }

    // GET /api/v1/conversations/{cid}/files — all files in a conversation
    [HttpGet]
    public async Task<IActionResult> MyFiles() => Ok(await _db.FileAttachments
        .Where(f => _db.ConversationMembers.Any(m => m.ConversationId == f.ConversationId && m.UserId == CurrentUserId))
        .OrderByDescending(f => f.UploadedAt).Take(500).Select(f => new FileInfoDto(f.Id, f.OriginalFileName,
            f.ContentType ?? "application/octet-stream", f.SizeBytes, f.Sha256Hash, f.UploadedBy.DisplayName, f.UploadedAt)).ToListAsync());

    [HttpGet("/api/v1/conversations/{cid}/files")]
    public async Task<IActionResult> GetConversationFiles(string cid,
        [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var userId = CurrentUserId;
        var isMember = await _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == cid && m.UserId == userId);
        if (!isMember) return Forbid();

        var files = await _db.FileAttachments
            .Where(f => f.ConversationId == cid)
            .Include(f => f.UploadedBy)
            .OrderByDescending(f => f.UploadedAt)
            .Skip(offset).Take(limit)
            .AsNoTracking()
            .Select(f => new FileInfoDto(
                f.Id, f.OriginalFileName,
                f.ContentType ?? "application/octet-stream",
                f.SizeBytes, f.Sha256Hash,
                f.UploadedBy != null ? f.UploadedBy.DisplayName : "Unknown",
                f.UploadedAt))
            .ToListAsync();

        return Ok(files);
    }
}

public record FileUploadResultDto(
    string Id, string FileName, string ContentType,
    long SizeBytes, string Sha256Hash);

public record FileInfoDto(
    string Id, string FileName, string ContentType,
    long SizeBytes, string Sha256Hash,
    string UploadedByName, DateTime UploadedAt);
