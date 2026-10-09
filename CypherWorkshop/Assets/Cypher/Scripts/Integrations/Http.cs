using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Cypher
{
    /// <summary>Small async HTTP helper (UnityWebRequest, main thread) for the Google and Notion APIs.</summary>
    public static class Http
    {
        public struct Response
        {
            public long Status;
            public string Text;
            public string Error;
            public bool Ok => Status >= 200 && Status < 300;
        }

        public static async Task<Response> Send(string method, string url, string bearerToken,
            string jsonBody = null, Dictionary<string, string> headers = null, int timeoutSeconds = 20)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (jsonBody != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                if (!string.IsNullOrEmpty(bearerToken)) req.SetRequestHeader("Authorization", "Bearer " + bearerToken);
                if (headers != null)
                    foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);
                req.timeout = timeoutSeconds;
                await req.SendWebRequest();
                return new Response { Status = req.responseCode, Text = req.downloadHandler.text, Error = req.error };
            }
        }

        public static async Task<Response> PostForm(string url, Dictionary<string, string> fields, int timeoutSeconds = 20)
        {
            using (var req = UnityWebRequest.Post(url, fields))
            {
                req.timeout = timeoutSeconds;
                await req.SendWebRequest();
                return new Response { Status = req.responseCode, Text = req.downloadHandler.text, Error = req.error };
            }
        }

        /// <summary>Logs a failed call without secrets (only status and the response body).</summary>
        public static void LogFailure(string what, Response r) =>
            Debug.LogWarning($"[Cypher] {what} failed ({r.Status} {r.Error}): {(r.Text ?? "").Substring(0, System.Math.Min(400, (r.Text ?? "").Length))}");
    }
}
