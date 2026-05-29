using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace WorkSupport360.API.Services;

public interface IEmailService
{
    Task SendWelcomeEmailAsync(string toEmail, string toName, string method);
    Task SendVerificationEmailAsync(string toEmail, string toName, string token);
    Task SendMeetingInviteAsync(string toEmail, string toName, string meetingLink,
        DateTime scheduledAt, string platform, string otherPartyName,
        decimal agreedRate, string budgetType, string currency);
    Task SendTimesheetApprovalAsync(string toEmail, string toName, string projectName,
        decimal hours, decimal amount, bool approved, string? reason = null);
    Task SendPaymentInstructionsAsync(string toEmail, string toName,
        string invoiceNumber, decimal amount, string currency,
        bool isGst, decimal gstAmount, string? bankDetails);
    Task SendPaymentConfirmationAsync(string toEmail, string toName,
        decimal amount, string currency, string invoiceNumber);
    Task SendPayoutNotificationAsync(string toEmail, string toName,
        decimal amount, string currency, string invoiceNumber, string? txnId);
    Task SendPaymentReminderAsync(string toEmail, string toName,
        string invoiceNumber, decimal amount, string currency, DateTime dueDate, int reminderNum);
    Task SendRequestReceivedAsync(string toEmail, string toName,
        string freelancerAlias, string sessionType, DateTime preferredDate);
    Task SendFreelancerAvailabilityCheckAsync(string toEmail, string toName,
        string clientCompany, DateTime proposedTime, string platform, string mobileNumber);
    Task SendProjectStartNotificationAsync(string toEmail, string toName,
        string projectName, string role, DateTime startDate, string? notes);
    Task SendProjectStatusChangeAsync(string toEmail, string toName,
        string projectName, string newStatus, string? reason);
    Task SendSubscriptionConfirmationAsync(string toEmail, string toName,
        string planName, decimal amount, string currency, string billingCycle, DateTime endDate);
    Task SendQuickSupportConfirmationAsync(string toEmail, string toName,
        string freelancerAlias, decimal rate, string currency, Guid sessionId);
    Task SendContactUsEmailAsync(string fromName, string fromEmail, string reason, string message);
}

public partial class EmailService(IConfiguration cfg, ILogger<EmailService> log) : IEmailService
{
    private readonly string _host     = cfg["Smtp:Host"]     ?? "smtpout.secureserver.net";
    private readonly int    _port     = int.Parse(cfg["Smtp:Port"] ?? "587");
    private readonly string _user     = cfg["Smtp:User"]     ?? "";
    private readonly string _pass     = cfg["Smtp:Password"] ?? "";
    private readonly string _from     = cfg["Smtp:From"]     ?? "help@worksupport360.com";
    private readonly string _fromName = cfg["Smtp:FromName"] ?? "WorkSupport360";
    private readonly string _appUrl   = cfg["App:ApiUrl"]    ?? "http://localhost:5000";
    private readonly string _feUrl    = cfg["App:FrontendUrl"] ?? "http://localhost:3000";
    private readonly string _whatsapp = cfg["App:WhatsApp"]  ?? "9441363687";
    private readonly string _adminPhone = cfg["App:AdminPhone"] ?? "9441363687";

