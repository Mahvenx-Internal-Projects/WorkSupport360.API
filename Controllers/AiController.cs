using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace WorkSupport360.API.Controllers;

[ApiController, Route("api/ai"), Authorize]
public class AiController(IConfiguration cfg) : ControllerBase
{
    [HttpPost("generate-jd")]
    public async Task<IActionResult> GenerateJD([FromBody] GenerateJDRequest req)
    {
        var apiKey = cfg["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            // Fallback template when no API key
            var template = GenerateTemplate(req);
            return Ok(new { jd = template, title = req.Prompt.Split(' ').Take(5).Aggregate((a, b) => a + " " + b) });
        }

        try
        {
            var prompt = $@"Generate a professional job description for this freelance/contract requirement:
""{req.Prompt}""

Context: Engagement type = {req.Type}, Skills = {string.Join(", ", req.Skills ?? [])}, Budget type = {req.BudgetType}

Format the JD as plain text with these sections:
Role Overview (2-3 sentences)

Key Responsibilities:
• [5-6 bullet points]

Required Skills:
• [4-5 bullet points]

Nice to Have:
• [2-3 optional skills]

About the Engagement:
[1-2 sentences about the work arrangement]

Keep it professional, concise and suitable for MNC engineers. Focus on what they will DO.";

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var payload = new {
                model = "claude-sonnet-4-20250514",
                max_tokens = 800,
                messages = new[] { new { role = "user", content = prompt } }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await http.PostAsync("https://api.anthropic.com/v1/messages", content);
            var body = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text").GetString() ?? "";

            // Extract title from prompt
            var words = req.Prompt.Split(' ');
            var title = string.Join(" ", words.Take(Math.Min(8, words.Length)));

            return Ok(new { jd = text, title });
        }
        catch (Exception ex)
        {
            var template = GenerateTemplate(req);
            return Ok(new { jd = template, title = req.Prompt.Split(' ').Take(5).Aggregate((a, b) => a + " " + b) });
        }
    }

    private static string GenerateTemplate(GenerateJDRequest req) => $@"Role: {req.Prompt}

Key Responsibilities:
• Deliver high-quality work aligned with project requirements
• Collaborate with the team and client stakeholders
• Provide regular updates and communicate blockers proactively
• Write clean, documented, and maintainable deliverables
• Participate in reviews and iterate based on feedback

Required Skills:
• {(req.Skills != null && req.Skills.Count > 0 ? string.Join("\n• ", req.Skills.Take(4)) : "Relevant technical expertise")}
• Strong communication skills (written & verbal)
• Ability to work independently and meet deadlines

Nice to Have:
• Domain knowledge in the client's industry
• Experience with similar projects

About the Engagement:
This is a {req.Type ?? "freelance"} engagement. The expert is expected to {(req.BudgetType == "hourly" ? "track and report hours worked" : "deliver agreed milestones")}. Fully remote-friendly.";
}

public record GenerateJDRequest(
    string Prompt,
    string? Type = "freelance",
    List<string>? Skills = null,
    string? BudgetType = "hourly"
);
