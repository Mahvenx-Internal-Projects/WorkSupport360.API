namespace WorkSupport360.API.DTOs;

// ── Auth ──────────────────────────────────────────────────────
public record LoginRequest(string Email, string Password, string? DeviceInfo = null);
public record GoogleSignInRequest(string IdToken, string? DeviceInfo = null);
public record RegisterRequest(
    string Email, string Password, string Name, string Role,
    string? MobileNumber    = null,
    string? CompanyName     = null, string? ContactName  = null,
    string? Industry        = null, string? Country      = null,
    string? AliasName       = null, string? CurrentRole  = null,
    string? CurrentCompany  = null,
    int     TotalExp        = 0,    int     FreelanceExp  = 0,
    decimal HourlyRate      = 0,    string? Currency      = "USD",
    string? Timezone        = "IST (UTC+5:30)", string? Bio = null,
    List<string>? Skills    = null,
    List<AvailabilityDto>? Availability = null
);
public record CompleteProfileRequest(
    string CurrentRole, string CurrentCompany,
    int TotalExp, int FreelanceExp,
    decimal HourlyRate, string Currency, string Country, string Timezone, string Bio,
    List<string>? Skills, List<AvailabilityDto>? Availability
);
public record RefreshTokenRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, string Role,
    string Name, string UserId, string? Picture, bool IsNewUser = false);

// ── Freelancer ────────────────────────────────────────────────
public record FreelancerListDto(
    Guid Id, string AliasName, string CurrentRole, int TotalExp, int FreelanceExp,
    List<string> Skills, decimal HourlyRate, string Currency,
    decimal Rating, int ReviewCount, int TrustScore, int Tier,
    bool IsAvailable, bool IsVerified, string Country, string Timezone,
    int CompletedProjects, int ProfileViews, int ResponseTimeMinutes,
    List<string> Badges, bool IsFeatured = false);

public record FreelancerDetailDto(
    Guid Id, string AliasName, string CurrentRole, int TotalExp, int FreelanceExp,
    List<string> Skills, List<string> Badges,
    decimal HourlyRate, string Currency,
    decimal Rating, int ReviewCount, int TrustScore, int Tier,
    bool IsAvailable, bool IsVerified, string Country, string Timezone, string Bio,
    List<AvailabilityDto> Availability, int CompletedProjects,
    List<ReviewDto> Reviews);

public record FreelancerPrivateDto(
    Guid Id, string AliasName, string RealName, string CurrentCompany,
    string CurrentRole, decimal TotalEarned, decimal PendingAmount,
    int CompletedProjects, int ProfileViews, int TrustScore,
    string? BankAccountName, string? BankAccountNumber,
    string? BankIfscCode, string? BankName, string? UpiId);

public record AvailabilityDto(string DayOfWeek, bool IsAvailable, string? StartTime, string? EndTime);

public record UpdateFreelancerRequest(
    string AliasName, string CurrentRole, string Bio,
    decimal HourlyRate, string Currency, string Country, string Timezone,
    List<string> Skills, List<AvailabilityDto> Availability,
    string? BankAccountName = null, string? BankAccountNumber = null,
    string? BankIfscCode = null, string? BankName = null, string? UpiId = null);

public record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize, int TotalPages);

// ── Client ────────────────────────────────────────────────────
public record ClientProfileDto(
    Guid Id, string CompanyName, string ContactName, string Industry,
    string Country, string Plan, int HoursIncluded, int HoursUsed, decimal TotalSpent,
    string? GstNumber, bool IsGstRegistered);

// ── Demo Request ──────────────────────────────────────────────
public record CreateDemoRequestDto(
    Guid FreelancerId, string SessionType, DateTime PreferredDateTime,
    int DurationMinutes, decimal BudgetMin, decimal BudgetMax,
    string BudgetType, string Currency, string Description);

