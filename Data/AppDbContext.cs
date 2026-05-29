using Microsoft.EntityFrameworkCore;
using WorkSupport360.API.Models;

namespace WorkSupport360.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User>                Users                => Set<User>();
    public DbSet<RefreshToken>        RefreshTokens        => Set<RefreshToken>();
    public DbSet<AttendanceLog>       AttendanceLogs       => Set<AttendanceLog>();
    public DbSet<Freelancer>          Freelancers          => Set<Freelancer>();
    public DbSet<FreelancerSkill>     FreelancerSkills     => Set<FreelancerSkill>();
    public DbSet<FreelancerBadge>     FreelancerBadges     => Set<FreelancerBadge>();
    public DbSet<WeeklyAvailability>  WeeklyAvailabilities => Set<WeeklyAvailability>();
    public DbSet<Client>              Clients              => Set<Client>();
    public DbSet<DemoRequest>         DemoRequests         => Set<DemoRequest>();
    public DbSet<Meeting>             Meetings             => Set<Meeting>();
    public DbSet<Project>             Projects             => Set<Project>();
    public DbSet<ProjectStatusLog>    ProjectStatusLogs    => Set<ProjectStatusLog>();
    public DbSet<ProjectSkill>        ProjectSkills        => Set<ProjectSkill>();
    public DbSet<Milestone>           Milestones           => Set<Milestone>();
    public DbSet<Timesheet>           Timesheets           => Set<Timesheet>();
    public DbSet<TimesheetEntry>      TimesheetEntries     => Set<TimesheetEntry>();
    public DbSet<Invoice>             Invoices             => Set<Invoice>();
    public DbSet<InvoiceLineItem>     InvoiceLineItems     => Set<InvoiceLineItem>();
    public DbSet<InvoiceTimesheet>    InvoiceTimesheets    => Set<InvoiceTimesheet>();
    public DbSet<Payment>             Payments             => Set<Payment>();
    public DbSet<Notification>        Notifications        => Set<Notification>();
    public DbSet<DailyStandup>        DailyStandups        => Set<DailyStandup>();
    public DbSet<Review>              Reviews              => Set<Review>();
    public DbSet<SubscriptionPlan>    SubscriptionPlans    => Set<SubscriptionPlan>();
    public DbSet<ClientSubscription>  ClientSubscriptions  => Set<ClientSubscription>();
    public DbSet<PlatformEarning>     PlatformEarnings     => Set<PlatformEarning>();
    public DbSet<QuickSupportSession> QuickSupportSessions => Set<QuickSupportSession>();
    public DbSet<SupportTicket>       SupportTickets       => Set<SupportTicket>();
    public DbSet<SupportMessage>      SupportMessages      => Set<SupportMessage>();
    public DbSet<Faq>                 Faqs                 => Set<Faq>();
    public DbSet<ContactSubmission>   ContactSubmissions   => Set<ContactSubmission>();
    public DbSet<FeaturedBoost>       FeaturedBoosts       => Set<FeaturedBoost>();
    public DbSet<PlatformSetting>     PlatformSettings     => Set<PlatformSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e => {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.GoogleId);
            e.HasOne(u => u.FreelancerProfile).WithOne(f => f.User).HasForeignKey<Freelancer>(f => f.UserId);
            e.HasOne(u => u.ClientProfile).WithOne(c => c.User).HasForeignKey<Client>(c => c.UserId);
        });

        b.Entity<RefreshToken>(e => e.HasOne(r => r.User).WithMany(u => u.RefreshTokens).HasForeignKey(r => r.UserId));
        b.Entity<AttendanceLog>(e => e.HasOne(a => a.User).WithMany(u => u.AttendanceLogs).HasForeignKey(a => a.UserId));

        b.Entity<Freelancer>(e => {
            e.Property(f => f.HourlyRate).HasPrecision(10, 2);
            e.Property(f => f.Rating).HasPrecision(3, 2);
            e.Property(f => f.TotalEarned).HasPrecision(14, 2);
            e.Property(f => f.PendingAmount).HasPrecision(14, 2);
        });
        b.Entity<FreelancerSkill>(e => e.HasOne(s => s.Freelancer).WithMany(f => f.Skills).HasForeignKey(s => s.FreelancerId));
        b.Entity<FreelancerBadge>(e => e.HasOne(bg => bg.Freelancer).WithMany(f => f.Badges).HasForeignKey(bg => bg.FreelancerId));
        b.Entity<WeeklyAvailability>(e => {
            e.HasOne(a => a.Freelancer).WithMany(f => f.Availability).HasForeignKey(a => a.FreelancerId);
            e.HasIndex(a => new { a.FreelancerId, a.DayOfWeek }).IsUnique();
        });

        b.Entity<DemoRequest>(e => {
            e.Property(r => r.BudgetMin).HasPrecision(10, 2);
            e.Property(r => r.BudgetMax).HasPrecision(10, 2);
            e.Property(r => r.FinalBudget).HasPrecision(14, 2);
            e.HasOne(r => r.Client).WithMany(c => c.Requests).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Freelancer).WithMany(f => f.Requests).HasForeignKey(r => r.FreelancerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Meeting).WithOne(m => m.Request).HasForeignKey<Meeting>(m => m.RequestId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Meeting>(e => {
            e.Property(m => m.AgreedRate).HasPrecision(10, 2);
            e.HasOne(m => m.Client).WithMany().HasForeignKey(m => m.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Freelancer).WithMany(f => f.Meetings).HasForeignKey(m => m.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Project>(e => {
            e.Property(p => p.HourlyRate).HasPrecision(10, 2);
            e.Property(p => p.TotalBudget).HasPrecision(14, 2);
            e.Property(p => p.TotalPaid).HasPrecision(14, 2);
            e.Property(p => p.PendingAmount).HasPrecision(14, 2);
            e.Property(p => p.EscrowBalance).HasPrecision(14, 2);
            e.HasOne(p => p.Client).WithMany(c => c.Projects).HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Freelancer).WithMany(f => f.Projects).HasForeignKey(p => p.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProjectStatusLog>(e => e.HasOne(l => l.Project).WithMany(p => p.StatusLogs).HasForeignKey(l => l.ProjectId));
        b.Entity<ProjectSkill>(e => e.HasOne(s => s.Project).WithMany(p => p.Skills).HasForeignKey(s => s.ProjectId));
        b.Entity<Milestone>(e => {
            e.Property(m => m.Amount).HasPrecision(14, 2);
            e.HasOne(m => m.Project).WithMany(p => p.Milestones).HasForeignKey(m => m.ProjectId);
        });

        b.Entity<Timesheet>(e => {
            e.Property(t => t.TotalHours).HasPrecision(8, 2);
            e.Property(t => t.TotalAmount).HasPrecision(14, 2);
            e.HasOne(t => t.Project).WithMany(p => p.Timesheets).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Freelancer).WithMany(f => f.Timesheets).HasForeignKey(t => t.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TimesheetEntry>(e => {
            e.Property(te => te.Hours).HasPrecision(6, 2);
            e.HasOne(te => te.Timesheet).WithMany(t => t.Entries).HasForeignKey(te => te.TimesheetId);
        });

        b.Entity<Invoice>(e => {
            e.HasIndex(i => i.InvoiceNumber).IsUnique();
            e.Property(i => i.Subtotal).HasPrecision(14, 2);
            e.Property(i => i.Commission).HasPrecision(10, 2);
            e.Property(i => i.GstAmount).HasPrecision(10, 2);
            e.Property(i => i.Total).HasPrecision(14, 2);
            e.Property(i => i.FreelancerAmount).HasPrecision(14, 2);
            e.HasOne(i => i.Client).WithMany(c => c.Invoices).HasForeignKey(i => i.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Freelancer).WithMany(f => f.Invoices).HasForeignKey(i => i.FreelancerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Project).WithMany(p => p.Invoices).HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Payment).WithOne(p => p.Invoice).HasForeignKey<Payment>(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InvoiceLineItem>(e => {
            e.Property(li => li.Hours).HasPrecision(8, 2);
            e.Property(li => li.Rate).HasPrecision(10, 2);
            e.Property(li => li.Amount).HasPrecision(14, 2);
            e.HasOne(li => li.Invoice).WithMany(i => i.LineItems).HasForeignKey(li => li.InvoiceId);
        });
        b.Entity<InvoiceTimesheet>(e => {
            e.HasKey(it => new { it.InvoiceId, it.TimesheetId });
            e.HasOne(it => it.Invoice).WithMany(i => i.InvoiceTimesheets).HasForeignKey(it => it.InvoiceId);
            e.HasOne(it => it.Timesheet).WithMany(t => t.Invoices).HasForeignKey(it => it.TimesheetId);
        });

        b.Entity<Payment>(e => {
            e.Property(p => p.Amount).HasPrecision(14, 2);
            e.Property(p => p.Commission).HasPrecision(10, 2);
            e.Property(p => p.GstAmount).HasPrecision(10, 2);
            e.Property(p => p.FreelancerAmount).HasPrecision(14, 2);
            e.HasOne(p => p.Client).WithMany(c => c.Payments).HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Freelancer).WithMany(f => f.Payments).HasForeignKey(p => p.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e => e.HasOne(n => n.User).WithMany(u => u.Notifications).HasForeignKey(n => n.UserId));
        b.Entity<DailyStandup>(e => {
            e.Property(s => s.HoursWorked).HasPrecision(6, 2);
            e.HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Freelancer).WithMany(f => f.Standups).HasForeignKey(s => s.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Review>(e => {
            e.HasOne(r => r.Client).WithMany(c => c.Reviews).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Freelancer).WithMany(f => f.ReviewsGiven).HasForeignKey(r => r.FreelancerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Project).WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SubscriptionPlan>(e => { e.Property(p => p.PriceMonthly).HasPrecision(10, 2); e.Property(p => p.PriceYearly).HasPrecision(10, 2); e.Property(p => p.OverageRatePerHr).HasPrecision(10, 2); });
        b.Entity<ClientSubscription>(e => { e.Property(s => s.AmountPaid).HasPrecision(10, 2); e.HasOne(s => s.Client).WithMany().HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Restrict); e.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<PlatformEarning>(e => e.Property(p => p.Amount).HasPrecision(14, 2));
        b.Entity<QuickSupportSession>(e => { e.Property(q => q.Rate).HasPrecision(10, 2); e.Property(q => q.PlatformFee).HasPrecision(10, 2); e.HasOne(q => q.Client).WithMany().HasForeignKey(q => q.ClientId).OnDelete(DeleteBehavior.Restrict); e.HasOne(q => q.Freelancer).WithMany().HasForeignKey(q => q.FreelancerId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<SupportTicket>(e => e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId));
        b.Entity<SupportMessage>(e => e.HasOne(m => m.Ticket).WithMany(t => t.Messages).HasForeignKey(m => m.TicketId));
        b.Entity<Faq>(e => e.HasIndex(f => new { f.Category, f.SortOrder }));
        b.Entity<FeaturedBoost>(e => { e.Property(fb => fb.Amount).HasPrecision(10, 2); e.HasOne(fb => fb.Freelancer).WithMany().HasForeignKey(fb => fb.FreelancerId); });
        b.Entity<PlatformSetting>(e => e.HasKey(ps => ps.Key));
    }
}