    private static string Layout(string title, string body) => $$"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  body{font-family:-apple-system,sans-serif;background:#f8f9ff;margin:0;padding:24px;}
  .card{background:#fff;border-radius:16px;padding:32px;max-width:560px;margin:0 auto;border:1px solid #e5e7eb;}
  .logo{color:#1a1a2e;font-weight:900;font-size:20px;margin-bottom:24px;}
  .logo span{color:#f97316;}
  h2{color:#1a1a2e;font-size:18px;margin:0 0 12px;}
  p{color:#374151;line-height:1.6;margin:0 0 12px;}
  .btn{display:inline-block;background:#1a1a2e;color:#fff;text-decoration:none;padding:12px 28px;border-radius:10px;font-weight:700;font-size:14px;margin:12px 0;}
  .btn-orange{background:linear-gradient(135deg,#f97316,#ea580c);}
  .info-table{width:100%;background:#f8f9ff;border-radius:12px;padding:16px;margin:16px 0;border-collapse:collapse;}
  .info-table td{padding:6px 0;color:#374151;font-size:14px;}
  .info-table td:first-child{color:#6b7280;width:40%;}
  .badge{display:inline-block;background:#1a1a2e;color:#f97316;border-radius:8px;padding:4px 12px;font-size:13px;font-weight:700;}
  .alert{background:#fff7ed;border-left:4px solid #f97316;padding:12px 16px;border-radius:8px;margin:12px 0;font-size:13px;color:#9a3412;}
  .success{background:#f0fdf4;border-left:4px solid #16a34a;padding:12px 16px;border-radius:8px;margin:12px 0;font-size:13px;color:#15803d;}
  .footer{text-align:center;font-size:11px;color:#9ca3af;margin-top:20px;}
  hr{border:none;border-top:1px solid #f3f4f6;margin:20px 0;}
</style>
</head>

<body>
<div class="card">

  <div class="logo">
    Work<span>Support</span>360
  </div>

  <h2>{{title}}</h2>

  {{body}}

  <hr/>

  <p class="footer">
    © 2025 WorkSupport360 · help@worksupport360.com · WhatsApp: +91-9441363687
    <br/>
    <a href="https://worksupport360.com" style="color:#1a1a2e">
      worksupport360.com
    </a>
  </p>

</div>
</body>
</html>
""";

    public async Task SendWelcomeEmailAsync(string toEmail, string toName, string method)
    {
        var body = Layout("Welcome to WorkSupport360! ", $"""
            <p>Hi {toName},</p>
            <p>You've successfully joined WorkSupport360 via <span class="badge">{method}</span>.</p>
            <p>WorkSupport360 connects you with top tech experts from MNC companies — all working under a privacy alias. Your employer is never notified.</p>
            <a href="{_feUrl}" class="btn btn-orange">Get started →</a>
            <div class="alert">📧 Please verify your email to complete activation.</div>
            <p style="font-size:12px;color:#9ca3af;">Questions? WhatsApp: +91-{_whatsapp}</p>
            """);
        await SendAsync(toEmail, toName, "Welcome to WorkSupport360! ", body);
    }

    public async Task SendVerificationEmailAsync(string toEmail, string toName, string token)
    {
        var link = $"{_appUrl}/api/auth/verify-email?token={token}";
        var body = Layout("Verify your email ✉", $"""
            <p>Hi {toName},</p>
            <p>Click the button below to verify your email address and activate your WorkSupport360 account.</p>
            <a href="{link}" class="btn btn-orange"> Verify my email</a>
            <p style="font-size:12px;color:#9ca3af;margin-top:16px">
              This link expires in 24 hours. If you did not register, ignore this email.<br/>
              Or copy this link: {link}
            </p>
            """);
        await SendAsync(toEmail, toName, "Verify your WorkSupport360 email", body);
    }

    public async Task SendMeetingInviteAsync(string toEmail, string toName, string meetingLink,
        DateTime scheduledAt, string platform, string otherPartyName,
        decimal agreedRate, string budgetType, string currency)
    {
        var rateStr = budgetType == "fixed" ? $"{currency} {agreedRate} fixed" : $"{currency} {agreedRate}/hr";
        var body = Layout($"Meeting confirmed — {scheduledAt:ddd, MMM d 'at' h:mm tt} UTC ", $"""
            <p>Hi {toName},</p>
            <p>Your <strong>{platform}</strong> meeting has been scheduled by WorkSupport360 admin.</p>
            <table class="info-table">
              <tr><td>Date &amp; Time</td><td><strong>{scheduledAt:dddd, MMMM d, yyyy 'at' h:mm tt} UTC</strong></td></tr>
              <tr><td>With</td><td><strong>{otherPartyName}</strong></td></tr>
              <tr><td>Platform</td><td><span class="badge">{platform}</span></td></tr>
              <tr><td>Rate</td><td><strong>{rateStr}</strong></td></tr>
            </table>
            <a href="{meetingLink}" class="btn btn-orange">🎥 Join {platform} meeting →</a>
            <div class="alert">⚠️ If you cannot attend, contact us at help@worksupport360.com or WhatsApp +91-{_whatsapp} at least 2 hours in advance.</div>
            """);
        await SendAsync(toEmail, toName, $"Meeting confirmed — {scheduledAt:MMM d 'at' h:mm tt} UTC", body);
    }

    public async Task SendTimesheetApprovalAsync(string toEmail, string toName,
        string projectName, decimal hours, decimal amount, bool approved, string? reason = null)
    {
        var subject = approved ? "Timesheet approved " : "Timesheet rejected ";
        var body = Layout(approved ? "Timesheet approved" : "Timesheet requires attention", $"""
            <p>Hi {toName},</p>
            <p>Your timesheet for <strong>{projectName}</strong> has been
              <strong style="color:{(approved ? "#16a34a" : "#dc2626")}">{(approved ? "approved" : "rejected")}</strong>.</p>
            <table class="info-table">
              <tr><td>Project</td><td>{projectName}</td></tr>
              <tr><td>Hours</td><td>{hours}h</td></tr>
              <tr><td>Amount</td><td>USD {amount:N2}</td></tr>
            </table>
            {(approved
                ? "<div class=\"success\"> An invoice has been generated. Payment will be processed within 3 business days.</div>"
                : $"<div class=\"alert\">Reason: {reason ?? "Please contact admin for details."}<br/>Please correct and resubmit.</div>")}
            """);
        await SendAsync(toEmail, toName, subject, body);
    }

    public async Task SendPaymentInstructionsAsync(string toEmail, string toName,
        string invoiceNumber, decimal amount, string currency,
        bool isGst, decimal gstAmount, string? bankDetails)
    {
        var gstLine = isGst ? $"<tr><td>GST (18%)</td><td><strong>{currency} {gstAmount:N2}</strong></td></tr>" : "";
        var body = Layout($"Invoice {invoiceNumber} — Payment Instructions ", $"""
            <p>Hi {toName},</p>
            <p>Your invoice is ready. Please make the payment using the details below.</p>
            <table class="info-table">
              <tr><td>Invoice</td><td><strong>{invoiceNumber}</strong></td></tr>
              <tr><td>Subtotal</td><td>{currency} {(amount - gstAmount):N2}</td></tr>
              {gstLine}
              <tr><td>Total Due</td><td><strong style="font-size:18px;color:#1a1a2e">{currency} {amount:N2}</strong></td></tr>
            </table>
            {(string.IsNullOrEmpty(bankDetails) ? "" : $"<div class=\"alert\"><strong>Payment Details:</strong><br/>{bankDetails.Replace("\n","<br/>")}</div>")}
            <div class="alert">⚠️ Please include invoice number <strong>{invoiceNumber}</strong> as payment reference.</div>
            <p>Once payment is done, reply to this email with transaction ID and we'll confirm within 24 hours.</p>
            <p>Questions? WhatsApp: <strong>+91-{_whatsapp}</strong></p>
            """);
        await SendAsync(toEmail, toName, $"Invoice {invoiceNumber} — Payment Due {currency} {amount:N2}", body);
    }

    public async Task SendPaymentConfirmationAsync(string toEmail, string toName,
        decimal amount, string currency, string invoiceNumber)
    {
        var body = Layout("Payment received ", $"""
            <p>Hi {toName},</p>
            <div class="success">✅ Your payment of <strong>{currency} {amount:N2}</strong> for invoice <strong>{invoiceNumber}</strong> has been received.</div>
            <p>Project work will continue as scheduled. Thank you!</p>
            """);
        await SendAsync(toEmail, toName, $"Payment confirmed — {currency} {amount:N2}", body);
    }

    public async Task SendPayoutNotificationAsync(string toEmail, string toName,
        decimal amount, string currency, string invoiceNumber, string? txnId)
    {
        var body = Layout("Payout processed 💰", $"""
            <p>Hi {toName},</p>
            <div class="success">✅ Your payout of <strong>{currency} {amount:N2}</strong> has been processed!</div>
            <table class="info-table">
              <tr><td>Invoice</td><td>{invoiceNumber}</td></tr>
              <tr><td>Amount</td><td><strong>{currency} {amount:N2}</strong></td></tr>
              {(string.IsNullOrEmpty(txnId) ? "" : $"<tr><td>Transaction ID</td><td>{txnId}</td></tr>")}
            </table>
            <p>Funds will appear in your registered bank account within 1-2 business days.</p>
            """);
        await SendAsync(toEmail, toName, $"Payout processed — {currency} {amount:N2}", body);
    }

    public async Task SendPaymentReminderAsync(string toEmail, string toName,
        string invoiceNumber, decimal amount, string currency, DateTime dueDate, int reminderNum)
    {
        var overdue = dueDate < DateTime.UtcNow;
        var subject = overdue ? $"⚠️ OVERDUE: Invoice {invoiceNumber}" : $"Payment reminder #{reminderNum}: Invoice {invoiceNumber}";
        var body = Layout(overdue ? "Invoice Overdue ⚠️" : $"Payment Reminder #{reminderNum}", $"""
            <p>Hi {toName},</p>
            <div class="alert">{(overdue ? "⚠️ Your invoice is OVERDUE." : $"This is reminder #{reminderNum} for your pending invoice.")}
            Please make the payment at the earliest to avoid project disruption.</div>
            <table class="info-table">
              <tr><td>Invoice</td><td><strong>{invoiceNumber}</strong></td></tr>
              <tr><td>Amount Due</td><td><strong style="color:#dc2626">{currency} {amount:N2}</strong></td></tr>
              <tr><td>Due Date</td><td>{dueDate:MMMM d, yyyy}</td></tr>
            </table>
            <p>To pay, contact us at <strong>help@worksupport360.com</strong> or WhatsApp <strong>+91-{_whatsapp}</strong></p>
            """);
        await SendAsync(toEmail, toName, subject, body);
    }

    public async Task SendRequestReceivedAsync(string toEmail, string toName,
        string freelancerAlias, string sessionType, DateTime preferredDate)
    {
        var body = Layout("Request received 📥", $"""
            <p>Hi {toName},</p>
            <p>Your request for a <strong>{sessionType.Replace("_", " ")}</strong> with <strong>{freelancerAlias}</strong> has been received.</p>
            <table class="info-table">
              <tr><td>Preferred date</td><td>{preferredDate:MMMM d, yyyy 'at' h:mm tt}</td></tr>
            </table>
            <p>Our admin team will coordinate timing and <strong>confirm your meeting within 4 hours</strong>. You'll receive a calendar invite once confirmed.</p>
            <p>For urgent help, WhatsApp: <strong>+91-{_whatsapp}</strong></p>
            """);
        await SendAsync(toEmail, toName, "Request received — we'll confirm soon", body);
    }

    public async Task SendFreelancerAvailabilityCheckAsync(string toEmail, string toName,
        string clientCompany, DateTime proposedTime, string platform, string mobileNumber)
    {
        var body = Layout("Availability check — action needed ⏰", $"""
            <p>Hi {toName},</p>
            <p>A client (<strong>{clientCompany}</strong>) has requested a meeting with you. Please confirm your availability.</p>
            <table class="info-table">
              <tr><td>Proposed time</td><td><strong>{proposedTime:dddd, MMMM d, yyyy 'at' h:mm tt} UTC</strong></td></tr>
              <tr><td>Platform</td><td>{platform}</td></tr>
            </table>
            <div class="alert">⚠️ Please reply to this email or WhatsApp <strong>+91-{_whatsapp}</strong> within 2 hours to confirm or suggest an alternate time.</div>
            <p>Admin will also contact you on: <strong>+91-{mobileNumber}</strong></p>
            """);
        await SendAsync(toEmail, toName, $"Availability check — {proposedTime:MMM d 'at' h:mm tt}", body);
    }

    public async Task SendProjectStartNotificationAsync(string toEmail, string toName,
        string projectName, string role, DateTime startDate, string? notes)
    {
        var body = Layout("Project starting! 🚀", $"""
            <p>Hi {toName},</p>
            <p>Your project <strong>{projectName}</strong> is now starting.</p>
            <table class="info-table">
              <tr><td>Your role</td><td><span class="badge">{role}</span></td></tr>
              <tr><td>Start date</td><td><strong>{startDate:MMMM d, yyyy}</strong></td></tr>
            </table>
            {(string.IsNullOrEmpty(notes) ? "" : $"<div class=\"alert\">📝 Note: {notes}</div>")}
            <div class="success">✅ Project is now active. You can track progress in your dashboard.</div>
            <a href="{_feUrl}" class="btn btn-orange">Go to dashboard →</a>
            """);
        await SendAsync(toEmail, toName, $"Project starting — {projectName}", body);
    }

    public async Task SendProjectStatusChangeAsync(string toEmail, string toName,
        string projectName, string newStatus, string? reason)
    {
        var emoji = newStatus switch { "paused" => "⏸️", "completed" => "✅", "cancelled" => "❌", _ => "🔄" };
        var body = Layout($"Project {newStatus} {emoji}", $"""
            <p>Hi {toName},</p>
            <p>The project <strong>{projectName}</strong> status has changed to <strong>{newStatus}</strong>.</p>
            {(string.IsNullOrEmpty(reason) ? "" : $"<div class=\"alert\">Reason: {reason}</div>")}
            <a href="{_feUrl}" class="btn btn-orange">View project →</a>
            """);
        await SendAsync(toEmail, toName, $"Project {newStatus} — {projectName}", body);
    }

    public async Task SendSubscriptionConfirmationAsync(string toEmail, string toName,
        string planName, decimal amount, string currency, string billingCycle, DateTime endDate)
    {
        var body = Layout($"Subscription activated — {planName} ✅", $"""
            <p>Hi {toName}, your <strong>{planName}</strong> plan is now active.</p>
            <table class="info-table">
              <tr><td>Plan</td><td>{planName}</td></tr>
              <tr><td>Billing</td><td style="text-transform:capitalize">{billingCycle}</td></tr>
              <tr><td>Amount paid</td><td>{currency} {amount:N2}</td></tr>
              <tr><td>Valid until</td><td>{endDate:MMMM d, yyyy}</td></tr>
            </table>
            <a href="{_feUrl}/client/browse" class="btn btn-orange">Browse experts →</a>
            """);
        await SendAsync(toEmail, toName, $"Subscription activated — {planName}", body);
    }

    public async Task SendQuickSupportConfirmationAsync(string toEmail, string toName,
        string freelancerAlias, decimal rate, string currency, Guid sessionId)
    {
        var body = Layout("Quick support session confirmed ⚡", $"""
            <p>Hi {toName},</p>
            <div class="success">⚡ Your quick support session is confirmed!</div>
            <table class="info-table">
              <tr><td>Expert</td><td><strong>{freelancerAlias}</strong></td></tr>
              <tr><td>Rate</td><td>{currency} {rate}/hr</td></tr>
              <tr><td>Session ID</td><td style="font-size:12px;color:#9ca3af">{sessionId}</td></tr>
            </table>
            <p>The expert will contact you within <strong>30 minutes</strong>.</p>
            <p>Need help? WhatsApp: <strong>+91-{_whatsapp}</strong></p>
            """);
        await SendAsync(toEmail, toName, "Quick support session confirmed ⚡", body);
    }

    public async Task SendContactUsEmailAsync(string fromName, string fromEmail, string reason, string message)
    {
        // Send to admin
        var body = Layout("New contact form submission 📬", $"""
            <p>A new contact form was submitted on WorkSupport360.</p>
            <table class="info-table">
              <tr><td>Name</td><td><strong>{fromName}</strong></td></tr>
              <tr><td>Email</td><td>{fromEmail}</td></tr>
              <tr><td>Reason</td><td>{reason}</td></tr>
            </table>
            <div class="alert"><strong>Message:</strong><br/>{message}</div>
            """);
        await SendAsync("help@worksupport360.com", "WorkSupport360 Admin", "New contact form submission", body);

        // Auto-reply to user
        var reply = Layout("We received your message ✅", $"""
            <p>Hi {fromName},</p>
            <p>Thank you for contacting WorkSupport360! We've received your message and will respond within <strong>4 business hours</strong>.</p>
            <div class="success">✅ Message received successfully</div>
            <p>For urgent help: WhatsApp <strong>+91-{_whatsapp}</strong></p>
            """);
        await SendAsync(fromEmail, fromName, "We received your message — WorkSupport360", reply);
    }

    internal async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        if (string.IsNullOrEmpty(_user))
        {
            log.LogWarning("SMTP not configured. Would send to {Email}: {Subject}", toEmail, subject);
            return;
        }
        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(_fromName, _from));
            msg.To.Add(new MailboxAddress(toName, toEmail));
            msg.Subject = subject;
            msg.Body = new TextPart("html") { Text = htmlBody };
            using var client = new SmtpClient();
            await client.ConnectAsync(_host, _port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_user, _pass);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            log.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Email failed to {Email}: {Subject}", toEmail, subject);
        }
    }
}