public record DemoRequestDto(
    Guid Id, Guid ClientId, string ClientName, string ClientMobile,
    Guid FreelancerId, string FreelancerName, string FreelancerMobile,
    string SessionType, DateTime PreferredDateTime, int DurationMinutes,
    decimal BudgetMin, decimal BudgetMax, string BudgetType, string Currency,
    string Description, string Status, string? AdminNotes,
    decimal? FinalBudget, string? FinalBudgetType, DateTime CreatedAt,
    // Interest fields
    bool ClientInterested = false,
    decimal? ClientOfferedBudget = null,
    string? ClientBudgetType = null,
    string? ClientMessage = null,
    DateTime? ClientInterestedAt = null,
    string InterestStatus = "none",
    string? FreelancerAliasName = null,
    string? ClientEmail = null,
    string? ClientCompany = null,
    string? FreelancerEmail = null);

public record UpdateRequestStatusDto(string Status, string? AdminNotes,
    decimal? FinalBudget = null, string? FinalBudgetType = null);

// ── Meeting ───────────────────────────────────────────────────
public record ScheduleMeetingDto(
    Guid RequestId, DateTime ScheduledAt, int DurationMinutes,
    string Platform, string MeetingLink, decimal AgreedRate,
    string BudgetType, string Currency);

public record MeetingDto(
    Guid Id, string ClientName, string FreelancerName, string SessionType,
    DateTime ScheduledAt, int DurationMinutes, string Platform, string MeetingLink,
    decimal AgreedRate, string BudgetType, string Currency,
    string Status, string? Outcome, bool FreelancerConfirmed, DateTime CreatedAt);

public record UpdateMeetingOutcomeDto(string Outcome, string? Notes,
    decimal? FinalBudget = null, string? FinalBudgetType = null);

public record FreelancerConfirmDto(bool Confirmed, string? DeclineReason = null);

// ── Project ───────────────────────────────────────────────────
public record CreateProjectDto(
    string Name, Guid ClientId, Guid FreelancerId, string Description,
    List<string> Skills, decimal HourlyRate, string BudgetType, string Currency,
    int EstimatedHours, DateTime StartDate, DateTime EndDate,
    decimal TotalBudget, string Timezone, bool ApplyGst, decimal GstRate,
    string? BufferDays = null,
    List<CreateMilestoneDto>? Milestones = null);

public record CreateMilestoneDto(string Title, string Description, DateTime DueDate, decimal Amount);

public record ProjectDto(
    Guid Id, string Name, string ClientName, string FreelancerName,
    string FreelancerAlias, string Description,
    List<string> Skills, decimal HourlyRate, string BudgetType, string Currency,
    int EstimatedHours, int LoggedHours,
    DateTime StartDate, DateTime EndDate, string Status, int Progress,
    decimal TotalBudget, decimal TotalPaid, decimal PendingAmount, decimal EscrowBalance,
    bool ApplyGst, decimal GstRate, string Timezone, string? BufferDays,
    List<MilestoneDto> Milestones, List<ProjectStatusLogDto> StatusLogs, DateTime CreatedAt);

public record MilestoneDto(Guid Id, string Title, string Description, DateTime DueDate, decimal Amount, string Status);
public record ProjectStatusLogDto(string OldStatus, string NewStatus, string? Reason, DateTime ChangedAt);

public record UpdateProjectDto(
    string? Name = null, string? Description = null,
    Guid? FreelancerId = null, decimal? HourlyRate = null, string? BudgetType = null,
    decimal? TotalBudget = null, DateTime? StartDate = null, DateTime? EndDate = null,
    string? Status = null, int? Progress = null, string? Reason = null);

// ── Timesheet ─────────────────────────────────────────────────
public record CreateTimesheetDto(
    Guid ProjectId, DateTime WeekStart, DateTime WeekEnd,
    List<CreateEntryDto> Entries);

public record CreateEntryDto(DateTime Date, decimal Hours, string Description, string? TaskType = null);

