using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using WorkSupport360.API.Data;
using WorkSupport360.API.Middleware;
using WorkSupport360.API.Models;
using WorkSupport360.API.Services;

var builder = WebApplication.CreateBuilder(args);

var conn = builder.Configuration.GetConnectionString("DefaultConnection")!;
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseMySql(conn, ServerVersion.AutoDetect(conn), o => o.EnableRetryOnFailure(3)));

var jwtSecret = builder.Configuration["Jwt:Secret"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts => {
        opts.TokenValidationParameters = new() {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        };
        opts.Events = new JwtBearerEvents {
            OnAuthenticationFailed = ctx => {
                if (ctx.Exception is SecurityTokenExpiredException) ctx.Response.Headers.Append("Token-Expired", "true");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
var frontendUrl = builder.Configuration["App:FrontendUrl"] ?? "http://localhost:3000";
builder.Services.AddCors(opts => opts.AddPolicy("Frontend", p =>
    p.WithOrigins(
        frontendUrl,
        "https://worksupport.com",
        "https://www.worksupport.com",
        "http://worksupport.com",
        "https://worksupport360.com",
        "https://www.worksupport360.com",
        "http://204.168.159.160:5001",
        "http://localhost:3000",
        "http://localhost:5001"
     )
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => {
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "WorkSupport360 API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Description = "JWT Bearer {token}", Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.ApiKey, Scheme = "Bearer" });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {{ new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }});
});

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "WorkSupport360 API v1"); c.DefaultModelsExpandDepth(-1); });
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope()) {
    var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try { await db.Database.MigrateAsync(); await SeedAsync(db, log); }
    catch (Exception ex) { log.LogError(ex, "Startup error — check DB"); }
}
app.Run();

static async Task SeedAsync(AppDbContext db, ILogger log)
{
    if (!await db.SubscriptionPlans.AnyAsync())
    {
        db.SubscriptionPlans.AddRange(
            new SubscriptionPlan { PlanKey="payg", Name="Pay As You Go", PriceMonthly=0, PriceYearly=0, HoursIncluded=0, OverageRatePerHr=0, CommissionRate=15, MaxProjects=1, HasPrioritySupport=false, HasDedicatedManager=false, SortOrder=0, FeaturesJson=JsonSerializer.Serialize(new[]{"No monthly fee","15% commission","All experts","Standard support","Pay per project"}) },
            new SubscriptionPlan { PlanKey="starter", Name="Starter", PriceMonthly=199, PriceYearly=1990, HoursIncluded=10, OverageRatePerHr=22, CommissionRate=15, MaxProjects=3, HasPrioritySupport=false, HasDedicatedManager=false, SortOrder=1, FeaturesJson=JsonSerializer.Serialize(new[]{"10 hours/month","$22/extra hour","15% commission","Email support","Up to 3 projects"}) },
            new SubscriptionPlan { PlanKey="growth", Name="Growth", PriceMonthly=449, PriceYearly=4290, HoursIncluded=25, OverageRatePerHr=19, CommissionRate=12, MaxProjects=10, HasPrioritySupport=true, HasDedicatedManager=false, SortOrder=2, FeaturesJson=JsonSerializer.Serialize(new[]{"25 hours/month","$19/extra hour","12% commission","Priority support","Up to 10 projects","Monthly reports"}) },
            new SubscriptionPlan { PlanKey="enterprise", Name="Enterprise", PriceMonthly=999, PriceYearly=9990, HoursIncluded=75, OverageRatePerHr=15, CommissionRate=10, MaxProjects=999, HasPrioritySupport=true, HasDedicatedManager=true, SortOrder=3, FeaturesJson=JsonSerializer.Serialize(new[]{"75 hours/month","$15/extra hour","10% commission","24/7 support","Unlimited projects","Dedicated manager","Custom contracts","GST invoicing"}) });
        await db.SaveChangesAsync();
        log.LogInformation("Plans seeded");
    }

    if (!await db.PlatformSettings.AnyAsync())
    {
        db.PlatformSettings.AddRange(
            new PlatformSetting { Key="public.support_email", Value="help@worksupport360.com" },
            new PlatformSetting { Key="public.whatsapp_number", Value="+91-9441363687" },
            new PlatformSetting { Key="public.admin_phone", Value="+91-9441363687" },
            new PlatformSetting { Key="public.calendly_link", Value="https://calendly.com/worksupport360/demo" },
            new PlatformSetting { Key="public.commission_default", Value="15" },
            new PlatformSetting { Key="public.quick_support_fee", Value="20" },
            new PlatformSetting { Key="public.gst_rate", Value="18" },
            new PlatformSetting { Key="bank.account_name", Value="WorkSupport360 Pvt Ltd" },
            new PlatformSetting { Key="bank.account_number", Value="XXXXXXXXXX" },
            new PlatformSetting { Key="bank.ifsc_code", Value="HDFC0000001" },
            new PlatformSetting { Key="bank.bank_name", Value="HDFC Bank" },
            new PlatformSetting { Key="bank.upi_id", Value="help@worksupport360" });
        await db.SaveChangesAsync();
        log.LogInformation("Platform settings seeded");
    }

    if (!await db.Faqs.AnyAsync())
    {
        db.Faqs.AddRange(
            new Faq { Category="general", SortOrder=1, Question="What is WorkSupport360?", Answer="WorkSupport360 is India's first identity-safe freelancer marketplace. MNC professionals from Infosys, TCS, Wipro and others freelance under a privacy alias — their employer never finds out. Clients get top enterprise-level talent at flexible rates." },
            new Faq { Category="general", SortOrder=2, Question="How quickly can I get help?", Answer="Quick Support: expert on call in 30 minutes. Demo/project request: admin schedules within 4 hours. Projects start within 24 hours after approval and payment." },
            new Faq { Category="general", SortOrder=3, Question="What if I need urgent help right now?", Answer="Use Quick Support on the homepage! Pick an available expert, describe your problem (Kubernetes crash, React bug, deployment issue), and they'll join your Zoom/Meet call within 30 minutes." },
            new Faq { Category="payments", SortOrder=1, Question="How do payments work?", Answer="Flow: Freelancer submits weekly timesheet → You approve → Invoice auto-generated → Admin sends you bank details via email → You pay via bank transfer/UPI → We pay freelancer within 3 business days. All payments tracked in your dashboard." },
            new Faq { Category="payments", SortOrder=2, Question="Are payments secure?", Answer="All project money is held in escrow by WorkSupport360. Released to freelancer only after YOU approve timesheets. Dispute resolution team available if any issue." },
            new Faq { Category="payments", SortOrder=3, Question="Does GST apply?", Answer="Yes — for Indian clients, 18% GST applies on project invoices. We provide GST-compliant invoices. You can add your GST number to your profile for input tax credit. International clients: no GST." },
            new Faq { Category="payments", SortOrder=4, Question="What payment methods do you accept?", Answer="Bank Transfer (NEFT/RTGS/IMPS), International Wire, PayPal, Stripe, Razorpay, UPI. Payment instructions are sent via email with each invoice." },
            new Faq { Category="payments", SortOrder=5, Question="When do freelancers get paid?", Answer="Within 3 business days of client payment confirmation. Payout goes to their registered bank account. Freelancers track payouts in their dashboard." },
            new Faq { Category="freelancer", SortOrder=1, Question="Will my employer find out?", Answer="Absolutely not. You work under a privacy alias (e.g. 'Rahul S.'). Your real name, company, and personal details are stored encrypted and never shared with clients. Only WorkSupport360 admin sees this for identity verification." },
            new Faq { Category="freelancer", SortOrder=2, Question="How much can I earn?", Answer="You set your hourly rate. After 15% platform commission (10% for Enterprise clients), you keep 85-90%. At $35/hr × 20 hrs/month = $595 extra. Many experts earn $1,000-$3,000/month extra." },
            new Faq { Category="freelancer", SortOrder=3, Question="What is Quick Support and how do I earn?", Answer="Mark yourself 'Available' to appear in Quick Support. Clients book 1-hour sessions. You earn your hourly rate minus 20% platform fee. Great for evening/weekend income." },
            new Faq { Category="freelancer", SortOrder=4, Question="How do I get paid?", Answer="Add your bank account details in My Profile → Bank Details. Once client pays the invoice, we process your payout within 3 business days. You get an email + notification when payout is processed." },
            new Faq { Category="client", SortOrder=1, Question="How do I hire an expert?", Answer="Browse experts → Click Request → Fill project details & budget → Admin schedules a 45-min video call within 4 hours → Interview the expert → Approve → Project starts after payment." },
            new Faq { Category="client", SortOrder=2, Question="What if the expert is unavailable at the proposed time?", Answer="Admin first checks expert availability before scheduling. If unavailable, admin immediately suggests an alternative time or a different expert. You're notified instantly via email and in-app notification." },
            new Faq { Category="client", SortOrder=3, Question="Can I change the expert mid-project?", Answer="Yes. Contact admin via help@worksupport360.com or WhatsApp. Admin can replace the expert and reassign the project. Your escrow funds are fully protected throughout." },
            new Faq { Category="privacy", SortOrder=1, Question="Is my project information confidential?", Answer="Yes. Experts sign NDAs. Project details visible only to the assigned expert and admin. We never share your business information. Competitors won't know you're building the same product." },
            new Faq { Category="privacy", SortOrder=2, Question="What if the expert leaves mid-project?", Answer="Admin guarantees continuity. We maintain a pool of verified experts in each skill area. If an expert becomes unavailable, admin assigns a replacement within 24 hours with full context handoff." }
        );
        await db.SaveChangesAsync();
        log.LogInformation("FAQs seeded");
    }

    if (await db.Users.AnyAsync()) { log.LogInformation("Users already exist — skipping user seed"); return; }

    // Admin
    var admin = new User { Email="admin@worksupport360.com", Name="Admin User", Role="admin", MobileNumber="9441363687", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Admin@123!"), EmailVerified=true };
    db.Users.Add(admin);

    // Support Agents (role = "agent")
    var agent1 = new User { Email="support1@worksupport360.com", Name="Ravi Kumar", Role="agent", MobileNumber="9000000001", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Agent@123!"), EmailVerified=true };
    var agent2 = new User { Email="support2@worksupport360.com", Name="Preethi S.", Role="agent", MobileNumber="9000000002", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Agent@123!"), EmailVerified=true };
    db.Users.AddRange(agent1, agent2);

    // Freelancers
    void AddFreelancer(string email, string name, string company, string role,
        int totalExp, int flExp, decimal rate, string currency, string bio,
        string[] skills, bool available = true)
    {
        var u = new User { Email=email, Name=name, Role="freelancer", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Test@123!"), EmailVerified=true, MobileNumber="90000" + name.GetHashCode().ToString().Replace("-","").Substring(0,5) };
        db.Users.Add(u);
        var parts = name.Trim().Split(' ');
        var fl = new Freelancer {
            UserId=u.Id, AliasName=$"{parts[0]} {parts[^1][0]}.", RealName=name,
            CurrentCompany=company, CurrentRole=role,
            TotalExp=totalExp, FreelanceExp=flExp,
            HourlyRate=rate, Currency=currency, Country="India", Timezone="IST (UTC+5:30)", Bio=bio,
            Rating=4.5m + (decimal)(new Random(email.GetHashCode()).NextDouble() * 0.4),
            ReviewCount=new Random(email.GetHashCode()).Next(5,50),
            TrustScore=70 + new Random(email.GetHashCode()).Next(0,28),
            Tier=2, IsAvailable=available, IsVerified=true,
            TotalEarned=new Random(email.GetHashCode()).Next(5000,50000),
            CompletedProjects=new Random(email.GetHashCode()).Next(5,30),
            ProfileViews=new Random(email.GetHashCode()).Next(100,1500),
            ResponseTimeMinutes=new Random(email.GetHashCode()).Next(15,60),
            Skills=skills.Select(s => new FreelancerSkill { Skill=s }).ToList(),
            Badges=new List<FreelancerBadge> { new(){Badge="ID Verified"} },
            Availability=new List<WeeklyAvailability> {
                new(){DayOfWeek="monday",IsAvailable=true,StartTime="18:00",EndTime="22:00"},
                new(){DayOfWeek="tuesday",IsAvailable=true,StartTime="18:00",EndTime="22:00"},
                new(){DayOfWeek="wednesday",IsAvailable=false},
                new(){DayOfWeek="thursday",IsAvailable=true,StartTime="18:00",EndTime="22:00"},
                new(){DayOfWeek="friday",IsAvailable=true,StartTime="18:00",EndTime="22:00"},
                new(){DayOfWeek="saturday",IsAvailable=available,StartTime="10:00",EndTime="18:00"},
                new(){DayOfWeek="sunday",IsAvailable=false},
            }
        };
        db.Freelancers.Add(fl);
    }

    AddFreelancer("rahul@example.com","Rahul Sharma","Infosys","Senior Software Engineer",8,3,35,"USD","Full-stack specialist. React + Node.js + AWS. Fintech & SaaS experience.",new[]{"React","Node.js","AWS","TypeScript","PostgreSQL","Docker"});
    AddFreelancer("priya@example.com","Priya Kumar","TCS","Lead Data Scientist",6,2,2800,"INR","ML pipeline expert. Python, pandas, scikit-learn, Power BI dashboards.",new[]{"Python","ML","SQL","Pandas","Power BI","TensorFlow"});
    AddFreelancer("arjun@example.com","Arjun Mehta","Wipro","DevOps Lead",5,2,28,"USD","Kubernetes-certified. AWS + Azure + CI/CD. Production incident specialist.",new[]{"Docker","Kubernetes","AWS","Terraform","Jenkins","Linux"});
    AddFreelancer("sneha@example.com","Sneha Reddy","HCL","Senior .NET Developer",7,3,30,"USD","C# .NET Core microservices. Azure cloud. Enterprise API specialist.",new[]{".NET","C#","Azure","SQL Server","Microservices"});
    AddFreelancer("vikram@example.com","Vikram Singh","Cognizant","Java Architect",10,4,40,"USD","Java Spring Boot microservices. Kafka, Redis, Kubernetes. 10 years enterprise.",new[]{"Java","Spring Boot","Kafka","Kubernetes","Redis","MySQL"});
    AddFreelancer("deepa@example.com","Deepa Nair","Capgemini","QA Lead",6,2,22,"USD","Selenium, Cypress, Playwright. API testing. CI/CD integration.",new[]{"Selenium","Cypress","Playwright","API Testing","JIRA","Jenkins"},false);

    // Clients
    void AddClient(string email, string name, string company, string industry, string country, string plan = "payg")
    {
        var u = new User { Email=email, Name=name, Role="client", PasswordHash=BCrypt.Net.BCrypt.HashPassword("Test@123!"), EmailVerified=true, MobileNumber="80000" + company.GetHashCode().ToString().Replace("-","").Substring(0,5) };
        db.Users.Add(u);
        db.Clients.Add(new Client { UserId=u.Id, CompanyName=company, ContactName=name, Industry=industry, Country=country, Plan=plan, HoursIncluded=plan=="starter"?10:plan=="growth"?25:0 });
    }
    AddClient("john@abccorp.com","John Smith","ABC Corp","Fintech","USA","starter");
    AddClient("sarah@xyzltd.com","Sarah Chen","XYZ Ltd","E-commerce","Singapore","growth");
    AddClient("ravi@techstartup.in","Ravi Krishnan","TechStartup India","SaaS","India");

    await db.SaveChangesAsync();
    log.LogInformation("=== SEED COMPLETE === admin@worksupport360.com / Admin@123!");
}
