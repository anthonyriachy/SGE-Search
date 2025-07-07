using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Search_Generative_Experience
{
    public class SearchHandler : IHttpAsyncHandler
    {
        public bool IsReusable => false;

        public class SectionResult
        {
            public string Title { get; set; }
            public string Content { get; set; }
            public string FilePath { get; set; }
            public string Anchor { get; set; }
            // Full URL (with encoded anchor) ready for the <iframe>
            public string Url
            {
                get
                {
                    var path = FilePath.StartsWith("/") ? FilePath : "/" + FilePath;
                    var frag = HttpUtility.UrlEncode(Anchor ?? "");
                    return $"{path}#{frag}";
                }
            }
        }

        public void ProcessRequest(HttpContext context)
        {
            string action = context.Request["action"];
            string query = context.Request["query"];
            if (action == "results")
            {
                context.Response.ContentType = "application/json";
                var results = FetchSections(query);
                context.Response.Write(JsonConvert.SerializeObject(results));
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Write("{\"error\":\"Missing or unsupported action\"}");
            }
        }

        public IAsyncResult BeginProcessRequest(HttpContext context, AsyncCallback cb, object extra)
        {
            string action = context.Request["action"];
            string query = context.Request["query"];
            var tcs = new TaskCompletionSource<bool>();

            if (action == "summary")
            {
                ProcessSummaryAsync(query, context).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        context.Response.Write("{\"error\":\"Internal Server Error\"}");
                    tcs.SetResult(true);
                    cb?.Invoke(tcs.Task);
                });
            }
            else
            {
                ProcessRequest(context);
                tcs.SetResult(true);
                cb?.Invoke(tcs.Task);
            }

            return tcs.Task;
        }
        public void EndProcessRequest(IAsyncResult result) { }

        private List<SectionResult> FetchSections(string query)
        {
            var list = new List<SectionResult>();
            if (string.IsNullOrWhiteSpace(query)) return list;

            string allWords = FormatFullTextQuery(query);
            if (string.IsNullOrEmpty(allWords)) return list;

            string connStr = ConfigurationManager.ConnectionStrings["aio"].ConnectionString;
            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                string sql = @"
WITH Ranked AS (
    SELECT s.Id, 1000 + ft.RANK AS Rnk
    FROM dbo.Sections s
    INNER JOIN CONTAINSTABLE(dbo.Sections, Title, @exact) ft ON s.Id = ft.[KEY]
  UNION ALL
    SELECT s.Id,  500 + ft.RANK AS Rnk
    FROM dbo.Sections s
    INNER JOIN CONTAINSTABLE(dbo.Sections, Title, @all) ft ON s.Id = ft.[KEY]
  UNION ALL
    SELECT s.Id,  300 + ft.RANK AS Rnk
    FROM dbo.Sections s
    INNER JOIN CONTAINSTABLE(dbo.Sections, Content, @exact) ft ON s.Id = ft.[KEY]
  UNION ALL
    SELECT s.Id,      ft.RANK AS Rnk
    FROM dbo.Sections s
    INNER JOIN FREETEXTTABLE(dbo.Sections, (Title,Content), @natural) ft ON s.Id = ft.[KEY]
)
SELECT TOP 6
    s.Title,
    s.Content,
    s.FilePath,
    s.Anchor
FROM Ranked r
JOIN dbo.Sections s ON s.Id = r.Id
GROUP BY s.Id, s.Title, s.Content, s.FilePath, s.Anchor
ORDER BY MAX(r.Rnk) DESC;";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@exact", "\"" + query + "\"");
                    cmd.Parameters.AddWithValue("@all", allWords);
                    cmd.Parameters.AddWithValue("@natural", query);

                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string phys = rdr["FilePath"].ToString();
                            string rel = phys
                                .Replace(@"C:\ITR_solution\", "")
                                .Replace("\\", "/")
                                .TrimStart('/');
                            string web = "/" + rel;

                            list.Add(new SectionResult
                            {
                                Title = HttpUtility.HtmlEncode(rdr["Title"].ToString()),
                                Content = HttpUtility.HtmlEncode(rdr["Content"].ToString()),
                                FilePath = web,
                                Anchor = rdr["Anchor"].ToString()
                            });
                        }
                    }
                }
            }
            return list;
        }

        private string FormatFullTextQuery(string query)
        {
            var tokens = query.Trim()
                              .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!tokens.Any()) return null;
            return string.Join(" AND ",
                tokens.Select(t => "\"" + t.Replace("\"", "\"\"") + "\""));
        }

        private async Task ProcessSummaryAsync(string query, HttpContext context)
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.BufferOutput = false;
            try
            {
                var secs = FetchSections(query);
                int maxSec = 3, maxC = 3000, total = 0;
                var sb = new StringBuilder();
                sb.AppendLine("السؤال: " + query + "\n\nالنصوص القانونية:");

                foreach (var sec in secs)
                {
                    var block = $"**{sec.Title}**\n{sec.Content}";
                    if (total + block.Length > maxC) break;
                    sb.AppendLine(block).AppendLine();
                    total += block.Length;
                }
                sb.AppendLine("يرجى تقديم ملخص واضح باستخدام المعلومات أعلاه فقط.");

                var body = new
                {
                    model = "deepseek/deepseek-chat:free",
                    messages = new[] {
                      new { role="system", content="أنت مساعد قانوني ذكي وخبير في القوانين اللبنانية." },
                      new { role="user",   content=sb.ToString() }
                    },
                    stream = true
                };

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization =
                      new AuthenticationHeaderValue("Bearer", "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");

                    using (var req = new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://openrouter.ai/api/v1/chat/completions"))
                    {
                        req.Content = new StringContent(
                          JsonConvert.SerializeObject(body),
                          Encoding.UTF8, "application/json");

                        using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
                        {
                            resp.EnsureSuccessStatusCode();
                            using (var stream = await resp.Content.ReadAsStreamAsync())
                            using (var reader = new StreamReader(stream))
                            {
                                while (!reader.EndOfStream)
                                {
                                    var line = await reader.ReadLineAsync();
                                    if (!line.StartsWith("data: ")) continue;
                                    var data = line.Substring(6);
                                    if (data.Trim() == "[DONE]") break;
                                    dynamic d = JsonConvert.DeserializeObject(data);
                                    string chunk = d?.choices[0]?.delta?.content;
                                    if (!string.IsNullOrEmpty(chunk))
                                    {
                                        await context.Response.Output.WriteAsync(chunk);
                                        await context.Response.Output.FlushAsync();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await context.Response.Output.WriteAsync("Error: " + ex.Message);
                await context.Response.Output.FlushAsync();
            }
        }
    }
}
