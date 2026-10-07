using System.Net;
using System.Net.Mail;
using SkiaSharp;
using Slims.Application.Interfaces;
using Slims.Application.Services;

namespace Slims.Infrastructure.Messaging;

/// <summary>Draws the evidence stamp on a copy of the photograph: a dark band along the bottom with white text.</summary>
public sealed class SkiaImageStamper : IImageStamper
{
    public byte[]? Stamp(byte[] image, IReadOnlyList<string> lines)
    {
        try
        {
            using var src = SKBitmap.Decode(image);
            if (src is null) return null;
            var (w, h) = (src.Width, src.Height);
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var c = surface.Canvas;
            c.DrawBitmap(src, 0, 0);
            var fs = Math.Max(14f, Math.Min(w, h * 1.4f) / 40f);
            var lineH = fs * 1.35f;
            var band = lineH * lines.Count + fs * 0.9f;
            using (var bg = new SKPaint { Color = new SKColor(0, 0, 0, 160) }) c.DrawRect(0, h - band, w, band, bg);
            using var font = new SKFont(SKTypeface.Default, fs);
            using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
            for (var i = 0; i < lines.Count; i++)
            {
                if (i == 0) text.Color = new SKColor(255, 214, 102); else text.Color = SKColors.White;
                c.DrawText(lines[i], fs * 0.6f, h - band + fs * 0.45f + lineH * i + fs, SKTextAlign.Left, font, text);
            }
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 85);
            return data.ToArray();
        }
        catch (Exception) { return null; }
    }
}

/// <summary>SMTP e-mail and a plain HTTP SMS gateway. Both are free to use with the department's own mail server and
/// whatever SMS gateway it already has; nothing here is tied to a paid service.</summary>
public sealed class MessageSender(IHttpClientFactory http) : IMessageSender
{
    public async Task SendEmailAsync(NotifySettings s, IEnumerable<string> to, string subject, string body, IEnumerable<(string Name, byte[] Content)>? attachments, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.FromAddress)) throw new InvalidOperationException("SMTP host and sender address are not configured.");
        using var msg = new MailMessage { From = new MailAddress(s.FromAddress, s.FromName), Subject = subject, Body = body };
        foreach (var t in to) msg.To.Add(t);
        foreach (var (name, content) in attachments ?? []) msg.Attachments.Add(new Attachment(new MemoryStream(content), name, "text/csv"));
        using var smtp = new SmtpClient(s.SmtpHost, s.SmtpPort) { EnableSsl = s.SmtpSsl, DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 30000 };
        if (!string.IsNullOrWhiteSpace(s.SmtpUser)) smtp.Credentials = new NetworkCredential(s.SmtpUser, s.SmtpPassword);
        await smtp.SendMailAsync(msg, ct);
    }

    public async Task SendSmsAsync(NotifySettings s, string phone, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(s.SmsUrl)) throw new InvalidOperationException("SMS gateway URL is not configured.");
        var url = s.SmsUrl.Replace("{phone}", Uri.EscapeDataString(phone)).Replace("{text}", Uri.EscapeDataString(text)).Replace("{key}", Uri.EscapeDataString(s.SmsApiKey ?? ""));
        var client = http.CreateClient("sms");
        using var res = s.SmsMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) ? await client.PostAsync(url, null, ct) : await client.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"SMS gateway answered {(int)res.StatusCode}.");
    }
}
