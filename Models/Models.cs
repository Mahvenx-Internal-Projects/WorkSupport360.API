using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkSupport360.API.Models;

// ══════════════════════════════════════════════════════════════
// USER & AUTH
// ══════════════════════════════════════════════════════════════
public class User
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string Email       { get; set; } = "";
    public string? PasswordHash                          { get; set; }
    [Required, MaxLength(20)]  public string Role        { get; set; } = "client";
    [Required, MaxLength(200)] public string Name        { get; set; } = "";
    [MaxLength(500)]           public string? Picture    { get; set; }
    [MaxLength(20)]            public string? MobileNumber { get; set; }  // admin contacts via this
    public bool   IsActive          { get; set; } = true;
    public bool   EmailVerified     { get; set; } = false;
    public string? GoogleId         { get; set; }
    public string? GoogleEmail      { get; set; }
    public string? EmailVerifyToken { get; set; }
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt    { get; set; }
    public DateTime? LastLogoutAt   { get; set; }
    public int       LoginCount     { get; set; } = 0;
    public int       ActiveSessions { get; set; } = 0;

    public Freelancer?               FreelancerProfile { get; set; }
    public Client?                   ClientProfile     { get; set; }
    public ICollection<Notification> Notifications     { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens     { get; set; } = [];
    public ICollection<AttendanceLog> AttendanceLogs   { get; set; } = [];
}

