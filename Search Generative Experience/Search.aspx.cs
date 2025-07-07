using Newtonsoft.Json;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Services;
using System.Web.Services;
using System.Web.UI;

namespace Search_Generative_Experience
{
    public partial class Search : Page
    {
        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static string[] GetResultsAjax(string query)
        {
            return FetchSections(query).ToArray();
        }

        // Important: Must return string, NOT Task<string> (WebMethod limitation)
        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public static string GetSummaryAjax(string query)
        {
            return GetAiSummaryAsync(query, FetchSections(query)).GetAwaiter().GetResult();
        }

        private static List<string> FetchSections(string query)
        {
            var list = new List<string>();
            string connStr = ConfigurationManager.ConnectionStrings["aio"].ConnectionString;
            string sql = @"
                SELECT TOP 3 Title, Content
                FROM Sections
                WHERE Title LIKE @q OR Content LIKE @q
                ORDER BY Id";

            using (var conn = new SqlConnection(connStr))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@q", SqlDbType.NVarChar, 4000)
                { Value = "%" + query + "%" });

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string title = HttpUtility.HtmlEncode(reader["Title"].ToString());
                        string content = HttpUtility.HtmlEncode(reader["Content"].ToString());
                        list.Add($"<strong>{title}</strong>: {content}");
                    }
                }
            }

            return list;
        }

        private static async Task<string> GetAiSummaryAsync(string userQuery, List<string> sections)
        {
            string systemMessage = "أنت مساعد قانوني ذكي وخبير في القوانين اللبنانية.";
            string prompt = $"السؤال: {userQuery}\n\nالنصوص القانونية:\n" +
                            string.Join("\n\n", sections) +
                            "\n\nيرجى تقديم ملخص واضح باستخدام المعلومات أعلاه فقط.";

            var requestBody = new
            {
                model = "deepseek/deepseek-r1:free",
                messages = new[] {
                    new { role = "system", content = systemMessage },
                    new { role = "user",   content = prompt }
                }
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", "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 " +
                    "google one: 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" +
                    "lama: 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" +
                    "user .399: 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");

                var response = await client.PostAsJsonAsync("https://openrouter.ai/api/v1/chat/completions", requestBody);
                var json = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(json);

                if (result?.choices != null && result.choices.Count > 0)
                    return (string)result.choices[0].message.content;
                if (result?.error != null)
                    return $"خطأ من المزود: {result.error.message}";
                return "لا يوجد رد من الخادم.";
            }
        }
    }
}
