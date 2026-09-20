using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using CvMaker.Models;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Text;
using MimeKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;

namespace CvMaker.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;
    private const string PADDLE_VENDOR_ID = "YOUR_VENDOR_ID";
    private const string PADDLE_API_KEY = "YOUR_API_KEY";
    private const string PADDLE_PRODUCT_ID = "YOUR_PRODUCT_ID";

    public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> DownloadPdf([FromForm] string name, [FromForm] string surname,
        [FromForm] string email, [FromForm] string phone, [FromForm] string address,
        [FromForm] string skills, [FromForm] string school, [FromForm] string department,
        [FromForm] string aboutText, [FromForm] string[] experienceNames,
        [FromForm] DateTime[] startDates, [FromForm] DateTime[] finishDates,
        [FromForm] string[] experienceDescriptions, [FromForm] IFormFile photo)
    {
        try
        {
            // Null gelen alanlar için güvenli varsayılanlar
            name ??= string.Empty;
            surname ??= string.Empty;
            email = (email ?? string.Empty).Trim();
            phone ??= string.Empty;
            address ??= string.Empty;
            skills ??= string.Empty;
            school ??= string.Empty;
            department ??= string.Empty;
            aboutText ??= string.Empty;

            if (name.Any(char.IsDigit) || surname.Any(char.IsDigit))
            {
                return BadRequest(new { message = "Name ve surname sayı içeremez." });
            }

            experienceNames ??= Array.Empty<string>();
            experienceDescriptions ??= Array.Empty<string>();
            startDates ??= Array.Empty<DateTime>();
            finishDates ??= Array.Empty<DateTime>();

            // PDF için encoding ayarı
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // PDF oluşturma
            byte[] pdfBytes;
            using (var document = new PdfDocument())
            {
                var page = document.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);

                // Renkler
                var primaryColor = XColor.FromArgb(220, 53, 69);    // Kırmızı
                var secondaryColor = XColor.FromArgb(52, 58, 64);   // Koyu gri
                var lightGray = XColor.FromArgb(248, 249, 250);     // Açık gri
                var white = XColors.White;

                // Fontlar
                var nameFont = new XFont("Arial", 28, XFontStyle.Bold);
                var titleFont = new XFont("Arial", 18, XFontStyle.Regular);
                var sectionFont = new XFont("Arial", 14, XFontStyle.Bold);
                var normalFont = new XFont("Arial", 11);
                var smallFont = new XFont("Arial", 10);

                // Sol kenar çubuğu
                gfx.DrawRectangle(new XSolidBrush(secondaryColor), 0, 0, 200, page.Height);

                // Profil fotoğrafı
                double photoSize = 120;
                double photoX = 40;
                double photoY = 40;

                try
                {
                    if (photo != null && photo.Length > 0)
                    {
                        using var stream = new MemoryStream();
                        await photo.CopyToAsync(stream);
                        stream.Position = 0;
                        using var image = XImage.FromStream(stream);

                        var state = gfx.Save();

                        // Dairesel kırpma
                        var path = new XGraphicsPath();
                        path.AddEllipse(photoX, photoY, photoSize, photoSize);
                        gfx.IntersectClip(path);

                        // Telefonla çekilen fotoğraflarda EXIF sebebiyle resim yatık gelebiliyor.
                        // Genişlik yükseklikten büyükse resmi dik hale getirmek için 90 derece döndürüyoruz.
                        if (image.PixelWidth > image.PixelHeight)
                        {
                            // Fotoğrafın merkezine göre saat yönünde 90 derece döndür
                            gfx.TranslateTransform(photoX + photoSize / 2, photoY + photoSize / 2);
                            gfx.RotateTransform(90);
                            gfx.DrawImage(image, -photoSize / 2, -photoSize / 2, photoSize, photoSize);
                        }
                        else
                        {
                            gfx.DrawImage(image, photoX, photoY, photoSize, photoSize);
                        }

                        gfx.Restore(state);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing image");
                }

                // İsim ve Başlık - Daha kompakt yerleşim
                int y = 180;
                gfx.DrawString(name.ToUpper(), new XFont("Arial", 22, XFontStyle.Bold), XBrushes.White, 20, y);
                y += 30;
                gfx.DrawString(surname.ToUpper(), new XFont("Arial", 22, XFontStyle.Bold), XBrushes.White, 20, y);
                y += 25;

                // Department yazısını birden fazla satıra böl
                var departmentFont = new XFont("Arial", 14);
                var departmentText = string.IsNullOrWhiteSpace(department) ? "-" : department;
                var departmentWords = departmentText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in departmentWords)
                {
                    gfx.DrawString(word, departmentFont, new XSolidBrush(lightGray), 20, y);
                    y += 20;
                }
                y += 5;

                // İletişim Bilgileri - Daha sıkı aralıklar
                DrawSectionTitle(gfx, "İLETİŞİM", 20, ref y, sectionFont, white);
                y += 15;
                DrawCompactContactInfo(gfx, "Email", email, 20, ref y, normalFont, white);
                DrawCompactContactInfo(gfx, "Tel", phone, 20, ref y, normalFont, white);
                DrawCompactMultiLineText(gfx, "Adres", address, 20, ref y, 160, normalFont, white);

                // Yetenekler - İki sütunlu düzen
                y += 25;
                DrawSectionTitle(gfx, "YETENEKLER", 20, ref y, sectionFont, white);
                y += 15;
                var skillsList = skills.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                       .ToList();

                if (skillsList.Count == 0)
                {
                    skillsList.Add("Belirtilmedi");
                }

                var midPoint = skillsList.Count / 2 + skillsList.Count % 2;
                
                for (int i = 0; i < midPoint; i++)
                {
                    var leftSkill = skillsList[i];
                    gfx.DrawString("• " + leftSkill, normalFont, XBrushes.White, 25, y);
                    
                    if (i + midPoint < skillsList.Count)
                    {
                        var rightSkill = skillsList[i + midPoint];
                        gfx.DrawString("• " + rightSkill, normalFont, XBrushes.White, 100, y);
                    }
                    y += 15; // Daha sıkı aralık
                }

                // Sağ taraf içeriği
                y = 60;
                var rightX = 240;

                // Hakkımda
                DrawSectionTitle(gfx, "HAKKIMDA", rightX, ref y, sectionFont, primaryColor);
                y += 20;
                DrawMultiLineText(gfx, "", aboutText, rightX, ref y, (int)(page.Width - rightX - 40), normalFont, primaryColor);

                // Eğitim
                y += 40;
                DrawSectionTitle(gfx, "EĞİTİM", rightX, ref y, sectionFont, primaryColor);
                y += 20;
                gfx.DrawString(school, new XFont("Arial", 12, XFontStyle.Bold), XBrushes.Black, rightX, y);
                y += 20;
                gfx.DrawString(department, normalFont, XBrushes.Black, rightX, y);

                // Deneyimler
                if (experienceNames != null && experienceNames.Length > 0)
                {
                    y += 40;
                    DrawSectionTitle(gfx, "PROFESYONEL DENEYİM", rightX, ref y, sectionFont, primaryColor);
                    y += 20;

                    var experienceCount = new[]
                    {
                        experienceNames.Length,
                        experienceDescriptions.Length,
                        startDates.Length,
                        finishDates.Length
                    }.Min();

                    for (int i = 0; i < experienceCount; i++)
                    {
                        // Deneyim kartı arka planı
                        var cardHeight = 100;
                        gfx.DrawRectangle(new XSolidBrush(lightGray), rightX - 10, y - 10, page.Width - rightX - 30, cardHeight);
                        
                        // Deneyim detayları
                        gfx.DrawString(experienceNames[i], new XFont("Arial", 12, XFontStyle.Bold), XBrushes.Black, rightX, y);
                        y += 20;
                        gfx.DrawString($"{startDates[i]:MM/yyyy} - {finishDates[i]:MM/yyyy}", smallFont, XBrushes.Black, rightX, y);
                        y += 20;
                        DrawMultiLineText(gfx, "", experienceDescriptions[i], rightX, ref y, (int)(page.Width - rightX - 60), normalFont, secondaryColor);
                        y += 30;
                    }
                }

                // PDF'i byte array'e çevir
                using (var ms = new MemoryStream())
                {
                    document.Save(ms);
                    pdfBytes = ms.ToArray();
                }
            }

            var copyEmail = _configuration["EmailSettings:CopyEmail"] ?? "birguldiyetlistem@gmail.com";
            var fileName = $"CV_{SafeFilePart(name)}_{SafeFilePart(surname)}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var emailSent = false;
            var recipients = string.Join(", ", new[] { email, copyEmail }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            var emailMessage = string.IsNullOrWhiteSpace(recipients)
                ? "PDF indirildi ancak e-posta adresi boş olduğu için mail gönderilmedi."
                : $"PDF indirildi ancak {recipients} adresine gönderilemedi.";

            try
            {
                _logger.LogInformation("Oluşturulan PDF {Email} adresine gönderiliyor", recipients);
                await SendCvPdfToContactEmail(email, name, surname, pdfBytes, fileName);
                emailSent = true;
                emailMessage = $"Oluşturulan PDF {recipients} adresine gönderildi.";
                _logger.LogInformation("Email with PDF sent successfully to {Email}", recipients);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Email sending failed to {Email}: {Message}", recipients, emailEx.Message);
                if (emailEx is MailKit.Security.AuthenticationException ||
                    emailEx.Message.Contains("5.7.8", StringComparison.OrdinalIgnoreCase) ||
                    emailEx.Message.Contains("uygulama şifresi", StringComparison.OrdinalIgnoreCase))
                {
                    emailMessage =
                        "PDF indirildi ancak mail gönderilemedi. Gmail hesap şifresini kabul etmiyor. " +
                        "birguldiyetlistem@gmail.com için 16 haneli uygulama şifresi oluşturun: https://myaccount.google.com/apppasswords";
                }
            }

            return Json(new
            {
                fileName,
                pdf = Convert.ToBase64String(pdfBytes),
                emailSent,
                emailMessage,
                sentTo = recipients
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF generation failed");
            return StatusCode(500, "PDF oluşturulurken bir hata oluştu. Lütfen tekrar deneyin.");
        }
    }

    private void DrawSectionTitle(XGraphics gfx, string title, int x, ref int y, XFont font, XColor color)
    {
        gfx.DrawString(title, font, new XSolidBrush(color), x, y);
        gfx.DrawLine(new XPen(color, 2), x, y + 5, x + 160, y + 5);
    }

    private void DrawContactInfo(XGraphics gfx, string label, string value, int x, ref int y, XFont font, XColor color)
    {
        gfx.DrawString($"{label}:", font, new XSolidBrush(color), x, y);
        y += 20;
        gfx.DrawString(value, font, new XSolidBrush(color), x + 10, y);
        y += 25;
    }

    private void DrawMultiLineText(XGraphics gfx, string label, string text, int x, ref int y, int maxWidth, XFont font, XColor color)
    {
        if (!string.IsNullOrEmpty(label))
        {
            gfx.DrawString($"{label}:", font, new XSolidBrush(color), x, y);
            y += 20;
        }

        var words = text.Split(' ');
        var line = new StringBuilder();
        var lineHeight = font.Height * 1.2;

        foreach (var word in words)
        {
            var testLine = line.Length == 0 ? word : line + " " + word;
            var size = gfx.MeasureString(testLine, font);

            if (size.Width > maxWidth && line.Length > 0)
            {
                gfx.DrawString(line.ToString(), font, new XSolidBrush(color), x + 10, y);
                y += (int)lineHeight;
                line.Clear();
            }

            if (line.Length == 0)
                line.Append(word);
            else
                line.Append(" ").Append(word);
        }

        if (line.Length > 0)
        {
            gfx.DrawString(line.ToString(), font, new XSolidBrush(color), x + 10, y);
            y += (int)lineHeight;
        }
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult Payment()
    {
        var viewModel = new PaymentViewModel
        {
            VendorId = PADDLE_VENDOR_ID,
            ProductId = PADDLE_PRODUCT_ID
        };
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> WebhookHandler()
    {
        try
        {
            // Webhook verilerini al
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            
            // Paddle'dan gelen webhook verilerini doğrula
            if (!VerifyPaddleWebhook(Request.Form))
            {
                return BadRequest("Invalid webhook signature");
            }

            // Webhook tipine göre işlem yap
            var alertName = Request.Form["alert_name"].ToString();
            switch (alertName)
            {
                case "payment_succeeded":
                    // Ödeme başarılı olduğunda
                    var paymentId = Request.Form["payment_id"].ToString();
                    var amount = Request.Form["amount"].ToString();
                    var email = Request.Form["email"].ToString();
                    
                    // Burada ödeme başarılı olduktan sonra yapılacak işlemleri yazın
                    _logger.LogInformation($"Payment succeeded: {paymentId} - Amount: {amount} - Email: {email}");
                    break;

                case "payment_refunded":
                    // İade durumunda
                    _logger.LogInformation("Payment refunded");
                    break;
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return StatusCode(500);
        }
    }

    private bool VerifyPaddleWebhook(IFormCollection form)
    {
        try
        {
            // Paddle public key'i
            var publicKey = "YOUR_PUBLIC_KEY";
            
            // Signature'ı al
            var signature = form["p_signature"].ToString();

            // Diğer tüm alanları sırala
            var fields = form.Where(x => x.Key != "p_signature")
                            .OrderBy(x => x.Key)
                            .ToDictionary(x => x.Key, x => x.Value.ToString());

            // Serialize edilmiş string oluştur
            var serialized = string.Join(":", fields.Select(x => $"{x.Key}|{x.Value}"));

            // PHP serialize formatına benzer şekilde dönüştür
            serialized = $"a:{fields.Count}:{{{serialized}}}";

            // Verify signature using PHP-serialize format and public key
            // Not: Gerçek uygulamada burada PHPSerialize ve RSA doğrulama kullanılmalıdır
            return true; // Bu örnek için her zaman true döndürüyoruz
        }
        catch
        {
            return false;
        }
    }

    // Yeni kompakt yardımcı metodlar
    private void DrawCompactContactInfo(XGraphics gfx, string label, string value, int x, ref int y, XFont font, XColor color)
    {
        gfx.DrawString($"{label}:", font, new XSolidBrush(color), x, y);
        gfx.DrawString(value, font, new XSolidBrush(color), x + 10, y + 15);
        y += 30; // Daha sıkı aralık
    }

    private void DrawCompactMultiLineText(XGraphics gfx, string label, string text, int x, ref int y, int maxWidth, XFont font, XColor color)
    {
        if (!string.IsNullOrEmpty(label))
        {
            gfx.DrawString($"{label}:", font, new XSolidBrush(color), x, y);
            y += 15;
        }

        var words = text.Split(' ');
        var line = new StringBuilder();
        var lineHeight = font.Height * 1.1; // Daha sıkı satır aralığı

        foreach (var word in words)
        {
            var testLine = line.Length == 0 ? word : line + " " + word;
            var size = gfx.MeasureString(testLine, font);

            if (size.Width > maxWidth && line.Length > 0)
            {
                gfx.DrawString(line.ToString(), font, new XSolidBrush(color), x + 10, y);
                y += (int)lineHeight;
                line.Clear();
            }

            if (line.Length == 0)
                line.Append(word);
            else
                line.Append(" ").Append(word);
        }

        if (line.Length > 0)
        {
            gfx.DrawString(line.ToString(), font, new XSolidBrush(color), x + 10, y);
            y += (int)lineHeight;
        }
    }

    private async Task SendCvPdfToContactEmail(string userEmail, string name, string surname, byte[] pdfBytes, string fileName)
    {
        userEmail = (userEmail ?? string.Empty).Trim();
        var emailSettings = _configuration.GetSection("EmailSettings");
        var copyEmail = (emailSettings["CopyEmail"] ?? "birguldiyetlistem@gmail.com").Trim();

        var smtpServer = emailSettings["SmtpServer"] ?? "smtp.gmail.com";
        var smtpPort = int.TryParse(emailSettings["SmtpPort"], out var port) ? port : 587;
        var smtpUsername = emailSettings["SmtpUsername"]?.Trim();
        var smtpPassword = emailSettings["SmtpPassword"]?.Replace(" ", string.Empty);
        var senderName = emailSettings["SenderName"] ?? "CV Maker";

        if (string.IsNullOrWhiteSpace(smtpUsername) || string.IsNullOrWhiteSpace(smtpPassword))
        {
            throw new InvalidOperationException("Gmail SMTP kullanıcı adı veya uygulama şifresi eksik.");
        }

        var displayName = $"{name} {surname}".Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = userEmail;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, smtpUsername));
        AddUniqueRecipient(message.To, userEmail, displayName);
        AddUniqueRecipient(message.To, copyEmail, "CV Kopya");

        if (message.To.Count == 0)
        {
            throw new InvalidOperationException("Gönderilecek geçerli e-posta adresi yok.");
        }

        message.Subject = $"CV (son form verileri) - {displayName} - {DateTime.Now:dd.MM.yyyy HH:mm}";

        var builder = new BodyBuilder
        {
            TextBody =
                $"Merhaba {displayName},\n\n" +
                "Bu e-postanın ekinde yalnızca formun son doldurulan verilerinden oluşturulan tek PDF vardır.\n\n" +
                $"Dosya: {fileName}\n" +
                $"Tarih: {DateTime.Now:dd/MM/yyyy HH:mm}\n\n" +
                "CV Maker"
        };

        builder.Attachments.Clear();
        builder.LinkedResources.Clear();

        if (pdfBytes is { Length: > 0 })
        {
            var attachment = builder.Attachments.Add(fileName, pdfBytes, new ContentType("application", "pdf"));
            attachment.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment)
            {
                FileName = fileName
            };
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpServer, smtpPort, MailKit.Security.SecureSocketOptions.StartTls);
        client.AuthenticationMechanisms.Remove("XOAUTH2");
        await client.AuthenticateAsync(smtpUsername, smtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static void AddUniqueRecipient(InternetAddressList list, string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email) || !MailboxAddress.TryParse(email.Trim(), out var address))
        {
            return;
        }

        if (list.Mailboxes.Any(x => string.Equals(x.Address, address.Address, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        address.Name = string.IsNullOrWhiteSpace(displayName) ? address.Address : displayName;
        list.Add(address);
    }

    private static string SafeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "CV";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "CV" : cleaned;
    }
}