public record TimesheetDto(
    Guid Id, Guid ProjectId, string ProjectName, string FreelancerName,
    DateTime WeekStart, DateTime WeekEnd,
    decimal TotalHours, decimal TotalAmount, string Status,
    List<TimesheetEntryDto> Entries,
    DateTime? SubmittedAt, DateTime? ApprovedAt);

public record TimesheetEntryDto(Guid Id, DateTime Date, decimal Hours, string Description, string? TaskType);
public record ApproveTimesheetDto(bool Approve, string? Reason = null);

// ── Invoice ───────────────────────────────────────────────────
public record InvoiceDto(
    Guid Id, string InvoiceNumber, string ClientName, string FreelancerName,
    string ProjectName, List<InvoiceLineItemDto> LineItems,
    decimal Subtotal, decimal Commission, decimal CommissionRate,
    bool ApplyGst, decimal GstRate, decimal GstAmount,
    decimal Total, decimal FreelancerAmount, string Currency,
    string Status, string? PaymentInstructions,
    DateTime IssuedAt, DateTime DueAt, DateTime? PaidAt,
    int RemindersSent);

public record InvoiceLineItemDto(string Description, decimal Hours, decimal Rate, decimal Amount);

// ── Payment ───────────────────────────────────────────────────
public record PaymentDto(
    Guid Id, string InvoiceNumber, decimal Amount, decimal Commission,
    decimal GstAmount, decimal FreelancerAmount, string Currency,
    string Status, string Method, string? TransactionId,
    string PayoutStatus, DateTime? PayoutDate,
    DateTime CreatedAt, DateTime? PaidAt);

public record RecordPaymentDto(Guid InvoiceId, string Method, string? TransactionId = null, string? PaymentNote = null);

public record RecordPayoutDto(Guid PaymentId, string? PayoutTransactionId = null);

// ── Notifications ─────────────────────────────────────────────
public record NotificationDto(
    Guid Id, string Type, string Title, string Message,
    bool IsRead, string Priority, string? ActionUrl, DateTime CreatedAt);

// ── Standup ───────────────────────────────────────────────────
public record CreateStandupDto(
    Guid ProjectId, DateTime Date,
    string YesterdayWork, string TodayPlan, string Blockers, decimal HoursWorked);

public record StandupDto(
    Guid Id, string ProjectName, DateTime Date,
    string YesterdayWork, string TodayPlan, string Blockers,
    decimal HoursWorked, DateTime CreatedAt);

// ── Review ────────────────────────────────────────────────────
public record CreateReviewDto(Guid ProjectId, Guid FreelancerId, int Rating, string Comment);
public record ReviewDto(Guid Id, string ClientName, int Rating, string Comment, DateTime CreatedAt);

// ── Attendance ────────────────────────────────────────────────
public record AttendanceDto(Guid Id, string Action, DateTime Timestamp, string? Note);

// ── Stats ─────────────────────────────────────────────────────
public record AdminStatsDto(
    decimal TotalRevenue, int ActiveProjects, int PendingRequests,
    decimal PlatformCommission, int TotalFreelancers, int TotalClients,
    decimal AvgRating, decimal RevenueGrowth, int PendingPayouts,
    decimal PendingInvoiceAmount);

public record FreelancerStatsDto(
    decimal MonthlyEarnings, decimal ClearedAmount,
    decimal PendingAmount, decimal AllTimeEarned,
    int CompletedProjects, int TrustScore, int ProfileViews, int ActiveProjects);

public record LeaderboardEntryDto(
    int Rank, string FreelancerName, decimal Earnings,
    decimal Rating, int CompletedProjects, string? Badge);

// ── Quick Support ─────────────────────────────────────────────
public record BookQuickSupportDto(
    Guid FreelancerId, string Topic, string? Platform,
    string? ClientEmail = null, string? ClientName = null);