public class RefreshToken
{
    [Key] public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid   UserId        { get; set; }
    [Required] public string Token { get; set; } = "";
    public DateTime ExpiresAt   { get; set; }
    public bool     IsRevoked   { get; set; }
    [MaxLength(100)] public string? DeviceInfo { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public User User            { get; set; } = null!;
}

// ── Attendance tracking ──────────────────────────────────────
public class AttendanceLog
{
    [Key] public Guid Id    { get; set; } = Guid.NewGuid();
    public Guid   UserId    { get; set; }
    [MaxLength(20)] public string Action { get; set; } = "login";  // login|logout|call_start|call_end
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? Note { get; set; }
    [MaxLength(45)]  public string? IpAddress { get; set; }
    public User User { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// FREELANCER
// ══════════════════════════════════════════════════════════════
public class Freelancer
{
    [Key] public Guid Id              { get; set; } = Guid.NewGuid();
    public Guid       UserId          { get; set; }
    [Required, MaxLength(100)] public string AliasName      { get; set; } = "";
    [Required, MaxLength(200)] public string RealName       { get; set; } = "";
    [Required, MaxLength(200)] public string CurrentCompany { get; set; } = "";
    [Required, MaxLength(200)] public string CurrentRole    { get; set; } = "";
    public int      TotalExp          { get; set; }
    public int      FreelanceExp      { get; set; }
    public decimal  HourlyRate        { get; set; }
    [MaxLength(5)]  public string Currency   { get; set; } = "USD";
    [MaxLength(100)] public string Country   { get; set; } = "India";
    [MaxLength(60)]  public string Timezone  { get; set; } = "IST";
    [MaxLength(2000)] public string Bio      { get; set; } = "";
    public decimal  Rating            { get; set; } = 0;
    public int      ReviewCount       { get; set; } = 0;
    public int      TrustScore        { get; set; } = 60;
    public int      Tier              { get; set; } = 1;
    public bool     IsAvailable       { get; set; } = false;
    public bool     IsVerified        { get; set; } = false;
    public int      ProfileViews      { get; set; } = 0;
    public int      ResponseTimeMinutes { get; set; } = 60;
    public decimal  TotalEarned       { get; set; } = 0;
    public decimal  PendingAmount     { get; set; } = 0;
    public int      CompletedProjects { get; set; } = 0;
    // Bank details for payouts
    [MaxLength(200)] public string? BankAccountName   { get; set; }
    [MaxLength(50)]  public string? BankAccountNumber { get; set; }
    [MaxLength(20)]  public string? BankIfscCode      { get; set; }
    [MaxLength(100)] public string? BankName          { get; set; }
    [MaxLength(50)]  public string? UpiId             { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User                             User         { get; set; } = null!;
    public ICollection<FreelancerSkill>     Skills       { get; set; } = [];
    public ICollection<FreelancerBadge>     Badges       { get; set; } = [];
    public ICollection<WeeklyAvailability>  Availability { get; set; } = [];
    public ICollection<DemoRequest>         Requests     { get; set; } = [];
    public ICollection<Meeting>             Meetings     { get; set; } = [];
    public ICollection<Project>             Projects     { get; set; } = [];
    public ICollection<Timesheet>           Timesheets   { get; set; } = [];
    public ICollection<Payment>             Payments     { get; set; } = [];
    public ICollection<DailyStandup>        Standups     { get; set; } = [];
    public ICollection<Invoice>             Invoices     { get; set; } = [];
    public ICollection<Review>              ReviewsGiven { get; set; } = [];
}

public class FreelancerSkill
{
    [Key] public Guid Id   { get; set; } = Guid.NewGuid();
    public Guid FreelancerId { get; set; }
    [Required, MaxLength(100)] public string Skill { get; set; } = "";
    public Freelancer Freelancer { get; set; } = null!;
}

public class FreelancerBadge
{
    [Key] public Guid Id     { get; set; } = Guid.NewGuid();
    public Guid FreelancerId { get; set; }
    [Required, MaxLength(100)] public string Badge { get; set; } = "";
    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
    public Freelancer Freelancer { get; set; } = null!;
}

public class WeeklyAvailability
{
    [Key] public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid FreelancerId   { get; set; }
    [Required, MaxLength(10)] public string DayOfWeek { get; set; } = "";
    public bool IsAvailable    { get; set; }
    [MaxLength(5)] public string? StartTime { get; set; }
    [MaxLength(5)] public string? EndTime   { get; set; }
    public Freelancer Freelancer { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// CLIENT
// ══════════════════════════════════════════════════════════════
public class Client
{
    [Key] public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid UserId         { get; set; }
    [Required, MaxLength(200)] public string CompanyName  { get; set; } = "";
    [Required, MaxLength(200)] public string ContactName  { get; set; } = "";
    [MaxLength(100)] public string Industry   { get; set; } = "";
    [MaxLength(100)] public string Country    { get; set; } = "";
    [MaxLength(20)]  public string Plan       { get; set; } = "payg";
    public int      HoursIncluded { get; set; } = 0;
    public int      HoursUsed     { get; set; } = 0;
    public decimal  TotalSpent    { get; set; } = 0;
    // GST for Indian clients
    [MaxLength(20)] public string? GstNumber { get; set; }
    public bool     IsGstRegistered { get; set; } = false;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;

    public User                       User     { get; set; } = null!;
    public ICollection<DemoRequest>   Requests { get; set; } = [];
    public ICollection<Project>       Projects { get; set; } = [];
    public ICollection<Invoice>       Invoices { get; set; } = [];
    public ICollection<Payment>       Payments { get; set; } = [];
    public ICollection<Review>        Reviews  { get; set; } = [];
}

// ══════════════════════════════════════════════════════════════
// DEMO REQUEST
// ══════════════════════════════════════════════════════════════
public class DemoRequest
{
    [Key] public Guid Id          { get; set; } = Guid.NewGuid();
    public Guid ClientId          { get; set; }
    public Guid FreelancerId      { get; set; }
    [MaxLength(30)] public string SessionType  { get; set; } = "demo";
    public DateTime PreferredDateTime          { get; set; }
    public int      DurationMinutes            { get; set; } = 45;
    // Budget — client can choose hourly or fixed
    public decimal  BudgetMin                  { get; set; }
    public decimal  BudgetMax                  { get; set; }
    [MaxLength(10)] public string BudgetType   { get; set; } = "hourly"; // hourly|fixed
    [MaxLength(5)]  public string Currency     { get; set; } = "USD";
    [MaxLength(2000)] public string Description { get; set; } = "";
    [MaxLength(20)] public string Status       { get; set; } = "pending";
    [MaxLength(1000)] public string? AdminNotes { get; set; }
    // Confirmed final budget after call
    public decimal? FinalBudget               { get; set; }
    [MaxLength(10)] public string? FinalBudgetType { get; set; }
    // Express Interest flow
    public bool     ClientInterested          { get; set; } = false;
    public bool     FreelancerInterested      { get; set; } = false;
    public decimal? ClientOfferedBudget       { get; set; }
    [MaxLength(10)] public string? ClientBudgetType { get; set; }
    [MaxLength(2000)] public string? ClientMessage { get; set; }
    public DateTime? ClientInterestedAt       { get; set; }
    public DateTime? FreelancerRespondedAt    { get; set; }
    [MaxLength(30)] public string InterestStatus { get; set; } = "none"; // none|client_interested|freelancer_accepted|freelancer_declined|scheduled
    public DateTime CreatedAt                 { get; set; } = DateTime.UtcNow;

    public Client    Client     { get; set; } = null!;
    public Freelancer Freelancer { get; set; } = null!;
    public Meeting?  Meeting    { get; set; }
}

// ── Job Requirement ──────────────────────────────────────────
public class JobRequirement
{
    [Key] public Guid Id               { get; set; } = Guid.NewGuid();
    public Guid   ClientId             { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(5000)] public string JobDescription { get; set; } = "";
    [MaxLength(500)]  public string? RequiredSkills { get; set; }
    [MaxLength(50)]   public string? ExperienceMin  { get; set; }
    [MaxLength(50)]   public string? ExperienceMax  { get; set; }
    [MaxLength(20)]   public string  BudgetType     { get; set; } = "hourly";
    public decimal?   BudgetMin        { get; set; }
    public decimal?   BudgetMax        { get; set; }
    [MaxLength(10)]   public string  Currency       { get; set; } = "USD";
    [MaxLength(30)]   public string  WorkMode       { get; set; } = "remote";
    public int?       HybridDaysPerWeek { get; set; }
    [MaxLength(100)]  public string? Location       { get; set; }
    [MaxLength(100)]  public string? WorkTimings    { get; set; }
    [MaxLength(20)]   public string  EngagementType { get; set; } = "freelance";
    [MaxLength(1000)] public string? Notes          { get; set; }
    public int        OpenPositions    { get; set; } = 1;
    [MaxLength(20)]   public string  Status         { get; set; } = "open";
    public DateTime   CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime?  ClosedAt         { get; set; }
    public Client  Client              { get; set; } = null!;
    public ICollection<RequirementAssignment> Assignments { get; set; } = [];
}

public class RequirementAssignment
{
    [Key] public Guid Id               { get; set; } = Guid.NewGuid();
    public Guid   RequirementId        { get; set; }
    public Guid   FreelancerId         { get; set; }
    [MaxLength(30)]  public string Status { get; set; } = "notified";
    [MaxLength(1000)] public string? FreelancerNote { get; set; }
    [MaxLength(1000)] public string? AdminNote      { get; set; }
    public DateTime   AssignedAt       { get; set; } = DateTime.UtcNow;
    public DateTime?  FreelancerRespondedAt { get; set; }
    public JobRequirement Requirement  { get; set; } = null!;
    public Freelancer    Freelancer    { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// MEETING
// ══════════════════════════════════════════════════════════════
public class Meeting
{
    [Key] public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid RequestId      { get; set; }
    public Guid ClientId       { get; set; }
    public Guid FreelancerId   { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes  { get; set; } = 45;
    [MaxLength(20)]  public string Platform    { get; set; } = "zoom";
    [MaxLength(500)] public string MeetingLink { get; set; } = "";
    public decimal AgreedRate   { get; set; }
    [MaxLength(10)]  public string BudgetType  { get; set; } = "hourly"; // hourly|fixed
    [MaxLength(5)]   public string Currency    { get; set; } = "USD";
    [MaxLength(20)]  public string Status      { get; set; } = "upcoming";
    [MaxLength(30)]  public string? Outcome    { get; set; }
    [MaxLength(2000)] public string? ClientFeedback     { get; set; }
    [MaxLength(2000)] public string? FreelancerFeedback { get; set; }
    // Freelancer availability confirmation
    public bool     FreelancerConfirmed { get; set; } = false;
    [MaxLength(500)] public string? FreelancerDeclineReason { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;

    public DemoRequest Request    { get; set; } = null!;
    public Client      Client     { get; set; } = null!;
    public Freelancer  Freelancer { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// PROJECT
// ══════════════════════════════════════════════════════════════
public class Project
{
    [Key] public Guid Id    { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string Name        { get; set; } = "";
    public Guid ClientId        { get; set; }
    public Guid FreelancerId    { get; set; }
    [MaxLength(2000)] public string Description { get; set; } = "";
    public decimal HourlyRate   { get; set; }
    [MaxLength(10)] public string BudgetType { get; set; } = "hourly"; // hourly|fixed
    [MaxLength(5)] public string Currency    { get; set; } = "USD";
    public int  EstimatedHours  { get; set; }
    public int  LoggedHours     { get; set; } = 0;
    public DateTime StartDate   { get; set; }
    public DateTime EndDate     { get; set; }
    public DateTime? PauseDate  { get; set; }
    public DateTime? ResumeDate { get; set; }
    public DateTime? ActualEndDate { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "pending_payment"; // pending_payment|active|paused|completed|cancelled
    public int     Progress     { get; set; } = 0;
    public decimal TotalBudget  { get; set; }
    public decimal TotalPaid    { get; set; } = 0;
    public decimal PendingAmount { get; set; } = 0;
    public decimal EscrowBalance { get; set; } = 0;
    // GST
    public bool   ApplyGst      { get; set; } = false;
    public decimal GstRate       { get; set; } = 18;
    [MaxLength(500)] public string? PauseReason   { get; set; }
    [MaxLength(500)] public string? CancelReason  { get; set; }
    [MaxLength(200)] public string Timezone       { get; set; } = "IST";
    [MaxLength(10)]  public string? BufferDays    { get; set; }  // buffer days after payment before start
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;

    public Client     Client     { get; set; } = null!;
    public Freelancer Freelancer { get; set; } = null!;
    public ICollection<ProjectSkill>  Skills     { get; set; } = [];
    public ICollection<Milestone>     Milestones { get; set; } = [];
    public ICollection<Timesheet>     Timesheets { get; set; } = [];
    public ICollection<Invoice>       Invoices   { get; set; } = [];
    public ICollection<ProjectStatusLog> StatusLogs { get; set; } = [];
}

public class ProjectStatusLog
{
    [Key] public Guid Id    { get; set; } = Guid.NewGuid();
    public Guid ProjectId   { get; set; }
    [MaxLength(20)] public string OldStatus { get; set; } = "";
    [MaxLength(20)] public string NewStatus { get; set; } = "";
    [MaxLength(500)] public string? Reason  { get; set; }
    public Guid ChangedBy   { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public Project Project  { get; set; } = null!;
}

public class ProjectSkill
{
    [Key] public Guid Id   { get; set; } = Guid.NewGuid();
    public Guid ProjectId  { get; set; }
    [Required, MaxLength(100)] public string Skill { get; set; } = "";
    public Project Project { get; set; } = null!;
}

public class Milestone
{
    [Key] public Guid Id  { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    [Required, MaxLength(200)] public string Title       { get; set; } = "";
    [MaxLength(1000)]          public string Description { get; set; } = "";
    public DateTime DueDate { get; set; }
    public decimal  Amount  { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "pending";
    public Project Project  { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// TIMESHEET
// ══════════════════════════════════════════════════════════════
public class Timesheet
{
    [Key] public Guid Id  { get; set; } = Guid.NewGuid();
    public Guid ProjectId   { get; set; }
    public Guid FreelancerId { get; set; }
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd   { get; set; }
    public decimal TotalHours  { get; set; } = 0;
    public decimal TotalAmount { get; set; } = 0;
    [MaxLength(20)] public string Status { get; set; } = "draft";
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt  { get; set; }
    public Guid? ApprovedById    { get; set; }
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;

    public Project     Project    { get; set; } = null!;
    public Freelancer  Freelancer { get; set; } = null!;
    public ICollection<TimesheetEntry>   Entries  { get; set; } = [];
    public ICollection<InvoiceTimesheet> Invoices { get; set; } = [];
}

public class TimesheetEntry
{
    [Key] public Guid Id  { get; set; } = Guid.NewGuid();
    public Guid TimesheetId { get; set; }
    public Guid ProjectId   { get; set; }
    public DateTime Date    { get; set; }
    public decimal Hours    { get; set; }
    [MaxLength(1000)] public string Description { get; set; } = "";
    [MaxLength(30)]  public string? TaskType    { get; set; } // development|meeting|review|support
    public Timesheet Timesheet { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// INVOICE
// ══════════════════════════════════════════════════════════════
public class Invoice
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(50)] public string InvoiceNumber { get; set; } = "";
    public Guid ProjectId    { get; set; }
    public Guid ClientId     { get; set; }
    public Guid FreelancerId { get; set; }
    public decimal Subtotal        { get; set; }
    public decimal Commission      { get; set; }
    public decimal CommissionRate  { get; set; } = 15;
    // GST
    public bool   ApplyGst         { get; set; } = false;
    public decimal GstRate          { get; set; } = 18;
    public decimal GstAmount        { get; set; } = 0;
    public decimal Total            { get; set; }
    public decimal FreelancerAmount { get; set; }
    [MaxLength(5)]  public string Currency { get; set; } = "USD";
    [MaxLength(20)] public string Status   { get; set; } = "pending";
    // Payment instructions
    [MaxLength(1000)] public string? PaymentInstructions { get; set; }
    public DateTime IssuedAt  { get; set; } = DateTime.UtcNow;
    public DateTime DueAt     { get; set; }
    public DateTime? PaidAt   { get; set; }
    // Reminder tracking
    public int RemindersSent  { get; set; } = 0;
    public DateTime? LastReminderAt { get; set; }

    public Project    Project    { get; set; } = null!;
    public Client     Client     { get; set; } = null!;
    public Freelancer Freelancer { get; set; } = null!;
    public ICollection<InvoiceLineItem>  LineItems         { get; set; } = [];
    public ICollection<InvoiceTimesheet> InvoiceTimesheets { get; set; } = [];
    public Payment? Payment { get; set; }
}

public class InvoiceLineItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId  { get; set; }
    [MaxLength(500)] public string Description { get; set; } = "";
    public decimal Hours  { get; set; }
    public decimal Rate   { get; set; }
    public decimal Amount { get; set; }
    public Invoice Invoice { get; set; } = null!;
}

public class InvoiceTimesheet
{
    public Guid InvoiceId   { get; set; }
    public Guid TimesheetId { get; set; }
    public Invoice   Invoice   { get; set; } = null!;
    public Timesheet Timesheet { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// PAYMENT
// ══════════════════════════════════════════════════════════════
public class Payment
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId    { get; set; }
    public Guid ClientId     { get; set; }
    public Guid FreelancerId { get; set; }
    public decimal Amount           { get; set; }
    public decimal Commission       { get; set; }
    public decimal GstAmount        { get; set; } = 0;
    public decimal FreelancerAmount { get; set; }
    [MaxLength(5)]   public string Currency      { get; set; } = "USD";
    [MaxLength(20)]  public string Status        { get; set; } = "pending";
    [MaxLength(100)] public string Method        { get; set; } = "Bank Transfer";
    [MaxLength(100)] public string? TransactionId { get; set; }
    [MaxLength(500)] public string? PaymentNote   { get; set; }
    // Freelancer payout tracking
    [MaxLength(20)]  public string PayoutStatus  { get; set; } = "pending"; // pending|processing|paid
    public DateTime? PayoutDate   { get; set; }
    [MaxLength(100)] public string? PayoutTransactionId { get; set; }
    public DateTime  CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt       { get; set; }

    public Invoice    Invoice    { get; set; } = null!;
    public Client     Client     { get; set; } = null!;
    public Freelancer Freelancer { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// NOTIFICATION
// ══════════════════════════════════════════════════════════════
public class Notification
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId   { get; set; }
    [MaxLength(30)]   public string Type      { get; set; } = "system";
    [MaxLength(200)]  public string Title     { get; set; } = "";
    [MaxLength(1000)] public string Message   { get; set; } = "";
    public bool     IsRead    { get; set; } = false;
    [MaxLength(20)]  public string Priority   { get; set; } = "normal"; // low|normal|high|urgent
    [MaxLength(500)] public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User User          { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// DAILY STANDUP
// ══════════════════════════════════════════════════════════════
public class DailyStandup
{
    [Key] public Guid Id  { get; set; } = Guid.NewGuid();
    public Guid ProjectId   { get; set; }
    public Guid FreelancerId { get; set; }
    public DateTime Date    { get; set; }
    [MaxLength(2000)] public string YesterdayWork { get; set; } = "";
    [MaxLength(2000)] public string TodayPlan     { get; set; } = "";
    [MaxLength(1000)] public string Blockers      { get; set; } = "None";
    public decimal HoursWorked { get; set; }
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public Project    Project    { get; set; } = null!;
    public Freelancer Freelancer { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// REVIEW
// ══════════════════════════════════════════════════════════════
public class Review
{
    [Key] public Guid Id  { get; set; } = Guid.NewGuid();
    public Guid ProjectId   { get; set; }
    public Guid ClientId    { get; set; }
    public Guid FreelancerId { get; set; }
    public int    Rating    { get; set; }
    [MaxLength(2000)] public string Comment { get; set; } = "";
    public bool   IsPublic  { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Client     Client     { get; set; } = null!;
    public Freelancer Freelancer { get; set; } = null!;
    public Project    Project    { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// SUBSCRIPTION & REVENUE
// ══════════════════════════════════════════════════════════════
public class SubscriptionPlan
{
    [Key] public Guid Id            { get; set; } = Guid.NewGuid();
    [MaxLength(30)] public string PlanKey  { get; set; } = "";
    [MaxLength(100)] public string Name    { get; set; } = "";
    public decimal PriceMonthly     { get; set; }
    public decimal PriceYearly      { get; set; }
    public int HoursIncluded        { get; set; }
    public decimal OverageRatePerHr { get; set; }
    public decimal CommissionRate   { get; set; } = 15;
    public int MaxProjects          { get; set; }
    public bool HasPrioritySupport  { get; set; }
    public bool HasDedicatedManager { get; set; }
    [MaxLength(1000)] public string FeaturesJson { get; set; } = "[]";
    public bool IsActive            { get; set; } = true;
    public int SortOrder            { get; set; }
}

public class ClientSubscription
{
    [Key] public Guid Id         { get; set; } = Guid.NewGuid();
    public Guid ClientId         { get; set; }
    public Guid PlanId           { get; set; }
    [MaxLength(20)] public string BillingCycle { get; set; } = "monthly";
    public decimal AmountPaid    { get; set; }
    [MaxLength(5)] public string Currency      { get; set; } = "USD";
    [MaxLength(20)] public string Status       { get; set; } = "active";
    public DateTime StartDate    { get; set; }
    public DateTime EndDate      { get; set; }
    public DateTime? CancelledAt { get; set; }
    [MaxLength(100)] public string? GatewayTxnId { get; set; }
    [MaxLength(30)]  public string? PaymentMethod { get; set; }
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public Client    Client      { get; set; } = null!;
    public SubscriptionPlan Plan { get; set; } = null!;
}

public class PlatformEarning
{
    [Key] public Guid Id          { get; set; } = Guid.NewGuid();
    [MaxLength(30)] public string Source     { get; set; } = "";
    public decimal Amount         { get; set; }
    [MaxLength(5)] public string Currency    { get; set; } = "USD";
    public Guid? RelatedEntityId  { get; set; }
    [MaxLength(200)] public string Description { get; set; } = "";
    public DateTime EarnedAt      { get; set; } = DateTime.UtcNow;
}

// ══════════════════════════════════════════════════════════════
// QUICK SUPPORT SESSION
// ══════════════════════════════════════════════════════════════
public class QuickSupportSession
{
    [Key] public Guid Id          { get; set; } = Guid.NewGuid();
    public Guid? ClientId         { get; set; }
    public Guid FreelancerId      { get; set; }
    [MaxLength(500)] public string Topic     { get; set; } = "";
    public int DurationMinutes    { get; set; } = 60;
    public decimal Rate           { get; set; }
    [MaxLength(5)] public string Currency    { get; set; } = "USD";
    [MaxLength(20)] public string Status     { get; set; } = "pending";
    [MaxLength(20)] public string Platform   { get; set; } = "zoom";
    [MaxLength(500)] public string? MeetingLink { get; set; }
    public DateTime? ScheduledAt  { get; set; }
    public decimal? PlatformFee   { get; set; }
    [MaxLength(200)] public string? ClientContactEmail { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public Client?    Client      { get; set; }
    public Freelancer Freelancer  { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// SUPPORT TICKETS
// ══════════════════════════════════════════════════════════════
public class SupportTicket
{
    [Key] public Guid Id           { get; set; } = Guid.NewGuid();
    public Guid UserId             { get; set; }
    [MaxLength(200)] public string Subject    { get; set; } = "";
    [MaxLength(20)]  public string Category   { get; set; } = "general";
    [MaxLength(20)]  public string Status     { get; set; } = "bot";  // bot|open|assigned|resolved|closed
    [MaxLength(20)]  public string Priority   { get; set; } = "normal";
    // Assignment
    public Guid? AssignedAgentId   { get; set; }                     // admin user assigned
    [MaxLength(100)] public string? AssignedAgentName { get; set; }
    public DateTime? AssignedAt    { get; set; }
    // Metadata collected by bot
    [MaxLength(50)]  public string? UserType  { get; set; }           // client|freelancer|visitor
    [MaxLength(500)] public string? BotSummary { get; set; }          // summary of bot Q&A
    [MaxLength(100)] public string? ContactEmail { get; set; }
    [MaxLength(20)]  public string? ContactPhone { get; set; }
    public bool IsRead             { get; set; } = false;             // admin has seen it
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt    { get; set; }
    public DateTime? LastMessageAt { get; set; } = DateTime.UtcNow;
    public User User               { get; set; } = null!;
    public ICollection<SupportMessage> Messages { get; set; } = [];
}

public class SupportMessage
{
    [Key] public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TicketId        { get; set; }
    public Guid SenderId        { get; set; }
    [MaxLength(10)] public string SenderRole { get; set; } = "user";
    [MaxLength(4000)] public string Content  { get; set; } = "";
    public bool IsAi            { get; set; } = false;
    public DateTime SentAt      { get; set; } = DateTime.UtcNow;
    public SupportTicket Ticket { get; set; } = null!;
}

// ══════════════════════════════════════════════════════════════
// FAQ, CONTACT, SETTINGS
// ══════════════════════════════════════════════════════════════
public class Faq
{
    [Key] public Guid Id          { get; set; } = Guid.NewGuid();
    [MaxLength(30)] public string Category  { get; set; } = "general";
    [MaxLength(500)] public string Question { get; set; } = "";
    [MaxLength(3000)] public string Answer  { get; set; } = "";
    public int SortOrder          { get; set; }
    public bool IsActive          { get; set; } = true;
    public int HelpfulCount       { get; set; } = 0;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
}

public class ContactSubmission
{
    [Key] public Guid Id          { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string Name     { get; set; } = "";
    [MaxLength(200)] public string Email    { get; set; } = "";
    [MaxLength(30)] public string Reason    { get; set; } = "general";
    [MaxLength(2000)] public string Message { get; set; } = "";
    [MaxLength(20)] public string Status    { get; set; } = "new";
    public DateTime SubmittedAt   { get; set; } = DateTime.UtcNow;
}

public class FeaturedBoost
{
    [Key] public Guid Id          { get; set; } = Guid.NewGuid();
    public Guid FreelancerId      { get; set; }
    public decimal Amount         { get; set; }
    [MaxLength(5)] public string Currency { get; set; } = "USD";
    public DateTime StartsAt      { get; set; }
    public DateTime EndsAt        { get; set; }
    public bool IsActive          { get; set; } = true;
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
    public Freelancer Freelancer  { get; set; } = null!;
}

public class PlatformSetting
{
    [Key, MaxLength(100)] public string Key   { get; set; } = "";
    [MaxLength(2000)]     public string Value { get; set; } = "";
    [MaxLength(200)]      public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Inbox / Messaging ─────────────────────────────────────────
public class MessageThread
{
    [Key] public Guid Id              { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string Subject  { get; set; } = "Message";
    public bool   IsAdminThread       { get; set; } = false;
    public DateTime LastMessageAt     { get; set; } = DateTime.UtcNow;
    public ICollection<InboxMessage>      Messages     { get; set; } = [];
    public ICollection<ThreadParticipant> Participants { get; set; } = [];
    public ICollection<MessageAttachment> Attachments  { get; set; } = [];
}

public class ThreadParticipant
{
    [Key] public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid ThreadId       { get; set; }
    public Guid UserId         { get; set; }
    public MessageThread Thread { get; set; } = null!;
    public User User            { get; set; } = null!;
}

public class InboxMessage
{
    [Key] public Guid Id         { get; set; } = Guid.NewGuid();
    public Guid   ThreadId       { get; set; }
    public Guid   SenderId       { get; set; }
    public Guid   RecipientId    { get; set; }
    [MaxLength(4000)] public string Body { get; set; } = "";
    public bool   IsRead         { get; set; } = false;
    public DateTime SentAt       { get; set; } = DateTime.UtcNow;
    public MessageThread Thread  { get; set; } = null!;
    public User Sender           { get; set; } = null!;
}

public class MessageAttachment
{
    [Key] public Guid Id         { get; set; } = Guid.NewGuid();
    public Guid   ThreadId       { get; set; }
    [MaxLength(200)] public string FileName { get; set; } = "";
    [MaxLength(500)] public string FileUrl  { get; set; } = "";
    public Guid   UploadedBy     { get; set; }
    public DateTime UploadedAt   { get; set; } = DateTime.UtcNow;
    public MessageThread Thread  { get; set; } = null!;
}
public class Requirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientUserId { get; set; }
    public string Title { get; set; } = "";
    public string SkillsRequired { get; set; } = "";
    public string HoursPerEngagement { get; set; } = "";
    public int FreelancerCount { get; set; } = 1;
    public decimal BudgetMin { get; set; }
    public decimal BudgetMax { get; set; }
    public string Currency { get; set; } = "INR";
    public string? Duration { get; set; }
    public string? DurationType { get; set; }
    public string? WorkMode { get; set; } = "remote";
    public string? Description { get; set; }
    public string? CompanyName { get; set; }
    public string? ContactName { get; set; }
    public string? Urgency { get; set; } = "normal";
    public string? PreferredStartDate { get; set; }
    public string Status { get; set; } = "pending";
    // pending | open | allocated | closed | rejected
    public string? AdminNotes { get; set; }
    public Guid? AllocatedFreelancerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class RequirementApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequirementId { get; set; }
    public Guid FreelancerUserId { get; set; }
    public Guid? FreelancerId { get; set; }
    public string? CoverNote { get; set; }
    public string? ProposedRate { get; set; }
    public string Status { get; set; } = "pending";
    // pending | shortlisted | assigned | rejected
    public string? AdminNote { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}

